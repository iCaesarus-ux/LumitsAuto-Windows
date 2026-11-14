using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// Shows a frozen, dimmed screenshot of every monitor. Dragging a box picks an area (shown
    /// undimmed); a plain click picks an exact spot. Right-click, Esc or Enter finishes.
    /// </summary>
    public class PickOverlay : Form
    {
        static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
        static readonly Color MarkerColor = Color.FromArgb(220, 38, 38);

        readonly Bitmap frame;
        readonly Bitmap dimmed;
        readonly Rectangle virtualScreen;
        readonly IList<ClickPoint> existing;
        readonly bool singleArea;
        readonly List<Rectangle> picks = new List<Rectangle>();
        readonly Font titleFont = new Font("Segoe UI", 15F, FontStyle.Bold);
        readonly Font textFont = new Font("Segoe UI", 10F);
        readonly Font badgeFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);

        bool dragging;
        Point dragStart;
        Rectangle dragRect;
        Rectangle labelRect;
        string labelText = "";
        Rectangle bannerRect;
        bool bannerAtBottom;
        bool wasActivated;
        bool closing;

        /// <param name="frame">Screenshot of the virtual screen; stays owned by the caller.</param>
        /// <param name="singleArea">Pick one area and close (used for choosing a watch area).</param>
        public PickOverlay(Bitmap frame, Rectangle virtualScreen, IList<ClickPoint> existing, bool singleArea)
        {
            this.frame = frame;
            this.virtualScreen = virtualScreen;
            this.existing = existing;
            this.singleArea = singleArea;

            var whole = new Rectangle(Point.Empty, frame.Size);
            dimmed = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dimmed))
            {
                g.DrawImage(frame, whole, whole, GraphicsUnit.Pixel);
                using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(shade, whole);
            }

            Text = "Pick on screen";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        /// <summary>Picked areas in screen coordinates; a 0 × 0 size means an exact spot.</summary>
        public IList<Rectangle> Picks { get { return picks; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = virtualScreen;
            Activate();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            wasActivated = true;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Alt+Tab away (or anything else stealing focus) ends picking rather than leaving the screen covered.
            if (wasActivated && !closing) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragStart = e.Location;
            dragRect = new Rectangle(e.Location, Size.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging && bannerRect.Contains(e.Location))
            {
                // Get the instructions out of the way of whatever is underneath.
                bannerAtBottom = !bannerAtBottom;
                Invalidate();
            }

            Rectangle dirty = Rectangle.Inflate(labelRect, 2, 2);
            if (dragging)
            {
                Rectangle old = dragRect;
                dragRect = FromCorners(dragStart, e.Location);
                dirty = Rectangle.Union(dirty, Rectangle.Inflate(Rectangle.Union(old, dragRect), 6, 6));
                labelText = dragRect.Width + " × " + dragRect.Height;
            }
            else
            {
                Point p = Cursor.Position;
                labelText = p.X + ", " + p.Y;
            }
            labelRect = LabelRectNear(e.Location);
            Invalidate(dirty);
            Invalidate(Rectangle.Inflate(labelRect, 2, 2));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                // Close on release so the button-up doesn't land on the window underneath.
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left || !dragging) return;

            dragging = false;
            Rectangle r = dragRect;
            if (r.Width < 6 || r.Height < 6)
            {
                if (!singleArea) picks.Add(new Rectangle(ToScreen(dragStart), Size.Empty));
            }
            else
            {
                picks.Add(new Rectangle(ToScreen(r.Location), r.Size));
            }
            Invalidate();

            if (singleArea && picks.Count > 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            labelText = "";
            Invalidate(Rectangle.Inflate(labelRect, 2, 2));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Everything, background included, is painted in OnPaint.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);
            foreach (Rectangle area in BrightAreas())
            {
                Rectangle part = Rectangle.Intersect(area, clip);
                if (part.Width > 0 && part.Height > 0) g.DrawImage(frame, part, part, GraphicsUnit.Pixel);
            }

            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = g.DpiX / 96f;

            for (int i = 0; i < existing.Count; i++)
            {
                ClickPoint p = existing[i];
                DrawStep(g, ToClient(p.IsArea ? p.Area : new Rectangle(p.X, p.Y, 0, 0)), i + 1, false, s);
            }
            if (!singleArea)
                for (int i = 0; i < picks.Count; i++) DrawStep(g, ToClient(picks[i]), existing.Count + i + 1, true, s);

            if (dragging && dragRect.Width > 0 && dragRect.Height > 0)
                using (var pen = new Pen(AccentColor, 2 * s)) g.DrawRectangle(pen, dragRect);

            DrawBanner(g, s);

            if (labelText.Length > 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(24, 24, 27))) g.FillRectangle(b, labelRect);
                TextRenderer.DrawText(g, labelText, textFont, labelRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        IEnumerable<Rectangle> BrightAreas()
        {
            foreach (Rectangle r in picks)
                if (r.Width > 0) yield return ToClient(r);
            if (dragging && dragRect.Width > 0 && dragRect.Height > 0) yield return dragRect;
        }

        void DrawStep(Graphics g, Rectangle r, int number, bool isNew, float s)
        {
            Color color = isNew ? AccentColor : MarkerColor;
            PointF badgeCenter;
            if (r.Width > 0 && r.Height > 0)
            {
                using (var pen = new Pen(isNew ? AccentColor : Color.FromArgb(235, 255, 255, 255), 2 * s))
                {
                    if (!isNew) pen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen, r);
                }
                badgeCenter = new PointF(r.Right, r.Top);
            }
            else
            {
                float rad = 11 * s;
                using (var ring = new Pen(color, 3 * s)) g.DrawEllipse(ring, r.X - rad, r.Y - rad, rad * 2, rad * 2);
                g.FillEllipse(Brushes.White, r.X - 2 * s, r.Y - 2 * s, 4 * s, 4 * s);
                badgeCenter = new PointF(r.X + rad, r.Y - rad);
            }

            string label = number.ToString();
            Size ls = TextRenderer.MeasureText(label, badgeFont, Size.Empty, TextFormatFlags.NoPadding);
            int bh = ls.Height + (int)(4 * s);
            int bw = Math.Max(ls.Width + (int)(10 * s), bh);
            var badge = new Rectangle((int)(badgeCenter.X - bw / 2f), (int)(badgeCenter.Y - bh / 2f), bw, bh);
            using (GraphicsPath path = RoundedRect(badge, bh / 2))
            using (var b = new SolidBrush(color))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, label, badgeFont, badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        void DrawBanner(Graphics g, float s)
        {
            Rectangle primary = ToClient(Screen.PrimaryScreen.Bounds);
            string title, hint;
            if (singleArea)
            {
                title = "Drag a box over something that shows this screen is ready";
                hint = "Small, steady areas work best, like a button's text   •   Esc to cancel";
            }
            else
            {
                int count = picks.Count;
                title = "Drag a box over a button to click a random spot inside it";
                hint = "Or click once for an exact spot   •   Right-click or Esc when done   •   " +
                       count + (count == 1 ? " step added" : " steps added");
            }

            Size ts = TextRenderer.MeasureText(title, titleFont);
            Size hs = TextRenderer.MeasureText(hint, textFont);
            int pad = (int)(16 * s);
            int w = Math.Max(ts.Width, hs.Width) + pad * 2;
            int h = ts.Height + hs.Height + pad * 2;
            int margin = (int)(40 * s);
            int y = bannerAtBottom ? primary.Bottom - h - margin : primary.Top + margin;
            bannerRect = new Rectangle(primary.Left + (primary.Width - w) / 2, y, w, h);

            using (GraphicsPath path = RoundedRect(bannerRect, (int)(10 * s)))
            using (var b = new SolidBrush(AccentColor))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, title, titleFont, new Point(bannerRect.Left + (w - ts.Width) / 2, bannerRect.Top + pad), Color.White);
            TextRenderer.DrawText(g, hint, textFont, new Point(bannerRect.Left + (w - hs.Width) / 2, bannerRect.Top + pad + ts.Height), Color.White);
        }

        Rectangle LabelRectNear(Point p)
        {
            Size size = TextRenderer.MeasureText(labelText.Length > 0 ? labelText : "0, 0", textFont);
            int offset = size.Height;
            var r = new Rectangle(p.X + offset, p.Y + offset, size.Width + size.Height / 2, size.Height + 6);
            if (r.Right > ClientSize.Width) r.X = p.X - offset - r.Width;
            if (r.Bottom > ClientSize.Height) r.Y = p.Y - offset - r.Height;
            return r;
        }

        Rectangle ToClient(Rectangle screen)
        {
            return new Rectangle(screen.X - virtualScreen.X, screen.Y - virtualScreen.Y, screen.Width, screen.Height);
        }

        Point ToScreen(Point client)
        {
            return new Point(client.X + virtualScreen.X, client.Y + virtualScreen.Y);
        }

        static Rectangle FromCorners(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dimmed.Dispose();
                titleFont.Dispose();
                textFont.Dispose();
                badgeFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
