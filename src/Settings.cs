using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// Everything the app remembers, stored as simple key=value lines.
    /// Saved lists (profiles) use the same format without the app-wide keys.
    /// </summary>
    public class AppSettings
    {
        public readonly List<ClickPoint> Points = new List<ClickPoint>();
        public int IntervalMs = 100;
        public bool RandomizeSpeed;
        public int RandomPercent = 25;
        public bool RandomizePosition;
        public int JitterPx = 3;
        public bool RepeatForever = true;
        public int Cycles = 10;
        public bool RedoPrevious = true;

        public bool HumanMouse = true;
        public bool StopOnUserMouse = true;
        public bool Breaks = true;
        public int BreakEveryMin = 12;
        public int BreakMinSec = 15;
        public int BreakMaxSec = 60;

        /// <summary>The game window step positions are measured from; empty means the whole screen.</summary>
        public string TargetProcess = "";
        public string TargetTitle = "";
        public bool Background;

        /// <summary>Drive the pointer through a Pico HID bridge instead of SendInput.</summary>
        public bool HidMouse;
        public string HidPort = "";

        /// <summary>Radiant alert: check the enemy's name for gold before clicking step AlertStep (1-based).</summary>
        public bool AlertEnabled;
        /// <summary>The attack step and the enemy's name card, used by both the radiant check and the counter.</summary>
        public int AlertStep = 2;
        public Rectangle AlertArea;
        /// <summary>Count the lumits met, by name (needs the name card above).</summary>
        public bool CountLumits = true;

        /// <summary>Catch the lumits ticked in the list.</summary>
        public bool CatchOn;
        /// <summary>Always press Keep, and only log what it would have done.</summary>
        public bool CatchPractice = true;
        /// <summary>Catch a radiant by itself, rather than stopping the run before attacking it.</summary>
        public bool CatchRadiants = true;
        /// <summary>
        /// Radiants only: no fighting. The radiant check's step is the Run button, clicked for every Lumit that isn't a
        /// radiant (ticked ones and 2v2s too); a radiant is caught, or stops the run, as set above.
        /// </summary>
        public bool RadiantsOnly;
        /// <summary>In a 2v2, the move your second Lumit weakens a Lumit you catch with, 0–5 from the top left (top right: Gnashing Blades).</summary>
        public int PartnerWeaken = 2;
        /// <summary>In a 2v2, the move your second Lumit finishes off a Lumit you don't catch with (bottom right: Rift Rend).</summary>
        public int PartnerKill = 5;
        /// <summary>The area being farmed. Each area has its own Lumit list with catch ticks, keep rules and attacks.</summary>
        public string Area = DefaultArea;
        public List<string> Areas = new List<string> { DefaultArea };
        public const string DefaultArea = "Cave 1";
        /// <summary>Feed Bark Tea after a fight that ends below this many HP; 0 = never.</summary>
        public int HealBelowHp = 30;
        public int LeadMaxHp = 182;
        public bool DragRealPointer;
        /// <summary>Catching spots you picked yourself (in game-window coordinates); the rest use the Lumits layout.</summary>
        public readonly Dictionary<string, Rectangle> CatchSpots = new Dictionary<string, Rectangle>();

        /// <summary>Catching and healing are both off.</summary>
        public bool CatchAndHealOff { get { return !CatchOn && HealBelowHp <= 0; } }
        public int AlertGoldPercent = 2;
        /// <summary>ntfy topic or Discord webhook URL; kept out of saved lists since it's personal.</summary>
        public string AlertPhone = "";
        /// <summary>Also tell the phone when a run stops on its own, with the reason.</summary>
        public bool AlertOnStop = true;

        public Keys ToggleHotkey = Keys.F6;
        public Keys AddPointHotkey = Keys.F7;
        public bool TopMost;

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AutoClicker", "settings.txt");
            }
        }

        public static AppSettings LoadOrDefault(string path)
        {
            var s = new AppSettings();
            try
            {
                if (File.Exists(path)) s.ReadFrom(path, true);
            }
            catch (Exception)
            {
                // Unreadable settings shouldn't stop the app from opening; start with defaults.
            }
            return s;
        }

        public void Save(string path) { WriteTo(path, true); }

        public void SaveProfile(string path) { WriteTo(path, false); }

        public void LoadProfile(string path) { ReadFrom(path, false); }

        void WriteTo(string path, bool includeAppSettings)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var lines = new List<string>();
            lines.Add(includeAppSettings ? "# Auto Clicker settings" : "# Auto Clicker click list");
            lines.Add("interval=" + Num(IntervalMs));
            lines.Add("randomizeSpeed=" + Flag(RandomizeSpeed));
            lines.Add("randomPercent=" + Num(RandomPercent));
            lines.Add("randomizePosition=" + Flag(RandomizePosition));
            lines.Add("jitterPx=" + Num(JitterPx));
            lines.Add("repeatForever=" + Flag(RepeatForever));
            lines.Add("cycles=" + Num(Cycles));
            lines.Add("redoPrevious=" + Flag(RedoPrevious));
            lines.Add("humanMouse=" + Flag(HumanMouse));
            lines.Add("stopOnUserMouse=" + Flag(StopOnUserMouse));
            lines.Add("breaks=" + Flag(Breaks));
            lines.Add("breakEveryMin=" + Num(BreakEveryMin));
            lines.Add("breakMinSec=" + Num(BreakMinSec));
            lines.Add("breakMaxSec=" + Num(BreakMaxSec));
            lines.Add("targetProcess=" + TargetProcess);
            lines.Add("targetTitle=" + TargetTitle);
            lines.Add("background=" + Flag(Background));
            lines.Add("hidMouse=" + Flag(HidMouse));
            lines.Add("hidPort=" + HidPort);
            lines.Add("alert=" + Flag(AlertEnabled));
            lines.Add("alertStep=" + Num(AlertStep));
            lines.Add("alertArea=" + Num(AlertArea.X) + "," + Num(AlertArea.Y) + "," + Num(AlertArea.Width) + "," + Num(AlertArea.Height));
            lines.Add("alertGold=" + Num(AlertGoldPercent));
            lines.Add("catch=" + Flag(CatchOn));
            lines.Add("catchPractice=" + Flag(CatchPractice));
            lines.Add("catchRadiants=" + Flag(CatchRadiants));
            lines.Add("radiantsOnly=" + Flag(RadiantsOnly));
            lines.Add("partnerWeaken=" + Num(PartnerWeaken));
            lines.Add("partnerKill=" + Num(PartnerKill));
            lines.Add("healBelow=" + Num(HealBelowHp));
            lines.Add("leadMaxHp=" + Num(LeadMaxHp));
            lines.Add("dragRealPointer=" + Flag(DragRealPointer));
            foreach (KeyValuePair<string, Rectangle> s in CatchSpots)
                lines.Add("spot." + s.Key + "=" + Num(s.Value.X) + "," + Num(s.Value.Y) + "," + Num(s.Value.Width) + "," + Num(s.Value.Height));
            if (includeAppSettings)
            {
                lines.Add("alertPhone=" + AlertPhone);
                lines.Add("alertOnStop=" + Flag(AlertOnStop));
                lines.Add("countLumits=" + Flag(CountLumits));
                lines.Add("area=" + Area);
                lines.Add("areas=" + string.Join("|", Areas));
                lines.Add("toggleHotkey=" + Num((int)ToggleHotkey));
                lines.Add("addPointHotkey=" + Num((int)AddPointHotkey));
                lines.Add("topMost=" + Flag(TopMost));
            }
            lines.Add("# point=X,Y,Button,Double,DelayAfterMs(-1 = interval),Width,Height,Wait,WatchX,WatchY,WatchW,WatchH," +
                      "Match%,ReactMinMs,ReactMaxMs,TimeoutSec,OnTimeout,Snapshot");
            foreach (ClickPoint p in Points) lines.Add("point=" + p.Serialize());

            File.WriteAllLines(path, lines);
        }

        void ReadFrom(string path, bool includeAppSettings)
        {
            string[] lines = File.ReadAllLines(path);
            Points.Clear();
            CatchSpots.Clear();

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key.StartsWith("spot."))
                {
                    string spot = line.Substring(5, eq - 5).Trim(); // keys keep their case
                    Rectangle r;
                    if (TryRect(val, out r) && r.Width > 0) CatchSpots[spot] = r;
                    continue;
                }
                switch (key)
                {
                    case "catch": CatchOn = val == "1"; break;
                    case "catchpractice": CatchPractice = val == "1"; break;
                    case "catchradiants": CatchRadiants = val == "1"; break;
                    case "radiantsonly": RadiantsOnly = val == "1"; break;
                    case "partnerweaken": PartnerWeaken = Math.Max(0, Math.Min(5, Int(val, PartnerWeaken))); break;
                    case "partnerkill": PartnerKill = Math.Max(0, Math.Min(5, Int(val, PartnerKill))); break;
                    case "healbelow": HealBelowHp = Math.Max(0, Int(val, HealBelowHp)); break;
                    case "leadmaxhp": LeadMaxHp = Math.Max(1, Int(val, LeadMaxHp)); break;
                    case "dragrealpointer": DragRealPointer = val == "1"; break;
                    case "interval": IntervalMs = Int(val, IntervalMs); break;
                    case "randomizespeed": RandomizeSpeed = val == "1"; break;
                    case "randompercent": RandomPercent = Int(val, RandomPercent); break;
                    case "randomizeposition": RandomizePosition = val == "1"; break;
                    case "jitterpx": JitterPx = Int(val, JitterPx); break;
                    case "repeatforever": RepeatForever = val == "1"; break;
                    case "cycles": Cycles = Int(val, Cycles); break;
                    case "redoprevious": RedoPrevious = val == "1"; break;
                    case "humanmouse": HumanMouse = val == "1"; break;
                    case "stoponusermouse": StopOnUserMouse = val == "1"; break;
                    case "breaks": Breaks = val == "1"; break;
                    case "breakeverymin": BreakEveryMin = Int(val, BreakEveryMin); break;
                    case "breakminsec": BreakMinSec = Int(val, BreakMinSec); break;
                    case "breakmaxsec": BreakMaxSec = Int(val, BreakMaxSec); break;
                    case "targetprocess": TargetProcess = val; break;
                    case "targettitle": TargetTitle = val; break;
                    case "background": Background = val == "1"; break;
                    case "hidmouse": HidMouse = val == "1"; break;
                    case "hidport": HidPort = val; break;
                    case "alert": AlertEnabled = val == "1"; break;
                    case "alertstep": AlertStep = Int(val, AlertStep); break;
                    case "alertgold": AlertGoldPercent = Int(val, AlertGoldPercent); break;
                    case "alertarea":
                        {
                            Rectangle r;
                            if (TryRect(val, out r)) AlertArea = r;
                            break;
                        }
                    case "point":
                        {
                            ClickPoint p = ClickPoint.Parse(val);
                            if (p != null) Points.Add(p);
                            break;
                        }
                }

                if (!includeAppSettings) continue;
                switch (key)
                {
                    case "togglehotkey": ToggleHotkey = (Keys)Int(val, (int)ToggleHotkey); break;
                    case "addpointhotkey": AddPointHotkey = (Keys)Int(val, (int)AddPointHotkey); break;
                    case "topmost": TopMost = val == "1"; break;
                    case "alertphone": AlertPhone = val; break;
                    case "alertonstop": AlertOnStop = val == "1"; break;
                    case "countlumits": CountLumits = val == "1"; break;
                    case "area": if (val.Trim().Length > 0) Area = val.Trim(); break;
                    case "areas":
                        {
                            var list = val.Split('|').Select(a => a.Trim()).Where(a => a.Length > 0).Distinct().ToList();
                            if (list.Count > 0) Areas = list;
                            break;
                        }
                }
            }
            if (!Areas.Contains(Area)) Areas.Add(Area);
        }

        static bool TryRect(string val, out Rectangle r)
        {
            r = Rectangle.Empty;
            string[] a = val.Split(',');
            if (a.Length != 4) return false;
            r = new Rectangle(Int(a[0], 0), Int(a[1], 0), Math.Max(0, Int(a[2], 0)), Math.Max(0, Int(a[3], 0)));
            return true;
        }

        static string Num(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        static string Flag(bool v) { return v ? "1" : "0"; }

        static int Int(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }
}
