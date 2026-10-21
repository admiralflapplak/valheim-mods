using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    public enum DeliveryMode
    {
        /// <summary>He shows up in person for first-time deaths and milestones; repeats are a text message.</summary>
        SpecialOnly,
        Always,
        TextOnly
    }

    public enum TextPosition
    {
        Center,
        TopLeft,
        /// <summary>In the chat window, as if he'd typed it (it pops up like other players' messages).</summary>
        Chat,
        /// <summary>No message: he only speaks when he turns up in person.</summary>
        Off
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.fulingheckler";
        public const string Name = "Fuling Heckler";
        public const string Version = "1.8.5";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // 1 - General (settings are grouped in the F1 menu by the section names in BindConfig)
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> ChanceToComment;
        internal static ConfigEntry<bool> AlwaysCommentOnFirst;
        internal static ConfigEntry<float> CooldownSeconds;
        internal static ConfigEntry<DeliveryMode> DeliveryModeSetting;
        internal static ConfigEntry<float> BubbleSeconds;
        internal static ConfigEntry<TextPosition> TextPos;
        internal static ConfigEntry<float> DeliveryDelaySeconds;
        internal static ConfigEntry<string> Milestones;

        // 3 - Look, 5 - Visits, 8 - Smoke & Sounds
        internal static ConfigEntry<string> FulingType;
        internal static ConfigEntry<string> FulingName;
        internal static ConfigEntry<float> SayNameChance;
        internal static ConfigEntry<bool> ShareLook, EchoTest;
        internal static ConfigEntry<float> HelmetChance;
        internal static ConfigEntry<string> Helmets;
        internal static ConfigEntry<string> HelmetTestList;
        internal static ConfigEntry<string> OutfitItems;
        internal static ConfigEntry<float> OutfitPieceChance;

        // 4 - Hat Fitting
        internal static ConfigEntry<bool> FittingMode;
        internal static ConfigEntry<string> FitHelmet;
        internal static ConfigEntry<float> FitUp, FitForward, FitSide, FitTilt, FitTurn, FitSize;
        internal static ConfigEntry<string> SavedFitsText;

        // 8 - Smoke & Sounds, 3 - Look
        internal static ConfigEntry<bool> SoundsEnabled;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<bool> Recolor;
        internal static ConfigEntry<float> SkinHue, SkinSaturation, SkinBrightness;

        // 6 - Scenes, 7 - Thief & Gifter
        internal static ConfigEntry<bool> RandomScenes, LurkerEnabled, RedLightEnabled, TombstoneDancer;
        internal static ConfigEntry<float> SceneMinutesMin, SceneMinutesMax, TombstoneChance, LurkerPatience, CreepSpeed, TombstoneCatchDistance;
        internal static ConfigEntry<float> LurkerDistanceMin, LurkerDistanceMax, RedLightWatchAngle, BooDistance, LurkerNoticeDistance;
        internal static ConfigEntry<string> TombstoneDances;
        internal static ConfigEntry<bool> CopycatEnabled, CampfireEnabled, ThiefEnabled;
        internal static ConfigEntry<float> CopycatDuration, CampfireChance, CampfireCatchDistance, ThiefCoinSpacing;
        internal static ConfigEntry<int> ThiefMinCoins, ThiefAmount, ThiefFee;
        internal static ConfigEntry<string> CampfirePoses, ThiefGrabAnimation;
        internal static ConfigEntry<float> SeatUp, SeatForward;
        internal static ConfigEntry<bool> GifterEnabled, SignGifts;
        internal static ConfigEntry<int> GiftsMin, GiftsMax, GifterFreeSlots;

        /// <summary>Fired when the local player uses an emote (for the Copycat).</summary>
        internal static event Action<string> PlayerEmoted;
        internal static void RaiseEmote(string emote) => PlayerEmoted?.Invoke(emote);
        internal static ConfigEntry<float> TorchAtNightChance;
        internal static ConfigEntry<string> TorchItem;
        internal static ConfigEntry<float> HeldItemSize;
        internal static ConfigEntry<bool> TorchPose;
        internal static ConfigEntry<bool> PoofEnabled;
        internal static ConfigEntry<string> ArrivalEffects;
        internal static ConfigEntry<string> ExitEffects;
        internal static ConfigEntry<float> PoofSize;
        internal static ConfigEntry<bool> PoofSound;
        internal static ConfigEntry<float> ExitRunDistance;
        internal static ConfigEntry<float> StopDistanceMin;
        internal static ConfigEntry<float> StopDistanceMax;
        internal static ConfigEntry<float> PersonalSpace;
        internal static ConfigEntry<string> TorchRunAnimation;
        internal static ConfigEntry<string> TorchIdleAnimation;
        internal static ConfigEntry<bool> HopOnArrival;
        internal static ConfigEntry<float> TauntChance;
        internal static ConfigEntry<float> PlayDeadChance;
        internal static ConfigEntry<bool> PlayDeadOnMilestones;
        internal static ConfigEntry<string> DeathImpressions;
        internal static ConfigEntry<bool> UsePlayerEmotes;
        internal static ConfigEntry<float> EmoteShare;
        internal static ConfigEntry<string> Emotes;

        // 2 - Deaths (which deaths he comments on)
        internal static ConfigEntry<bool> EvEnvironment;
        internal static ConfigEntry<bool> EvFalling;
        internal static ConfigEntry<bool> EvTrees;
        internal static ConfigEntry<bool> EvCreatures;
        internal static ConfigEntry<bool> EvFriendlyFire;
        internal static ConfigEntry<bool> EvOther;

        // 9 - Tools
        internal static ConfigEntry<bool> DebugLogging;

        internal static QuipBook Quips;
        internal static LootTable Loot;     // the loot table that applies: the server's on a server with the mod, else your file's
        internal static string LootText;    // your loot file as read
        internal static ServerSettings Synced;
        internal static DeathMemory Memory;
        internal static Delivery Courier;

        private readonly System.Random _rng = new System.Random();
        private float _lastCommentTime = -9999f;
        private Harmony _harmony;

        internal static string LootFilePath => Path.Combine(Paths.ConfigPath, "FulingHeckler.loot.txt");
        internal static string QuipFilePath => Path.Combine(Paths.ConfigPath, "FulingHeckler.quips.txt");
        internal static string MemoryFolder => Path.Combine(Paths.ConfigPath, "FulingHeckler_memory");
        private static string OldRavensMemoryFolder => Path.Combine(Paths.ConfigPath, "RavensRemember_memory");

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            MoveOldSettings();
            Synced = new ServerSettings(Guid, Config, Logger);
            BindConfig();

            Translations.Load(this);
            Quips = new QuipBook();
            ReloadQuips();
            Localization.OnLanguageChange += ReloadQuips;   // his built-in quips follow the game's language
            Loot = new LootTable();
            ReloadLoot();
            Synced.AddText("Loot", () => LootText, UseLoot);
            Memory = new DeathMemory(MemoryFolder);
            Memory.MigrateFrom(OldRavensMemoryFolder); // keep tallies from Ravens Remember
            Scenes.FindLoadedFires();
            Courier = gameObject.AddComponent<Delivery>();
            gameObject.AddComponent<Sounds>();
            gameObject.AddComponent<Scenes>();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            PatchEmotes();
            gameObject.AddComponent<Net>();
            Net.Patch(_harmony);

            Log.LogInfo($"{Name} {Version} loaded. A Fuling is watching.");
        }

        private void Update() => Synced?.Update();

        private void OnDestroy()
        {
            Localization.OnLanguageChange -= ReloadQuips;
            Translations.Unload();
            Synced?.Dispose();
            _harmony?.UnpatchSelf();
            Scenes.Fires.Clear();
        }

        /// <summary>Up to 1.5.0 the mod's id was nick.FulingHeckler: copy that settings file to the new name once (the old one stays as a backup).</summary>
        private void MoveOldSettings()
        {
            try
            {
                string old = Path.Combine(Paths.ConfigPath, "nick.FulingHeckler.cfg");
                if (File.Exists(Config.ConfigFilePath) || !File.Exists(old)) return;
                File.Copy(old, Config.ConfigFilePath);
                Config.Reload();
                Log.LogInfo($"Settings carried over from nick.FulingHeckler.cfg to {Path.GetFileName(Config.ConfigFilePath)}.");
            }
            catch (Exception e) { Log.LogWarning($"Couldn't carry over the old settings file ({e.Message}); starting from the defaults."); }
        }

        /// <summary>Hooks the game's emote function for the Copycat. Done by hand so a mismatch only disables the Copycat.</summary>
        private void PatchEmotes()
        {
            try
            {
                var original = AccessTools.Method(typeof(Player), "StartEmote");
                if (original == null)
                {
                    Log.LogWarning("Copycat: couldn't find the game's emote function, so he can't copy you.");
                    return;
                }
                _harmony.Patch(original, postfix: new HarmonyMethod(typeof(EmoteHook), nameof(EmoteHook.Postfix)));
            }
            catch (Exception e)
            {
                Log.LogWarning($"Copycat: couldn't hook emotes ({e.Message}), so he can't copy you.");
            }
        }

        // ------------------------------------------------------------------
        // Config
        // ------------------------------------------------------------------

        private void BindConfig()
        {
            Enabled = Config.Bind("1 - General", "Enabled", true, Desc("Turn the Fuling on or off.", 1000));
            ChanceToComment = Config.Bind("1 - General", "Chance To Comment", 0.6f,
                Desc("Chance (0-1) that the Fuling comments on a repeat death. Lower = rarer, more surprising.",
                    980, new AcceptableValueRange<float>(0f, 1f)));
            AlwaysCommentOnFirst = Config.Bind("1 - General", "Always Comment On First Time", true,
                Desc("The first time you die a new way, he always shows up (ignores chance and cooldown).", 970));
            CooldownSeconds = Config.Bind("1 - General", "Cooldown Seconds", 90f,
                Desc("Minimum seconds between visits. First-time deaths and milestones ignore this.",
                    960, new AcceptableValueRange<float>(0f, 900f)));
            DeliveryModeSetting = Config.Bind("1 - General", "Delivery", DeliveryMode.SpecialOnly,
                Desc("When he shows up in person instead of a text message.\n" +
                     "SpecialOnly: first-time deaths and milestones; repeats are a text message.\n" +
                     "Always: every comment.\n" +
                     "TextOnly: never, text messages only.", 990));
            BubbleSeconds = Config.Bind("1 - General", "Bubble Seconds", 8f,
                Desc("How long his speech bubble stays up before he runs off.", 930,
                    new AcceptableValueRange<float>(4f, 30f)));
            TextPos = Config.Bind("1 - General", "Text Position", TextPosition.Chat,
                Desc("Where his line goes when he doesn't come in person (see 'Delivery'). Chat: in the chat window, as if he'd typed it. " +
                     "TopLeft: the small messages corner. Center: big, mid-screen. Off: no message at all.", 920));
            DeliveryDelaySeconds = Config.Bind("1 - General", "Delay After Respawn", 7f,
                Desc("Seconds after you respawn before he shows up.", 950,
                    new AcceptableValueRange<float>(0f, 30f)));
            Milestones = Config.Bind("1 - General", "Milestones", "5,10,25,50,100",
                Desc("Death counts (per death type) where he always shows up to celebrate. Comma separated.",
                    940, advanced: true));

            FulingType = Config.Bind("3 - Look", "Fuling Type", "Goblin",
                Desc("Which goblin body to borrow.", 980,
                    new AcceptableValueList<string>("Goblin", "GoblinShaman", "GoblinBrute", "GoblinArcher")));
            FulingName = Config.Bind("3 - Look", "Fuling Name", "Rocko",
                Desc("Name shown on his speech bubble. Give him a name, he'd like that.", 1000));
            SayNameChance = Config.Bind("3 - Look", "Say His Name Chance", 0.5f,
                Desc("When a line has {me} in it, the chance (0-1) he says his own name instead of just 'Fuling'. " +
                     "0 = always 'Fuling', 1 = always his name. Only matters if you've renamed him.", 990,
                    new AcceptableValueRange<float>(0f, 1f)));
            ShareLook = Config.Bind("1 - General", "Share Goblin On My Server", true,
                Desc("Only matters on a server (or when you host). Players who join with the mod get YOUR goblin (name, body, colour, hats, outfit, torch) " +
                     "and see each other's goblin visits: his heckles and scenes show up for everyone nearby, and anyone can spot or catch him. " +
                     "Their own look settings come back on other worlds. Off = everyone keeps their own goblin, and visits stay private.", 1000));
            PoofEnabled = Config.Bind("8 - Smoke & Sounds", "Smoke Puff", true,
                Desc("He appears and vanishes in a puff of smoke (borrowed from the game's own effects).", 1000));
            ExitRunDistance = Config.Bind("5 - Visits", "Run Before Vanishing", 15f,
                Desc("How far (metres) he runs off before he vanishes in smoke. Keep it short so you see the puff.", 900,
                    new AcceptableValueRange<float>(2f, 25f)));
            PoofSound = Config.Bind("8 - Smoke & Sounds", "Smoke Puff Sound", true, Desc("Play the effect's own sound, if it has one.", 980));
            ArrivalEffects = Config.Bind("8 - Smoke & Sounds", "Arrival Effects", "vfx_bush_leaf_puff, vfx_lootspawn, vfx_DraugrSpawn, fx_seeker_spawn",
                Desc("Game effects played when he appears. List several (comma separated) and they all play at once, layered. " +
                     "The log line 'Effects you could use' lists real names, e.g. vfx_bush_leaf_puff, vfx_ghost_death, vfx_spawn_large.", 970));
            ExitEffects = Config.Bind("8 - Smoke & Sounds", "Exit Effects", "vfx_bush_leaf_puff, vfx_lootspawn, vfx_DraugrSpawn, fx_seeker_spawn",
                Desc("Game effects played when he vanishes. Same rules as 'Arrival Effects'.", 960));
            PoofSize = Config.Bind("8 - Smoke & Sounds", "Smoke Puff Size", 1f,
                Desc("Size multiplier for the puff.", 990, new AcceptableValueRange<float>(0.2f, 3f), advanced: true));
            StopDistanceMin = Config.Bind("5 - Visits", "Stop Distance Min", 5f,
                Desc("Closest he stops to talk to you, in metres. Each visit picks a spot between min and max.", 1000,
                    new AcceptableValueRange<float>(2f, 15f)));
            StopDistanceMax = Config.Bind("5 - Visits", "Stop Distance Max", 8f,
                Desc("Furthest he stops to talk to you, in metres.", 990, new AcceptableValueRange<float>(2f, 15f)));
            PersonalSpace = Config.Bind("5 - Visits", "Personal Space", 2.5f,
                Desc("Walk closer than this (metres) while he's talking and he cuts himself off with a 'Gotta go!' and runs. 0 = never.",
                    980, new AcceptableValueRange<float>(0f, 6f)));
            HelmetChance = Config.Bind("3 - Look", "Helmet Chance", 1f,
                Desc("Chance (0-1) he turns up wearing something on his head.", 930, new AcceptableValueRange<float>(0f, 1f)));
            Helmets = Config.Bind("3 - Look", "Helmet Items", "HelmetFishingHat",
                Desc("Comma-separated item names he picks from for his head. The log line 'Goblin can carry:' lists the Fuling's own gear. " +
                     "Gear made for Fulings fits; player gear is made for a different skeleton and will probably look broken.", 920));
            HelmetTestList = Config.Bind("4 - Hat Fitting", "Helmet Test List",
                "GoblinHelmet, HelmetMidsummerCrown, HelmetYule, HelmetPointyHat, HelmetFishingHat, HelmetLeather, HelmetTrollLeather, " +
                "HelmetBronze, HelmetIron, HelmetDrake, HelmetPadded, HelmetFenring, HelmetRoot, HelmetDverger, HelmetOdin, HelmetMage",
                Desc("Helmets listed in the Hat Fitting dropdown. Restart the game after changing this.", 890, advanced: true));
            OutfitPieceChance = Config.Bind("3 - Look", "Outfit Piece Chance", 1f,
                Desc("Chance (0-1), per piece, that he wears each item in 'Outfit Items'. 0 = always naked (apart from the helmet).", 910,
                    new AcceptableValueRange<float>(0f, 1f)));
            OutfitItems = Config.Bind("3 - Look", "Outfit Items", "GoblinLegband",
                Desc("Goblin armour pieces he might wear. Fuling gear fits his body; player armour won't.", 900));
            TorchAtNightChance = Config.Bind("3 - Look", "Torch At Night Chance", 0.5f,
                Desc("Chance (0-1) he carries a torch when it's night.", 890, new AcceptableValueRange<float>(0f, 1f)));
            TorchItem = Config.Bind("3 - Look", "Torch Item", "GoblinTorch",
                Desc("What he carries at night. Fuling items fit his hand best: 'GoblinTorch', 'GoblinClub', 'GoblinSpear', 'GoblinSword'.", 880, advanced: true));
            HeldItemSize = Config.Bind("3 - Look", "Held Item Size", 1f,
                Desc("Size multiplier for what he holds (the torch). 1 = the item's real size.", 870,
                    new AcceptableValueRange<float>(0.1f, 3f), advanced: true));
            TorchPose = Config.Bind("3 - Look", "Torch Pose", true,
                Desc("When he carries a torch, he uses the player's torch-carrying run and idle so he holds it up properly.", 860));
            TorchRunAnimation = Config.Bind("3 - Look", "Torch Run Animation", "Run New Torch Right",
                Desc("Player animation used for running with the torch. Names are in the 'Player animations' log line.", 850, advanced: true));
            TorchIdleAnimation = Config.Bind("3 - Look", "Torch Idle Animation", "Standing Torch Idle right",
                Desc("Player animation used for standing with the torch.", 840, advanced: true));
            HopOnArrival = Config.Bind("5 - Visits", "Hop On Arrival", false, Desc("He does a little jump when he reaches you.", 970));
            TauntChance = Config.Bind("5 - Visits", "Taunt Chance", 1f,
                Desc("Chance (0-1) of a mock swing, stagger or throw after his line.", 960, new AcceptableValueRange<float>(0f, 1f)));
            PlayDeadChance = Config.Bind("5 - Visits", "Play Dead Chance", 0.12f,
                Desc("Chance (0-1) he acts out your death: an impression of how you died (see 'Death Impressions'), or he flops over and gets back up.", 920, new AcceptableValueRange<float>(0f, 1f)));
            DeathImpressions = Config.Bind("5 - Visits", "Death Impressions",
                "fall: hard landing, slipping on ice | drowning: swimming, treading water | burning: despair, cry | lava: despair, cry | incinerator: shrug, despair | freezing: cower, despair | " +
                "poison: dying backwards, despair | smoke: cower, dying backwards | tree: slipping on ice, dying backwards | stalactite: hard landing, cower | " +
                "self: shrug, despair | friend: point, nonono | boss: kneeldeath, cower | enemy: dying backwards, kneeldeath, cower | generic: dying backwards, kneeldeath",
                Desc("How he acts out each kind of death: 'kind: animation, animation | ...'. Kinds are the same as the quips file's events " +
                     "(enemy:troll, boss, enemy, fall, drowning, burning, tree, friend, self... generic). The most specific match wins. " +
                     "Leave a kind out and he falls back to the next one, ending at 'generic'.", 915, advanced: true));
            PlayDeadOnMilestones = Config.Bind("5 - Visits", "Play Dead On Milestones", true,
                Desc("Always act out your death on milestone deaths.", 910));

            UsePlayerEmotes = Config.Bind("5 - Visits", "Use Player Emotes", true,
                Desc("He can perform the player's emotes (laugh, point...) as taunts.", 950));
            EmoteShare = Config.Bind("5 - Visits", "Emote Share", 1f,
                Desc("When he taunts, chance (0-1) it's a player emote rather than one of his own moves (swing, stagger, throw).",
                    940, new AcceptableValueRange<float>(0f, 1f)));
            Emotes = Config.Bind("5 - Visits", "Emote Names", "laugh, point, cheer, flex, thumbsup, headbang, challenge, blowkiss, bow, shrug, nonono, lisa dance, toast, love you, kickstep, comehere",
                Desc("Which player animations he taunts with (picked at random). These are animation names, not emote commands: some emotes (like /vibe or /relax) use animations " +
                     "with different names. Any part of a name works. The log line 'Player animations' lists every name; 'Test next emote' (9 - Tools) tries them one by one.",
                    930));

            EvEnvironment = Config.Bind("2 - Deaths", "Environment", true,
                Desc("Drowning, burning, freezing, poison, smoke, the edge of the world, and other things nature does to you.", 910));
            EvFalling = Config.Bind("2 - Deaths", "Falling", true, Desc("Fall damage deaths.", 900));
            EvTrees = Config.Bind("2 - Deaths", "Trees", true, Desc("Death by your own lumberjacking.", 890));
            EvCreatures = Config.Bind("2 - Deaths", "Creatures", true, Desc("Killed by creatures, from deer to bosses.", 880));
            EvFriendlyFire = Config.Bind("2 - Deaths", "Friendly Fire", true, Desc("Killed by another player (or yourself).", 870));
            EvOther = Config.Bind("2 - Deaths", "Other", true, Desc("Carts, boats, collapsing buildings, and anything unrecognised.", 860));

            Config.Bind("9 - Tools", "Reload Quips", "",
                Button("Re-read the quips file after editing it. No restart needed.", 930, "Reload quips file", () =>
                {
                    ReloadQuips();
                    ShowToolFeedback($"Loaded {Quips.Count} quips.");
                }));
            Config.Bind("9 - Tools", "Test Fuling", "",
                Button("Make the Fuling run over and heckle you right now.", 1000, "Summon Fuling", () => TestSummon(false, null)));
            Config.Bind("9 - Tools", "Test Torch", "",
                Button("Summon him carrying his torch, day or night.", 990, "Summon Fuling with torch", () => TestSummon(false, true)));
            Config.Bind("9 - Tools", "Emote Test", "",
                Button("Experiment: can he play the player's emotes? He tries the player's wave. Results go in the log.",
                    960, "Run emote test", () => TestSummon(true, null)));
            Config.Bind("9 - Tools", "Test Next Emote", "",
                Button("Summons him and performs the next name from 'Emote Names', in order, with no dice. The log says which one.",
                    970, "Test next emote", TestNextEmote));
            Config.Bind("9 - Tools", "Test Poof", "",
                Button("Plays the 'Arrival Effects' a few metres in front of you, to preview them.", 950, "Test arrival puff",
                    () => TestPuff(ArrivalEffects.Value)));
            Config.Bind("9 - Tools", "Test Exit Poof", "",
                Button("Plays the 'Exit Effects' a few metres in front of you, to preview them.", 940, "Test exit puff",
                    () => TestPuff(ExitEffects.Value)));
            Config.Bind("9 - Tools", "Test Outfit", "",
                Button("Summon him wearing every outfit piece, a helmet and a torch.", 980, "Summon in full outfit", () =>
                {
                    var p = Player.m_localPlayer;
                    if (p == null) { ShowToolFeedback("Load into a world first."); return; }
                    var helmets = NameList.Parse(Helmets.Value);
                    Courier.Deliver(p, "Fuling dressed up! Fuling fancy!", special: true, milestone: false, isTest: true,
                        forceTorch: true, forcedHelmet: helmets.Count > 0 ? helmets[0] : null, forceOutfit: true);
                }));
            Config.Bind("9 - Tools", "Reset Memory", "",
                Button("Wipe the Fuling's death tallies for your current character. Every death becomes a first again.",
                    920, "Make Fuling forget", () =>
                    {
                        var p = Player.m_localPlayer;
                        if (p == null) { ShowToolFeedback("Load into a world first."); return; }
                        Memory.Reset(p.GetPlayerName());
                        ShowToolFeedback("The Fuling has forgotten everything. He looks confused.");
                    }, advanced: true));
            DebugLogging = Config.Bind("9 - Tools", "Debug Logging", false,
                Desc("Log every death classification and dice roll to the BepInEx console.", 910, advanced: true));
            EchoTest = Config.Bind("9 - Tools", "Echo Test", false,
                Desc("For testing server sharing alone. On a server that has the mod, the server sends your own goblin's messages back to you, " +
                     "so a second copy marked '(echo)' appears beside him, doing exactly what your friends would see. " +
                     "Looking at or walking up to the echo copy counts as a friend spotting him. While it's on, real friends DON'T see your goblin. Turn off when done.", 990, advanced: true));

            FittingMode = Config.Bind("4 - Hat Fitting", "Fitting Mode", false,
                Desc("Tick to have him walk up and stand still in front of you. Untick when you're done and he runs off.", 1000));
            var helmetChoices = NameList.Parse(HelmetTestList.Value);
            if (helmetChoices.Count == 0) helmetChoices.Add("GoblinHelmet");
            FitHelmet = Config.Bind("4 - Hat Fitting", "Helmet", helmetChoices.Contains("HelmetFishingHat") ? "HelmetFishingHat" : helmetChoices[0],
                Desc("The helmet he's wearing while you fit it. Pick one and he puts it on. (List comes from 'Helmet Test List'; restart to refresh it.)",
                    990, new AcceptableValueList<string>(helmetChoices.Distinct().ToArray())));
            FitUp = Config.Bind("4 - Hat Fitting", "Up", 0f, Desc("Move the helmet up (+) or down (-), in metres. Saves automatically.", 980, new AcceptableValueRange<float>(-0.6f, 0.6f)));
            FitForward = Config.Bind("4 - Hat Fitting", "Forward", 0f, Desc("Move it forward (+) or back (-). Saves automatically.", 970, new AcceptableValueRange<float>(-0.6f, 0.6f)));
            FitSide = Config.Bind("4 - Hat Fitting", "Side", 0f, Desc("Move it to his left (-) or right (+). Saves automatically.", 960, new AcceptableValueRange<float>(-0.4f, 0.4f)));
            FitTilt = Config.Bind("4 - Hat Fitting", "Tilt", 0f, Desc("Tip it forward or back, in degrees. Saves automatically.", 950, new AcceptableValueRange<float>(-180f, 180f)));
            FitTurn = Config.Bind("4 - Hat Fitting", "Turn", 0f, Desc("Turn it left or right, in degrees. Saves automatically.", 940, new AcceptableValueRange<float>(-180f, 180f)));
            FitSize = Config.Bind("4 - Hat Fitting", "Size", 1f, Desc("Make it bigger or smaller. Saves automatically.", 930, new AcceptableValueRange<float>(0.2f, 3f)));
            Config.Bind("4 - Hat Fitting", "Use For All", "",
                Button("Copy this helmet's fit to every helmet that hasn't been fitted yet. Most player helmets are off by the same amount, so fit one, then press this.",
                    920, "Use this fit for all other helmets", () => SaveFit(true)));
            Config.Bind("4 - Hat Fitting", "Reset This", "",
                Button("Forget this helmet's own fit, so it goes back to the shared one.", 910, "Reset this helmet", ClearFit));
            SavedFitsText = Config.Bind("4 - Hat Fitting", "Saved Fits", BuiltInFitsText,
                Desc("Saved helmet fits: 'name: up, forward, side, tilt, turn, size | ...'. Written automatically; can be shared with friends. " +
                     "The mod's own fits (fishing hat etc.) always sit underneath, so emptying this brings them back.",
                    900, advanced: true));

            SoundsEnabled = Config.Bind("8 - Smoke & Sounds", "Custom Sounds", true,
                Desc("Play your own recordings from the sounds folder (arrive, taunt, poof, flee).", 950));
            SoundVolume = Config.Bind("8 - Smoke & Sounds", "Custom Sounds Volume", 1f,
                Desc("Volume of your recordings (on top of the game's own volume).", 940, new AcceptableValueRange<float>(0f, 2f)));
            Config.Bind("8 - Smoke & Sounds", "Open Sounds Folder", "",
                Button("Opens the sounds folder. Its README.txt lists every folder (arrive, taunt, poof, flee, dance, spotted, boo...).", 930, "Open sounds folder", () =>
                {
                    Directory.CreateDirectory(Sounds.Folder);
                    Application.OpenURL("file:///" + Sounds.Folder.Replace('\\', '/'));
                }));
            Config.Bind("8 - Smoke & Sounds", "Reload Sounds", "",
                Button("Re-read the sounds folder after adding or changing recordings. No restart needed.", 920, "Reload sounds", () =>
                {
                    Sounds.Instance?.Reload();
                    ShowToolFeedback("Reloading sounds... (the log lists how many loaded)");
                }));

            Recolor = Config.Bind("3 - Look", "Recolour", true,
                Desc("Change his skin colour. Tick 'Fitting Mode' in 4 - Hat Fitting to have him stand still while you adjust.", 970));
            SkinHue = Config.Bind("3 - Look", "Hue", 0.364f,
                Desc("Shift his colour around the colour wheel.", 960, new AcceptableValueRange<float>(-0.5f, 0.5f)));
            SkinSaturation = Config.Bind("3 - Look", "Saturation", -0.158f,
                Desc("More colourful (+) or greyer (-).", 950, new AcceptableValueRange<float>(-1f, 1f)));
            SkinBrightness = Config.Bind("3 - Look", "Brightness", 0f,
                Desc("Lighter (+) or darker (-).", 940, new AcceptableValueRange<float>(-1f, 1f)));

            RandomScenes = Config.Bind("6 - Scenes", "Random Scenes", true,
                Desc("Every so often he turns up uninvited (Lurker, Red Light Green Light). Never during fights, on boats, in water or dungeons.", 1000));
            SceneMinutesMin = Config.Bind("6 - Scenes", "Minutes Between (Min)", 20f,
                Desc("Shortest wait between random scenes, in minutes of play.", 990, new AcceptableValueRange<float>(1f, 180f)));
            SceneMinutesMax = Config.Bind("6 - Scenes", "Minutes Between (Max)", 40f,
                Desc("Longest wait between random scenes, in minutes of play.", 980, new AcceptableValueRange<float>(1f, 180f)));
            LurkerEnabled = Config.Bind("6 - Scenes", "Lurker", true,
                Desc("He stares at you from off to the side. Look right at him and he flinches and bolts.", 970));
            LurkerDistanceMin = Config.Bind("6 - Scenes", "Lurker Distance (Min)", 22f,
                Desc("Closest the Lurker appears, in metres.", 960, new AcceptableValueRange<float>(5f, 60f)));
            LurkerDistanceMax = Config.Bind("6 - Scenes", "Lurker Distance (Max)", 30f,
                Desc("Furthest the Lurker appears, in metres.", 950, new AcceptableValueRange<float>(5f, 60f)));
            LurkerNoticeDistance = Config.Bind("6 - Scenes", "Lurker Notice Distance", 25f,
                Desc("You only 'spot' the Lurker within this many metres, looking right at him with nothing in the way. " +
                     "Lower it if he bolts before you could really see him. (Walking within 7 m always spooks him.)", 940,
                    new AcceptableValueRange<float>(8f, 60f)));
            RedLightEnabled = Config.Bind("6 - Scenes", "Red Light Green Light", true,
                Desc("He creeps up behind you, freezing mid-pose whenever you look. If he reaches you: BOO.", 910));
            TombstoneDancer = Config.Bind("6 - Scenes", "Tombstone Dancer", true,
                Desc("When you head back to where you died, he might be dancing on your grave.", 830));
            TombstoneChance = Config.Bind("6 - Scenes", "Tombstone Dancer Chance", 0.6f,
                Desc("Chance (0-1) he's there when you go back for your things.", 820, new AcceptableValueRange<float>(0f, 1f)));
            LurkerPatience = Config.Bind("6 - Scenes", "Lurker Patience", 45f,
                Desc("Seconds the Lurker waits to be noticed before he gets bored and leaves.", 930,
                    new AcceptableValueRange<float>(10f, 180f), advanced: true));
            BooDistance = Config.Bind("6 - Scenes", "BOO Distance", 2.5f,
                Desc("In Red Light Green Light, how close (metres) he gets before he's ready to BOO you.", 900,
                    new AcceptableValueRange<float>(1.5f, 10f)));
            RedLightWatchAngle = Config.Bind("6 - Scenes", "Red Light Watch Angle", 60f,
                Desc("In Red Light Green Light, he counts as 'watched' (and freezes) when he's within this many degrees of where your character faces. " +
                     "Smaller = you have to face him more directly.", 890, new AcceptableValueRange<float>(20f, 90f), advanced: true));
            CreepSpeed = Config.Bind("6 - Scenes", "Creep Speed", 5f,
                Desc("How fast he moves in Red Light Green Light (metres per second).", 880,
                    new AcceptableValueRange<float>(1f, 10f), advanced: true));
            TombstoneCatchDistance = Config.Bind("6 - Scenes", "Tombstone Catch Distance", 10f,
                Desc("How close you get to the dancing Fuling before he notices you and runs.", 810,
                    new AcceptableValueRange<float>(3f, 20f), advanced: true));
            TombstoneDances = Config.Bind("6 - Scenes", "Tombstone Dances", "Dance, Dance 2, Dance 3, Headbang, Lisa Dance",
                Desc("Player animations he dances with on your grave.", 800, advanced: true));
            CopycatEnabled = Config.Bind("6 - Scenes", "Copycat", true,
                Desc("He appears and copies your emotes from a distance. Always leaves on his own after a while. Don't point at him.", 860));
            CopycatDuration = Config.Bind("6 - Scenes", "Copycat Time Limit", 40f,
                Desc("Seconds before the Copycat waves goodbye and leaves, no matter what.", 850, new AcceptableValueRange<float>(10f, 180f)));
            CampfireEnabled = Config.Bind("6 - Scenes", "Campfire Squatter", true,
                Desc("At night, he might be sitting by a lit fire as you come back to it. He bolts when you get close.", 780));
            CampfireChance = Config.Bind("6 - Scenes", "Campfire Squatter Chance", 0.4f,
                Desc("Chance (0-1) he's there when you approach a lit fire at night (checked at most once every 20 minutes).", 770,
                    new AcceptableValueRange<float>(0f, 1f)));
            CampfireCatchDistance = Config.Bind("6 - Scenes", "Campfire Catch Distance", 12f,
                Desc("How close you get before he notices you and runs.", 760, new AcceptableValueRange<float>(3f, 25f), advanced: true));
            CampfirePoses = Config.Bind("6 - Scenes", "Campfire Poses", "Sitting idle",
                Desc("Player animations he uses while sitting on the ground by the fire (with no seat nearby).", 750, advanced: true));
            SeatUp = Config.Bind("6 - Scenes", "Seat Height", 0.1f,
                Desc("On a seat by the fire: how far (metres) above the seat's own spot he sits. The sitting pose was made for a player, " +
                     "who is taller. Changes show live while he's sitting.", 740, new AcceptableValueRange<float>(-0.5f, 0.5f), advanced: true));
            SeatForward = Config.Bind("6 - Scenes", "Seat Forward", 0.1f,
                Desc("On a seat by the fire: how far (metres) forward from the seat's own spot he sits. Changes show live while he's sitting.",
                    730, new AcceptableValueRange<float>(-0.5f, 0.5f), advanced: true));
            ThiefEnabled = Config.Bind("7 - Thief & Gifter", "Coin Thief", false,
                Desc("OFF by default. He grabs some of your coins and runs, dropping them one by one behind him so you can get them back. " +
                     "The dropped coins are real items that other players can see and pick up.", 1000));
            ThiefMinCoins = Config.Bind("7 - Thief & Gifter", "Coin Thief Minimum", 50,
                Desc("He only tries it if you're carrying at least this many coins.", 990, new AcceptableValueRange<int>(1, 1000)));
            ThiefAmount = Config.Bind("7 - Thief & Gifter", "Coin Thief Amount", 10,
                Desc("How many coins he grabs (and drops along his path).", 980, new AcceptableValueRange<int>(1, 50)));
            ThiefFee = Synced.Add(Config.Bind("7 - Thief & Gifter", "Coin Thief Fee", 0,
                Desc("Coins he keeps for himself at the end. 0 = he drops every coin back." + ServerMark, 970, new AcceptableValueRange<int>(0, 50))));
            ThiefGrabAnimation = Config.Bind("7 - Thief & Gifter", "Thief Grab Animation", "Interact_forward",
                Desc("What he does as he snatches your coins (and as the Gifter slips things into your pockets). 'none' = nothing; 'jump', 'stagger', 'swing_longsword' or 'spear_throw' = his own moves; " +
                     "or any player animation name from the 'Player animations' log line (e.g. Interact_forward, Laugh 2, Cheer, Pickaxe...). " +
                     "Changes apply on his next visit; use 'Test Coin Thief' to try them.", 960));
            ThiefCoinSpacing = Config.Bind("7 - Thief & Gifter", "Coin Trail Spacing", 3f,
                Desc("Metres between dropped coins.", 950, new AcceptableValueRange<float>(1f, 8f), advanced: true));
            GifterEnabled = Synced.Add(Config.Bind("7 - Thief & Gifter", "Gifter", false,
                Desc("OFF by default. He sneaks up from behind and slips a few things into your pockets: mostly junk, now and then something nice. " +
                     "Turn around and catch him and he drops them on the ground and bolts. What he gives is in FulingHeckler.loot.txt " +
                     "(on a server with the mod, the server's loot table)." + ServerMark, 930)));
            GiftsMin = Synced.Add(Config.Bind("7 - Thief & Gifter", "Gifter Gifts Min", 1,
                Desc("Fewest things he slips you per visit." + ServerMark, 920, new AcceptableValueRange<int>(1, 6))));
            GiftsMax = Synced.Add(Config.Bind("7 - Thief & Gifter", "Gifter Gifts Max", 3,
                Desc("Most things he slips you per visit." + ServerMark, 910, new AcceptableValueRange<int>(1, 6))));
            GifterFreeSlots = Config.Bind("7 - Thief & Gifter", "Gifter Free Slots Needed", 5,
                Desc("He only visits if you have at least this many empty inventory slots, so he never fills you up. " +
                     "He also stays away while you're heading back to your grave, and never pushes you over your carry weight.", 900,
                    new AcceptableValueRange<int>(1, 20)));
            SignGifts = Config.Bind("7 - Thief & Gifter", "Sign His Gifts", true,
                Desc("Equipment he gives you says 'Crafted by' his name. (Only equipment; stackable things can't carry a signature.)", 890));
            Config.Bind("7 - Thief & Gifter", "Reload Loot Table", "",
                Button("Re-read FulingHeckler.loot.txt (in BepInEx/config) after editing it. Delete the file to get the defaults back.", 880,
                    "Reload loot table", () => { ReloadLoot(); ShowToolFeedback($"Loot table: {Loot.Entries.Count} entries" +
                        (Loot.Warnings.Count > 0 ? $", {Loot.Warnings.Count} problem line(s) (see log)." : ".") +
                        (Synced.TextFromServer("Loot") ? " This server uses its own loot table, though." : "")); }));
            Config.Bind("6 - Scenes", "Test Lurker", "",
                Button("Start the Lurker now. He appears off to your side; turn and look right at him.", 920, "Test Lurker",
                    () => TestScene(SceneKind.Lurker)));
            Config.Bind("6 - Scenes", "Test Red Light", "",
                Button("Start Red Light Green Light now. He appears behind you. Close the menu and keep walking!", 870, "Test Red Light Green Light",
                    () => TestScene(SceneKind.RedLight)));
            Config.Bind("6 - Scenes", "Test Tombstone", "",
                Button("He dances on a pretend grave 30 m in front of you. Walk up to him.", 790, "Test Tombstone Dancer",
                    () => TestScene(SceneKind.Tombstone)));

            Config.Bind("6 - Scenes", "Test Copycat", "",
                Button("Start the Copycat now. Use emotes (/wave, /dance, /flex...) and watch him copy you.", 840, "Test Copycat",
                    () => TestScene(SceneKind.Copycat)));
            Config.Bind("6 - Scenes", "Test Campfire", "",
                Button("He sits by the nearest lit fire (within 60 m), or a pretend one 20 m ahead. Walk up to him.", 740, "Test Campfire Squatter",
                    () => TestScene(SceneKind.Campfire)));
            Config.Bind("7 - Thief & Gifter", "Test Thief", "",
                Button("Start the Coin Thief now (works even if Coin Thief is off, but you need coins). Your coins drop along his path.", 940,
                    "Test Coin Thief", () => TestScene(SceneKind.CoinThief)));
            Config.Bind("7 - Thief & Gifter", "Test Gifter", "",
                Button("Start the Gifter now (works even if Gifter is off). He appears behind you: keep walking to get gifts, or turn around to catch him.", 870,
                    "Test Gifter", () => TestScene(SceneKind.Gifter)));

            CarryOverMovedSettings();   // before the controls are wired up: bringing old values along mustn't set anything off

            // Wire the controls up.
            FittingMode.SettingChanged += (_, __) => OnFittingModeChanged();
            FitHelmet.SettingChanged += (_, __) => OnFitHelmetChanged();
            foreach (var slider in new[] { FitUp, FitForward, FitSide, FitTilt, FitTurn, FitSize })
                slider.SettingChanged += (_, __) => OnSliderChanged();
            if (FittingMode.Value) FittingMode.Value = false; // never start a session already in fitting mode
            Tidy();
        }

        /// <summary>
        /// The F1 menu, in the order it shows, section by section; "*" = only with "Advanced settings" ticked. One place to reorganise it
        /// (the order and Advanced given at each Bind are overruled here).
        /// </summary>
        private static readonly string[] Layout =
        {
            // 1 - General
            "1 - General/Enabled", "1 - General/Delivery", "1 - General/Text Position", "1 - General/Chance To Comment",
            "1 - General/Cooldown Seconds", "1 - General/Share Goblin On My Server", "1 - General/Always Comment On First Time*", "1 - General/Delay After Respawn*",
            "1 - General/Bubble Seconds*", "1 - General/Milestones*",
            // 2 - Deaths
            "2 - Deaths/Environment", "2 - Deaths/Falling", "2 - Deaths/Trees", "2 - Deaths/Creatures",
            "2 - Deaths/Friendly Fire", "2 - Deaths/Other",
            // 3 - Look
            "3 - Look/Fuling Name", "3 - Look/Say His Name Chance", "3 - Look/Fuling Type", "3 - Look/Recolour",
            "3 - Look/Hue", "3 - Look/Saturation", "3 - Look/Brightness", "3 - Look/Helmet Chance",
            "3 - Look/Helmet Items", "3 - Look/Outfit Piece Chance", "3 - Look/Outfit Items", "3 - Look/Torch At Night Chance",
            "3 - Look/Torch Item", "3 - Look/Held Item Size*", "3 - Look/Torch Pose*", "3 - Look/Torch Run Animation*",
            "3 - Look/Torch Idle Animation*",
            // 4 - Hat Fitting
            "4 - Hat Fitting/Fitting Mode", "4 - Hat Fitting/Helmet", "4 - Hat Fitting/Up", "4 - Hat Fitting/Forward",
            "4 - Hat Fitting/Side", "4 - Hat Fitting/Tilt", "4 - Hat Fitting/Turn", "4 - Hat Fitting/Size",
            "4 - Hat Fitting/Use For All", "4 - Hat Fitting/Reset This", "4 - Hat Fitting/Saved Fits*", "4 - Hat Fitting/Helmet Test List*",
            // 5 - Visits
            "5 - Visits/Personal Space", "5 - Visits/Taunt Chance", "5 - Visits/Hop On Arrival", "5 - Visits/Play Dead Chance",
            "5 - Visits/Play Dead On Milestones", "5 - Visits/Stop Distance Min*", "5 - Visits/Stop Distance Max*", "5 - Visits/Run Before Vanishing*",
            "5 - Visits/Use Player Emotes*", "5 - Visits/Emote Share*", "5 - Visits/Emote Names*", "5 - Visits/Death Impressions*",
            // 6 - Scenes
            "6 - Scenes/Random Scenes", "6 - Scenes/Minutes Between (Min)", "6 - Scenes/Minutes Between (Max)", "6 - Scenes/Lurker",
            "6 - Scenes/Test Lurker", "6 - Scenes/Lurker Distance (Min)*", "6 - Scenes/Lurker Distance (Max)*", "6 - Scenes/Lurker Notice Distance*",
            "6 - Scenes/Lurker Patience*", "6 - Scenes/Red Light Green Light", "6 - Scenes/Test Red Light", "6 - Scenes/BOO Distance*",
            "6 - Scenes/Red Light Watch Angle*", "6 - Scenes/Creep Speed*", "6 - Scenes/Copycat", "6 - Scenes/Test Copycat",
            "6 - Scenes/Copycat Time Limit*", "6 - Scenes/Tombstone Dancer", "6 - Scenes/Test Tombstone", "6 - Scenes/Tombstone Dancer Chance",
            "6 - Scenes/Tombstone Catch Distance*", "6 - Scenes/Tombstone Dances*", "6 - Scenes/Campfire Squatter", "6 - Scenes/Test Campfire",
            "6 - Scenes/Campfire Squatter Chance", "6 - Scenes/Campfire Catch Distance*", "6 - Scenes/Campfire Poses*", "6 - Scenes/Seat Height*", "6 - Scenes/Seat Forward*",
            // 7 - Thief & Gifter
            "7 - Thief & Gifter/Coin Thief", "7 - Thief & Gifter/Test Thief", "7 - Thief & Gifter/Coin Thief Minimum", "7 - Thief & Gifter/Coin Thief Amount",
            "7 - Thief & Gifter/Coin Thief Fee", "7 - Thief & Gifter/Gifter", "7 - Thief & Gifter/Test Gifter", "7 - Thief & Gifter/Gifter Gifts Min",
            "7 - Thief & Gifter/Gifter Gifts Max", "7 - Thief & Gifter/Gifter Free Slots Needed", "7 - Thief & Gifter/Reload Loot Table", "7 - Thief & Gifter/Sign His Gifts*",
            "7 - Thief & Gifter/Thief Grab Animation*", "7 - Thief & Gifter/Coin Trail Spacing*",
            // 8 - Smoke & Sounds
            "8 - Smoke & Sounds/Smoke Puff", "8 - Smoke & Sounds/Smoke Puff Sound", "8 - Smoke & Sounds/Custom Sounds", "8 - Smoke & Sounds/Custom Sounds Volume",
            "8 - Smoke & Sounds/Open Sounds Folder", "8 - Smoke & Sounds/Reload Sounds", "8 - Smoke & Sounds/Arrival Effects*", "8 - Smoke & Sounds/Exit Effects*",
            "8 - Smoke & Sounds/Smoke Puff Size*",
            // 9 - Tools
            "9 - Tools/Test Fuling", "9 - Tools/Reload Quips", "9 - Tools/Reset Memory", "9 - Tools/Debug Logging*",
            "9 - Tools/Echo Test*", "9 - Tools/Test Torch*", "9 - Tools/Test Outfit*", "9 - Tools/Test Poof*",
            "9 - Tools/Test Exit Poof*", "9 - Tools/Test Next Emote*", "9 - Tools/Emote Test*",
        };

        /// <summary>Settings that moved or were renamed in 1.8.0: (old section, old key, new section, new key). Their values come along.</summary>
        private static readonly (string, string, string, string)[] Moved =
        {
            ("8 - Server", "Share Goblin On My Server", "1 - General", "Share Goblin On My Server"),
            ("1 - General", "Comment On Environment", "2 - Deaths", "Environment"),
            ("1 - General", "Comment On Falling", "2 - Deaths", "Falling"),
            ("1 - General", "Comment On Trees", "2 - Deaths", "Trees"),
            ("1 - General", "Comment On Creatures", "2 - Deaths", "Creatures"),
            ("1 - General", "Comment On Friendly Fire", "2 - Deaths", "Friendly Fire"),
            ("1 - General", "Comment On Other", "2 - Deaths", "Other"),
            ("2 - Look", "Fuling Name", "3 - Look", "Fuling Name"),
            ("2 - Look", "Say His Name Chance", "3 - Look", "Say His Name Chance"),
            ("2 - Look", "Fuling Type", "3 - Look", "Fuling Type"),
            ("2 - Look", "Recolour", "3 - Look", "Recolour"),
            ("2 - Look", "Hue", "3 - Look", "Hue"),
            ("2 - Look", "Saturation", "3 - Look", "Saturation"),
            ("2 - Look", "Brightness", "3 - Look", "Brightness"),
            ("2 - Look", "Helmet Chance", "3 - Look", "Helmet Chance"),
            ("2 - Look", "Helmet Items", "3 - Look", "Helmet Items"),
            ("2 - Look", "Outfit Piece Chance", "3 - Look", "Outfit Piece Chance"),
            ("2 - Look", "Outfit Items", "3 - Look", "Outfit Items"),
            ("2 - Look", "Torch At Night Chance", "3 - Look", "Torch At Night Chance"),
            ("2 - Look", "Night Item", "3 - Look", "Torch Item"),
            ("2 - Look", "Held Item Size", "3 - Look", "Held Item Size"),
            ("2 - Look", "Torch Pose", "3 - Look", "Torch Pose"),
            ("2 - Look", "Torch Run Animation", "3 - Look", "Torch Run Animation"),
            ("2 - Look", "Torch Idle Animation", "3 - Look", "Torch Idle Animation"),
            ("3 - Hat Fitting", "Fitting Mode", "4 - Hat Fitting", "Fitting Mode"),
            ("3 - Hat Fitting", "Helmet", "4 - Hat Fitting", "Helmet"),
            ("3 - Hat Fitting", "Up", "4 - Hat Fitting", "Up"),
            ("3 - Hat Fitting", "Forward", "4 - Hat Fitting", "Forward"),
            ("3 - Hat Fitting", "Side", "4 - Hat Fitting", "Side"),
            ("3 - Hat Fitting", "Tilt", "4 - Hat Fitting", "Tilt"),
            ("3 - Hat Fitting", "Turn", "4 - Hat Fitting", "Turn"),
            ("3 - Hat Fitting", "Size", "4 - Hat Fitting", "Size"),
            ("3 - Hat Fitting", "Use For All", "4 - Hat Fitting", "Use For All"),
            ("3 - Hat Fitting", "Reset This", "4 - Hat Fitting", "Reset This"),
            ("3 - Hat Fitting", "Saved Fits", "4 - Hat Fitting", "Saved Fits"),
            ("3 - Hat Fitting", "Helmet Test List", "4 - Hat Fitting", "Helmet Test List"),
            ("4 - Heckling", "Personal Space", "5 - Visits", "Personal Space"),
            ("4 - Heckling", "Taunt Chance", "5 - Visits", "Taunt Chance"),
            ("4 - Heckling", "Hop On Arrival", "5 - Visits", "Hop On Arrival"),
            ("4 - Heckling", "Play Dead Chance", "5 - Visits", "Play Dead Chance"),
            ("4 - Heckling", "Play Dead On Milestones", "5 - Visits", "Play Dead On Milestones"),
            ("4 - Heckling", "Stop Distance Min", "5 - Visits", "Stop Distance Min"),
            ("4 - Heckling", "Stop Distance Max", "5 - Visits", "Stop Distance Max"),
            ("4 - Heckling", "Run Before Vanishing", "5 - Visits", "Run Before Vanishing"),
            ("4 - Heckling", "Use Player Emotes", "5 - Visits", "Use Player Emotes"),
            ("4 - Heckling", "Emote Share", "5 - Visits", "Emote Share"),
            ("4 - Heckling", "Emote Names", "5 - Visits", "Emote Names"),
            ("4 - Heckling", "Death Impressions", "5 - Visits", "Death Impressions"),
            ("5 - Puffs & Sounds", "Smoke Puff", "8 - Smoke & Sounds", "Smoke Puff"),
            ("5 - Puffs & Sounds", "Smoke Puff Sound", "8 - Smoke & Sounds", "Smoke Puff Sound"),
            ("5 - Puffs & Sounds", "Play Sounds", "8 - Smoke & Sounds", "Custom Sounds"),
            ("5 - Puffs & Sounds", "Volume", "8 - Smoke & Sounds", "Custom Sounds Volume"),
            ("5 - Puffs & Sounds", "Open Folder", "8 - Smoke & Sounds", "Open Sounds Folder"),
            ("5 - Puffs & Sounds", "Reload", "8 - Smoke & Sounds", "Reload Sounds"),
            ("5 - Puffs & Sounds", "Arrival Effects", "8 - Smoke & Sounds", "Arrival Effects"),
            ("5 - Puffs & Sounds", "Exit Effects", "8 - Smoke & Sounds", "Exit Effects"),
            ("5 - Puffs & Sounds", "Smoke Puff Size", "8 - Smoke & Sounds", "Smoke Puff Size"),
            ("8 - Server", "Echo Test", "9 - Tools", "Echo Test"),
        };

        private static void Tidy()
        {
            var sectionOrder = new System.Collections.Generic.Dictionary<string, int>();
            var inOrder = new System.Collections.Generic.List<ConfigDefinition>();
            foreach (string item in Layout)
            {
                bool advanced = item.EndsWith("*");
                string id = advanced ? item.Substring(0, item.Length - 1) : item;
                int slash = id.IndexOf('/');
                var def = new ConfigDefinition(id.Substring(0, slash), id.Substring(slash + 1));
                if (!Instance.Config.ContainsKey(def)) { Log.LogWarning($"Menu layout names a setting that isn't there: {id}"); continue; }
                inOrder.Add(def);
                sectionOrder.TryGetValue(def.Section, out int n);
                sectionOrder[def.Section] = n + 1;
                foreach (var tag in Instance.Config[def].Description?.Tags ?? new object[0])
                    if (tag is ConfigurationManagerAttributes a) { a.Order = 1000 - n * 10; a.IsAdvanced = advanced; }
            }

            // Configuration Manager shows a mod's sections in the order its settings were made (unless the player sorts them by name),
            // and they're made in whatever order the code binds them: put them in the menu's order. (BepInEx's list, private there.)
            try
            {
                var entries = AccessTools.Property(typeof(ConfigFile), "Entries")?.GetValue(Instance.Config) as
                    System.Collections.Generic.Dictionary<ConfigDefinition, ConfigEntryBase>;
                if (entries == null) return;
                var all = inOrder.Where(entries.ContainsKey).Concat(entries.Keys.Except(inOrder).ToList())
                                 .Select(d => new System.Collections.Generic.KeyValuePair<ConfigDefinition, ConfigEntryBase>(d, entries[d])).ToList();
                entries.Clear();
                foreach (var kv in all) entries.Add(kv.Key, kv.Value);
            }
            catch (Exception e) { Log.LogWarning($"Couldn't put the settings in menu order ({e.Message}); they still all work."); }
        }

        /// <summary>
        /// Brings the values of settings that moved (Moved) to their new place, then drops every setting the mod no longer has from the
        /// file: BepInEx keeps them for ever, so each earlier reshuffle had left a copy behind.
        /// </summary>
        private void CarryOverMovedSettings()
        {
            try
            {
                var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) as
                    System.Collections.Generic.Dictionary<ConfigDefinition, string>;
                if (orphans == null || orphans.Count == 0) return;
                int carried = 0;
                foreach (var (oldSection, oldKey, newSection, newKey) in Moved)
                {
                    var now = new ConfigDefinition(newSection, newKey);
                    if (orphans.TryGetValue(new ConfigDefinition(oldSection, oldKey), out string value) && Config.ContainsKey(now))
                    {
                        Config[now].SetSerializedValue(value);
                        carried++;
                    }
                }
                int dropped = orphans.Count;
                orphans.Clear();
                Config.Save();
                Log.LogInfo($"Settings file tidied: {carried} setting(s) moved to their new place, {dropped} old entr(ies) removed.");
            }
            catch (Exception e) { Log.LogWarning($"Couldn't tidy the settings file ({e.Message}); everything still works."); }
        }

        /// <summary>Marks the settings the server decides on a server with the mod (Synced), as ServerSettings.Bind would.</summary>
        private const string ServerMark = " [Synced with Server]";

        private static ConfigDescription Desc(string text, int order, AcceptableValueBase range = null, bool advanced = false)
        {
            return new ConfigDescription(text, range,
                new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced });
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
                        onClick();
                }
            });
        }

        // ------------------------------------------------------------------
        // Core flow
        // ------------------------------------------------------------------

        /// <summary>The game's language ("English", "German"...), as its own translations name it.</summary>
        internal static string Language
        {
            get
            {
                try { return Localization.instance?.GetSelectedLanguage() ?? "English"; }
                catch { return "English"; }
            }
        }

        /// <summary>
        /// His quips: your FulingHeckler.quips.txt once you've edited it (in whatever language you wrote it). Until then the built-in
        /// quips in the game's language: the file is written in English, and an unedited one is the English built-in set.
        /// </summary>
        internal static void ReloadQuips()
        {
            try
            {
                if (!File.Exists(QuipFilePath))
                {
                    File.WriteAllText(QuipFilePath, QuipBook.ReadDefaultQuips());
                    Log.LogInfo($"Wrote default quips to {QuipFilePath}");
                }
                // The placeholders' words in the game's language too.
                bool english = Language == "English";
                Placeholders.TallOne = Translations.T("fulingheckler_tallone");
                Placeholders.Something = Translations.T("fulingheckler_something");
                Placeholders.OrdinalWord = english ? null : (Func<int, string>)(n => Translations.T("fulingheckler_ordinal", n));

                string own = File.ReadAllText(QuipFilePath);
                string built = QuipBook.SameText(own, QuipBook.ReadDefaultQuips()) ? QuipBook.ReadBuiltInQuips(Language) : null;
                Quips.Load((built ?? own).Split('\n'));
                foreach (var w in Quips.Warnings) Log.LogWarning("Quips file: " + w);
                Log.LogInfo(built != null ? $"Loaded {Quips.Count} built-in quips ({Language})." : $"Loaded {Quips.Count} quips from {Path.GetFileName(QuipFilePath)}.");
            }
            catch (Exception e)
            {
                Log.LogError($"Couldn't load quips file, using built-in defaults: {e.Message}");
                Quips.Load((QuipBook.ReadBuiltInQuips(Language) ?? QuipBook.ReadDefaultQuips()).Split('\n'));
            }
        }

        /// <summary>Called by the death patch when the local player dies.</summary>
        internal void OnLocalPlayerDied(Player player, DeathInfo info)
        {
            if (!Enabled.Value) return;

            Scenes.Instance?.RecordDeath(player.transform.position); // for the Tombstone Dancer
            var ev = DeathClassifier.Classify(info);
            string playerName = player.GetPlayerName();
            int count = Memory.Increment(playerName, ev.Key); // he remembers even when he stays home

            var settings = new PickerSettings
            {
                ChanceToComment = ChanceToComment.Value,
                AlwaysCommentOnFirst = AlwaysCommentOnFirst.Value,
                CooldownSeconds = CooldownSeconds.Value,
                Milestones = PickerSettings.ParseMilestones(Milestones.Value)
            };
            float now = Time.realtimeSinceStartup;

            var decision = QuipPicker.Decide(Quips, ev, count, settings, now - _lastCommentTime, IsCategoryEnabled(ev.Category), _rng);
            DebugLog($"Death: hitType={info.HitType} killer={info.KillerPrefab ?? "-"} -> {ev.Key} ({ev.Category}), count={count}, {decision.Reason}");

            if (decision.Line == null) return;

            _lastCommentTime = now;
            decision.Line = Placeholders.Fill(decision.Line, playerName, ev.KillerName, count);
            Courier.QueueAfterRespawn(player, decision);
        }

        private static bool IsCategoryEnabled(DeathCategory c)
        {
            switch (c)
            {
                case DeathCategory.Environment: return EvEnvironment.Value;
                case DeathCategory.Falling: return EvFalling.Value;
                case DeathCategory.Trees: return EvTrees.Value;
                case DeathCategory.Creatures: return EvCreatures.Value;
                case DeathCategory.FriendlyFire: return EvFriendlyFire.Value;
                default: return EvOther.Value;
            }
        }

        // ------------------------------------------------------------------
        // Tools
        // ------------------------------------------------------------------

        private void TestSummon(bool emoteTest, bool? forceTorch)
        {
            var p = Player.m_localPlayer;
            if (p == null) { ShowToolFeedback("Load into a world first."); return; }
            string line = QuipPicker.PickForTest(Quips, _rng);
            if (line == null) { ShowToolFeedback("No lines in the quips file."); return; }
            line = Placeholders.Fill(line, p.GetPlayerName(), "a test", 3);
            Courier.Deliver(p, line, special: true, milestone: false, emoteTest: emoteTest, forceTorch: forceTorch, isTest: true);
        }

        private int _emoteTestIndex;

        private void TestNextEmote()
        {
            var p = Player.m_localPlayer;
            if (p == null) { ShowToolFeedback("Load into a world first."); return; }
            var names = NameList.Parse(Emotes.Value);
            if (names.Count == 0) { ShowToolFeedback("'Emote Names' is empty."); return; }

            string name = names[_emoteTestIndex % names.Count];
            _emoteTestIndex++;
            ShowToolFeedback($"Emote test {((_emoteTestIndex - 1) % names.Count) + 1}/{names.Count}: {name}");
            Courier.Deliver(p, $"Watch! Fuling do '{name}'!", special: true, milestone: false, isTest: true, forcedEmote: name);
        }

        /// <summary>
        /// The fits that ship with the mod (the fishing hat and friends). Your own Saved Fits sit on top of these,
        /// so clearing Saved Fits brings these back rather than leaving the hat in his face.
        /// </summary>
        internal const string BuiltInFitsText = "default: 0.082, 0.065, 0.014, -2.596, 180, 0.97 | HelmetFishingHat: 0.039, 0.065, 0.014, -2.596, 180, 1.192 | HelmetIron: 0, 0, 0, 0, 180, 2.323 | HelmetLeather: 0.061, 0.065, -0.017, -33.75, 180, 1.444 | HelmetMidsummerCrown: 0.048, 0.056, -0.012, -28.558, 0, 0.838 | HelmetPointyHat: 0.074, 0.039, 0, -53.221, 180, 1 | HelmetYule: 0.104, 0.073, 0, 0, 180, 1";

        private static readonly System.Random NameRng = new System.Random();

        /// <summary>Swaps {me} for his name or plain "Fuling", by the 'Say His Name Chance' setting.</summary>
        internal static string FillMe(string text) =>
            Placeholders.FillMe(text, Look.Name, Look.SayNameChance, NameRng);

        /// <summary>The settings that make up his look, which a server shares with its players.</summary>
        internal static ConfigEntryBase[] LookSettings() => new ConfigEntryBase[]
        {
            FulingName, SayNameChance, FulingType, HelmetChance, Helmets, OutfitItems, OutfitPieceChance,
            TorchAtNightChance, TorchItem, HeldItemSize, Recolor, SkinHue, SkinSaturation, SkinBrightness, SavedFitsText
        };

        internal static FitBook SavedFits()
        {
            var book = FitBook.Parse(BuiltInFitsText);
            foreach (var kv in FitBook.Parse(SavedFitsText?.Value).Fits) book.Fits[kv.Key] = kv.Value;
            return book;
        }

        internal static void ReloadLoot()
        {
            try
            {
                if (!File.Exists(LootFilePath))
                {
                    File.WriteAllText(LootFilePath, LootTable.ReadDefault());
                    Log.LogInfo($"Wrote default loot table to {LootFilePath}");
                }
                LootText = File.ReadAllText(LootFilePath);
            }
            catch (Exception e)
            {
                Log.LogError($"Couldn't load the loot table, using built-in defaults: {e.Message}");
                LootText = LootTable.ReadDefault();
            }
            Synced.TextChanged("Loot");   // UseLoot gets it, unless the server's table applies (yours is kept for other worlds)
        }

        /// <summary>The loot table that applies now: your file's, or the server's on a server with the mod.</summary>
        private static void UseLoot(string text)
        {
            Loot.Load((text ?? "").Split('\n'));
            foreach (var w in Loot.Warnings) Log.LogWarning("Loot file: " + w);
            Log.LogInfo(Synced.TextFromServer("Loot") ? $"Using the server's loot table: {Loot.Entries.Count} entries." : $"Loaded {Loot.Entries.Count} loot entries.");
        }

        internal static Fit SliderFit() => new Fit
        {
            Up = FitUp.Value, Forward = FitForward.Value, Side = FitSide.Value,
            Tilt = FitTilt.Value, Turn = FitTurn.Value, Size = FitSize.Value
        };

        private static void LoadSliders(Fit f)
        {
            FitUp.Value = f.Up; FitForward.Value = f.Forward; FitSide.Value = f.Side;
            FitTilt.Value = f.Tilt; FitTurn.Value = f.Turn; FitSize.Value = f.Size;
        }

        private bool _loadingSliders;

        private void OnFittingModeChanged()
        {
            if (FittingMode.Value)
            {
                var p = Player.m_localPlayer;
                if (p == null) { ShowToolFeedback("Load into a world first."); FittingMode.Value = false; return; }
                LoadSlidersFor(FitHelmet.Value);
                Courier.Deliver(p, "Fuling can't come right now.", special: true, milestone: false, isTest: true,
                    forcedHelmet: FitHelmet.Value, fitting: true);
                ShowToolFeedback($"Fitting {FitHelmet.Value}. Drag the sliders; changes save automatically.");
            }
            else
            {
                FulingActor.Current?.EndFitting();
            }
        }

        private void OnFitHelmetChanged()
        {
            LoadSlidersFor(FitHelmet.Value);
            var current = FulingActor.Current;
            if (FittingMode.Value && current != null && current.IsFitting)
            {
                current.WearHelmet(FitHelmet.Value, logResult: true);
                ShowToolFeedback(SavedFits().HasOwn(FitHelmet.Value)
                    ? $"{FitHelmet.Value}: using its own saved fit."
                    : $"{FitHelmet.Value}: using the shared fit. Move a slider to give it its own.");
            }
        }

        /// <summary>Slider moved: save it as this helmet's own fit (the helmet itself updates live every frame).</summary>
        private void OnSliderChanged()
        {
            if (_loadingSliders || !FittingMode.Value) return;
            var fits = SavedFits();
            fits.Fits[FitHelmet.Value] = SliderFit();
            SavedFitsText.Value = fits.ToString();
        }

        private void LoadSlidersFor(string helmet)
        {
            _loadingSliders = true;
            try { LoadSliders(SavedFits().For(helmet)); }
            finally { _loadingSliders = false; }
        }

        private void SaveFit(bool asDefault)
        {
            if (!FittingMode.Value) { ShowToolFeedback("Tick 'Fitting Mode' first."); return; }
            var fits = SavedFits();
            fits.Fits["default"] = SliderFit();
            SavedFitsText.Value = fits.ToString();
            ShowToolFeedback($"{FitHelmet.Value}'s fit is now the shared fit for every helmet without its own.");
        }

        private void ClearFit()
        {
            string name = FitHelmet.Value;
            var fits = SavedFits();
            var builtIn = FitBook.Parse(BuiltInFitsText);
            if (builtIn.HasOwn(name))
            {
                fits.Fits[name] = builtIn.Fits[name];
                SavedFitsText.Value = fits.ToString();
                LoadSlidersFor(name);
                ShowToolFeedback($"{name} is back on the fit the mod ships with.");
                return;
            }
            if (!fits.Fits.Remove(name)) { ShowToolFeedback($"{name} was already using the shared fit."); return; }
            SavedFitsText.Value = fits.ToString();
            LoadSlidersFor(name);
            ShowToolFeedback($"{name} is back on the shared fit.");
        }

        private static void TestPuff(string effects)
        {
            var p = Player.m_localPlayer;
            if (p == null) { ShowToolFeedback("Load into a world first."); return; }
            Vector3 f = p.transform.forward; f.y = 0f;
            Poof.At(p.transform.position + f.normalized * 4f, effects);
            ShowToolFeedback("Puff: " + effects);
        }

        private void TestScene(SceneKind kind)
        {
            var p = Player.m_localPlayer;
            if (p == null) { ShowToolFeedback("Load into a world first."); return; }
            if (FulingActor.Current != null) Destroy(FulingActor.Current.gameObject);

            bool ok;
            Vector3 ahead = p.transform.forward; ahead.y = 0f; ahead.Normalize();
            if (kind == SceneKind.Tombstone)
                ok = Scenes.Instance != null && Scenes.Instance.StartTombstone(p, p.transform.position + ahead * 30f);
            else if (kind == SceneKind.Campfire)
                ok = Scenes.Instance != null && Scenes.Instance.StartCampfire(p, Scenes.NearestLitFire(p, 60f) ?? p.transform.position + ahead * 20f);
            else if (kind == SceneKind.CoinThief && CoinTheft.CoinsCarried(p) < 1)
            {
                ShowToolFeedback("Coin Thief: you're not carrying any coins.");
                return;
            }
            else if (kind == SceneKind.Gifter && !Rules.CanTestGifts)
            {
                ShowToolFeedback("Gifter: on someone else's server the test needs the game's cheats on (devcommands), since he hands out real items.");
                return;
            }
            else if (kind == SceneKind.Gifter && Rules.Waiting)
            {
                ShowToolFeedback("Gifter: still waiting to hear the server's rules. Try again in a few seconds.");
                return;
            }
            else if (kind == SceneKind.Gifter && !Gifts.HasRoom(p, out string why))
            {
                ShowToolFeedback($"Gifter: {why}.");
                return;
            }
            else ok = Scenes.Instance != null && Scenes.Instance.Stage(p, kind);

            ShowToolFeedback(ok ? $"{kind}: started. Close the menu!" : $"{kind}: couldn't find a spot for him here. Try somewhere more open.");
        }

        private static void ShowToolFeedback(string msg)
        {
            Log.LogInfo(msg);
            if (Player.m_localPlayer == null) return;
            try { Delivery.ShowPlainText(msg); }
            catch (Exception e) { Log.LogWarning("Couldn't show message: " + e.Message); }
        }

        internal static void DebugLog(string msg)
        {
            if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
