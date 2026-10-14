using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace Grudges
{
    /// <summary>
    /// The server's side: owns the roster for the current world, decides what each death means, saves, and
    /// tells everyone. In single player or when hosting, your own game is the server.
    /// </summary>
    internal static class Server
    {
        internal static readonly Rules Rules = new Rules();
        private static readonly Random Rng = new Random();

        private static Roster _roster;
        private static string _worldKey;

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>The roster for the world being played (loads it the first time). Null when not the server.</summary>
        internal static Roster Roster
        {
            get
            {
                if (!IsServer) return null;
                string key = WorldKey();
                if (_roster == null || key != _worldKey)
                {
                    _worldKey = key;
                    _pendingSave = false;
                    _roster = Load(key);
                    if (_pendingSave) { _pendingSave = false; Save(); }
                }
                return _roster;
            }
        }

        internal static void Forget()
        {
            _roster = null;
            _worldKey = null;
            _keyFor = null;
            _cachedKey = null;
        }

        private static string FilePath(string key) => Path.Combine(Plugin.ConfigFolder, "rosters", key + ".txt");

        private static Roster Load(string key)
        {
            string path = FilePath(key);
            try
            {
                if (!File.Exists(path))
                {
                    Plugin.Log.LogInfo($"No nemeses yet in this world (roster will be saved to {path}).");
                    return new Roster();
                }
                var warnings = new List<string>();
                var r = Roster.Parse(File.ReadAllText(path), warnings);
                // Nemeses from before personalities existed get one (and a colouring and size), once.
                Rules.PaletteNames = Plugin.PaletteNames();
                int given = r.Living.Count(n => Rules.AssignLook(n, Rng));
                if (given > 0)
                {
                    Plugin.Log.LogInfo($"Gave {given} older nemeses a personality and look.");
                    _pendingSave = true;
                }
                foreach (var w in warnings) Plugin.Log.LogWarning(w);
                Plugin.Log.LogInfo($"Loaded {r.Active.Count()} active nemeses ({r.All.Count} total) for this world.");
                return r;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't read the roster at {path} ({e.Message}). Starting empty; the file is left alone.");
                _worldKey = key + "_unreadable"; // never overwrite a file we couldn't read
                return new Roster();
            }
        }

        internal static void Save()
        {
            if (_roster == null || _worldKey == null || _worldKey.EndsWith("_unreadable")) return;
            string path = FilePath(_worldKey);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, _roster.Serialize());
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't save the roster ({e.Message})."); }
        }

        private static object _keyFor;
        private static string _cachedKey;

        /// <summary>A file-name-safe key for the current world: its name plus its unique id when available.</summary>
        private static string WorldKey()
        {
            if (ReferenceEquals(_keyFor, ZNet.instance) && _cachedKey != null) return _cachedKey;
            _keyFor = ZNet.instance;
            _cachedKey = ComputeWorldKey();
            Plugin.DebugLog($"World key: {_cachedKey}");
            return _cachedKey;
        }

        private static string ComputeWorldKey()
        {
            string name = "world";
            long uid = 0;
            try { name = Traverse.Create(ZNet.instance).Method("GetWorldName").GetValue<string>() ?? name; } catch { /* older/newer game */ }
            try { uid = Traverse.Create(ZNet.instance).Method("GetWorldUID").GetValue<long>(); } catch { /* fine without */ }
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return uid != 0 ? $"{name}_{uid}" : name;
        }

        // ---- Events from players ------------------------------------------------

        /// <summary>A player died. Decide, save, tell everyone.</summary>
        internal static void OnDeath(DeathReport report)
        {
            var roster = Roster;
            if (roster == null) return;
            Rules.Settings = Plugin.CurrentRules();
            Rules.Personas = Plugin.Personas;
            Rules.PaletteNames = Plugin.PaletteNames();
            Rules.Lines = Plugin.Lines;

            // A creature killed them but it can't be a nemesis (a boar, say): that's a mundane death with witnesses.
            if (report.Killer != null && string.IsNullOrEmpty(report.Killer.NemesisId) && !Rules.Settings.Species.Contains(report.Killer.Species))
            {
                report.Cause = "Creature";
                report.CauseDetail = report.Killer.SpeciesName;
                report.Killer = null;
            }
            if (!Plugin.Enabled.Value && !report.Forced)
            {
                // Off: existing nemeses still remember, but nobody new rises.
                if (report.Killer == null || roster.FindActive(report.Killer.NemesisId) == null) return;
            }

            var res = Rules.OnDeath(roster, report, DateTime.UtcNow, Rng);
            Plugin.DebugLog($"Death of {report.VictimName} ({report.Cause}{(report.Killer != null ? " by " + report.Killer.Species : "")}, " +
                            $"{report.Witnesses.Count} nearby): {(res.Promoted != null ? "promoted " + res.Promoted.FullName : "no promotion")}, " +
                            $"{res.Changed.Count} changed.");
            if (res.Promoted != null)
                Plugin.Log.LogInfo($"New nemesis: {res.Promoted.FullName} ({res.Promoted.Species}, {res.Promoted.Biome}, level {res.Promoted.Level}, " +
                                   $"strong vs {Or(res.Promoted.Strength)}, weak to {res.Promoted.Weakness}, {string.Join("/", res.Promoted.Traits)}).");

            // The reveal goes first, so the victim's game knows to hold the new look back for the camera
            // before the roster update arrives.
            if (res.RevealId != null)
            {
                var n = roster.Find(res.RevealId);
                Net.SendReveal(report.VictimName, new RevealInfo
                {
                    NemesisId = res.RevealId,
                    Zdo = res.RevealZdo,
                    Kind = res.RevealKind,
                    Title = n?.FullName ?? "",
                    Line = res.RevealLine,
                    Messages = res.Messages,
                    IsTest = report.Forced
                });
            }
            if (res.RosterChanged)
            {
                Changed();
            }
            if (res.RevealId == null)
                foreach (var m in res.Messages) Net.SendMessage(report.VictimName, m);
        }

        /// <summary>A nemesis died for real.</summary>
        internal static void OnSlain(string nemesisId, string killerName)
        {
            var roster = Roster;
            var n = roster?.FindActive(nemesisId);
            if (n == null) return;
            n.State = NemesisState.Retired;
            n.RetiredReason = string.IsNullOrEmpty(killerName) ? "Slain" : $"Slain by {killerName}";
            Plugin.Log.LogInfo($"{n.FullName} has fallen ({n.RetiredReason}).");
            Changed();
            if (!string.IsNullOrEmpty(killerName))
            {
                Net.SendMessage(killerName, Fill.Line(Plugin.Lines.Pick(Rng, "msg-slain") ?? "You have slain {name}.", n, killerName));
                Net.SendMessage("", Fill.Line(Plugin.Lines.Pick(Rng, "msg-slain-other") ?? "{player} has slain {name}.", n, killerName),
                                exceptName: killerName);
            }
        }

        /// <summary>A nemesis cheated death: it's off recovering, scarred, and will come back for whoever did it.</summary>
        internal static void OnEscaped(string id, string blow, long byId, string byName)
        {
            var n = Roster?.FindActive(id);
            if (n == null) return;
            Rules.Lines = Plugin.Lines;
            var msgs = Rules.OnEscaped(n, blow, byId, byName, WorldTime(), Plugin.RecoveryDays.Value * DayLength(), Rng);
            Plugin.Log.LogInfo($"{n.FullName} cheated death ({blow}, by {(byName.Length > 0 ? byName : "something")}); " +
                               $"back in {Plugin.RecoveryDays.Value:0.#} day(s).");
            Changed();
            if (Plugin.AnnounceEscapes.Value) foreach (var m in msgs) Net.SendMessage(byName, m);
        }

        /// <summary>A recovering nemesis has come back in a new body, for a player.</summary>
        /// <summary>After any change: crown Champions, save, tell everyone.</summary>
        private static void Changed()
        {
            var roster = Roster;
            if (roster == null) return;
            foreach (var n in Rules.UpdateChampions(roster, Plugin.ChampionMinInBiome.Value))
            {
                Plugin.Log.LogInfo($"{n.FullName} is now the Champion of the {Rules.BiomeName(n.Biome)}.");
                Net.SendMessage("", Fill.Line(Plugin.Lines.Pick(Rng, "msg-champion") ?? "{name} is now the Champion of the {cause}.",
                                              n, "", 0, Rules.BiomeName(n.Biome)));
            }
            Save();
            Net.BroadcastRoster();
        }

        /// <summary>A nemesis got a new body (it came back, or joined a raid). Older bodies stop being it.</summary>
        internal static void OnNewBody(string id, string zdoKey, int gen, string reason, string playerName)
        {
            var n = Roster?.FindLiving(id);
            if (n == null) return;
            if (reason == "return") { OnReturned(n, zdoKey, playerName); n.BodyGen = Math.Max(n.BodyGen, gen); Changed(); return; }
            n.Zdo = zdoKey ?? "";
            n.BodyGen = Math.Max(n.BodyGen, gen);
            Plugin.Log.LogInfo($"{n.FullName} has a new body ({reason}).");
            Changed();
        }

        /// <summary>Test button: a scar of a given type, added at once (the epithet changes too).</summary>
        private static bool _pendingSave;

        /// <summary>Test button: a personality for a nemesis, with a fresh colouring and size.</summary>
        internal static void TestPersonality(string id, string personality)
        {
            var n = Roster?.FindActive(id);
            if (n == null) return;
            if (!Plugin.Personas.Names.Contains(personality))
            {
                Plugin.ShowPlain($"Grudges: no personality called '{personality}'. Try: {string.Join(", ", Plugin.Personas.Names.ToArray())}");
                return;
            }
            Rules.Personas = Plugin.Personas;
            Rules.PaletteNames = Plugin.PaletteNames();
            n.Personality = personality; n.Palette = ""; n.SizeRoll = -1f;
            Rules.AssignLook(n, Rng);
            Plugin.Log.LogInfo($"Test: {n.FullName} is now a {personality} ({n.Palette}, size roll {n.SizeRoll:0.00}).");
            Changed();
        }

        internal static void TestScar(string id, string type, long byId, string byName)
        {
            var n = Roster?.FindActive(id);
            if (n == null) return;
            string effect = type == "Fire" && Rng.NextDouble() < 0.5 ? "Fear" : "Resist";
            n.Scars.Add(new Scar { Type = type, Effect = effect, By = byName ?? "" });
            n.GrudgeFor(byId, byName, true);
            string epithet = Plugin.Lines.Pick(Rng, "epithet-scar:" + type);
            if (!string.IsNullOrEmpty(epithet)) n.Epithet = epithet;
            Plugin.Log.LogInfo($"Test: {n.FullName} now has a {type} scar ({effect}). Props: {string.Join(", ", Rules.PropsFor(n).ToArray())}.");
            Changed();
        }

        // ---- Raids ----------------------------------------------------------------------

        private static readonly Dictionary<string, string> _raidPicks = new Dictionary<string, string>(); // raid key -> nemesis id ("" = none)

        /// <summary>
        /// A player's base is being raided. The first player to ask decides (and spawns it); everyone else in the
        /// same raid just gets the announcement.
        /// </summary>
        internal static RaidAnswer OnRaid(string raidKey, List<string> species, string baseBiome, long playerId, string playerName, bool forced)
        {
            var roster = Roster;
            var answer = new RaidAnswer();
            if (roster == null || (!Plugin.RaidsEnabled.Value && !forced)) return answer;

            bool first = !_raidPicks.TryGetValue(raidKey, out string id);
            if (first)
            {
                var pick = Rules.PickRaider(roster, species, baseBiome, playerId, playerName, Rng,
                    forced ? 1f : Plugin.RaidChanceNamed.Value, forced ? 1f : Plugin.RaidChanceChampion.Value,
                    forced ? 1f : Plugin.RaidChanceChampionGrudge.Value);
                id = pick?.Id ?? "";
                _raidPicks[raidKey] = id;
                Plugin.Log.LogInfo(pick != null ? $"Raid ({raidKey}): {pick.FullName} joins." : $"Raid ({raidKey}): no nemesis joins.");
            }
            var n = roster.FindActive(id);
            if (n == null) return answer;
            answer.NemesisId = n.Id;
            answer.Spawn = first;
            answer.Message = Fill.Line(Plugin.Lines.Pick(Rng, n.Champion ? "msg-raid-champion" : "msg-raid", "msg-raid") ?? "{name} leads the attack!",
                                       n, playerName, 0, Rules.BiomeName(n.Biome));
            answer.Line = Fill.Line(Rules.PickFor(Rng, n, "raid-line") ?? "I've come for you, {player}!", n, playerName);
            return answer;
        }

        private static void OnReturned(Nemesis n, string zdoKey, string playerName)
        {
            if (n == null || n.IsActive) return; // already back (someone else's game got there first)
            Rules.OnReturned(n, zdoKey);
            Plugin.Log.LogInfo($"{n.FullName} has returned for {playerName}.");
            var msg = Fill.Line(Plugin.Lines.Pick(Rng, "msg-returned") ?? "{name} has come back for you.", n, playerName);
            string cause = n.Scars.Count > 0 ? n.Scars[n.Scars.Count - 1].Type : "";
            string line = Fill.Line(Rules.PickFor(Rng, n, "reveal-returned:" + cause, "reveal-returned") ?? "Remember me, {player}?",
                                    n, playerName, 0, Rules.DamagePhrase(cause));
            Net.SendReveal(playerName, new RevealInfo
            {
                NemesisId = n.Id, Zdo = zdoKey ?? "", Kind = "returned", Title = n.FullName, Line = line,
                Messages = new List<string> { msg }
            });
            // (saved by the caller)
        }

        internal class RaidAnswer
        {
            public string NemesisId = "", Message = "", Line = "";
            public bool Spawn;
        }

        /// <summary>The world's clock in seconds (keeps counting across sessions).</summary>
        internal static double WorldTime()
        {
            try { return ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0; } catch { return 0; }
        }

        /// <summary>Length of an in-game day in seconds (1800 unless something changed it).</summary>
        internal static double DayLength()
        {
            try
            {
                if (EnvMan.instance != null)
                {
                    object v = Traverse.Create(EnvMan.instance).Field("m_dayLengthSec").GetValue();
                    if (v != null) { double d = Convert.ToDouble(v); if (d > 60) return d; }
                }
            }
            catch { /* use the default */ }
            return 1800;
        }

        internal static void Clear()
        {
            var roster = Roster;
            if (roster == null) return;
            int count = roster.All.Count;
            roster.All.Clear();
            Changed();
            Plugin.Log.LogInfo($"Roster cleared ({count} records forgotten).");
        }

        private static string Or(string s) => string.IsNullOrEmpty(s) ? "nothing yet" : s;
    }
}
