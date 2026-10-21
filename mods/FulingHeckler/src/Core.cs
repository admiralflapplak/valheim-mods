// Pure logic: no Unity or Valheim types in this file, so it can be tested outside the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FulingHeckler
{
    public enum When { First, Repeat, Milestone, Any }
    public enum DeathCategory { Environment, Falling, Trees, Creatures, FriendlyFire, Other }

    /// <summary>Raw facts about a death, pulled out of Valheim's HitData by the death patch.</summary>
    public class DeathInfo
    {
        public string HitType;       // HitData.HitType as a string, e.g. "Drowning", "EnemyHit"
        public string KillerPrefab;  // e.g. "Troll", null if no attacker
        public string KillerName;    // localized display name, e.g. "Troll" or another player's name
        public bool KillerIsPlayer;
        public bool KillerIsSelf;
    }

    public class DeathEvent
    {
        public string Key;                 // what the Fuling keeps a tally of, e.g. "drowning", "enemy:troll"
        public DeathCategory Category;
        public string KillerName;
        public List<string> Chain;         // most specific key first, ending in "generic"
    }

    public static class DeathClassifier
    {
        private static readonly HashSet<string> Bosses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Eikthyr", "gd_king", "Bonemass", "Dragon", "GoblinKing", "SeekerQueen", "Fader"
        };

        public static DeathEvent Classify(DeathInfo d)
        {
            string key;
            DeathCategory cat;

            switch (d.HitType ?? "")
            {
                case "Drowning": key = "drowning"; cat = DeathCategory.Environment; break;
                case "Fall": key = "fall"; cat = DeathCategory.Falling; break;
                case "Burning":
                case "CinderFire": key = "burning"; cat = DeathCategory.Environment; break;
                case "Freezing": key = "freezing"; cat = DeathCategory.Environment; break;
                case "Poisoned": key = "poison"; cat = DeathCategory.Environment; break;
                case "Smoke": key = "smoke"; cat = DeathCategory.Environment; break;
                case "EdgeOfWorld": key = "edge"; cat = DeathCategory.Environment; break;
                case "Water":
                case "AshlandsOcean": key = "boiled"; cat = DeathCategory.Environment; break;
                case "Tree": key = "tree"; cat = DeathCategory.Trees; break;
                case "Self": key = "self"; cat = DeathCategory.FriendlyFire; break;
                case "Cart": key = "cart"; cat = DeathCategory.Other; break;
                case "Boat": key = "boat"; cat = DeathCategory.Other; break;
                case "Structural": key = "structural"; cat = DeathCategory.Other; break;
                case "Stalagtite": key = "stalactite"; cat = DeathCategory.Environment; break;
                case "AshlandsLava": key = "lava"; cat = DeathCategory.Environment; break;
                case "Incinerator": key = "incinerator"; cat = DeathCategory.Other; break;
                case "Turret":
                case "Catapult": key = "siege"; cat = DeathCategory.Other; break;
                default: key = null; cat = DeathCategory.Other; break;
            }

            var chain = new List<string>();

            if (key == null)
            {
                if (d.KillerIsSelf) { key = "self"; cat = DeathCategory.FriendlyFire; }
                else if (d.KillerIsPlayer) { key = "friend"; cat = DeathCategory.FriendlyFire; }
                else if (!string.IsNullOrEmpty(d.KillerPrefab))
                {
                    key = "enemy:" + d.KillerPrefab.ToLowerInvariant();
                    cat = DeathCategory.Creatures;
                    chain.Add(key);
                    if (Bosses.Contains(d.KillerPrefab)) chain.Add("boss");
                    chain.Add("enemy");
                    chain.Add("generic");
                    return new DeathEvent { Key = key, Category = cat, KillerName = d.KillerName, Chain = chain };
                }
                else { key = "unknown"; cat = DeathCategory.Other; }
            }

            chain.Add(key);
            chain.Add("generic");
            return new DeathEvent { Key = key, Category = cat, KillerName = d.KillerName, Chain = chain };
        }
    }

    public class QuipLine
    {
        public string Event;  // lower case
        public When When;
        public string Text;
        public bool UsesKiller => Text.Contains("{killer}");
    }

    /// <summary>The quips file, parsed. One line per quip: event | when | text</summary>
    public class QuipBook
    {
        public readonly List<QuipLine> Lines = new List<QuipLine>();
        public readonly List<string> Warnings = new List<string>();
        public int Count => Lines.Count;

        public void Load(IEnumerable<string> rawLines)
        {
            Lines.Clear();
            Warnings.Clear();
            int n = 0;
            foreach (var raw in rawLines)
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var parts = line.Split(new[] { '|' }, 3);
                if (parts.Length < 3)
                {
                    Warnings.Add($"line {n}: expected 'event | when | text', skipped.");
                    continue;
                }

                if (!Enum.TryParse(parts[1].Trim(), true, out When when) || !Enum.IsDefined(typeof(When), when))
                {
                    Warnings.Add($"line {n}: unknown 'when' '{parts[1].Trim()}' (use first, repeat, milestone or any), skipped.");
                    continue;
                }

                string text = parts[2].Trim();
                if (text.Length == 0) continue;
                Lines.Add(new QuipLine { Event = parts[0].Trim().ToLowerInvariant(), When = when, Text = text });
            }
        }

        /// <summary>The built-in quips in a language (the game's language name, e.g. "German"), or null if there are none.</summary>
        public static string ReadBuiltInQuips(string language)
        {
            if (string.IsNullOrEmpty(language) || language == "English") return ReadDefaultQuips();
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("FulingHeckler.Quips." + language + ".txt"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        /// <summary>Lines older versions wrote in their default file, and what they are now: such a file is still unedited.</summary>
        private static readonly Dictionary<string, string> RetiredLines = new Dictionary<string, string>
        {
            { "enemy:drake | any | Ice-bird freeze you! {me} stay away from mountain. Mountain is mean.",
              "enemy:hatchling | any | Ice-bird freeze you! {me} stay away from mountain. Mountain is mean." },
        };

        /// <summary>Lines added to the default file since older versions wrote theirs: an older file without them is still unedited.</summary>
        private static readonly HashSet<string> AddedLines = new HashSet<string>
        {
            "lava | first | Floor is lava! Tall-one not know game? {me} know game. {me} WIN game.",
            "lava | repeat | Hot rock again! {me} smell you from far away. Crispy.",
            "lava | repeat | Tall-one go swimming in fire-water. {me} stay on cold rock. Smart {me}.",
            "incinerator | any | Tall-one jump in big junk-box! Box eat tall-one! {me} put junk in there, not tall-ones!",
            "incinerator | any | Poof! Tall-one gone! Like junk! {me} laughing SO hard!",
        };

        /// <summary>
        /// The same quips (an unedited file): comments, blank lines, line endings and spacing don't count, and neither do the
        /// default lines older versions wrote nor the ones added since.
        /// </summary>
        public static bool SameText(string a, string b)
        {
            IEnumerable<string> Norm(string t) => (t ?? "").Replace("\r", "").Split('\n')
                .Select(l => string.Join(" | ", l.Split('|').Select(x => string.Join(" ", x.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)))))
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => RetiredLines.TryGetValue(l, out string now) ? now : l)
                .Where(l => !AddedLines.Contains(l));
            return Norm(a).SequenceEqual(Norm(b));
        }

        public static string ReadDefaultQuips()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("FulingHeckler.DefaultQuips.txt"))
            {
                if (s == null) return "generic | any | Hehehe! Fuling has no words. Fuling laughing too hard.\n";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }

    public static class Placeholders
    {
        /// <summary>The words for an unknown name, an unknown killer and "27th" in the player's language (the plugin sets them).</summary>
        public static string TallOne = "tall-one", Something = "something";
        public static Func<int, string> OrdinalWord;   // null: English ("second", "27th")

        public static string Fill(string text, string playerName, string killerName, int count)
        {
            return text
                .Replace("{name}", string.IsNullOrEmpty(playerName) ? TallOne : playerName)
                .Replace("{killer}", string.IsNullOrEmpty(killerName) ? Something : killerName)
                .Replace("{count}", Ordinal(count))
                .Replace("{n}", count.ToString());
        }

        /// <summary>
        /// Fills {me}: his own name (chance) or plain "Fuling". The whole line uses the same one,
        /// so he doesn't switch names halfway through a sentence.
        /// </summary>
        public static string FillMe(string text, string ownName, double nameChance, Random rng)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("{me}", StringComparison.OrdinalIgnoreCase) < 0) return text;
            string me = !string.IsNullOrEmpty(ownName) && rng.NextDouble() < nameChance ? ownName : "Fuling";
            return System.Text.RegularExpressions.Regex.Replace(text, @"\{me\}", me, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static readonly string[] Words =
            { "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth" };

        /// <summary>"second", "27th" in English; other languages' own form ("27.", "27-й") through OrdinalWord.</summary>
        public static string Ordinal(int n)
        {
            if (OrdinalWord != null) return OrdinalWord(n);
            if (n >= 0 && n < Words.Length) return Words[n];
            int mod100 = n % 100;
            string suffix = (mod100 >= 11 && mod100 <= 13) ? "th"
                : (n % 10 == 1) ? "st" : (n % 10 == 2) ? "nd" : (n % 10 == 3) ? "rd" : "th";
            return n + suffix;
        }
    }

    public class PickerSettings
    {
        public float ChanceToComment = 0.6f;
        public bool AlwaysCommentOnFirst = true;
        public float CooldownSeconds = 90f;
        public HashSet<int> Milestones = new HashSet<int> { 5, 10, 25, 50, 100 };

        public static HashSet<int> ParseMilestones(string s)
        {
            var set = new HashSet<int>();
            foreach (var part in (s ?? "").Split(','))
                if (int.TryParse(part.Trim(), out int v) && v > 1) set.Add(v);
            return set;
        }
    }

    public class Decision
    {
        public string Line;        // null = the Fuling stays home
        public bool IsFirst;
        public bool IsMilestone;
        public bool IsSpecial => IsFirst || IsMilestone;
        public string Reason;
        public List<string> DeathChain;  // what killed you, most specific first (for his impressions)
    }

    /// <summary>
    /// Which player animations he uses to act out each kind of death:
    /// "fall: hard landing, slipping on ice | drowning: swimming | generic: dying backwards".
    /// Keys are the same as the quips file's events (enemy:troll, boss, enemy, fall, drowning... generic).
    /// </summary>
    public class ImpressionBook
    {
        public readonly Dictionary<string, List<string>> Moves = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static ImpressionBook Parse(string s)
        {
            var book = new ImpressionBook();
            foreach (var entry in (s ?? "").Split('|'))
            {
                int colon = entry.LastIndexOf(':');   // last colon, so "enemy:troll: ..." works
                if (colon <= 0) continue;
                string key = entry.Substring(0, colon).Trim();
                var names = NameList.Parse(entry.Substring(colon + 1));
                if (key.Length > 0 && names.Count > 0) book.Moves[key] = names;
            }
            return book;
        }

        /// <summary>The moves for the most specific matching key, falling back to "generic". Empty if none.</summary>
        public List<string> For(IList<string> chain)
        {
            if (chain != null)
                foreach (var key in chain)
                    if (key != null && Moves.TryGetValue(key, out var list)) return list;
            return Moves.TryGetValue("generic", out var g) ? g : new List<string>();
        }
    }

    public static class QuipPicker
    {
        private static readonly Queue<string> Recent = new Queue<string>();
        private const int RecentMemory = 10;

        public static Decision Decide(QuipBook book, DeathEvent ev, int count, PickerSettings s,
            float secondsSinceLastComment, bool categoryEnabled, Random rng)
        {
            if (!categoryEnabled) return Quiet("category turned off");

            bool isFirst = count == 1;
            bool isMilestone = s.Milestones.Contains(count);
            bool forced = (isFirst && s.AlwaysCommentOnFirst) || isMilestone;

            if (!forced)
            {
                if (secondsSinceLastComment < s.CooldownSeconds) return Quiet("on cooldown");
                if (rng.NextDouble() >= s.ChanceToComment) return Quiet("chose to stay home");
            }

            var whens = isFirst ? new[] { When.First, When.Any }
                : isMilestone ? new[] { When.Milestone, When.Repeat, When.Any }
                : new[] { When.Repeat, When.Any };

            var line = Find(book, ev.Chain, whens, rng);
            if (line == null) return Quiet("no matching lines in quips file");

            string why = isMilestone ? "milestone" : isFirst ? "first time" : "repeat";
            return new Decision { Line = line.Text, IsFirst = isFirst, IsMilestone = isMilestone, Reason = $"Fuling heckles ({why})", DeathChain = ev.Chain };
        }

        /// <summary>Walks from the most specific event key to "generic"; within each key, prefers the most specific 'when'.</summary>
        private static QuipLine Find(QuipBook book, List<string> chain, When[] whens, Random rng)
        {
            foreach (var key in chain)
                foreach (var w in whens)
                {
                    var candidates = book.Lines.Where(q => q.Event == key && q.When == w).ToList();
                    if (candidates.Count > 0) return PickAvoidingRepeats(candidates, rng);
                }
            return null;
        }

        private static QuipLine PickAvoidingRepeats(List<QuipLine> candidates, Random rng)
        {
            var fresh = candidates.Where(q => !Recent.Contains(q.Text)).ToList();
            var pool = fresh.Count > 0 ? fresh : candidates;
            var pick = pool[rng.Next(pool.Count)];
            Recent.Enqueue(pick.Text);
            while (Recent.Count > RecentMemory) Recent.Dequeue();
            return pick;
        }

        public static string PickForTest(QuipBook book, Random rng)
        {
            var pool = book.Lines.Where(q => !q.UsesKiller).ToList();
            return pool.Count == 0 ? null : pool[rng.Next(pool.Count)].Text;
        }

        private static Decision Quiet(string reason) => new Decision { Line = null, Reason = reason };
    }

    /// <summary>The Fuling's tallies: one small text file per character, "eventKey=count" per line.</summary>
    public class DeathMemory
    {
        private readonly string _folder;
        private readonly Dictionary<string, Dictionary<string, int>> _cache = new Dictionary<string, Dictionary<string, int>>();

        public DeathMemory(string folder) { _folder = folder; }

        public int Increment(string playerName, string key)
        {
            var tallies = Load(playerName);
            tallies.TryGetValue(key, out int n);
            tallies[key] = ++n;
            Save(playerName, tallies);
            return n;
        }

        public int Get(string playerName, string key)
        {
            Load(playerName).TryGetValue(key, out int n);
            return n;
        }

        public void Reset(string playerName)
        {
            _cache[playerName] = new Dictionary<string, int>();
            try { File.Delete(PathFor(playerName)); } catch { /* nothing to delete */ }
        }

        private Dictionary<string, int> Load(string playerName)
        {
            if (_cache.TryGetValue(playerName, out var t)) return t;
            t = new Dictionary<string, int>();
            try
            {
                string path = PathFor(playerName);
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        int eq = line.LastIndexOf('=');
                        if (eq > 0 && int.TryParse(line.Substring(eq + 1), out int v))
                            t[line.Substring(0, eq)] = v;
                    }
            }
            catch { /* a corrupt file just means a fresh start */ }
            _cache[playerName] = t;
            return t;
        }

        private void Save(string playerName, Dictionary<string, int> t)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllLines(PathFor(playerName), t.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { /* best effort */ }
        }

        private string PathFor(string playerName)
        {
            var safe = new string((playerName ?? "unknown").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(_folder, safe + ".txt");
        }
    }

    /// <summary>Parses a comma-separated list of prefab names.</summary>
    public static class NameList
    {
        public static List<string> Parse(string s)
        {
            return (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
    }
}

namespace FulingHeckler
{
    /// <summary>How a rigid helmet is adjusted on the Fuling's head. Offsets are in his body's directions.</summary>
    public class Fit
    {
        public float Up, Forward, Side, Tilt, Turn, Size = 1f;

        public static readonly Fit Identity = new Fit();

        public override string ToString()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join(", ", new[] { Up, Forward, Side, Tilt, Turn, Size }.Select(v => v.ToString("0.###", c)).ToArray());
        }

        public static bool TryParse(string s, out Fit fit)
        {
            fit = null;
            var parts = (s ?? "").Split(',');
            if (parts.Length != 6) return false;
            var v = new float[6];
            for (int i = 0; i < 6; i++)
                if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                    return false;
            fit = new Fit { Up = v[0], Forward = v[1], Side = v[2], Tilt = v[3], Turn = v[4], Size = v[5] <= 0 ? 1f : v[5] };
            return true;
        }
    }

    /// <summary>
    /// Saved helmet fits, stored in one config string:
    ///   "default: up, forward, side, tilt, turn, size | HelmetYule: ..."
    /// A helmet with no fit of its own uses "default".
    /// </summary>
    public class FitBook
    {
        public readonly Dictionary<string, Fit> Fits = new Dictionary<string, Fit>(StringComparer.OrdinalIgnoreCase);

        public static FitBook Parse(string s)
        {
            var book = new FitBook();
            foreach (var entry in (s ?? "").Split('|'))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0) continue;
                string name = entry.Substring(0, colon).Trim();
                if (name.Length > 0 && Fit.TryParse(entry.Substring(colon + 1), out Fit f)) book.Fits[name] = f;
            }
            return book;
        }

        public Fit For(string helmet)
        {
            if (helmet != null && Fits.TryGetValue(helmet, out Fit f)) return f;
            return Fits.TryGetValue("default", out Fit d) ? d : Fit.Identity;
        }

        public bool HasOwn(string helmet) => helmet != null && Fits.ContainsKey(helmet);

        public override string ToString()
        {
            // "default" first, then alphabetical, so the config stays readable.
            var keys = Fits.Keys.OrderBy(k => k.Equals("default", StringComparison.OrdinalIgnoreCase) ? "" : k, StringComparer.OrdinalIgnoreCase);
            return string.Join(" | ", keys.Select(k => $"{k}: {Fits[k]}").ToArray());
        }
    }
}

