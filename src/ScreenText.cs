using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace AutoClicker
{
    /// <summary>How text stands out from what's behind it, which decides how a picture is prepared for reading.</summary>
    public enum Ink
    {
        /// <summary>Light letters of any colour (white or gold) on a dark card.</summary>
        Light,
        /// <summary>White letters over coloured shapes: colour counts as background.</summary>
        White,
        /// <summary>Light letters, cut to pure black and white halfway between the darkest and lightest pixel.</summary>
        Sharp,
        /// <summary>Faint light letters (grey on grey): contrast stretched from the background up to the letters.</summary>
        LightStretch,
        /// <summary>As <see cref="White"/>, with the contrast stretched.</summary>
        WhiteStretch
    }

    /// <summary>Reads text off pictures of the game with the text recognition built into Windows 10 and 11.</summary>
    public static class ScreenText
    {
        /// <summary>The text in <paramref name="area"/> of <paramref name="source"/>, "" if none, or null if this PC has no text recognition.</summary>
        /// <param name="scale">How much to enlarge first; 0 picks a size that suits a line of text.</param>
        /// <param name="pad">Add a margin, which helps with small labels that fill their box.</param>
        public static string Read(Bitmap source, Rectangle area, Ink ink, int scale, bool pad)
        {
            if (source == null) return "";
            area.Intersect(new Rectangle(Point.Empty, source.Size));
            if (area.Width < 2 || area.Height < 2) return "";
            if (scale <= 0) scale = Math.Max(2, Math.Min(4, (int)Math.Round(96.0 / area.Height)));
            try
            {
                int w, h;
                byte[] pixels = Prepare(source, area, ink, scale, pad ? 24 : 0, out w, out h);
                return WindowsOcr.Recognize(pixels, w, h);
            }
            catch (Exception)
            {
                return null; // Windows' text recognition isn't available
            }
        }

        /// <summary>
        /// A number between <paramref name="min"/> and <paramref name="max"/> (the last one in the text, e.g. 25 in "IV 25"),
        /// read many ways; -1 unless one value clearly wins (see <see cref="Confident"/>).
        /// </summary>
        public static int ReadNumber(Bitmap source, Rectangle area, int min, int max)
        {
            return Confident(ReadVotes(source, area, min, max));
        }

        /// <summary>
        /// How many of the ways of reading a small label found each number in it. Faint small labels read under some
        /// of these and not others, and now and then one is misread ("IVI" for "IV 7"), so the counts are what's trusted.
        /// </summary>
        public static Dictionary<int, int> ReadVotes(Bitmap source, Rectangle area, int min, int max)
        {
            var votes = new Dictionary<int, int>();
            foreach (int scale in new[] { 3, 4, 5, 6, 7, 8, 10 })
            {
                foreach (Ink ink in new[] { Ink.Light, Ink.LightStretch, Ink.WhiteStretch, Ink.White })
                {
                    string text = Read(source, area, ink, scale, true);
                    if (text == null) return votes;
                    int n = LastNumber(text);
                    if (n < min || n > max) continue;
                    int count;
                    votes.TryGetValue(n, out count);
                    votes[n] = count + 1;
                }
            }
            return votes;
        }

        /// <summary>The value read clearly: at least 2 readings, and 4 times as many as all other values together; -1 otherwise.</summary>
        public static int Confident(Dictionary<int, int> votes)
        {
            if (votes.Count == 0) return -1;
            KeyValuePair<int, int> best = votes.OrderByDescending(v => v.Value).First();
            int others = votes.Values.Sum() - best.Value;
            return best.Value >= 2 && best.Value >= 4 * others ? best.Key : -1;
        }

        /// <summary>The number at the end of the text ("IV 25" → 25), with letters that look like digits right after "IV" read as digits; -1 if none.</summary>
        public static int LastNumber(string text)
        {
            Match m = Regex.Match(text, @"I[VY]\s*([0-9SOIlZB]{1,2})\s*$");
            string digits = m.Success ? m.Groups[1].Value : null;
            if (digits == null)
            {
                m = Regex.Match(text, @"(\d{1,5})\D*$");
                if (!m.Success) return -1;
                digits = m.Groups[1].Value;
            }
            // "IV 1" often comes out as "IVI"; a 7 occasionally does too, which the vote counts outweigh.
            digits = digits.Replace('S', '5').Replace('O', '0').Replace('I', '1').Replace('l', '1').Replace('Z', '2').Replace('B', '8');
            int n;
            return int.TryParse(digits, out n) ? n : -1;
        }

        /// <summary>How full a coloured bar is (0–1), judged by its rightmost filled pixel in most rows; -1 if the area is outside the picture.</summary>
        public static double BarFill(Bitmap source, Rectangle bar, Func<int, int, int, bool> isFill)
        {
            if (source == null || bar.Width < 4 || bar.Height < 1 || !new Rectangle(Point.Empty, source.Size).Contains(bar)) return -1;
            BitmapData data = source.LockBits(bar, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var rows = new List<double>();
            try
            {
                var row = new byte[bar.Width * 4];
                for (int y = 0; y < bar.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    int end = -1, run = 0;
                    for (int x = 0; x < bar.Width; x++)
                    {
                        // A few filled pixels in a row, so a speck of colour doesn't count as the end of the bar.
                        if (isFill(row[x * 4 + 2], row[x * 4 + 1], row[x * 4])) { if (++run >= 3) end = x; }
                        else run = 0;
                    }
                    rows.Add((end + 1) / (double)bar.Width);
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
            rows.Sort();
            return rows[rows.Count / 2];
        }

        /// <summary>
        /// Whether a button's label is lit (white text), as opposed to greyed out or dimmed while the game animates.
        /// Measured on the fight menu: lit labels fill 12–20% of their box with near-white pixels, dimmed ones under 1%.
        /// </summary>
        public static bool Lit(Bitmap source, Rectangle button)
        {
            return Lit(source, button, 5.0);
        }

        /// <summary>Lit, with the share of near-white pixels (in %) that counts: lower for short labels like "Strike".</summary>
        public static bool Lit(Bitmap source, Rectangle button, double minPercent)
        {
            if (source == null || button.Width < 2 || button.Height < 2 || !new Rectangle(Point.Empty, source.Size).Contains(button)) return false;
            BitmapData data = source.LockBits(button, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int white = 0;
            try
            {
                var row = new byte[button.Width * 4];
                for (int y = 0; y < button.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < button.Width; x++)
                        if (Math.Min(row[x * 4], Math.Min(row[x * 4 + 1], row[x * 4 + 2])) >= 170) white++;
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
            return white * 100.0 >= button.Width * button.Height * minPercent;
        }

        public static bool IsRed(int r, int g, int b) { return r >= 140 && r - g >= 70 && r - b >= 50; }

        public static bool IsGreen(int r, int g, int b) { return g >= 120 && g - r >= 35 && g - b >= 15; }

        /// <summary>Enlarged, as dark text on white: recognition is far more reliable on bigger letters.</summary>
        static byte[] Prepare(Bitmap source, Rectangle area, Ink ink, int scale, int pad, out int w, out int h)
        {
            int iw = area.Width * scale, ih = area.Height * scale;
            w = iw + pad * 2;
            h = ih + pad * 2;
            using (var big = new Bitmap(iw, ih, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, iw, ih), area, GraphicsUnit.Pixel);
                }
                var levels = new byte[iw * ih];
                BitmapData data = big.LockBits(new Rectangle(0, 0, iw, ih), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[iw * 4];
                    for (int y = 0; y < ih; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        for (int x = 0; x < iw; x++)
                        {
                            byte b = row[x * 4], gr = row[x * 4 + 1], r = row[x * 4 + 2];
                            // Brightest channel keeps white and gold letters alike; the dimmest keeps only white ones.
                            levels[y * iw + x] = ink == Ink.White || ink == Ink.WhiteStretch ? Math.Min(r, Math.Min(gr, b)) : Math.Max(r, Math.Max(gr, b));
                        }
                    }
                }
                finally
                {
                    big.UnlockBits(data);
                }

                byte lo = 255, hi = 0;
                foreach (byte v in levels) { if (v < lo) lo = v; if (v > hi) hi = v; }
                int mid = (lo + hi) / 2;
                bool stretch = ink == Ink.LightStretch || ink == Ink.WhiteStretch;
                // For stretching: the background is the most common level, the letters the brightest few.
                byte[] sorted = stretch ? levels.OrderBy(v => v).ToArray() : null;
                int background = stretch ? sorted[sorted.Length / 2] : lo;
                int top = stretch ? sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.995))] : hi;
                var pixels = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int ix = x - pad, iy = y - pad;
                        // The margin takes the background level, so it blends in rather than drawing an edge.
                        int v = ix >= 0 && iy >= 0 && ix < iw && iy < ih ? levels[iy * iw + ix] : background;
                        if (ink == Ink.Sharp) v = v > mid ? 255 : 0;
                        else if (stretch) v = Math.Max(0, Math.Min(255, (v - background) * 255 / Math.Max(1, top - background)));
                        int i = (y * w + x) * 4;
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)(255 - v);
                        pixels[i + 3] = 255;
                    }
                }
                return pixels;
            }
        }

        /// <summary>Windows' own text recognition, kept apart so a PC without it only loses reading, not the whole app.</summary>
        static class WindowsOcr
        {
            static readonly object sync = new object();
            static Windows.Media.Ocr.OcrEngine engine;
            static bool tried;

            /// <summary>Recognized text of 32-bit BGRA pixels, or null if no recognizer is installed.</summary>
            public static string Recognize(byte[] pixels, int w, int h)
            {
                lock (sync)
                {
                    if (!tried)
                    {
                        tried = true;
                        engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US")) ??
                                 Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                    }
                    if (engine == null) return null;

                    var writer = new Windows.Storage.Streams.DataWriter();
                    writer.WriteBytes(pixels);
                    var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),
                        Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, w, h, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                    var op = engine.RecognizeAsync(bitmap);
                    using (var done = new ManualResetEvent(false))
                    {
                        op.Completed = delegate { done.Set(); };
                        done.WaitOne(3000);
                    }
                    return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults().Text : "";
                }
            }
        }
    }
}
