using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AutoClicker
{
    /// <summary>Reads a lumit's name off its name card ("Coalython Lvl 19" → "Coalython").</summary>
    public static class LumitNames
    {
        /// <summary>The name, "" if none could be made out, or null if this PC has no text recognition.</summary>
        public static string Read(Bitmap card)
        {
            if (card == null) return "";
            string text = ScreenText.Read(card, new Rectangle(Point.Empty, card.Size), Ink.Light, 0, false);
            return text == null ? null : Clean(text);
        }

        /// <summary>Keeps the words of the name: drops the level ("Lvl 19"), the radiant star and stray marks.</summary>
        public static string Clean(string text)
        {
            var words = new List<string>();
            foreach (string token in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Any(char.IsDigit)) break;
                string letters = new string(token.Where(char.IsLetter).ToArray());
                if (letters.Length <= 3 && letters.StartsWith("lv", StringComparison.OrdinalIgnoreCase)) break;
                if (letters.Length < 2) continue; // the star, or a speck read as a letter
                words.Add(char.ToUpperInvariant(letters[0]) + letters.Substring(1).ToLowerInvariant());
            }
            return string.Join(" ", words);
        }

        /// <summary>Same name allowing for a letter or two misread, depending on its length.</summary>
        public static bool Similar(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            int allowed = Math.Max(a.Length, b.Length) >= 9 ? 2 : Math.Max(a.Length, b.Length) >= 5 ? 1 : 0;
            return Distance(a, b) <= allowed;
        }

        public static int Distance(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diag = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int up = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diag + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diag = up;
                }
            }
            return row[b.Length];
        }
    }

    /// <summary>The six stats on the caught screen, as flags so a set of them fits in one number.</summary>
    [Flags]
    public enum Stat
    {
        None = 0,
        HP = 1,
        Attack = 2,
        Defense = 4,
        SpAtk = 8,
        SpDef = 16,
        Speed = 32
    }

    public static class Stats
    {
        public static readonly Stat[] All = { Stat.HP, Stat.Attack, Stat.Defense, Stat.SpAtk, Stat.SpDef, Stat.Speed };

        public static string Label(Stat s)
        {
            switch (s)
            {
                case Stat.SpAtk: return "Sp. Atk";
                case Stat.SpDef: return "Sp. Def";
                default: return s.ToString();
            }
        }

        public static string Short(Stat s)
        {
            switch (s)
            {
                case Stat.Attack: return "Atk";
                case Stat.Defense: return "Def";
                case Stat.SpAtk: return "SpA";
                case Stat.SpDef: return "SpD";
                case Stat.Speed: return "Spe";
                default: return "HP";
            }
        }

        public static string List(Stat set)
        {
            return string.Join(", ", All.Where(s => (set & s) != 0).Select(Short));
        }
    }

    /// <summary>The natures in Lumits and what they raise and lower.</summary>
    public static class Natures
    {
        public static readonly string[][] Table =
        {
            new[] { "Fierce", "+Attack", "−Sp. Atk" },
            new[] { "Savage", "+Attack", "−Sp. Def" },
            new[] { "Arcane", "+Sp. Atk", "−Attack" },
            new[] { "Volatile", "+Sp. Atk", "−Defense" },
            new[] { "Ironclad", "+Defense", "−Sp. Atk" },
            new[] { "Fortified", "+Defense", "−Speed" },
            new[] { "Serene", "+Sp. Def", "−Attack" },
            new[] { "Wary", "+Sp. Def", "−Speed" },
            new[] { "Swift", "+Speed", "−Defense" },
            new[] { "Restless", "+Speed", "−Sp. Def" },
            new[] { "Vigorous", "+HP (10%)", "−Speed" },
            new[] { "Tenacious", "+HP (10%)", "−Sp. Atk" },
            new[] { "Balanced", "neutral", "" },
            new[] { "Curious", "neutral", "" },
        };

        public static IEnumerable<string> Names { get { return Table.Select(row => row[0]); } }

        /// <summary>"+Attack, −Sp. Def" for a known nature, "" otherwise.</summary>
        public static string Effect(string name)
        {
            string[] row = Table.FirstOrDefault(r => string.Equals(r[0], name, StringComparison.OrdinalIgnoreCase));
            if (row == null) return "";
            return row[2].Length > 0 ? row[1] + ", " + row[2] : row[1];
        }
    }

    /// <summary>
    /// Tally of the lumits met in battle, by name, kept across runs in a small file, together with what to do
    /// about each one: catch it or not, and which catches are worth keeping.
    /// </summary>
    public class LumitCounter
    {
        public class Entry
        {
            /// <summary>The spelling read most often, or the one you typed.</summary>
            public string Name;
            public int Met;
            public int Radiants;
            public DateTime LastMet;
            /// <summary>Named by you, so readings no longer change the name.</summary>
            public bool Fixed;
            /// <summary>Every way the name has been read, with how often.</summary>
            public Dictionary<string, int> Spellings = new Dictionary<string, int>();

            /// <summary>Weaken and catch it instead of attacking to kill.</summary>
            public bool Catch;
            /// <summary>Name on the caught screen (its first evolution, e.g. Axel for a Tzik); "" until one is caught.</summary>
            public string CaughtName = "";
            /// <summary>Keep a catch with IV 25 in any of these stats.</summary>
            public Stat KeepIvs;
            /// <summary>Keep a catch with any of these natures.</summary>
            public List<string> KeepNatures = new List<string>();
            /// <summary>Your lead's move for weakening it, 0–5 from the top left; Heat Siphon unless you pick another.</summary>
            public int Attack = DefaultAttack;
            /// <summary>Recent damage of that move, as a share of the enemy's HP bar (0–1).</summary>
            public List<double> Siphon = new List<double>();
            /// <summary>Recent damage of your second Lumit's weakening move, in 2v2 fights.</summary>
            public List<double> PartnerHits = new List<double>();
            public int Caught, Kept, Released;
            /// <summary>Keep rules a catch has met, ticked off the rules above since you have one now.</summary>
            public Stat FoundIvs;
            public List<string> FoundNatures = new List<string>();

            public Entry Clone()
            {
                var c = (Entry)MemberwiseClone();
                c.Spellings = new Dictionary<string, int>(Spellings);
                c.KeepNatures = new List<string>(KeepNatures);
                c.Siphon = new List<double>(Siphon);
                c.PartnerHits = new List<double>(PartnerHits);
                c.FoundNatures = new List<string>(FoundNatures);
                return c;
            }

            public bool Matches(string name)
            {
                return Name == name || Spellings.ContainsKey(name);
            }

            public bool HasKeepRules { get { return KeepIvs != Stat.None || KeepNatures.Count > 0; } }

            /// <summary>
            /// Nothing left to catch it for: no keep rules, or every one met by a catch. Not while it's ticked with no rules,
            /// since then you're catching it to keep them all.
            /// </summary>
            public bool IsPerfect { get { return !HasKeepRules && !Catch && Name != Unreadable; } }

            /// <summary>"IV 25: HP, Spe; Fierce", or "" with no rules.</summary>
            public string NeedsText { get { return RulesText(KeepIvs, KeepNatures); } }

            public string FoundText { get { return RulesText(FoundIvs, FoundNatures); } }

            static string RulesText(Stat ivs, List<string> natures)
            {
                var parts = new List<string>();
                if (ivs != Stat.None) parts.Add("IV 25: " + Stats.List(ivs));
                if (natures.Count > 0) parts.Add(string.Join(", ", natures));
                return string.Join("; ", parts);
            }

            /// <summary>Its attack's usual damage: the middle of the recent ones, so an occasional crit doesn't count. 0 if none yet.</summary>
            public double TypicalSiphon { get { return Middle(Siphon); } }

            public double TypicalPartnerDamage { get { return Middle(PartnerHits); } }

            public string AttackName { get { return LeadMoves[Attack]; } }

            static double Middle(List<double> hits)
            {
                if (hits.Count == 0) return 0;
                List<double> sorted = hits.OrderBy(d => d).ToList();
                int n = sorted.Count;
                return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
            }

            /// <summary>The fight name with the caught name after it once known: "Tzik → Axel".</summary>
            public string Display { get { return CaughtName.Length > 0 && CaughtName != Name ? Name + " → " + CaughtName : Name; } }
        }

        /// <summary>Shown for battles whose name card couldn't be read.</summary>
        public const string Unreadable = "(name not read)";
        /// <summary>Your lead's moves from the top left along the rows: Infibex's. A Lumit's attack is one of these.</summary>
        public static readonly string[] LeadMoves = { "Strike", "Pyrecrest Assault", "Heat Siphon", "Flash Horn", "Heavy Strike", "Pursuit Jab" };
        public static readonly string[] Slots = { "top left", "top middle", "top right", "bottom left", "bottom middle", "bottom right" };
        public const int DefaultAttack = 2;
        const int SiphonSamples = 15;

        readonly object sync = new object();
        readonly string path;
        readonly List<Entry> entries = new List<Entry>();
        readonly SortedSet<string> natures = new SortedSet<string>(Natures.Names, StringComparer.OrdinalIgnoreCase);
        int total, thisRun, sinceRadiant, lastRadiantAfter;
        string last = "";

        /// <summary>Raised (on any thread) after the counts or rules change.</summary>
        public event Action Changed;

        public LumitCounter(string path)
        {
            this.path = path;
        }

        public static string DefaultPath
        {
            get { return Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath), "lumits.txt"); }
        }

        /// <summary>
        /// Where an area's list is kept. The first area keeps the list from before there were areas, whatever it's renamed
        /// to; each other one has its own file.
        /// </summary>
        public static string PathFor(string area, IList<string> areas)
        {
            if (areas.Count == 0 || areas[0] == area) return DefaultPath;
            string safe = new string(area.Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_').ToArray());
            return Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath), "lumits-" + safe + ".txt");
        }

        public int Total { get { lock (sync) return total; } }
        public int ThisRun { get { lock (sync) return thisRun; } }
        public int SinceRadiant { get { lock (sync) return sinceRadiant; } }
        /// <summary>How many lumits it took to meet the latest radiant (0 = none met yet).</summary>
        public int LastRadiantAfter { get { lock (sync) return lastRadiantAfter; } }
        public string Last { get { lock (sync) return last; } }

        /// <summary>Copies of the per-name counts and rules, most met first.</summary>
        public List<Entry> Entries()
        {
            lock (sync) return entries.OrderByDescending(e => e.Met).ThenBy(e => e.Name).Select(e => e.Clone()).ToList();
        }

        /// <summary>A copy of the entry a read name belongs to (allowing for misreads), or null if it's never been met.</summary>
        public Entry Lookup(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (sync)
            {
                Entry e = FindExisting(name);
                return e == null ? null : e.Clone();
            }
        }

        public bool AnyToCatch
        {
            get { lock (sync) return entries.Any(e => e.Catch); }
        }

        /// <summary>Every nature known: the game's list, plus any seen on caught screens or typed in rules.</summary>
        public List<string> KnownNatures()
        {
            lock (sync) return natures.ToList();
        }

        public void StartRun()
        {
            lock (sync) thisRun = 0;
            OnChanged();
        }

        /// <summary>Counts a battle, reading the name off <paramref name="card"/> (which it disposes) on a worker thread.</summary>
        public void Add(Bitmap card, bool radiant)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string name;
                try { name = LumitNames.Read(card); }
                finally { if (card != null) card.Dispose(); }
                Record(name, radiant);
            });
        }

        /// <summary>Counts a battle whose name is already known ("" or null if it wasn't read). Returns the entry's name.</summary>
        public string Record(string name, bool radiant)
        {
            string entryName;
            lock (sync)
            {
                total++;
                thisRun++;
                sinceRadiant++;
                if (radiant)
                {
                    lastRadiantAfter = sinceRadiant;
                    sinceRadiant = 0;
                }
                if (string.IsNullOrEmpty(name)) name = Unreadable;
                Entry e = Find(name);
                e.Met++;
                if (radiant) e.Radiants++;
                e.LastMet = DateTime.Now;
                if (name != Unreadable)
                {
                    int n;
                    e.Spellings.TryGetValue(name, out n);
                    e.Spellings[name] = n + 1;
                    // An odd misread early on gets outvoted as the correct spelling keeps turning up.
                    if (!e.Fixed && e.Spellings[name] > Votes(e, e.Name)) e.Name = name;
                }
                last = e.Name;
                entryName = e.Name;
                SaveQuietly();
            }
            OnChanged();
            return entryName;
        }

        static int Votes(Entry e, string spelling)
        {
            int n;
            return e.Spellings.TryGetValue(spelling, out n) ? n : 0;
        }

        /// <summary>The entry for a name, allowing for a letter misread (e.g. "Coatython" counts as "Coalython").</summary>
        Entry Find(string name)
        {
            return FindExisting(name) ?? Create(name);
        }

        Entry FindExisting(string name)
        {
            Entry exact = entries.FirstOrDefault(e => e.Matches(name));
            if (exact != null || name == Unreadable) return exact;
            int allowed = name.Length >= 9 ? 2 : name.Length >= 5 ? 1 : 0;
            return entries.Where(e => e.Name != Unreadable &&
                                      (LumitNames.Distance(e.Name, name) <= allowed || e.Spellings.Keys.Any(s => LumitNames.Distance(s, name) <= allowed)))
                          .OrderByDescending(e => e.Met).FirstOrDefault();
        }

        Entry Create(string name)
        {
            var e = new Entry { Name = name };
            entries.Add(e);
            return e;
        }

        /// <summary>Renames an entry; if the new name already exists, the two are merged.</summary>
        public void Rename(string from, string to)
        {
            to = (to ?? "").Trim();
            if (to.Length == 0 || to == from) return;
            lock (sync)
            {
                Entry e = entries.FirstOrDefault(x => x.Name == from);
                if (e == null) return;
                Entry into = entries.FirstOrDefault(x => x != e && x.Matches(to));
                if (into == null)
                {
                    into = e;
                }
                else
                {
                    into.Met += e.Met;
                    into.Radiants += e.Radiants;
                    if (e.LastMet > into.LastMet) into.LastMet = e.LastMet;
                    foreach (KeyValuePair<string, int> s in e.Spellings) into.Spellings[s.Key] = Votes(into, s.Key) + s.Value;
                    into.Catch |= e.Catch;
                    into.KeepIvs |= e.KeepIvs;
                    foreach (string n in e.KeepNatures) if (!into.KeepNatures.Contains(n, StringComparer.OrdinalIgnoreCase)) into.KeepNatures.Add(n);
                    if (into.Attack == e.Attack) into.Siphon.AddRange(e.Siphon);
                    into.PartnerHits.AddRange(e.PartnerHits);
                    if (into.CaughtName.Length == 0) into.CaughtName = e.CaughtName;
                    into.Caught += e.Caught;
                    into.Kept += e.Kept;
                    into.Released += e.Released;
                    into.FoundIvs |= e.FoundIvs;
                    foreach (string n in e.FoundNatures) if (!into.FoundNatures.Contains(n, StringComparer.OrdinalIgnoreCase)) into.FoundNatures.Add(n);
                    // Found by either one means it's no longer needed.
                    into.KeepIvs &= ~into.FoundIvs;
                    into.KeepNatures.RemoveAll(n => into.FoundNatures.Contains(n, StringComparer.OrdinalIgnoreCase));
                    entries.Remove(e);
                }
                // Your spelling wins from now on; keep it as a known reading so it's found again.
                into.Name = to;
                into.Fixed = true;
                if (!into.Spellings.ContainsKey(to)) into.Spellings[to] = 0;
                if (last == from) last = to;
                SaveQuietly();
            }
            OnChanged();
        }

        /// <summary>Takes a Lumit off this area's list, with its counts and rules.</summary>
        public void Remove(string name)
        {
            lock (sync)
            {
                Entry e = entries.FirstOrDefault(x => x.Name == name);
                if (e == null) return;
                entries.Remove(e);
                if (last == name) last = "";
                SaveQuietly();
            }
            OnChanged();
        }

        // ---------------------------------------------------------------- catching

        public void SetCatch(string name, bool on)
        {
            Change(name, e => e.Catch = on);
        }

        public void SetKeepRules(string name, Stat ivs, IEnumerable<string> keepNatures)
        {
            List<string> list = keepNatures.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Change(name, e =>
            {
                e.KeepIvs = ivs;
                e.KeepNatures = list;
                // Asked for again, so it's wanted again rather than found.
                e.FoundIvs &= ~ivs;
                e.FoundNatures.RemoveAll(n => list.Contains(n, StringComparer.OrdinalIgnoreCase));
            });
            lock (sync) foreach (string n in list) natures.Add(n);
        }

        /// <summary>
        /// Ticks off the keep rules a kept catch met, so later catches with only those are released. Once none are left the
        /// Lumit is perfect and is no longer caught. Returns a copy of the entry afterwards, or null if it isn't listed.
        /// </summary>
        public Entry TickOff(string name, Stat ivs, IEnumerable<string> metNatures)
        {
            List<string> met = metNatures.ToList();
            Entry after = null;
            Change(name, e =>
            {
                Stat found = e.KeepIvs & ivs;
                e.KeepIvs &= ~found;
                e.FoundIvs |= found;
                foreach (string n in e.KeepNatures.Where(n => met.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList())
                {
                    e.KeepNatures.Remove(n);
                    if (!e.FoundNatures.Contains(n, StringComparer.OrdinalIgnoreCase)) e.FoundNatures.Add(n);
                }
                // With no rules a ticked Lumit keeps every catch, which is the opposite of done: stop catching it.
                if (!e.HasKeepRules) e.Catch = false;
                after = e.Clone();
            });
            return after;
        }

        /// <summary>Remembers how much of the enemy's HP bar one hit of its attack took.</summary>
        public void AddSiphonDamage(string name, double share)
        {
            Change(name, e =>
            {
                e.Siphon.Add(share);
                if (e.Siphon.Count > SiphonSamples) e.Siphon.RemoveAt(0);
            });
        }

        /// <summary>Remembers what your second Lumit's weakening move took, in a 2v2.</summary>
        public void AddPartnerDamage(string name, double share)
        {
            Change(name, e =>
            {
                e.PartnerHits.Add(share);
                if (e.PartnerHits.Count > SiphonSamples) e.PartnerHits.RemoveAt(0);
            });
        }

        /// <summary>Picks your lead's move for weakening it. Damage measured with another move says nothing now, so it's dropped.</summary>
        public void SetAttack(string name, int slot)
        {
            if (slot < 0 || slot >= LeadMoves.Length) return;
            Change(name, e =>
            {
                if (e.Attack == slot) return;
                e.Attack = slot;
                e.Siphon.Clear();
            });
        }

        /// <summary>Notes a catch and whether it was kept.</summary>
        public void RecordCatch(string name, string caughtName, string nature, bool kept)
        {
            Change(name, e =>
            {
                if (!string.IsNullOrEmpty(caughtName)) e.CaughtName = caughtName;
                e.Caught++;
                if (kept) e.Kept++;
                else e.Released++;
            });
            if (!string.IsNullOrEmpty(nature)) LearnNature(nature);
        }

        public void LearnNature(string nature)
        {
            bool added;
            lock (sync) added = natures.Add(nature);
            if (added)
            {
                lock (sync) SaveQuietly();
                OnChanged();
            }
        }

        void Change(string name, Action<Entry> change)
        {
            lock (sync)
            {
                Entry e = entries.FirstOrDefault(x => x.Name == name) ?? FindExisting(name);
                if (e == null) return;
                change(e);
                SaveQuietly();
            }
            OnChanged();
        }

        public void Reset()
        {
            lock (sync)
            {
                // Counts go; what you set up for catching stays, since it took effort to choose.
                entries.RemoveAll(e => !e.Catch && !e.HasKeepRules && e.FoundIvs == Stat.None && e.FoundNatures.Count == 0);
                foreach (Entry e in entries)
                {
                    e.Met = e.Radiants = e.Caught = e.Kept = e.Released = 0;
                    e.LastMet = DateTime.MinValue;
                }
                total = thisRun = sinceRadiant = lastRadiantAfter = 0;
                last = "";
                SaveQuietly();
            }
            OnChanged();
        }

        void OnChanged()
        {
            var changed = Changed;
            if (changed != null) changed();
        }

        // ---------------------------------------------------------------- file

        public static LumitCounter Load(string path)
        {
            var c = new LumitCounter(path);
            try
            {
                if (File.Exists(path)) c.ReadFrom(path);
            }
            catch (Exception)
            {
                // An unreadable tally shouldn't stop the app from opening; start counting afresh.
            }
            return c;
        }

        void ReadFrom(string file)
        {
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line[0] == '#' || eq <= 0) continue;
                string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                switch (key)
                {
                    case "total": total = Int(val); break;
                    case "sinceRadiant": sinceRadiant = Int(val); break;
                    case "lastRadiantAfter": lastRadiantAfter = Int(val); break;
                    case "last": last = val; break;
                    case "natures":
                        foreach (string n in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) natures.Add(n.Trim());
                        break;
                    case "lumit":
                        {
                            // Name|Met|Radiants|LastMet|Fixed|Spellings|Catch|CaughtName|KeepIvs|KeepNatures|Siphon|Caught|Kept|Released|FoundIvs|FoundNatures
                            string[] f = val.Split('|');
                            if (f.Length < 3 || f[0].Length == 0) break;
                            DateTime when;
                            DateTime.TryParse(Field(f, 3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when);
                            var e = new Entry { Name = f[0], Met = Int(f[1]), Radiants = Int(f[2]), LastMet = when, Fixed = Field(f, 4) == "1" };
                            foreach (string pair in Items(Field(f, 5), ','))
                            {
                                int colon = pair.LastIndexOf(':');
                                if (colon > 0) e.Spellings[pair.Substring(0, colon)] = Int(pair.Substring(colon + 1));
                            }
                            e.Catch = Field(f, 6) == "1";
                            e.CaughtName = Field(f, 7);
                            e.KeepIvs = (Stat)(Int(Field(f, 8)) & 63);
                            e.KeepNatures = Items(Field(f, 9), ';').ToList();
                            foreach (string s in Items(Field(f, 10), ';'))
                            {
                                double d;
                                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && d <= 1) e.Siphon.Add(d);
                            }
                            e.Caught = Int(Field(f, 11));
                            e.Kept = Int(Field(f, 12));
                            e.Released = Int(Field(f, 13));
                            e.FoundIvs = (Stat)(Int(Field(f, 14)) & 63);
                            e.FoundNatures = Items(Field(f, 15), ';').ToList();
                            // Lists from before areas had no attack or second-Lumit damage.
                            if (Field(f, 16).Length > 0) e.Attack = Math.Min(LeadMoves.Length - 1, Int(Field(f, 16)));
                            foreach (string s in Items(Field(f, 17), ';'))
                            {
                                double d;
                                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && d <= 1) e.PartnerHits.Add(d);
                            }
                            foreach (string n in e.KeepNatures) natures.Add(n);
                            entries.Add(e);
                            break;
                        }
                }
            }
        }

        static string Field(string[] f, int i)
        {
            return f.Length > i ? f[i] : "";
        }

        static IEnumerable<string> Items(string s, char separator)
        {
            return s.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);
        }

        void SaveQuietly()
        {
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine("# Auto Clicker lumits counter");
                sb.AppendLine("total=" + total.ToString(inv));
                sb.AppendLine("sinceRadiant=" + sinceRadiant.ToString(inv));
                sb.AppendLine("lastRadiantAfter=" + lastRadiantAfter.ToString(inv));
                sb.AppendLine("last=" + last);
                sb.AppendLine("natures=" + string.Join(",", natures));
                sb.AppendLine("# lumit=Name|Met|Radiants|LastMet|Fixed|Spelling:count,...|Catch|CaughtName|KeepIvs|Nature;...|Siphon;...|Caught|Kept|Released|FoundIvs|Nature;...|Attack|PartnerHits;...");
                foreach (Entry e in entries)
                {
                    sb.AppendLine("lumit=" + string.Join("|", new[]
                    {
                        e.Name, e.Met.ToString(inv), e.Radiants.ToString(inv), e.LastMet.ToString("o", inv), e.Fixed ? "1" : "0",
                        string.Join(",", e.Spellings.Select(s => s.Key + ":" + s.Value.ToString(inv))),
                        e.Catch ? "1" : "0", e.CaughtName, ((int)e.KeepIvs).ToString(inv), string.Join(";", e.KeepNatures),
                        string.Join(";", e.Siphon.Select(d => d.ToString("0.###", inv))),
                        e.Caught.ToString(inv), e.Kept.ToString(inv), e.Released.ToString(inv),
                        ((int)e.FoundIvs).ToString(inv), string.Join(";", e.FoundNatures),
                        e.Attack.ToString(inv), string.Join(";", e.PartnerHits.Select(d => d.ToString("0.###", inv)))
                    }));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Write aside first, so a crash mid-write can't wipe the tally.
                string temp = path + ".new";
                File.WriteAllText(temp, sb.ToString());
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception)
            {
                // Counting carries on in memory; the next save tries again.
            }
        }

        static int Int(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? Math.Max(0, v) : 0;
        }
    }
}
