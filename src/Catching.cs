using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoClicker
{
    /// <summary>Which edge of the window a piece of the game's interface stays attached to when the window changes size.</summary>
    public enum Anchor { Left, Center, Right }

    /// <summary>What a spot is used for, which decides how "Test" shows it.</summary>
    public enum SpotKind { Click, Text, Bar }

    /// <summary>A place in the Lumits window the catching needs: a button to click, text to read or a bar to measure.</summary>
    public class Spot
    {
        public readonly string Key, Label, Screen;
        public readonly SpotKind Kind;
        readonly Rectangle box;
        readonly Anchor anchor;

        public Spot(string key, string label, string screen, SpotKind kind, Anchor anchor, int x, int y, int w, int h)
        {
            Key = key;
            Label = label;
            Screen = screen;
            Kind = kind;
            this.anchor = anchor;
            box = new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// Where it is in a window with this content size. The game scales its interface with the window's height
        /// and keeps each part attached to its edge, so wider or narrower windows only shift things sideways.
        /// </summary>
        public Rectangle For(Size client)
        {
            double s = client.Height / (double)CatchSpots.LayoutHeight;
            double x;
            switch (anchor)
            {
                case Anchor.Left: x = box.X * s; break;
                case Anchor.Right: x = client.Width - (CatchSpots.LayoutWidth - box.X) * s; break;
                default: x = client.Width / 2.0 + (box.X - CatchSpots.LayoutWidth / 2.0) * s; break;
            }
            return new Rectangle((int)Math.Round(x), (int)Math.Round(box.Y * s), (int)Math.Round(box.Width * s), (int)Math.Round(box.Height * s));
        }
    }

    /// <summary>Every spot, as measured on a 1920 × 1025 Lumits window.</summary>
    public static class CatchSpots
    {
        public const int LayoutWidth = 1920, LayoutHeight = 1025;

        public const string Siphon = "siphon", Capture = "capture", Strike = "strike", EnemyBar = "enemyBar", LeadBar = "leadBar", LeadName = "leadName";
        public const string FightText = "fightText", FirstLumit = "firstLumit";
        public const string CaughtTitle = "caughtTitle", CaughtName = "caughtName", Nature = "nature", Keep = "keep", Release = "release";
        public const string ConfirmTitle = "confirmTitle", ConfirmRelease = "confirmRelease", ConfirmCancel = "confirmCancel";
        public const string TeamIcon = "teamIcon", PanelTitle = "panelTitle", FoodTab = "foodTab", BarkTea = "barkTea";
        public const string ItemName = "itemName", ItemCount = "itemCount", DropSpot = "dropSpot", PanelBar = "panelBar", PanelClose = "panelClose";
        public const string Lead2Bar = "lead2Bar", Partner2Bar = "partner2Bar", Enemy1Bar = "enemy1Bar", Enemy2Bar = "enemy2Bar";
        public const string Enemy1Card = "enemy1Card", Enemy2Card = "enemy2Card", Back = "back";
        public const string Enemy1Tap = "enemy1Tap", Enemy2Tap = "enemy2Tap", Enemy1TapLow = "enemy1TapLow", Enemy2TapLow = "enemy2TapLow";
        public const string QuickCheckTitle = "quickCheckTitle";

        public static string Iv(Stat s) { return "iv" + s; }

        const string Battle = "Fight, your turn", Caught = "New Lumit caught", Confirm = "Release confirmation", Map = "Map", Panel = "Infibex panel, Food tab";
        const string Duo = "2v2 fight", DuoTarget = "2v2, choosing a target", QuickCheck = "Quick check (the game's captcha)";

        public static readonly Spot[] All =
        {
            new Spot(Siphon, "Heat Siphon button", Battle, SpotKind.Click, Anchor.Center, 1240, 876, 120, 26),
            new Spot(Capture, "Capture button", Battle, SpotKind.Click, Anchor.Center, 1400, 806, 120, 30),
            new Spot(Strike, "Strike button (0 MP, shows it's your turn)", Battle, SpotKind.Click, Anchor.Center, 528, 876, 120, 26),
            new Spot(EnemyBar, "Enemy's HP bar", Battle, SpotKind.Bar, Anchor.Right, 1525, 170, 118, 7),
            // The bar itself, right of the red "HP" label (measured on the real window: 117/182 fills to x 364).
            new Spot(LeadBar, "Your Lumit's HP bar", Battle, SpotKind.Bar, Anchor.Left, 294, 170, 112, 7),
            new Spot(LeadName, "Your Lumit's name", Battle, SpotKind.Text, Anchor.Left, 240, 128, 180, 28),
            new Spot(FightText, "Fight message (\"… fainted!\")", "Fight", SpotKind.Text, Anchor.Center, 700, 84, 520, 28),
            new Spot(FirstLumit, "First Lumit to send in", "Your Lumit fainted", SpotKind.Click, Anchor.Center, 460, 890, 180, 36),
            new Spot(CaughtTitle, "\"New Lumit caught\" title", Caught, SpotKind.Text, Anchor.Center, 520, 242, 280, 26),
            new Spot(CaughtName, "Caught Lumit's name", Caught, SpotKind.Text, Anchor.Center, 470, 278, 370, 50),
            // Just the "IV n" line: the big stat number ends 4 px above these.
            new Spot(Iv(Stat.HP), "HP IV", Caught, SpotKind.Text, Anchor.Center, 1314, 198, 72, 16),
            new Spot(Iv(Stat.Attack), "Attack IV", Caught, SpotKind.Text, Anchor.Center, 1495, 303, 72, 16),
            new Spot(Iv(Stat.Defense), "Defense IV", Caught, SpotKind.Text, Anchor.Center, 1493, 511, 72, 16),
            new Spot(Iv(Stat.SpAtk), "Sp. Atk IV", Caught, SpotKind.Text, Anchor.Center, 1133, 303, 72, 16),
            new Spot(Iv(Stat.SpDef), "Sp. Def IV", Caught, SpotKind.Text, Anchor.Center, 1132, 511, 72, 16),
            new Spot(Iv(Stat.Speed), "Speed IV", Caught, SpotKind.Text, Anchor.Center, 1315, 615, 72, 16),
            new Spot(Nature, "Nature", Caught, SpotKind.Text, Anchor.Center, 1190, 668, 330, 26),
            new Spot(Keep, "Keep button", Caught, SpotKind.Click, Anchor.Center, 1110, 850, 180, 28),
            new Spot(Release, "Release button", Caught, SpotKind.Click, Anchor.Center, 1440, 850, 160, 28),
            new Spot(ConfirmTitle, "\"Release …?\" title", Confirm, SpotKind.Text, Anchor.Center, 1100, 590, 380, 34),
            new Spot(ConfirmRelease, "Release (confirm) button", Confirm, SpotKind.Click, Anchor.Center, 1455, 742, 115, 30),
            new Spot(ConfirmCancel, "Cancel button", Confirm, SpotKind.Click, Anchor.Center, 1290, 742, 110, 30),
            new Spot(TeamIcon, "Infibex's icon (double-clicked)", Map, SpotKind.Click, Anchor.Left, 30, 160, 45, 45),
            new Spot(PanelTitle, "Panel title (Lumit's name)", Panel, SpotKind.Text, Anchor.Left, 60, 28, 160, 28),
            new Spot(FoodTab, "Food tab", Panel, SpotKind.Click, Anchor.Left, 722, 36, 40, 20),
            new Spot(BarkTea, "Bark Tea (dragged from)", Panel, SpotKind.Click, Anchor.Left, 508, 102, 46, 46),
            new Spot(ItemName, "Selected food's name", Panel, SpotKind.Text, Anchor.Left, 555, 425, 220, 24),
            new Spot(ItemCount, "How many you have", Panel, SpotKind.Text, Anchor.Left, 750, 466, 64, 22),
            new Spot(DropSpot, "Infibex's picture (dropped on)", Panel, SpotKind.Click, Anchor.Left, 150, 230, 200, 160),
            new Spot(PanelBar, "HP bar in the panel", Panel, SpotKind.Bar, Anchor.Left, 38, 489, 403, 6),
            new Spot(PanelClose, "Close button (X)", Panel, SpotKind.Click, Anchor.Left, 834, 35, 20, 20),
            // A 2v2 on the PC (measured in the owner's game on 14 November): two cards a side, one above the other, unlike
            // the phone's four in a row. Your lead's is the lower one on the left, your second Lumit's the upper one. The
            // wild Lumits' are on the right: the upper card is the left one on the field (picked first as a target), the
            // lower card the right one. Each bar spot runs the bar's length (106 px) in its middle rows; each name spot
            // stops short of the card's portrait, which can be yellow (Lithorse's mane starts at x 1720 on the lower card).
            new Spot(Lead2Bar, "Your lead's HP bar (lower card)", Duo, SpotKind.Bar, Anchor.Left, 243, 223, 106, 5),
            new Spot(Partner2Bar, "Your second Lumit's HP bar (upper card)", Duo, SpotKind.Bar, Anchor.Left, 195, 85, 106, 5),
            new Spot(Enemy1Bar, "Left wild Lumit's HP bar (upper card)", Duo, SpotKind.Bar, Anchor.Right, 1651, 74, 106, 5),
            new Spot(Enemy2Bar, "Right wild Lumit's HP bar (lower card)", Duo, SpotKind.Bar, Anchor.Right, 1603, 201, 106, 5),
            new Spot(Enemy1Card, "Left wild Lumit's name (upper card)", Duo, SpotKind.Text, Anchor.Right, 1606, 34, 154, 30),
            new Spot(Enemy2Card, "Right wild Lumit's name (lower card)", Duo, SpotKind.Text, Anchor.Right, 1558, 161, 154, 30),
            new Spot(Back, "Back button", DuoTarget, SpotKind.Text, Anchor.Center, 1225, 806, 80, 31),
            new Spot(Enemy1Tap, "Left wild Lumit (clicked)", DuoTarget, SpotKind.Click, Anchor.Center, 1174, 569, 43, 43),
            new Spot(Enemy2Tap, "Right wild Lumit (clicked)", DuoTarget, SpotKind.Click, Anchor.Center, 1494, 612, 43, 43),
            new Spot(Enemy1TapLow, "Left wild Lumit, lower", DuoTarget, SpotKind.Click, Anchor.Center, 1174, 612, 43, 36),
            new Spot(Enemy2TapLow, "Right wild Lumit, lower", DuoTarget, SpotKind.Click, Anchor.Center, 1501, 655, 43, 36),
            // The panel sits in the middle of the window. Its title's letters spanned 872–1049 × 228–248 on both of the
            // owner's captcha pictures of 15 November; the line of instructions under it starts at y 268.
            new Spot(QuickCheckTitle, "\"Quick check\" title", QuickCheck, SpotKind.Text, Anchor.Center, 850, 220, 222, 36),
        };

        public static Spot Get(string key)
        {
            return All.First(s => s.Key == key);
        }

        /// <summary>Every spot for a window of this size, with your own picks taking the place of the defaults.</summary>
        public static Dictionary<string, Rectangle> Resolve(Size client, IDictionary<string, Rectangle> picked)
        {
            var result = new Dictionary<string, Rectangle>();
            foreach (Spot s in All)
            {
                Rectangle r;
                result[s.Key] = picked != null && picked.TryGetValue(s.Key, out r) && r.Width > 0 ? r : s.For(client);
            }
            return result;
        }

        /// <summary>One of a 2v2's four HP bars: how full, counting only red that starts at the bar's left end.</summary>
        public static double DuoBar(Bitmap shot, Dictionary<string, Rectangle> spots, string key)
        {
            return ScreenText.BarFromLeft(shot, spots[key], ScreenText.IsRed);
        }

        /// <summary>
        /// A 2v2 is showing: both your Lumits' bars, and a wild Lumit's. A 1v1 has neither of your 2v2 bars, while its
        /// red card frame and a red radiant's portrait sit where your lead's and the lower wild bar would be.
        /// </summary>
        public static bool DuoShowing(Bitmap shot, Dictionary<string, Rectangle> spots)
        {
            if (shot == null) return false;
            return DuoBar(shot, spots, Lead2Bar) > 0.01 && DuoBar(shot, spots, Partner2Bar) > 0.01 &&
                   Math.Max(DuoBar(shot, spots, Enemy1Bar), DuoBar(shot, spots, Enemy2Bar)) > 0.01;
        }
    }

    /// <summary>What a run needs for catching and healing.</summary>
    public class CatchOptions
    {
        /// <summary>Catch the lumits ticked in the list (otherwise only healing and the faint alert are on).</summary>
        public bool CatchOn;
        /// <summary>Always press Keep, and only note what would have happened.</summary>
        public bool Practice;
        /// <summary>Catch a radiant by itself, rather than stopping the run before attacking it.</summary>
        public bool CatchRadiants;
        /// <summary>
        /// No fighting: the radiant check's step is the Run button, and it's clicked for every Lumit that isn't a radiant,
        /// in 2v2s too. Ticked Lumits aren't caught. A radiant is caught as usual, or stops the run.
        /// </summary>
        public bool RadiantsOnly;
        /// <summary>
        /// Fight a radiant found in a 2v2, as in a 1v1. Off: the run stops before anything is clicked, for you to catch it.
        /// Off while 2v2 target clicks haven't been seen working on the PC: a click on the wrong Lumit, or a second Lumit's
        /// move that hits both, could kill the radiant. Nothing turns it on yet but the tests.
        /// </summary>
        public bool CatchDuoRadiants;
        /// <summary>In a 2v2, your second Lumit's moves for weakening a Lumit you catch, and for finishing off one you don't (0–5).</summary>
        public int PartnerWeaken = 2, PartnerKill = 5;
        /// <summary>Background mode: park the real pointer on a wild Lumit while clicking it as a target, like map clicks.</summary>
        public bool BorrowForTargets = true;
        /// <summary>Feed Bark Tea after a fight that ends below this many HP; 0 = never.</summary>
        public int HealBelowHp;
        public int LeadMaxHp;
        /// <summary>Move the real pointer along with the Bark Tea drag (for games that follow the pointer while dragging).</summary>
        public bool DragRealPointer;
        public Dictionary<string, Rectangle> Spots;
        public CatchLog Log;
    }

    /// <summary>
    /// Wears a radiant down without killing it. Its only attack is Strike, whose damage is measured in each fight from how
    /// far that radiant's own HP bar drops, and only while even a crit would leave it alive.
    /// </summary>
    public class RadiantStrikes
    {
        /// <summary>Only hit while its HP is above this many Strikes: 1.5 for a crit, plus 20%. If Strike does 20, that's 36 HP.</summary>
        const double StrikesLeft = 1.5 * 1.2;
        /// <summary>Until a Strike has been measured, only hit from this share of its HP or more, where one Strike can't kill it.</summary>
        const double UnmeasuredFrom = 0.6;
        /// <summary>A hit this many times another one, give or take, is that one with a crit.</summary>
        const double CritLow = 1.35, CritHigh = 1.65;

        readonly List<double> hits = new List<double>();
        double before = -1;

        public void Clear()
        {
            hits.Clear();
            before = -1;
        }

        /// <summary>The HP bar at your turn, which shows what the last Strike took.</summary>
        public void Observe(double enemy)
        {
            if (before >= 0 && enemy >= 0 && before - enemy > 0.01) hits.Add(before - enemy);
            before = -1;
        }

        /// <summary>A Strike is about to be used, with the HP bar at this.</summary>
        public void Struck(double enemy)
        {
            before = enemy;
        }

        /// <summary>
        /// Strike's usual damage this fight: the biggest hit that wasn't a crit. A crit counts until a normal hit shows it was
        /// one, which only makes it stop attacking sooner, and a hit lowered by the radiant healing itself doesn't win.
        /// </summary>
        public double Damage
        {
            get
            {
                double damage = 0;
                foreach (double hit in hits)
                    if (!hits.Any(other => hit >= other * CritLow && hit <= other * CritHigh)) damage = Math.Max(damage, hit);
                return damage;
            }
        }

        /// <summary>Whether to Strike at this HP. Otherwise it's Capture, which is also the answer when the bar can't be read.</summary>
        public bool ShouldStrike(double enemy)
        {
            if (enemy < 0) return false;
            double damage = Damage;
            return damage > 0 ? enemy > damage * StrikesLeft : enemy >= UnmeasuredFrom;
        }
    }

    /// <summary>What the caught screen said, as far as it could be read.</summary>
    public class CaughtReading
    {
        public string Name = "";
        public string Nature = "";
        /// <summary>IV per stat; -1 where it couldn't be read for certain.</summary>
        public readonly Dictionary<Stat, int> Ivs = new Dictionary<Stat, int>();
        /// <summary>Per stat, how many ways of reading its label found each number.</summary>
        public readonly Dictionary<Stat, Dictionary<int, int>> Votes = new Dictionary<Stat, Dictionary<int, int>>();

        /// <summary>Any reading at all saw 25: for keeping, that's enough.</summary>
        public bool MightBe25(Stat s)
        {
            return Votes.ContainsKey(s) && Votes[s].ContainsKey(25);
        }

        public string IvText()
        {
            return string.Join(", ", Stats.All.Select(s => Stats.Short(s) + " " + (Ivs.ContainsKey(s) && Ivs[s] > 0 ? Ivs[s].ToString(CultureInfo.InvariantCulture) : "?")));
        }

        /// <summary>Reads everything off captures of the caught screen; the IVs from the last, most settled one.</summary>
        public static CaughtReading From(IList<Bitmap> shots, IDictionary<string, Rectangle> spots, IEnumerable<string> knownNatures)
        {
            var r = new CaughtReading();
            Bitmap first = shots[0], last = shots[shots.Count - 1];
            r.Name = LumitNames.Clean(ScreenText.Read(first, spots[CatchSpots.CaughtName], Ink.Light, 0, false) ?? "");
            string nature = ScreenText.Read(first, spots[CatchSpots.Nature], Ink.Light, 0, false) ?? "";
            Match word = Regex.Match(nature, @"[A-Za-z]{3,}");
            if (word.Success)
            {
                string read = char.ToUpperInvariant(word.Value[0]) + word.Value.Substring(1).ToLowerInvariant();
                string known = knownNatures.FirstOrDefault(n => LumitNames.Similar(n, read));
                r.Nature = known ?? read;
            }
            foreach (Stat s in Stats.All)
            {
                r.Votes[s] = ScreenText.ReadVotes(last, spots[CatchSpots.Iv(s)], 1, 25);
                r.Ivs[s] = ScreenText.Confident(r.Votes[s]);
            }
            return r;
        }
    }

    /// <summary>Keep or release, and why.</summary>
    public class CatchDecision
    {
        public bool Keep;
        public string Reason;
        /// <summary>Kept only because something it needed couldn't be read.</summary>
        public bool Unsure;
        /// <summary>The keep rules this catch surely meets, to tick off as found.</summary>
        public Stat MetIvs;
        public List<string> MetNatures = new List<string>();

        public static CatchDecision For(CaughtReading read, LumitCounter.Entry rules)
        {
            if (rules == null || !rules.HasKeepRules)
                return new CatchDecision { Keep = true, Reason = "no keep rules set for this Lumit" };

            var d = new CatchDecision();
            foreach (Stat s in Stats.All)
                if ((rules.KeepIvs & s) != 0 && read.Ivs[s] == 25) d.MetIvs |= s;
            if (read.Nature.Length > 0) d.MetNatures.AddRange(rules.KeepNatures.Where(n => LumitNames.Similar(n, read.Nature)));
            if (d.MetIvs != Stat.None || d.MetNatures.Count > 0)
            {
                var why = Stats.All.Where(s => (d.MetIvs & s) != 0).Select(s => Stats.Label(s) + " IV 25").ToList();
                if (d.MetNatures.Count > 0) why.Add("nature " + read.Nature);
                d.Keep = true;
                d.Reason = string.Join(", ", why);
                return d;
            }

            var unsure = new List<string>();
            // Only a 25 matters. Tested on the game's own labels, every "IV 25" showed at least one reading of 25,
            // and no other number ever did, so a ticked stat with no 25 among its readings isn't 25. One that only
            // some readings call 25 is kept, but not ticked off: its picture goes with the unread ones to check.
            foreach (Stat s in Stats.All)
                if ((rules.KeepIvs & s) != 0 && read.MightBe25(s))
                    return new CatchDecision { Keep = true, Unsure = true, Reason = Stats.Label(s) + " IV 25 (some readings)" };
            // Nothing read on any label means the screen wasn't read properly, not that there's no 25.
            if (rules.KeepIvs != Stat.None && Stats.All.All(s => !read.Votes.ContainsKey(s) || read.Votes[s].Count == 0))
                unsure.Add("any IV");
            if (rules.KeepNatures.Count > 0 && read.Nature.Length == 0) unsure.Add("nature");
            // A release can't be undone, so anything in doubt is kept for you to look at.
            if (unsure.Count > 0) return new CatchDecision { Keep = true, Unsure = true, Reason = "couldn't read " + string.Join(", ", unsure) + " for sure" };
            return new CatchDecision { Keep = false, Reason = "no wanted IV 25 or nature" };
        }
    }

    /// <summary>
    /// A plain text diary of every catch, plus a picture of each caught screen that was (or in practice would have
    /// been) released, and of each one kept only because something couldn't be read.
    /// </summary>
    public class CatchLog
    {
        public readonly string FilePath, PicturesDir, UnsureDir;
        readonly object sync = new object();

        public CatchLog(string folder)
        {
            FilePath = Path.Combine(folder, "catches.txt");
            PicturesDir = Path.Combine(folder, "released");
            UnsureDir = Path.Combine(folder, "unread");
        }

        public static CatchLog Default
        {
            get { return new CatchLog(Path.GetDirectoryName(AppSettings.DefaultPath)); }
        }

        public void Write(string fightName, CaughtReading read, CatchDecision decision, bool practice, Bitmap picture, string note = "")
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string what = practice ? (decision.Keep ? "kept (would keep)" : "kept (would RELEASE)") : (decision.Keep ? "KEPT" : "RELEASED");
            string line = time + "  " + fightName + (read.Name.Length > 0 ? " → " + read.Name : "") + "  |  " + read.IvText() +
                          "  |  " + (read.Nature.Length > 0 ? read.Nature : "nature ?") + "  |  " + what + ": " + decision.Reason +
                          (note.Length > 0 ? "  |  " + note : "");
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                    string folder = !decision.Keep ? PicturesDir : decision.Unsure ? UnsureDir : null;
                    if (folder != null && picture != null)
                    {
                        Directory.CreateDirectory(folder);
                        string file = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + " " +
                                                           (read.Name.Length > 0 ? read.Name : fightName));
                        // Unread ones are kept lossless, so the reading can be tuned on them; releases are only for checking.
                        if (decision.Keep) picture.Save(file + ".png", System.Drawing.Imaging.ImageFormat.Png);
                        else File.WriteAllBytes(file + ".jpg", GoldText.ToJpeg(picture));
                    }
                }
                catch (Exception)
                {
                    // The log is a convenience; catching goes on without it.
                }
            }
        }
    }
}
