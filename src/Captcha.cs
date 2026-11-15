using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AutoClicker
{
    /// <summary>
    /// The game's "tap the shape that matches" check. Finds the shape in the box, then the candidate whose outline
    /// is closest. Colour, fill, stripes and rotation are ignored. Coordinates are game-window pixels at a maximised
    /// 1920 × 1080 window and are scaled for any other size.
    /// </summary>
    public static class Captcha
    {
        /// <summary>The window width the coordinates below were measured at.</summary>
        const int MeasuredWidth = 1920;

        static readonly Rectangle Play = new Rectangle(602, 200, 720, 520);
        const int BoxW = 150, BoxH = 148;
        const int MinSide = 34, MaxSide = 130, MinArea = 350;
        const int Rays = 36;
        const int BlurRadius = 8, EdgeMin = 20, Grow = 2, MaxHole = 6000;

        /// <summary>How unlike the reference the best candidate may be; above this it gives up rather than guess.</summary>
        const double MaxDistance = 0.48;

        sealed class Blob
        {
            public int X0, Y0, X1, Y1, Area, SumX, SumY;
            public readonly List<int> Pixels = new List<int>();
            public int CentreX { get { return SumX / Math.Max(1, Area); } }
            public int CentreY { get { return SumY / Math.Max(1, Area); } }
        }

        sealed class Shape
        {
            public double[] Signature;
            public double Fill;
            public double Point;
            public int W, H;
        }

        static Rectangle S(Rectangle r, double k)
        {
            return new Rectangle((int)Math.Round(r.X * k), (int)Math.Round(r.Y * k),
                                 (int)Math.Round(r.Width * k), (int)Math.Round(r.Height * k));
        }

       /// <summary>
        /// A pixel bright and coloured enough to be a shape. Every background mass is darker than every real shape, so the
        /// masses drop out and stop welding the shapes drawn over them into one blob.
        /// </summary>
        static bool Ink(int r, int g, int b)
        {
            int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            return mx >= 190 && (mx - mn >= 50 || mx >= 225);
        }

        /// <summary>Whether the panel is up: three pixels of its top margin are plain light grey and the play area is dark.</summary>
        public static bool Showing(Bitmap shot)
        {
            if (shot == null) return false;
            double k = shot.Width / (double)MeasuredWidth;
            foreach (Point q in new[] { new Point(700, 231), new Point(960, 231), new Point(1220, 231) })
            {
                int x = (int)Math.Round(q.X * k), y = (int)Math.Round(q.Y * k);
                if (x >= shot.Width || y >= shot.Height) return false;
                Color c = shot.GetPixel(x, y);
                int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
                if (mx < 58 || mx > 100 || mx - mn > 14) return false;
            }
            Rectangle play = S(Play, k);
            if (play.Right >= shot.Width || play.Bottom >= shot.Height) return false;
            return shot.GetPixel(play.X + 8, play.Y + 90).GetBrightness() < 0.45;
        }

        /// <summary>The shape to tap, in game-window coordinates, or null if none could be told apart.</summary>
       public static Point? Solve(Bitmap shot) { string trace; Rectangle box; return Solve(shot, out trace, out box); }

        /// <summary>
        /// The shape to tap, in game-window coordinates, or null if none could be told apart. <paramref name="trace"/> says how
        /// far it got and <paramref name="box"/> is the box it found, both for the probe.
        /// </summary>
        public static Point? Solve(Bitmap shot, out string trace, out Rectangle box)
        {
            trace = "";
            box = Rectangle.Empty;
            double k = shot.Width / (double)MeasuredWidth;
            Rectangle play = S(Play, k);
            play.Intersect(new Rectangle(Point.Empty, shot.Size));
            if (play.Width < 200 || play.Height < 120) { trace = "the play area is too small"; return null; }

            int w = play.Width, h = play.Height;
            var px = new byte[w * h * 4];
            BitmapData data = shot.LockBits(play, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    Buffer.BlockCopy(row, 0, px, y * w * 4, row.Length);
                }
            }
            finally { shot.UnlockBits(data); }
            byte[] solid = Solid(px, w, h);

            box = FindBox(px, w, h, (int)Math.Round(BoxW * k), (int)Math.Round(BoxH * k));
            if (box.IsEmpty) { trace = "no box found"; return null; }

            // Clear the box's own white border: it would otherwise be the reference itself, or bridge to it.
            for (int y = Math.Max(0, box.Y - 4); y < Math.Min(h, box.Bottom + 4); y++)
                for (int x = Math.Max(0, box.X - 4); x < Math.Min(w, box.Right + 4); x++)
                    if (x < box.X + 14 || x > box.Right - 14 || y < box.Y + 14 || y > box.Bottom - 14)
                        solid[y * w + x] = 0;
            // The criss-cross lines also run through the box and out to its edges, dragging the outline with them. They are two
            // pixels wide, so one erosion of the box interior takes them and leaves the shape's own rim.
            var thick = new byte[w * h];
            for (int y = Math.Max(1, box.Y - 4); y < Math.Min(h - 1, box.Bottom + 4); y++)
                for (int x = Math.Max(1, box.X - 4); x < Math.Min(w - 1, box.Right + 4); x++)
                    thick[y * w + x] = solid[y * w + x];
            for (int y = Math.Max(1, box.Y - 4); y < Math.Min(h - 1, box.Bottom + 4); y++)
                for (int x = Math.Max(1, box.X - 4); x < Math.Min(w - 1, box.Right + 4); x++)
                    if (solid[y * w + x] != 0 &&
                        (thick[y * w + x - 1] == 0 || thick[y * w + x + 1] == 0 ||
                         thick[(y - 1) * w + x] == 0 || thick[(y + 1) * w + x] == 0))
                        solid[y * w + x] = 0;
            int bandPix = 0, innerPix = 0;
            for (int y = Math.Max(0, box.Y); y < Math.Min(h, box.Bottom); y++)
                for (int x = Math.Max(0, box.X); x < Math.Min(w, box.Right); x++)
                {
                    if (solid[y * w + x] == 0) continue;
                    if (x < box.X + 12 || x > box.Right - 12 || y < box.Y + 12 || y > box.Bottom - 12) bandPix++;
                    else innerPix++;
                }

            string why;
            Shape reference = DescribeLargest(solid, w, h, box, out why);
            if (reference == null)
            {
                trace = "box " + box.X + "," + box.Y + " band" + bandPix + " inner" + innerPix + " but no shape inside it (" + why + ")";
                return null;
            }

            Shape best = null;
            Point bestAt = Point.Empty;
            double bestDistance = double.MaxValue;
            int candidates = 0;
            var shown = new List<KeyValuePair<double, string>>();
            foreach (Blob b in Blobs(solid, w, h, box, (int)Math.Round(MinSide * k), (int)Math.Round(MaxSide * k), false))
            {
                Shape s = Describe(solid, w, b);
                if (s == null) continue;
                candidates++;
                double distance = SignatureDistance(reference.Signature, s.Signature)
                                + Math.Abs(reference.Fill - s.Fill) * 0.08
                                + Math.Abs(reference.Point - s.Point) * 0.6;
                shown.Add(new KeyValuePair<double, string>(distance,
                    "(" + (play.X + b.CentreX) + "," + (play.Y + b.CentreY) + " f" + s.Fill.ToString("0.00") + " p" + s.Point.ToString("0.00") + " d" + distance.ToString("0.000") + ")"));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = s;
                bestAt = new Point(play.X + b.CentreX, play.Y + b.CentreY);
            }
            trace = "box " + box.X + "," + box.Y + ", ref f" + reference.Fill.ToString("0.00") + " p" + reference.Point.ToString("0.00") +
                    " " + reference.W + "x" + reference.H + ", " + candidates + " candidates, best " +
                    (best == null ? "none" : bestDistance.ToString("0.000") + " at " + bestAt.X + "," + bestAt.Y);
            shown.Sort((p, q) => p.Key.CompareTo(q.Key));
            for (int i = 0; i < shown.Count && i < 12; i++) trace += " " + shown[i].Value;
            if (best == null || bestDistance > MaxDistance)
            {
                trace += " (over " + MaxDistance.ToString("0.00") + ")";
                return null;
            }
            return bestAt;
        }

        // ------------------------------------------------------------------ the box

        /// <summary>The white-bordered box holding the shape to match, or Empty.</summary>
        static Rectangle FindBox(byte[] px, int w, int h, int bw, int bh)
        {
            Rectangle best = Rectangle.Empty;
            double bestScore = 0;
            for (int step = 6; step >= 1; step /= 2)
            {
                Rectangle from = best.IsEmpty ? new Rectangle(0, 0, w - bw, h - bh) : Inflate(best, step * 2, w, h, bw, bh);
                for (int y = from.Y; y <= from.Bottom; y += step)
                    for (int x = from.X; x <= from.Right; x += step)
                    {
                        double score = BorderScore(px, w, h, x, y, bw, bh);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = new Rectangle(x, y, bw, bh);
                    }
            }
            return bestScore >= 0.85 ? best : Rectangle.Empty;
        }

        static Rectangle Inflate(Rectangle r, int by, int w, int h, int bw, int bh)
        {
            var o = new Rectangle(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);
            o.X = Math.Max(0, Math.Min(w - bw, o.X));
            o.Y = Math.Max(0, Math.Min(h - bh, o.Y));
            o.Width = Math.Min(w - o.X - bw, o.Width);
            o.Height = Math.Min(h - o.Y - bh, o.Height);
            return o;
        }

        /// <summary>How much of the box's border is white, and whether its middle is dark with something bright in it.</summary>
        /// <summary>How much of the box's border is white, and whether its middle is dark with something bright in it.</summary>
        static double BorderScore(byte[] px, int w, int h, int x, int y, int bw, int bh)
        {
            if (x < 0 || y < 0 || x + bw > w || y + bh > h) return 0;
            const int samples = 24;
            int hit = 0, total = 0;
            for (int i = 0; i < samples; i++)
            {
                int ax = x + 4 + (bw - 8) * i / (samples - 1);
                int ay = y + 4 + (bh - 8) * i / (samples - 1);
                foreach (Point q in new[] { new Point(ax, y + 3), new Point(ax, y + bh - 3),
                                            new Point(x + 3, ay), new Point(x + bw - 3, ay) })
                {
                    total++;
                    if (BrightNear(px, w, h, q.X, q.Y, 2)) hit++;
                }
            }
            double border = hit / (double)total;
            if (border < 0.6) return 0;

            int cx = x + bw / 2, cy = y + bh / 2;
            long bright = 0;
            for (int dy = -12; dy <= 12; dy += 3)
                for (int dx = -12; dx <= 12; dx += 3)
                {
                    int o = ((cy + dy) * w + cx + dx) * 4;
                    if (Ink(px[o + 2], px[o + 1], px[o])) bright++;
                }
            return bright >= 2 && bright <= 140 ? border : 0;
        }

        /// <summary>Whether a border-white pixel lies within <paramref name="reach"/> of this one.</summary>
        static bool BrightNear(byte[] px, int w, int h, int x, int y, int reach)
        {
            for (int dy = -reach; dy <= reach; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= h) continue;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int nx = x + dx;
                    if (nx < 0 || nx >= w) continue;
                    int o = (ny * w + nx) * 4;
                    if (Math.Min(px[o], Math.Min(px[o + 1], px[o + 2])) >= 170) return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ candidates

        static List<Blob> Blobs(byte[] px, int w, int h, Rectangle box, int minSide, int maxSide, bool inside)
        {
            var seen = new bool[w * h];
            var found = new List<Blob>();
            var stack = new Stack<int>();

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int at = y * w + x;
                    if (seen[at] || !Inside(px, at)) continue;
                    if (InBox(box, x, y) != inside) continue;

                    var b = new Blob { X0 = x, Y0 = y, X1 = x, Y1 = y };
                    seen[at] = true;
                    stack.Push(at);
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        int ix = i % w, iy = i / w;
                        b.Area++;
                        b.SumX += ix;
                        b.SumY += iy;
                        b.Pixels.Add(i);
                        if (ix < b.X0) b.X0 = ix;
                        if (iy < b.Y0) b.Y0 = iy;
                        if (ix > b.X1) b.X1 = ix;
                        if (iy > b.Y1) b.Y1 = iy;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = ix + dx, ny = iy + dy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                                int ni = ny * w + nx;
                                if (seen[ni] || !Inside(px, ni)) continue;
                                if (InBox(box, nx, ny) != inside) continue;
                                seen[ni] = true;
                                stack.Push(ni);
                            }
                    }

                    int bw = b.X1 - b.X0 + 1, bh = b.Y1 - b.Y0 + 1;
                    if (b.Area < MinArea || bw < minSide || bh < minSide || bw > maxSide || bh > maxSide) continue;
                    if (!inside && (b.X0 == 0 || b.Y0 == 0 || b.X1 == w - 1 || b.Y1 == h - 1)) continue;
                    if (bw > bh * 2.4 || bh > bw * 2.4) continue;
                    if (!inside && b.Area < bw * bh * 0.12) continue;
                    found.Add(b);
                }
            return found;
        }

        /// <summary>
        /// The ink mask, from local contrast rather than colour: every pixel that stands out against a blurred copy of the panel.
        /// Soft translucent masses and the panel itself have no edges and drop out, while shapes, their hatching and their
        /// outlines do have them. Gaps are closed and small enclosed pockets filled, so hatching reads as one solid body.
        /// </summary>
        static byte[] Solid(byte[] px, int w, int h)
        {
            var lum = new byte[w * h];
            for (int i = 0; i < lum.Length; i++)
            {
                int o = i * 4;
                lum[i] = (byte)((px[o + 2] * 30 + px[o + 1] * 59 + px[o] * 11) / 100);
            }

            // Summed-area table, so each blurred value is a constant-time box sum.
            int stride = w + 1;
            var sat = new int[stride * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                int run = 0;
                for (int x = 0; x < w; x++)
                {
                    run += lum[y * w + x];
                    sat[(y + 1) * stride + x + 1] = sat[y * stride + x + 1] + run;
                }
            }

            var mask = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - BlurRadius), y1 = Math.Min(h - 1, y + BlurRadius);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - BlurRadius), x1 = Math.Min(w - 1, x + BlurRadius);
                    int area = (x1 - x0 + 1) * (y1 - y0 + 1);
                    int sum = sat[(y1 + 1) * stride + x1 + 1] - sat[y0 * stride + x1 + 1]
                            - sat[(y1 + 1) * stride + x0] + sat[y0 * stride + x0];
                    int o = (y * w + x) * 4;
                    if (Math.Abs(lum[y * w + x] - sum / area) >= EdgeMin) mask[y * w + x] = 1;
                }
            }

            // Erase anything thinner than a rim, before it is thickened: the panel's one- or two-pixel criss-cross lines.
            var eroded = new byte[w * h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int at = y * w + x;
                    if (mask[at] == 0) continue;
                    if (mask[at - 1] != 0 && mask[at + 1] != 0 && mask[at - w] != 0 && mask[at + w] != 0)
                        eroded[at] = 1;
                }

            // Drop anything too thin to be a rim: the panel's one-pixel criss-cross lines and their dots.
            var kept = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (eroded[y * w + x] == 0) continue;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            n += eroded[ny * w + nx];
                        }
                    if (n >= 4) kept[y * w + x] = 1;
                }

            // Close the gaps in hatching and outlines.
            var solid = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (kept[y * w + x] == 0) continue;
                    int y0 = Math.Max(0, y - Grow), y1 = Math.Min(h - 1, y + Grow);
                    int x0 = Math.Max(0, x - Grow), x1 = Math.Min(w - 1, x + Grow);
                    for (int ny = y0; ny <= y1; ny++)
                        for (int nx = x0; nx <= x1; nx++) solid[ny * w + nx] = 1;
                }

            FillHoles(solid, w, h);
            return solid;
        }

        /// <summary>Fills pockets fully enclosed by a blob, except big ones, which are a ring's or a crescent's middle.</summary>
        static void FillHoles(byte[] solid, int w, int h)
        {
            var outside = new bool[w * h];
            var stack = new Stack<int>();
            for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (i < 0 || i >= outside.Length || outside[i] || solid[i] != 0) continue;
                outside[i] = true;
                int ix = i % w, iy = i / w;
                if (ix > 0) stack.Push(i - 1);
                if (ix < w - 1) stack.Push(i + 1);
                if (iy > 0) stack.Push(i - w);
                if (iy < h - 1) stack.Push(i + w);
            }

            var hole = new List<int>();
            var seen = new bool[w * h];
            for (int start = 0; start < solid.Length; start++)
            {
                if (solid[start] != 0 || outside[start] || seen[start]) continue;
                hole.Clear();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    hole.Add(i);
                    int ix = i % w, iy = i / w;
                    if (ix > 0 && solid[i - 1] == 0 && !outside[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
                    if (ix < w - 1 && solid[i + 1] == 0 && !outside[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
                    if (iy > 0 && solid[i - w] == 0 && !outside[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Push(i - w); }
                    if (iy < h - 1 && solid[i + w] == 0 && !outside[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Push(i + w); }
                }
                if (hole.Count > MaxHole) continue;
                foreach (int i in hole) solid[i] = 1;
            }
        }

        /// <summary>Whether a pixel of the thickened mask is part of a shape.</summary>
        static bool Inside(byte[] mask, int at)
        {
            return mask[at] != 0;
        }

        static bool InBox(Rectangle box, int x, int y)
        {
            return !box.IsEmpty && x >= box.X - 4 && x <= box.Right + 4 && y >= box.Y - 4 && y <= box.Bottom + 4;
        }

        /// <summary>
        /// The biggest shape inside the box, not counting the border. The interior is opened (eroded, then dilated) first, so
        /// the thin lines crossing the box and the shape are gone while a solid shape of any real size stays.
        /// </summary>
        static Shape DescribeLargest(byte[] px, int w, int h, Rectangle box, out string why)
        {
            why = "";
            Rectangle inner = box;
            inner.Inflate(-16, -16);
            if (inner.Width < 20 || inner.Height < 20) { why = "the box is too small"; return null; }

            const int Open = 6;
            var scratch = new byte[w * h];
            for (int pass = 0; pass < Open; pass++)
            {
                Array.Copy(px, scratch, px.Length);
                for (int y = inner.Y; y < inner.Bottom; y++)
                    for (int x = inner.X; x < inner.Right; x++)
                    {
                        int at = y * w + x;
                        if (px[at] == 0) continue;
                        if (scratch[at - 1] == 0 || scratch[at + 1] == 0 ||
                            scratch[at - w] == 0 || scratch[at + w] == 0) px[at] = 0;
                    }
            }
            for (int pass = 0; pass < Open; pass++)
            {
                Array.Copy(px, scratch, px.Length);
                for (int y = inner.Y; y < inner.Bottom; y++)
                    for (int x = inner.X; x < inner.Right; x++)
                    {
                        int at = y * w + x;
                        if (scratch[at] == 0) continue;
                        px[at - 1] = 1;
                        px[at + 1] = 1;
                        px[at - w] = 1;
                        px[at + w] = 1;
                    }
            }

            Blob best = null;
            int seen = 0;
            foreach (Blob b in Blobs(px, w, h, inner, 20, Math.Max(inner.Width, inner.Height), true))
            {
                seen++;
                int bw = b.X1 - b.X0 + 1, bh = b.Y1 - b.Y0 + 1;
                why += bw + "x" + bh + " ";
                if (best == null || b.Area > best.Area) best = b;
            }
            why = seen + " found: " + why;
            if (best == null) return null;
            Shape s = Describe(px, w, best);
            if (s == null) why += "(describe failed)";
            return s;
        }

       /// <summary>
        /// The blob's outer outline: radii every 11.25° from its centre, each taken at the furthest ink along that ray inside the
        /// blob's own box, so stripes, fills and hollow middles don't matter. Normalised by their mean.
        /// </summary>
        static Shape Describe(byte[] px, int w, Blob b)
        {
            int cx = b.CentreX, cy = b.CentreY;
            var radii = new double[Rays];
            double sum = 0;
            for (int r = 0; r < Rays; r++)
            {
                double angle = r * 2 * Math.PI / Rays;
                double dx = Math.Cos(angle), dy = Math.Sin(angle);
                // Stop at the blob's own box, so a neighbour further out can't pass for this outline.
                double maxT = 400;
                if (dx > 1e-9) maxT = Math.Min(maxT, (b.X1 - cx) / dx);
                if (dx < -1e-9) maxT = Math.Min(maxT, (b.X0 - cx) / dx);
                if (dy > 1e-9) maxT = Math.Min(maxT, (b.Y1 - cy) / dy);
                if (dy < -1e-9) maxT = Math.Min(maxT, (b.Y0 - cy) / dy);
                maxT = Math.Min(maxT + 2, 400);

                double last = 0;
                for (double t = 1; t <= maxT; t += 0.5)
                {
                    int x = (int)Math.Round(cx + dx * t), y = (int)Math.Round(cy + dy * t);
                    if (x < 0 || y < 0 || x >= w) break;
                    if (Inside(px, y * w + x)) last = t;   // the furthest ink, not the first gap
                }
                if (last <= 0) return null;
                radii[r] = last;
                sum += last;
            }
            double mean = sum / Rays;
            if (mean <= 0) return null;
            double spread = 0;
            for (int r = 0; r < Rays; r++)
            {
                double d = radii[r] - mean;
                spread += d * d;
            }
            for (int r = 0; r < Rays; r++) radii[r] /= mean;
            return new Shape
            {
                Signature = radii,
                Point = Math.Sqrt(spread / Rays) / mean,
                Fill = b.Area / (double)((b.X1 - b.X0 + 1) * (b.Y1 - b.Y0 + 1)),
                W = b.X1 - b.X0 + 1,
                H = b.Y1 - b.Y0 + 1
            };
        }

        /// <summary>How much of the blob's box is background fully enclosed by it: a ring or a hollow shape scores high.</summary>
        static double HoleShare(byte[] px, int w, Blob b)
        {
            int bw = b.X1 - b.X0 + 1, bh = b.Y1 - b.Y0 + 1;
            if (bw < 6 || bh < 6) return 0;
            var seen = new bool[bw * bh];
            var stack = new Stack<int>();
            for (int x = 0; x < bw; x++) { stack.Push(x); stack.Push((bh - 1) * bw + x); }
            for (int y = 0; y < bh; y++) { stack.Push(y * bw); stack.Push(y * bw + bw - 1); }
            int outside = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (i < 0 || i >= seen.Length || seen[i]) continue;
                seen[i] = true;
                int ix = i % bw, iy = i / bw;
                if (Inside(px, (b.Y0 + iy) * w + b.X0 + ix)) continue;
                outside++;
                if (ix > 0) stack.Push(i - 1);
                if (ix < bw - 1) stack.Push(i + 1);
                if (iy > 0) stack.Push(i - bw);
                if (iy < bh - 1) stack.Push(i + bw);
            }
            int inside = bw * bh - outside - b.Area;
            return inside <= 0 ? 0 : inside / (double)(bw * bh);
        }

        /// <summary>
        /// How unlike two outlines are at their best rotation: half the mean difference and half the worst quarter of it, so
        /// corners and points count for much more than an even edge does.
        /// </summary>
        static double SignatureDistance(double[] a, double[] b)
        {
            int bestShift = 0;
            double bestSum = double.MaxValue;
            for (int shift = 0; shift < Rays; shift++)
            {
                double sum = 0;
                for (int i = 0; i < Rays; i++) sum += Math.Abs(a[i] - b[(i + shift) % Rays]);
                if (sum < bestSum) { bestSum = sum; bestShift = shift; }
            }

            var diffs = new double[Rays];
            for (int i = 0; i < Rays; i++) diffs[i] = Math.Abs(a[i] - b[(i + bestShift) % Rays]);
            Array.Sort(diffs);
            int worst = Math.Max(3, Rays / 4);
            double top = 0;
            for (int i = Rays - worst; i < Rays; i++) top += diffs[i];
            return 0.5 * (bestSum / Rays) + 0.5 * (top / worst);
        }

        /// <summary>Keeps the frame and the choice for tuning, in captchas beside the settings.</summary>
        public static void Save(Bitmap shot, Point? at)
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath), "captchas");
                Directory.CreateDirectory(dir);
                string name = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + (at.HasValue ? " at " + at.Value.X + "," + at.Value.Y : " unsolved");
                shot.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            }
            catch (Exception) { }
        }
    }
}