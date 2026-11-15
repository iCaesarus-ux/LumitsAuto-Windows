using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>Runs the steps on a background thread so the window and hotkeys stay responsive.</summary>
    public class ClickEngine
    {
        /// <summary>Thrown inside the worker to end a run. Problem is shown to the user, or null for an ordinary stop.</summary>
        class StopRun : Exception
        {
            public readonly string Problem;
            public bool Radiant;
            public string RadiantName = "";
            /// <summary>Where the radiant is, when it isn't a 1v1's only wild Lumit ("in a 2v2, the left one"); "" otherwise.</summary>
            public string RadiantPlace = "";
            public byte[] Screenshot;
            /// <summary>Worth telling your phone about (not when you yourself caused the stop).</summary>
            public bool Notify = true;
            /// <summary>For stops you always want to hear about: the phone alert's title.</summary>
            public string AlertTitle;

            public StopRun(string problem)
            {
                Problem = problem;
            }
        }

        const int PollMs = 100;
        const int UserMoveTolerancePx = 25;
        const int FrameReuseMs = 60;
        /// <summary>Another step's screen showing this long means the game is there, not where the run expected.</summary>
        const int StuckMs = 5000;
        /// <summary>Time-outs in a row with no step recognisable before the run gives up.</summary>
        const int MaxMissedTimeouts = 3;

        readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        Thread worker;
        volatile bool running;

        // State of the current run, only touched by the worker thread.
        RunOptions o;
        Random rnd;
        Stopwatch clock;
        long clicks;
        int cycle;
        int countedCycle;      // the cycle whose lumit has been counted
        string activity;
        long lastReportMs;
        bool watchUserMouse;
        Point restPos;
        int screenFailures;
        GameWindow window;     // set when step positions are measured from a game window
        Point virtualPos;      // background mode: where the game believes the pointer is
        Bitmap frame;          // latest capture of the game window, shared by checks made close together
        long frameAt;

        // Catching and healing (only with RunOptions.Catch).
        string leadName;       // your fighting lumit, read off its card on the first turn
        int leadHp;            // its HP at your last turn; -1 = not known yet
        int fightCycle;        // the cycle whose fight the fields below describe
        string fightLumit;     // the enemy's entry in the counter
        bool catchThis;        // this fight's enemy is to be caught
        int siphons;           // Heat Siphons used this fight
        double siphonBefore;   // the enemy's HP bar before the last Heat Siphon; -1 = none pending
        bool captureTried;     // a Capture was clicked and its result isn't handled yet
        long nextCaughtCheck, nextFaintCheck, nextChoiceCheck;
        string faintSuspect;   // another Lumit's name seen once on your card; a second sighting confirms the faint
        bool leadFainted;      // your Lumit fainted: a stand-in captures until the fight ends, then Bark Tea heals it
        int radiantCycle;      // the cycle whose enemy is a radiant being caught
        string radiantName;    // its name as read off its card; "" if it couldn't be
        bool radiantCaught;
        readonly RadiantStrikes strikes = new RadiantStrikes();
        int duoCycle;          // the cycle whose fight is a 2v2
        int duoCheckedCycle;   // catching off: the cycle whose 2v2 had its names checked for a radiant
        readonly Foe[] foes = new Foe[2];
        string duoLead, duoPartner; // your two Lumits' names, from "Choose an action for …"
        bool standInPending;   // a Lumit fainted in a 2v2, so the next new name choosing is a stand-in's
        readonly List<Foe> caughtOrder = new List<Foe>(); // caught in earlier rounds, in the order their caught screens come
        int ranCycle;          // radiants only: the cycle whose fight was run from

        // Step 1's pictures retaken from the screen when nothing known showed up: the map may only look different, say with
        // your character standing on the watched spot. They count beside the step's own for the rest of the run, and are
        // never put into your steps (one with your character in it would stop fitting once it moves).
        readonly List<Snapshot> firstStepExtra = new List<Snapshot>();
        long retakenAt;        // when the last one was taken and clicked, until another step's screen shows something happened; -1 = not waiting
        /// <summary>Step 1's retaken pictures kept at most; the oldest goes first.</summary>
        const int MaxExtraPictures = 8;
        /// <summary>Radiants only: after a run only the map can come, so step 1's picture is retaken after this long without it.</summary>
        const int RetakeAfterRunMs = 10000;

        /// <summary>No fighting: every Lumit but a radiant gets the radiant check's step, which is the Run button.</summary>
        bool RadiantsOnly { get { return o.Catch != null && o.Catch.RadiantsOnly; } }

        /// <summary>Raised on the worker thread a few times a second: (clicks so far, current cycle, what it's doing).</summary>
        public event Action<long, int, string> Progress;

        /// <summary>Raised on the worker thread when a run ends.</summary>
        public event Action<RunResult> Finished;

        /// <summary>Raised on the worker thread for news worth a phone alert while the run goes on: (title, message, JPEG or null).</summary>
        public event Action<string, string, byte[]> Notice;

        public bool IsRunning { get { return running; } }

        public bool Start(RunOptions options)
        {
            if (running) return false;
            if (worker != null && worker.IsAlive && !worker.Join(1000)) return false;

            stopSignal.Reset();
            running = true;
            worker = new Thread(() => Run(options)) { IsBackground = true, Name = "Clicker" };
            worker.Start();
            return true;
        }

        public void Stop()
        {
            stopSignal.Set();
        }

        public void StopAndWait(int timeoutMs)
        {
            stopSignal.Set();
            Thread t = worker;
            if (t != null && t.IsAlive) t.Join(timeoutMs);
        }

        void Run(RunOptions options)
        {
            o = options;
            rnd = new Random();
            clock = Stopwatch.StartNew();
            clicks = 0;
            cycle = 0;
            countedCycle = 0;
            activity = "";
            lastReportMs = 0;
            watchUserMouse = false;
            screenFailures = 0;
            leadName = null;
            leadHp = -1;
            fightCycle = 0;
            fightLumit = null;
            catchThis = captureTried = false;
            siphons = 0;
            siphonBefore = -1;
            nextCaughtCheck = nextFaintCheck = nextChoiceCheck = 0;
            faintSuspect = null;
            leadFainted = false;
            radiantCycle = 0;
            radiantName = "";
            radiantCaught = false;
            strikes.Clear();
            duoCycle = duoCheckedCycle = 0;
            duoLead = duoPartner = null;
            standInPending = false;
            caughtOrder.Clear();
            ranCycle = 0;
            firstStepExtra.Clear();
            retakenAt = -1;
            window = o.Space == null ? null : o.Space.Window;
            if (window != null)
            {
                Size size = window.ClientSize;
                virtualPos = new Point(size.Width / 2, size.Height / 2);
            }

            var result = new RunResult();
            // Default Windows sleep granularity is ~15.6 ms; ask for 1 ms so short waits are honoured.
            Native.timeBeginPeriod(1);
            try
            {
                RunSteps();
                result.Completed = true;
            }
            catch (StopRun stop)
            {
                result.Problem = stop.Problem;
                result.RadiantFound = stop.Radiant;
                result.RadiantName = stop.RadiantName;
                result.RadiantPlace = stop.RadiantPlace;
                result.AlertTitle = stop.AlertTitle;
                result.NotifyPhone = stop.Problem != null && stop.Notify;
                result.Screenshot = stop.Screenshot ?? (result.NotifyPhone ? Evidence() : null);
            }
            catch (Exception ex)
            {
                result.Problem = "unexpected error: " + ex.Message;
                result.NotifyPhone = true;
                result.Screenshot = Evidence();
            }
            finally
            {
                Native.timeEndPeriod(1);
                if (frame != null)
                {
                    frame.Dispose();
                    frame = null;
                }
                running = false;
                result.Clicks = clicks;
                result.Cycles = cycle;
                var finished = Finished;
                if (finished != null) finished(result);
            }
        }

        void RunSteps()
        {
            bool atCursor = o.Points.Length == 0;
            int steps = atCursor ? 1 : o.Points.Length;
            long nextBreak = NextBreakTime();
            int completed = 0;
            cycle = 1;
            int i = atCursor ? 0 : StepShowingNow();

            while (true)
            {
                ThrowIfStopped();
                ClickPoint p = atCursor ? null : o.Points[i];
                bool click = true;
                if (p != null && p.Waits)
                {
                    // The game may turn out to be on another step's screen; then carry on from that step.
                    int showing = WaitForScreen(i);
                    if (showing < 0)
                    {
                        click = false; // didn't appear, and the step is set to be skipped
                    }
                    else
                    {
                        // Going back to the last step from the start of a new pass means the previous
                        // pass never really finished (e.g. its Continue click didn't register).
                        if (i == 0 && showing == steps - 1 && completed > 0)
                        {
                            completed--;
                            cycle = completed + 1;
                        }
                        else if (showing == 0 && o.NameStep > 0 && i > o.NameStep)
                        {
                            // Back on the first step's screen after the fight's step: the fight is over without the
                            // steps left (as after running from it, with no Victory to Continue). That ends the pass,
                            // so the next fight is a new one: counted, checked for a radiant, and a 1v1 or 2v2 afresh.
                            CheckRadiantLost(0);
                            if (EndPass(ref completed, ref nextBreak)) return;
                            i = 0;
                            continue;
                        }
                        i = showing;
                        p = o.Points[i];
                        CheckRadiantLost(i);
                        CheckEnemy(i, false); // as early as possible, so a radiant alert goes out straight away
                        if (i == 0 && retakenAt < 0) MaybeHeal(); // on the map, before heading into the next fight (not on a hunch that it's the map)
                        SetActivity("");
                        Pause(Triangular(p.ReactMinMs, p.ReactMaxMs, 0.3)); // human reaction time
                    }
                }
                if (click)
                {
                    ClickStep(p, i);
                    if (i == steps - 1 && o.Cycles > 0 && completed + 1 >= o.Cycles) return;
                    Pause(NextDelay(p));
                }

                if (++i < steps) continue;

                // Finished a pass through the list.
                CheckRadiantLost(-1);
                i = 0;
                if (EndPass(ref completed, ref nextBreak)) return;
            }
        }

        /// <summary>A pass through the list is done: a break if one is due, and the next pass. True when it was the last one asked for.</summary>
        bool EndPass(ref int completed, ref long nextBreak)
        {
            completed++;
            if (o.Cycles > 0 && completed >= o.Cycles) return true;
            if (clock.ElapsedMilliseconds >= nextBreak)
            {
                TakeBreak();
                nextBreak = NextBreakTime();
                if (retakenAt >= 0) retakenAt = clock.ElapsedMilliseconds; // the break isn't time the game had to react
            }
            cycle = completed + 1;
            return false;
        }

        /// <summary>The first step whose screen is showing right now, so a run picks up where the game is; 0 if none is.</summary>
        int StepShowingNow()
        {
            for (int i = 0; i < o.Points.Length; i++)
            {
                if (!o.Points[i].Waits || !Matches(o.Points[i])) continue;
                if (i > 0) SetActivity("starting at step " + (i + 1) + ", its screen is showing");
                return i;
            }
            return 0;
        }

        // ---------------------------------------------------------------- enemy name card

        /// <summary>
        /// Before step <paramref name="i"/> (the attack) is clicked: if the enemy's name is gold, starts catching it
        /// or stops the run, and counts each battle's lumit once, right before the first attack.
        /// </summary>
        void CheckEnemy(int i, bool aboutToClick)
        {
            if (i != o.NameStep || radiantCycle == cycle || duoCycle == cycle || duoCheckedCycle == cycle) return;
            // Radiants only: running can't be undone, so the fight's menu has to be up before the names are judged. By
            // then the name cards have surely faded in, and a 2v2's bars show.
            if (RadiantsOnly && window != null && countedCycle != cycle) WaitUntil(() => YourTurnMenuLit(CurrentFrame()), 5000);
            if (window != null && DuoSpotsNow != null && countedCycle != cycle && IsDuo(CurrentFrame()))
            {
                if (o.Catch != null) StartDuo();
                else CheckDuoNames();
                return;
            }
            bool count = o.Counter != null && countedCycle != cycle;
            if (!o.RadiantCheck && !(count && aboutToClick)) return;

            Bitmap card = NameCard();
            double gold = card == null ? -1 : GoldText.Percent(card, new Rectangle(Point.Empty, card.Size));
            bool radiant = gold >= o.AlertGoldPercent;
            if (radiant && o.RadiantCheck)
            {
                string name = LumitNames.Read(card) ?? ""; // a few ms, worth it so the alert can say which lumit
                card.Dispose();
                if (count)
                {
                    countedCycle = cycle;
                    fightLumit = o.Counter.Record(name, true);
                }
                if (o.Catch == null || !o.Catch.CatchRadiants)
                    throw new StopRun("a radiant appeared (gold name) before step " + (i + 1) + ", nothing was clicked")
                    {
                        Radiant = true,
                        RadiantName = name,
                        Screenshot = Evidence()
                    };
                radiantCycle = cycle;
                radiantName = name;
                radiantCaught = false;
                strikes.Clear();
                int after = o.Counter != null ? o.Counter.LastRadiantAfter : 0;
                RaiseNotice(RadiantLabel() + " spotted!", (after > 0 ? "Found after " + after + (after == 1 ? " lumit. " : " lumits. ") : "") +
                            "Catching it at " + DateTime.Now.ToString("HH:mm") + ", using only Strike and Capture.");
                return;
            }
            if (count && aboutToClick)
            {
                countedCycle = cycle;
                if (o.Catch != null && o.Catch.CatchOn)
                {
                    // Whether to catch depends on the name, so it's needed now (a few ms).
                    string name = LumitNames.Read(card) ?? "";
                    if (card != null) card.Dispose();
                    fightLumit = o.Counter.Record(name, radiant);
                }
                else
                {
                    o.Counter.Add(card, radiant); // the name is read on another thread, so the click isn't held up
                }
                card = null;
            }
            if (card != null) card.Dispose();
            // Can't tell, so don't risk attacking: better an unneeded stop than a lost radiant.
            if (o.RadiantCheck && gold < 0) throw new StopRun("the enemy's name couldn't be checked for a radiant");
        }

        /// <summary>The enemy's name card as a picture of its own, or null if it couldn't be read.</summary>
        Bitmap NameCard()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (attempt > 0) Pause(100);
                if (window != null && !window.Exists) throw new StopRun("the game window was closed");
                Bitmap card = GoldText.Capture(o.Space, o.NameArea);
                if (card != null) return card;
            }
            return null;
        }

        string RadiantLabel()
        {
            return radiantName.Length > 0 ? "Radiant " + radiantName : "Radiant";
        }

        /// <summary>The fight has moved on from your turn without a catch, so the radiant is gone. That's worth stopping for.</summary>
        void CheckRadiantLost(int showing)
        {
            if (radiantCycle != cycle || radiantCaught || showing == o.NameStep) return;
            throw new StopRun(RadiantLabel() + " wasn't caught: the fight ended without a catch")
            {
                AlertTitle = "Radiant not caught",
                Screenshot = Evidence()
            };
        }

        byte[] Evidence()
        {
            try
            {
                using (Bitmap shot = window != null ? window.CaptureClient() : ScreenGrab.CaptureVirtualScreen())
                    return shot == null ? null : GoldText.ToJpeg(shot);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- catching

        Rectangle Spot(string key)
        {
            return o.Catch.Spots[key];
        }

        /// <summary>
        /// On your turn in a fight: the attack step itself, or Heat Siphon / Capture for an enemy that's to be
        /// caught. Heat Siphon first; after that, Capture as soon as another Heat Siphon would usually kill it.
        /// A radiant only gets Strike and Capture.
        /// </summary>
        ClickPoint ChooseAction(ClickPoint attack)
        {
            Bitmap shot = CurrentFrame();
            double enemy = ScreenText.BarFill(shot, Spot(CatchSpots.EnemyBar), ScreenText.IsRed);
            double lead = ScreenText.BarFill(shot, Spot(CatchSpots.LeadBar), ScreenText.IsRed);
            // With a stand-in fighting, the card shows the stand-in, not your Lumit.
            if (lead >= 0 && !leadFainted) leadHp = (int)Math.Round(lead * o.Catch.LeadMaxHp);
            if (leadName == null && !leadFainted)
            {
                string n = LumitNames.Clean(ScreenText.Read(shot, Spot(CatchSpots.LeadName), Ink.Light, 0, false) ?? "");
                if (n.Length >= 3) leadName = n;
            }

            if (fightCycle != cycle)
            {
                fightCycle = cycle;
                siphons = 0;
                siphonBefore = -1;
                captureTried = false;
                LumitCounter.Entry rules = o.Catch.CatchOn && o.Counter != null ? o.Counter.Lookup(fightLumit) : null;
                catchThis = rules != null && rules.Catch;
            }
            // What the last Heat Siphon took, for judging the next one.
            if (siphonBefore >= 0 && enemy >= 0)
            {
                double took = siphonBefore - enemy;
                if (took > 0.01 && fightLumit != null) o.Counter.AddSiphonDamage(fightLumit, took);
                siphonBefore = -1;
            }
            LumitCounter.Entry entry = o.Counter != null ? o.Counter.Lookup(fightLumit) : null;
            string who = entry == null ? "it" : entry.Name;
            // Radiants only: ticked Lumits are run from too.
            if (RadiantsOnly) catchThis = false;
            if (leadFainted && (radiantCycle == cycle || !RadiantsOnly))
            {
                // A stand-in is fighting: its moves aren't yours and could kill, so only Capture until the fight ends.
                captureTried = true;
                SetActivity("capturing " + who + " (" + (leadName ?? "your Lumit") + " fainted, a stand-in is fighting)");
                return SpotAction(attack, CatchSpots.Capture);
            }
            if (radiantCycle == cycle)
            {
                strikes.Observe(enemy);
                string radiantHp = enemy >= 0 ? " (HP " + Math.Round(enemy * 100) + "%" + (strikes.Damage > 0 ? ", Strike takes " + Math.Round(strikes.Damage * 100) + "%" : "") + ")" : "";
                if (strikes.ShouldStrike(enemy))
                {
                    strikes.Struck(enemy);
                    SetActivity("Strike on " + RadiantLabel() + radiantHp);
                    return SpotAction(attack, CatchSpots.Strike);
                }
                captureTried = true;
                SetActivity("capturing " + RadiantLabel() + radiantHp);
                return SpotAction(attack, CatchSpots.Capture);
            }
            if (!catchThis) return attack;

            int slot = entry == null ? LumitCounter.DefaultAttack : entry.Attack;
            string move = LumitCounter.LeadMoves[slot];
            double usual = entry == null ? 0 : entry.TypicalSiphon;
            bool capture;
            if (siphons == 0) capture = false;          // always weaken it first
            else if (enemy < 0 || usual <= 0) capture = true; // can't judge, so don't risk a kill
            else capture = enemy <= usual;              // crits aside, the next hit would kill it

            string hp = enemy >= 0 ? " (HP " + Math.Round(enemy * 100) + "%" + (usual > 0 ? ", " + move + " usually takes " + Math.Round(usual * 100) + "%" : "") + ")" : "";
            // A move greys out without the MP for it; clicking it would do nothing, so capture as it is. The menu may still be
            // fading in, so it only counts as greyed out once Strike is lit and the move stays dark for a moment.
            if (!capture && !MoveUsable(slot))
            {
                capture = true;
                hp += ", " + move + " is out of MP";
            }
            if (capture)
            {
                captureTried = true;
                SetActivity("capturing " + who + hp);
                return SpotAction(attack, CatchSpots.Capture);
            }
            siphonBefore = enemy;
            siphons++;
            SetActivity(move + " on " + who + hp);
            return RectAction(attack, MoveBox(slot));
        }

        /// <summary>The attack step's timing, clicking one of the catching spots instead.</summary>
        ClickPoint SpotAction(ClickPoint attack, string key)
        {
            return RectAction(attack, Spot(key));
        }

        ClickPoint RectAction(ClickPoint attack, Rectangle r)
        {
            ClickPoint action = attack.Clone();
            action.X = r.X;
            action.Y = r.Y;
            action.Width = r.Width;
            action.Height = r.Height;
            action.Shape = null; // a box of its own, not the attack step's lasso
            action.Button = MouseButton.Left;
            action.DoubleClick = false;
            action.BorrowPointer = false;
            return action;
        }

        /// <summary>One of the six moves, 0–5 from the top left along the rows: Strike, Pyrecrest Assault, Heat Siphon above.</summary>
        Rectangle MoveBox(int slot)
        {
            Rectangle first = Spot(CatchSpots.Strike), third = Spot(CatchSpots.Siphon);
            int rowGap = (int)Math.Round(56.0 * first.Height / 26);
            int column = slot % 3;
            int x = column == 0 ? first.X : column == 2 ? third.X : (first.X + third.X) / 2;
            return new Rectangle(x, first.Y + slot / 3 * rowGap, first.Width, first.Height);
        }

        bool MoveUsable(int slot)
        {
            if (slot == 0) return true; // Strike needs no MP
            long end = clock.ElapsedMilliseconds + 1200;
            while (true)
            {
                Bitmap shot = CurrentFrame();
                if (ScreenText.Lit(shot, MoveBox(slot))) return true;
                bool menuReady = ScreenText.Lit(shot, Spot(CatchSpots.Strike), 2.5);
                if (menuReady && clock.ElapsedMilliseconds > end) return false;
                if (clock.ElapsedMilliseconds > end + 3000) return true; // the menu never settled: try the move as usual
                Pause(150);
            }
        }

        // ---------------------------------------------------------------- 2v2

        /// <summary>One of the two wild Lumits in a 2v2, and what's been done to it.</summary>
        class Foe
        {
            public int Slot;
            public string Entry = "";
            public bool Radiant, Catch, Gone, CaptureThrown, Matched;
            public double Hp = -1, LeadBefore = -1, PartnerBefore = -1;
            public int LeadHits, PartnerHits;
            public readonly RadiantStrikes Strikes = new RadiantStrikes();

            public string Name { get { return Entry.Length > 0 ? Entry : Slot == 0 ? "the left one" : "the right one"; } }
        }

        const int Lead = 0, Partner = 1, StandIn = 2;
        /// <summary>A Lumit at this much HP or more hasn't been hit yet, so the first move is always a weakening one.</summary>
        const double Unhurt = 0.95;

        /// <summary>A 2v2 fight: both your Lumits' HP bars on their small cards, and a wild one's at least.</summary>
        bool IsDuo(Bitmap shot)
        {
            return CatchSpots.DuoShowing(shot, DuoSpotsNow);
        }

        /// <summary>A 2v2's spots: catching's, or the run's own for the radiant check when catching is off; null without a game window.</summary>
        Dictionary<string, Rectangle> DuoSpotsNow
        {
            get { return o.Catch != null ? o.Catch.Spots : o.DuoSpots; }
        }

        double DuoBar(Bitmap shot, string key)
        {
            return CatchSpots.DuoBar(shot, DuoSpotsNow, key);
        }

        /// <summary>A wild Lumit's name card in a 2v2 (slot 0 the upper card, the left one on the field): its name, and how gold it is.</summary>
        string DuoCard(Bitmap shot, int slot, out double gold)
        {
            Rectangle card = Rectangle.Intersect(DuoSpotsNow[slot == 0 ? CatchSpots.Enemy1Card : CatchSpots.Enemy2Card], new Rectangle(Point.Empty, shot.Size));
            gold = 0;
            if (card.Width <= 0 || card.Height <= 0) return "";
            using (Bitmap c = shot.Clone(card, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                gold = GoldText.Percent(c, new Rectangle(Point.Empty, c.Size));
                return LumitNames.Read(c) ?? "";
            }
        }

        /// <summary>
        /// A 2v2 with catching off: both wild names are checked for gold and counted, and the step is clicked as it is. A radiant
        /// stops the run before anything is clicked.
        /// </summary>
        void CheckDuoNames()
        {
            duoCheckedCycle = countedCycle = cycle;
            Bitmap shot = CurrentFrame();
            var names = new string[2];
            var radiant = new bool[2];
            for (int slot = 0; slot < 2; slot++)
            {
                double gold;
                names[slot] = DuoCard(shot, slot, out gold);
                radiant[slot] = o.RadiantCheck && gold >= o.AlertGoldPercent;
                bool there = DuoBar(shot, slot == 0 ? CatchSpots.Enemy1Bar : CatchSpots.Enemy2Bar) > 0.01;
                if (o.Counter != null && (there || radiant[slot])) names[slot] = o.Counter.Record(names[slot], radiant[slot]);
            }
            for (int slot = 0; slot < 2; slot++)
            {
                if (!radiant[slot]) continue;
                throw new StopRun("a radiant appeared (gold name) " + Place(slot) + ", nothing was clicked")
                {
                    Radiant = true,
                    RadiantName = names[slot],
                    RadiantPlace = Place(slot),
                    Screenshot = Evidence()
                };
            }
        }

        /// <summary>A 2v2 has begun: both wild Lumits are counted and checked for radiants.</summary>
        void StartDuo()
        {
            duoCycle = fightCycle = countedCycle = cycle;
            duoLead = duoPartner = null;
            captureTried = false;
            caughtOrder.Clear();
            Bitmap shot = CurrentFrame();
            for (int i = 0; i < 2; i++)
            {
                var f = foes[i] = new Foe { Slot = i };
                f.Hp = DuoBar(shot, i == 0 ? CatchSpots.Enemy1Bar : CatchSpots.Enemy2Bar);
                double gold;
                string name = DuoCard(shot, i, out gold);
                f.Radiant = o.RadiantCheck && gold >= o.AlertGoldPercent;
                // A bar read as empty at the start doesn't skip the gold check: better an unneeded stop than a lost radiant.
                if (f.Hp <= 0.01 && !f.Radiant)
                {
                    f.Gone = true;
                    continue;
                }
                f.Entry = o.Counter != null ? o.Counter.Record(name, f.Radiant) : name;
                LumitCounter.Entry rules = o.Counter != null ? o.Counter.Lookup(f.Entry) : null;
                f.Catch = o.Catch.CatchOn && !o.Catch.RadiantsOnly && rules != null && rules.Catch;
            }
            foreach (Foe f in foes)
            {
                if (!f.Radiant) continue;
                if (!o.Catch.CatchRadiants)
                    throw new StopRun("a radiant appeared (gold name) in a 2v2, nothing was clicked") { Radiant = true, RadiantName = f.Entry, RadiantPlace = Place(f), Screenshot = Evidence() };
                // 2v2 target clicks haven't been seen working on the PC, so the radiant is left for you, untouched.
                if (!o.Catch.CatchDuoRadiants)
                    throw new StopRun("a radiant appeared (gold name) " + Place(f) + ", nothing was clicked: catch it yourself")
                    {
                        Radiant = true,
                        RadiantName = f.Entry,
                        RadiantPlace = Place(f),
                        Screenshot = Evidence()
                    };
                radiantCycle = cycle;
                radiantName = f.Entry;
                radiantCaught = false;
                int after = o.Counter != null ? o.Counter.LastRadiantAfter : 0;
                RaiseNotice(RadiantLabel() + " spotted!", (after > 0 ? "Found after " + after + (after == 1 ? " lumit. " : " lumits. ") : "") +
                            "Catching it in a 2v2 at " + DateTime.Now.ToString("HH:mm") + ", using only Strike and Capture.");
            }
        }

        /// <summary>Which wild Lumit of the 2v2 it is, as seen on the field (on the PC, the left one's card is the upper one).</summary>
        static string Place(Foe f)
        {
            return Place(f.Slot);
        }

        static string Place(int slot)
        {
            return slot == 0 ? "in a 2v2, the left one (upper card)" : "in a 2v2, the right one (lower card)";
        }

        /// <summary>
        /// A round of a 2v2: your lead chooses, then your second Lumit, each move followed by clicking its target. The lead
        /// works on one wild Lumit and the second on the other; a radiant is the lead's, with the Strike rule.
        /// </summary>
        void DuoRound()
        {
            string last = null;
            for (int turn = 0; turn < 3; turn++)
            {
                // The step may match while the round is still playing out, so the menu itself is waited for. After a choice the
                // second Lumit's menu follows straight away; if it doesn't, the round is playing out and this round is done.
                if (!WaitUntil(() => DuoMenuUp(CurrentFrame()) && !BackShowing(), turn == 0 ? 15000 : 3500)) return;
                Pause(Triangular(200, 500, 0.3));
                string chooser = Chooser();
                // The same menu twice in a row means the last choice didn't take; it's simply made again.
                int role = RoleOf(chooser, turn, last);
                last = chooser;
                Bitmap shot = CurrentFrame();
                double[] hp =
                {
                    DuoBar(shot, CatchSpots.Enemy1Bar),
                    DuoBar(shot, CatchSpots.Enemy2Bar)
                };
                if (role == Lead)
                {
                    double ours = DuoBar(shot, CatchSpots.Lead2Bar);
                    if (ours >= 0) leadHp = (int)Math.Round(ours * o.Catch.LeadMaxHp);
                }
                // The lead always chooses first in a round.
                if (turn == 0 || role == Lead) DuoNewRound(hp);
                else for (int i = 0; i < 2; i++) if (!foes[i].Gone) foes[i].Hp = hp[i];
                // A card that's gone means that Lumit is too, whatever the turn.
                for (int i = 0; i < 2; i++)
                {
                    if (foes[i].Gone || hp[i] > 0.005) continue;
                    foes[i].Gone = true;
                    if (foes[i].CaptureThrown && !caughtOrder.Contains(foes[i])) caughtOrder.Add(foes[i]);
                }

                Foe target = LeadTarget();
                if (role == Partner) target = PartnerTarget(target);
                if (target == null) return;
                bool capture;
                int slot;
                if (role == StandIn) { capture = true; slot = -1; }
                else if (role == Lead) LeadAction(target, out capture, out slot);
                else PartnerAction(target, LeadTarget(), out capture, out slot);

                string what = capture ? "Capture" : role == Lead ? LumitCounter.LeadMoves[slot] : "the " + LumitCounter.Slots[slot] + " move";
                string who = role == Lead ? (duoLead ?? "lead") : role == Partner ? (duoPartner ?? "second Lumit") : "stand-in";
                SetActivity(who + ": " + what + " on " + target.Name + (target.Hp >= 0 ? " (HP " + Math.Round(target.Hp * 100) + "%)" : ""));
                Pause(Triangular(250, 600, 0.3));
                ClickRect(capture ? Spot(CatchSpots.Capture) : MoveBox(slot), false);
                if (!PickTarget(target)) continue;
                if (capture)
                {
                    target.CaptureThrown = true;
                    captureTried = true;
                }
                else if (role == Lead && target.Radiant) target.Strikes.Struck(target.Hp);
                else if (role == Lead)
                {
                    target.LeadBefore = target.Hp;
                    target.LeadHits++;
                }
                else
                {
                    target.PartnerBefore = target.Hp;
                    target.PartnerHits++;
                }
                // Your second Lumit chooses last; the round plays out next, and the next one starts when the menu returns.
                if (role != Lead) return;
            }
        }

        /// <summary>A 2v2 menu ready for a choice: Capture lit, and Strike, which needs no MP, lit too.</summary>
        bool DuoMenuUp(Bitmap shot)
        {
            return ScreenText.Lit(shot, Spot(CatchSpots.Capture)) && ScreenText.Lit(shot, Spot(CatchSpots.Strike), 2.5);
        }

        /// <summary>Whose menu this is, from "Choose an action for Lacerath." above it; "" if it can't be read.</summary>
        string Chooser()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) Pause(200);
                Match m = Regex.Match(TextAt(CatchSpots.FightText), @"for\s+([A-Za-z][A-Za-z' -]+)", RegexOptions.IgnoreCase);
                string name = m.Success ? LumitNames.Clean(m.Groups[1].Value) : "";
                if (name.Length >= 3) return name;
            }
            return "";
        }

        /// <summary>
        /// In each round the lead chooses first and the second Lumit next, which decides whose menu it is. The names above
        /// the menu only confirm it: the same menu twice is the same Lumit choosing again, and a name that's neither, after
        /// a faint, is a stand-in's, which only captures.
        /// </summary>
        int RoleOf(string chooser, int turn, string previous)
        {
            bool named = chooser.Length >= 3;
            if (named && duoLead != null && LumitNames.Similar(chooser, duoLead)) return Lead;
            if (named && duoPartner != null && LumitNames.Similar(chooser, duoPartner)) return Partner;
            if (named && (standInPending || (duoLead != null && duoPartner != null))) return StandIn;
            if (turn == 0)
            {
                if (named && duoLead == null && !standInPending)
                {
                    duoLead = chooser;
                    if (leadName == null) leadName = chooser; // for healing it after
                }
                return Lead;
            }
            if (named && duoPartner == null && (duoLead == null || !LumitNames.Similar(chooser, duoLead))) duoPartner = chooser;
            return Partner;
        }

        /// <summary>
        /// A new round: what last round's moves took is measured, and a Lumit whose card is gone was caught if a Capture was
        /// thrown at it in that round, and otherwise beaten.
        /// </summary>
        void DuoNewRound(double[] hp)
        {
            for (int i = 0; i < 2; i++)
            {
                Foe f = foes[i];
                if (f.Gone) continue;
                if (hp[i] <= 0.005)
                {
                    f.Gone = true;
                    if (f.CaptureThrown) caughtOrder.Add(f);
                    continue;
                }
                if (f.Radiant) f.Strikes.Observe(hp[i]);
                else if (o.Counter != null && f.Entry.Length > 0)
                {
                    if (f.LeadBefore >= 0 && f.LeadBefore - hp[i] > 0.01) o.Counter.AddSiphonDamage(f.Entry, f.LeadBefore - hp[i]);
                    if (f.PartnerBefore >= 0 && f.PartnerBefore - hp[i] > 0.01) o.Counter.AddPartnerDamage(f.Entry, f.PartnerBefore - hp[i]);
                }
                f.LeadBefore = f.PartnerBefore = -1;
                f.CaptureThrown = false;
                f.Hp = hp[i];
            }
        }

        /// <summary>The lead's: a radiant first, then the only one you catch, otherwise the left one.</summary>
        Foe LeadTarget()
        {
            List<Foe> alive = foes.Where(f => f != null && !f.Gone).ToList();
            Foe radiant = alive.FirstOrDefault(f => f.Radiant);
            if (radiant != null) return radiant;
            List<Foe> wanted = alive.Where(f => f.Catch).ToList();
            return wanted.Count == 1 ? wanted[0] : alive.FirstOrDefault();
        }

        /// <summary>The second Lumit's: the other one, or the same one when it's the last.</summary>
        Foe PartnerTarget(Foe leads)
        {
            return foes.FirstOrDefault(f => f != null && !f.Gone && f != leads) ?? leads;
        }

        void LeadAction(Foe t, out bool capture, out int slot)
        {
            if (t.Radiant)
            {
                slot = 0;
                capture = !t.Strikes.ShouldStrike(t.Hp);
                return;
            }
            if (!t.Catch)
            {
                capture = false;
                slot = MoveLit(1) ? 1 : 0; // Pyrecrest Assault, or Strike without the MP
                return;
            }
            LumitCounter.Entry e = o.Counter != null ? o.Counter.Lookup(t.Entry) : null;
            slot = e != null ? e.Attack : LumitCounter.DefaultAttack;
            double usual = e != null ? e.TypicalSiphon : 0;
            // Weakened already, maybe by the second Lumit: only hit again if the usual damage leaves it standing.
            if (t.LeadHits == 0 && t.Hp >= Unhurt) capture = false;
            else if (t.Hp < 0 || usual <= 0) capture = true;
            else capture = t.Hp <= usual;
            if (!capture && !MoveLit(slot)) capture = true;
        }

        /// <summary>On the last one left it only captures, or finishes it off, so the two of you can't add up to a kill.</summary>
        void PartnerAction(Foe u, Foe leads, out bool capture, out int slot)
        {
            int kill = MoveLit(o.Catch.PartnerKill) ? o.Catch.PartnerKill : 0;
            if (u == leads || u.Radiant || !u.Catch)
            {
                capture = u.Radiant || u.Catch;
                slot = kill;
                return;
            }
            slot = o.Catch.PartnerWeaken;
            LumitCounter.Entry e = o.Counter != null ? o.Counter.Lookup(u.Entry) : null;
            double usual = e != null ? e.TypicalPartnerDamage : 0;
            if (u.PartnerHits == 0 && u.Hp >= Unhurt) capture = false;
            else if (u.Hp < 0 || usual <= 0) capture = true;
            else capture = u.Hp <= usual;
            if (!capture && !MoveLit(slot)) capture = true;
        }

        bool MoveLit(int slot)
        {
            return slot == 0 || ScreenText.Lit(CurrentFrame(), MoveBox(slot));
        }

        bool BackShowing()
        {
            return ScreenText.Lit(CurrentFrame(), Spot(CatchSpots.Back), 8);
        }

        /// <summary>
        /// After a move or Capture the game asks which wild Lumit, with a Back button showing. Each click on the target picks
        /// it or, once it's picked, confirms it, so it's checked after every click and a missed one only costs another.
        /// Returns false when the move didn't take, so the turn is chosen again.
        /// </summary>
        bool PickTarget(Foe target)
        {
            if (!WaitUntil(BackShowing, 2500)) return !YourTurnMenuLit(CurrentFrame());
            string high = target.Slot == 0 ? CatchSpots.Enemy1Tap : CatchSpots.Enemy2Tap;
            string low = target.Slot == 0 ? CatchSpots.Enemy1TapLow : CatchSpots.Enemy2TapLow;
            for (int click = 0; click < 8; click++)
            {
                Pause(Triangular(250, 550, 0.3));
                ClickRect(Spot(click % 4 < 2 ? high : low), o.Catch.BorrowForTargets);
                if (WaitUntil(() => !BackShowing(), 900)) return true;
            }
            throw new StopRun("couldn't pick " + target.Name + " as the target in a 2v2") { Screenshot = Evidence() };
        }

        /// <summary>Clicks inside an area; a click on a wild Lumit borrows the real pointer in background mode, like map clicks.</summary>
        void ClickRect(Rectangle r, bool borrowInBackground)
        {
            MoveMouse(new Point(Biased(r.X, r.Width), Biased(r.Y, r.Height)), Math.Min(r.Width, r.Height));
            PressButton(MouseButton.Left, false, borrowInBackground && o.Background);
            clicks++;
            ReportProgress(false);
        }

        /// <summary>
        /// Which wild Lumit of the 2v2 a caught screen is for: by the name on it, else by which ones were caught. Several
        /// possible ones come back when it can't be told.
        /// </summary>
        List<Foe> DuoCaughtFoes(string caughtName)
        {
            List<Foe> open = foes.Where(f => f != null && !f.Matched && (f.CaptureThrown || caughtOrder.Contains(f))).ToList();
            if (caughtName.Length > 0)
            {
                List<Foe> byName = open.Where(f =>
                {
                    if (LumitNames.Similar(f.Entry, caughtName)) return true;
                    LumitCounter.Entry e = o.Counter != null ? o.Counter.Lookup(f.Entry) : null;
                    return e != null && e.CaughtName.Length > 0 && LumitNames.Similar(e.CaughtName, caughtName);
                }).ToList();
                if (byName.Count == 1) return byName;
            }
            Foe earlier = caughtOrder.FirstOrDefault(f => !f.Matched);
            if (earlier != null) return new List<Foe> { earlier };
            return open;
        }

        /// <summary>After a Capture: whether the "New Lumit caught" screen is up (checked a few times a second).</summary>
        bool CaughtScreenShowing()
        {
            if (o.Catch == null || !captureTried || fightCycle != cycle || window == null) return false;
            long now = clock.ElapsedMilliseconds;
            if (now < nextCaughtCheck) return false;
            nextCaughtCheck = now + 300;
            return TextAt(CatchSpots.CaughtTitle).IndexOf("CAUGHT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Reads the caught Lumit's stats, keeps or releases it by its rules, and notes it in the log.</summary>
        void HandleCaught()
        {
            // After a 2v2 another caught screen can follow, so then it stays on the lookout.
            captureTried = duoCycle == cycle;
            SetActivity("caught one, reading its stats");
            Pause(700); // let it finish sliding in
            var shots = new List<Bitmap>();
            try
            {
                for (int k = 0; k < 2; k++)
                {
                    if (k > 0) Pause(250);
                    Bitmap b = FreshFrame();
                    if (b != null) shots.Add(b);
                }
                if (shots.Count == 0) throw new StopRun("the caught screen couldn't be read, so nothing was kept or released");

                CaughtReading read = CaughtReading.From(shots, o.Catch.Spots, o.Counter.KnownNatures());
                string lumit = fightLumit;
                bool isRadiant = radiantCycle == cycle;
                CatchDecision decision;
                if (duoCycle == cycle)
                {
                    List<Foe> which = DuoCaughtFoes(read.Name);
                    if (which.Count == 1) which[0].Matched = true;
                    lumit = which.Count == 1 ? which[0].Entry : null;
                    isRadiant = which.Any(f => f.Radiant);
                    // Can't tell which: keep it unless every one it could be would be released.
                    List<CatchDecision> each = which.Select(f => Decide(read, f.Entry, f.Catch, f.Radiant)).ToList();
                    if (each.Count > 0 && each.All(d => d.Keep == each[0].Keep) && (which.Count == 1 || !each[0].Keep)) decision = each[0];
                    else if (each.Count > 0 && each.All(d => d.Keep)) decision = new CatchDecision { Keep = true, Reason = each[0].Reason };
                    else decision = new CatchDecision { Keep = true, Unsure = true, Reason = "not sure which Lumit of the 2v2 this is" };
                    if (which.Count != 1)
                    {
                        decision.MetIvs = Stat.None;
                        decision.MetNatures.Clear();
                    }
                }
                else
                {
                    decision = Decide(read, fightLumit, catchThis, isRadiant);
                }
                bool release = !decision.Keep && !o.Catch.Practice;
                string who = read.Name.Length > 0 ? read.Name : "it";
                // Got one with what was wanted, so that's no longer needed; with nothing left the Lumit is perfect.
                string progress = "";
                if (lumit != null && (decision.MetIvs != Stat.None || decision.MetNatures.Count > 0))
                {
                    LumitCounter.Entry after = o.Counter.TickOff(lumit, decision.MetIvs, decision.MetNatures);
                    if (after != null)
                        progress = after.HasKeepRules ? after.Name + " still needs " + after.NeedsText
                                                      : after.Name + " is PERFECT now, no longer caught";
                }
                o.Catch.Log.Write(lumit ?? "?", read, decision, o.Catch.Practice, shots[0], progress);
                if (lumit != null) o.Counter.RecordCatch(lumit, read.Name, read.Nature, !release);
                if (isRadiant)
                {
                    radiantCaught = true;
                    RaiseNotice(RadiantLabel() + " caught!", "Keeping it. " + read.IvText() + (read.Nature.Length > 0 ? ", " + read.Nature : "") + ".");
                }

                if (release)
                {
                    SetActivity("releasing " + who + ": " + decision.Reason);
                    ReleaseCaught(read.Name);
                }
                else
                {
                    SetActivity((o.Catch.Practice && !decision.Keep ? "practice: keeping " + who + " (would release: " : "keeping " + who + " (") + decision.Reason + ")" +
                                (progress.Length > 0 ? "; " + progress : ""));
                    KeepCaught(read.Name);
                }
            }
            finally
            {
                foreach (Bitmap b in shots) b.Dispose();
            }
        }

        /// <summary>
        /// The game's shape check is up. One tap only, so the closest match is taken whenever there is one at all;
        /// the run only stops when nothing could be told apart.
        /// </summary>
        void SolveCaptcha()
        {
            SetActivity("captcha, finding the matching shape");
            using (Bitmap shot = FreshFrame())
            {
                if (shot == null) throw new StopRun("a captcha appeared and the screen couldn't be read");
                Point? at = Captcha.Solve(shot);
                Captcha.Save(shot, at);
                if (!at.HasValue)
                    throw new StopRun("a captcha appeared and no matching shape could be found, so nothing was tapped")
                    {
                        AlertTitle = "Captcha",
                        Screenshot = Evidence()
                    };
                SetActivity("captcha, tapping the match at " + at.Value.X + ", " + at.Value.Y);
                ClickRect(new Rectangle(at.Value.X, at.Value.Y, 1, 1), false);
            }
            Pause(600);
        }

        CatchDecision Decide(CaughtReading read, string lumit, bool catchIt, bool radiant)
        {
            if (radiant) return new CatchDecision { Keep = true, Reason = "radiant" };
            // Not one you catch: it was only captured because a stand-in couldn't safely attack.
            if (!catchIt) return new CatchDecision { Keep = false, Reason = "not ticked for catching; caught only to end the fight after a Lumit fainted" };
            return CatchDecision.For(read, lumit == null ? null : o.Counter.Lookup(lumit));
        }

        /// <summary>
        /// This caught screen has gone: there's no caught screen now, or another Lumit's is, since after a 2v2 one can follow
        /// another.
        /// </summary>
        bool CaughtScreenGone(string name)
        {
            if (TextAt(CatchSpots.CaughtTitle).IndexOf("CAUGHT", StringComparison.OrdinalIgnoreCase) < 0) return true;
            string now = LumitNames.Clean(TextAt(CatchSpots.CaughtName));
            return name.Length > 0 && now.Length > 0 && !LumitNames.Similar(now, name);
        }

        void KeepCaught(string name)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Pause(Triangular(300, 700, 0.3));
                ClickSpot(CatchSpots.Keep, false);
                if (WaitUntil(() => CaughtScreenGone(name), 5000)) return;
            }
            throw new StopRun("the Keep button didn't close the caught screen");
        }

        void ReleaseCaught(string name)
        {
            Pause(Triangular(300, 700, 0.3));
            ClickSpot(CatchSpots.Release, false);
            string title = WaitForText(CatchSpots.ConfirmTitle, t => t.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0, 4000);
            if (title == null)
                throw new StopRun("the release confirmation didn't show up, so nothing was released");

            // Only confirm when the question is about the Lumit that was just read.
            Match m = Regex.Match(title, @"Release\s+(.+)", RegexOptions.IgnoreCase);
            string asked = m.Success ? LumitNames.Clean(m.Groups[1].Value) : "";
            Pause(Triangular(300, 600, 0.3));
            if (name.Length > 0 && asked.Length > 0 && !LumitNames.Similar(asked, name))
            {
                ClickSpot(CatchSpots.ConfirmCancel, false);
                SetActivity("the confirmation named " + asked + ", not " + name + ", so keeping it");
                Pause(600);
                KeepCaught(name);
                return;
            }
            ClickSpot(CatchSpots.ConfirmRelease, false);
            if (!WaitUntil(() => CaughtScreenGone(name), 5000))
                throw new StopRun("the caught screen didn't close after Release");
        }

        /// <summary>
        /// When your Lumit faints, the game shows the other Lumits in the menu to pick one. Recognised by your card's
        /// HP bar being empty, the first Lumit's name lit where the moves usually are, and Capture greyed out.
        /// </summary>
        bool FaintedChoiceShowing()
        {
            if (o.Catch == null || window == null) return false;
            long now = clock.ElapsedMilliseconds;
            if (now < nextChoiceCheck) return false;
            nextChoiceCheck = now + 400;
            Bitmap shot = CurrentFrame();
            // In a 2v2 either of your two may be the one that fainted.
            double lead = duoCycle == cycle
                ? Math.Min(ScreenText.BarFill(shot, Spot(CatchSpots.Lead2Bar), ScreenText.IsRed), ScreenText.BarFill(shot, Spot(CatchSpots.Partner2Bar), ScreenText.IsRed))
                : ScreenText.BarFill(shot, Spot(CatchSpots.LeadBar), ScreenText.IsRed);
            if (lead < 0 || lead >= 0.02 || !ScreenText.Lit(shot, Spot(CatchSpots.FirstLumit), 2.5) || ScreenText.Lit(shot, Spot(CatchSpots.Capture)))
                return false;
            // Mid-fight that's enough; otherwise (say the run was started on this screen) the message must say so too.
            return fightCycle == cycle || TextAt(CatchSpots.FightText).IndexOf("fainted", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Your Lumit fainted: sends in the first Lumit offered, to capture until the fight ends.</summary>
        void SendInStandIn()
        {
            string who = leadName ?? "Your Lumit";
            bool leadDown = true;
            if (duoCycle == cycle)
            {
                // Only your lead gets Bark Tea; a fainted second Lumit just gets a stand-in, which only captures.
                standInPending = true;
                leadDown = ScreenText.BarFill(CurrentFrame(), Spot(CatchSpots.Lead2Bar), ScreenText.IsRed) < 0.02;
                if (!leadDown) who = duoPartner ?? "Your second Lumit";
            }
            if (leadDown && !leadFainted)
            {
                leadFainted = true;
                leadHp = 0;
                RaiseNotice(who + " fainted", who + " fainted in a fight at " + DateTime.Now.ToString("HH:mm") +
                            ". The first Lumit in the list takes over and captures; " + who + " gets Bark Tea after the fight.");
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                SetActivity(who + " fainted, sending in the first Lumit");
                Pause(Triangular(400, 900, 0.3));
                ClickSpot(CatchSpots.FirstLumit, false);
                nextChoiceCheck = 0;
                if (WaitUntil(() => !FaintedChoiceShowing(), 5000)) return;
            }
            throw new StopRun("couldn't send in another Lumit after " + who + " fainted")
            {
                AlertTitle = who + " fainted!",
                Screenshot = Evidence()
            };
        }

        void RaiseNotice(string title, string message)
        {
            var notice = Notice;
            if (notice != null) notice(title, message, Evidence());
        }

        /// <summary>During a fight: notices when a different Lumit is fighting for you (yours fainted and the game sent one in).</summary>
        void CheckFainted(int waitingFor)
        {
            // A 2v2 has other cards there; a faint shows up as the choice of a stand-in instead.
            if (o.Catch == null || leadName == null || leadFainted || fightCycle != cycle || duoCycle == cycle || waitingFor < o.NameStep || window == null) return;
            long now = clock.ElapsedMilliseconds;
            if (now < nextFaintCheck) return;
            nextFaintCheck = now + 2000;
            string text = TextAt(CatchSpots.LeadName);
            string name = LumitNames.Clean(text);
            // A fighter's card is a name and a level; anything else there means no card is showing.
            if (name.Length < 3 || !Regex.IsMatch(text, @"\d") || LumitNames.Similar(name, leadName))
            {
                faintSuspect = null;
                return;
            }
            // The same other name twice in a row, so one misread can't raise a false alarm.
            if (faintSuspect == null || !LumitNames.Similar(faintSuspect, name))
            {
                faintSuspect = name;
                nextFaintCheck = now + 700;
                return;
            }
            leadFainted = true;
            leadHp = 0;
            RaiseNotice(leadName + " fainted", leadName + " fainted in a fight at " + DateTime.Now.ToString("HH:mm") + " and " + name +
                        " took over. It captures until the fight ends; " + leadName + " gets Bark Tea after.");
        }

        /// <summary>On the map: feeds up to three Bark Tea if your Lumit ended the last fight below the set HP, or fainted.</summary>
        void MaybeHeal()
        {
            if (o.Catch == null || window == null) return;
            bool due = leadFainted || (o.Catch.HealBelowHp > 0 && leadHp >= 0 && leadHp < o.Catch.HealBelowHp);
            if (!due) return;
            string who = leadName ?? "your Lumit";
            SetActivity("healing " + who + " (" + leadHp + " HP) with Bark Tea");
            Pause(Triangular(300, 700, 0.3));
            ClickSpot(CatchSpots.TeamIcon, true); // a double-click opens its panel
            Func<string, bool> panelOpen = t => leadName == null ? LumitNames.Clean(t).Length >= 3 : LumitNames.Similar(LumitNames.Clean(t), leadName);
            if (WaitForText(CatchSpots.PanelTitle, panelOpen, 5000) == null)
                throw new StopRun("couldn't open " + who + "'s panel to feed it Bark Tea (" + leadHp + " HP)")
                {
                    AlertTitle = who + " couldn't be healed",
                    Screenshot = Evidence()
                };

            Pause(Triangular(250, 500, 0.3));
            ClickSpot(CatchSpots.FoodTab, false);
            Pause(700);
            string item = TextAt(CatchSpots.ItemName);
            int have = ScreenText.ReadNumber(CurrentFrame(), Spot(CatchSpots.ItemCount), 0, 99999);
            string letters = new string(item.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            if (LumitNames.Distance(letters, "barktea") > 2 || have == 0)
            {
                Pause(400);
                ClickSpot(CatchSpots.PanelClose, false);
                throw new StopRun("out of Bark Tea (" + who + " is at " + leadHp + " HP)")
                {
                    AlertTitle = "Out of Bark Tea",
                    Screenshot = Evidence()
                };
            }

            double before = ScreenText.BarFill(CurrentFrame(), Spot(CatchSpots.PanelBar), ScreenText.IsGreen);
            int feeds = have > 0 ? Math.Min(3, have) : 3;
            for (int k = 0; k < feeds; k++)
            {
                Pause(Triangular(300, 600, 0.3));
                Drag(Spot(CatchSpots.BarkTea), Spot(CatchSpots.DropSpot));
                Pause(700);
                if (ScreenText.BarFill(CurrentFrame(), Spot(CatchSpots.PanelBar), ScreenText.IsGreen) >= 0.99) break; // full: don't waste tea
            }
            Pause(300);
            double after = ScreenText.BarFill(CurrentFrame(), Spot(CatchSpots.PanelBar), ScreenText.IsGreen);
            if (before >= 0 && after >= 0 && after < before + 0.1)
                throw new StopRun("Bark Tea didn't heal " + who + " (the drag may not have reached it)")
                {
                    AlertTitle = who + " couldn't be healed",
                    Screenshot = Evidence()
                };
            leadHp = after >= 0 ? (int)Math.Round(after * o.Catch.LeadMaxHp) : -1;
            leadFainted = false; // back on its feet for the next fight

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Pause(Triangular(300, 600, 0.3));
                ClickSpot(CatchSpots.PanelClose, false);
                if (WaitUntil(() => !panelOpen(TextAt(CatchSpots.PanelTitle)) && Matches(o.Points[0]), 4000)) return;
            }
            throw new StopRun("the panel didn't close after feeding Bark Tea");
        }

        /// <summary>Text in a spot of the current capture ("" if none).</summary>
        string TextAt(string key)
        {
            return ScreenText.Read(CurrentFrame(), Spot(key), Ink.Light, 0, false) ?? "";
        }

        /// <summary>Waits for a spot's text to pass <paramref name="test"/>; returns that text, or null on time-out.</summary>
        string WaitForText(string key, Func<string, bool> test, int timeoutMs)
        {
            long end = clock.ElapsedMilliseconds + timeoutMs;
            while (true)
            {
                string text = TextAt(key);
                if (test(text)) return text;
                if (clock.ElapsedMilliseconds > end) return null;
                Pause(200);
            }
        }

        bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            long end = clock.ElapsedMilliseconds + timeoutMs;
            while (!condition())
            {
                if (clock.ElapsedMilliseconds > end) return false;
                Pause(PollMs);
            }
            return true;
        }

        /// <summary>Clicks a random spot inside one of the catching spots, the same human way as steps.</summary>
        void ClickSpot(string key, bool doubleClick)
        {
            Rectangle r = Spot(key);
            MoveMouse(new Point(Biased(r.X, r.Width), Biased(r.Y, r.Height)), Math.Min(r.Width, r.Height));
            PressButton(MouseButton.Left, doubleClick, false);
            clicks++;
            ReportProgress(false);
        }

        /// <summary>Drags from one area to another with the left button held, like moving an item onto a Lumit.</summary>
        void Drag(Rectangle fromArea, Rectangle toArea)
        {
            var a = new Point(Biased(fromArea.X, fromArea.Width), Biased(fromArea.Y, fromArea.Height));
            var b = new Point(Biased(toArea.X, toArea.Width), Biased(toArea.Y, toArea.Height));
            MoveMouse(a, Math.Min(fromArea.Width, fromArea.Height));
            List<HumanMouse.Sample> path = HumanMouse.Plan(a, b, Math.Min(toArea.Width, toArea.Height), rnd);
            Point origin = Origin();

            if (!o.Background)
            {
                Thread.Sleep(Triangular(60, 140, 0.4));
                Pointer.Button(MouseButton.Left, true);
                try
                {
                    Thread.Sleep(Triangular(80, 160, 0.4));
                    Play(path, p => Pointer.MoveTo(p.X + origin.X, p.Y + origin.Y));
                    Thread.Sleep(Triangular(80, 160, 0.4));
                }
                finally
                {
                    Pointer.Button(MouseButton.Left, false);
                }
                Point now;
                restPos = Native.TryGetCursorPos(out now) ? now : new Point(b.X + origin.X, b.Y + origin.Y);
                clicks++;
                return;
            }

            // Background mode: the drag goes to the game as messages; optionally the real pointer follows it.
            bool real = o.Catch.DragRealPointer;
            Point saved = Point.Empty;
            bool haveSaved = real && Native.TryGetCursorPos(out saved);
            Point start = new Point(a.X + origin.X, a.Y + origin.Y);
            try
            {
                if (real) Native.SetCursorPos(start.X, start.Y);
                Thread.Sleep(Triangular(60, 140, 0.4));
                Posted(window.PostButton(MouseButton.Left, true, false, a));
                Thread.Sleep(Triangular(80, 160, 0.4));
                Play(path, p =>
                {
                    if (real) Native.SetCursorPos(p.X + origin.X, p.Y + origin.Y);
                    Posted(window.PostMove(p, true));
                });
                Thread.Sleep(Triangular(80, 160, 0.4));
                Posted(window.PostButton(MouseButton.Left, false, false, b));
                virtualPos = b;
                clicks++;
            }
            finally
            {
                if (haveSaved) Native.SetCursorPos(saved.X, saved.Y);
            }
        }

        // ---------------------------------------------------------------- waiting for the screen

        /// <summary>
        /// Waits until step <paramref name="i"/>'s screen shows, while watching for the game being
        /// somewhere else. Returns the step to click next: <paramref name="i"/>, or another step whose
        /// screen is showing instead; -1 if the step timed out and is set to be skipped.
        /// </summary>
        int WaitForScreen(int i)
        {
            ClickPoint p = o.Points[i];
            MoveOutOf(p.WatchArea);
            int prev = PreviousWaitingStep(i);
            bool prevGone = false;
            var showingSince = new long[o.Points.Length];
            for (int k = 0; k < showingSince.Length; k++) showingSince[k] = -1;
            int missed = 0;
            long started = clock.ElapsedMilliseconds, deadline = started + p.TimeoutSec * 1000L;
            // Radiants only: after a run there's no Victory, so the map (step 1) is what comes next, not step i.
            bool afterRun = ranCycle == cycle && o.NameStep > 0 && i > o.NameStep && o.Points[0].Waits;
            bool triedAfterRun = false;
            string waitingFor = afterRun ? "the map (step 1) after the run" : "step " + (i + 1);
            SetActivity("waiting for " + waitingFor);

            while (true)
            {
                ThrowIfStopped();
                if (o.Captcha && Captcha.Showing(CurrentFrame()))
                {
                    SomethingHappened();
                    SolveCaptcha();
                    deadline = clock.ElapsedMilliseconds + p.TimeoutSec * 1000L;
                    SetActivity("waiting for step " + (i + 1));
                    continue;
                }
                if (CaughtScreenShowing())
                {
                    SomethingHappened();
                    HandleCaught();
                    deadline = clock.ElapsedMilliseconds + p.TimeoutSec * 1000L;
                    SetActivity("waiting for step " + (i + 1));
                    continue;
                }
                if (FaintedChoiceShowing())
                {
                    SomethingHappened();
                    SendInStandIn();
                    deadline = clock.ElapsedMilliseconds + p.TimeoutSec * 1000L;
                    SetActivity("waiting for step " + (i + 1));
                    continue;
                }
                CheckFainted(i);
                if (Matches(p))
                {
                    if (i != 0) SomethingHappened();
                    return i;
                }
                // Radiants only: after a run, the first step's screen (the map) means the fight is over, and the steps for
                // after a fought one, like Continue, won't come. So it goes straight on, rather than after 5 s.
                if (afterRun && Matches(o.Points[0])) return 0;
                long now = clock.ElapsedMilliseconds;
                // In a 2v2 parts of the fight screen stay up while the round plays out, so the menu going is what counts as
                // your turn's screen having gone.
                if (prev >= 0 && duoCycle == cycle && o.Catch != null && !DuoMenuUp(CurrentFrame())) prevGone = true;

                bool anyShowing = false;
                for (int j = 0; j < o.Points.Length; j++)
                {
                    if (j == i || !o.Points[j].Waits) continue;
                    if (!Matches(o.Points[j]))
                    {
                        showingSince[j] = -1;
                        if (j == prev) prevGone = true;
                        continue;
                    }
                    anyShowing = true;
                    if (j != 0) SomethingHappened();
                    if (j == prev && prevGone)
                    {
                        // The previous screen went away and came back, e.g. the enemy survived and the
                        // attack buttons lit up again. (It must disappear first, so its own click isn't
                        // repeated while that screen is still fading out.)
                        SetActivity("step " + (j + 1) + " came back, clicking it again");
                        return j;
                    }
                    if (showingSince[j] < 0) showingSince[j] = now;
                    else if (now - showingSince[j] >= StuckMs)
                    {
                        // e.g. a click on Continue didn't register, so Victory is still up
                        SetActivity("the game is still on step " + (j + 1) + "'s screen, continuing from there");
                        return j;
                    }
                }

                // Step 1's picture was retaken and it was clicked, and still no other step's screen: the game really is
                // somewhere the steps don't know. That's for you to see to.
                if (retakenAt >= 0 && now - retakenAt > (MaxMissedTimeouts - 1) * 1000L * p.TimeoutSec)
                    throw StillStuck((int)((now - retakenAt) / 1000));

                string why;
                // After a run only the map can come, so a map that doesn't fit step 1's picture (your character standing on
                // the watched spot, say) gets it retaken after 10 s, not after a whole time-out. Once per wait: each try
                // takes a second.
                if (afterRun && !triedAfterRun && !anyShowing && retakenAt < 0 && now - started >= RetakeAfterRunMs)
                {
                    triedAfterRun = true;
                    if (RetakeFirstStep(RetakeAfterRunMs / 1000 + " s after the run", out why)) return 0;
                }

                if (now > deadline)
                {
                    if (p.OnTimeout == TimeoutAction.Skip) return -1;
                    if (p.OnTimeout == TimeoutAction.ClickAnyway) return i;
                    // Nothing recognisable is showing. The game may still be on the first step's screen, only looking
                    // different now, so its picture is retaken from the screen and it's clicked. Another step's screen
                    // coming up after that shows it was; if none does, the run stops (above).
                    missed++;
                    why = anyShowing ? "another step's screen kept coming and going" : null;
                    if (!anyShowing && RetakeFirstStep(missed * p.TimeoutSec + " s", out why)) return 0;
                    // Otherwise keep looking a while before giving up, saying which step's picture comes closest.
                    string closest = Closest();
                    if (missed >= MaxMissedTimeouts)
                        throw new StopRun("no step's screen showed up for " + missed * p.TimeoutSec + " s (it was waiting for " + waitingFor +
                                          (closest.Length > 0 ? "; " + closest : "") + (why != null ? "; step 1's picture wasn't retaken: " + why : "") + ")");
                    SetActivity("no known screen" + (closest.Length > 0 ? " (" + closest + ")" : "") + ", looking " + (missed + 1) + "/" + MaxMissedTimeouts);
                    deadline = now + p.TimeoutSec * 1000L;
                }
                Pause(PollMs);
            }
        }

        /// <summary>
        /// Nothing the steps know has shown up for a while: retakes step 1's picture from the screen, as its Retake button
        /// would, as an extra one for this run. True when it did, and step 1 is to be clicked; otherwise <paramref name="why"/>
        /// says why not. One at a time: a new one only once the last has been shown right by another step's screen coming up.
        /// </summary>
        bool RetakeFirstStep(string stuckFor, out string why)
        {
            why = null;
            ClickPoint first = o.Points[0];
            if (!first.Waits) why = "step 1 doesn't wait for a screen";
            else if (retakenAt >= 0) why = "the last retake hasn't been shown right yet";
            else if (!o.Points.Skip(1).Any(s => s.Waits)) why = "no other step waits for a screen, to show it worked";
            else if (radiantCycle == cycle && !radiantCaught) why = "a radiant's fight is on"; // nothing is clicked on a hunch then
            else why = BehindAPanel();
            if (why != null) return false;
            // Only a screen that holds still: one fading in or out would be taken halfway.
            Snapshot before = ReadArea(first.WatchArea);
            Pause(1000);
            Snapshot now = ReadArea(first.WatchArea);
            if (before == null || now == null) why = "the screen couldn't be read";
            else if (before.Similarity(now, first.WatchMask()) * 100 < first.MatchPercent) why = "the screen kept changing";
            else if (o.Points.Any(s => s.Waits && Matches(s))) why = "a step's screen came up after all";
            else why = BehindAPanel(); // it may have come up during that second
            if (why != null) return false;
            firstStepExtra.Add(now);
            if (firstStepExtra.Count > MaxExtraPictures) firstStepExtra.RemoveAt(0);
            retakenAt = clock.ElapsedMilliseconds;
            SetActivity("nothing known for " + stuckFor + ": step 1's picture retaken (" + firstStepExtra.Count + " this run), clicking it");
            return true;
        }

        /// <summary>
        /// Why step 1's picture mustn't be retaken and clicked now: the game has put a panel up and darkened everything behind it,
        /// as with its captcha ("Quick check"), where step 1's spot would land on the shapes to pick from. Seen by the captcha
        /// check, or by step 1's screen being there but evenly darker (on 15 November the captcha kept half of each colour of
        /// the map's bottom bar: 0% as taken, 96% evenly darkened). Null when neither.
        /// </summary>
        string BehindAPanel()
        {
            if (window != null && Captcha.Showing(CurrentFrame())) return "a captcha is showing";
            ClickPoint first = o.Points[0];
            Snapshot live = ReadArea(first.WatchArea);
            if (live == null) return null;
            bool[] mask = first.WatchMask();
            bool dimmed = first.Snapshot.DimmedSimilarity(live, mask) * 100 >= first.MatchPercent ||
                          firstStepExtra.Any(s => s.DimmedSimilarity(live, mask) * 100 >= first.MatchPercent);
            return dimmed ? "the map is darkened behind something, a captcha or a message" : null;
        }

        /// <summary>After step 1's picture was retaken and clicked, another step's screen came up: the game wasn't stuck.</summary>
        void SomethingHappened()
        {
            retakenAt = -1;
        }

        /// <summary>How closely a step's picture (for step 1, its best one) fits the screen now, in percent; -1 if it can't be read.</summary>
        int Likeness(ClickPoint p)
        {
            Snapshot live = ReadArea(p.WatchArea);
            if (live == null) return -1;
            bool[] mask = p.WatchMask();
            double best = p.Snapshot.Similarity(live, mask);
            if (ReferenceEquals(p, o.Points[0])) foreach (Snapshot s in firstStepExtra) best = Math.Max(best, s.Similarity(live, mask));
            return (int)Math.Round(best * 100);
        }

        /// <summary>The waiting step whose picture comes closest to the screen, for the status line: "step 1 closest, 64% of 80%".</summary>
        string Closest()
        {
            int step = -1, best = -1;
            for (int k = 0; k < o.Points.Length; k++)
            {
                if (!o.Points[k].Waits) continue;
                int l = Likeness(o.Points[k]);
                if (l > best)
                {
                    best = l;
                    step = k;
                }
            }
            return step < 0 || best < 0 ? "" : "step " + (step + 1) + " closest, " + best + "% of " + o.Points[step].MatchPercent + "%";
        }

        /// <summary>The stop when step 1's picture was retaken and clicked, and no other step's screen came up after.</summary>
        StopRun StillStuck(int seconds)
        {
            if (firstStepExtra.Count > 0) firstStepExtra.RemoveAt(firstStepExtra.Count - 1); // of a screen the steps don't know
            retakenAt = -1;
            return new StopRun("stuck on a screen it doesn't know: step 1's picture was retaken from it and clicked, but no other step's screen came up in " +
                               seconds + " s")
            {
                AlertTitle = "Stuck on an unknown screen",
                Screenshot = Evidence()
            };
        }

        int PreviousWaitingStep(int i)
        {
            if (!o.RedoPrevious || o.Points.Length < 2) return -1;
            int j = i - 1;
            if (j < 0)
            {
                if (cycle <= 1) return -1; // nothing has been clicked yet
                j = o.Points.Length - 1;
            }
            return o.Points[j].Waits ? j : -1;
        }

        bool Matches(ClickPoint p)
        {
            Snapshot live = ReadArea(p.WatchArea);
            if (live == null)
            {
                if (++screenFailures > 150) // several reads per check, so this is roughly 5 s of failures
                {
                    if (window == null) throw new StopRun("the screen couldn't be read (is the PC locked?)");
                    throw new StopRun(window.IsMinimized ? "the game window is minimized" : "the game window couldn't be read");
                }
                return false;
            }
            screenFailures = 0;
            bool[] mask = p.WatchMask();
            if (p.Snapshot.Similarity(live, mask) * 100 >= p.MatchPercent) return true;
            // Step 1's pictures retaken during the run count too.
            if (ReferenceEquals(p, o.Points[0]) && firstStepExtra.Any(s => s.Similarity(live, mask) * 100 >= p.MatchPercent)) return true;
            // Your attack button greys out when your Lumit is low on MP (after a few Heat Siphons), so when catching,
            // your turn is also recognised by the menu itself.
            return IsYourTurnStep(p) && YourTurnMenuLit(CurrentFrame());
        }

        /// <summary>
        /// The fight menu is ready for your move: Capture lit, and a move lit too. Capture lights up first while the
        /// Lumits are still being sent out, so it alone isn't enough. Any of the six moves counts, since a stand-in
        /// Lumit has other moves than yours, and some grey out without MP.
        /// </summary>
        bool YourTurnMenuLit(Bitmap shot)
        {
            return ScreenText.Lit(shot, Spot(CatchSpots.Capture)) && MoveBoxes().Any(m => ScreenText.Lit(shot, m, 2.5));
        }

        /// <summary>The six move buttons, laid out from Strike (first column) and Heat Siphon (third column).</summary>
        IEnumerable<Rectangle> MoveBoxes()
        {
            Rectangle first = Spot(CatchSpots.Strike), third = Spot(CatchSpots.Siphon);
            int rowGap = (int)Math.Round(56.0 * first.Height / 26);
            var second = new Rectangle((first.X + third.X) / 2, first.Y, first.Width, first.Height);
            foreach (Rectangle r in new[] { first, second, third })
            {
                yield return r;
                yield return new Rectangle(r.X, r.Y + rowGap, r.Width, r.Height);
            }
        }

        bool IsYourTurnStep(ClickPoint p)
        {
            return o.Catch != null && window != null && o.NameStep >= 0 && o.NameStep < o.Points.Length && ReferenceEquals(p, o.Points[o.NameStep]);
        }

        /// <summary>Snapshot of an area in step coordinates, from the game window if there is one (works while it's covered).</summary>
        Snapshot ReadArea(Rectangle area)
        {
            if (window == null) return Snapshot.FromScreen(area);
            Bitmap shot = CurrentFrame();
            return shot == null ? null : Snapshot.FromBitmap(shot, area);
        }

        /// <summary>A recent capture of the game window (shared, don't dispose), or null if it can't be read.</summary>
        Bitmap CurrentFrame()
        {
            if (window == null) return null;
            if (!window.Exists) throw new StopRun("the game window was closed");
            long now = clock.ElapsedMilliseconds;
            if (frame == null || now - frameAt > FrameReuseMs)
            {
                if (frame != null) frame.Dispose();
                frame = window.CaptureClient();
                frameAt = now;
            }
            return frame;
        }

        /// <summary>A new capture of the game window, owned by the caller.</summary>
        Bitmap FreshFrame()
        {
            if (!window.Exists) throw new StopRun("the game window was closed");
            return window.CaptureClient();
        }

        /// <summary>Moves the pointer off an area that's about to be watched, so a hover highlight doesn't spoil the match.</summary>
        void MoveOutOf(Rectangle area)
        {
            Point c;
            if (o.Background)
            {
                c = virtualPos;
            }
            else
            {
                Point s;
                if (!Native.TryGetCursorPos(out s)) return;
                Point origin = Origin();
                c = new Point(s.X - origin.X, s.Y - origin.Y);
            }
            Rectangle zone = Rectangle.Inflate(area, 8, 8);
            if (!zone.Contains(c)) return;

            Rectangle bounds = PointerBounds();
            int gap = rnd.Next(20, 70);
            bool below = zone.Bottom - c.Y <= c.Y - zone.Top;
            int y = below ? zone.Bottom + gap : zone.Top - gap;
            if (y >= bounds.Bottom || y < bounds.Top) y = below ? zone.Top - gap : zone.Bottom + gap;
            int x = c.X + rnd.Next(-40, 41);
            MoveMouse(new Point(Clamp(x, bounds.Left, bounds.Right - 1), Clamp(y, bounds.Top, bounds.Bottom - 1)), 60);
        }

        /// <summary>Where the pointer may go, in step coordinates.</summary>
        Rectangle PointerBounds()
        {
            if (window != null) return new Rectangle(Point.Empty, window.ClientSize);
            return SystemInformation.VirtualScreen;
        }

        Point Origin()
        {
            if (window == null) return Point.Empty;
            if (!window.Exists) throw new StopRun("the game window was closed");
            return window.ClientOrigin;
        }

        // ---------------------------------------------------------------- clicking

        void ClickStep(ClickPoint p, int index)
        {
            CheckEnemy(index, true); // again right before clicking: the name card is certainly up by now
            bool fightStep = p != null && index == o.NameStep && o.Catch != null;
            // Radiants only: a 2v2 without a radiant gets the step itself, the Run button, rather than a round.
            bool runFromDuo = fightStep && duoCycle == cycle && RadiantsOnly && radiantCycle != cycle;
            if (fightStep && duoCycle == cycle && !runFromDuo)
            {
                DuoRound();
                return;
            }
            if (fightStep && !runFromDuo) p = ChooseAction(p);
            if (fightStep && RadiantsOnly && ReferenceEquals(p, o.Points[index]))
            {
                ranCycle = cycle;
                SetActivity("no radiant" + (runFromDuo ? " in this 2v2" : "") + ", so step " + (index + 1) + " (Run)");
            }
            if (p == null)
            {
                PressButton(MouseButton.Left, false, false);
            }
            else
            {
                MoveMouse(PickSpot(p), p.IsArea ? Math.Min(p.Width, p.Height) : 16);
                PressButton(p.Button, p.DoubleClick, p.BorrowPointer);
            }
            clicks++;
            ReportProgress(false);
        }

        Point PickSpot(ClickPoint p)
        {
            if (p.IsLasso) return SpotInside(p.Shape, p.Area);
            if (p.IsArea) return new Point(Biased(p.X, p.Width), Biased(p.Y, p.Height));

            int x = p.X, y = p.Y;
            if (o.JitterPx > 0)
            {
                x += rnd.Next(-o.JitterPx, o.JitterPx + 1);
                y += rnd.Next(-o.JitterPx, o.JitterPx + 1);
            }
            return new Point(x, y);
        }

        /// <summary>
        /// A random spot inside a lasso, usually near the middle of its box like a person aiming, and not on its edge.
        /// A thin or hollow outline falls back to any spot inside, then to its first point.
        /// </summary>
        Point SpotInside(Point[] shape, Rectangle box)
        {
            for (int k = 0; k < 300; k++)
            {
                Point p = k < 150
                    ? new Point(Biased(box.X, box.Width), Biased(box.Y, box.Height))
                    : new Point(box.X + rnd.Next(box.Width), box.Y + rnd.Next(box.Height));
                if (Lasso.ContainsWithRoom(shape, p, 2)) return p;
            }
            for (int k = 0; k < 300; k++)
            {
                var p = new Point(box.X + rnd.Next(box.Width), box.Y + rnd.Next(box.Height));
                if (Lasso.Contains(shape, p.X, p.Y)) return p;
            }
            return shape[0];
        }

        /// <summary>A random coordinate inside [start, start + size), usually near the middle like a person aiming.</summary>
        int Biased(int start, int size)
        {
            double margin = Math.Min(3.0, size / 6.0);
            double lo = start + margin, hi = start + size - 1 - margin;
            if (hi <= lo) return start + size / 2;
            double mid = (lo + hi) / 2, sigma = (hi - lo) / 5.0;
            for (int k = 0; k < 10; k++)
            {
                double v = mid + Gaussian() * sigma;
                if (v >= lo && v <= hi) return (int)Math.Round(v);
            }
            return (int)Math.Round(mid);
        }

        /// <summary>Moves the pointer to <paramref name="target"/> (step coordinates): the game's own pointer in background mode, else the real one.</summary>
        void MoveMouse(Point target, int targetSize)
        {
            if (o.Background)
            {
                if (o.HumanMouse)
                    Play(HumanMouse.Plan(virtualPos, target, targetSize, rnd), p => Posted(window.PostMove(p)));
                else
                    Posted(window.PostMove(target));
                virtualPos = target;
                return;
            }

            CheckUserMouse();
            Point origin = Origin();
            var screenTarget = new Point(target.X + origin.X, target.Y + origin.Y);
            Point from;
            if (o.HumanMouse && Native.TryGetCursorPos(out from))
                Play(HumanMouse.Plan(from, screenTarget, targetSize, rnd), p => Pointer.MoveTo(p.X, p.Y));
            else
                Pointer.MoveTo(screenTarget.X, screenTarget.Y);

            Point now;
            restPos = Native.TryGetCursorPos(out now) ? now : screenTarget;
            watchUserMouse = o.StopOnUserMouse;
        }

        void Play(System.Collections.Generic.List<HumanMouse.Sample> path, Action<Point> moveTo)
        {
            if (!HumanMouse.Play(path, moveTo, stopSignal)) throw new StopRun(null);
        }

        void PressButton(MouseButton button, bool doubleClick, bool borrowPointer)
        {
            if (o.Background)
            {
                if (borrowPointer) PressWithBorrowedPointer(button, doubleClick);
                else PressInWindow(button, doubleClick);
                return;
            }
            if (!o.HumanMouse)
            {
                Pointer.Click(button, doubleClick);
                return;
            }
            // A short settle before pressing, and a real press-and-hold, like a finger.
            Thread.Sleep(Triangular(40, 140, 0.35));
            Pointer.Button(button, true);
            Thread.Sleep(Triangular(55, 130, 0.35));
            Pointer.Button(button, false);
            if (doubleClick)
            {
                Thread.Sleep(Triangular(70, 140, 0.4));
                Pointer.Button(button, true);
                Thread.Sleep(Triangular(45, 100, 0.4));
                Pointer.Button(button, false);
            }
        }

        /// <summary>The same press-and-hold timing, delivered as messages to the game window.</summary>
        void PressInWindow(MouseButton button, bool doubleClick)
        {
            Point at = virtualPos;
            if (o.HumanMouse) Thread.Sleep(Triangular(40, 140, 0.35));
            Posted(window.PostButton(button, true, false, at));
            if (o.HumanMouse) Thread.Sleep(Triangular(55, 130, 0.35));
            Posted(window.PostButton(button, false, false, at));
            if (!doubleClick) return;
            Thread.Sleep(o.HumanMouse ? Triangular(70, 140, 0.4) : 30);
            Posted(window.PostButton(button, true, true, at));
            if (o.HumanMouse) Thread.Sleep(Triangular(45, 100, 0.4));
            Posted(window.PostButton(button, false, false, at));
        }

        /// <summary>
        /// For clicks the game places using the real pointer (its world map asks Windows where the pointer
        /// is rather than reading the click): parks the real pointer on the spot just long enough for the
        /// game to see the press and release, then hands it back.
        /// </summary>
        void PressWithBorrowedPointer(MouseButton button, bool doubleClick)
        {
            Point at = virtualPos;
            Point origin = Origin();
            var spot = new Point(at.X + origin.X, at.Y + origin.Y);
            Point saved;
            bool haveSaved = Native.TryGetCursorPos(out saved);

            if (o.HumanMouse) Thread.Sleep(Triangular(40, 140, 0.35));
            Native.SetCursorPos(spot.X, spot.Y);
            try
            {
                Thread.Sleep(35); // a couple of game frames with the pointer in place
                Posted(window.PostMove(at));
                Posted(window.PostButton(button, true, false, at));
                Thread.Sleep(o.HumanMouse ? Triangular(55, 130, 0.35) : 40);
                Posted(window.PostButton(button, false, false, at));
                if (doubleClick)
                {
                    Thread.Sleep(o.HumanMouse ? Triangular(70, 140, 0.4) : 30);
                    Posted(window.PostButton(button, true, true, at));
                    Thread.Sleep(o.HumanMouse ? Triangular(45, 100, 0.4) : 30);
                    Posted(window.PostButton(button, false, false, at));
                }
                Thread.Sleep(90); // let the game act on the click before the pointer leaves
            }
            finally
            {
                if (haveSaved)
                {
                    // Hand the pointer back, keeping any movement you made in the meantime.
                    Point now;
                    if (Native.TryGetCursorPos(out now))
                        Native.SetCursorPos(saved.X + now.X - spot.X, saved.Y + now.Y - spot.Y);
                    else
                        Native.SetCursorPos(saved.X, saved.Y);
                }
            }
        }

        void Posted(bool accepted)
        {
            if (accepted) return;
            if (!window.Exists) throw new StopRun("the game window was closed");
            throw new StopRun("Windows blocked the clicks to the game window (try running Auto Clicker as administrator)");
        }

        // ---------------------------------------------------------------- timing

        /// <summary>Waits while staying responsive to Stop and to the user grabbing the mouse.</summary>
        void Pause(int ms)
        {
            long end = clock.ElapsedMilliseconds + ms;
            while (true)
            {
                long left = end - clock.ElapsedMilliseconds;
                if (left <= 0) return;
                if (stopSignal.WaitOne((int)Math.Min(left, 50))) throw new StopRun(null);
                CheckUserMouse();
                ReportProgress(false);
            }
        }

        int NextDelay(ClickPoint p)
        {
            int baseMs = p != null && p.DelayMs >= 0 ? p.DelayMs : o.IntervalMs;
            if (o.RandomPercent <= 0) return baseMs;
            int spread = (int)Math.Round(baseMs * o.RandomPercent / 100.0);
            return Triangular(Math.Max(0, baseMs - spread), baseMs + spread, 0.5);
        }

        long NextBreakTime()
        {
            if (o.BreakEveryMs <= 0) return long.MaxValue;
            return clock.ElapsedMilliseconds + (long)(o.BreakEveryMs * (0.6 + 0.8 * rnd.NextDouble()));
        }

        void TakeBreak()
        {
            long end = clock.ElapsedMilliseconds + Triangular(o.BreakMinMs, o.BreakMaxMs, 0.4);
            while (true)
            {
                long left = end - clock.ElapsedMilliseconds;
                if (left <= 0) break;
                SetActivity("taking a break, " + (left + 999) / 1000 + " s left");
                Pause((int)Math.Min(left, 1000));
            }
            SetActivity("");
        }

        /// <summary>Random value in [min, max], most often around <paramref name="mode"/> (0–1) of the way along.</summary>
        int Triangular(int min, int max, double mode)
        {
            if (max <= min) return Math.Max(0, min);
            double u = rnd.NextDouble();
            double f = u < mode ? Math.Sqrt(u * mode) : 1 - Math.Sqrt((1 - u) * (1 - mode));
            return (int)Math.Round(min + f * (max - min));
        }

        double Gaussian()
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // ---------------------------------------------------------------- bookkeeping

        void ThrowIfStopped()
        {
            if (stopSignal.WaitOne(0)) throw new StopRun(null);
        }

        void CheckUserMouse()
        {
            if (!watchUserMouse || o.Background) return;
            Point now;
            if (!Native.TryGetCursorPos(out now)) return;
            if (Math.Abs(now.X - restPos.X) > UserMoveTolerancePx || Math.Abs(now.Y - restPos.Y) > UserMoveTolerancePx)
                throw new StopRun("you moved the mouse") { Notify = false }; // you're at the PC already
        }

        void SetActivity(string text)
        {
            if (text == activity) return;
            activity = text;
            ReportProgress(true);
        }

        void ReportProgress(bool force)
        {
            long now = clock.ElapsedMilliseconds;
            if (!force && now - lastReportMs < 100) return;
            lastReportMs = now;
            var progress = Progress;
            if (progress != null) progress(clicks, cycle, activity);
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : v > max ? max : v;
        }
    }
}
