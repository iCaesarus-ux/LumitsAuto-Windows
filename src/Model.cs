using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace AutoClicker
{
    public enum MouseButton { Left, Right, Middle }

    /// <summary>What a waiting step does when its screen doesn't show up in time.</summary>
    public enum TimeoutAction { Stop, Skip, ClickAnyway }

    /// <summary>One step of the sequence: where to click, and optionally what to wait for first.</summary>
    public class ClickPoint
    {
        public int X;
        public int Y;

        /// <summary>Click area size; 0 × 0 means the exact spot X, Y.</summary>
        public int Width;
        public int Height;

        public MouseButton Button = MouseButton.Left;
        public bool DoubleClick;

        /// <summary>Wait after this click in milliseconds; -1 means use the main interval.</summary>
        public int DelayMs = -1;

        public bool WaitForScreen;

        /// <summary>Screen area compared with <see cref="Snapshot"/> before clicking.</summary>
        public Rectangle WatchArea;
        public Snapshot Snapshot;
        public int MatchPercent = 80;

        /// <summary>Random wait between the screen appearing and the click.</summary>
        public int ReactMinMs = 300;
        public int ReactMaxMs = 900;

        public int TimeoutSec = 30;
        public TimeoutAction OnTimeout = TimeoutAction.Stop;

        /// <summary>
        /// Background mode: park the real pointer on the spot during this click. Needed where the game
        /// asks Windows for the pointer position instead of reading the click (e.g. clicks on its world map).
        /// </summary>
        public bool BorrowPointer;

        /// <summary>A lassoed click area's outline, in the same coordinates as X and Y; null for a box. X, Y, Width and Height are its bounds.</summary>
        public Point[] Shape;

        /// <summary>A lassoed watch area's outline: only the snapshot inside it is compared. Null compares all of <see cref="WatchArea"/>.</summary>
        public Point[] WatchShape;

        // The watch outline's cells in the snapshot grid, kept until the outline, area or snapshot changes.
        bool[] watchMask;
        Point[] maskShape;
        Rectangle maskArea;
        Snapshot maskSnapshot;

        public bool IsArea { get { return Width > 0 && Height > 0; } }

        public Rectangle Area { get { return new Rectangle(X, Y, Width, Height); } }

        /// <summary>The click area is a lasso rather than a box.</summary>
        public bool IsLasso { get { return IsArea && Lasso.IsShape(Shape); } }

        /// <summary>The step watches exactly what it clicks.</summary>
        public bool WatchesClickArea
        {
            get { return IsArea && WatchArea == Area && Lasso.SameShape(WatchShape, IsLasso ? Shape : null); }
        }

        /// <summary>Which cells of the snapshot to compare: those inside the watch outline. Null compares them all.</summary>
        public bool[] WatchMask()
        {
            if (Snapshot == null || !Lasso.IsShape(WatchShape)) return null;
            if (!ReferenceEquals(maskShape, WatchShape) || maskArea != WatchArea || !ReferenceEquals(maskSnapshot, Snapshot))
            {
                watchMask = Lasso.GridMask(WatchShape, WatchArea, Snapshot.Width, Snapshot.Height);
                maskShape = WatchShape;
                maskArea = WatchArea;
                maskSnapshot = Snapshot;
            }
            return watchMask;
        }

        /// <summary>Moves both outlines with the step.</summary>
        public void OffsetShapes(int dx, int dy)
        {
            Shape = Lasso.Offset(Shape, dx, dy);
            WatchShape = Lasso.Offset(WatchShape, dx, dy);
        }

        /// <summary>True when the step is set to wait and has everything it needs to do so.</summary>
        public bool Waits
        {
            get { return WaitForScreen && Snapshot != null && WatchArea.Width > 0 && WatchArea.Height > 0; }
        }

        public ClickPoint Clone()
        {
            return (ClickPoint)MemberwiseClone(); // Snapshot is immutable, so sharing it is fine
        }

        public string Serialize()
        {
            var parts = new List<string>
            {
                Num(X), Num(Y), Button.ToString(), DoubleClick ? "1" : "0", Num(DelayMs),
                Num(Width), Num(Height),
                WaitForScreen ? "1" : "0",
                Num(WatchArea.X), Num(WatchArea.Y), Num(WatchArea.Width), Num(WatchArea.Height),
                Num(MatchPercent), Num(ReactMinMs), Num(ReactMaxMs), Num(TimeoutSec), OnTimeout.ToString(),
                Snapshot == null ? "-" : Snapshot.Serialize(),
                BorrowPointer ? "1" : "0",
                Lasso.Serialize(IsLasso ? Shape : null),
                Lasso.Serialize(WatchShape)
            };
            return string.Join(",", parts);
        }

        /// <summary>Parses the output of <see cref="Serialize"/> (or the older 5-field form); returns null for malformed text.</summary>
        public static ClickPoint Parse(string text)
        {
            string[] parts = text.Split(',');
            if (parts.Length < 2) return null;

            var p = new ClickPoint();
            if (!TryInt(parts, 0, out p.X) || !TryInt(parts, 1, out p.Y)) return null;

            MouseButton button;
            if (parts.Length > 2 && Enum.TryParse(parts[2].Trim(), true, out button) && Enum.IsDefined(typeof(MouseButton), button))
                p.Button = button;
            p.DoubleClick = Text(parts, 3) == "1";
            int delay = Int(parts, 4, -1);
            p.DelayMs = delay < 0 ? -1 : delay;

            p.Width = Math.Max(0, Int(parts, 5, 0));
            p.Height = Math.Max(0, Int(parts, 6, 0));
            p.WaitForScreen = Text(parts, 7) == "1";
            p.WatchArea = new Rectangle(Int(parts, 8, 0), Int(parts, 9, 0), Math.Max(0, Int(parts, 10, 0)), Math.Max(0, Int(parts, 11, 0)));
            p.MatchPercent = Math.Max(1, Math.Min(100, Int(parts, 12, p.MatchPercent)));
            p.ReactMinMs = Math.Max(0, Int(parts, 13, p.ReactMinMs));
            p.ReactMaxMs = Math.Max(p.ReactMinMs, Int(parts, 14, p.ReactMaxMs));
            p.TimeoutSec = Math.Max(1, Int(parts, 15, p.TimeoutSec));
            TimeoutAction onTimeout;
            if (parts.Length > 16 && Enum.TryParse(parts[16].Trim(), true, out onTimeout) && Enum.IsDefined(typeof(TimeoutAction), onTimeout))
                p.OnTimeout = onTimeout;
            if (parts.Length > 17) p.Snapshot = Snapshot.Parse(parts[17].Trim());
            p.BorrowPointer = Text(parts, 18) == "1";
            p.Shape = p.IsArea ? Lasso.Parse(Text(parts, 19)) : null;
            p.WatchShape = Lasso.Parse(Text(parts, 20));
            return p;
        }

        static string Num(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        static string Text(string[] parts, int i) { return parts.Length > i ? parts[i].Trim() : ""; }

        static bool TryInt(string[] parts, int i, out int value)
        {
            value = 0;
            return parts.Length > i && int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        static int Int(string[] parts, int i, int fallback)
        {
            int v;
            return TryInt(parts, i, out v) ? v : fallback;
        }
    }

    public class RunOptions
    {
        /// <summary>Steps run in order. Empty means "click wherever the mouse is".</summary>
        public ClickPoint[] Points;
        /// <summary>What the steps' positions are measured from.</summary>
        public StepSpace Space = StepSpace.WholeScreen;
        /// <summary>Send mouse messages straight to the game window instead of moving the real pointer.</summary>
        public bool Background;
        /// <summary>Send mouse input through the Pico HID bridge, so Windows sees a real mouse.</summary>
        public bool Hid;
        public int IntervalMs;
        /// <summary>Each wait varies by up to this many percent either way; 0 = exact timing.</summary>
        public int RandomPercent;
        /// <summary>Exact-spot clicks land up to this many pixels off; 0 = exact position.</summary>
        public int JitterPx;
        /// <summary>Passes through the list before stopping; 0 = until stopped.</summary>
        public int Cycles;

        /// <summary>Curved, eased mouse movement and realistic press/release timing instead of instant jumps.</summary>
        public bool HumanMouse;
        public bool StopOnUserMouse;
        /// <summary>While waiting for a step, click the previous step again if its screen comes back.</summary>
        public bool RedoPrevious;

        /// <summary>Average time between breaks; 0 = no breaks.</summary>
        public int BreakEveryMs;
        public int BreakMinMs;
        public int BreakMaxMs;

        /// <summary>Index of the step (the attack) before which the enemy's name card is looked at; -1 = never.</summary>
        public int NameStep = -1;
        /// <summary>Where the enemy's name is, in step coordinates.</summary>
        public Rectangle NameArea;
        /// <summary>Stop before attacking an enemy whose name is gold.</summary>
        public bool RadiantCheck;
        public double AlertGoldPercent = 2;
        /// <summary>Counts each battle's lumit by name, and holds the catch rules; null = no counting.</summary>
        public LumitCounter Counter;
        /// <summary>Catching and healing; null = off.</summary>
        public CatchOptions Catch;
        /// <summary>
        /// Where a 2v2's bars and wild names are in the game window, so the radiant check sees 2v2s even with catching off
        /// (the 1v1 name area misses both of a PC 2v2's name cards); null without a game window.
        /// </summary>
        public Dictionary<string, Rectangle> DuoSpots;
    }

    public class RunResult
    {
        public long Clicks;
        public int Cycles;
        /// <summary>True when every requested cycle ran.</summary>
        public bool Completed;
        /// <summary>Why the run stopped on its own, or null when it finished or the user stopped it.</summary>
        public string Problem;
        /// <summary>The run stopped because a radiant's gold name showed up.</summary>
        public bool RadiantFound;
        /// <summary>The radiant's name as read off its card, or "" if it couldn't be read.</summary>
        public string RadiantName = "";
        /// <summary>Where it is, for one in a 2v2 ("in a 2v2, the left one (upper card)"); "" for a 1v1.</summary>
        public string RadiantPlace = "";
        /// <summary>It stopped on its own for a reason worth sending to your phone.</summary>
        public bool NotifyPhone;
        /// <summary>A stop you asked to always hear about (Infibex fainted, out of Bark Tea): the phone alert's title; null otherwise.</summary>
        public string AlertTitle;
        /// <summary>JPEG of the game when the run stopped on its own.</summary>
        public byte[] Screenshot;
    }
}
