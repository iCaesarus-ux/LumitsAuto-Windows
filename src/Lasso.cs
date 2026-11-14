using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace AutoClicker
{
    /// <summary>
    /// A lassoed area: the outline drawn around something, as a closed list of points. A step with one clicks and
    /// watches only inside it, while its box (X, Y, Width, Height) is the outline's bounds.
    /// </summary>
    public static class Lasso
    {
        /// <summary>A hand-drawn outline is thinned out to at most this many points.</summary>
        const int MaxPoints = 200;

        public static bool IsShape(Point[] shape)
        {
            return shape != null && shape.Length >= 3;
        }

        /// <summary>The smallest box holding every point of the outline.</summary>
        public static Rectangle Bounds(Point[] shape)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Point p in shape)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// Whether a spot is inside the outline. Where an outline crosses itself, the overlap of its loops counts as
        /// outside, the same way it's drawn.
        /// </summary>
        public static bool Contains(Point[] shape, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = shape.Length - 1; i < shape.Length; j = i++)
            {
                double xi = shape[i].X, yi = shape[i].Y, xj = shape[j].X, yj = shape[j].Y;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>Inside the outline, with room around it: <paramref name="margin"/> pixels each way are inside too.</summary>
        public static bool ContainsWithRoom(Point[] shape, Point p, int margin)
        {
            return Contains(shape, p.X, p.Y) &&
                   Contains(shape, p.X - margin, p.Y) && Contains(shape, p.X + margin, p.Y) &&
                   Contains(shape, p.X, p.Y - margin) && Contains(shape, p.X, p.Y + margin);
        }

        /// <summary>A moved copy of the outline (copies of a step share their outlines, so they're never changed in place).</summary>
        public static Point[] Offset(Point[] shape, int dx, int dy)
        {
            if (shape == null) return null;
            var moved = new Point[shape.Length];
            for (int i = 0; i < shape.Length; i++) moved[i] = new Point(shape[i].X + dx, shape[i].Y + dy);
            return moved;
        }

        /// <summary>The outline stretched from one box to another, for when a step's box is changed by its numbers.</summary>
        public static Point[] Fit(Point[] shape, Rectangle from, Rectangle to)
        {
            if (shape == null || from == to) return shape;
            double sx = from.Width > 1 ? (to.Width - 1) / (double)(from.Width - 1) : 1;
            double sy = from.Height > 1 ? (to.Height - 1) / (double)(from.Height - 1) : 1;
            var fitted = new Point[shape.Length];
            for (int i = 0; i < shape.Length; i++)
                fitted[i] = new Point(to.X + (int)Math.Round((shape[i].X - from.X) * sx),
                                      to.Y + (int)Math.Round((shape[i].Y - from.Y) * sy));
            return fitted;
        }

        public static bool SameShape(Point[] a, Point[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Turns the points of a drawn outline into a lighter one: points that barely change its shape are left out,
        /// and it keeps at most 200. Null when there aren't enough points for an area.
        /// </summary>
        public static Point[] Simplify(IList<Point> drawn)
        {
            if (drawn == null || drawn.Count < 3) return null;
            var keep = new bool[drawn.Count];
            keep[0] = keep[drawn.Count - 1] = true;
            KeepCorners(drawn, keep, 1.0);

            var kept = new List<Point>();
            for (int i = 0; i < drawn.Count; i++)
                if (keep[i]) kept.Add(drawn[i]);
            if (kept.Count > MaxPoints)
            {
                var even = new List<Point>(MaxPoints);
                for (int k = 0; k < MaxPoints; k++) even.Add(kept[(int)((long)k * kept.Count / MaxPoints)]);
                kept = even;
            }
            return kept.Count >= 3 ? kept.ToArray() : null;
        }

        /// <summary>Marks the points that stick out more than <paramref name="tolerance"/> pixels from the line between their neighbours kept so far.</summary>
        static void KeepCorners(IList<Point> pts, bool[] keep, double tolerance)
        {
            var spans = new Stack<int[]>();
            spans.Push(new[] { 0, pts.Count - 1 });
            while (spans.Count > 0)
            {
                int[] span = spans.Pop();
                int first = span[0], last = span[1];
                if (last - first < 2) continue;
                double farthest = -1;
                int at = -1;
                for (int i = first + 1; i < last; i++)
                {
                    double d = DistanceToSegment(pts[i], pts[first], pts[last]);
                    if (d > farthest)
                    {
                        farthest = d;
                        at = i;
                    }
                }
                if (farthest <= tolerance) continue;
                keep[at] = true;
                spans.Push(new[] { first, at });
                spans.Push(new[] { at, last });
            }
        }

        static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>
        /// Which cells of a snapshot's grid over <paramref name="area"/> lie inside the outline (by their middles).
        /// Null when they all do, or none would, so the whole box is compared.
        /// </summary>
        public static bool[] GridMask(Point[] shape, Rectangle area, int gridWidth, int gridHeight)
        {
            if (!IsShape(shape) || gridWidth <= 0 || gridHeight <= 0 || area.Width <= 0 || area.Height <= 0) return null;
            var mask = new bool[gridWidth * gridHeight];
            int inside = 0;
            for (int gy = 0; gy < gridHeight; gy++)
            {
                double y = area.Y + (gy + 0.5) * area.Height / gridHeight;
                for (int gx = 0; gx < gridWidth; gx++)
                {
                    double x = area.X + (gx + 0.5) * area.Width / gridWidth;
                    if (!Contains(shape, x, y)) continue;
                    mask[gy * gridWidth + gx] = true;
                    inside++;
                }
            }
            return inside == 0 || inside == mask.Length ? null : mask;
        }

        public static GraphicsPath Path(Point[] shape)
        {
            var path = new GraphicsPath(FillMode.Alternate);
            path.AddPolygon(shape);
            return path;
        }

        /// <summary>"x y;x y;…", or "-" for no outline. No commas, since a step's fields are separated by them.</summary>
        public static string Serialize(Point[] shape)
        {
            if (!IsShape(shape)) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < shape.Length; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(shape[i].X.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(shape[i].Y.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Reads <see cref="Serialize"/>'s text; null for "-", nothing, or anything that isn't an outline.</summary>
        public static Point[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text == "-") return null;
            var points = new List<Point>();
            foreach (string pair in text.Split(';'))
            {
                string[] xy = pair.Trim().Split(' ');
                int x, y;
                if (xy.Length != 2 ||
                    !int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                    !int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
                    return null;
                points.Add(new Point(x, y));
            }
            return points.Count >= 3 ? points.ToArray() : null;
        }
    }
}
