using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace AutoClicker
{
    /// <summary>
    /// Mouse movement that looks like a hand on a mouse: a gentle curve, speeding up and slowing
    /// down, taking longer for longer moves and smaller targets, and now and then overshooting
    /// slightly and correcting.
    /// </summary>
    public static class HumanMouse
    {
        const int TickMs = 7;

        public struct Sample
        {
            public readonly int Ms;
            public readonly Point Pos;

            public Sample(int ms, Point pos)
            {
                Ms = ms;
                Pos = pos;
            }
        }

        /// <summary>
        /// Plays a planned path in real time, handing each position to <paramref name="moveTo"/>
        /// (the real cursor, or messages to a game window). Returns false if <paramref name="stop"/> was signalled.
        /// </summary>
        public static bool Play(List<Sample> path, Action<Point> moveTo, WaitHandle stop)
        {
            var clock = Stopwatch.StartNew();
            foreach (Sample s in path)
            {
                long wait = s.Ms - clock.ElapsedMilliseconds;
                if (wait > 0 && stop.WaitOne((int)wait)) return false;
                moveTo(s.Pos);
            }
            return true;
        }

        /// <summary>The cursor positions of a move and when (ms from the start) to show each; the last one is exactly <paramref name="to"/>.</summary>
        public static List<Sample> Plan(Point from, Point to, int targetSize, Random rnd)
        {
            var path = new List<Sample>();
            double dist = Distance(from, to);
            if (dist < 1)
            {
                path.Add(new Sample(0, to));
                return path;
            }

            // Fitts's law: longer moves and smaller targets take longer.
            double ms = (90 + 115 * Math.Log(dist / Math.Max(12, targetSize) + 1, 2)) * (0.85 + 0.35 * rnd.NextDouble());
            ms = Math.Max(70, Math.Min(1400, ms));

            if (dist > 250 && rnd.NextDouble() < 0.25)
            {
                // Overshoot a little past the target, pause, then correct back onto it.
                double ux = (to.X - from.X) / dist, uy = (to.Y - from.Y) / dist;
                double over = dist * (0.025 + 0.045 * rnd.NextDouble());
                double side = (rnd.NextDouble() - 0.5) * over * 0.6;
                var past = new Point((int)Math.Round(to.X + ux * over - uy * side), (int)Math.Round(to.Y + uy * over + ux * side));
                int reached = AddSegment(path, from, past, 0, ms * 0.9, rnd);
                AddSegment(path, past, to, reached + rnd.Next(25, 70), 90 + rnd.Next(0, 90), rnd);
            }
            else
            {
                AddSegment(path, from, to, 0, ms, rnd);
            }
            return path;
        }

        static int AddSegment(List<Sample> path, Point a, Point b, int startMs, double ms, Random rnd)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int endMs = startMs + (int)Math.Round(ms);
            if (len < 1)
            {
                path.Add(new Sample(endMs, b));
                return endMs;
            }

            // A cubic Bézier bowed to one side of the straight line.
            double nx = -dy / len, ny = dx / len;
            double bend = len * (0.04 + 0.14 * rnd.NextDouble()) * (rnd.Next(2) == 0 ? -1 : 1);
            double k1 = bend * (0.7 + 0.6 * rnd.NextDouble());
            double k2 = bend * (0.4 + 0.5 * rnd.NextDouble());
            double c1x = a.X + dx * 0.3 + nx * k1, c1y = a.Y + dy * 0.3 + ny * k1;
            double c2x = a.X + dx * 0.7 + nx * k2, c2y = a.Y + dy * 0.7 + ny * k2;

            Point last = a;
            for (double t = TickMs; t < ms; t += TickMs)
            {
                double u = t / ms;
                double s = u * u * u * (10 - 15 * u + 6 * u * u); // minimum-jerk easing: smooth start and stop
                double r = 1 - s;
                double x = r * r * r * a.X + 3 * r * r * s * c1x + 3 * r * s * s * c2x + s * s * s * b.X;
                double y = r * r * r * a.Y + 3 * r * r * s * c1y + 3 * r * s * s * c2y + s * s * s * b.Y;
                var p = new Point((int)Math.Round(x), (int)Math.Round(y));
                if (p != last)
                {
                    path.Add(new Sample(startMs + (int)t, p));
                    last = p;
                }
            }
            path.Add(new Sample(endMs, b));
            return endMs;
        }

        static double Distance(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
