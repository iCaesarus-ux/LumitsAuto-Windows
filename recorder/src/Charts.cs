using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ClickRecorder
{
    /// <summary>The graphs' colours: a light surface, three inks, hairlines, and a fixed colour per recording.</summary>
    static class Ink
    {
        public static readonly Color Surface = Hex(0xfcfcfb);
        public static readonly Color Primary = Hex(0x0b0b0b);
        public static readonly Color Secondary = Hex(0x52514e);
        public static readonly Color Muted = Hex(0x898781);
        public static readonly Color Grid = Hex(0xe1e0d9);
        public static readonly Color Axis = Hex(0xc3c2b7);

        // Blue, orange, aqua, yellow, magenta, green, violet, red: in this order neighbours stay apart for colour-blind
        // eyes too. The click map shows at most three recordings, since only the first three stay apart all together.
        static readonly Color[] series =
        {
            Hex(0x2a78d6), Hex(0xeb6834), Hex(0x1baf7a), Hex(0xeda100),
            Hex(0xe87ba4), Hex(0x008300), Hex(0x6250d6), Hex(0xe34948)
        };

        /// <summary>A recording's colour by its slot; past the eighth recording they're all grey.</summary>
        public static Color Series(int slot)
        {
            return slot >= 0 && slot < series.Length ? series[slot] : Muted;
        }

        static Color Hex(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }
    }

    /// <summary>Equal-width bins over a stretch of milliseconds, shared by every histogram so they line up.</summary>
    sealed class Bins
    {
        public double From, Width;
        public int Count;

        public double To { get { return From + Width * Count; } }

        /// <param name="width">Bin width in ms; 0 picks one from how spread out the gaps are.</param>
        /// <param name="upTo">
        /// Last ms shown; 0 picks it: up to where 99.5% of the gaps are, but no further than four times the middle half's
        /// spread past it, so a few long pauses (between screens, say) don't squash the rest. Those count as off the scale.
        /// </param>
        public static Bins For(IEnumerable<double[]> sets, double width, double upTo)
        {
            double[] all = sets.SelectMany(s => s).ToArray();
            if (all.Length == 0) return null;
            Array.Sort(all);

            double q1 = Summary.Percentile(all, 0.25), q3 = Summary.Percentile(all, 0.75), iqr = q3 - q1;
            double w = width;
            // Half the Freedman–Diaconis width (the middle half's spread over the cube root of the count), rounded to
            // 1, 2 or 5: a little finer than usual, since the shape of the peak is what's being looked at.
            if (w <= 0) w = NiceUp(Math.Max(1, iqr / Math.Pow(all.Length, 1 / 3.0)));
            double low = Math.Max(Summary.Percentile(all, 0.005), q1 - 4 * iqr);
            double high = upTo > 0 ? upTo : Math.Min(Summary.Percentile(all, 0.995), q3 + 4 * iqr);
            while (true)
            {
                double from = Math.Max(0, Math.Floor(low / w) * w);
                int count = Math.Max(1, (int)Math.Floor((Math.Max(high, from) - from) / w) + 1);
                if (count <= 400) return new Bins { From = from, Width = w, Count = count };
                w = NiceUp(w * 1.5);
            }
        }

        /// <summary>The bin a gap falls in, or -1 outside the shown stretch.</summary>
        public int IndexOf(double v)
        {
            if (v < From || v >= To) return -1;
            return Math.Min(Count - 1, (int)((v - From) / Width));
        }

        public int[] Counts(double[] gaps, out int outside)
        {
            var counts = new int[Count];
            outside = 0;
            foreach (double g in gaps)
            {
                int i = IndexOf(g);
                if (i < 0) outside++;
                else counts[i]++;
            }
            return counts;
        }

        /// <summary>The smallest 1, 2 or 5 times a power of ten that's at least <paramref name="x"/>.</summary>
        public static double NiceUp(double x)
        {
            double mag = Math.Pow(10, Math.Floor(Math.Log10(x)));
            foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (m * mag >= x - 1e-9) return m * mag;
            return 10 * mag;
        }

        /// <summary>A round tick step that gives about <paramref name="ticks"/> ticks over <paramref name="range"/>.</summary>
        public static double TickStep(double range, int ticks)
        {
            return NiceUp(Math.Max(range, 1e-9) / Math.Max(1, ticks));
        }
    }

    static class Draw
    {
        public static string Ms(double v)
        {
            return v >= 100 ? v.ToString("N0", CultureInfo.CurrentCulture) : v.ToString("0.#", CultureInfo.CurrentCulture);
        }

        public static string Number(double v)
        {
            return v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "N0" : "N1", CultureInfo.CurrentCulture);
        }

        /// <summary>A column with a rounded top and a square foot on the baseline.</summary>
        public static GraphicsPath Column(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height * 2));
            var path = new GraphicsPath();
            if (d < 1)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Top + d / 2, r.Right, r.Bottom);
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
        }

        /// <summary>A dot with a ring of the surface colour, so dots stay apart where they overlap.</summary>
        public static void Dot(Graphics g, PointF c, float radius, float ring, Color color)
        {
            using (var b = new SolidBrush(Ink.Surface))
                g.FillEllipse(b, c.X - radius - ring, c.Y - radius - ring, 2 * (radius + ring), 2 * (radius + ring));
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, c.X - radius, c.Y - radius, 2 * radius, 2 * radius);
        }

        public static void Text(Graphics g, string text, Font font, Color color, float x, float y)
        {
            TextRenderer.DrawText(g, text, font, new Point((int)x, (int)y), color, TextFormatFlags.NoPadding);
        }

        public static void TextRight(Graphics g, string text, Font font, Color color, float right, float y)
        {
            Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, font, new Point((int)(right - size.Width), (int)y), color, TextFormatFlags.NoPadding);
        }

        public static void TextCentred(Graphics g, string text, Font font, Color color, float centre, float y)
        {
            Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, font, new Point((int)(centre - size.Width / 2f), (int)y), color, TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// One histogram of the time between clicks per recording, stacked, on the same millisecond scale so they can be
    /// compared. Each has its numbers beside it; hovering a column shows its range and count.
    /// </summary>
    sealed class HistogramsView : Panel
    {
        sealed class Part
        {
            public Recording Recording;
            public Rectangle Plot;
            public int[] Counts;
        }

        readonly Font titleFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        readonly Font textFont = new Font("Segoe UI", 9F);
        readonly Font smallFont = new Font("Segoe UI", 8.25F);
        readonly ToolTip tip = new ToolTip { InitialDelay = 0, ReshowDelay = 0, AutoPopDelay = 20000 };
        List<Recording> recordings = new List<Recording>();
        bool leftOnly = true;
        Bins bins;
        List<Part> parts = new List<Part>();
        string tipText;

        public HistogramsView()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            AutoScroll = true;
            BackColor = Ink.Surface;
        }

        public void ShowRecordings(List<Recording> shown, bool leftOnly, double binWidth, double upTo)
        {
            recordings = shown;
            this.leftOnly = leftOnly;
            bins = Bins.For(shown.Select(r => r.Gaps(leftOnly)), binWidth, upTo);
            UpdateScrollSize();
            Invalidate();
        }

        int PartHeight(Graphics g)
        {
            float s = g.DpiX / 96f;
            int min = (int)(250 * s);
            return recordings.Count == 0 ? min : Math.Max(min, ClientSize.Height / recordings.Count);
        }

        void UpdateScrollSize()
        {
            using (Graphics g = CreateGraphics())
                AutoScrollMinSize = new Size(0, recordings.Count * (int)(250 * g.DpiX / 96f));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            Render(e.Graphics, ClientSize.Width, PartHeight(e.Graphics));
        }

        /// <summary>Draws every histogram, one under the other, each <paramref name="partHeight"/> high.</summary>
        public void Render(Graphics g, int width, int partHeight)
        {
            g.Clear(Ink.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = g.DpiX / 96f;
            var laidOut = new List<Part>();
            if (recordings.Count == 0)
            {
                Draw.Text(g, "Tick a recording in Click Recorder to see it here.", textFont, Ink.Secondary, 16 * s, 16 * s);
                parts = laidOut;
                return;
            }
            for (int i = 0; i < recordings.Count; i++)
                laidOut.Add(RenderPart(g, recordings[i], new Rectangle(0, i * partHeight, width, partHeight), s));
            parts = laidOut;
        }

        Part RenderPart(Graphics g, Recording r, Rectangle area, float s)
        {
            float pad = 16 * s;
            double[] gaps = r.Gaps(leftOnly);
            Summary sum = Summary.Of(gaps);

            // Title, with the recording's colour beside it so it matches the click map.
            Draw.Dot(g, new PointF(area.X + pad + 5 * s, area.Y + pad + 9 * s), 5 * s, 0, Ink.Series(r.Slot));
            Draw.Text(g, r.Name, titleFont, Ink.Primary, area.X + pad + 16 * s, area.Y + pad);
            Size titleSize = TextRenderer.MeasureText(g, r.Name, titleFont, Size.Empty, TextFormatFlags.NoPadding);
            Draw.Text(g, "   time between " + (leftOnly ? "left clicks" : "clicks"), textFont, Ink.Secondary,
                area.X + pad + 16 * s + titleSize.Width, area.Y + pad + 2 * s);

            var part = new Part { Recording = r };
            if (bins == null || gaps.Length == 0)
            {
                Draw.Text(g, gaps.Length == 0 && r.Counted(leftOnly).Length < 2 ? "Not enough clicks yet: a gap needs two." : "Nothing to show.",
                    textFont, Ink.Secondary, area.X + pad, area.Y + pad + 34 * s);
                return part;
            }

            float statsWidth = 176 * s;
            var plot = Rectangle.Round(RectangleF.FromLTRB(area.X + pad + 44 * s, area.Y + pad + 48 * s,
                                                           area.Right - pad - statsWidth - 12 * s, area.Bottom - pad - 36 * s));
            if (plot.Width < 40 || plot.Height < 40) return part;
            part.Plot = plot;

            int outside;
            int[] counts = bins.Counts(gaps, out outside);
            part.Counts = counts;
            int top = Math.Max(1, counts.Max());
            double yStep = Math.Max(1, Bins.TickStep(top, 4));
            double yMax = Math.Ceiling(top / yStep) * yStep;

            // Gridlines and the count axis
            Draw.Text(g, "gaps", smallFont, Ink.Muted, area.X + pad, plot.Top - 26 * s);
            using (var grid = new Pen(Ink.Grid, 1))
                for (double v = 0; v <= yMax + 1e-9; v += yStep)
                {
                    float y = plot.Bottom - (float)(v / yMax * plot.Height);
                    if (v > 0) g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    Draw.TextRight(g, Draw.Number(v), smallFont, Ink.Muted, plot.Left - 6 * s, y - 7 * s);
                }

            // Columns
            float bw = plot.Width / (float)bins.Count;
            float gap = bw >= 6 * s ? 2 * s : bw >= 3 * s ? 1 * s : 0;
            using (var fill = new SolidBrush(Ink.Series(r.Slot)))
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] == 0) continue;
                    float h = (float)(counts[i] / yMax * plot.Height);
                    var col = new RectangleF(plot.Left + i * bw + gap / 2, plot.Bottom - h, Math.Max(1, bw - gap), h);
                    using (GraphicsPath p = Draw.Column(col, 4 * s)) g.FillPath(fill, p);
                }
            using (var axis = new Pen(Ink.Axis, 1)) g.DrawLine(axis, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

            // The millisecond axis
            double xStep = Bins.TickStep(bins.To - bins.From, Math.Max(2, plot.Width / (int)(80 * s)));
            for (double v = Math.Ceiling(bins.From / xStep) * xStep; v <= bins.To + 1e-9; v += xStep)
            {
                float x = plot.Left + (float)((v - bins.From) / (bins.To - bins.From) * plot.Width);
                using (var tick = new Pen(Ink.Axis, 1)) g.DrawLine(tick, x, plot.Bottom, x, plot.Bottom + 4 * s);
                Draw.TextCentred(g, Draw.Ms(v), smallFont, Ink.Muted, x, plot.Bottom + 6 * s);
            }
            Draw.TextCentred(g, "time between clicks (ms)", textFont, Ink.Secondary, (plot.Left + plot.Right) / 2f, plot.Bottom + 20 * s);

            // The numbers beside it
            float sx = area.Right - pad - statsWidth, sy = plot.Top;
            var rows = new List<KeyValuePair<string, string>>
            {
                Row("Gaps", sum.Count.ToString("N0")),
                Row("Mean", Draw.Ms(sum.Mean) + " ms"),
                Row("Std dev", Draw.Ms(sum.StdDev) + " ms"),
                Row("Median", Draw.Ms(sum.Median) + " ms"),
                Row("Shortest", Draw.Ms(sum.Min) + " ms"),
                Row("Longest", Draw.Ms(sum.Max) + " ms"),
                Row("Clicks", r.Counted(leftOnly).Length.ToString("N0"))
            };
            if (outside > 0) rows.Add(Row("Off the scale", outside.ToString("N0")));
            float rowH = 20 * s;
            var box = new RectangleF(sx, sy, statsWidth, rows.Count * rowH + 12 * s);
            using (var border = new Pen(Ink.Grid, 1)) g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            for (int i = 0; i < rows.Count; i++)
            {
                float y = sy + 6 * s + i * rowH;
                Draw.Text(g, rows[i].Key, textFont, Ink.Secondary, sx + 10 * s, y);
                Draw.TextRight(g, rows[i].Value, textFont, Ink.Primary, sx + statsWidth - 10 * s, y);
            }
            return part;
        }

        static KeyValuePair<string, string> Row(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
            string text = null;
            if (bins != null)
                foreach (Part part in parts)
                {
                    if (part.Counts == null || !part.Plot.Contains(p)) continue;
                    int i = (int)((p.X - part.Plot.Left) / (part.Plot.Width / (float)bins.Count));
                    if (i < 0 || i >= part.Counts.Length) break;
                    double from = bins.From + i * bins.Width;
                    text = part.Recording.Name + ": " + Draw.Ms(from) + "–" + Draw.Ms(from + bins.Width) + " ms, " +
                           part.Counts[i].ToString("N0") + (part.Counts[i] == 1 ? " gap" : " gaps");
                    break;
                }
            if (text == tipText) return;
            tipText = text;
            if (text == null) tip.Hide(this);
            else tip.Show(text, this, e.X + 14, e.Y + 18);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            tipText = null;
            tip.Hide(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                textFont.Dispose();
                smallFont.Dispose();
                tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Where the clicks landed: a dot per click over a faded picture of the screen as the recording started. Up to three
    /// recordings at once, each in its colour, with a legend; hovering a dot says which click it was.
    /// </summary>
    sealed class MapView : Control
    {
        public const int MaxShown = 3;

        struct Spot
        {
            public PointF At;
            public Recording Recording;
            public int Number;
            public Click Click;
            public double Gap;
        }

        readonly Font textFont = new Font("Segoe UI", 9F);
        readonly ToolTip tip = new ToolTip { InitialDelay = 0, ReshowDelay = 0, AutoPopDelay = 20000 };
        List<Recording> recordings = new List<Recording>();
        int hidden;
        bool leftOnly = true;
        List<Spot> spots = new List<Spot>();
        string tipText;

        public MapView()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Ink.Surface;
        }

        public void ShowRecordings(List<Recording> shown, bool leftOnly)
        {
            recordings = shown.Take(MaxShown).ToList();
            hidden = Math.Max(0, shown.Count - MaxShown);
            this.leftOnly = leftOnly;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Render(e.Graphics, ClientSize);
        }

        public void Render(Graphics g, Size size)
        {
            g.Clear(Ink.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float s = g.DpiX / 96f, pad = 16 * s;
            var laidOut = new List<Spot>();
            spots = laidOut;
            if (recordings.Count == 0)
            {
                Draw.Text(g, "Tick a recording in Click Recorder to see it here.", textFont, Ink.Secondary, pad, pad);
                return;
            }

            // Legend: a dot in each recording's colour, then its name and count.
            float x = pad;
            foreach (Recording r in recordings)
            {
                string label = r.Name + "  (" + r.Counted(leftOnly).Length.ToString("N0") + " clicks)";
                Draw.Dot(g, new PointF(x + 5 * s, pad + 8 * s), 5 * s, 0, Ink.Series(r.Slot));
                Draw.Text(g, label, textFont, Ink.Primary, x + 15 * s, pad);
                x += 15 * s + TextRenderer.MeasureText(g, label, textFont, Size.Empty, TextFormatFlags.NoPadding).Width + 20 * s;
            }
            if (hidden > 0)
                Draw.Text(g, "Showing the first " + MaxShown + " ticked recordings; untick some to see the others.",
                          textFont, Ink.Secondary, pad, pad + 20 * s);

            // The screen the clicks were on, fitted to the space left.
            Rectangle world = recordings.Select(r => r.ScreenBounds).Where(b => b.Width > 0)
                                        .DefaultIfEmpty(Rectangle.Empty).Aggregate(Rectangle.Union);
            foreach (Recording r in recordings)
                foreach (Click c in r.Counted(leftOnly))
                    if (!world.Contains(c.X, c.Y)) world = Rectangle.Union(world, new Rectangle(c.X, c.Y, 1, 1));
            if (world.Width <= 0 || world.Height <= 0) return;
            var space = RectangleF.FromLTRB(pad, pad + (hidden > 0 ? 44 : 28) * s, size.Width - pad, size.Height - pad);
            if (space.Width < 20 || space.Height < 20) return;
            float scale = Math.Min(space.Width / world.Width, space.Height / world.Height);
            var screen = new RectangleF(space.X + (space.Width - world.Width * scale) / 2, space.Y + (space.Height - world.Height * scale) / 2,
                                        world.Width * scale, world.Height * scale);

            Recording withPicture = recordings.FirstOrDefault(r => r.Screen != null);
            if (withPicture != null)
            {
                Rectangle b = withPicture.ScreenBounds;
                var at = new RectangleF(screen.X + (b.X - world.X) * scale, screen.Y + (b.Y - world.Y) * scale, b.Width * scale, b.Height * scale);
                g.DrawImage(withPicture.Screen, at);
                using (var wash = new SolidBrush(Color.FromArgb(150, Ink.Surface))) g.FillRectangle(wash, at);
            }
            using (var border = new Pen(Ink.Axis, 1)) g.DrawRectangle(border, screen.X, screen.Y, screen.Width, screen.Height);

            // A dot per click, later recordings on top.
            foreach (Recording r in recordings)
            {
                Click[] clicks = r.Counted(leftOnly);
                for (int i = 0; i < clicks.Length; i++)
                {
                    Click c = clicks[i];
                    var at = new PointF(screen.X + (c.X - world.X + 0.5f) * scale, screen.Y + (c.Y - world.Y + 0.5f) * scale);
                    Draw.Dot(g, at, 4 * s, 2 * s, Ink.Series(r.Slot));
                    laidOut.Add(new Spot { At = at, Recording = r, Number = i + 1, Click = c, Gap = i == 0 ? -1 : c.Ms - clicks[i - 1].Ms });
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            float reach = 8 * DeviceScale();
            string text = null;
            double best = reach * reach;
            foreach (Spot spot in spots)
            {
                double dx = spot.At.X - e.X, dy = spot.At.Y - e.Y, d = dx * dx + dy * dy;
                if (d > best) continue;
                best = d;
                text = spot.Recording.Name + ": click " + spot.Number.ToString("N0") + " at " + spot.Click.X + ", " + spot.Click.Y +
                       (spot.Gap >= 0 ? ", " + Draw.Ms(spot.Gap) + " ms after the one before" : ", the first");
            }
            if (text == tipText) return;
            tipText = text;
            if (text == null) tip.Hide(this);
            else tip.Show(text, this, e.X + 14, e.Y + 18);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            tipText = null;
            tip.Hide(this);
        }

        float DeviceScale()
        {
            using (Graphics g = CreateGraphics()) return g.DpiX / 96f;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                textFont.Dispose();
                tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
