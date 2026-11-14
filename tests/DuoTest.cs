using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// Plays 2v2 fights against the real ClickEngine, in a pretend game window placed off-screen. The screens are real
    /// screenshots of the phone, enlarged to a 1822 × 1025 window (the phone's shape), with the HP bars painted in. It
    /// reads them with the app's own text recognition, so "Choose an action for Lacerath." and the caught names are real.
    /// DuoTest folderOfPhoneScreenshots
    /// </summary>
    static class DuoTest
    {
        const int W = 1822, H = 1025;
        static Bitmap leadMenu, partnerMenu, attackTarget, captureTarget, waiting, caughtA, caughtB, victory, map;
        static int failures;
        static string tempDir;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        static Bitmap Load(string dir, string name)
        {
            using (var phone = new Bitmap(Path.Combine(dir, "6215183_1102" + name + ".png")))
            {
                var big = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(phone, 0, 0, W, H);
                }
                return big;
            }
        }

        class Wild
        {
            public double Hp = 1;
            public bool Caught, Killed;
            public string Name;
        }

        /// <summary>The pretend 2v2: lead chooses, then the second Lumit, then the round plays out, second Lumit first.</summary>
        class Game : Form
        {
            public enum Screen { LeadMenu, PartnerMenu, Target, Wait, Caught, Victory, Map }

            public Screen State = Screen.Map;
            public readonly Wild[] Wild = { new Wild { Name = "Piroink" }, new Wild { Name = "Finny" } };
            public double[] LeadDamage = { 0.30, 0.95, 0.72, 0.5, 0.45, 0.4 }, PartnerDamage = { 0.25, 0.3, 0.38, 0.3, 0.6, 1.1 };
            public double CaptureChance = 0.5, MissClick = 0.25;
            public Random Random = new Random(1);
            public volatile bool Finished;
            public readonly List<string> Lead = new List<string>(), Partner = new List<string>(), Trace = new List<string>(), Odd = new List<string>();
            public readonly List<Wild> CaughtQueue = new List<Wild>();
            public int Keeps, Rounds, IgnoredClicks;

            readonly Dictionary<string, Rectangle> spots;
            readonly Rectangle[] moves = new Rectangle[6];
            readonly object sync = new object();
            Bitmap shown;
            bool chooserIsLead = true, capturing;
            int highlighted, slot;

            public Game(Dictionary<string, Rectangle> spots)
            {
                this.spots = spots;
                Rectangle first = spots[CatchSpots.Strike], third = spots[CatchSpots.Siphon];
                int rowGap = (int)Math.Round(56.0 * first.Height / 26);
                for (int s = 0; s < 6; s++)
                {
                    int column = s % 3;
                    int x = column == 0 ? first.X : column == 2 ? third.X : (first.X + third.X) / 2;
                    moves[s] = new Rectangle(x, first.Y + s / 3 * rowGap, first.Width, first.Height);
                }
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-7000, -7000, W, H);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
                Redraw();
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            bool Alive(int i) { return !Wild[i].Caught && !Wild[i].Killed; }

            public void Redraw()
            {
                lock (sync)
                {
                    if (shown != null) shown.Dispose();
                    shown = Picture();
                }
                if (IsHandleCreated) Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                lock (sync) e.Graphics.DrawImageUnscaled(shown, 0, 0);
            }

            Bitmap Picture()
            {
                Bitmap source;
                switch (State)
                {
                    case Screen.LeadMenu: source = leadMenu; break;
                    case Screen.PartnerMenu: source = partnerMenu; break;
                    case Screen.Target: source = capturing ? captureTarget : attackTarget; break;
                    case Screen.Wait: source = waiting; break;
                    case Screen.Caught: source = Keeps % 2 == 0 ? caughtA : caughtB; break;
                    case Screen.Victory: source = victory; break;
                    default: source = map; break;
                }
                var bmp = new Bitmap(source);
                if (State <= Screen.Wait)
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            Rectangle r = spots[i == 0 ? CatchSpots.Enemy1Bar : CatchSpots.Enemy2Bar];
                            g.FillRectangle(new SolidBrush(Color.FromArgb(30, 30, 35)), r);
                            if (Alive(i)) g.FillRectangle(new SolidBrush(Color.FromArgb(230, 50, 60)), r.X, r.Y, (int)Math.Round(r.Width * Wild[i].Hp), r.Height);
                        }
                    }
                }
                return bmp;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0202) // WM_LBUTTONUP
                {
                    long l = m.LParam.ToInt64();
                    Clicked(new Point((int)(l & 0xFFFF), (int)((l >> 16) & 0xFFFF)));
                    return;
                }
                base.WndProc(ref m);
            }

            int Tapped(Point p)
            {
                if (spots[CatchSpots.Enemy1Tap].Contains(p) || spots[CatchSpots.Enemy1TapLow].Contains(p)) return 0;
                if (spots[CatchSpots.Enemy2Tap].Contains(p) || spots[CatchSpots.Enemy2TapLow].Contains(p)) return 1;
                return -1;
            }

            void Clicked(Point p)
            {
                switch (State)
                {
                    case Screen.Map:
                        State = Screen.LeadMenu;
                        break;
                    case Screen.LeadMenu:
                    case Screen.PartnerMenu:
                        if (spots[CatchSpots.Capture].Contains(p))
                        {
                            capturing = true;
                            ToTarget();
                        }
                        else
                        {
                            int s = Array.FindIndex(moves, r => r.Contains(p));
                            if (s >= 0)
                            {
                                capturing = false;
                                slot = s;
                                ToTarget();
                            }
                            else Odd.Add("click at " + p + " on a menu");
                        }
                        break;
                    case Screen.Target:
                        int t = Tapped(p);
                        if (t < 0)
                        {
                            Odd.Add("click at " + p + " while picking a target");
                            break;
                        }
                        if (Random.NextDouble() < MissClick)
                        {
                            IgnoredClicks++;
                            break;
                        }
                        if (!Alive(t))
                        {
                            Odd.Add("clicked a Lumit that's gone");
                            break;
                        }
                        if (t != highlighted)
                        {
                            highlighted = t;
                            break;
                        }
                        (chooserIsLead ? Lead : Partner).Add((capturing ? "capture" : "move" + slot) + ">" + t);
                        if (chooserIsLead)
                        {
                            chooserIsLead = false;
                            State = Screen.PartnerMenu;
                        }
                        else Resolve();
                        break;
                    case Screen.Caught:
                        if (spots[CatchSpots.Keep].Contains(p))
                        {
                            Keeps++;
                            CaughtQueue.RemoveAt(0);
                            State = CaughtQueue.Count > 0 ? Screen.Caught : Screen.Victory;
                        }
                        break;
                    case Screen.Victory:
                        State = Screen.Map;
                        Finished = true;
                        break;
                }
                Redraw();
            }

            void ToTarget()
            {
                State = Screen.Target;
                highlighted = Alive(0) ? 0 : 1;
            }

            void Resolve()
            {
                Act(Partner.LastOrDefault(), PartnerDamage);
                Act(Lead.LastOrDefault(), LeadDamage);
                Rounds++;
                Trace.Add("round " + Rounds + ": second " + Partner.LastOrDefault() + ", lead " + Lead.LastOrDefault() + " -> " + Show(0) + " / " + Show(1));
                chooserIsLead = true;
                State = Screen.Wait;
                var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                timer.Tick += delegate
                {
                    timer.Dispose();
                    State = Alive(0) || Alive(1) ? Screen.LeadMenu : CaughtQueue.Count > 0 ? Screen.Caught : Screen.Victory;
                    Redraw();
                };
                timer.Start();
            }

            string Show(int i)
            {
                return Wild[i].Caught ? "caught" : Wild[i].Killed ? "dead" : Math.Round(Wild[i].Hp * 100) + "%";
            }

            void Act(string action, double[] damage)
            {
                if (action == null) return;
                string[] parts = action.Split('>');
                int t = int.Parse(parts[1]);
                if (!Alive(t)) return;
                if (parts[0] == "capture")
                {
                    if (Random.NextDouble() < CaptureChance + (1 - Wild[t].Hp) * 0.4)
                    {
                        Wild[t].Caught = true;
                        CaughtQueue.Add(Wild[t]);
                    }
                    return;
                }
                double hit = damage[int.Parse(parts[0].Substring(4))] * (1 + 0.08 * (2 * Random.NextDouble() - 1));
                if (Random.NextDouble() < 0.1) hit *= 1.5;
                Wild[t].Hp -= hit;
                if (Wild[t].Hp <= 0)
                {
                    Wild[t].Hp = 0;
                    Wild[t].Killed = true;
                }
            }
        }

        static GameWindow WindowFor(IntPtr handle)
        {
            ConstructorInfo make = typeof(GameWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First();
            return (GameWindow)make.Invoke(new object[] { handle, "Pretend game", "test" });
        }

        class Played
        {
            public Game Game;
            public RunResult Result;
            public LumitCounter Counter;
            public List<string> Notices = new List<string>();
            public List<string> Activity = new List<string>();
        }

        static Played Play(LumitCounter counter, Action<Game> setUp)
        {
            Dictionary<string, Rectangle> spots = CatchSpots.Resolve(new Size(W, H), null);
            Game game = null;
            var ready = new ManualResetEvent(false);
            var ui = new Thread(() =>
            {
                game = new Game(spots);
                setUp(game);
                game.Redraw();
                game.Show();
                ready.Set();
                Application.Run(game);
            });
            ui.SetApartmentState(ApartmentState.STA);
            ui.IsBackground = true;
            ui.Start();
            ready.WaitOne();
            IntPtr handle = IntPtr.Zero;
            game.Invoke((MethodInvoker)delegate { handle = game.Handle; });

            Rectangle corner = new Rectangle(20, 300, 60, 60);
            Rectangle watch = spots[CatchSpots.Capture];
            var steps = new[]
            {
                new ClickPoint { X = 880, Y = 500, Width = 40, Height = 40, WaitForScreen = true, WatchArea = corner, Snapshot = Snapshot.FromBitmap(map, corner), ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 20 },
                new ClickPoint { X = 1240, Y = 932, Width = 120, Height = 26, WaitForScreen = true, WatchArea = watch, Snapshot = Snapshot.FromBitmap(leadMenu, watch), ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 30 },
                new ClickPoint { X = 880, Y = 840, Width = 60, Height = 30, WaitForScreen = true, WatchArea = new Rectangle(760, 160, 300, 70), Snapshot = Snapshot.FromBitmap(victory, new Rectangle(760, 160, 300, 70)), ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 30 },
            };
            var played = new Played { Game = game, Counter = counter };
            var run = new RunOptions
            {
                Points = steps,
                Space = new StepSpace(WindowFor(handle)),
                Background = true,
                IntervalMs = 100,
                HumanMouse = false,
                RedoPrevious = true,
                NameStep = 1,
                NameArea = GoldText.LumitsNameAreaFor(new Size(W, H)),
                RadiantCheck = true,
                AlertGoldPercent = 2,
                Counter = counter,
                Catch = new CatchOptions
                {
                    CatchOn = true,
                    CatchRadiants = true,
                    HealBelowHp = 0,
                    LeadMaxHp = 182,
                    PartnerWeaken = 2,
                    PartnerKill = 5,
                    BorrowForTargets = false, // the test must never move your real pointer
                    Spots = spots,
                    Log = new CatchLog(tempDir)
                }
            };
            var engine = new ClickEngine();
            var finished = new ManualResetEvent(false);
            engine.Notice += (t, m, p) => { lock (played.Notices) played.Notices.Add(t); };
            engine.Progress += (clicks, cyc, activity) =>
            {
                lock (played.Activity)
                    if (activity.Length > 0 && (played.Activity.Count == 0 || played.Activity[played.Activity.Count - 1] != activity)) played.Activity.Add(activity);
            };
            engine.Finished += r => { played.Result = r; finished.Set(); };
            engine.Start(run);
            var clock = Stopwatch.StartNew();
            while (!finished.WaitOne(100))
            {
                if (game.Finished) engine.Stop();
                if (clock.Elapsed.TotalSeconds > 150)
                {
                    Check(false, "A fight had to be stopped from outside. " + string.Join("; ", game.Trace));
                    engine.Stop();
                }
            }
            game.Invoke((MethodInvoker)delegate { game.Close(); });
            if (played.Result.Problem != null) Console.WriteLine("      Stopped: " + played.Result.Problem);
            return played;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Native.EnableDpiAwareness();
            tempDir = Path.Combine(Path.GetTempPath(), "lumits-duo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string dir = args[0];
            leadMenu = Load(dir, "114501");
            partnerMenu = Load(dir, "114542");
            attackTarget = Load(dir, "114523");
            captureTarget = Load(dir, "114546");
            waiting = Load(dir, "114602");
            caughtA = Load(dir, "114713");
            caughtB = Load(dir, "114730");
            victory = Load(dir, "114738");
            map = Load(dir, "113756");
            try
            {
                var counter = new LumitCounter(Path.Combine(tempDir, "lumits.txt"));

                Console.WriteLine("== Neither ticked");
                Played first = Play(counter, g => { });
                Console.WriteLine("      Lead: " + string.Join(", ", first.Game.Lead) + "; second: " + string.Join(", ", first.Game.Partner));
                Console.WriteLine("      Names read off the cards: " + string.Join(", ", counter.Entries().Select(e => e.Name + " (met " + e.Met + ")")));
                Check(counter.Entries().Count == 2, "Both wild Lumits were counted by their names");
                Check(first.Game.Lead.FirstOrDefault() == "move1>0" && first.Game.Partner.FirstOrDefault() == "move5>1", "The lead used Pyrecrest on the left one, the second Lumit its bottom-right move on the right one");
                Check(first.Game.Odd.Count == 0, "No stray clicks: " + string.Join("; ", first.Game.Odd));

                string piroink = counter.Entries().Select(e => e.Name).FirstOrDefault(n => n.StartsWith("Piro", StringComparison.OrdinalIgnoreCase));
                string finny = counter.Entries().Select(e => e.Name).FirstOrDefault(n => n.StartsWith("Finn", StringComparison.OrdinalIgnoreCase));
                Check(piroink != null && finny != null, "The names read were Piroink and Finny");
                if (piroink == null || finny == null) return 1;

                Console.WriteLine("\n== Both ticked, no keep rules");
                counter.SetCatch(piroink, true);
                counter.SetCatch(finny, true);
                int kills = 0, kept = 0, caught = 0, odd = 0, stops = 0, ignored = 0;
                bool leadLeft = true, partnerRight = true;
                for (int i = 0; i < 8; i++)
                {
                    Played p = Play(counter, g => { g.Random = new Random(100 + i); });
                    kills += p.Game.Wild.Count(w => w.Killed);
                    caught += p.Game.Wild.Count(w => w.Caught);
                    kept += p.Game.Keeps;
                    odd += p.Game.Odd.Count;
                    ignored += p.Game.IgnoredClicks;
                    if (p.Result.Problem != null) stops++;
                    if (p.Game.Lead.Count > 0 && !p.Game.Lead[0].StartsWith("move2>0")) leadLeft = false;
                    if (p.Game.Partner.Count > 0 && !p.Game.Partner[0].StartsWith("move2>1")) partnerRight = false;
                    if (i == 0) foreach (string t in p.Game.Trace) Console.WriteLine("      " + t);
                    if (p.Result.Problem != null && stops == 1)
                    {
                        foreach (string t in p.Game.Trace) Console.WriteLine("      game: " + t);
                        foreach (string a in p.Activity) Console.WriteLine("      app:  " + a);
                        Console.WriteLine("      game lists: lead " + string.Join(", ", p.Game.Lead) + "; second " + string.Join(", ", p.Game.Partner));
                    }
                }
                Check(leadLeft && partnerRight, "The lead started with Heat Siphon on the left one, the second Lumit with its top-right move on the right one");
                Check(kept == caught, "Every caught Lumit's screen was read and kept: " + kept + " of " + caught);
                Check(odd == 0 && stops == 0, "No stray clicks and no stops");
                Console.WriteLine("      " + kills + " of 16 died; " + ignored + " clicks on a target were ignored on purpose and recovered from.");
                string log = File.Exists(Path.Combine(tempDir, "catches.txt")) ? File.ReadAllText(Path.Combine(tempDir, "catches.txt")) : "";
                foreach (string l in log.Split('\n').Take(4)) if (l.Trim().Length > 0) Console.WriteLine("      Log: " + l.Trim());
                int known = log.Split('\n').Count(l => l.Contains("  " + piroink + " ") || l.Contains("  " + finny + " "));
                Check(known == kept, "Each catch in the log is put down to the right Lumit by its caught name: " + known + " of " + kept);

                Console.WriteLine("\n== Only Finny ticked, with Heavy Strike as its attack");
                counter.SetCatch(piroink, false);
                counter.SetAttack(finny, 4);
                Played heavy = Play(counter, g => { g.Random = new Random(7); });
                Console.WriteLine("      Lead: " + string.Join(", ", heavy.Game.Lead) + "; second: " + string.Join(", ", heavy.Game.Partner));
                Check(heavy.Game.Lead.FirstOrDefault() == "move4>1", "The lead used Heavy Strike on Finny, on the right");
                Check(heavy.Game.Partner.FirstOrDefault() == "move5>0", "The second Lumit used its bottom-right move on Piroink");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "\nAll checks passed." : "\n" + failures + " checks FAILED.");
            return failures == 0 ? 0 : 1;
        }
    }
}
