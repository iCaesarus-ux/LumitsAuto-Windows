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
            GrayStats(rgb, Width * Height, out mean, out sd);
            plain = sd < 6;
        }

        /// <summary>True for a nearly flat area, where only the colour can be compared.</summary>
        public bool IsPlain { get { return plain; } }

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
            if (live == null || live.Width != Width || live.Height != Height) return 0;

            int n = Width * Height;
            byte[] a = rgb, b = live.rgb;
            double absSum = 0, sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
            for (int i = 0; i < n; i++)
            {
                int o = i * 3;
                absSum += Math.Abs(a[o] - b[o]) + Math.Abs(a[o + 1] - b[o + 1]) + Math.Abs(a[o + 2] - b[o + 2]);
                double ga = Gray(a, o), gb = Gray(b, o);
                sumA += ga;
                sumB += gb;
                sumAA += ga * ga;
                sumBB += gb * gb;
                sumAB += ga * gb;
            }

            double color = Clamp01(1 - absSum / (3.0 * n) / 60.0);
            if (plain) return color;

            double meanA = sumA / n, meanB = sumB / n;
            double sdA = Math.Sqrt(Math.Max(0, sumAA / n - meanA * meanA));
            double sdB = Math.Sqrt(Math.Max(0, sumBB / n - meanB * meanB));
            if (sdB < 3) return 0; // the live area is blank
            double pattern = (sumAB / n - meanA * meanB) / (sdA * sdB);
            return Clamp01(pattern) * color;
        }

        /// <summary>A small picture of what this snapshot compares, for showing in the editor.</summary>
        public Bitmap ToPreview()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int o = (y * Width + x) * 3;
                    bmp.SetPixel(x, y, Color.FromArgb(rgb[o], rgb[o + 1], rgb[o + 2]));
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

        static void GrayStats(byte[] rgb, int n, out double mean, out double sd)
        {
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double g = Gray(rgb, i * 3);
                sum += g;
                sumSq += g * g;
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
