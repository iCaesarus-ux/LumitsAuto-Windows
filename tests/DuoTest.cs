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
    /// Radiant checks in PC 2v2s: the 2v2 spots on screenshots of the owner's game (1920 × 1025, two cards a side, one above
    /// the other), then the real ClickEngine against a pretend game window placed off-screen. A radiant in a 2v2 hasn't been
    /// seen yet, so one is made by copying the owner's own radiant's gold name (Minoking, your upper card in these pictures)
    /// over a wild Lumit's name. Nothing is clicked in your game and the real pointer doesn't move.
    ///   DuoTest folderOfPcPictures   (tests\pictures\pc)
    /// </summary>
    static class DuoTest
    {
        const int W = 1920, H = 1025;
        static int failures;
        static string tempDir;
        /// <summary>radiant.jpg: a 1v1 with the fight menu lit for your turn. Its menu goes over the 2v2 pictures.</summary>
        static Bitmap lit;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        /// <summary>How the run is set up: radiants only; catching on (radiants caught); or catching off, a radiant only stopping the run.</summary>
        enum Mode { RadiantsOnly, Catching, StopOnly }

        /// <summary>The pretend game: the map, a fight, or Victory, reacting to clicks.</summary>
        class Game : Form
        {
            public enum Screen { Map, Fight, Victory }

            /// <summary>The stone on the map that starts the next fight, the Run button (the boot, bottom left) and the attack step.</summary>
            public static readonly Rectangle Stone = new Rectangle(900, 500, 40, 40), RunButton = new Rectangle(212, 914, 50, 50),
                                             AttackStep = new Rectangle(1240, 932, 120, 26);

            public Screen State = Screen.Fight;
            /// <summary>The fight on screen, and the ones after it, each started by clicking the stone.</summary>
            public Bitmap Fight;
            public readonly Queue<Bitmap> NextFights = new Queue<Bitmap>();
            public volatile bool Finished;
            public int Runs, Attacks, Fights = 1;
            public readonly List<string> Trace = new List<string>();
            /// <summary>Clicks that had no business happening.</summary>
            public readonly List<string> Odd = new List<string>();

            readonly object sync = new object();
            Bitmap shown;

            public Game()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-6000, -6000, W, H);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
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

            Bitmap Picture()
            {
                var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    if (State == Screen.Map) g.FillRectangle(Brushes.Olive, 0, 0, W, H);
                    else if (State == Screen.Victory) g.FillRectangle(Brushes.Teal, 0, 0, W, H);
                    else g.DrawImage(Fight, new Rectangle(0, 0, W, H));
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

            void Clicked(Point p)
            {
                switch (State)
                {
                    case Screen.Map:
                        if (!Stone.Contains(p))
                        {
                            Odd.Add("map (" + p.X + "," + p.Y + ")");
                            break;
                        }
                        if (NextFights.Count == 0)
                        {
                            Finished = true;
                            break;
                        }
                        Fight = NextFights.Dequeue();
                        Fights++;
                        Trace.Add("fight " + Fights);
                        State = Screen.Fight;
                        break;
                    case Screen.Fight:
                        if (RunButton.Contains(p))
                        {
                            Runs++;
                            Trace.Add("Run");
                            State = Screen.Map;
                        }
                        else if (AttackStep.Contains(p))
                        {
                            Attacks++;
                            Trace.Add("attack");
                            State = Screen.Victory;
                        }
                        else Odd.Add("(" + p.X + "," + p.Y + ") in a fight");
                        break;
                    case Screen.Victory:
                        // The step's Continue.
                        State = Screen.Map;
                        if (NextFights.Count == 0) Finished = true;
                        break;
                }
                Redraw();
            }
        }

        class Played
        {
            public Game Game;
            public RunResult Result;
            public LumitCounter Counter;
            public List<string> Activity = new List<string>();
        }

        static GameWindow WindowFor(IntPtr handle)
        {
            ConstructorInfo make = typeof(GameWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First();
            return (GameWindow)make.Invoke(new object[] { handle, "Pretend game", "test" });
        }

        /// <summary>
        /// A run on the pretend game, which starts on <paramref name="first"/>. Steps: the stone on the map, the fight's step (the
        /// attack, or the Run button in radiants-only runs, which is the radiant check's step) and Victory's Continue.
        /// </summary>
        static Played Play(Mode mode, Bitmap first, Bitmap[] after = null, bool catchRadiants = true)
        {
            Dictionary<string, Rectangle> spots = CatchSpots.Resolve(new Size(W, H), null);
            Game game = null;
            var ready = new ManualResetEvent(false);
            var ui = new Thread(() =>
            {
                game = new Game { Fight = first };
                if (after != null) foreach (Bitmap b in after) game.NextFights.Enqueue(b);
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

            var corner = new Rectangle(20, 300, 60, 60);
            Rectangle watch = spots[CatchSpots.Capture];
            Rectangle fightStep = mode == Mode.RadiantsOnly ? Game.RunButton : Game.AttackStep;
            Snapshot mapLook, victoryLook;
            using (var b = new Bitmap(W, H))
            {
                using (Graphics g = Graphics.FromImage(b)) g.FillRectangle(Brushes.Olive, 0, 0, W, H);
                mapLook = Snapshot.FromBitmap(b, corner);
                using (Graphics g = Graphics.FromImage(b)) g.FillRectangle(Brushes.Teal, 0, 0, W, H);
                victoryLook = Snapshot.FromBitmap(b, corner);
            }
            Func<Rectangle, Rectangle, Snapshot, ClickPoint> step = (click, area, look) => new ClickPoint
            {
                X = click.X, Y = click.Y, Width = click.Width, Height = click.Height, WaitForScreen = true, WatchArea = area, Snapshot = look,
                ReactMinMs = 30, ReactMaxMs = 60, DelayMs = 100, TimeoutSec = 20
            };
            var played = new Played { Game = game, Counter = new LumitCounter(Path.Combine(tempDir, "lumits-" + Guid.NewGuid().ToString("N") + ".txt")) };
            var run = new RunOptions
            {
                Points = new[] { step(Game.Stone, corner, mapLook), step(fightStep, watch, Snapshot.FromBitmap(lit, watch)), step(Game.Stone, corner, victoryLook) },
                Space = new StepSpace(WindowFor(handle)),
                Background = true,
                IntervalMs = 100,
                HumanMouse = false,
                RedoPrevious = true,
                NameStep = 1,
                NameArea = GoldText.LumitsNameArea,
                RadiantCheck = true,
                AlertGoldPercent = 2,
                Counter = played.Counter,
                Catch = mode == Mode.StopOnly ? null : new CatchOptions
                {
                    CatchOn = mode == Mode.Catching,
                    CatchRadiants = catchRadiants,
                    RadiantsOnly = mode == Mode.RadiantsOnly,
                    HealBelowHp = 0,
                    LeadMaxHp = 182,
                    BorrowForTargets = false, // the test must never move your real pointer
                    DragRealPointer = false,
                    Spots = spots,
                    Log = new CatchLog(tempDir)
                },
                DuoSpots = spots // as the main window passes them whenever there's a game window
            };
            var engine = new ClickEngine();
            var finished = new ManualResetEvent(false);
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
                if (clock.Elapsed.TotalSeconds > 60)
                {
                    Check(false, "A run had to be stopped from outside. " + string.Join(", ", game.Trace));
                    engine.Stop();
                }
            }
            game.Invoke((MethodInvoker)delegate { game.Close(); });
            return played;
        }

        /// <summary>Nothing at all was clicked in the fight: no move, no Run, no target, no stray click.</summary>
        static bool Untouched(Played p)
        {
            return p.Game.Trace.Count == 0 && p.Game.Odd.Count == 0;
        }

        /// <summary>The run stopped for a radiant in the given card (0 upper, 1 lower), with a picture for your phone.</summary>
        static bool StoppedFor(Played p, int slot)
        {
            return p.Result.RadiantFound && p.Result.RadiantPlace.Contains(slot == 0 ? "upper card" : "lower card") &&
                   p.Result.Screenshot != null && p.Result.Screenshot.Length > 0;
        }

        static string Card(int slot)
        {
            return slot == 0 ? "upper card (left one)" : "lower card (right one)";
        }

        // ---------------------------------------------------------------- pictures

        /// <summary>A picture from the folder, at 1025 px high like the game window, or null if it isn't there.</summary>
        static Bitmap Load(string dir, string name)
        {
            string file = Path.Combine(dir, name);
            if (!File.Exists(file)) return null;
            using (var raw = new Bitmap(file))
            {
                if (raw.Height == H) return new Bitmap(raw);
                int width = (int)Math.Round(raw.Width * (double)H / raw.Height);
                var b = new Bitmap(width, H, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(raw, new Rectangle(0, 0, width, H));
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
            var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.DrawImage(duo, new Rectangle(0, 0, W, H));
                var part = new Rectangle(0, 790, W, 235);
                g.DrawImage(menu, part, part, GraphicsUnit.Pixel);
            }
            return b;
        }

        /// <summary>A copy with an HP bar painted empty.</summary>
        static Bitmap EmptyBar(Bitmap source, Rectangle bar)
        {
            var b = new Bitmap(source);
            using (Graphics g = Graphics.FromImage(b))
            using (var dark = new SolidBrush(Color.FromArgb(30, 30, 35)))
                g.FillRectangle(dark, bar);
            return b;
        }

        // ---------------------------------------------------------------- the spots, on pictures

        /// <summary>
        /// The PC's 2v2 spots on pictures of the owner's game. Each 2v2 must be seen as one, both names read, and either name turned
        /// gold found; no 1v1 may look like a 2v2. Also the Alerts window's Test now.
        /// </summary>
        static void Spots(string dir)
        {
            Console.WriteLine("== The PC's 2v2 spots, on pictures of the game");
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
                    Check(false, d[0] + " is missing");
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

            // The 1v1 name area, which the radiant check reads in a 1v1, misses both of a 2v2's names: 2v2s need their own check.
            Dictionary<string, Rectangle> pc = CatchSpots.Resolve(new Size(W, H), null);
            Rectangle oneName = GoldText.LumitsNameArea;
            Func<string, double> covered = key =>
            {
                Rectangle card = pc[key], both = Rectangle.Intersect(oneName, card);
                return both.Width * both.Height / (double)(card.Width * card.Height);
            };
            double upperCovered = covered(CatchSpots.Enemy1Card), lowerCovered = covered(CatchSpots.Enemy2Card);
            Check(upperCovered < 0.05 && lowerCovered < 0.05, "The 1v1 name area covers " + Math.Round(upperCovered * 100) + "% of the upper 2v2 name card and " +
                  Math.Round(lowerCovered * 100) + "% of the lower one, so 2v2s are checked by their own spots");
        }

        [STAThread]
        static int Main(string[] args)
        {
            Native.EnableDpiAwareness();
            if (args.Length < 1)
            {
                Console.WriteLine("DuoTest folderOfPcPictures   (tests\\pictures\\pc)");
                return 2;
            }
            string dir = args[0];
            tempDir = Path.Combine(Path.GetTempPath(), "lumits-duo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                Spots(dir);

                lit = Load(dir, "radiant.jpg");
                Bitmap normal = Load(dir, "game-turn-lowmp.png");
                string[] names = { "duo-turn-bonwing.png", "duo-target-lithorse.png", "duo-start-gryphair.png" };
                var duos = new List<Bitmap>();
                foreach (string n in names)
                {
                    Bitmap raw = Load(dir, n);
                    if (raw != null) duos.Add(WithMenu(raw, lit));
                }
                if (lit == null || normal == null || duos.Count < names.Length)
                {
                    Check(false, "radiant.jpg, game-turn-lowmp.png and the three duo-*.png pictures must be in " + dir);
                    return 1;
                }
                Dictionary<string, Rectangle> pc = CatchSpots.Resolve(new Size(W, H), null);
                string[] cards = { CatchSpots.Enemy1Card, CatchSpots.Enemy2Card };
                Bitmap bonwing = duos[0];

                Console.WriteLine("\n== Radiants only, no radiant: two 2v2s in a row");
                Played runs = Play(Mode.RadiantsOnly, bonwing, new[] { duos[2] });
                Console.WriteLine("      " + string.Join(", ", runs.Game.Trace) + "; app: " + string.Join(" / ", runs.Activity.Where(a => a.Contains("Run"))));
                Check(runs.Game.Runs == 2 && runs.Game.Attacks == 0 && runs.Game.Odd.Count == 0 && runs.Result.Problem == null,
                      "Each 2v2 got the Run step, and nothing else was clicked: " + string.Join("; ", runs.Game.Odd));
                Check(runs.Counter.Total == 4, "Both wild Lumits of each 2v2 were counted (" + runs.Counter.Total + " of 4)");
                Check(runs.Activity.Any(a => a.Contains("no radiant in this 2v2")), "The status line says it ran from a 2v2 with no radiant");

                Console.WriteLine("\n== Radiants only, a radiant in a 2v2");
                for (int d = 0; d < duos.Count; d++)
                {
                    for (int slot = 0; slot < 2; slot++)
                    {
                        Played p = Play(Mode.RadiantsOnly, Gild(duos[d], pc[cards[slot]]));
                        Console.WriteLine("      " + names[d] + ", " + Card(slot) + ": " + p.Result.Problem);
                        Check(StoppedFor(p, slot) && Untouched(p), names[d] + ", gold " + Card(slot) + ": the run stops, nothing clicked, saying which one, with a picture");
                        if (d == 0 && slot == 1)
                            Check(p.Counter.Entries().Any(e => e.Radiants > 0), "The radiant is in the counts as a radiant");
                    }
                }
                Played both = Play(Mode.RadiantsOnly, Gild(Gild(bonwing, pc[cards[0]]), pc[cards[1]]));
                Console.WriteLine("      both gold: " + both.Result.Problem);
                Check(both.Result.RadiantFound && Untouched(both), "Both names gold: the run stops, nothing clicked");
                Played catchOff = Play(Mode.RadiantsOnly, Gild(bonwing, pc[cards[1]]), catchRadiants: false);
                Check(StoppedFor(catchOff, 1) && Untouched(catchOff), "Catch radiants off: the same");

                Console.WriteLine("\n== Catching on (not radiants only), a radiant in a 2v2");
                for (int slot = 0; slot < 2; slot++)
                {
                    Played p = Play(Mode.Catching, Gild(bonwing, pc[cards[slot]]));
                    Console.WriteLine("      " + Card(slot) + ": " + p.Result.Problem);
                    Check(StoppedFor(p, slot) && Untouched(p), "Gold " + Card(slot) + ": the run stops before anything is clicked (2v2 catching isn't checked on the PC yet)");
                }

                Console.WriteLine("\n== Catching off: the radiant check just stops the run");
                for (int slot = 0; slot < 2; slot++)
                {
                    Played p = Play(Mode.StopOnly, Gild(bonwing, pc[cards[slot]]));
                    Console.WriteLine("      " + Card(slot) + ": " + p.Result.Problem);
                    Check(StoppedFor(p, slot) && Untouched(p), "Gold " + Card(slot) + ": the run stops, nothing clicked, saying which one");
                }
                Played plain = Play(Mode.StopOnly, bonwing);
                Console.WriteLine("      no radiant: " + string.Join(", ", plain.Game.Trace));
                Check(plain.Game.Attacks == 1 && plain.Game.Odd.Count == 0 && plain.Result.Problem == null, "No radiant: the attack step is clicked as usual");
                Check(plain.Counter.Total == 2, "Both wild Lumits were counted (" + plain.Counter.Total + " of 2)");

                Console.WriteLine("\n== A wild Lumit's bar reads empty as the 2v2 starts, its name gold");
                Bitmap faint = EmptyBar(Gild(bonwing, pc[cards[1]]), pc[CatchSpots.Enemy2Bar]);
                foreach (Mode mode in new[] { Mode.RadiantsOnly, Mode.StopOnly })
                {
                    Played p = Play(mode, faint);
                    Console.WriteLine("      " + mode + ": " + (p.Result.Problem ?? string.Join(", ", p.Game.Trace)));
                    Check(StoppedFor(p, 1) && Untouched(p), mode + ": its name is still checked, and the run stops with nothing clicked");
                }

                Console.WriteLine("\n== 1v1s and 2v2s mixed (radiants only, catch radiants off)");
                Played oneThenTwo = Play(Mode.RadiantsOnly, normal, new[] { Gild(bonwing, pc[cards[1]]) }, catchRadiants: false);
                Console.WriteLine("      " + string.Join(", ", oneThenTwo.Game.Trace) + "; " + oneThenTwo.Result.Problem);
                Check(oneThenTwo.Game.Runs == 1 && oneThenTwo.Game.Odd.Count == 0 && StoppedFor(oneThenTwo, 1),
                      "A 1v1 run from, then a radiant in the next fight's 2v2: stopped, saying it's the lower card");
                Played twoThenOne = Play(Mode.RadiantsOnly, bonwing, new[] { lit }, catchRadiants: false);
                Console.WriteLine("      " + string.Join(", ", twoThenOne.Game.Trace) + "; " + twoThenOne.Result.Problem);
                Check(twoThenOne.Game.Runs == 1 && twoThenOne.Game.Odd.Count == 0 && twoThenOne.Result.RadiantFound && twoThenOne.Result.RadiantPlace == "",
                      "A 2v2 run from, then a radiant in a 1v1: stopped as a 1v1's radiant");
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
