using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// Plays radiant fights against the real ClickEngine, in a pretend game window placed off-screen. The run is in
    /// background mode, so clicks are messages to that window and the real pointer never moves. It reads real pictures of
    /// the game with the HP bars painted in, through the app's own screen reading.
    /// RadiantCatchTest radiant.jpg normal-fight.png caught.png fainted.png
    /// </summary>
    static class RadiantCatchTest
    {
        static Bitmap radiantPic, normalPic, caughtPic, faintedPic;
        static int failures;
        static string tempDir;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        /// <summary>The pretend game: shows a picture for each screen, and reacts to clicks.</summary>
        class Game : Form
        {
            public enum Screen { Map, Turn, Wait, Fainted, Caught, Victory }

            public double MaxHp = 118, Hp = 118, StrikeDamage = 20, CaptureChance = 1;
            public double[] ScriptedCrits;
            public int FaintAfterTurns = -1;
            public bool Radiant = true;
            public volatile bool Finished;
            public Screen State = Screen.Turn;
            public Random Random = new Random(1);

            public int Strikes, Captures, Turns, AttackClicks, StrikesAfterStandIn;
            public bool StandIn, Killed, Kept, Released;
            public double HpAtFirstCapture = -1;
            public readonly List<string> Trace = new List<string>();
            public readonly List<string> Other = new List<string>();

            readonly Dictionary<string, Rectangle> spots;
            readonly Rectangle attackStep;
            readonly object sync = new object();
            Bitmap shown;

            public Game(Dictionary<string, Rectangle> spots, Rectangle attackStep)
            {
                this.spots = spots;
                this.attackStep = attackStep;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-6000, -6000, 1920, 1025);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
                Redraw();
            }

            protected override bool ShowWithoutActivation { get { return true; } }

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

            void Bar(Graphics g, Rectangle r, double share)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(30, 30, 35)), r);
                g.FillRectangle(new SolidBrush(Color.FromArgb(230, 50, 60)), r.X, r.Y, (int)Math.Round(r.Width * Math.Max(0, Math.Min(1, share))), r.Height);
            }

            Bitmap Picture()
            {
                var bmp = new Bitmap(1920, 1025, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    switch (State)
                    {
                        case Screen.Map:
                            g.FillRectangle(Brushes.Olive, 0, 0, 1920, 1025);
                            break;
                        case Screen.Victory:
                            g.FillRectangle(Brushes.Teal, 0, 0, 1920, 1025);
                            break;
                        case Screen.Caught:
                            g.DrawImageUnscaled(caughtPic, 0, 0);
                            break;
                        case Screen.Fainted:
                            g.DrawImageUnscaled(faintedPic, 0, 0);
                            Bar(g, spots[CatchSpots.EnemyBar], Hp / MaxHp);
                            break;
                        default:
                            g.DrawImageUnscaled(Radiant ? radiantPic : normalPic, 0, 0);
                            Bar(g, spots[CatchSpots.EnemyBar], Hp / MaxHp);
                            Bar(g, spots[CatchSpots.LeadBar], 0.9);
                            if (State == Screen.Wait)
                            {
                                // The menu is dark until your turn.
                                g.FillRectangle(Brushes.Black, spots[CatchSpots.Capture]);
                                g.FillRectangle(Brushes.Black, new Rectangle(460, 860, 1000, 110));
                                g.FillRectangle(Brushes.Black, attackStep);
                            }
                            break;
                    }
                }
                return bmp;
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_LBUTTONUP = 0x0202;
                if (m.Msg == WM_LBUTTONUP)
                {
                    long l = m.LParam.ToInt64();
                    Clicked(new Point((int)(l & 0xFFFF), (int)((l >> 16) & 0xFFFF)));
                    return;
                }
                base.WndProc(ref m);
            }

            bool In(string key, Point p)
            {
                return spots[key].Contains(p);
            }

            void Clicked(Point p)
            {
                switch (State)
                {
                    case Screen.Turn:
                        if (In(CatchSpots.Strike, p)) Hit();
                        else if (In(CatchSpots.Capture, p)) TryCapture();
                        else if (attackStep.Contains(p))
                        {
                            AttackClicks++;
                            Trace.Add("Attack step at " + Math.Round(Hp));
                            if (Radiant) Other.Add("attack step");
                            Hp = 0;
                            State = Screen.Victory;
                        }
                        else Other.Add("(" + p.X + "," + p.Y + ")");
                        break;
                    case Screen.Fainted:
                        if (In(CatchSpots.FirstLumit, p))
                        {
                            StandIn = true;
                            State = Screen.Turn;
                        }
                        break;
                    case Screen.Caught:
                        if (In(CatchSpots.Keep, p))
                        {
                            Kept = true;
                            State = Screen.Victory;
                        }
                        else if (In(CatchSpots.Release, p))
                        {
                            Released = true;
                            State = Screen.Victory;
                        }
                        break;
                    case Screen.Victory:
                        // The step's Continue.
                        State = Screen.Map;
                        Finished = true;
                        break;
                }
                Redraw();
            }

            void Hit()
            {
                Strikes++;
                if (StandIn) StrikesAfterStandIn++;
                Trace.Add("Strike at " + Math.Round(Hp));
                double damage = StrikeDamage;
                if (ScriptedCrits != null && Strikes <= ScriptedCrits.Length && ScriptedCrits[Strikes - 1] > 0) damage *= 1.5;
                Hp -= damage;
                if (Hp <= 0)
                {
                    Hp = 0;
                    Killed = true;
                    State = Screen.Victory;
                    return;
                }
                EnemyActs();
            }

            void TryCapture()
            {
                Captures++;
                if (HpAtFirstCapture < 0) HpAtFirstCapture = Hp;
                Trace.Add("Capture at " + Math.Round(Hp));
                if (Random.NextDouble() < CaptureChance) State = Screen.Caught;
                else EnemyActs();
            }

            void EnemyActs()
            {
                Turns++;
                if (!StandIn && FaintAfterTurns >= 0 && Turns >= FaintAfterTurns)
                {
                    State = Screen.Fainted;
                    return;
                }
                State = Screen.Wait;
                var timer = new System.Windows.Forms.Timer { Interval = 500 };
                timer.Tick += delegate
                {
                    timer.Dispose();
                    if (State == Screen.Wait)
                    {
                        State = Screen.Turn;
                        Redraw();
                    }
                };
                timer.Start();
            }
        }

        class Played
        {
            public Game Game;
            public RunResult Result;
            public List<string> Notices = new List<string>();
            public LumitCounter Counter;
        }

        static GameWindow WindowFor(IntPtr handle)
        {
            ConstructorInfo make = typeof(GameWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First();
            return (GameWindow)make.Invoke(new object[] { handle, "Pretend game", "test" });
        }

        static Snapshot SnapshotOf(Bitmap source, Rectangle area)
        {
            return Snapshot.FromBitmap(source, area);
        }

        static Played Play(Action<Game> setUp, Action<CatchOptions> options = null, bool radiantCheck = true)
        {
            Size client = new Size(1920, 1025);
            Dictionary<string, Rectangle> spots = CatchSpots.Resolve(client, null);
            Rectangle attackArea = new Rectangle(1240, 932, 120, 26);
            Rectangle watch = spots[CatchSpots.Capture];
            Rectangle corner = new Rectangle(20, 300, 60, 60);

            using (var map = new Bitmap(1920, 1025))
            using (var victory = new Bitmap(1920, 1025))
            {
                using (Graphics g = Graphics.FromImage(map)) g.FillRectangle(Brushes.Olive, 0, 0, 1920, 1025);
                using (Graphics g = Graphics.FromImage(victory)) g.FillRectangle(Brushes.Teal, 0, 0, 1920, 1025);

                Game game = null;
                var ready = new ManualResetEvent(false);
                var ui = new Thread(() =>
                {
                    game = new Game(spots, attackArea);
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

                var steps = new[]
                {
                    new ClickPoint { X = 900, Y = 500, Width = 40, Height = 40, WaitForScreen = true, WatchArea = corner, Snapshot = SnapshotOf(map, corner), ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 20 },
                    new ClickPoint { X = attackArea.X, Y = attackArea.Y, Width = attackArea.Width, Height = attackArea.Height, WaitForScreen = true, WatchArea = watch, Snapshot = SnapshotOf(radiantPic, watch), ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 20 },
                    new ClickPoint { X = 900, Y = 500, Width = 40, Height = 40, WaitForScreen = true, WatchArea = corner, Snapshot = SnapshotOf(victory, corner), ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 20 },
                };

                var played = new Played { Game = game, Counter = new LumitCounter(Path.Combine(tempDir, "lumits-" + Guid.NewGuid().ToString("N") + ".txt")) };
                var catching = new CatchOptions
                {
                    CatchOn = true,
                    Practice = false,
                    CatchRadiants = true,
                    HealBelowHp = 0,
                    LeadMaxHp = 182,
                    Spots = spots,
                    Log = new CatchLog(tempDir)
                };
                if (options != null) options(catching);
                var run = new RunOptions
                {
                    Points = steps,
                    Space = new StepSpace(WindowFor(handle)),
                    Background = true,
                    IntervalMs = 100,
                    HumanMouse = false,
                    RedoPrevious = true,
                    NameStep = 1,
                    NameArea = GoldText.LumitsNameArea,
                    RadiantCheck = radiantCheck,
                    AlertGoldPercent = 2,
                    Counter = played.Counter,
                    Catch = catching
                };
                var engine = new ClickEngine();
                var finished = new ManualResetEvent(false);
                engine.Notice += (title, message, picture) => { lock (played.Notices) played.Notices.Add(title + " | " + message + " | picture: " + (picture != null && picture.Length > 0)); };
                engine.Finished += r => { played.Result = r; finished.Set(); };
                engine.Start(run);

                // Stop once the fight is over and Continue was clicked.
                var clock = Stopwatch.StartNew();
                while (!finished.WaitOne(100))
                {
                    if (game.Finished) engine.Stop();
                    if (clock.Elapsed.TotalSeconds > 120)
                    {
                        Check(false, "A fight had to be stopped from outside. Trace: " + string.Join(", ", game.Trace));
                        engine.Stop();
                    }
                }
                game.Invoke((MethodInvoker)delegate { game.Close(); });
                return played;
            }
        }

        static void Simulate()
        {
            Console.WriteLine("\n== Many fights through the decision itself, with damage varying per hit and 15% crits");
            double[] damages = { 6, 10, 15, 20, 25, 30, 40, 50, 60 };
            double[] spreads = { 0, 0.05, 0.10, 0.13 };
            var random = new Random(7);
            const int fightsEach = 20000;
            foreach (double spread in spreads)
            {
                int kills = 0;
                double captureHp = 0;
                foreach (double damage in damages)
                {
                    for (int f = 0; f < fightsEach; f++)
                    {
                        var s = new RadiantStrikes();
                        double hp = 118;
                        while (true)
                        {
                            // Read off a bar 118 pixels wide, as on the 1920 x 1025 window.
                            double read = Math.Round(hp / 118 * 118) / 118;
                            s.Observe(read);
                            if (!s.ShouldStrike(read))
                            {
                                captureHp += hp;
                                break;
                            }
                            s.Struck(read);
                            double hit = damage * (1 + spread * (2 * random.NextDouble() - 1));
                            if (random.NextDouble() < 0.15) hit *= 1.5;
                            hp -= hit;
                            if (hp <= 0)
                            {
                                kills++;
                                break;
                            }
                        }
                    }
                }
                int total = fightsEach * damages.Length;
                Console.WriteLine("Damage varying +-" + Math.Round(spread * 100) + "%: " + kills + " kills in " + total + " fights");
                if (spread == 0) Check(kills == 0, "No kills when damage doesn't vary");
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Native.EnableDpiAwareness();
            tempDir = Path.Combine(Path.GetTempPath(), "lumits-radiant-catch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            radiantPic = new Bitmap(args[0]);
            normalPic = new Bitmap(args[1]);
            caughtPic = new Bitmap(args[2]);
            faintedPic = new Bitmap(args[3]);

            try
            {
                Console.WriteLine("== Your example: Strike does 20, no crits");
                foreach (double max in new double[] { 118, 116 })
                {
                    Played p = Play(g => { g.MaxHp = g.Hp = max; g.CaptureChance = 1; });
                    Console.WriteLine("      " + max + " HP: " + string.Join(", ", p.Game.Trace));
                    Check(!p.Game.Killed && p.Game.Kept && !p.Game.Released && p.Game.Other.Count == 0, "Caught and kept, with only Strike and Capture clicked");
                    Check(p.Game.HpAtFirstCapture > 0 && p.Game.HpAtFirstCapture <= 37.5, "It stopped attacking at about 36 HP or lower (" + Math.Round(p.Game.HpAtFirstCapture) + ")");
                    if (max == 118)
                    {
                        foreach (string n in p.Notices) Console.WriteLine("      Alert: " + n);
                        Check(p.Notices.Any(n => n.Contains("spotted") && n.Contains("picture: True")), "An alert with a picture when it's spotted");
                        Check(p.Notices.Any(n => n.Contains("caught!") && n.Contains("picture: True")), "An alert with a picture when it's caught");
                        LumitCounter.Entry e = p.Counter.Entries().FirstOrDefault(x => x.Radiants > 0);
                        Check(e != null && e.Kept == 1 && e.Released == 0, "The counter shows a radiant, kept: " + (e == null ? "none" : e.Name));
                        Check(p.Result.Problem == null, "The run carried on after the catch, until the test stopped it");
                    }
                }

                Console.WriteLine("\n== The first Strike is a crit, then normal ones");
                Played crit = Play(g => { g.ScriptedCrits = new double[] { 1 }; });
                Console.WriteLine("      " + string.Join(", ", crit.Game.Trace));
                Check(!crit.Game.Killed && crit.Game.Kept && crit.Game.HpAtFirstCapture <= 37.5, "A later normal hit shows the first was a crit, so it still goes down to about 36");

                Console.WriteLine("\n== Capture fails a few times");
                Played fails = Play(g => { g.CaptureChance = 0.35; g.Random = new Random(4); });
                Console.WriteLine("      " + string.Join(", ", fails.Game.Trace));
                Check(fails.Game.Kept && fails.Game.Other.Count == 0 && fails.Game.Strikes == fails.Game.Trace.Count(t => t.StartsWith("Strike")), "It keeps using Capture until caught");

                Console.WriteLine("\n== Settings that must not change how a radiant is treated");
                Played practice = Play(g => { }, c => c.Practice = true);
                Check(practice.Game.Kept && !practice.Game.Released, "Practice mode: caught and kept");
                Played noCatch = Play(g => { }, c => c.CatchOn = false);
                Check(noCatch.Game.Kept && !noCatch.Game.Released && noCatch.Game.Other.Count == 0, "Catching turned off: caught and kept");

                Console.WriteLine("\n== Your Lumit faints partway through");
                Played faint = Play(g => { g.FaintAfterTurns = 2; g.CaptureChance = 0.4; g.Random = new Random(9); });
                Console.WriteLine("      " + string.Join(", ", faint.Game.Trace));
                Check(faint.Game.StandIn && faint.Game.StrikesAfterStandIn == 0, "The stand-in only used Capture");
                Check(faint.Game.Kept && !faint.Game.Killed, "Caught and kept");

                Console.WriteLine("\n== Things go wrong");
                Played oneShot = Play(g => { g.StrikeDamage = 500; });
                Console.WriteLine("      " + (oneShot.Result.Problem ?? "(no problem reported)"));
                Check(oneShot.Result.Problem != null && oneShot.Result.Problem.Contains("wasn't caught") && oneShot.Result.AlertTitle == "Radiant not caught", "A radiant that dies anyway stops the run, and always alerts");

                Console.WriteLine("\n== The old way, and normal fights");
                Played stop = Play(g => { }, c => c.CatchRadiants = false);
                Console.WriteLine("      " + stop.Result.Problem);
                Check(stop.Result.RadiantFound && stop.Game.Trace.Count == 0, "Catch radiants off: it stops without clicking anything");
                Played normal = Play(g => { g.Radiant = false; g.MaxHp = g.Hp = 132; });
                Console.WriteLine("      " + string.Join(", ", normal.Game.Trace));
                Check(normal.Game.AttackClicks > 0 && normal.Game.Strikes == 0 && normal.Game.Captures == 0 && !normal.Result.RadiantFound, "A normal unticked Lumit still gets your attack step");

                Console.WriteLine("\n== The window's text fits");
                using (var font = new Font("Segoe UI", 9F))
                {
                    int width = TextRenderer.MeasureText("Radiant check before step 2, catches them   •   phone alerts by Discord (radiants and stops)", font).Width;
                    Check(width <= 512, "The longest alert line is " + width + " px, with 512 px before the Alerts button");
                }

                Simulate();
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
