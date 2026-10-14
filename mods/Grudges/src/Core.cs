// Pure logic: no Unity or Valheim types in this file, so it can be tested outside the game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Grudges
{
    public enum Origin { Kill, Witness }
    public enum NemesisState { Active, Retired, Recovering }
    public enum Intensity { Subtle, Standard, Mordor, Custom }

    /// <summary>What a nemesis remembers about one player.</summary>
    public class Grudge
    {
        public long PlayerId;
        public string PlayerName = "";
        public int Kills;
        /// <summary>Embarrassing deaths it watched, as cause keys ("Tree", "Fall"...), oldest first.</summary>
        public List<string> Witnessed = new List<string>();
    }

    /// <summary>A mark left by a fight it barely survived.</summary>
    public class Scar
    {
        public string Type = "";      // damage type, e.g. "Fire"
        public string Effect = "";    // "Resist" (it toughened up) or "Fear" (it's afraid of it now)
        public string By = "";        // the player who did it
    }

    /// <summary>One nemesis. The roster entry is the truth; the creature in the world just wears it.</summary>
    public class Nemesis
    {
        public string Id = "";
        public string Species = "";       // prefab name, e.g. "Greydwarf"
        public string SpeciesName = "";   // display name, e.g. "Greydwarf"
        public string Biome = "";
        public string Name = "";          // e.g. "Mossjaw"
        public string Epithet = "";       // e.g. "the Rot-Tongued"
        public Origin Origin;
        public string Cause = "";         // how it earned its rank: "Kill" or a mundane cause key
        public int Level = 2;             // Valheim level: stars + 1
        public int BaseLevel = 1;         // the level it had before promotion
        public string Strength = "";      // damage type it resists ("" = none yet)
        public string Weakness = "";      // damage type it is weak to
        public List<string> Traits = new List<string>();
        public string Zdo = "";           // the body's network id, base64 (a hint; the tag on the body is the real link)
        public NemesisState State = NemesisState.Active;
        public string RetiredReason = "";
        public long CreatedTicks;
        public List<Grudge> Grudges = new List<Grudge>();
        public int CheatDeaths;
        public List<Scar> Scars = new List<Scar>();
        public double ReturnAt;           // world time (seconds) when a recovering nemesis may come back
        public long ScarredById;          // the player it comes back for
        public string ScarredByName = "";
        public bool Champion;             // the strongest in its biome
        public int BodyGen;               // bumped each time it gets a new body; older bodies stop being it
        public string StolenHelmet = "";  // a copy of the helmet worn by a player it killed (from an approved list)
        public string Personality = "";   // e.g. "Braggart" (see the personalities file)
        public string Palette = "";       // its colouring, e.g. "rust" (see 'Palettes' in config)
        public float SizeRoll = -1f;      // 0..1 within the size range (-1 = not rolled yet)

        /// <summary>The level its body has: a Champion gets one extra.</summary>
        public int EffectiveLevel => Level + (Champion ? 1 : 0);

        /// <summary>"Mossjaw the Rot-Tongued", "Barkeye of the Hungry Roots", "Knotback, Who Saw the Tree Fall".</summary>
        public string FullName =>
            string.IsNullOrEmpty(Epithet) ? Name
            : (Epithet.StartsWith("the ") || Epithet.StartsWith("of ")) ? $"{Name} {Epithet}"
            : $"{Name}, {Epithet}";
        public int Stars => Math.Max(0, Level - 1);
        public bool IsActive => State == NemesisState.Active;
        /// <summary>Still in the roster (out in the world, or off licking its wounds).</summary>
        public bool IsLiving => State != NemesisState.Retired;

        public Grudge GrudgeFor(long playerId, string playerName, bool create)
        {
            var g = Grudges.FirstOrDefault(x => x.PlayerId == playerId && playerId != 0)
                    ?? Grudges.FirstOrDefault(x => string.Equals(x.PlayerName, playerName, StringComparison.OrdinalIgnoreCase));
            if (g == null && create)
            {
                g = new Grudge { PlayerId = playerId, PlayerName = playerName ?? "" };
                Grudges.Add(g);
            }
            if (g != null && !string.IsNullOrEmpty(playerName)) g.PlayerName = playerName;
            return g;
        }

        /// <summary>Rough fighting strength, used when two creatures fight over a slot.</summary>
        public int Power => Level * 2 + Grudges.Sum(g => g.Kills) + (string.IsNullOrEmpty(Strength) ? 0 : 1);
    }

    /// <summary>A creature in the world, as described by the player's game that saw it.</summary>
    public class CreatureRef
    {
        public string Zdo = "";
        public string Species = "";
        public string SpeciesName = "";
        public string NemesisId = "";     // "" if it isn't one
        public int Level = 1;
        public string Biome = "";
        public float Distance;
    }

    /// <summary>A player's death, as reported to the server.</summary>
    public class DeathReport
    {
        public long VictimId;
        public string VictimName = "";
        public string Cause = "";          // "Kill" when an eligible creature did it, else a mundane cause key
        public string CauseDetail = "";    // e.g. "Boar" when a mundane creature did it
        public CreatureRef Killer;         // set only when an eligible creature killed them
        public List<CreatureRef> Witnesses = new List<CreatureRef>();
        public bool Forced;                // test buttons: skip chance and cooldown
        public string VictimHelmet = "";   // item name of the helmet the victim wore, if any
    }

    /// <summary>What the server decided, for the game side to act on.</summary>
    public class DeathResult
    {
        public Nemesis Promoted;           // a new nemesis
        public CreatureRef PromotedBody;   // the creature that becomes it
        public List<Nemesis> Changed = new List<Nemesis>();
        public List<string> Messages = new List<string>(); // shown to the victim
        public bool RosterChanged;

        // The on-screen moment after your death: which nemesis to show, and what it says.
        public string RevealId;            // null = nothing to show
        public string RevealZdo = "";      // the body's network id, to find it before it's tagged
        public string RevealKind = "";     // "promoted", "stronger" or "saw"
        public string RevealLine = "";     // its speech bubble
    }

    public static class Causes
    {
        /// <summary>Turns Valheim's hit type (as a string) into the mod's cause key.</summary>
        public static string FromHitType(string hitType, bool killerIsPlayer, bool killerIsSelf, bool hasCreatureKiller)
        {
            switch (hitType ?? "")
            {
                case "Tree": return "Tree";
                case "Fall": return "Fall";
                case "Drowning": return "Drowning";
                case "Burning":
                case "CinderFire": return "Burning";
                case "Freezing": return "Freezing";
                case "Poisoned": return "Poisoned";
                case "Smoke": return "Smoke";
                case "Water":
                case "AshlandsOcean": return "Boiled";
                case "Self": return "Self";
                case "Cart": return "Cart";
                case "Boat": return "Boat";
            }
            if (killerIsSelf) return "Self";
            if (killerIsPlayer) return "Friend";
            if (hasCreatureKiller) return "Creature";
            return "Other";
        }

        /// <summary>How a witnessed death is remembered: "Tree", or "Creature/Boar" when a creature did it.</summary>
        public static string Entry(string cause, string detail) =>
            string.IsNullOrEmpty(detail) ? cause : cause + "/" + detail.Replace("+", " ").Replace("/", " ");

        public static void Split(string entry, out string cause, out string detail)
        {
            int i = (entry ?? "").IndexOf('/');
            cause = i < 0 ? (entry ?? "") : entry.Substring(0, i);
            detail = i < 0 ? "" : entry.Substring(i + 1);
        }

        /// <summary>A short phrase for a cause, used in lines like "beaten by {cause}".</summary>
        public static string Phrase(string cause, string detail)
        {
            switch (cause)
            {
                case "Tree": return "a tree";
                case "Fall": return "the ground";
                case "Drowning": return "the water";
                case "Burning": return "fire";
                case "Freezing": return "the cold";
                case "Poisoned": return "poison";
                case "Smoke": return "smoke";
                case "Boiled": return "hot water";
                case "Self": return "themselves";
                case "Friend": return string.IsNullOrEmpty(detail) ? "a friend" : detail;
                case "Cart": return "a cart";
                case "Boat": return "a boat";
                case "Creature": return string.IsNullOrEmpty(detail) ? "a beast" : "a " + detail.ToLowerInvariant();
                default: return "bad luck";
            }
        }
    }

    public static class Traits
    {
        public const string Swift = "Swift", Brutal = "Brutal", Regenerating = "Regenerating", Coward = "Coward", Relentless = "Relentless";
        public static readonly string[] All = { Swift, Brutal, Regenerating, Coward, Relentless };

        public static string Describe(string trait)
        {
            switch (trait)
            {
                case Swift: return "moves faster";
                case Brutal: return "hits harder";
                case Regenerating: return "heals over time";
                case Coward: return "flees when badly hurt";
                case Relentless: return "sees further and never stops chasing";
                default: return trait;
            }
        }

        public static readonly string[] DamageTypes = { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit" };
    }

    /// <summary>All the dials. Built from config on the server.</summary>
    public class RuleSettings
    {
        public HashSet<string> Species = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Greyling" };
        public float KillChance = 0.35f;
        public float WitnessChance = 0.1f;
        public float CooldownMinutes = 20f;
        public int MaxPerBiome = 3;
        public Dictionary<string, int> MaxLevelByBiome = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int DefaultMaxLevel = 4;
        /// <summary>Helmets a nemesis may take (a copy of) from a player it kills. Only rigid ones that fit.</summary>
        public HashSet<string> StealableHelmets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static RuleSettings ForIntensity(Intensity i, RuleSettings custom)
        {
            var s = custom.Clone();
            switch (i)
            {
                case Intensity.Subtle: s.KillChance = 0.35f; s.WitnessChance = 0.10f; s.CooldownMinutes = 20f; s.MaxPerBiome = 3; break;
                case Intensity.Standard: s.KillChance = 0.60f; s.WitnessChance = 0.25f; s.CooldownMinutes = 10f; s.MaxPerBiome = 5; break;
                case Intensity.Mordor: s.KillChance = 0.90f; s.WitnessChance = 0.50f; s.CooldownMinutes = 2f; s.MaxPerBiome = 8; break;
            }
            return s;
        }

        public RuleSettings Clone() => new RuleSettings
        {
            Species = new HashSet<string>(Species, StringComparer.OrdinalIgnoreCase),
            KillChance = KillChance, WitnessChance = WitnessChance, CooldownMinutes = CooldownMinutes, MaxPerBiome = MaxPerBiome,
            MaxLevelByBiome = new Dictionary<string, int>(MaxLevelByBiome, StringComparer.OrdinalIgnoreCase),
            DefaultMaxLevel = DefaultMaxLevel,
            StealableHelmets = new HashSet<string>(StealableHelmets, StringComparer.OrdinalIgnoreCase)
        };

        public int MaxLevel(string biome) => MaxLevelByBiome.TryGetValue(biome ?? "", out int v) ? v : DefaultMaxLevel;

        /// <summary>Parses "Meadows=3, BlackForest=4" into the table.</summary>
        public static Dictionary<string, int> ParseLevels(string text)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (text ?? "").Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out int n)) d[kv[0].Trim()] = Math.Max(1, n);
            }
            return d;
        }

        /// <summary>Parses "a=b, c=d" into a lookup.</summary>
        public static Dictionary<string, string> ParseMap(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (text ?? "").Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0].Trim().Length > 0 && kv[1].Trim().Length > 0) d[kv[0].Trim()] = kv[1].Trim();
            }
            return d;
        }

        public static HashSet<string> ParseList(string text) =>
            new HashSet<string>((text ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every nemesis in one world, saved as a text file on the server.</summary>
    public class Roster
    {
        public List<Nemesis> All = new List<Nemesis>();

        public IEnumerable<Nemesis> Active => All.Where(n => n.IsActive);
        public IEnumerable<Nemesis> Living => All.Where(n => n.IsLiving);
        /// <summary>Nemeses holding a slot in a biome (recovering ones keep theirs).</summary>
        public IEnumerable<Nemesis> ActiveIn(string biome) => Living.Where(n => string.Equals(n.Biome, biome, StringComparison.OrdinalIgnoreCase));
        public Nemesis FindLiving(string id) { var n = Find(id); return n != null && n.IsLiving ? n : null; }
        public Nemesis Find(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(n => n.Id == id);
        public Nemesis FindActive(string id) { var n = Find(id); return n != null && n.IsActive ? n : null; }

        // ---- Save format: one nemesis per line, "key=value;key=value", values URL-escaped. ----

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Grudges roster. One nemesis per line. Safe to delete (wipes every nemesis in this world).");
            foreach (var n in All) sb.AppendLine(SerializeOne(n));
            return sb.ToString();
        }

        public static string SerializeOne(Nemesis n)
        {
            var f = new List<KeyValuePair<string, string>>
            {
                KV("id", n.Id), KV("species", n.Species), KV("speciesName", n.SpeciesName), KV("biome", n.Biome),
                KV("name", n.Name), KV("epithet", n.Epithet), KV("origin", n.Origin.ToString()), KV("cause", n.Cause),
                KV("level", n.Level.ToString(CultureInfo.InvariantCulture)), KV("baseLevel", n.BaseLevel.ToString(CultureInfo.InvariantCulture)),
                KV("strength", n.Strength), KV("weakness", n.Weakness), KV("traits", string.Join(",", n.Traits)),
                KV("zdo", n.Zdo), KV("state", n.State.ToString()), KV("retired", n.RetiredReason),
                KV("created", n.CreatedTicks.ToString(CultureInfo.InvariantCulture)),
                KV("grudges", string.Join(",", n.Grudges.Select(g =>
                    Esc(g.PlayerId.ToString(CultureInfo.InvariantCulture)) + ":" + Esc(g.PlayerName) + ":" +
                    g.Kills.ToString(CultureInfo.InvariantCulture) + ":" + Esc(string.Join("+", g.Witnessed))))),
                KV("cheats", n.CheatDeaths.ToString(CultureInfo.InvariantCulture)),
                KV("scars", string.Join(",", n.Scars.Select(x => Esc(x.Type) + ":" + Esc(x.Effect) + ":" + Esc(x.By)))),
                KV("returnAt", n.ReturnAt.ToString("R", CultureInfo.InvariantCulture)),
                KV("scarredBy", n.ScarredById.ToString(CultureInfo.InvariantCulture)),
                KV("scarredByName", n.ScarredByName),
                KV("champion", n.Champion ? "1" : "0"),
                KV("gen", n.BodyGen.ToString(CultureInfo.InvariantCulture)),
                KV("helmet", n.StolenHelmet),
                KV("persona", n.Personality),
                KV("palette", n.Palette),
                KV("size", n.SizeRoll.ToString("0.###", CultureInfo.InvariantCulture))
            };
            return string.Join(";", f.Select(kv => kv.Key + "=" + Esc(kv.Value)));
        }

        public static Roster Parse(string text, List<string> warnings = null)
        {
            var r = new Roster();
            int lineNo = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                try
                {
                    var n = ParseOne(line);
                    if (n != null && n.Id.Length > 0) r.All.Add(n);
                }
                catch (Exception e) { warnings?.Add($"roster line {lineNo}: {e.Message}, skipped."); }
            }
            return r;
        }

        public static Nemesis ParseOne(string line)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in line.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                d[part.Substring(0, eq)] = Unesc(part.Substring(eq + 1));
            }
            string G(string k) => d.TryGetValue(k, out var v) ? v : "";
            int I(string k, int def) => int.TryParse(G(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;

            var n = new Nemesis
            {
                Id = G("id"), Species = G("species"), SpeciesName = G("speciesName"), Biome = G("biome"),
                Name = G("name"), Epithet = G("epithet"), Cause = G("cause"),
                Level = I("level", 2), BaseLevel = I("baseLevel", 1),
                Strength = G("strength"), Weakness = G("weakness"),
                Traits = G("traits").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
                Zdo = G("zdo"), RetiredReason = G("retired")
            };
            if (Enum.TryParse(G("origin"), true, out Origin o)) n.Origin = o;
            if (Enum.TryParse(G("state"), true, out NemesisState s)) n.State = s;
            long.TryParse(G("created"), NumberStyles.Integer, CultureInfo.InvariantCulture, out n.CreatedTicks);
            foreach (var gs in G("grudges").Split(','))
            {
                if (gs.Length == 0) continue;
                var p = gs.Split(':');
                if (p.Length < 3) continue;
                var g = new Grudge { PlayerName = Unesc(p[1]) };
                long.TryParse(Unesc(p[0]), NumberStyles.Integer, CultureInfo.InvariantCulture, out g.PlayerId);
                int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out g.Kills);
                if (p.Length > 3) g.Witnessed = Unesc(p[3]).Split('+').Where(x => x.Length > 0).ToList();
                n.Grudges.Add(g);
            }
            n.CheatDeaths = I("cheats", 0);
            foreach (var sc in G("scars").Split(','))
            {
                var p = sc.Split(':');
                if (p.Length >= 2) n.Scars.Add(new Scar { Type = Unesc(p[0]), Effect = Unesc(p[1]), By = p.Length > 2 ? Unesc(p[2]) : "" });
            }
            double.TryParse(G("returnAt"), NumberStyles.Float, CultureInfo.InvariantCulture, out n.ReturnAt);
            long.TryParse(G("scarredBy"), NumberStyles.Integer, CultureInfo.InvariantCulture, out n.ScarredById);
            n.ScarredByName = G("scarredByName");
            n.Champion = G("champion") == "1";
            n.BodyGen = I("gen", 0);
            n.StolenHelmet = G("helmet");
            n.Personality = G("persona");
            n.Palette = G("palette");
            n.SizeRoll = float.TryParse(G("size"), NumberStyles.Float, CultureInfo.InvariantCulture, out float sz) ? sz : -1f;
            return n;
        }

        private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v ?? "");
        private static string Esc(string s) => Uri.EscapeDataString(s ?? "");
        private static string Unesc(string s) => Uri.UnescapeDataString(s ?? "");
    }

    /// <summary>
    /// Every line the mod says, read from an editable text file with [sections].
    /// A section can be specific ("[epithet-witness:Tree]") with a general fallback ("[epithet-witness]").
    /// </summary>
    public class LineBook
    {
        private readonly Dictionary<string, List<string>> _sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new List<string>();

        public int SectionCount => _sections.Count;

        public void Load(IEnumerable<string> rawLines)
        {
            _sections.Clear();
            Warnings.Clear();
            List<string> current = null;
            int n = 0;
            foreach (var raw in rawLines)
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string key = line.Substring(1, line.Length - 2).Trim();
                    if (!_sections.TryGetValue(key, out current)) { current = new List<string>(); _sections[key] = current; }
                    continue;
                }
                if (current == null) { Warnings.Add($"line {n}: text before any [section], skipped."); continue; }
                current.Add(line);
            }
        }

        public bool Has(string section) => _sections.TryGetValue(section, out var l) && l.Count > 0;

        public IEnumerable<string> Sections => _sections.Keys;

        /// <summary>
        /// The text of every section in <paramref name="defaults"/> that <paramref name="userText"/> doesn't have,
        /// ready to append to the user's file (so new versions add their new lines without touching edits).
        /// </summary>
        public static string MissingSections(string userText, string defaults)
        {
            var user = new LineBook(); user.Load((userText ?? "").Split('\n'));
            var have = new HashSet<string>(user.Sections, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            bool copying = false;
            foreach (var raw in (defaults ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    copying = !have.Contains(t.Substring(1, t.Length - 2).Trim());
                    if (copying) sb.AppendLine().AppendLine(t);
                    continue;
                }
                if (copying && t.Length > 0 && !t.StartsWith("#")) sb.AppendLine(t);
            }
            return sb.ToString();
        }

        /// <summary>Picks a line from the first of the sections that has any.</summary>
        public string Pick(Random rng, params string[] sectionsInOrder)
        {
            foreach (var s in sectionsInOrder)
                if (s != null && _sections.TryGetValue(s, out var list) && list.Count > 0)
                    return list[rng.Next(list.Count)];
            return null;
        }

        public static string ReadDefault()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("Grudges.DefaultLines.txt"))
            {
                if (s == null) return "[prefix]\nGrim\n[suffix]\njaw\n[epithet-kill]\nthe Grudge-Holder\n[epithet-witness]\nthe Laughing\n";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }

    public static class Fill
    {
        /// <summary>Fills {player} {name} {short} {species} {kills} {times} {cause} {other} in a line.</summary>
        public static string Line(string text, Nemesis n, string player, int kills = 0, string causePhrase = null, string other = null)
        {
            if (text == null) return "";
            return text
                .Replace("{player}", string.IsNullOrEmpty(player) ? "tall-one" : player)
                .Replace("{name}", n?.FullName ?? "")
                .Replace("{short}", n?.Name ?? "")
                .Replace("{species}", n == null ? "" : (string.IsNullOrEmpty(n.SpeciesName) ? n.Species : n.SpeciesName))
                .Replace("{kills}", kills.ToString(CultureInfo.InvariantCulture))
                .Replace("{times}", Times(kills))
                .Replace("{cause}", causePhrase ?? "")
                .Replace("{other}", other ?? "");
        }

        public static string Times(int n)
        {
            switch (n)
            {
                case 0: return "never";
                case 1: return "once";
                case 2: return "twice";
                default: return n.ToString(CultureInfo.InvariantCulture) + " times";
            }
        }
    }

    /// <summary>
    /// The server's rules for deaths: who gets promoted, who grows stronger, who loses a slot.
    /// Pure: the caller passes in the roster, the time and the dice.
    /// </summary>
    public class Rules
    {
        public RuleSettings Settings = new RuleSettings();
        public LineBook Lines = new LineBook();
        public PersonalityBook Personas = new PersonalityBook();
        public List<string> PaletteNames = new List<string>();
        /// <summary>The palettes a creature can get (the shared ones plus its own [palettes:Creature]); null = PaletteNames for all.</summary>
        public Func<string, List<string>> PalettesFor;

        /// <summary>
        /// Gives a nemesis its personality, colouring and size roll if it doesn't have them yet (new ones, and ones
        /// from before personalities existed). The colouring leans toward its personality's favourite palettes.
        /// </summary>
        public bool AssignLook(Nemesis n, Random rng)
        {
            bool changed = false;
            if (string.IsNullOrEmpty(n.Personality)) { n.Personality = Personas.Roll(rng, n.Species) ?? ""; changed = true; }
            var pool = PalettesFor?.Invoke(n.Species) ?? PaletteNames;
            if (string.IsNullOrEmpty(n.Palette) && pool.Count > 0)
            {
                var liked = RuleSettings.ParseList(Personas.Get(n.Personality, n.Species, "palettes"))
                                        .Where(p => pool.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                var from = liked.Count > 0 && rng.NextDouble() < 0.6 ? liked : pool;
                n.Palette = from[rng.Next(from.Count)];
                changed = true;
            }
            if (n.SizeRoll < 0f) { n.SizeRoll = (float)rng.NextDouble(); changed = true; }
            return changed;
        }

        /// <summary>
        /// Picks a line, preferring this nemesis's personality version of each section ("taunt-killer/Braggart")
        /// most of the time, then the normal ones.
        /// </summary>
        public string PickFor(Random rng, Nemesis n, params string[] sections)
        {
            string species = n?.Species;
            if (n != null && !string.IsNullOrEmpty(n.Personality) && rng.NextDouble() < 0.75)
            {
                var personal = sections.Where(x => x != null).Select(x => (x.Contains(":") ? x.Substring(0, x.IndexOf(':')) : x) + "/" + n.Personality)
                                       .Distinct().ToArray();
                string line = Lines.Pick(rng, Kinds.Expand(species, personal));
                if (line != null) return line;
            }
            return Lines.Pick(rng, Kinds.Expand(species, sections));
        }
        /// <summary>Victim player id (or name hash) → when a nemesis was last made from their death (UTC ticks).</summary>
        private readonly Dictionary<string, long> _lastPromotion = new Dictionary<string, long>();

        public DeathResult OnDeath(Roster roster, DeathReport d, DateTime nowUtc, Random rng)
        {
            var res = new DeathResult();
            string victimKey = d.VictimId != 0 ? d.VictimId.ToString(CultureInfo.InvariantCulture) : (d.VictimName ?? "").ToLowerInvariant();

            // 1. Killed by a nemesis: it remembers and grows stronger.
            if (d.Killer != null)
            {
                var existing = roster.FindActive(d.Killer.NemesisId);
                if (existing != null)
                {
                    var g = existing.GrudgeFor(d.VictimId, d.VictimName, true);
                    g.Kills++;
                    int cap = Settings.MaxLevel(existing.Biome);
                    bool grew = existing.Level < cap;
                    if (grew) existing.Level++;
                    bool newStrength = false;
                    if (string.IsNullOrEmpty(existing.Strength))
                    {
                        existing.Strength = PickDamageType(rng, existing.Weakness);
                        newStrength = true;
                    }
                    Steal(existing, d);
                    res.Changed.Add(existing);
                    res.RosterChanged = true;
                    res.Messages.Add(Fill.Line(Lines.Pick(rng, newStrength ? "msg-first-blood" : (grew ? "msg-stronger" : "msg-kill-again"), "msg-stronger")
                                               ?? "{name} grows stronger.", existing, d.VictimName, g.Kills));
                    SetReveal(res, existing, d.Killer.Zdo, "stronger",
                              Fill.Line(PickFor(rng, existing, "reveal-stronger", "taunt-killer") ?? "Again!", existing, d.VictimName, g.Kills));
                    return res;
                }
            }

            // 2. Killed by an ordinary eligible creature: maybe it rises.
            if (d.Killer != null && Settings.Species.Contains(d.Killer.Species))
            {
                if (!d.Forced && (OnCooldown(victimKey, nowUtc) || rng.NextDouble() >= Settings.KillChance)) return res;
                TryPromote(roster, d, d.Killer, Origin.Kill, "Kill", nowUtc, rng, res, victimKey);
                return res;
            }

            // 3. A mundane death: witnesses.
            var witnesses = d.Witnesses.Where(w => Settings.Species.Contains(w.Species)).OrderBy(w => w.Distance).ToList();
            if (witnesses.Count == 0) return res;

            // A nemesis that saw it remembers, whatever the dice say.
            foreach (var w in witnesses)
            {
                var n = roster.FindActive(w.NemesisId);
                if (n == null) continue;
                var g = n.GrudgeFor(d.VictimId, d.VictimName, true);
                g.Witnessed.Add(Causes.Entry(d.Cause, d.CauseDetail));
                if (g.Witnessed.Count > 20) g.Witnessed.RemoveAt(0);
                res.Changed.Add(n);
                res.RosterChanged = true;
                res.Messages.Add(Fill.Line(Lines.Pick(rng, "msg-saw-again") ?? "{name} saw that.", n, d.VictimName,
                                           g.Kills, Causes.Phrase(d.Cause, d.CauseDetail)));
                SetReveal(res, n, w.Zdo, "saw",
                          Fill.Line(PickFor(rng, n, "reveal-saw:" + d.Cause, "reveal-saw", "taunt-witness:" + d.Cause, "taunt-witness") ?? "Hehehe!",
                                    n, d.VictimName, g.Kills, Causes.Phrase(d.Cause, d.CauseDetail)));
                return res; // one smug nemesis is enough
            }

            if (!d.Forced && (OnCooldown(victimKey, nowUtc) || rng.NextDouble() >= Settings.WitnessChance)) return res;
            var pick = witnesses.FirstOrDefault(w => string.IsNullOrEmpty(w.NemesisId));
            if (pick != null) TryPromote(roster, d, pick, Origin.Witness, d.Cause, nowUtc, rng, res, victimKey);
            return res;
        }

        public bool OnCooldown(string victimKey, DateTime nowUtc)
        {
            if (Settings.CooldownMinutes <= 0f) return false;
            return _lastPromotion.TryGetValue(victimKey, out long t)
                   && (nowUtc - new DateTime(t, DateTimeKind.Utc)).TotalMinutes < Settings.CooldownMinutes;
        }

        private void TryPromote(Roster roster, DeathReport d, CreatureRef body, Origin origin, string cause,
                                DateTime nowUtc, Random rng, DeathResult res, string victimKey)
        {
            var candidate = Create(roster, body, origin, cause, d, nowUtc, rng);
            string causePhrase = Causes.Phrase(d.Cause, d.CauseDetail);

            // A full biome: the newcomer has to take a slot from the weakest.
            var rivals = roster.ActiveIn(body.Biome).ToList();
            if (rivals.Count >= Math.Max(1, Settings.MaxPerBiome))
            {
                var weakest = rivals.OrderBy(r => r.Power).ThenBy(r => r.CreatedTicks).First();
                int challenger = candidate.Power + rng.Next(0, 5);
                int defender = weakest.Power + rng.Next(0, 3);
                if (challenger <= defender)
                {
                    res.Messages.Add(Fill.Line(Lines.Pick(rng, "msg-challenge-lost") ?? "A {species} tried to claim your death, but {other} crushed it.",
                                               candidate, d.VictimName, 0, causePhrase, weakest.FullName));
                    _lastPromotion[victimKey] = nowUtc.Ticks;
                    return;
                }
                weakest.State = NemesisState.Retired;
                weakest.RetiredReason = $"Overthrown by {candidate.FullName}";
                res.Changed.Add(weakest);
                res.Messages.Add(Fill.Line(Lines.Pick(rng, "msg-challenge-won") ?? "{name} overthrew {other} to claim your death.",
                                           candidate, d.VictimName, 0, causePhrase, weakest.FullName));
            }

            if (origin == Origin.Kill) Steal(candidate, d);
            roster.All.Add(candidate);
            res.Promoted = candidate;
            res.PromotedBody = body;
            res.RosterChanged = true;
            _lastPromotion[victimKey] = nowUtc.Ticks;
            string section = origin == Origin.Kill ? "msg-promoted-kill" : "msg-promoted-witness";
            res.Messages.Add(Fill.Line(Lines.Pick(rng, section) ?? "{name} will remember you.", candidate, d.VictimName,
                                       origin == Origin.Kill ? 1 : 0, causePhrase));
            string line = origin == Origin.Kill
                ? PickFor(rng, candidate, "reveal-kill")
                : PickFor(rng, candidate, "reveal-witness:" + cause, "reveal-witness");
            SetReveal(res, candidate, body.Zdo, origin == Origin.Kill ? "promoted" : "promoted-witness",
                      Fill.Line(line ?? "I will remember you, {player}.", candidate, d.VictimName, origin == Origin.Kill ? 1 : 0, causePhrase));
        }

        /// <summary>Takes (a copy of) the victim's helmet, if it's one that fits.</summary>
        private void Steal(Nemesis n, DeathReport d)
        {
            if (!string.IsNullOrEmpty(d?.VictimHelmet) && Settings.StealableHelmets.Contains(d.VictimHelmet)) n.StolenHelmet = d.VictimHelmet;
        }

        private static void SetReveal(DeathResult res, Nemesis n, string zdo, string kind, string line)
        {
            res.RevealId = n.Id;
            res.RevealZdo = zdo ?? "";
            res.RevealKind = kind;
            res.RevealLine = line;
        }

        /// <summary>Builds a new nemesis for a creature (not yet added to the roster).</summary>
        public Nemesis Create(Roster roster, CreatureRef body, Origin origin, string cause, DeathReport d, DateTime nowUtc, Random rng)
        {
            int baseLevel = Math.Max(1, body.Level);
            var n = new Nemesis
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Species = body.Species,
                SpeciesName = string.IsNullOrEmpty(body.SpeciesName) ? body.Species : body.SpeciesName,
                Biome = body.Biome,
                Origin = origin,
                Cause = cause,
                BaseLevel = baseLevel,
                Level = Math.Min(Math.Max(baseLevel + 1, 2), Math.Max(Settings.MaxLevel(body.Biome), baseLevel)),
                Zdo = body.Zdo,
                CreatedTicks = nowUtc.Ticks
            };
            n.Name = MakeName(roster, body.Species, rng);
            n.Epithet = MakeEpithet(origin, cause, body.Species, rng);
            n.Weakness = PickDamageType(rng, null);
            // Witnesses earn a strength only once they actually beat someone.
            if (origin == Origin.Kill) n.Strength = PickDamageType(rng, n.Weakness);
            n.Traits.Add(Traits.All[rng.Next(Traits.All.Length)]);
            n.Personality = ""; n.Palette = ""; n.SizeRoll = -1f;
            AssignLook(n, rng);
            // A Coward personality tends to be a coward in a fight too.
            if (n.Personality == "Coward" && !n.Traits.Contains(Traits.Coward) && rng.NextDouble() < 0.6) n.Traits[0] = Traits.Coward;
            if (d != null)
            {
                var g = n.GrudgeFor(d.VictimId, d.VictimName, true);
                if (origin == Origin.Kill) g.Kills = 1;
                else g.Witnessed.Add(Causes.Entry(cause, d.CauseDetail));
            }
            return n;
        }

        public string MakeName(Roster roster, string species, Random rng)
        {
            var taken = new HashSet<string>(roster.Living.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            string name = "Grim";
            for (int i = 0; i < 20; i++)
            {
                string a = Lines.Pick(rng, Kinds.Expand(species, "prefix")) ?? "Grim";
                string b = Lines.Pick(rng, Kinds.Expand(species, "suffix")) ?? "jaw";
                name = a + b.ToLowerInvariant();
                if (!taken.Contains(name)) break;
            }
            return name;
        }

        public string MakeEpithet(Origin origin, string cause, string species, Random rng)
        {
            if (origin == Origin.Kill)
                return Lines.Pick(rng, Kinds.Expand(species, "epithet-kill")) ?? "the Grudge-Holder";
            return Lines.Pick(rng, Kinds.Expand(species, "epithet-witness:" + cause, "epithet-witness")) ?? "the Laughing";
        }

        public static string PickDamageType(Random rng, string notThis)
        {
            var options = Traits.DamageTypes.Where(t => !string.Equals(t, notThis, StringComparison.OrdinalIgnoreCase)).ToArray();
            return options[rng.Next(options.Length)];
        }

        // ---- Cheating death ----------------------------------------------------------

        /// <summary>
        /// Chance a nemesis survives a killing blow: the base chance, halved for every escape so far,
        /// halved again if the blow was its weakness, and better for Cowards. Never above 90%.
        /// </summary>
        public static float SurvivalChance(Nemesis n, string blowType, float baseChance, float decayPerEscape)
        {
            if (n == null || baseChance <= 0f) return 0f;
            double c = baseChance * Math.Pow(Math.Max(0f, decayPerEscape), n.CheatDeaths);
            if (!string.IsNullOrEmpty(blowType) && string.Equals(blowType, n.Weakness, StringComparison.OrdinalIgnoreCase)) c *= 0.5;
            if (n.Traits.Contains(Traits.Coward)) c *= 1.5;
            return (float)Math.Min(0.9, c);
        }

        /// <summary>
        /// It survived: it goes off to recover, scarred by what nearly killed it, and comes back for that player later.
        /// Returns the messages for the player who did it.
        /// </summary>
        public List<string> OnEscaped(Nemesis n, string blowType, long byId, string byName, double worldNow, double recoverySeconds, Random rng)
        {
            var msgs = new List<string>();
            if (n == null || !n.IsActive) return msgs;
            string type = Traits.DamageTypes.Contains(blowType) ? blowType : "Slash";
            // Fire can leave it terrified of flames instead of hardened against them.
            string effect = type == "Fire" && rng.NextDouble() < 0.5 ? "Fear" : "Resist";
            n.Scars.Add(new Scar { Type = type, Effect = effect, By = byName ?? "" });
            n.CheatDeaths++;
            n.State = NemesisState.Recovering;
            n.ReturnAt = worldNow + Math.Max(0, recoverySeconds);
            n.ScarredById = byId;
            n.ScarredByName = byName ?? "";
            n.Zdo = ""; // the old body is gone; it comes back in a new one
            string epithet = Lines.Pick(rng, "epithet-scar:" + type);
            if (!string.IsNullOrEmpty(epithet)) n.Epithet = epithet;
            if (!string.IsNullOrEmpty(byName))
            {
                n.GrudgeFor(byId, byName, true);
                msgs.Add(Fill.Line(Lines.Pick(rng, "msg-escaped") ?? "{name} escaped. It will be back for you.", n, byName, 0, DamagePhrase(type)));
            }
            return msgs;
        }

        /// <summary>A recovering nemesis is back in a new body.</summary>
        public void OnReturned(Nemesis n, string zdo)
        {
            if (n == null || n.State != NemesisState.Recovering) return;
            n.State = NemesisState.Active;
            n.Zdo = zdo ?? "";
        }

        /// <summary>The scar it carries from a damage type: "burns", "frostbite"...</summary>
        public static string DamagePhrase(string type)
        {
            switch (type)
            {
                case "Fire": return "fire";
                case "Frost": return "frost";
                case "Lightning": return "lightning";
                case "Poison": return "poison";
                case "Spirit": return "spirit";
                case "Blunt": return "your club";
                case "Pierce": return "your arrows";
                default: return "your blade";
            }
        }

        /// <summary>The story written on a fallen nemesis's trophy.</summary>
        public static string TrophyStory(Nemesis n, string killerName)
        {
            if (n == null) return "";
            var sb = new StringBuilder();
            sb.Append($"{(string.IsNullOrEmpty(n.SpeciesName) ? n.Species : n.SpeciesName)} nemesis of the {BiomeName(n.Biome)}.");
            foreach (var g in n.Grudges.Where(g => g.Kills > 0).OrderByDescending(g => g.Kills).Take(3))
                sb.Append($" Killed {g.PlayerName} {Fill.Times(g.Kills)}.");
            var seen = n.Grudges.Where(g => g.Kills == 0 && g.Witnessed.Count > 0).Take(2);
            foreach (var g in seen)
            {
                Causes.Split(g.Witnessed[g.Witnessed.Count - 1], out string cause, out string detail);
                sb.Append($" Watched {g.PlayerName} lose to {Causes.Phrase(cause, detail)}.");
            }
            if (n.CheatDeaths > 0) sb.Append($" Cheated death {Fill.Times(n.CheatDeaths)}.");
            foreach (var sc in n.Scars.Take(3))
            {
                string what = sc.Type == "Slash" ? "blades" : sc.Type == "Blunt" ? "clubs" : sc.Type == "Pierce" ? "arrows" : sc.Type.ToLowerInvariant();
                string who = string.IsNullOrEmpty(sc.By) ? "" : sc.Effect == "Fear" ? " from " + sc.By : " by " + sc.By;
                sb.Append(sc.Effect == "Fear" ? $" Learned to fear {what}{who}." : $" Hardened against {what}{who}.");
            }
            sb.Append(string.IsNullOrEmpty(killerName) ? " Found dead." : $" Slain by {killerName}.");
            return sb.ToString();
        }

        public static string BiomeName(string biome)
        {
            switch (biome)
            {
                case "BlackForest": return "Black Forest";
                case "AshLands": return "Ashlands";
                case "DeepNorth": return "Deep North";
                default: return string.IsNullOrEmpty(biome) ? "wilds" : biome;
            }
        }

        // ---- Props ------------------------------------------------------------------------

        /// <summary>
        /// What it carries on its body, from its history: arrows for each arrow scar (2, then a third), an axe in its
        /// back for a blade scar, ice on its shoulders for frost, an aura for its latest elemental scar, and a helmet
        /// (one it stole, or a dented bronze one it took to after a club to the head).
        /// </summary>
        public static List<string> PropsFor(Nemesis n)
        {
            var props = new List<string>();
            if (n == null) return props;
            int pierce = n.Scars.Count(x => x.Type == "Pierce");
            if (pierce >= 1) { props.Add("arrow1"); props.Add("arrow2"); }
            if (pierce >= 2) props.Add("arrow3");
            if (n.Scars.Any(x => x.Type == "Slash")) props.Add("axe");
            if (n.Scars.Any(x => x.Type == "Frost")) { props.Add("ice1"); props.Add("ice2"); }
            // The latest elemental scar gives it an aura (embers, frost mist, sickly fumes...).
            var element = n.Scars.LastOrDefault(x => IsElemental(x.Type));
            if (element != null) props.Add("aura:" + element.Type);
            if (!string.IsNullOrEmpty(n.Personality)) props.Add("persona:" + n.Personality);
            if (!string.IsNullOrEmpty(n.StolenHelmet)) props.Add("helmet:" + n.StolenHelmet);
            else if (n.Scars.Any(x => x.Type == "Blunt")) props.Add("helmet:HelmetBronze");
            return props;
        }

        public static bool IsElemental(string type) => type == "Fire" || type == "Frost" || type == "Poison" || type == "Lightning" || type == "Spirit";

        /// <summary>The scar its skin shows: the most recent one.</summary>
        public static string SkinScar(Nemesis n) => n?.Scars.LastOrDefault()?.Type ?? "";

        // ---- Travelling ---------------------------------------------------------------------

        /// <summary>How tough a biome is, roughly in the order you reach them.</summary>
        public static int BiomeTier(string biome)
        {
            switch (biome)
            {
                case "Meadows": return 1;
                case "BlackForest": return 2;
                case "Swamp": return 3;
                case "Ocean": return 3;
                case "Mountain": return 4;
                case "Plains": return 5;
                case "Mistlands": return 6;
                case "AshLands": return 7;
                case "DeepNorth": return 7;
                default: return 2;
            }
        }

        /// <summary>Extra levels a nemesis gets while hunting someone in a tougher biome than its own (none for easier ones).</summary>
        public static int TravelBonus(string home, string destination) => Math.Max(0, BiomeTier(destination) - BiomeTier(home));

        /// <summary>
        /// Nemeses that may follow this player into <paramref name="here"/>: out in the world, from a different
        /// biome, and with something personal against them (killed them, saw them die, or was scarred by them).
        /// </summary>
        public static List<Nemesis> Hunters(Roster r, string here, long playerId, string playerName, bool adjacentOnly)
        {
            return r.Active.Where(n =>
            {
                if (string.Equals(n.Biome, here, StringComparison.OrdinalIgnoreCase)) return false;
                if (adjacentOnly && Math.Abs(BiomeTier(n.Biome) - BiomeTier(here)) > 1) return false;
                var g = n.GrudgeFor(playerId, playerName, false);
                return (g != null && (g.Kills > 0 || g.Witnessed.Count > 0))
                       || n.Scars.Any(x => string.Equals(x.By, playerName, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

        // ---- Champions ------------------------------------------------------------------

        /// <summary>
        /// Each biome with at least <paramref name="minInBiome"/> living nemeses has one Champion: the strongest.
        /// A sitting Champion keeps the title until someone is strictly stronger. Returns the newly crowned.
        /// </summary>
        public static List<Nemesis> UpdateChampions(Roster r, int minInBiome = 2)
        {
            var crowned = new List<Nemesis>();
            foreach (var n in r.All.Where(x => !x.IsLiving && x.Champion)) n.Champion = false;
            foreach (var biome in r.Living.GroupBy(x => x.Biome ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var list = biome.ToList();
                if (list.Count < Math.Max(1, minInBiome)) { foreach (var n in list) n.Champion = false; continue; }
                var best = list.OrderByDescending(x => x.Power).ThenBy(x => x.CreatedTicks).First();
                var current = list.FirstOrDefault(x => x.Champion);
                var keep = current != null && current.Power >= best.Power ? current : best;
                foreach (var n in list) n.Champion = n == keep;
                if (keep != current) crowned.Add(keep);
            }
            return crowned;
        }

        // ---- Raids ----------------------------------------------------------------------

        /// <summary>
        /// Picks a nemesis to join a raid on a player's base, or null. Only ones of a species the raid brings, out in
        /// the world (not recovering), preferring ones from the base's own biome. The Champion comes rarely, or
        /// often if it has a grudge against the player being raided.
        /// </summary>
        public static Nemesis PickRaider(Roster r, ICollection<string> raidSpecies, string baseBiome, long playerId, string playerName,
                                         Random rng, float namedChance, float championChance, float grudgeChampionChance)
        {
            // A raid that brings any of a family brings the whole family's nemeses (a Fuling raid without shamans can still bring a Shaman nemesis).
            var families = new HashSet<string>(raidSpecies.Select(Kinds.FamilyOf), StringComparer.OrdinalIgnoreCase);
            var pool = r.Active.Where(n => raidSpecies.Contains(n.Species, StringComparer.OrdinalIgnoreCase) || families.Contains(Kinds.FamilyOf(n.Species))).ToList();
            if (pool.Count == 0) return null;
            var local = pool.Where(n => string.Equals(n.Biome, baseBiome, StringComparison.OrdinalIgnoreCase)).ToList();
            if (local.Count > 0) pool = local;

            var champ = pool.FirstOrDefault(n => n.Champion);
            if (champ != null)
            {
                var g = champ.GrudgeFor(playerId, playerName, false);
                bool grudge = (g != null && (g.Kills > 0 || g.Witnessed.Count > 0))
                              || champ.Scars.Any(x => string.Equals(x.By, playerName, StringComparison.OrdinalIgnoreCase));
                if (rng.NextDouble() < (grudge ? grudgeChampionChance : championChance)) return champ;
            }
            var named = pool.Where(n => !n.Champion).ToList();
            if (named.Count == 0 || rng.NextDouble() >= namedChance) return null;
            // Ones that know this player are likelier to come.
            var known = named.Where(n => n.GrudgeFor(playerId, playerName, false) != null).ToList();
            var from = known.Count > 0 && rng.NextDouble() < 0.7 ? known : named;
            return from[rng.Next(from.Count)];
        }

        /// <summary>The line a nemesis says when it spots a player.</summary>
        public string Taunt(Nemesis n, long playerId, string playerName, Random rng)
        {
            var g = n.GrudgeFor(playerId, playerName, false);
            string line;
            // Scarred by this player: that's what it wants to talk about, most of the time.
            if (n.Champion && rng.NextDouble() < 0.4)
            {
                line = Lines.Pick(rng, Kinds.Expand(n.Species, "taunt-champion"));
                if (line != null) return Fill.Line(line, n, playerName, g?.Kills ?? 0, BiomeName(n.Biome));
            }
            var scar = n.Scars.LastOrDefault(x => string.Equals(x.By, playerName, StringComparison.OrdinalIgnoreCase));
            if (scar != null && rng.NextDouble() < 0.6)
            {
                line = PickFor(rng, n, "taunt-scarred:" + scar.Type, "taunt-scarred");
                if (line != null) return Fill.Line(line, n, playerName, g?.Kills ?? 0, DamagePhrase(scar.Type));
            }
            if (g != null && g.Kills > 0)
                line = PickFor(rng, n, g.Kills >= 3 ? "taunt-killer-many" : null, "taunt-killer");
            else if (g != null && g.Witnessed.Count > 0)
            {
                Causes.Split(g.Witnessed[g.Witnessed.Count - 1], out string cause, out string detail);
                line = PickFor(rng, n, "taunt-witness:" + cause, "taunt-witness");
                return Fill.Line(line ?? "Hehe. I saw what happened.", n, playerName, g.Kills, Causes.Phrase(cause, detail));
            }
            else
                line = PickFor(rng, n, "taunt-stranger");
            return Fill.Line(line ?? "You. I will remember you.", n, playerName, g?.Kills ?? 0);
        }
    }
}

namespace Grudges
{
    /// <summary>How a prop sits: offsets in the creature's own directions (metres), tilt/turn/roll (degrees), size.</summary>
    public class Fit
    {
        public float Up, Forward, Side, Tilt, Turn, Roll, Size = 1f;

        public static readonly Fit Identity = new Fit();

        public override string ToString()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(", ", new[] { Up, Forward, Side, Tilt, Turn, Roll, Size }.Select(v => v.ToString("0.###", c)).ToArray());
        }

        public static bool TryParse(string s, out Fit fit)
        {
            fit = null;
            var parts = (s ?? "").Split(',');
            if (parts.Length != 6 && parts.Length != 7) return false;
            var v = new float[7];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            if (parts.Length == 6) { v[6] = v[5]; v[5] = 0f; } // old style without roll
            fit = new Fit { Up = v[0], Forward = v[1], Side = v[2], Tilt = v[3], Turn = v[4], Roll = v[5], Size = v[6] <= 0 ? 1f : v[6] };
            return true;
        }
    }

    /// <summary>
    /// Saved prop fits in one config string: "arrow1: up, forward, side, tilt, turn, roll, size | Greyling/arrow1: ...".
    /// Lookup goes species-specific first ("Greyling/arrow1"), then the prop ("arrow1"), then its family
    /// ("helmet:HelmetIron" falls back to "helmet").
    /// </summary>
    public class FitBook
    {
        public readonly Dictionary<string, Fit> Fits = new Dictionary<string, Fit>(StringComparer.OrdinalIgnoreCase);

        public static FitBook Parse(string s)
        {
            var book = new FitBook();
            foreach (var entry in (s ?? "").Split('|'))
            {
                int colon = entry.LastIndexOf(':');
                // keys can contain ':' (helmet:HelmetIron), so split on the colon before the numbers
                int firstComma = entry.IndexOf(',');
                if (firstComma > 0) colon = entry.LastIndexOf(':', firstComma);
                if (colon <= 0) continue;
                string name = entry.Substring(0, colon).Trim();
                if (name.Length > 0 && Fit.TryParse(entry.Substring(colon + 1), out Fit f)) book.Fits[name] = f;
            }
            return book;
        }

        public Fit For(string species, string prop)
        {
            // This creature's own fit, then its family's (a Fuling Berserker uses the Fulings' until it has one), then the default.
            Fit f;
            foreach (var k in Kinds.Chain(species)) if (Fits.TryGetValue(k + "/" + prop, out f)) return f;
            if (Fits.TryGetValue(prop, out f)) return f;
            int c = prop.IndexOf(':');
            if (c > 0)
            {
                string family = prop.Substring(0, c);
                foreach (var k in Kinds.Chain(species)) if (Fits.TryGetValue(k + "/" + family, out f)) return f;
                if (Fits.TryGetValue(family, out f)) return f;
            }
            return Fit.Identity;
        }

        public override string ToString() =>
            string.Join(" | ", Fits.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Select(k => $"{k}: {Fits[k]}").ToArray());

        /// <summary>Reads the fits file: one "Creature/prop = up, forward, side, tilt, turn, roll, size" per line.</summary>
        public static FitBook ParseFile(IEnumerable<string> lines)
        {
            var book = new FitBook();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (Fit.TryParse(line.Substring(eq + 1), out var f)) book.Fits[line.Substring(0, eq).Trim()] = f;
            }
            return book;
        }

        public string ToFile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Grudges: prop placements, written by the fitting tool (F1 -> 11 - Prop Fitting).");
            sb.AppendLine("# One per line:  Creature/prop = up, forward, side, tilt, turn, roll, size");
            sb.AppendLine("# A line without a creature (just 'prop = ...') is the fallback for every creature.");
            foreach (var k in Fits.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) sb.AppendLine($"{k} = {Fits[k]}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// A simple data file of [sections] holding "key = value" lines (the looks file). A section named
    /// [name:Creature] overrides [name] for that creature type.
    /// </summary>
    public class KeyValueBook
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        public int Version { get; private set; }

        public void Load(IEnumerable<string> rawLines)
        {
            _sections.Clear();
            Version++;
            Dictionary<string, string> cur = null;
            foreach (var raw in rawLines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (!_sections.TryGetValue(name, out cur)) { cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); _sections[name] = cur; }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (cur == null || eq <= 0) continue;
                cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        /// <summary>Everything in a section, with a creature's overrides on top.</summary>
        public Dictionary<string, string> Section(string name, string species = null)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_sections.TryGetValue(name, out var b)) foreach (var kv in b) d[kv.Key] = kv.Value;
            // Family first ([props:Fuling]), then the exact creature ([props:GoblinBrute]) on top.
            var chain = Kinds.Chain(species);
            chain.Reverse();
            foreach (var k in chain)
                if (_sections.TryGetValue(name + ":" + k, out var sp)) foreach (var kv in sp) d[kv.Key] = kv.Value;
            return d;
        }

        public string Get(string section, string key, string species = null) =>
            Section(section, species).TryGetValue(key, out var v) ? v : "";

        /// <summary>"h/s/v" as three numbers.</summary>
        public static bool TryHsv(string text, out float h, out float s, out float v)
        {
            h = s = v = 0f;
            var p = (text ?? "").Split('/');
            return p.Length == 3
                && float.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out h)
                && float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out s)
                && float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static string ReadDefaultLooks()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream("Grudges.DefaultLooks.txt"))
            {
                if (st == null) return "[palettes]\nmoss = 0.06/0.25/-0.15\n";
                using (var r = new StreamReader(st, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        public IEnumerable<string> SectionNames => _sections.Keys;

        public KeyValueBook With(string text) { Load((text ?? "").Split('\n')); return this; }

        /// <summary>Swaps the body of one [section] in a file's text (keeps its header and the comments above it).</summary>
        public static string ReplaceSection(string text, string section, string body)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n').ToList();
            int start = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0) return text.TrimEnd() + "\n\n[" + section + "]\n" + body.Trim() + "\n";
            int end = start + 1;
            while (end < lines.Count && !(lines[end].Trim().StartsWith("[") && lines[end].Trim().EndsWith("]"))) end++;
            // Keep the comment block that introduces the next section.
            int keep = end;
            while (keep > start + 1 && (lines[keep - 1].Trim().Length == 0 || lines[keep - 1].Trim().StartsWith("#"))) keep--;
            var tail = lines.Skip(keep).ToList();
            var result = lines.Take(start + 1).ToList();
            result.AddRange(body.Trim().Split('\n').Select(l => l.Trim()));
            if (tail.Count > 0 && tail[0].Trim().Length > 0) result.Add("");
            result.AddRange(tail);
            return string.Join("\n", result.ToArray());
        }

        /// <summary>Sets some keys inside one [section] of a file's text, leaving everything else alone.</summary>
        public static string SetKeys(string text, string section, IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0) return text;
            var lines = (text ?? "").Replace("\r", "").Split('\n').ToList();
            int start = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
                return (text ?? "").TrimEnd() + "\n\n[" + section + "]\n" +
                       string.Join("\n", values.Select(kv => $"{kv.Key} = {kv.Value}").ToArray()) + "\n";
            var left = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            int i = start + 1;
            for (; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                int eq = t.IndexOf('=');
                if (t.StartsWith("#") || eq <= 0) continue;
                string k = t.Substring(0, eq).Trim();
                if (left.TryGetValue(k, out var v)) { lines[i] = $"{k} = {v}"; left.Remove(k); }
            }
            foreach (var kv in left) lines.Insert(start + 1, $"{kv.Key} = {kv.Value}");
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Converts the old one-line config format ("a=b, c=d") into section lines.</summary>
        public static string FromOldMap(string oldValue) =>
            string.Join("\n", RuleSettings.ParseMap(oldValue).Select(kv => $"{kv.Key} = {kv.Value}").ToArray());
    }
}

namespace Grudges
{
    /// <summary>
    /// The personalities, read from an editable file of sections like:
    ///   [Braggart]
    ///   weight = 1
    ///   size = 0.3              (nudges its size: -1 smallest .. 1 biggest)
    ///   palettes = rust, blood  (colourings it leans toward)
    ///   prop = item:BoneFragments@Chest
    ///   emotes-new-killer = flex, challenge
    ///   animations = jump       (its own animations, for creatures that can't do player emotes)
    /// A section like [Braggart:Fuling] overrides any of those for one creature type.
    /// </summary>
    public class PersonalityBook
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Names = new List<string>();

        public void Load(IEnumerable<string> rawLines)
        {
            _sections.Clear();
            Names.Clear();
            Dictionary<string, string> cur = null;
            foreach (var raw in rawLines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (!_sections.TryGetValue(name, out cur))
                    {
                        cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _sections[name] = cur;
                        if (!name.Contains(":") && !name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase)) Names.Add(name);
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (cur == null || eq <= 0) continue;
                cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        public const string DefaultName = "Default";

        /// <summary>
        /// A value for a personality: its creature-specific section, then its own, then [Default:Creature], then
        /// [Default]. "" if none of them say.
        /// </summary>
        public string Get(string personality, string species, string key)
        {
            foreach (var name in new[] { personality, DefaultName })
            {
                if (string.IsNullOrEmpty(name)) continue;
                foreach (var k in Kinds.Chain(species))
                    if (_sections.TryGetValue(name + ":" + k, out var sp) && sp.TryGetValue(key, out var v1)) return v1;
                if (_sections.TryGetValue(name, out var b) && b.TryGetValue(key, out var v2)) return v2;
            }
            return "";
        }

        public float GetFloat(string personality, string species, string key, float def) =>
            float.TryParse(Get(personality, species, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

        /// <summary>A random personality, by weight. Null if there are none.</summary>
        /// <summary>
        /// A random personality, by weight. Weights can differ per creature ([Hoarder] weight = 0 with
        /// [Hoarder:Fuling] weight = 1 makes a Fuling-only personality).
        /// </summary>
        public string Roll(Random rng, string species = null)
        {
            if (Names.Count == 0) return null;
            var weights = Names.Select(n => Math.Max(0f, GetFloat(n, species, "weight", 1f))).ToList();
            if (weights.Sum() <= 0) return Names[rng.Next(Names.Count)];
            double total = weights.Sum(), r = rng.NextDouble() * total;
            for (int i = 0; i < Names.Count; i++) { r -= weights[i]; if (r <= 0) return Names[i]; }
            return Names[Names.Count - 1];
        }

        public static string ReadDefault()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream("Grudges.DefaultPersonalities.txt"))
            {
                if (st == null) return "[Braggart]\nweight = 1\n";
                using (var r = new StreamReader(st, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}

namespace Grudges
{
    /// <summary>
    /// Creature families: several creature types that share names, lines, looks and fits. A Fuling Berserker
    /// (GoblinBrute) is in the Fuling family, so it uses [prefix:Fuling], [props:Fuling], Fuling fits and so on,
    /// unless it has its own ([prefix:GoblinBrute]). Read from [families] in the looks file.
    /// </summary>
    public static class Kinds
    {
        public static Dictionary<string, string> Families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string FamilyOf(string species) =>
            !string.IsNullOrEmpty(species) && Families.TryGetValue(species, out var f) && !string.IsNullOrEmpty(f) ? f : species ?? "";

        /// <summary>The creature itself, then its family (if it has a different one).</summary>
        public static List<string> Chain(string species)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(species)) return list;
            list.Add(species);
            string fam = FamilyOf(species);
            if (!string.Equals(fam, species, StringComparison.OrdinalIgnoreCase)) list.Add(fam);
            return list;
        }

        /// <summary>Each section, tried as section:Creature, then section:Family, then plain.</summary>
        public static string[] Expand(string species, params string[] sections)
        {
            var chain = Chain(species);
            var res = new List<string>();
            foreach (var s in sections)
            {
                if (s == null) continue;
                foreach (var k in chain) res.Add(s + ":" + k);
                res.Add(s);
            }
            return res.Distinct().ToArray();
        }
    }
}
