using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClickRecorder
{
    /// <summary>One mouse button press.</summary>
    sealed class Click
    {
        /// <summary>Milliseconds since its recording started.</summary>
        public double Ms;
        public int X, Y;
        public MouseButtons Button;
    }

    /// <summary>The presses between one Start and Stop, kept in memory with a picture of the screen as it started.</summary>
    sealed class Recording
    {
        public string Name;
        /// <summary>Its colour, kept for good: 0–7 in the order recordings were made, -1 past the eighth.</summary>
        public int Slot;
        public DateTime Started;
        public double LengthMs;
        public readonly List<Click> Clicks = new List<Click>();
        /// <summary>The screen when it started, shrunk to at most 1920 wide; null if it couldn't be taken.</summary>
        public Bitmap Screen;
        /// <summary>Where that picture was on the screen (every monitor, in screen pixels).</summary>
        public Rectangle ScreenBounds;
        /// <summary>Shown in the graphs.</summary>
        public bool Shown = true;

        /// <summary>A copy of the clicks, safe to read while presses are still being added.</summary>
        public Click[] Snapshot()
        {
            lock (Clicks) return Clicks.ToArray();
        }

        public void Add(Click c)
        {
            lock (Clicks) Clicks.Add(c);
        }

        public int Count
        {
            get { lock (Clicks) return Clicks.Count; }
        }

        /// <summary>The clicks that count: all of them, or only left presses.</summary>
        public Click[] Counted(bool leftOnly)
        {
            Click[] all = Snapshot();
            return leftOnly ? all.Where(c => c.Button == MouseButtons.Left).ToArray() : all;
        }

        /// <summary>Milliseconds from each counted click to the next.</summary>
        public double[] Gaps(bool leftOnly)
        {
            Click[] c = Counted(leftOnly);
            var gaps = new double[Math.Max(0, c.Length - 1)];
            for (int i = 1; i < c.Length; i++) gaps[i - 1] = c[i].Ms - c[i - 1].Ms;
            return gaps;
        }
    }

    /// <summary>Mean, spread and middle of a set of gaps.</summary>
    sealed class Summary
    {
        public int Count;
        public double Mean, StdDev, Median, Min, Max;

        public static Summary Of(double[] values)
        {
            var s = new Summary { Count = values.Length };
            if (values.Length == 0) return s;
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            s.Mean = sorted.Average();
            double mean = s.Mean;
            s.StdDev = Math.Sqrt(sorted.Sum(v => (v - mean) * (v - mean)) / sorted.Length);
            s.Median = Percentile(sorted, 0.5);
            s.Min = sorted[0];
            s.Max = sorted[sorted.Length - 1];
            return s;
        }

        /// <summary>The value below which a share <paramref name="p"/> of <paramref name="sorted"/> lies, between neighbours.</summary>
        public static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            double at = p * (sorted.Length - 1);
            int i = (int)Math.Floor(at);
            if (i >= sorted.Length - 1) return sorted[sorted.Length - 1];
            return sorted[i] + (sorted[i + 1] - sorted[i]) * (at - i);
        }
    }

    static class Csv
    {
        /// <summary>Every click of the given recordings, one per line, with the gap since the one before (all buttons).</summary>
        public static string Of(IEnumerable<Recording> recordings)
        {
            var sb = new StringBuilder("recording,click,time_ms,gap_ms,x,y,button\r\n");
            CultureInfo inv = CultureInfo.InvariantCulture;
            foreach (Recording r in recordings)
            {
                Click[] clicks = r.Snapshot();
                for (int i = 0; i < clicks.Length; i++)
                {
                    Click c = clicks[i];
                    sb.Append(Quote(r.Name)).Append(',')
                      .Append(i + 1).Append(',')
                      .Append(c.Ms.ToString("0.0", inv)).Append(',')
                      .Append(i == 0 ? "" : (c.Ms - clicks[i - 1].Ms).ToString("0.0", inv)).Append(',')
                      .Append(c.X).Append(',').Append(c.Y).Append(',')
                      .Append(c.Button).Append("\r\n");
                }
            }
            return sb.ToString();
        }

        static string Quote(string s)
        {
            return s.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
