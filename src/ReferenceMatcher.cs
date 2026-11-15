// C# 5 / .NET Framework 4.8 version.
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace ShapeFinder
{
    public sealed class Candidate
    {
        public Candidate(DetectedShape shape, double score, bool sameShape, bool sameFill, double geometryDiff)
        {
            Shape = shape; Score = score; SameShape = sameShape; SameFill = sameFill; GeometryDiff = geometryDiff;
        }
        public DetectedShape Shape { get; private set; }
        public double Score { get; private set; }
        public bool SameShape { get; private set; }
        public bool SameFill { get; private set; }
        public double GeometryDiff { get; private set; }
    }

    public sealed class MatchResult
    {
        public DetectedShape Reference { get; set; }
        public DetectedShape Panel { get; set; }
        public string Corner { get; set; }
        public List<Candidate> Ranking { get; set; }     // best first
        public Candidate Best { get { return Ranking[0]; } }
    }

    /// <summary>
    /// Finds the reference shape (the shape lying on a square/rectangular panel in one of the four corners)
    /// and ranks every other front shape by how close it is to it.
    /// Ranking: same shape first, then same filled/hollow, then the smallest geometry difference.
    /// Size, rotation and colour are ignored.
    /// </summary>
    public static class ReferenceMatcher
    {
        const double CornerMargin = 0.15;   // panel corner must be within 15% of the shorter image side from an image corner

        public static MatchResult Find(List<DetectedShape> shapes, int width, int height)
        {
            DetectedShape reference = null, panel = null;
            string corner = null;

            foreach (var p in shapes)
            {
                if (p.Shape != "square" && p.Shape != "rectangle") continue;
                string c = CornerOf(p.Box, width, height);
                if (c == null) continue;
                var onPanel = shapes.Where(s => s.Front && s.LiesOn.Contains(p.Id)).ToList();
                if (onPanel.Count == 0) continue;
                var cand = onPanel.OrderByDescending(s => s.Area).First();
                if (reference == null || cand.Area > reference.Area)
                {
                    reference = cand; panel = p; corner = c;
                }
            }
            if (reference == null) return null;

            var refShape = reference;
            var ranking = shapes
                .Where(s => s.Front && s != refShape)
                .Select(s => Score(refShape, s))
                .OrderBy(s => s.Score)
                .ToList();
            if (ranking.Count == 0) return null;

            return new MatchResult { Reference = reference, Panel = panel, Corner = corner, Ranking = ranking };
        }

        static string CornerOf(Rect box, int w, int h)
        {
            string[] names = { "top-left", "top-right", "bottom-left", "bottom-right" };
            double[] px = { box.X, box.X + box.Width, box.X, box.X + box.Width };
            double[] py = { box.Y, box.Y, box.Y + box.Height, box.Y + box.Height };
            double[] cx = { 0, w, 0, w };
            double[] cy = { 0, 0, h, h };
            int best = 0;
            double bestD = double.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                double d = Math.Sqrt((px[k] - cx[k]) * (px[k] - cx[k]) + (py[k] - cy[k]) * (py[k] - cy[k]));
                if (d < bestD) { bestD = d; best = k; }
            }
            return bestD < CornerMargin * Math.Min(w, h) ? names[best] : null;
        }

        static Candidate Score(DetectedShape reference, DetectedShape o)
        {
            bool sameShape = o.Shape == reference.Shape;
            bool sameFill = o.Hollow == reference.Hollow;
            double geo = GeometryDiff(reference, o);
            double score = (sameShape ? 0 : 2) + (sameFill ? 0 : 1) + Math.Min(geo, 3) / 3 * 0.99;
            return new Candidate(o, score, sameShape, sameFill, geo);
        }

        /// RMS of normalised differences in outline geometry (scale- and rotation-free)
        static double GeometryDiff(DetectedShape a, DetectedShape b)
        {
            double[] d =
            {
                (a.Solidity - b.Solidity) / 0.10,
                (a.RoundnessError - b.RoundnessError) / 0.10,
                (a.FilledShare - b.FilledShare) / 0.20,
                (Math.Min(a.Notches, 5) - Math.Min(b.Notches, 5)) / 1.0,
                (a.RectFit - b.RectFit) / 0.15
            };
            return Math.Sqrt(d.Average(v => v * v));
        }
    }
}
