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

            /// <summary>A PC 2v2 shown instead of the 1v1 fight, or null.</summary>
            public Bitmap Duo;
            /// <summary>Radiants only: the Run button (bottom left), how many runs fail before one works, and runs made.</summary>
            public Rectangle RunArea;
            public int FailRuns, Runs, Fights = 1;
            /// <summary>Fights after this one, started by clicking the stone on the map: true for a radiant.</summary>
            public readonly Queue<bool> NextFights = new Queue<bool>();
            /// <summary>From each run that worked to the next click on the stone, in ms.</summary>
            public readonly List<long> RunToStoneMs = new List<long>();
            readonly Stopwatch clock = Stopwatch.StartNew();
            long ranAt = -1;

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
                            if (Duo != null)
                            {
                                // A PC 2v2, bars and names as they were in the game.
                                g.DrawImage(Duo, new Rectangle(0, 0, 1920, 1025));
                            }
                            else
                            {
                                g.DrawImageUnscaled(Radiant ? radiantPic : normalPic, 0, 0);
                                Bar(g, spots[CatchSpots.EnemyBar], Hp / MaxHp);
                                Bar(g, spots[CatchSpots.LeadBar], 0.9);
                            }
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
                    case Screen.Map:
                        // The stone: the next fight, if there's one.
                        if (!new Rectangle(900, 500, 40, 40).Contains(p))
                        {
                            Other.Add("map (" + p.X + "," + p.Y + ")");
                            break;
                        }
                        if (ranAt >= 0) RunToStoneMs.Add(clock.ElapsedMilliseconds - ranAt);
                        ranAt = -1;
                        if (NextFights.Count == 0)
                        {
                            Finished = true;
                            break;
                        }
                        Radiant = NextFights.Dequeue();
                        Hp = MaxHp;
                        Fights++;
                        Trace.Add("fight " + Fights + (Radiant ? ", a radiant" : ""));
                        State = Screen.Turn;
                        break;
                    case Screen.Turn:
                        if (In(CatchSpots.Strike, p)) Hit();
                        else if (In(CatchSpots.Capture, p)) TryCapture();
                        else if (RunArea.Contains(p))
                        {
                            Runs++;
                            if (Radiant) Other.Add("ran from the radiant");
                            if (FailRuns > 0)
                            {
                                FailRuns--;
                                Trace.Add("Run, which failed");
                                EnemyActs();
                            }
                            else
                            {
                                Trace.Add("Run");
                                ranAt = clock.ElapsedMilliseconds;
                                State = Screen.Map;
                            }
                        }
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
                        if (NextFights.Count == 0) Finished = true;
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
            public List<string> Activity = new List<string>();
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

        /// <summary>The Run button, bottom left in a fight (the boot), as the radiant check's step in radiants-only runs.</summary>
        static readonly Rectangle RunButton = new Rectangle(212, 914, 50, 50);

        static Played Play(Action<Game> setUp, Action<CatchOptions> options = null, bool radiantCheck = true, bool runStep = false, LumitCounter counter = null)
        {
            Size client = new Size(1920, 1025);
            Dictionary<string, Rectangle> spots = CatchSpots.Resolve(client, null);
            Rectangle attackArea = runStep ? RunButton : new Rectangle(1240, 932, 120, 26);
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
                    if (runStep) game.RunArea = RunButton;
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

                var played = new Played { Game = game, Counter = counter ?? new LumitCounter(Path.Combine(tempDir, "lumits-" + Guid.NewGuid().ToString("N") + ".txt")) };
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
                engine.Progress += (clicks, cyc, activity) =>
                {
                    lock (played.Activity)
                        if (activity.Length > 0 && (played.Activity.Count == 0 || played.Activity[played.Activity.Count - 1] != activity)) played.Activity.Add(activity);
                };
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

        /// <summary>A picture beside radiant.jpg, at 1025 px high like the game window, or null if it isn't there.</summary>
        static Bitmap Load(string dir, string name)
        {
            string file = Path.Combine(dir, name);
            if (!File.Exists(file)) return null;
            using (var raw = new Bitmap(file))
            {
                if (raw.Height == 1025) return new Bitmap(raw);
                int width = (int)Math.Round(raw.Width * 1025.0 / raw.Height);
                var b = new Bitmap(width, 1025, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(raw, new Rectangle(0, 0, width, 1025));
                }
                return b;
            }
        }

        /// <summary>Where the owner's own radiant, Minoking, has its gold name ("✦ Minoking Lvl 30") in these 2v2 pictures.</summary>
        static readonly Rectangle MinokingName = new Rectangle(150, 42, 154, 30);

        /// <summary>A copy with Minoking's real gold name copied over a wild Lumit's name, as a radiant's would show.</summary>
        static Bitmap Gild(Bitmap source, Rectangle box)
        {
            var b = new Bitmap(source);
            using (Graphics g = Graphics.FromImage(b))
            using (Bitmap name = source.Clone(MinokingName, PixelFormat.Format32bppArgb))
                g.DrawImage(name, new Rectangle(box.X, box.Y, Math.Min(box.Width, name.Width), Math.Min(box.Height, name.Height)),
                            new Rectangle(0, 0, Math.Min(box.Width, name.Width), Math.Min(box.Height, name.Height)), GraphicsUnit.Pixel);
            return b;
        }

        /// <summary>A 2v2 picture with a 1v1 picture's fight menu over its bottom, lit for your turn (the menus are laid out alike).</summary>
        static Bitmap WithMenu(Bitmap duo, Bitmap menu)
        {
            var b = new Bitmap(1920, 1025, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.DrawImage(duo, new Rectangle(0, 0, 1920, 1025));
                var part = new Rectangle(0, 790, 1920, 235);
                g.DrawImage(menu, part, part, GraphicsUnit.Pixel);
            }
            return b;
        }

        /// <summary>
        /// The PC's 2v2 spots on pictures of the owner's game: two cards a side, one above the other. Each 2v2 must be seen
        /// as one, both names read, and either name turned gold found; no 1v1 may look like a 2v2.
        /// </summary>
        static void DuoSpots(string dir)
        {
            Console.WriteLine("\n== The PC's 2v2 spots, on pictures of the game");
            string[][] duos =
            {
                new[] { "duo-target-lithorse.png", "Lithorse", "Lithorse", "120" },
                new[] { "duo-turn-bonwing.png", "Bonwing", "Musingens", "133" },
                new[] { "duo-start-gryphair.png", "Gryphair", "Bonwing", "133" },
            };
            foreach (string[] d in duos)
            {
                Bitmap b = Load(dir, d[0]);
                if (b == null)
                {
                    Check(false, d[0] + " is missing beside radiant.jpg");
                    continue;
                }
                using (b)
                {
                    Dictionary<string, Rectangle> s = CatchSpots.Resolve(b.Size, null);
                    Func<Bitmap, string, double> fill = (p, key) => CatchSpots.DuoBar(p, s, key);
                    Func<Bitmap, string, double> gold = (p, key) => GoldText.Percent(p, s[key]);
                    Func<string, string> name = key =>
                    {
                        using (Bitmap c = b.Clone(s[key], PixelFormat.Format32bppArgb)) return LumitNames.Read(c) ?? "";
                    };
                    double lead = fill(b, CatchSpots.Lead2Bar), partner = fill(b, CatchSpots.Partner2Bar);
                    double left = fill(b, CatchSpots.Enemy1Bar), right = fill(b, CatchSpots.Enemy2Bar);
                    string upper = name(CatchSpots.Enemy1Card), lower = name(CatchSpots.Enemy2Card);
                    Console.WriteLine(string.Format("      {0}: bars lead {1:0.00}, second {2:0.00}, left {3:0.00}, right {4:0.00}; names \"{5}\", \"{6}\"; gold {7:0.0}%, {8:0.0}%",
                        d[0], lead, partner, left, right, upper, lower, gold(b, CatchSpots.Enemy1Card), gold(b, CatchSpots.Enemy2Card)));
                    Check(CatchSpots.DuoShowing(b, s) && left > 0.9 && right > 0.9 && partner > 0.9, d[0] + ": seen as a 2v2, both wild Lumits' bars full");
                    Check(Math.Abs(lead - int.Parse(d[3]) / 181.0) < 0.04, d[0] + ": your lead's bar reads " + d[3] + " of 181 HP (" + Math.Round(lead * 181) + ")");
                    Check(LumitNames.Similar(upper, d[1]) && LumitNames.Similar(lower, d[2]), d[0] + ": names read as " + d[1] + " and " + d[2]);
                    Check(gold(b, CatchSpots.Enemy1Card) < 2 && gold(b, CatchSpots.Enemy2Card) < 2, d[0] + ": no gold in either wild name, with your own radiant's gold name and yellow portraits on screen");
                    using (Bitmap g = Gild(b, s[CatchSpots.Enemy1Card]))
                        Check(gold(g, CatchSpots.Enemy1Card) >= 4 && gold(g, CatchSpots.Enemy2Card) < 2, d[0] + ": the upper name turned gold is found (" + gold(g, CatchSpots.Enemy1Card).ToString("0.0") + "%)");
                    using (Bitmap g = Gild(b, s[CatchSpots.Enemy2Card]))
                        Check(gold(g, CatchSpots.Enemy2Card) >= 4 && gold(g, CatchSpots.Enemy1Card) < 2, d[0] + ": the lower name turned gold is found (" + gold(g, CatchSpots.Enemy2Card).ToString("0.0") + "%)");

                    // The Alerts window's Test now, which used to read only the 1v1 name area (empty in a 2v2).
                    bool radiant;
                    Bitmap picture;
                    string shown = AlertDialog.DescribeDuo(b, null, 2, out radiant, out picture);
                    Console.WriteLine("      Test now: " + (shown ?? "(no 2v2)").Replace("\n", " | "));
                    Check(shown != null && !radiant && picture != null && shown.Contains(d[1]) && shown.Contains(d[2]), d[0] + ": Test now shows both names, no radiant");
                    if (picture != null) picture.Dispose();
                    using (Bitmap g = Gild(b, s[CatchSpots.Enemy2Card]))
                    {
                        shown = AlertDialog.DescribeDuo(g, null, 2, out radiant, out picture);
                        if (picture != null) picture.Dispose();
                    }
                    Console.WriteLine("      Test now, lower name gold: " + (shown ?? "(no 2v2)").Replace("\n", " | "));
                    Check(shown != null && radiant && shown.Split('\n')[1].Contains("RADIANT") && !shown.Split('\n')[0].Contains("RADIANT"),
                          d[0] + ": Test now marks the lower one as the radiant");
                }
            }
            foreach (string one in new[] { "radiant.jpg", "game-turn-lowmp.png", "game-caught-sdoodo.png", "game-fainted-choice.png", "moomootaur.jpg", "tzik.jpg", "creebew.jpg", "coalython.jpg" })
            {
                Bitmap b = Load(dir, one);
                if (b == null) continue;
                using (b)
                {
                    Dictionary<string, Rectangle> s = CatchSpots.Resolve(b.Size, null);
                    string bars = string.Join(", ", new[] { CatchSpots.Lead2Bar, CatchSpots.Partner2Bar, CatchSpots.Enemy1Bar, CatchSpots.Enemy2Bar }
                        .Select(k => ScreenText.BarFill(b, s[k], ScreenText.IsRed).ToString("0.00") + "/" + CatchSpots.DuoBar(b, s, k).ToString("0.00")));
                    Check(!CatchSpots.DuoShowing(b, s), one + " (a 1v1) isn't taken for a 2v2 (2v2 bars, any red / from the left: " + bars + ")");
                    bool radiant;
                    Bitmap picture;
                    Check(AlertDialog.DescribeDuo(b, null, 2, out radiant, out picture) == null, one + ": Test now reads the name area, as for any 1v1");
                }
            }
        }

        /// <summary>A copy of part of a picture with its colours multiplied, as the game's night (or an even dimming) does.</summary>
        static Bitmap Tinted(Bitmap source, Rectangle part, double r, double g, double b)
        {
            Bitmap t = source.Clone(part, PixelFormat.Format32bppArgb);
            for (int y = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++)
                {
                    Color c = t.GetPixel(x, y);
                    t.SetPixel(x, y, Color.FromArgb((int)Math.Round(c.R * r), (int)Math.Round(c.G * g), (int)Math.Round(c.B * b)));
                }
            return t;
        }

        /// <summary>The same sprite in a picture by day and one by night: each snapshot must match the other.</summary>
        static void DayAndNight(Bitmap day, Rectangle d, Bitmap night, Rectangle n, string what)
        {
            Snapshot byDay = Snapshot.FromBitmap(day, d), byNight = Snapshot.FromBitmap(night, n);
            double darker, lighter;
            double atNight = byDay.Similarity(byNight, null, out darker), byDayNow = byNight.Similarity(byDay, null, out lighter);
            Console.WriteLine(string.Format("      {0}: taken by day, now night {1:0}% (night {2:0.0}); taken at night, now day {3:0}% ({4:0.0})",
                what, atNight * 100, darker, byDayNow * 100, lighter));
            Check(atNight >= 0.85 && darker >= 0.7, what + ": a snapshot taken by day matches at night");
            Check(byDayNow >= 0.85 && lighter <= -0.7, what + ": a snapshot taken at night matches by day");
        }

        /// <summary>
        /// The game's night multiplies its world's colours (not its buttons'). A step's snapshot must match by day and by
        /// night whichever it was taken in, while buttons dimmed as the game animates must score as they always did.
        /// </summary>
        static void NightLight(string dir)
        {
            Console.WriteLine("\n== Steps' snapshots by day and by night");
            Bitmap day = Load(dir, "map-day.png"), night = Load(dir, "map-night.png");
            if (day == null || night == null)
            {
                Check(false, "map-day.png and map-night.png must be beside radiant.jpg");
                return;
            }
            using (day)
            using (night)
            {
                // The same sprites on the two maps, by day and by night.
                DayAndNight(day, new Rectangle(266, 122, 44, 60), night, new Rectangle(1016, 198, 44, 60), "The standing stone");
                DayAndNight(day, new Rectangle(592, 480, 34, 30), night, new Rectangle(1342, 556, 34, 30), "The small rock");

                // Other things at night aren't the stone or the rock taken by day, nor the map's other spot.
                Snapshot stone = Snapshot.FromBitmap(day, new Rectangle(266, 122, 44, 60)), rock = Snapshot.FromBitmap(day, new Rectangle(592, 480, 34, 30));
                var others = new List<string>();
                double worst = 0;
                foreach (Point p in new[] { new Point(480, 250), new Point(1330, 545), new Point(700, 150), new Point(940, 500) })
                {
                    double s = stone.Similarity(Snapshot.FromBitmap(night, new Rectangle(p, new Size(44, 60))));
                    worst = Math.Max(worst, s);
                    others.Add(Math.Round(s * 100) + "%");
                }
                foreach (Point p in new[] { new Point(1020, 220), new Point(330, 480) })
                {
                    double s = rock.Similarity(Snapshot.FromBitmap(night, new Rectangle(p, new Size(34, 30))));
                    worst = Math.Max(worst, s);
                    others.Add(Math.Round(s * 100) + "%");
                }
                var spot = new Rectangle(919, 490, 106, 71);
                double elsewhere = Snapshot.FromBitmap(day, spot).Similarity(Snapshot.FromBitmap(night, spot));
                worst = Math.Max(worst, elsewhere);
                others.Add(Math.Round(elsewhere * 100) + "%");
                Check(worst < 0.4, "Other things at night don't match the stone, the rock or another spot taken by day: " + string.Join(", ", others));

                // Dusk and dawn taken as part of the way to the night, and a little past it, on a spot by a tree.
                var tree = new Rectangle(880, 420, 120, 90);
                Snapshot taken = Snapshot.FromBitmap(day, tree);
                var dusk = new List<string>();
                bool allMatch = true;
                foreach (double t in new[] { 0.3, 0.5, 0.7, 1.0, 1.2 })
                {
                    using (Bitmap dark = Tinted(day, tree, 1 - t * 0.60, 1 - t * 0.48, 1 - t * 0.21))
                    {
                        Snapshot now = Snapshot.FromBitmap(dark, new Rectangle(Point.Empty, dark.Size));
                        double darker, lighter;
                        double a = taken.Similarity(now, null, out darker), b = now.Similarity(taken, null, out lighter);
                        dusk.Add(t.ToString("0.0") + ": " + Math.Round(a * 100) + "%/" + Math.Round(b * 100) + "%");
                        if (a < 0.85 || b < 0.85) allMatch = false;
                    }
                }
                Check(allMatch, "Part of the way to the night and a little past it, both ways: " + string.Join(", ", dusk));

                // An even darkening, as when the screen fades, isn't the night.
                using (Bitmap faded = Tinted(day, tree, 0.5, 0.5, 0.5))
                {
                    double darker;
                    double s = taken.Similarity(Snapshot.FromBitmap(faded, new Rectangle(Point.Empty, faded.Size)), null, out darker);
                    Check(darker == 0, "Evenly darkened to half isn't taken for the night (" + Math.Round(s * 100) + "% as taken)");
                }
            }

            // Buttons dimmed while the game animates: scored as taken, never as the night. Lit is from radiant.jpg.
            Bitmap lit = Load(dir, "radiant.jpg");
            if (lit == null) return;
            using (lit)
            {
                string[][] dims =
                {
                    new[] { "Run", "212", "912", "54", "55", "duo-turn-bonwing.png" },
                    new[] { "Run", "212", "912", "54", "55", "duo-target-lithorse.png" },
                    new[] { "Strike", "528", "876", "120", "26", "duo-turn-bonwing.png" },
                    new[] { "Pyrecrest Assault", "884", "876", "120", "26", "duo-turn-bonwing.png" },
                    new[] { "Heat Siphon", "1240", "876", "120", "26", "duo-target-lithorse.png" },
                    new[] { "Capture", "1400", "806", "120", "30", "duo-target-lithorse.png" },
                };
                foreach (string[] d in dims)
                {
                    Bitmap dim = Load(dir, d[5]);
                    if (dim == null) continue;
                    using (dim)
                    {
                        var r = new Rectangle(int.Parse(d[1]), int.Parse(d[2]), int.Parse(d[3]), int.Parse(d[4]));
                        double darker;
                        double s = Snapshot.FromBitmap(lit, r).Similarity(Snapshot.FromBitmap(dim, r), null, out darker);
                        Check(darker == 0, d[0] + " lit against dimmed (" + d[5] + "): " + Math.Round(s * 100) + "%, as taken, not as the night");
                    }
                }
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

                Console.WriteLine("\n== Radiants only: the radiant check's step is the Run button");
                Action<CatchOptions> radiantsOnly = c => c.RadiantsOnly = true;
                Played runs = Play(g => { g.Radiant = false; g.NextFights.Enqueue(false); g.NextFights.Enqueue(false); }, radiantsOnly, runStep: true);
                Thread.Sleep(1000); // names are read on the side
                Console.WriteLine("      " + string.Join(", ", runs.Game.Trace) + "; from each run to the next stone: " + string.Join(", ", runs.Game.RunToStoneMs) + " ms");
                Check(runs.Game.Runs == 3 && runs.Game.Strikes + runs.Game.Captures + runs.Game.AttackClicks == 0 && runs.Game.Other.Count == 0,
                      "Three normal Lumits in a row, each run from, with nothing else clicked: " + string.Join("; ", runs.Game.Other));
                Check(runs.Counter.Total == 3, "Each fight was counted as a new one (" + runs.Counter.Total + " of 3)");
                Check(runs.Game.RunToStoneMs.Count == 3 && runs.Game.RunToStoneMs.All(ms => ms < 3000), "After each run it went straight on to the stone, without waiting 5 s for Continue");
                Check(runs.Activity.Any(a => a.Contains("no radiant, so step 2 (Run)")), "The status line says why it ran");
                Check(runs.Result.Problem == null, "The run carried on until the test stopped it");

                LumitCounter.Entry met = runs.Counter.Entries().FirstOrDefault();
                if (met != null) runs.Counter.SetCatch(met.Name, true);
                Played ticked = Play(g => { g.Radiant = false; }, c => { c.RadiantsOnly = true; c.CatchOn = true; }, runStep: true, counter: runs.Counter);
                Check(met != null && ticked.Game.Runs == 1 && ticked.Game.Strikes + ticked.Game.Captures == 0 && ticked.Game.Other.Count == 0,
                      "A Lumit ticked for catching (" + (met == null ? "none" : met.Name) + ") is run from too");

                Played radiantOnly = Play(g => { }, radiantsOnly, runStep: true);
                Console.WriteLine("      " + string.Join(", ", radiantOnly.Game.Trace));
                Check(radiantOnly.Game.Kept && !radiantOnly.Game.Killed && radiantOnly.Game.Runs == 0 && radiantOnly.Game.Other.Count == 0, "A radiant: Strike and Capture, kept, never run from");
                Check(radiantOnly.Game.HpAtFirstCapture > 0 && radiantOnly.Game.HpAtFirstCapture <= 37.5, "It stopped striking at about 36 HP or lower (" + Math.Round(radiantOnly.Game.HpAtFirstCapture) + ")");

                Played later = Play(g => { g.Radiant = false; g.NextFights.Enqueue(false); g.NextFights.Enqueue(true); }, radiantsOnly, runStep: true);
                Console.WriteLine("      " + string.Join(", ", later.Game.Trace));
                Check(later.Game.Runs == 2 && later.Game.Kept && !later.Game.Killed && later.Game.Other.Count == 0, "Two runs, then a radiant in the third fight is still seen, caught and kept");

                Played failed = Play(g => { g.Radiant = false; g.FailRuns = 1; }, radiantsOnly, runStep: true);
                Console.WriteLine("      " + string.Join(", ", failed.Game.Trace));
                Check(failed.Game.Runs == 2 && failed.Game.Strikes + failed.Game.Captures == 0 && failed.Game.Other.Count == 0, "A run that fails is simply tried again");

                Played noCatching = Play(g => { }, c => { c.RadiantsOnly = true; c.CatchRadiants = false; }, runStep: true);
                Console.WriteLine("      " + noCatching.Result.Problem);
                Check(noCatching.Result.RadiantFound && noCatching.Game.Trace.Count == 0 && noCatching.Game.Runs == 0, "Catch radiants off: a radiant stops the run, with nothing clicked");

                string dir = Path.GetDirectoryName(Path.GetFullPath(args[0]));
                DuoSpots(dir);
                NightLight(dir);

                Console.WriteLine("\n== A PC 2v2 (Bonwing and Musingens), radiants only");
                Bitmap bonwing = Load(dir, "duo-turn-bonwing.png");
                if (bonwing == null) Check(false, "duo-turn-bonwing.png is missing beside radiant.jpg");
                else
                {
                    Bitmap duoTurn = WithMenu(bonwing, radiantPic);
                    Dictionary<string, Rectangle> pc = CatchSpots.Resolve(new Size(1920, 1025), null);
                    Played duoRun = Play(g => { g.Duo = duoTurn; g.Radiant = false; }, radiantsOnly, runStep: true);
                    Console.WriteLine("      " + string.Join(", ", duoRun.Game.Trace) + "; app: " + string.Join(" / ", duoRun.Activity.Where(a => a.Contains("Run"))));
                    Check(duoRun.Game.Runs == 1 && duoRun.Game.Strikes + duoRun.Game.Captures + duoRun.Game.AttackClicks == 0 && duoRun.Game.Other.Count == 0 && duoRun.Result.Problem == null,
                          "No radiant: Run, and nothing else clicked: " + string.Join("; ", duoRun.Game.Other));
                    Check(duoRun.Activity.Any(a => a.Contains("no radiant in this 2v2")), "It was seen as a 2v2");
                    Check(duoRun.Counter.Total == 2, "Both wild Lumits were counted (" + duoRun.Counter.Total + " of 2)");
                    foreach (int slot in new[] { 0, 1 })
                    {
                        Bitmap gilded = Gild(duoTurn, pc[slot == 0 ? CatchSpots.Enemy1Card : CatchSpots.Enemy2Card]);
                        Played p = Play(g => { g.Duo = gilded; g.Radiant = true; }, radiantsOnly, runStep: true);
                        Console.WriteLine("      " + p.Result.Problem);
                        Check(p.Result.RadiantFound && p.Game.Trace.Count == 0 && p.Game.Runs == 0 && p.Game.Other.Count == 0 && p.Result.RadiantPlace.Contains(slot == 0 ? "left" : "right"),
                              "A radiant " + (slot == 0 ? "upper" : "lower") + " card: the run stops with nothing clicked, saying it's " + (slot == 0 ? "the left one" : "the right one"));
                    }
                    Bitmap lowerGold = Gild(duoTurn, pc[CatchSpots.Enemy2Card]);
                    Played fighting = Play(g => { g.Duo = lowerGold; g.Radiant = true; });
                    Check(fighting.Result.RadiantFound && fighting.Game.Trace.Count == 0 && fighting.Game.AttackClicks == 0 && fighting.Game.Other.Count == 0,
                          "Without radiants only too, a radiant in a PC 2v2 stops the run before anything is clicked");
                }

                Console.WriteLine("\n== The window's text fits");
                using (var font = new Font("Segoe UI", 9F))
                {
                    int width = TextRenderer.MeasureText("Radiant check before step 2, catches them   •   phone alerts by Discord (radiants and stops)", font).Width;
                    Check(width <= 512, "The longest alert line is " + width + " px, with 512 px before the Alerts button");
                    int catching = TextRenderer.MeasureText("Catching 12 kinds (practice)   •   heals below 182 HP   •   12 perfect", font).Width;
                    int radiantLine = TextRenderer.MeasureText("Radiants only, runs from the rest   •   heals below 182 HP   •   12 perfect", font).Width;
                    Check(radiantLine <= 396, "The radiants-only catching line is " + radiantLine + " px (catching's longest: " + catching + "), with 396 px before the Catching button");
                    int tick = TextRenderer.MeasureText("Radiants only: no fighting, it runs from every Lumit that isn't a radiant", font).Width + 20;
                    Check(tick <= 552, "The radiants-only tick is " + tick + " px wide, in a 566 px dialog from x 14");
                    int stepNote = TextRenderer.MeasureText("(the Run step, radiants only)", font).Width;
                    Check(stepNote <= 180, "The Alerts step note is " + stepNote + " px, with 180 px left in its box");
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
