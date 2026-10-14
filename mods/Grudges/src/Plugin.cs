using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.Grudges";
        public const string Name = "Grudges";
        public const string Version = "0.9.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // 01 - General
        internal static ConfigEntry<bool> Enabled, MessagesTopLeft;
        internal static ConfigEntry<Intensity> IntensitySetting;
        internal static ConfigEntry<string> SpeciesList;
        internal static ConfigEntry<float> WitnessRadius, WitnessCloseEnough;

        // 02 - Custom Intensity
        internal static ConfigEntry<float> KillChance, WitnessChance, CooldownMinutes;
        internal static ConfigEntry<int> MaxPerBiome;

        // 03 - Nemesis Power
        internal static ConfigEntry<string> MaxLevels;
        internal static ConfigEntry<float> SwiftSpeed, BrutalDamage, RegenPerSecond, RegenInCombat, CowardFleeHealth, RelentlessRange;

        // 04 - Looks
        internal static ConfigEntry<float> Scale, SizeMin, SizeMax, ChampionScale, AuraStrength;
        internal static ConfigEntry<bool> PropsEnabled, Tint;
        internal static ConfigEntry<string> StealableHelmets;
        internal static ConfigEntry<float> TintHue, TintSaturation, TintBrightness;

        // 05 - Taunts & Emotes
        internal static ConfigEntry<bool> TauntsEnabled, RevealPlayerEmotes, OnSightEmotes;
        internal static ConfigEntry<float> TauntDistance, TauntCooldown, BubbleSeconds, OnSightEmoteChance;
        internal static ConfigEntry<int> RevealAnimationRepeats;

        // 06 - Death Reveal
        internal static ConfigEntry<bool> RevealEnabled, RevealWalkOff, RevealOnTests, RevealSwell, ReturnCutscene;
        internal static ConfigEntry<float> RevealDelay, RevealCameraDistance, RevealCameraHeight;
        internal static ConfigEntry<string> RevealEffects;

        // 07 - Cheating Death & Trophies
        internal static ConfigEntry<bool> AnnounceEscapes, TrophiesEnabled;
        internal static ConfigEntry<float> CheatDeathChance, CheatDeathDecay, RecoveryDays, ReturnChancePerMinute, ReturnDistance;

        // 08 - Champions & Raids
        internal static ConfigEntry<int> ChampionMinInBiome, ChampionEscort;
        internal static ConfigEntry<float> RaidChanceNamed, RaidChanceChampion, RaidChanceChampionGrudge, RaidDelay;
        internal static ConfigEntry<bool> RaidsEnabled;

        // 09 - Hunting
        internal static ConfigEntry<bool> HuntsEnabled, HuntAdjacentOnly;
        internal static ConfigEntry<float> HuntChancePerHour, HuntCooldownMinutes, HuntWarningSeconds, HuntNightBoost;

        // 10 - Testing
        internal static ConfigEntry<bool> DebugLogging;
        internal static ConfigEntry<string> TestDamageType, TestPersonality, SpawnCreature;

        // 11 - Prop Fitting
        internal static ConfigEntry<bool> FitMode;
        internal static ConfigEntry<string> FitProp, FitHelmet, FitPersonality, FitSaveFor;
        internal static ConfigEntry<float> FitUp, FitForward, FitSide, FitTilt, FitTurn, FitRoll, FitSize;
        internal static ConfigEntry<string> TryItem, TryBone;
        internal static ConfigEntry<bool> TryOnlyThisCreature;
        internal const string NoItem = "(personality's own)";

        // 12 - Colour Preview
        internal static ConfigEntry<bool> ColourPreview;
        internal static ConfigEntry<float> PreviewHue, PreviewSaturation, PreviewBrightness;
        internal static ConfigEntry<string> PreviewSaveTo, PreviewName;
        internal static int PersonasVersion;

        // Data files (Bepinex/config/Grudges.*.txt)
        internal static LineBook Lines = new LineBook();
        internal static PersonalityBook Personas = new PersonalityBook();
        internal static KeyValueBook Looks = new KeyValueBook();
        internal static string ConfigFolder => Path.Combine(Paths.ConfigPath, "Grudges");
        internal static string LinesFilePath => Path.Combine(Paths.ConfigPath, "Grudges.lines.txt");
        internal static string PersonalitiesFilePath => Path.Combine(Paths.ConfigPath, "Grudges.personalities.txt");
        internal static string LooksFilePath => Path.Combine(Paths.ConfigPath, "Grudges.looks.txt");
        internal static string FitsFilePath => Path.Combine(Paths.ConfigPath, "Grudges.propfits.txt");

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            // The data files first: they take over settings older versions kept in the config file,
            // and the dropdowns below are filled from them.
            ReloadLooks();
            ReloadPersonalities();
            Props.LoadFitsFile();
            BindConfig();
            ForgetOldSettings();
            ReloadLines();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<Net>();
            Net.Patch(_harmony);
            gameObject.AddComponent<Bodies>();
            gameObject.AddComponent<Reveal>();
            Reveal.Patch(_harmony);
            gameObject.AddComponent<Returns>();
            gameObject.AddComponent<Raids>();
            gameObject.AddComponent<Hunts>();
            Trophies.Patch(_harmony);

            Log.LogInfo($"{Name} {Version} loaded. The forest is keeping score.");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        // ------------------------------------------------------------------
        // Config
        // ------------------------------------------------------------------

        private int _order;

        /// <summary>
        /// Binds a setting. If it lived somewhere else in an older version ("Old Section/Old Key"), its value is carried over.
        /// </summary>
        private ConfigEntry<T> B<T>(string section, string key, T def, string text, AcceptableValueBase range = null,
                                    bool advanced = false, string old = null)
        {
            var entry = Config.Bind(section, key, def, Desc(text, _order--, range, advanced));
            if (old != null)
            {
                int slash = old.IndexOf('/');
                string oldValue = TakeOld(old.Substring(0, slash), old.Substring(slash + 1));
                if (oldValue != null)
                {
                    try { entry.SetSerializedValue(oldValue); }
                    catch (Exception e) { Log.LogWarning($"Couldn't carry over '{old}' ({e.Message})."); }
                }
            }
            return entry;
        }

        private void Section() => _order = 1000;

        private void BindConfig()
        {
            Section();
            const string G = "01 - General";
            Enabled = B(G, "Enabled", true, "Turn nemeses on or off. Off still shows existing ones, but no new ones are made.", old: "1 - General/Enabled");
            IntensitySetting = B(G, "Intensity", Intensity.Subtle,
                "How often nemeses appear.\nSubtle: rare, the default. Standard: noticeable. Mordor: constant.\n" +
                "Custom: uses the numbers in '02 - Custom Intensity'.\nOn a server, the server's setting is the one that counts.",
                old: "1 - General/Intensity");
            SpeciesList = B(G, "Creatures", "Greydwarf, Greydwarf_Elite, Greydwarf_Shaman, Greyling, Goblin, GoblinBrute, GoblinShaman, GoblinArcher, " +
                "Skeleton, Skeleton_NoArcher, Skeleton_Poison, Charred_Melee, Charred_Archer, Charred_Mage, Charred_Twitcher",
                "Creatures (prefab names, comma-separated) that can become nemeses. To add a new kind, add it here, and give it " +
                "a trophy (and any per-creature looks) in Grudges.looks.txt.", old: "1 - General/Species");
            // Older defaults move up to the current one; a list you've edited stays as it is.
            string flat = SpeciesList.Value.Replace(" ", "");
            if (new[]
                {
                    "Greydwarf,Greydwarf_Elite,Greydwarf_Shaman",
                    "Greydwarf,Greydwarf_Elite,Greydwarf_Shaman,Greyling",
                    "Greydwarf,Greydwarf_Elite,Greydwarf_Shaman,Greyling,Goblin,GoblinBrute,GoblinShaman,GoblinArcher",
                }.Contains(flat))
                SpeciesList.Value = (string)SpeciesList.DefaultValue;
            MessagesTopLeft = B(G, "Messages Top Left", false,
                "Show announcements (new nemesis, grows stronger...) in the top-left instead of the centre of the screen.", old: "5 - Taunts/Messages Top Left");
            WitnessRadius = B(G, "Witness Radius", 30f,
                "How far (metres) a creature can be from an embarrassing death and still count as a witness.",
                new AcceptableValueRange<float>(5f, 80f), true, "1 - General/Witness Radius");
            WitnessCloseEnough = B(G, "Witness Always Sees Within", 12f,
                "A creature this close always counts as a witness. Further away, it has to have noticed you.",
                new AcceptableValueRange<float>(0f, 40f), true, "1 - General/Witness Always Sees Within");

            Section();
            const string C = "02 - Custom Intensity";
            const string OC = "2 - Custom Intensity/";
            KillChance = B(C, "Kill Chance", 0.35f, "Chance (0-1) that a creature that kills you becomes a nemesis.",
                new AcceptableValueRange<float>(0f, 1f), old: OC + "Kill Chance");
            WitnessChance = B(C, "Witness Chance", 0.1f, "Chance (0-1) that a creature that watched you die some silly way becomes a nemesis.",
                new AcceptableValueRange<float>(0f, 1f), old: OC + "Witness Chance");
            CooldownMinutes = B(C, "Cooldown Minutes", 20f, "Real minutes after a nemesis is made from your death before another can be.",
                new AcceptableValueRange<float>(0f, 240f), old: OC + "Cooldown Minutes");
            MaxPerBiome = B(C, "Max Per Biome", 3,
                "How many nemeses a biome can hold. When it's full, a newcomer has to fight the weakest for its place.",
                new AcceptableValueRange<int>(1, 20), old: OC + "Max Per Biome");

            Section();
            const string P = "03 - Nemesis Power";
            const string OP = "3 - Power/";
            SwiftSpeed = B(P, "Swift Speed", 1.25f, "Speed multiplier for Swift nemeses.", new AcceptableValueRange<float>(1f, 2f), old: OP + "Swift Speed");
            BrutalDamage = B(P, "Brutal Damage", 1.3f, "Damage multiplier for Brutal nemeses.", new AcceptableValueRange<float>(1f, 3f), old: OP + "Brutal Damage");
            RegenPerSecond = B(P, "Regenerating Heal", 0.02f, "Share of max health a Regenerating nemesis heals per second when it isn't fighting.",
                new AcceptableValueRange<float>(0f, 0.2f), old: OP + "Regenerating Heal");
            RegenInCombat = B(P, "Regenerating Heal In Combat", 0.005f, "Same, while it's fighting.",
                new AcceptableValueRange<float>(0f, 0.1f), old: OP + "Regenerating Heal In Combat");
            CowardFleeHealth = B(P, "Coward Flee Health", 0.35f, "A Coward runs away below this share of its health.",
                new AcceptableValueRange<float>(0f, 0.9f), old: OP + "Coward Flee Health");
            RelentlessRange = B(P, "Relentless Range", 1.5f, "Sight and hearing multiplier for Relentless nemeses (chase distance gets double this).",
                new AcceptableValueRange<float>(1f, 3f), old: OP + "Relentless Range");
            MaxLevels = B(P, "Max Level By Biome",
                "Meadows=3, BlackForest=4, Swamp=4, Mountain=5, Plains=5, Mistlands=6, AshLands=6, DeepNorth=6, Ocean=4",
                "Highest level a nemesis can reach in each biome (level = stars + 1). Others default to 4.\n" +
                "The game only draws star icons for 1 and 2 stars, so higher ones show no stars but still hit harder.",
                advanced: true, old: OP + "Max Level By Biome");

            Section();
            const string L = "04 - Looks";
            SizeMin = B(L, "Size Min", 1.05f, "Smallest a nemesis can be (1 = normal size). Each gets its own size in this range; " +
                "its personality nudges it (Brutes big, Cowards small).", new AcceptableValueRange<float>(0.7f, 1.5f), old: "4 - Look/Size Min");
            SizeMax = B(L, "Size Max", 1.3f, "Biggest a nemesis can be. Keep it modest; big ones get stuck on trees.",
                new AcceptableValueRange<float>(0.8f, 1.8f), old: "4 - Look/Size Max");
            ChampionScale = B(L, "Champion Size", 1.12f, "A Champion is this much bigger again than other nemeses.",
                new AcceptableValueRange<float>(1f, 1.4f), old: "8b - Champions & Raids/Champion Size");
            PropsEnabled = B(L, "Props", true,
                "Nemeses carry what happened to them: arrows for arrow scars, an axe for blade scars, ice for frost, a helmet " +
                "for club scars or one stolen from a player they killed, and their personality's own prop.\n" +
                "Which items and bones: Grudges.looks.txt. Where exactly they sit: '11 - Prop Fitting'.", old: "8d - Props/Props");
            StealableHelmets = B(L, "Stealable Helmets", "HelmetBronze, HelmetIron, HelmetDrake, HelmetPadded",
                "Helmets a nemesis can wear a copy of after killing you in one. Only rigid helmets: hoods and anything cloth-like " +
                "don't fit a creature's head. Check a new one with the fitting tool first.", old: "8d - Props/Stealable Helmets");
            AuraStrength = B(L, "Aura Strength", 0.4f,
                "How much of a scar's elemental aura shows (1 = as strong as really burning; lower = a lingering trickle).",
                new AcceptableValueRange<float>(0.05f, 1.5f), old: "8d - Props/Aura Strength");
            Tint = B(L, "Recolour", true,
                "Recolour nemeses so you can spot them. Each gets a colouring from Grudges.looks.txt; a scar's skin replaces it.",
                old: "4 - Look/Tint");
            TintHue = B(L, "Fallback Tint Hue", -0.08f, "Hue shift for a nemesis with no colouring (its palette was removed from the looks file).",
                new AcceptableValueRange<float>(-0.5f, 0.5f), true, "4 - Look/Tint Hue");
            TintSaturation = B(L, "Fallback Tint Saturation", 0.25f, "Saturation shift, same.",
                new AcceptableValueRange<float>(-1f, 1f), true, "4 - Look/Tint Saturation");
            TintBrightness = B(L, "Fallback Tint Brightness", -0.2f, "Brightness shift, same.",
                new AcceptableValueRange<float>(-1f, 1f), true, "4 - Look/Tint Brightness");
            Scale = B(L, "Old Fixed Size", 1.15f, "Size of nemeses made before 0.6.0 that never got their own size roll.",
                new AcceptableValueRange<float>(1f, 1.5f), true, "4 - Look/Size");

            Section();
            const string T = "05 - Taunts & Emotes";
            TauntsEnabled = B(T, "Taunts", true, "Nemeses speak up when they spot you.", old: "5 - Taunts/Taunts");
            TauntDistance = B(T, "Taunt Distance", 20f, "How close (metres) a nemesis has to be to taunt you.",
                new AcceptableValueRange<float>(5f, 50f), old: "5 - Taunts/Taunt Distance");
            TauntCooldown = B(T, "Taunt Cooldown", 60f, "Seconds before the same nemesis taunts you again.",
                new AcceptableValueRange<float>(5f, 600f), old: "5 - Taunts/Taunt Cooldown");
            BubbleSeconds = B(T, "Bubble Seconds", 6f, "How long a taunt stays up.", new AcceptableValueRange<float>(2f, 20f), old: "5 - Taunts/Bubble Seconds");
            RevealPlayerEmotes = B(T, "Player Emotes", true,
                "Creatures whose skeleton matches the player's (checked once per creature type; see the log line 'Player emotes on ...') " +
                "gloat with player emotes, like the Fuling Heckler. Others use their own animations.\n" +
                "Which emotes for which moment: the personalities file ([Default], and each personality).", old: "6 - Reveal/Player Emotes");
            OnSightEmotes = B(T, "Emote On Sight", true, "A nemesis that taunts you may also do an emote.", old: "5 - Taunts/Emote On Sight");
            OnSightEmoteChance = B(T, "Emote On Sight Chance", 0.6f, "Chance (0-1) a taunt comes with an emote.",
                new AcceptableValueRange<float>(0f, 1f), old: "5 - Taunts/Emote On Sight Chance");
            RevealAnimationRepeats = B(T, "Own Animation Repeats", 2,
                "For creatures without player emotes: how many times it plays its own gloat animation (2 = a double hop).",
                new AcceptableValueRange<int>(1, 5), old: "6 - Reveal/Taunt Animation Repeats");

            Section();
            const string V = "06 - Death Reveal";
            RevealEnabled = B(V, "Death Reveal", true,
                "After you die, the camera turns to the creature as it becomes (or grows as) a nemesis. It says its line, " +
                "walks off, and the screen fades. Off = just a message after you respawn.", old: "6 - Reveal/Death Reveal");
            ReturnCutscene = B(V, "Return Cutscene", true,
                "When a nemesis comes back for you, the camera shows it for a few seconds (no fade). Off = just a message.", old: "6 - Reveal/Return Cutscene");
            RevealSwell = B(V, "Swell", true, "The creature puffs up and settles back as it transforms.", old: "6 - Reveal/Swell");
            RevealWalkOff = B(V, "Walk Off", true, "After its line, the creature turns and walks away as the screen fades.", old: "6 - Reveal/Walk Off");
            RevealDelay = B(V, "Start Delay", 1.2f, "Seconds after your death before the camera moves (lets the death land first).",
                new AcceptableValueRange<float>(0f, 4f), true, "6 - Reveal/Start Delay");
            RevealCameraDistance = B(V, "Camera Distance", 3.5f, "How far from the creature the camera sits.",
                new AcceptableValueRange<float>(1.5f, 10f), true, "6 - Reveal/Camera Distance");
            RevealCameraHeight = B(V, "Camera Height", 1.4f, "How high above the ground the camera sits.",
                new AcceptableValueRange<float>(0.3f, 5f), true, "6 - Reveal/Camera Height");
            RevealEffects = B(V, "Transform Effects", "vfx_DraugrSpawn, vfx_lootspawn",
                "Game effects played as it transforms (comma-separated). The log line 'Effects you could use' lists real names.",
                advanced: true, old: "6 - Reveal/Transform Effects");

            Section();
            const string D = "07 - Cheating Death & Trophies";
            const string OD = "8 - Cheating Death & Trophies/";
            CheatDeathChance = B(D, "Cheat Death Chance", 0.5f,
                "Chance (0-1) a nemesis survives what should have killed it, the first time. It vanishes, recovers, and comes back " +
                "scarred by whatever nearly killed it. On a server, the game controlling the creature decides, so keep these the same for everyone.",
                new AcceptableValueRange<float>(0f, 0.9f), old: OD + "Cheat Death Chance");
            CheatDeathDecay = B(D, "Cheat Death Decay", 0.5f,
                "The chance is multiplied by this for every escape so far (0.5 = halves each time). " +
                "A killing blow from its weakness halves it again; Cowards get 1.5x.", new AcceptableValueRange<float>(0f, 1f), old: OD + "Cheat Death Decay");
            AnnounceEscapes = B(D, "Announce Escapes", false,
                "Show a message when a nemesis escapes death. Off = it just vanishes, and its return is a surprise.", old: OD + "Announce Escapes");
            RecoveryDays = B(D, "Recovery Days", 1f, "In-game days before it can come back (one day is about 30 real minutes).",
                new AcceptableValueRange<float>(0f, 10f), old: OD + "Recovery Days");
            ReturnChancePerMinute = B(D, "Return Chance Per Minute", 0.5f,
                "Once recovered, the chance each minute that it finds you while you're in its biome.",
                new AcceptableValueRange<float>(0.01f, 1f), old: OD + "Return Chance Per Minute");
            ReturnDistance = B(D, "Return Distance", 30f, "How far away (metres) it appears when it comes back for you.",
                new AcceptableValueRange<float>(15f, 60f), true, OD + "Return Distance");
            TrophiesEnabled = B(D, "Named Trophies", true,
                "A nemesis that dies for good drops a trophy with its name and story. Mount it on an item stand to keep the story.\n" +
                "Which trophy each creature drops: [trophies] in Grudges.looks.txt.", old: OD + "Named Trophies");

            Section();
            const string R = "08 - Champions & Raids";
            const string OR = "8b - Champions & Raids/";
            ChampionMinInBiome = B(R, "Champion Needs", 2,
                "A biome gets a Champion (its strongest nemesis) once it has at least this many nemeses.",
                new AcceptableValueRange<int>(1, 10), old: OR + "Champion Needs");
            ChampionEscort = B(R, "Champion Escort", 2,
                "Ordinary creatures of its kind that come with a Champion when it returns or joins a raid.",
                new AcceptableValueRange<int>(0, 6), old: OR + "Champion Escort");
            RaidsEnabled = B(R, "Nemeses Join Raids", true,
                "When a raid that brings a nemesis's kind hits a base (the Elder's raid brings Greydwarfs), one may join it.", old: OR + "Nemeses Join Raids");
            RaidChanceNamed = B(R, "Raid Chance", 0.25f, "Chance (0-1) an ordinary nemesis joins a matching raid.",
                new AcceptableValueRange<float>(0f, 1f), old: OR + "Raid Chance");
            RaidChanceChampion = B(R, "Raid Chance Champion", 0.1f, "Chance the Champion leads it instead.",
                new AcceptableValueRange<float>(0f, 1f), old: OR + "Raid Chance Champion");
            RaidChanceChampionGrudge = B(R, "Raid Chance Champion With Grudge", 0.5f,
                "Chance the Champion leads it when it has a grudge against the player being raided.",
                new AcceptableValueRange<float>(0f, 1f), old: OR + "Raid Chance Champion With Grudge");
            RaidDelay = B(R, "Raid Arrival Delay", 12f, "Seconds after the raid starts before the nemesis shows up.",
                new AcceptableValueRange<float>(0f, 120f), true, OR + "Raid Arrival Delay");

            Section();
            const string H = "09 - Hunting";
            const string OH = "8c - Hunting/";
            HuntsEnabled = B(H, "Nemeses Follow You", true,
                "A nemesis with a grudge against you (it killed you, saw you die, or you scarred it) may follow you into other biomes.",
                old: OH + "Nemeses Follow You");
            HuntChancePerHour = B(H, "Chance Per Hour", 0.3f,
                "Chance (0-1) per real hour, while you're outside its biome, that one comes for you. Rare by design.",
                new AcceptableValueRange<float>(0f, 1f), old: OH + "Chance Per Hour");
            HuntNightBoost = B(H, "Night Multiplier", 2f, "The chance is multiplied by this at night.",
                new AcceptableValueRange<float>(1f, 5f), old: OH + "Night Multiplier");
            HuntCooldownMinutes = B(H, "Cooldown Minutes", 45f, "Real minutes after one comes for you before another can.",
                new AcceptableValueRange<float>(1f, 600f), old: OH + "Cooldown Minutes");
            HuntWarningSeconds = B(H, "Warning Seconds", 8f, "Seconds between the warning and it turning up.",
                new AcceptableValueRange<float>(0f, 60f), true, OH + "Warning Seconds");
            HuntAdjacentOnly = B(H, "Neighbouring Biomes Only", false,
                "Only follow you into biomes next to its own in toughness (gentler).", old: OH + "Neighbouring Biomes Only");

            BindTesting();
            BindFitting();
            BindColourPreview();
        }

        private void BindTesting()
        {
            Section();
            const string X = "10 - Testing";
            Config.Bind(X, "About", "", Note(
                "Host or single player only. Most buttons act on the nearest nemesis within 40 m. " +
                "The data files are in BepInEx/config: Grudges.lines.txt (names and taunts), Grudges.personalities.txt, " +
                "Grudges.looks.txt (colours, scar looks, props, trophies) and Grudges.propfits.txt.", _order--));
            DebugLogging = B(X, "Debug Logging", false, "Extra detail in the BepInEx log.", old: "9 - Testing/Debug Logging");
            RevealOnTests = B(X, "Reveal On Test Buttons", true,
                "The test buttons play the reveal too (you're alive, so the camera just borrows your view for a moment).",
                old: "6 - Reveal/Reveal On Test Buttons");

            var kinds = RuleSettings.ParseList(SpeciesList.Value).Distinct().ToList();
            if (kinds.Count == 0) kinds.Add("Greydwarf");
            SpawnCreature = B(X, "Spawn Creature", kinds[0],
                "Which creature the button below spawns. The list is the Creatures setting (01 - General); restart after changing that.",
                new AcceptableValueList<string>(kinds.ToArray()));
            Config.Bind(X, "Spawn Nemesis", "", Button("Spawns the Spawn Creature a few metres ahead and promotes it straight away. " +
                "It joins the list of the biome you're standing in.", _order--, "Spawn a nemesis", () => Testing.SpawnAndPromote()));
            Config.Bind(X, "Promote Nearest", "", Button(
                "Turns the nearest eligible creature (within 25 m) into a nemesis, as if it had just killed you.",
                _order--, "Promote nearest creature", () => Testing.PromoteNearest(false)));
            Config.Bind(X, "Witness Nearest", "", Button(
                "Pretends a tree just killed you with the nearest eligible creature watching. It becomes a witness nemesis.",
                _order--, "Fake a tree death", () => Testing.PromoteNearest(true)));
            Config.Bind(X, "Nemesis Killed Me", "", Button("Pretends the nearest nemesis just killed you again (it grows stronger).",
                _order--, "Nearest nemesis killed me", () => Testing.NemesisKilledMe()));

            var names = PersonalityChoices();
            TestPersonality = B(X, "Test Personality", names.Contains("Braggart") ? "Braggart" : names[0],
                "Personality for the button below. The list comes from the personalities file (restart to see new ones).",
                new AcceptableValueList<string>(names.ToArray()), old: "9 - Testing/Test Personality");
            Config.Bind(X, "Set Personality", "", Button("Gives the nearest nemesis the Test Personality, and a fresh random colouring and size.",
                _order--, "Set personality of nearest nemesis", () => Testing.SetPersonality()));

            TestDamageType = B(X, "Test Damage Type", "Pierce",
                "The damage type for the two buttons below. Pierce = arrows, Slash = axe, Frost = ice and frost aura, " +
                "Fire = charred skin and embers, Blunt = bronze helmet.", new AcceptableValueList<string>(Traits.DamageTypes), old: "9 - Testing/Test Damage Type");
            Config.Bind(X, "Add Scar", "", Button(
                "Gives the nearest nemesis a scar of the Test Damage Type right away, with its props. The quickest way to see each prop.",
                _order--, "Add a scar to nearest nemesis", () => Testing.AddScar()));
            Config.Bind(X, "Wound Nearest", "", Button(
                "Hits the nearest nemesis with a killing blow of the Test Damage Type from you, and it cheats death (guaranteed).",
                _order--, "Nearest nemesis cheats death", () => Testing.Wound(true)));
            Config.Bind(X, "Kill Nearest", "", Button("Kills the nearest nemesis for good (no cheating death). It should drop its named trophy.",
                _order--, "Kill nearest nemesis for good", () => Testing.Wound(false)));

            Config.Bind(X, "Taunt Now", "", Button("Makes the nearest nemesis taunt you right now.", _order--, "Taunt me", () => Testing.TauntNow()));
            Config.Bind(X, "Replay Reveal", "", Button("Plays the death reveal on the nearest nemesis (no changes to it).",
                _order--, "Replay reveal", () => Testing.ReplayReveal()));
            Config.Bind(X, "Return Now", "", Button("A nemesis you scarred comes back for you right now, wherever you are.",
                _order--, "Bring a scarred nemesis back", () => Returns.ForceNow()));
            Config.Bind(X, "Fake Raid", "", Button(
                "Pretends your base is being raided: a nemesis is guaranteed to join (if one is out in the world), arriving after the delay. " +
                "To test the real thing, use the game's console command 'event army_theelder' (with devcommands).",
                _order--, "Fake a raid", () => Raids.FakeRaid()));
            Config.Bind(X, "Hunt Me", "", Button(
                "A nemesis with a grudge against you (or any nemesis from another biome) follows you here now: warning, then it arrives.",
                _order--, "A nemesis hunts me now", () => Hunts.ForceNow()));
            Config.Bind(X, "Show Roster", "", Button("Lists every nemesis in this world (on screen and in the log).",
                _order--, "Show roster", () => Testing.ShowRoster()));

            Config.Bind(X, "Reload Files", "", Button("Re-reads the lines, personalities and looks files after you edit them. No restart needed.",
                _order--, "Reload data files", () => { ReloadLines(); ReloadPersonalities(); ReloadLooks(); Props.LoadFitsFile(); ShowPlain("Grudges: data files reloaded."); }));
            Config.Bind(X, "Clear Roster", "", Button("Forgets EVERY nemesis in this world. They turn back into ordinary creatures.",
                _order--, "Clear roster", () => Testing.ClearRoster(), advanced: true));
        }

        private void BindFitting()
        {
            Section();
            const string F = "11 - Prop Fitting";
            const string OF = "10 - Prop Fitting/";
            FitMode = B(F, "Fitting Mode", false,
                "Turn on near a nemesis: every creature within 40 m freezes in its neutral pose, and the nearest nemesis shows every prop. " +
                "Pick one below, move the sliders, then Save. Fits are saved per creature type (a Greyling needs smaller offsets " +
                "than a Greydwarf), in Grudges.propfits.txt.", old: OF + "Fitting Mode");
            FitProp = B(F, "Prop", "arrow1", "Which prop the sliders move. 'persona' is the personality's own prop, 'helmet' the helmet slot.",
                new AcceptableValueList<string>(Props.FittableProps), old: OF + "Prop");
            var names = PersonalityChoices();
            FitPersonality = B(F, "Personality", names.Contains("Braggart") ? "Braggart" : names[0],
                "Whose prop is shown in the 'persona' slot. Each personality has its own fit.",
                new AcceptableValueList<string>(names.ToArray()), old: OF + "Personality");
            var helmets = new List<string> { "HelmetBronze" };
            helmets.AddRange(RuleSettings.ParseList(StealableHelmets.Value).Where(h => !helmets.Contains(h)));
            FitHelmet = B(F, "Helmet", "HelmetBronze",
                "Which helmet is shown in the helmet slot (the dented bronze one, or a stealable helmet). Each has its own fit.",
                new AcceptableValueList<string>(helmets.ToArray()), old: OF + "Helmet");
            FitUp = B(F, "Up", 0f, "Metres up.", new AcceptableValueRange<float>(-0.8f, 0.8f));
            FitForward = B(F, "Forward", 0f, "Metres forward (negative = behind).", new AcceptableValueRange<float>(-0.8f, 0.8f));
            FitSide = B(F, "Side", 0f, "Metres to its right (negative = left).", new AcceptableValueRange<float>(-0.8f, 0.8f));
            FitTilt = B(F, "Tilt", 0f, "Degrees, nodding forward/back.", new AcceptableValueRange<float>(-180f, 180f));
            FitTurn = B(F, "Turn", 0f, "Degrees, turning left/right.", new AcceptableValueRange<float>(-180f, 180f));
            FitRoll = B(F, "Roll", 0f, "Degrees, leaning sideways.", new AcceptableValueRange<float>(-180f, 180f));
            FitSize = B(F, "Size", 1f, "Size compared with the real item.", new AcceptableValueRange<float>(0.2f, 3f));
            TryItem = B(F, "Try Item", NoItem,
                "Shows a different item in the 'persona' slot, live, so you can audition props. Trophies sit on the outside of the " +
                "body (heads, shoulders) and change the silhouette most. Pick one, choose a Try Bone, fit it, then press " +
                "'Make it this personality's prop' and Save fit.", new AcceptableValueList<string>(Props.TryItems()));
            TryBone = B(F, "Try Bone", "Head", "Which bone the tried item hangs from.", new AcceptableValueList<string>(Props.Bones));
            TryOnlyThisCreature = B(F, "Only For This Creature", false,
                "On: the prop is saved for this personality on this creature type only ([Brute:Greyling]). Off: for every creature.");
            Config.Bind(F, "Keep Tried Item", "", Button("Writes the tried item and bone into the personalities file as this personality's prop.",
                _order--, "Make it this personality's prop", () => Props.SaveTriedProp()));
            FitSaveFor = B(F, "Save For", "Whole family",
                "Whole family: the fit is used by every creature in the nemesis's family (all Fulings) that has no fit of its own. " +
                "This creature only: just this type (e.g. only Fuling Berserkers). Creatures without a family save for themselves either way.",
                new AcceptableValueList<string>("Whole family", "This creature only"));
            Config.Bind(F, "Save Fit", "", Button("Saves the sliders as this prop's fit (see Save For).", _order--, "Save fit", () => Props.SaveSliders()));
            Config.Bind(F, "Load Fit", "", Button("Puts this prop's saved fit back into the sliders (undoes unsaved changes).", _order--, "Load saved fit", () => Props.LoadSliders()));
            Config.Bind(F, "Forget Fit", "", Button("Forgets this creature's own fit for this prop (then it uses its family's); press again to forget the family's too.", _order--, "Reset to default", () => Props.ForgetFit()));
            // Picking another prop, helmet or personality loads its own saved fit, so one prop's placement is never saved over another's.
            EventHandler reload = (_, __) => { if (!Props._loading) Props.LoadSliders(); };
            FitProp.SettingChanged += reload;
            FitHelmet.SettingChanged += reload;
            FitPersonality.SettingChanged += reload;
        }

        private void BindColourPreview()
        {
            Section();
            const string K = "12 - Colour Preview";
            ColourPreview = B(K, "Preview", false,
                "On: the nearest nemesis (within 40 m) wears the sliders below instead of its own colours, live. Off: back to normal.");
            Config.Bind(K, "Load", "", Button("Puts the nearest nemesis's current colouring into the sliders, to start from.",
                _order--, "Load nearest nemesis's colour", () => LoadPreviewFromNearest()));
            PreviewHue = B(K, "Hue", 0f, "Turns its colours around the colour wheel. 0.5 is halfway round; 1 is all the way back.",
                new AcceptableValueRange<float>(-10f, 10f));
            PreviewSaturation = B(K, "Saturation", 0f, "+ more vivid, - greyer (-1 is fully grey).", new AcceptableValueRange<float>(-10f, 10f));
            PreviewBrightness = B(K, "Brightness", 0f, "+ lighter, - darker.", new AcceptableValueRange<float>(-10f, 10f));
            PreviewSaveTo = B(K, "Save To", "palettes", "Which section of Grudges.looks.txt the buttons below write to.",
                new AcceptableValueList<string>("palettes", "scar-skin"));
            PreviewName = B(K, "Name", "new",
                "The palette's name (e.g. moss, or a new one), or the scar type for scar-skin (Fire, Frost, Poison, Lightning, Spirit).");
            Config.Bind(K, "Copy", "", Button("Copies the line (e.g. 'moss = 0.06/0.25/-0.15') so you can paste it into the looks file.",
                _order--, "Copy line", () => { GUIUtility.systemCopyBuffer = PreviewLine(); ShowPlain("Grudges: copied  " + PreviewLine()); }));
            Config.Bind(K, "Save", "", Button("Writes the line straight into Grudges.looks.txt (replacing one with the same name) and reloads it.",
                _order--, "Save to looks file", () => SavePreview()));
        }

        private static string Num(float f) => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        private static string PreviewLine() =>
            $"{PreviewName.Value.Trim()} = {Num(PreviewHue.Value)}/{Num(PreviewSaturation.Value)}/{Num(PreviewBrightness.Value)}";

        private static void LoadPreviewFromNearest()
        {
            var p = Player.m_localPlayer;
            var c = p != null ? GameInfo.NearestNemesis(p.transform.position, 40f) : null;
            var body = c != null ? c.GetComponent<NemesisBody>() : null;
            if (body == null || body.Record == null) { ShowPlain("Grudges: no nemesis within 40 m."); return; }
            var t = body.CurrentTint(out bool fromScar);
            PreviewHue.Value = t.x; PreviewSaturation.Value = t.y; PreviewBrightness.Value = t.z;
            string scar = Rules.SkinScar(body.Record);
            PreviewSaveTo.Value = fromScar ? "scar-skin" : "palettes";
            PreviewName.Value = fromScar ? scar : (body.Record.Palette.Length > 0 ? body.Record.Palette : "new");
            ShowPlain($"Grudges: loaded {(fromScar ? scar + " scar skin" : "palette '" + PreviewName.Value + "'")} from {body.Record.FullName}.");
        }

        private static void SavePreview()
        {
            string name = PreviewName.Value.Trim();
            if (name.Length == 0 || name.Contains("=") || name.Contains("[")) { ShowPlain("Grudges: give it a Name first."); return; }
            try
            {
                string text = File.ReadAllText(LooksFilePath);
                string hsv = $"{Num(PreviewHue.Value)}/{Num(PreviewSaturation.Value)}/{Num(PreviewBrightness.Value)}";
                File.WriteAllText(LooksFilePath, KeyValueBook.SetKeys(text, PreviewSaveTo.Value, new Dictionary<string, string> { { name, hsv } }));
            }
            catch (Exception e) { Log.LogWarning($"Couldn't write the looks file ({e.Message})."); return; }
            ReloadLooks();
            ShowPlain($"Grudges: saved [{PreviewSaveTo.Value}] {PreviewLine()}");
        }

        private static List<string> PersonalityChoices()
        {
            var names = Personas.Names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) names.Add("Braggart");
            return names;
        }

        // ------------------------------------------------------------------
        // Carrying settings over from older versions
        // ------------------------------------------------------------------

        private static readonly string[] OldSections =
        {
            "1 - General", "2 - Custom Intensity", "3 - Power", "4 - Look", "5 - Taunts", "6 - Reveal",
            "8 - Cheating Death & Trophies", "8b - Champions & Raids", "8c - Hunting", "8d - Props", "9 - Testing", "10 - Prop Fitting"
        };

        /// <summary>BepInEx keeps settings nothing has claimed yet ("orphans"); that's where an older version's values wait.</summary>
        private static Dictionary<ConfigDefinition, string> Orphans()
        {
            try
            {
                var prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                return prop?.GetValue(Instance.Config, null) as Dictionary<ConfigDefinition, string>;
            }
            catch { return null; }
        }

        /// <summary>An older version's value for a setting (and forgets it), or null if there isn't one.</summary>
        internal static string TakeOld(string section, string key)
        {
            var orphans = Orphans();
            if (orphans == null) return null;
            var def = orphans.Keys.FirstOrDefault(d => d.Section == section && d.Key == key);
            if (def == null) return null;
            string v = orphans[def];
            orphans.Remove(def);
            return v;
        }

        /// <summary>Drops whatever is left of the old sections and rewrites the config file in the new layout.</summary>
        private void ForgetOldSettings()
        {
            var orphans = Orphans();
            if (orphans == null) return;
            var stale = orphans.Keys.Where(d => OldSections.Contains(d.Section)).ToList();
            if (stale.Count == 0) return;
            foreach (var d in stale) orphans.Remove(d);
            Config.Save();
            Log.LogInfo($"Moved settings from an older Grudges into the new layout ({stale.Count} old entries tidied away).");
        }

        /// <summary>The server's rules, built from this game's config.</summary>
        internal static RuleSettings CurrentRules()
        {
            var custom = new RuleSettings
            {
                Species = RuleSettings.ParseList(SpeciesList.Value),
                KillChance = KillChance.Value,
                WitnessChance = WitnessChance.Value,
                CooldownMinutes = CooldownMinutes.Value,
                MaxPerBiome = MaxPerBiome.Value,
                MaxLevelByBiome = RuleSettings.ParseLevels(MaxLevels.Value),
                StealableHelmets = RuleSettings.ParseList(StealableHelmets.Value)
            };
            return IntensitySetting.Value == Intensity.Custom ? custom : RuleSettings.ForIntensity(IntensitySetting.Value, custom);
        }

        internal static bool IsEligible(string prefab) => RuleSettings.ParseList(SpeciesList.Value).Contains(prefab ?? "");

        // ------------------------------------------------------------------
        // Personalities file
        // ------------------------------------------------------------------

        internal static void ReloadPersonalities()
        {
            try
            {
                string defaults = PersonalityDefaults();
                if (!File.Exists(PersonalitiesFilePath)) File.WriteAllText(PersonalitiesFilePath, defaults);
                else
                {
                    string missing = LineBook.MissingSections(File.ReadAllText(PersonalitiesFilePath), defaults);
                    if (missing.Trim().Length > 0) File.AppendAllText(PersonalitiesFilePath, $"\n# ---- Added by Grudges {Version} ----\n" + missing);
                }
                Personas.Load(File.ReadAllLines(PersonalitiesFilePath));
                Log.LogInfo($"Loaded {Personas.Names.Count} personalities: {string.Join(", ", Personas.Names.ToArray())}.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't read the personalities file ({e.Message}); using the built-in ones.");
                Personas.Load(PersonalityBook.ReadDefault().Split('\n'));
            }
            Server.Rules.Personas = Personas;
            PersonasVersion++;
        }

        /// <summary>The built-in personalities, with any emote lists you'd set in an older version's config put into [Default].</summary>
        private static string PersonalityDefaults()
        {
            var old = new Dictionary<string, string>();
            void Take(string section, string key, string into)
            {
                string v = TakeOld(section, key);
                if (!string.IsNullOrEmpty(v)) old[into] = v;
            }
            Take("6 - Reveal", "Emotes: New Killer", "emotes-new-killer");
            Take("6 - Reveal", "Emotes: Witness", "emotes-witness");
            Take("6 - Reveal", "Emotes: Killed You Again", "emotes-killed-again");
            Take("6 - Reveal", "Emotes: Saw It Again", "emotes-saw-again");
            Take("6 - Reveal", "Emotes: Returns", "emotes-returned");
            Take("5 - Taunts", "Emotes: On Sight", "emotes-on-sight");
            Take("6 - Reveal", "Taunt Animations", "animations");
            return KeyValueBook.SetKeys(PersonalityBook.ReadDefault(), PersonalityBook.DefaultName, old);
        }

        /// <summary>A personality setting for a nemesis, falling back to [Default] ("" if nothing says).</summary>
        internal static string PersonaValue(Nemesis n, string key) =>
            n == null ? Personas.Get("", null, key) : Personas.Get(n.Personality, n.Species, key);

        // ------------------------------------------------------------------
        // Looks file
        // ------------------------------------------------------------------

        internal static void ReloadLooks()
        {
            try
            {
                if (!File.Exists(LooksFilePath))
                {
                    // First run of this version: start from the built-in looks, with whatever you'd tuned in the old config.
                    string text = KeyValueBook.ReadDefaultLooks();
                    text = FromOld(text, "palettes", "4 - Look", "Palettes");
                    text = FromOld(text, "scar-skin", "8d - Props", "Scar Skin");
                    text = FromOld(text, "scar-auras", "8d - Props", "Scar Auras");
                    text = FromOld(text, "props", "8d - Props", "Prop Parts");
                    text = FromOld(text, "trophies", "8 - Cheating Death & Trophies", "Trophy Items");
                    File.WriteAllText(LooksFilePath, text);
                    LooksDataVersion().Value = LooksUpgrades.Length;
                }
                else
                {
                    string have = File.ReadAllText(LooksFilePath);
                    var done = LooksDataVersion();
                    if (done.Value == 0 && have.Contains("[families]")) done.Value = 1; // a 0.8 file, from before this was tracked
                    string missing = LineBook.MissingSections(have, KeyValueBook.ReadDefaultLooks());
                    if (missing.Trim().Length > 0) File.AppendAllText(LooksFilePath, $"\n# ---- Added by Grudges {Version} ----\n" + missing);
                    // New creatures in later versions: add their lines to sections you already have, once (if you delete one, it stays deleted).
                    var defaults = new KeyValueBook().With(KeyValueBook.ReadDefaultLooks());
                    for (int step = done.Value; step < LooksUpgrades.Length; step++)
                    {
                        var check = new KeyValueBook(); check.Load(File.ReadAllLines(LooksFilePath));
                        foreach (var section in new[] { "families", "trophies" })
                        {
                            var add = new Dictionary<string, string>();
                            foreach (var kv in defaults.Section(section))
                                if (LooksUpgrades[step].Any(p => kv.Key.StartsWith(p)) && check.Get(section, kv.Key).Length == 0) add[kv.Key] = kv.Value;
                            if (add.Count > 0) File.WriteAllText(LooksFilePath, KeyValueBook.SetKeys(File.ReadAllText(LooksFilePath), section, add));
                        }
                    }
                    done.Value = LooksUpgrades.Length;
                }
                Looks.Load(File.ReadAllLines(LooksFilePath));
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't read the looks file ({e.Message}); using the built-in looks.");
                Looks.Load(KeyValueBook.ReadDefaultLooks().Split('\n'));
            }
            Kinds.Families = Looks.Section("families");
            Server.Rules.PaletteNames = PaletteNames();
            Server.Rules.PalettesFor = species => Looks.Section("palettes", species).Keys.ToList();
        }

        /// <summary>Creatures each version added, by prefab name prefix: 0.8.0 Fulings, 0.9.0 Skeletons and the Charred.</summary>
        private static readonly string[][] LooksUpgrades =
        {
            new[] { "Goblin" },
            new[] { "Skeleton", "Charred" },
        };

        private static ConfigEntry<int> _looksDataVersion;
        /// <summary>Which of those the looks file already has (kept out of sight in the config file).</summary>
        private static ConfigEntry<int> LooksDataVersion() =>
            _looksDataVersion ?? (_looksDataVersion = Instance.Config.Bind("99 - Internal", "Looks Data Version", 0,
                new ConfigDescription("Bookkeeping: which creature updates the looks file has had. Leave it alone.", null,
                    new ConfigurationManagerAttributes { Browsable = false })));

        private static string FromOld(string text, string section, string oldSection, string oldKey)
        {
            string v = TakeOld(oldSection, oldKey);
            if (string.IsNullOrEmpty(v)) return text;
            // The burnt torch prop is gone (fire scars char the skin and smoulder instead).
            var map = RuleSettings.ParseMap(v).Where(kv => !(section == "props" && kv.Key == "torch")).ToList();
            if (map.Count == 0) return text;
            string body = string.Join("\n", map.Select(kv => $"{kv.Key} = {kv.Value}").ToArray());
            return KeyValueBook.ReplaceSection(text, section, body);
        }

        internal static List<string> PaletteNames() => Looks.Section("palettes").Keys.ToList();

        internal static bool TryPalette(string name, string species, out Vector3 hsv)
        {
            hsv = Vector3.zero;
            if (string.IsNullOrEmpty(name) || !KeyValueBook.TryHsv(Looks.Get("palettes", name, species), out float h, out float s, out float v)) return false;
            hsv = new Vector3(h, s, v);
            return true;
        }

        /// <summary>Its size: its own roll within the size range, nudged by its personality.</summary>
        internal static float SizeFor(Nemesis n)
        {
            if (n == null || n.SizeRoll < 0f) return Scale.Value;
            float bias = Personas.GetFloat(n.Personality, n.Species, "size", 0f);
            float t = Mathf.Clamp01(n.SizeRoll * 0.7f + 0.15f + bias * 0.35f);
            return Mathf.Lerp(SizeMin.Value, Mathf.Max(SizeMin.Value, SizeMax.Value), t);
        }

        internal static void ReloadLines()
        {
            try
            {
                if (!File.Exists(LinesFilePath)) File.WriteAllText(LinesFilePath, LineBook.ReadDefault());
                else
                {
                    // Add sections from newer versions without touching anything you've edited.
                    string missing = LineBook.MissingSections(File.ReadAllText(LinesFilePath), LineBook.ReadDefault());
                    if (missing.Trim().Length > 0)
                    {
                        File.AppendAllText(LinesFilePath, $"\n# ---- Added by Grudges {Version} ----\n" + missing);
                        Log.LogInfo("Added new sections to your lines file.");
                    }
                }
                Lines.Load(File.ReadAllLines(LinesFilePath));
                foreach (var w in Lines.Warnings) Log.LogWarning("Lines file: " + w);
                Log.LogInfo($"Loaded {Lines.SectionCount} sections of lines from {LinesFilePath}.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't read the lines file ({e.Message}); using the built-in lines.");
                Lines.Load(LineBook.ReadDefault().Split('\n'));
            }
            Server.Rules.Lines = Lines;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        internal static void DebugLog(string msg)
        {
            if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(msg);
        }

        internal static void ShowMessage(string text)
        {
            if (MessageHud.instance == null || string.IsNullOrEmpty(text)) return;
            var type = MessagesTopLeft.Value ? MessageHud.MessageType.TopLeft : MessageHud.MessageType.Center;
            MessageHud.instance.ShowMessage(type, $"<color=#C8553D>{text}</color>");
        }

        internal static void ShowPlain(string text)
        {
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }

        private static ConfigDescription Desc(string text, int order, AcceptableValueBase range = null, bool advanced = false)
        {
            return new ConfigDescription(text, range, new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced });
        }

        /// <summary>A line of explanation at the top of a section (nothing to set).</summary>
        private static ConfigDescription Note(string text, int order)
        {
            return new ConfigDescription(text, null, new ConfigurationManagerAttributes
            {
                Order = order,
                HideDefaultButton = true,
                HideSettingName = true,
                CustomDrawer = _ => GUILayout.Label(text)
            });
        }

        private static ConfigDescription Button(string text, int order, string label, Action onClick, bool advanced = false)
        {
            return new ConfigDescription(text, null, new ConfigurationManagerAttributes
            {
                Order = order,
                IsAdvanced = advanced,
                HideDefaultButton = true,
                CustomDrawer = _ =>
                {
                    if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                    {
                        try { onClick(); }
                        catch (Exception e) { Log.LogWarning($"'{label}' failed: {e}"); }
                    }
                }
            });
        }
    }
}