namespace FulingHeckler
{
    public enum LootTier { Junk, Meh, Nice, Treasure }

    public class LootEntry
    {
        public string Item;
        public double Weight;
        public int Min = 1, Max = 1;
        public LootTier Tier = LootTier.Junk;
    }

    /// <summary>
    /// The Fuling's loot table: "item | weight | amount | tier" per line.
    /// Higher weight = more common. Amount is "3" or "1-3". Tier is junk, meh, nice or treasure.
    /// </summary>
    public class LootTable
    {
        public readonly List<LootEntry> Entries = new List<LootEntry>();
        public readonly List<string> Warnings = new List<string>();

        public void Load(IEnumerable<string> rawLines)
        {
            Entries.Clear();
            Warnings.Clear();
            int n = 0;
            foreach (var raw in rawLines)
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                if (parts.Length < 2 || parts[0].Length == 0)
                {
                    Warnings.Add($"line {n}: expected 'item | weight | amount | tier', skipped.");
                    continue;
                }

                var e = new LootEntry { Item = parts[0] };
                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out e.Weight) || e.Weight <= 0)
                {
                    Warnings.Add($"line {n}: weight '{parts[1]}' must be a positive number, skipped.");
                    continue;
                }
                if (parts.Length >= 3 && parts[2].Length > 0)
                {
                    var range = parts[2].Split('-');
                    if (!int.TryParse(range[0].Trim(), out e.Min) || e.Min < 1)
                    {
                        Warnings.Add($"line {n}: amount '{parts[2]}' should look like 2 or 1-3, skipped.");
                        continue;
                    }
                    e.Max = e.Min;
                    if (range.Length > 1 && (!int.TryParse(range[1].Trim(), out e.Max) || e.Max < e.Min))
                    {
                        Warnings.Add($"line {n}: amount '{parts[2]}' should look like 2 or 1-3, skipped.");
                        continue;
                    }
                }
                if (parts.Length >= 4 && parts[3].Length > 0 && !Enum.TryParse(parts[3], true, out e.Tier))
                {
                    Warnings.Add($"line {n}: tier '{parts[3]}' should be junk, meh, nice or treasure; using junk.");
                    e.Tier = LootTier.Junk;
                }
                Entries.Add(e);
            }
        }

        /// <summary>Weighted random pick. Returns null if the table is empty.</summary>
        public LootEntry Pick(Random rng)
        {
            double total = Entries.Sum(e => e.Weight);
            if (total <= 0) return null;
            double roll = rng.NextDouble() * total;
            foreach (var e in Entries)
            {
                roll -= e.Weight;
                if (roll < 0) return e;
            }
            return Entries[Entries.Count - 1];
        }

        public static string ReadDefault()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("FulingHeckler.DefaultLoot.txt"))
            {
                if (s == null) return "Pukeberries | 1 | 1-2 | junk\n";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
