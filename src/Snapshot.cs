using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// A shrunken copy of a screen area (at most 48 px on its longest side). Comparing a fresh one
    /// against a saved one tells whether the game is showing the screen a step waits for.
    /// </summary>
    public class Snapshot
    {
        const int MaxSide = 48;

        public readonly int Width;
        public readonly int Height;
        readonly byte[] rgb;
        readonly bool plain;

        Snapshot(int width, int height, byte[] rgb)
        {
            Width = width;
            Height = height;
            this.rgb = rgb;
            double mean, sd;
            GrayStats(rgb, Width * Height, null, out mean, out sd);
            plain = sd < PlainBelow;
        }

        /// <summary>Below this spread of brightness an area counts as plain.</summary>
        const double PlainBelow = 6;

        /// <summary>True for a nearly flat area, where only the colour can be compared.</summary>
        public bool IsPlain { get { return plain; } }

        /// <summary>The same for only the cells <paramref name="mask"/> marks (inside a lasso); null for the whole area.</summary>
        public bool IsPlainWithin(bool[] mask)
        {
            if (mask == null || mask.Length != Width * Height) return plain;
            double mean, sd;
            GrayStats(rgb, Width * Height, mask, out mean, out sd);
            return sd < PlainBelow;
        }

        static Size GridSize(Size area)
        {
            int w = area.Width, h = area.Height;
            if (w >= h)
            {
                int gw = Math.Min(w, MaxSide);
                return new Size(gw, Math.Max(1, (int)Math.Round(h * (double)gw / w)));
            }
            int gh = Math.Min(h, MaxSide);
            return new Size(Math.Max(1, (int)Math.Round(w * (double)gh / h)), gh);
        }

        /// <summary>Shrinks <paramref name="area"/> of <paramref name="source"/> by averaging blocks of pixels.</summary>
        public static Snapshot FromBitmap(Bitmap source, Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            if (!new Rectangle(Point.Empty, source.Size).Contains(area)) return null;

            Size grid = GridSize(area.Size);
            BitmapData data = source.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px;
            int stride = data.Stride;
            try
            {
                px = new byte[stride * area.Height];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
            }
            finally
            {
                source.UnlockBits(data);
            }

            var rgb = new byte[grid.Width * grid.Height * 3];
            for (int gy = 0; gy < grid.Height; gy++)
            {
                int y0 = gy * area.Height / grid.Height, y1 = (gy + 1) * area.Height / grid.Height;
                for (int gx = 0; gx < grid.Width; gx++)
                {
                    int x0 = gx * area.Width / grid.Width, x1 = (gx + 1) * area.Width / grid.Width;
                    long r = 0, g = 0, b = 0;
                    int n = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * stride;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x * 4;
                            b += px[i];
                            g += px[i + 1];
                            r += px[i + 2];
                            n++;
                        }
                    }
                    int o = (gy * grid.Width + gx) * 3;
                    rgb[o] = (byte)(r / n);
                    rgb[o + 1] = (byte)(g / n);
                    rgb[o + 2] = (byte)(b / n);
                }
            }
            return new Snapshot(grid.Width, grid.Height, rgb);
        }

        /// <summary>Snapshot of an area of the live screen, or null if the screen can't be read (e.g. the PC is locked).</summary>
        public static Snapshot FromScreen(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            try
            {
                using (var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                    return FromBitmap(bmp, new Rectangle(Point.Empty, area.Size));
                }
            }
            catch (ExternalException)
            {
                return null;
            }
        }

        /// <summary>
        /// How closely <paramref name="live"/> matches this snapshot, from 0 (different) to 1 (identical).
        /// Combines the pattern (so a blank loading screen never matches a button) with the colours
        /// (so a dimmed or half-faded button scores lower than a fully shown one).
        /// </summary>
        public double Similarity(Snapshot live)
        {
            return Similarity(live, null);
        }

        /// <summary>The same, comparing only the cells <paramref name="mask"/> marks (inside a lasso); null compares them all.</summary>
        public double Similarity(Snapshot live, bool[] mask)
        {
            double night;
            return Similarity(live, mask, out night);
        }

        /// <summary>
        /// What the game's night does to its world (the map, a fight's field and Lumits, the names over them), but not to
        /// its buttons, cards or minimap: each colour multiplied, with no offset. Measured on 14 November 2026 on the same
        /// sprites by day and by night: a standing stone (red 0.40, green 0.55, blue 0.79) and a rock (0.38, 0.49, 0.73).
        /// </summary>
        static readonly double[] NightTint = { 0.40, 0.52, 0.79 };
        /// <summary>
        /// The night is tried this far along, in steps of a tenth: dusk and dawn as part of the way, and a little past it in
        /// case the night darkens more than when it was measured.
        /// </summary>
        const int NightSteps = 13;

        /// <summary>
        /// The same, also as the night's light would change things, so a picture taken by day matches at night and one taken
        /// at night matches by day. <paramref name="night"/> says which matched best: 0 as taken; above 0, darker now than
        /// when taken (1 is the night as measured); below 0, lighter now (taken at night).
        /// </summary>
        public double Similarity(Snapshot live, bool[] mask, out double night)
        {
            night = 0;
            if (live == null || live.Width != Width || live.Height != Height) return 0;
            if (mask != null && mask.Length != Width * Height) mask = null;
            // Whether it's plain is judged as it was taken, whatever light it's compared in.
            bool flat = mask == null ? plain : IsPlainWithin(mask);
            double asTaken = Compare(live, mask, flat, null, null);
            if (asTaken >= 0.995) return asTaken;
            // Only a change of light like the night's is tried: buttons dimmed as the game animates go darker evenly, and
            // dark grey parts look much the same darkened either way (a lit Strike label scored 76% against a dimmed one
            // through the night's tint, and 41% as taken).
            double darker, nightLike;
            LightChange(live, mask, out darker, out nightLike);
            if (Math.Abs(darker) < NightFrom || nightLike < NightLikeFrom || nightLike > NightLikeTo) return asTaken;
            double atNight = 0, nightAt = 0;
            var tint = new double[3];
            for (int step = 1; step <= NightSteps; step++)
            {
                double t = step / 10.0;
                for (int c = 0; c < 3; c++) tint[c] = 1 - t * (1 - NightTint[c]);
                // Darker now: taken by day, so this snapshot as the night would show it. Lighter now: taken at night, so the
                // live area as the night would show it.
                double now = darker > 0 ? Compare(live, mask, flat, tint, null) : Compare(live, mask, flat, null, tint);
                if (now > atNight)
                {
                    atNight = now;
                    nightAt = darker > 0 ? t : -t;
                }
            }
            if (atNight <= asTaken) return asTaken;
            night = nightAt;
            return atNight;
        }

        /// <summary>The light has to have changed by at least this much (red's gain, 1 minus it) for the night to be tried.</summary>
        const double NightFrom = 0.15;
        /// <summary>
        /// How night-like a change of light has to be: blue kept over red, against red lost. The night's tint gives 0.65 at
        /// any strength (0.61 to 0.66 on the stone and the rock); dimmed buttons gave -0.43 to -0.04.
        /// </summary>
        const double NightLikeFrom = 0.35, NightLikeTo = 1;

        /// <summary>
        /// How the light changed from this snapshot to <paramref name="live"/>, from each colour's least-squares gain:
        /// <paramref name="darker"/> is 1 minus red's gain, above 0 when darker now, below 0 when lighter (the gains then
        /// taken the other way); <paramref name="nightLike"/> is how much more blue than red was kept, against the red lost.
        /// </summary>
        void LightChange(Snapshot live, bool[] mask, out double darker, out double nightLike)
        {
            var mineMine = new double[3];
            var mineTheirs = new double[3];
            var theirsTheirs = new double[3];
            for (int i = 0; i < Width * Height; i++)
            {
                if (mask != null && !mask[i]) continue;
                for (int c = 0; c < 3; c++)
                {
                    double a = rgb[i * 3 + c], b = live.rgb[i * 3 + c];
                    mineMine[c] += a * a;
                    mineTheirs[c] += a * b;
                    theirsTheirs[c] += b * b;
                }
            }
            var gain = new double[3];
            for (int c = 0; c < 3; c++) gain[c] = mineMine[c] > 0 ? mineTheirs[c] / mineMine[c] : 1;
            bool lighter = gain[0] > 1;
            if (lighter)
                for (int c = 0; c < 3; c++) gain[c] = theirsTheirs[c] > 0 ? mineTheirs[c] / theirsTheirs[c] : 1;
            double lost = 1 - gain[0];
            darker = lighter ? -lost : lost;
            nightLike = lost > 1e-6 ? (gain[2] - gain[0]) / lost : 0;
        }

        /// <summary>
        /// Pattern times colour, with this snapshot's colours multiplied by <paramref name="mine"/> and the live one's by
        /// <paramref name="theirs"/> (null: as they are). <paramref name="flat"/>: only the colour counts.
        /// </summary>
        double Compare(Snapshot live, bool[] mask, bool flat, double[] mine, double[] theirs)
        {
            int n = 0;
            byte[] a = rgb, b = live.rgb;
            double absSum = 0, sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
            for (int i = 0; i < Width * Height; i++)
            {
                if (mask != null && !mask[i]) continue;
                n++;
                int o = i * 3;
                double ar = a[o], ag = a[o + 1], ab = a[o + 2], br = b[o], bg = b[o + 1], bb = b[o + 2];
                if (mine != null)
                {
                    ar *= mine[0];
                    ag *= mine[1];
                    ab *= mine[2];
                }
                if (theirs != null)
                {
                    br *= theirs[0];
                    bg *= theirs[1];
                    bb *= theirs[2];
                }
                absSum += Math.Abs(ar - br) + Math.Abs(ag - bg) + Math.Abs(ab - bb);
                double ga = 0.299 * ar + 0.587 * ag + 0.114 * ab, gb = 0.299 * br + 0.587 * bg + 0.114 * bb;
                sumA += ga;
                sumB += gb;
                sumAA += ga * ga;
                sumBB += gb * gb;
                sumAB += ga * gb;
            }

            if (n == 0) return 0;
            double color = Clamp01(1 - absSum / (3.0 * n) / 60.0);
            if (flat) return color;
            double meanA = sumA / n, meanB = sumB / n;
            double sdA = Math.Sqrt(Math.Max(0, sumAA / n - meanA * meanA));
            double sdB = Math.Sqrt(Math.Max(0, sumBB / n - meanB * meanB));
            if (sdB < 3 || sdA < 1e-6) return 0; // the live area is blank
            double pattern = (sumAB / n - meanA * meanB) / (sdA * sdB);
            return Clamp01(pattern) * color;
        }

        /// <summary>A small picture of what this snapshot compares, for showing in the editor.</summary>
        public Bitmap ToPreview()
        {
            return ToPreview(null);
        }

        /// <summary>The same, with the cells outside <paramref name="mask"/> (outside a lasso) darkened, since they aren't compared.</summary>
        public Bitmap ToPreview(bool[] mask)
        {
            if (mask != null && mask.Length != Width * Height) mask = null;
            var bmp = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x, o = i * 3;
                    int d = mask == null || mask[i] ? 1 : 4;
                    bmp.SetPixel(x, y, Color.FromArgb(rgb[o] / d, rgb[o + 1] / d, rgb[o + 2] / d));
                }
            return bmp;
        }

        public string Serialize()
        {
            return Width + "x" + Height + ":" + Convert.ToBase64String(rgb);
        }

        public static Snapshot Parse(string text)
        {
            try
            {
                int colon = text.IndexOf(':'), x = text.IndexOf('x');
                if (colon < 0 || x < 0 || x > colon) return null;
                int w = int.Parse(text.Substring(0, x));
                int h = int.Parse(text.Substring(x + 1, colon - x - 1));
                byte[] data = Convert.FromBase64String(text.Substring(colon + 1));
                if (w <= 0 || h <= 0 || data.Length != w * h * 3) return null;
                return new Snapshot(w, h, data);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static double Gray(byte[] rgb, int o)
        {
            return 0.299 * rgb[o] + 0.587 * rgb[o + 1] + 0.114 * rgb[o + 2];
        }

        static void GrayStats(byte[] rgb, int cells, bool[] mask, out double mean, out double sd)
        {
            double sum = 0, sumSq = 0;
            int n = 0;
            for (int i = 0; i < cells; i++)
            {
                if (mask != null && !mask[i]) continue;
                double g = Gray(rgb, i * 3);
                sum += g;
                sumSq += g * g;
                n++;
            }
            if (n == 0)
            {
                mean = sd = 0;
                return;
            }
            mean = sum / n;
            sd = Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));
        }

        static double Clamp01(double v)
        {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }

    public static class ScreenGrab
    {
        /// <summary>Screenshot of every monitor; pixel (0, 0) is the top-left of the virtual screen.</summary>
        public static Bitmap CaptureVirtualScreen()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(vs.Location, Point.Empty, vs.Size, CopyPixelOperation.SourceCopy);
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
            return bmp;
        }

        /// <summary>Makes the given windows invisible while <paramref name="action"/> runs, so they stay out of screenshots.</summary>
        public static void WhileHidden(IEnumerable<Form> forms, Action action)
        {
            var hidden = new List<Form>();
            foreach (Form f in forms)
            {
                if (f == null || !f.Visible || f.Opacity <= 0) continue;
                f.Opacity = 0;
                hidden.Add(f);
            }
            if (hidden.Count > 0)
            {
                Application.DoEvents();
                Thread.Sleep(200); // give the desktop compositor time to redraw without them
            }
            try
            {
                action();
            }
            finally
            {
                foreach (Form f in hidden) f.Opacity = 1;
            }
        }
    }
}
