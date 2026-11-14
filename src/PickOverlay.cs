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
    /// undimmed); a plain click picks an exact spot. Where a lasso is allowed, L switches between
    /// dragging a box and drawing around something. Right-click, Esc or Enter finishes.
    /// </summary>
    public class PickOverlay : Form
    {
        static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
        static readonly Color MarkerColor = Color.FromArgb(220, 38, 38);

        /// <summary>Whether the last picker was left drawing lassos, so the next one starts that way.</summary>
        static bool lassoMode;

        readonly Bitmap frame;
        readonly Bitmap dimmed;
        readonly Rectangle virtualScreen;
        readonly IList<ClickPoint> existing;
        readonly bool singleArea;
        readonly bool allowLasso;
        readonly bool allowSpot;
        readonly List<Rectangle> picks = new List<Rectangle>();
        readonly List<Point[]> shapes = new List<Point[]>();
        readonly List<Point> lassoPoints = new List<Point>();
        readonly Font titleFont = new Font("Segoe UI", 15F, FontStyle.Bold);
        readonly Font textFont = new Font("Segoe UI", 10F);
        readonly Font badgeFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);

        bool dragging;
        bool lassoing;
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
            : this(frame, virtualScreen, existing, singleArea, false, !singleArea)
        {
        }

        /// <param name="allowLasso">L switches to drawing around something instead of dragging a box.</param>
        /// <param name="allowSpot">A plain click picks an exact spot (always so when picking several).</param>
        public PickOverlay(Bitmap frame, Rectangle virtualScreen, IList<ClickPoint> existing, bool singleArea, bool allowLasso, bool allowSpot)
        {
            this.frame = frame;
            this.virtualScreen = virtualScreen;
            this.existing = existing;
            this.singleArea = singleArea;
            this.allowLasso = allowLasso;
            this.allowSpot = allowSpot || !singleArea;

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

        /// <summary>Picked areas in screen coordinates; a 0 × 0 size means an exact spot. A lassoed one is its outline's bounds.</summary>
        public IList<Rectangle> Picks { get { return picks; } }

        /// <summary>For each pick, its lasso outline in screen coordinates, or null for a box or a spot.</summary>
        public IList<Point[]> Shapes { get { return shapes; } }

        bool LassoOn { get { return allowLasso && lassoMode; } }

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
            else if (e.KeyCode == Keys.L && allowLasso && !dragging)
            {
                lassoMode = !lassoMode;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            lassoing = LassoOn;
            dragStart = e.Location;
            dragRect = new Rectangle(e.Location, Size.Empty);
            lassoPoints.Clear();
            if (lassoing) lassoPoints.Add(e.Location);
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
                if (lassoing)
                {
                    Point last = lassoPoints[lassoPoints.Count - 1];
                    if (Math.Abs(e.X - last.X) + Math.Abs(e.Y - last.Y) >= 2) lassoPoints.Add(e.Location);
                    dragRect = BoundsOf(lassoPoints);
                }
                else
                {
                    dragRect = FromCorners(dragStart, e.Location);
                }
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
            Point[] shape = lassoing ? Lasso.Simplify(lassoPoints) : null;
            Rectangle r = shape != null ? Lasso.Bounds(shape) : lassoing ? Rectangle.Empty : dragRect;
            lassoing = false;
            lassoPoints.Clear();
            if (r.Width < 6 || r.Height < 6)
            {
                if (allowSpot) Add(new Rectangle(ToScreen(dragStart), Size.Empty), null);
            }
            else
            {
                Add(new Rectangle(ToScreen(r.Location), r.Size), shape == null ? null : Lasso.Offset(shape, virtualScreen.X, virtualScreen.Y));
            }
            Invalidate();

            if (singleArea && picks.Count > 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        void Add(Rectangle pick, Point[] shape)
        {
            picks.Add(pick);
            shapes.Add(shape);
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
            for (int i = 0; i < picks.Count; i++)
                if (picks[i].Width > 0) DrawBright(g, clip, ToClient(picks[i]), shapes[i] == null ? null : ToClient(shapes[i]));
            if (dragging && dragRect.Width > 0 && dragRect.Height > 0)
                DrawBright(g, clip, dragRect, lassoing && lassoPoints.Count >= 3 ? lassoPoints.ToArray() : null);

            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = g.DpiX / 96f;

            for (int i = 0; i < existing.Count; i++)
            {
                ClickPoint p = existing[i];
                Rectangle r = ToClient(p.IsArea ? p.Area : new Rectangle(p.X, p.Y, 0, 0));
                DrawStep(g, r, p.IsLasso ? ToClient(p.Shape) : null, i + 1, false, s);
            }
            if (!singleArea)
                for (int i = 0; i < picks.Count; i++)
                    DrawStep(g, ToClient(picks[i]), shapes[i] == null ? null : ToClient(shapes[i]), existing.Count + i + 1, true, s);

            if (dragging && lassoing && lassoPoints.Count >= 2)
            {
                using (var pen = new Pen(AccentColor, 2 * s)) g.DrawLines(pen, lassoPoints.ToArray());
                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1 * s) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, lassoPoints[lassoPoints.Count - 1], lassoPoints[0]);
            }
            else if (dragging && dragRect.Width > 0 && dragRect.Height > 0)
            {
                using (var pen = new Pen(AccentColor, 2 * s)) g.DrawRectangle(pen, dragRect);
            }

            DrawBanner(g, s);

            if (labelText.Length > 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(24, 24, 27))) g.FillRectangle(b, labelRect);
                TextRenderer.DrawText(g, labelText, textFont, labelRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        /// <summary>Shows a picked area undimmed: its whole box, or only inside its lasso.</summary>
        void DrawBright(Graphics g, Rectangle clip, Rectangle area, Point[] shape)
        {
            Rectangle part = Rectangle.Intersect(area, clip);
            if (part.Width <= 0 || part.Height <= 0) return;
            if (shape == null)
            {
                g.DrawImage(frame, part, part, GraphicsUnit.Pixel);
                return;
            }
            using (GraphicsPath path = Lasso.Path(shape))
            {
                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                g.DrawImage(frame, part, part, GraphicsUnit.Pixel);
                g.Restore(state);
            }
        }

        void DrawStep(Graphics g, Rectangle r, Point[] shape, int number, bool isNew, float s)
        {
            Color color = isNew ? AccentColor : MarkerColor;
            PointF badgeCenter;
            if (r.Width > 0 && r.Height > 0)
            {
                using (var pen = new Pen(isNew ? AccentColor : Color.FromArgb(235, 255, 255, 255), 2 * s))
                {
                    if (!isNew) pen.DashStyle = DashStyle.Dash;
                    if (shape != null) g.DrawPolygon(pen, shape);
                    else g.DrawRectangle(pen, r);
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
            bool lasso = LassoOn;
            string title, hint;
            if (singleArea && !allowSpot)
            {
                title = lasso ? "Draw around something that shows this screen is ready"
                              : "Drag a box over something that shows this screen is ready";
                hint = "Small, steady areas work best, like a button's text   •   Esc to cancel";
            }
            else if (singleArea)
            {
                title = lasso ? "Draw around the button to click a random spot inside it"
                              : "Drag a box over the button to click a random spot inside it";
                hint = "Or click once for an exact spot   •   Esc to cancel";
            }
            else
            {
                int count = picks.Count;
                title = lasso ? "Draw around a button to click a random spot inside it"
                              : "Drag a box over a button to click a random spot inside it";
                hint = "Or click once for an exact spot   •   Right-click or Esc when done   •   " +
                       count + (count == 1 ? " step added" : " steps added");
            }
            if (allowLasso) hint += lasso ? "   •   L for a box" : "   •   L to lasso instead";

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

        Point[] ToClient(Point[] screen)
        {
            return Lasso.Offset(screen, -virtualScreen.X, -virtualScreen.Y);
        }

        Point ToScreen(Point client)
        {
            return new Point(client.X + virtualScreen.X, client.Y + virtualScreen.Y);
        }

        static Rectangle FromCorners(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        static Rectangle BoundsOf(List<Point> points)
        {
            return points.Count == 0 ? Rectangle.Empty : Lasso.Bounds(points.ToArray());
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
