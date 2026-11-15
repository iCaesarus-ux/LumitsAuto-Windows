// C# 5 / .NET Framework 4.8 version. Builds with %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace ShapeFinder
{
    /// <summary>One shape found in the scene.</summary>
    public sealed class DetectedShape
    {
        public DetectedShape()
        {
            Shape = "unknown";
            Reason = "";
            Contour = new Point[0];
            LiesOn = new List<int>();
        }

        public string Shape { get; set; }          // star, cross, circle, square, rectangle, triangle, heart, crescent
        public bool Hollow { get; set; }           // ring = hollow circle, outlined cross = hollow cross, ...
        public bool Front { get; set; }            // false = background (faint, or something sits on top of it)
        public string Reason { get; set; }
        public Point[] Contour { get; set; }
        public Rect Box { get; set; }
        public int Area { get; set; }              // pixels
        public Mat Mask { get; set; }              // CV_8UC1, 255 = pixels of this shape
        public int Id { get; set; }
        public List<int> LiesOn { get; set; }      // ids of shapes this one sits on top of

        // geometry used for naming and for similarity ranking
        public double Solidity { get; set; }       // area / convex-hull area
        public double RoundnessError { get; set; } // deviation from best-fit circle
        public double FilledShare { get; set; }    // share of the outline that is filled (1 = solid)
        public double RectFit { get; set; }        // area / min-area rectangle
        public double TriangleFit { get; set; }    // area / min-area enclosing triangle
        public int Notches { get; set; }           // dents in the outline

        public string Label
        {
            get { return (Hollow ? "hollow " : "filled ") + Shape; }
        }
    }

    /// <summary>
    /// Finds the shapes in a flat 2-D scene, decides which are in front, and names them by
    /// outline + filled/hollow. Colour and stripes are only used to separate shapes from each
    /// other, never in the result.
    /// </summary>
    public sealed class ShapeDetector
    {
        // ---- tuning (identical to the Python reference) ----
        const double ForegroundDiff = 8;     // Lab distance from background model that counts as "something"
        const int Clusters = 18;             // k-means colour clusters
        const double ClusterMerge = 12;      // clusters closer than this are the same colour
        const int MinPiece = 150, MinObject = 500, MaxBlendPiece = 1200;
        const double FrontContrast = 38;     // below this a shape is too faint/translucent to be in front
        const double HollowLimit = 0.85;     // filled share of the outline below this = hollow
        const double NotchDepth = 0.07;      // dent deeper than this * sqrt(area) counts as a notch

        readonly int W, H, N;
        float[] lab = new float[0];          // light-blurred image, Lab, N*3
        float[] bg = new float[0];           // background model, Lab, N*3
        bool[] fg = new bool[0];

        sealed class Obj
        {
            public bool[] Mask = new bool[0];
            public float[] Col = new float[3];
            public int Area;
            public Point[] Contour = new Point[0];
            public bool[] Fill = new bool[0], Hull = new bool[0], Near = new bool[0];
            public double Solidity, Hollow, CircErr, Tip, RectFit, Elong, TriFit, Contrast;
            public int Verts, Notches;
            public bool Clipped;
        }

        ShapeDetector(int w, int h)
        {
            W = w; H = h; N = w * h;
        }

        public static List<DetectedShape> Detect(Mat bgrImage)
        {
            var d = new ShapeDetector(bgrImage.Cols, bgrImage.Rows);
            return d.Run(bgrImage);
        }

        List<DetectedShape> Run(Mat img)
        {
            // 1. background model: smooth gradient, quadratic surface per Lab channel, fitted robustly
            using (var h1 = new Mat())
            using (var heavy = new Mat())
            {
                Cv2.MedianBlur(img, h1, 5);
                Cv2.MedianBlur(h1, heavy, 5);                 // wipes thin lines and star dots
                bg = FitBackground(ToLab(heavy));
            }

            // 2. foreground = anything that differs from the background
            using (var light = new Mat())
            {
                Cv2.MedianBlur(img, light, 3);                // keeps stripes crisp
                lab = ToLab(light);
            }
            fg = new bool[N];
            for (int i = 0; i < N; i++) fg[i] = Dist(lab, i, bg, i) > ForegroundDiff;

            // 3. split foreground by colour: k-means, then merge near-identical clusters
            int[] lmap;
            float[] cent;
            ColourGroups(out lmap, out cent);

            // 4. connected pieces per colour group
            var pieces = new List<Obj>();
            foreach (int g in lmap.Where(v => v >= 0).Distinct().OrderBy(v => v))
            {
                var m = new bool[N];
                for (int i = 0; i < N; i++) m[i] = lmap[i] == g;
                m = Morph(m, MorphTypes.Close, RectK(1, 7));   // join horizontal stripes
                m = Morph(m, MorphTypes.Open, Ell(3));         // drop specks and 1-px lines
                m = Morph(m, MorphTypes.Close, Ell(7));        // heal lines cutting through a shape
                foreach (var pm in Components(m, PixelConnectivity.Connectivity8))
                {
                    int area = Count(pm);
                    if (area < MinPiece) continue;
                    var col = MeanOver(lab, pm, fg) ?? new float[] { cent[g * 3], cent[g * 3 + 1], cent[g * 3 + 2] };
                    pieces.Add(new Obj { Mask = pm, Col = col, Area = area });
                }
            }

            // 5. absorb fragments: fainter versions of a neighbour (thin stripes, anti-aliasing) and
            //    blends of two neighbouring colours (edges between shapes)
            AbsorbFragments(pieces);

            var objs = pieces.Where(ob => ob != null && ob.Area >= MinObject).ToList();
            foreach (var o in objs) { o.Mask = Tidy(o.Mask); o.Area = Count(o.Mask); }

            // 6. join pieces that are really one shape:
            //    - a light and a dark version of the same hue that overlap (interleaved stripes)
            //    - the same surface seen through a hole (e.g. a panel showing inside a ring)
            bool again = true;
            while (again)
            {
                again = false;
                for (int i = 0; i < objs.Count && !again; i++)
                {
                    for (int j = i + 1; j < objs.Count; j++)
                    {
                        Obj a = objs[i], b = objs[j];
                        if (CountBoth(a.Mask, b.Mask) > 0.3 * Math.Min(a.Area, b.Area) && SameHue(a.Col, b.Col))
                            a.Mask = Tidy(Or(a.Mask, b.Mask));
                        else if (Dist(a.Col, 0, b.Col, 0) < ClusterMerge && (BoxInside(a.Mask, b.Mask) || BoxInside(b.Mask, a.Mask)))
                            a.Mask = Or(a.Mask, b.Mask);
                        else
                            continue;
                        a.Area = Count(a.Mask);
                        objs.RemoveAt(j);
                        again = true;
                        break;
                    }
                }
            }

            // 6a. reclaim nearby pixels of each shape's own colour that no shape owns
            //     (lost to stripe blends or cut off by crossing lines)
            var assigned = new bool[N];
            foreach (var o in objs) OrInto(assigned, o.Mask);
            foreach (var o in objs.OrderBy(t => t.Area).ToList())
            {
                var own = OwnColour(o, AndNot(assigned, o.Mask));
                if (own != null)
                {
                    var zone = Morph(o.Mask, MorphTypes.Close, Ell(11));
                    var add = new bool[N];
                    for (int i = 0; i < N; i++) add[i] = zone[i] && own[i] && !assigned[i];
                    o.Mask = Tidy(Or(o.Mask, add));
                    o.Area = Count(o.Mask);
                }
                OrInto(assigned, o.Mask);
            }

            // 6c. rebuild strong, solid shapes from raw pixels of their own colour:
            //     crossing lines, other shapes and background drop out
            foreach (var o in objs)
            {
                if (ContrastOf(o) < FrontContrast) continue;                                  // faint shapes: leave
                if (Count(Morph(o.Mask, MorphTypes.Open, Ell(5))) <= 0.6 * Count(o.Mask)) continue;   // thin strokes
                var others = new bool[N];
                foreach (var p in objs) if (p != o) OrInto(others, p.Mask);
                o.Mask = Refine(o, others);
                o.Area = Count(o.Mask);
            }

            // 6b. clean masks: drop thin strips, cut off attached line spikes, smooth jagged stripe ends
            var kept = new List<Obj>();
            foreach (var o in objs)
            {
                var bb = Bounds(o.Mask);
                if (Math.Min(bb.Width, bb.Height) < 8) continue;
                int sameColour = 0;
                for (int i = 0; i < N; i++) if (o.Mask[i] && Dist(lab, i, o.Col, 0) < 22) sameColour++;
                bool striped = sameColour < 0.72 * o.Area;                    // gaps between stripes
                o.Mask = Clean(o.Mask, striped ? 3.0 : 1.5);
                o.Area = Count(o.Mask);
                if (o.Area >= MinObject) kept.Add(o);
            }
            objs = kept;

            // 7. geometry
            foreach (var o in objs) Outline(o);
            foreach (var o in objs) Measure(o, objs);

            // 8. front or background?  background = faint/translucent, or another shape sits on top of it
            var result = new List<DetectedShape>();
            for (int id = 0; id < objs.Count; id++)
            {
                var a = objs[id];
                int coveredBy = 0;
                var liesOn = new List<int>();
                for (int k = 0; k < objs.Count; k++)
                {
                    if (k == id) continue;
                    if (SitsOn(objs[k], a)) coveredBy++;
                    if (SitsOn(a, objs[k])) liesOn.Add(k);
                }
                bool front = a.Contrast >= FrontContrast && coveredBy == 0;
                string reason = front ? "front"
                    : a.Contrast < FrontContrast ? "background: low contrast"
                    : string.Format("background: {0} shape(s) on top", coveredBy);

                // 9. name: outline shape + filled/hollow
                result.Add(new DetectedShape
                {
                    Shape = ShapeOf(a),
                    Hollow = a.Hollow < HollowLimit,
                    Front = front,
                    Reason = reason,
                    Contour = a.Contour,
                    Box = Cv2.BoundingRect(a.Contour),
                    Area = a.Area,
                    Mask = ToMat(a.Mask),
                    Id = id,
                    LiesOn = liesOn,
                    Solidity = a.Solidity,
                    RoundnessError = a.CircErr,
                    FilledShare = a.Hollow,
                    RectFit = a.RectFit,
                    TriangleFit = a.TriFit,
                    Notches = a.Notches
                });
            }
            return result;
        }

        // ------------------------------------------------------------------ steps

        float[] FitBackground(float[] p)
        {
            // start from pixels close to the median colour, then refit 5x keeping pixels near the surface
            var med = new float[3];
            for (int c = 0; c < 3; c++)
            {
                var ch = new float[N];
                for (int i = 0; i < N; i++) ch[i] = p[i * 3 + c];
                Array.Sort(ch);
                med[c] = N % 2 == 1 ? ch[N / 2] : (ch[N / 2 - 1] + ch[N / 2]) / 2f;
            }
            var inl = new bool[N];
            for (int i = 0; i < N; i++)
            {
                double dl = p[i * 3] - med[0], da = p[i * 3 + 1] - med[1], db = p[i * 3 + 2] - med[2];
                inl[i] = Math.Sqrt(dl * dl + da * da + db * db) < 12;
            }
            var model = new float[N * 3];
            var f = new double[6];
            for (int it = 0; it < 5; it++)
            {
                var ata = new double[6, 6];
                var atb = new double[6, 3];
                for (int i = 0; i < N; i++)
                {
                    if (!inl[i]) continue;
                    Basis(i, f);
                    for (int r = 0; r < 6; r++)
                    {
                        for (int c = 0; c < 6; c++) ata[r, c] += f[r] * f[c];
                        for (int c = 0; c < 3; c++) atb[r, c] += f[r] * p[i * 3 + c];
                    }
                }
                var coef = Solve(ata, atb);
                for (int i = 0; i < N; i++)
                {
                    Basis(i, f);
                    double d2 = 0;
                    for (int c = 0; c < 3; c++)
                    {
                        double v = 0;
                        for (int r = 0; r < 6; r++) v += f[r] * coef[r, c];
                        model[i * 3 + c] = (float)v;
                        double dd = p[i * 3 + c] - v;
                        d2 += dd * dd;
                    }
                    inl[i] = Math.Sqrt(d2) < 6;
                }
            }
            return model;
        }

        void Basis(int i, double[] f)
        {
            double x = (i % W) / (double)W, y = (i / W) / (double)H;
            f[0] = 1; f[1] = x; f[2] = y; f[3] = x * x; f[4] = x * y; f[5] = y * y;
        }

        void ColourGroups(out int[] lmap, out float[] cent)
        {
            var idx = Enumerable.Range(0, N).Where(ix => fg[ix]).ToArray();
            var flat = new float[idx.Length * 3];
            for (int k = 0; k < idx.Length; k++)
                for (int c = 0; c < 3; c++) flat[k * 3 + c] = lab[idx[k] * 3 + c];

            var lbl = new int[idx.Length];
            cent = new float[Clusters * 3];
            using (var samples = new Mat(idx.Length, 3, MatType.CV_32FC1))
            using (var labels = new Mat())
            using (var centers = new Mat())
            {
                Marshal.Copy(flat, 0, samples.Data, flat.Length);
                Cv2.Kmeans(samples, Clusters, labels,
                    new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.Count, 60, 0.3),
                    5, KMeansFlags.PpCenters, centers);
                Marshal.Copy(Continuous(labels).Data, lbl, 0, lbl.Length);
                Marshal.Copy(Continuous(centers).Data, cent, 0, cent.Length);
            }

            var parent = Enumerable.Range(0, Clusters).ToArray();
            for (int i = 0; i < Clusters; i++)
                for (int j = i + 1; j < Clusters; j++)
                    if (Dist(cent, i, cent, j) < ClusterMerge) parent[Find(parent, j)] = Find(parent, i);

            lmap = Enumerable.Repeat(-1, N).ToArray();
            for (int k = 0; k < idx.Length; k++) lmap[idx[k]] = Find(parent, lbl[k]);
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i) i = parent[i];
            return i;
        }

        void AbsorbFragments(List<Obj> pieces)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var order = Enumerable.Range(0, pieces.Count).OrderBy(ix => pieces[ix] == null ? 0 : pieces[ix].Area).ToList();
                foreach (int i in order)
                {
                    var s = pieces[i];
                    if (s == null) continue;

                    var near = Morph(s.Mask, MorphTypes.Dilate, Ell(5));
                    var nbrs = new List<int>();
                    for (int j = 0; j < pieces.Count; j++)
                        if (j != i && pieces[j] != null && Overlaps(near, pieces[j].Mask)) nbrs.Add(j);
                    var sBg = MeanOver(bg, s.Mask, null);

                    // (a) fainter version of a neighbour: same direction away from the background,
                    //     35-92% as strong, not much bigger
                    var ds = Sub(s.Col, sBg);
                    double dsLen = Len(ds), bestAng = double.MaxValue;
                    int target = -1;
                    if (dsLen > 5)
                    {
                        foreach (int j in nbrs)
                        {
                            var n = pieces[j];
                            var dn = Sub(n.Col, MeanOver(bg, n.Mask, null));
                            double dnLen = Len(dn);
                            double cos = Dot(ds, dn) / (dsLen * dnLen + 1e-6);
                            double ang = Math.Acos(Clamp(cos, -1, 1)) * 180 / Math.PI;
                            if (ang < 20 && dsLen > 0.35 * dnLen && dsLen < 0.92 * dnLen && s.Area <= 1.5 * n.Area && ang < bestAng)
                            {
                                bestAng = ang;
                                target = j;
                            }
                        }
                    }

                    // (b) blend of two neighbouring colours (anti-aliased edge between two shapes)
                    if (target < 0 && s.Area <= MaxBlendPiece)
                    {
                        var candIdx = new List<int>(nbrs);
                        var candCol = nbrs.Select(ix => pieces[ix].Col).ToList();
                        candIdx.Add(-1);
                        candCol.Add(sBg);
                        double bestRes = double.MaxValue;
                        for (int a = 0; a < candIdx.Count; a++)
                        {
                            for (int b = a + 1; b < candIdx.Count; b++)
                            {
                                float[] ca = candCol[a], cb = candCol[b];
                                var v = Sub(cb, ca);
                                double t = Dot(Sub(s.Col, ca), v) / Math.Max(Dot(v, v), 1e-6), res = 0;
                                for (int c = 0; c < 3; c++)
                                {
                                    double e = s.Col[c] - (ca[c] + t * v[c]);
                                    res += e * e;
                                }
                                res = Math.Sqrt(res);
                                var owners = new[] { candIdx[a], candIdx[b] }.Where(ix => ix >= 0).ToList();
                                if (t > 0.1 && t < 0.9 && res < 10 && owners.Count > 0 && res < bestRes)
                                {
                                    bestRes = res;
                                    target = owners.OrderBy(ix => pieces[ix].Area).First();   // the shape on top
                                }
                            }
                        }
                    }

                    if (target >= 0)
                    {
                        var tgt = pieces[target];
                        tgt.Mask = Or(tgt.Mask, s.Mask);
                        tgt.Area = Count(tgt.Mask);
                        pieces[i] = null;
                        changed = true;
                    }
                }
            }
        }

        /// pixels that have this shape's colour, or a blend of it with the local background (>= 45% of it);
        /// null if the shape is too close to its surroundings to judge
        bool[] OwnColour(Obj o, bool[] others)
        {
            var outer = Morph(o.Mask, MorphTypes.Dilate, Ell(9));
            var inner = Morph(o.Mask, MorphTypes.Dilate, Ell(3));
            var ring = new List<int>();
            for (int i = 0; i < N; i++) if (outer[i] && !inner[i] && !others[i]) ring.Add(i);
            var local = ring.Count > 20 ? MedianLab(ring) : MeanOver(bg, o.Mask, null);
            var d = Sub(o.Col, local);
            double dd = Dot(d, d);
            if (dd < 400) return null;
            double maxR = Math.Max(12, 0.35 * Math.Sqrt(dd));
            var own = new bool[N];
            for (int i = 0; i < N; i++)
            {
                double p0 = lab[i * 3] - local[0], p1 = lab[i * 3 + 1] - local[1], p2 = lab[i * 3 + 2] - local[2];
                double t = (p0 * d[0] + p1 * d[1] + p2 * d[2]) / dd;
                if (t < 0.45) continue;
                double e0 = p0 - t * d[0], e1 = p1 - t * d[1], e2 = p2 - t * d[2];
                own[i] = Math.Sqrt(e0 * e0 + e1 * e1 + e2 * e2) < maxR;
            }
            return own;
        }

        bool[] Refine(Obj o, bool[] othersAll)
        {
            var others = AndNot(othersAll, o.Mask);          // never exclude this shape's own pixels
            var zoneD = Morph(o.Mask, MorphTypes.Dilate, Ell(15));
            var own = OwnColour(o, others);
            if (own == null) return o.Mask;                  // too weak to rebuild reliably

            var m = new bool[N];
            for (int i = 0; i < N; i++) m[i] = zoneD[i] && !others[i] && own[i];
            m = Morph(m, MorphTypes.Close, RectK(1, 9));    // stripes
            m = Morph(m, MorphTypes.Close, Ell(5));         // cuts from crossing lines
            m = FillSmallHoles(m, 40);
            m = KeepLargeComponents(m, 0.15);
            var nearM = Morph(m, MorphTypes.Dilate, Ell(9));
            for (int i = 0; i < N; i++) if (o.Mask[i] && nearM[i]) m[i] = true;   // keep original pixels next to the rebuilt shape

            int a0 = Count(o.Mask), a1 = Count(m);
            bool plausible = a1 > 0.85 * a0 && a1 < 1.6 * a0 && CountBoth(m, o.Mask) >= 0.88 * a0;
            return plausible ? m : o.Mask;
        }

        bool[] Clean(bool[] m, double sigma)
        {
            var orig = m;
            var core = Morph(m, MorphTypes.Open, Ell(5));
            if (Count(core) <= 0.6 * Count(m)) return m;     // thin-stroke outline (frame, outlined heart): leave as is
            var keep = Morph(core, MorphTypes.Dilate, Ell(5));
            var r = new bool[N];
            for (int i = 0; i < N; i++) r[i] = m[i] && keep[i];   // cut off attached line spikes
            r = KeepLargeComponents(r, 0.1);                       // drop small leftover islands
            var bridged = Morph(r, MorphTypes.Close, Ell(9));
            for (int i = 0; i < N; i++) if (bridged[i] && orig[i]) r[i] = true;   // re-bridge small gaps
            return Smooth(r, sigma);                               // smooth jagged stripe ends
        }

        void Outline(Obj o)
        {
            Point[][] cs;
            HierarchyIndex[] hierarchy;
            using (var m = ToMat(o.Mask))
            {
                Cv2.FindContours(m, out cs, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
            }
            o.Contour = cs.OrderByDescending(contour => Cv2.ContourArea(contour)).First();

            using (var fill = new Mat(H, W, MatType.CV_8UC1, Scalar.All(0)))
            {
                Cv2.DrawContours(fill, new[] { o.Contour }, -1, Scalar.All(255), -1);
                o.Fill = ToBool(fill);
            }
            using (var hull = new Mat(H, W, MatType.CV_8UC1, Scalar.All(0)))
            {
                Cv2.FillConvexPoly(hull, Cv2.ConvexHull(o.Contour), Scalar.All(255));
                o.Hull = ToBool(hull);
            }
            o.Near = Morph(o.Mask, MorphTypes.Dilate, Ell(5));
        }

        void Measure(Obj o, List<Obj> all)
        {
            var c = o.Contour;
            double area = Cv2.ContourArea(c), per = Cv2.ArcLength(c, true);
            var edge = c.Select(pt => OnBorder(pt)).ToArray();

            // notches = dents in the outline (5 = star, 4 = cross, 1 = heart/crescent)
            o.Notches = 0;
            try
            {
                foreach (var d in Cv2.ConvexityDefects(c, Cv2.ConvexHullIndices(c)))
                    if (d.Item3 / 256.0 > NotchDepth * Math.Sqrt(area) && !edge[d.Item2]) o.Notches++;
            }
            catch (Exception)
            {
                // degenerate contour: no notches
            }

            // hollow share. Shapes lying on top of this one (mostly inside its outline) don't count as holes;
            // a bigger shape showing through a hole from behind does.
            var onTop = all.Where(ob => ob != o && CountBoth(ob.Mask, o.Fill) > 0.5 * ob.Area).ToList();
            int filled = 0, covered = 0;
            for (int i = 0; i < N; i++)
            {
                if (o.Fill[i]) filled++;
                if (o.Mask[i]) { covered++; continue; }
                if (o.Fill[i])
                {
                    foreach (var p in onTop)
                    {
                        if (p.Mask[i]) { covered++; break; }
                    }
                }
            }
            o.Hollow = covered / (double)Math.Max(filled, 1);

            // circle fit on outline points that aren't on the image border
            var inner = c.Where((pt, ix) => !edge[ix]).ToArray();
            if (inner.Length <= 10) inner = c;
            var ata = new double[3, 3];
            var atb = new double[3, 1];
            foreach (var p in inner)
            {
                double[] row = { 2.0 * p.X, 2.0 * p.Y, 1 };
                double rhs = (double)p.X * p.X + (double)p.Y * p.Y;
                for (int r = 0; r < 3; r++)
                {
                    for (int k = 0; k < 3; k++) ata[r, k] += row[r] * row[k];
                    atb[r, 0] += row[r] * rhs;
                }
            }
            var sol = Solve(ata, atb);
            double cx = sol[0, 0], cy = sol[1, 0], rad = Math.Sqrt(Math.Max(sol[2, 0] + cx * cx + cy * cy, 0));
            double se = 0;
            foreach (var p in inner)
            {
                double e = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - rad;
                se += e * e;
            }
            o.CircErr = Math.Sqrt(se / inner.Length) / Math.Max(rad, 1);

            // angle at the outer corners (star tips ~50 deg, cross corners ~90 deg)
            var ap = Cv2.ApproxPolyDP(c, 0.015 * per, true);
            var angs = new List<double>();
            foreach (int k in Cv2.ConvexHullIndices(ap))
            {
                Point p = ap[(k - 1 + ap.Length) % ap.Length], q = ap[k], r = ap[(k + 1) % ap.Length];
                if (OnBorder(q)) continue;
                double v1x = p.X - q.X, v1y = p.Y - q.Y, v2x = r.X - q.X, v2y = r.Y - q.Y;
                double cos = (v1x * v2x + v1y * v2y) / (Math.Sqrt(v1x * v1x + v1y * v1y) * Math.Sqrt(v2x * v2x + v2y * v2y) + 1e-6);
                angs.Add(Math.Acos(Clamp(cos, -1, 1)) * 180 / Math.PI);
            }
            o.Tip = Median(angs) ?? 180;

            var rr = Cv2.MinAreaRect(c);
            double rw = rr.Size.Width, rh = rr.Size.Height;
            o.RectFit = area / Math.Max(rw * rh, 1);
            o.Elong = Math.Max(rw, rh) / Math.Max(Math.Min(rw, rh), 1);
            Point2f[] triangle;
            o.TriFit = area / Math.Max(Cv2.MinEnclosingTriangle(c, out triangle), 1);
            o.Solidity = area / Math.Max(Cv2.ContourArea(Cv2.ConvexHull(c)), 1);
            o.Verts = Cv2.ApproxPolyDP(c, 0.04 * per, true).Length;
            o.Clipped = edge.Count(flag => flag) / (double)edge.Length > 0.03;
            o.Contrast = ContrastOf(o);
        }

        double ContrastOf(Obj o)
        {
            return Len(Sub(o.Col, MeanOver(bg, o.Mask, null)));
        }

        /// b lies on top of a: b touches a and is mostly inside a's convex hull
        static bool SitsOn(Obj b, Obj a)
        {
            if (!Overlaps(a.Near, b.Mask)) return false;
            return CountBoth(b.Mask, a.Hull) > 0.5 * b.Area;
        }

        static string ShapeOf(Obj o)
        {
            if (o.Notches >= 5 || (o.Clipped && o.Notches >= 2 && o.Tip < 65)) return "star";
            if (o.Notches == 4 || (o.Clipped && o.Notches >= 2 && o.Tip >= 65)) return "cross";
            if (o.Solidity > 0.9 && o.CircErr < 0.085) return "circle";
            if (o.Verts == 4 && o.RectFit > 0.85) return o.Elong > 1.35 ? "rectangle" : "square";
            if (o.Solidity > 0.85 && o.TriFit > 0.8) return "triangle";
            if (o.Notches == 1 || o.Notches == 2) return o.Solidity < 0.75 ? "crescent" : "heart";
            if (o.Verts == 4 && o.RectFit > 0.75) return o.Elong > 1.35 ? "rectangle" : "square";
            return "unknown";
        }

        // ------------------------------------------------------------------ helpers

        bool OnBorder(Point p)
        {
            return p.X <= 1 || p.Y <= 1 || p.X >= W - 2 || p.Y >= H - 2;
        }

        static float[] ToLab(Mat bgr)
        {
            using (var l = new Mat())
            {
                Cv2.CvtColor(bgr, l, ColorConversionCodes.BGR2Lab);
                var px = new byte[l.Rows * l.Cols * 3];
                Marshal.Copy(Continuous(l).Data, px, 0, px.Length);
                var f = new float[px.Length];
                for (int i = 0; i < px.Length; i++) f[i] = px[i];
                return f;
            }
        }

        float[] MedianLab(List<int> pixels)
        {
            var res = new float[3];
            var v = new float[pixels.Count];
            for (int c = 0; c < 3; c++)
            {
                for (int k = 0; k < pixels.Count; k++) v[k] = lab[pixels[k] * 3 + c];
                Array.Sort(v);
                int n = v.Length;
                res[c] = n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2f;
            }
            return res;
        }

        static double Dist(float[] a, int i, float[] b, int j)
        {
            double s = 0;
            for (int c = 0; c < 3; c++)
            {
                double d = a[i * 3 + c] - b[j * 3 + c];
                s += d * d;
            }
            return Math.Sqrt(s);
        }

        static double[] Sub(float[] a, float[] b)
        {
            return new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        static double Len(double[] a)
        {
            return Math.Sqrt(Dot(a, a));
        }

        static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// mean Lab colour over mask (and optional second mask); null if empty
        static float[] MeanOver(float[] img3, bool[] mask, bool[] and)
        {
            double s0 = 0, s1 = 0, s2 = 0;
            int n = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] && (and == null || and[i]))
                {
                    s0 += img3[i * 3]; s1 += img3[i * 3 + 1]; s2 += img3[i * 3 + 2]; n++;
                }
            }
            return n == 0 ? null : new float[] { (float)(s0 / n), (float)(s1 / n), (float)(s2 / n) };
        }

        /// same colour family (lighter/darker allowed); greys and near-blacks have no hue
        static bool SameHue(float[] c1, float[] c2)
        {
            double a1 = c1[1] - 128, b1 = c1[2] - 128, a2 = c2[1] - 128, b2 = c2[2] - 128;
            if (Math.Sqrt(a1 * a1 + b1 * b1) < 15 || Math.Sqrt(a2 * a2 + b2 * b2) < 15) return false;
            double d = Math.Abs((Math.Atan2(b1, a1) - Math.Atan2(b2, a2)) * 180 / Math.PI) % 360;
            return Math.Min(d, 360 - d) < 25;
        }

        /// a's bounding box lies inside b's (2 px tolerance)
        bool BoxInside(bool[] a, bool[] b)
        {
            Rect ra = Bounds(a), rb = Bounds(b);
            return ra.X >= rb.X - 2 && ra.Y >= rb.Y - 2
                && ra.X + ra.Width <= rb.X + rb.Width + 2 && ra.Y + ra.Height <= rb.Y + rb.Height + 2;
        }

        Rect Bounds(bool[] m)
        {
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int i = 0; i < N; i++)
            {
                if (!m[i]) continue;
                int x = i % W, y = i / W;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            return x1 < 0 ? new Rect() : new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        bool[] Tidy(bool[] m)
        {
            return Morph(Morph(m, MorphTypes.Close, Ell(5)), MorphTypes.Close, RectK(1, 9));
        }

        static Mat Ell(int n)
        {
            return Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(n, n));
        }

        static Mat RectK(int w, int h)
        {
            return Cv2.GetStructuringElement(MorphShapes.Rect, new Size(w, h));
        }

        bool[] Morph(bool[] m, MorphTypes op, Mat kernel)
        {
            using (kernel)
            using (var src = ToMat(m))
            using (var dst = new Mat())
            {
                Cv2.MorphologyEx(src, dst, op, kernel);
                return ToBool(dst);
            }
        }

        /// Gaussian-smooth a mask's outline and threshold at half
        bool[] Smooth(bool[] m, double sigma)
        {
            var f = new float[N];
            for (int i = 0; i < N; i++) f[i] = m[i] ? 1f : 0f;
            var g = new float[N];
            using (var src = new Mat(H, W, MatType.CV_32FC1))
            using (var dst = new Mat())
            {
                Marshal.Copy(f, 0, src.Data, N);
                Cv2.GaussianBlur(src, dst, new Size(0, 0), sigma);
                Marshal.Copy(Continuous(dst).Data, g, 0, N);
            }
            var r = new bool[N];
            for (int i = 0; i < N; i++) r[i] = g[i] > 0.5f;
            return r;
        }

        List<bool[]> Components(bool[] m, PixelConnectivity conn)
        {
            var l = new int[N];
            int n;
            using (var src = ToMat(m))
            using (var labels = new Mat())
            {
                n = Cv2.ConnectedComponents(src, labels, conn);
                Marshal.Copy(Continuous(labels).Data, l, 0, N);
            }
            var res = new List<bool[]>();
            for (int k = 1; k < n; k++) res.Add(new bool[N]);
            for (int i = 0; i < N; i++) if (l[i] > 0) res[l[i] - 1][i] = true;
            return res;
        }

        /// keep components at least `share` of the largest one
        bool[] KeepLargeComponents(bool[] m, double share)
        {
            var comps = Components(m, PixelConnectivity.Connectivity8);
            if (comps.Count <= 1) return m;
            var areas = comps.Select(comp => Count(comp)).ToList();
            int big = areas.Max();
            var r = new bool[N];
            for (int k = 0; k < comps.Count; k++) if (areas[k] >= share * big) OrInto(r, comps[k]);
            return r;
        }

        /// fill holes (4-connected background regions) no larger than maxArea
        bool[] FillSmallHoles(bool[] m, int maxArea)
        {
            var inv = new bool[N];
            for (int i = 0; i < N; i++) inv[i] = !m[i];
            var r = (bool[])m.Clone();
            foreach (var hole in Components(inv, PixelConnectivity.Connectivity4))
                if (Count(hole) <= maxArea) OrInto(r, hole);
            return r;
        }

        Mat ToMat(bool[] m)
        {
            var bytes = new byte[N];
            for (int i = 0; i < N; i++) bytes[i] = m[i] ? (byte)255 : (byte)0;
            var mat = new Mat(H, W, MatType.CV_8UC1);
            Marshal.Copy(bytes, 0, mat.Data, N);
            return mat;
        }

        static bool[] ToBool(Mat mat)
        {
            var b = new byte[mat.Rows * mat.Cols];
            Marshal.Copy(Continuous(mat).Data, b, 0, b.Length);
            var m = new bool[b.Length];
            for (int i = 0; i < b.Length; i++) m[i] = b[i] != 0;
            return m;
        }

        static Mat Continuous(Mat m)
        {
            return m.IsContinuous() ? m : m.Clone();
        }

        static bool[] Or(bool[] a, bool[] b)
        {
            var r = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] || b[i];
            return r;
        }

        static void OrInto(bool[] acc, bool[] b)
        {
            for (int i = 0; i < acc.Length; i++) if (b[i]) acc[i] = true;
        }

        static bool[] AndNot(bool[] a, bool[] b)
        {
            var r = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] && !b[i];
            return r;
        }

        static int Count(bool[] m)
        {
            int n = 0;
            foreach (var v in m) if (v) n++;
            return n;
        }

        static int CountBoth(bool[] a, bool[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] && b[i]) n++;
            return n;
        }

        static bool Overlaps(bool[] a, bool[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] && b[i]) return true;
            return false;
        }

        static double? Median(List<double> v)
        {
            if (v.Count == 0) return null;
            var s = v.OrderBy(val => val).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        /// Gaussian elimination with partial pivoting: solves A X = B
        static double[,] Solve(double[,] a, double[,] b)
        {
            int n = a.GetLength(0), m = b.GetLength(1);
            a = (double[,])a.Clone();
            b = (double[,])b.Clone();
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
                for (int k = 0; k < n; k++) { double t = a[col, k]; a[col, k] = a[piv, k]; a[piv, k] = t; }
                for (int k = 0; k < m; k++) { double t = b[col, k]; b[col, k] = b[piv, k]; b[piv, k] = t; }
                double d = a[col, col];
                if (Math.Abs(d) < 1e-12) continue;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r, col] / d;
                    if (f == 0) continue;
                    for (int k = col; k < n; k++) a[r, k] -= f * a[col, k];
                    for (int k = 0; k < m; k++) b[r, k] -= f * b[col, k];
                }
            }
            var x = new double[n, m];
            for (int r = 0; r < n; r++)
                for (int k = 0; k < m; k++) x[r, k] = Math.Abs(a[r, r]) < 1e-12 ? 0 : b[r, k] / a[r, r];
            return x;
        }
    }
}
