using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)
using Nick.Shared;

// Registers the wandering trader's prefab (goodtidings_trader): reload at the main menu.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace GoodTidings
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.GoodTidings";
        public const string Name = "GoodTidings";
        public const string Version = "0.13.5";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        /// <summary>The gameplay settings: the server's values apply while playing on a server that has Good Tidings.</summary>
        internal static ServerSettings Synced;

        // 01 - General
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> VisitChance;

        // 02 - Wandering Trader
        internal static ConfigEntry<bool> TraderEnabled;
        internal static ConfigEntry<float> TraderMinutes;

        // 03 - Bountiful Season
        internal static ConfigEntry<bool> BountifulEnabled;
        internal static ConfigEntry<float> BountifulMinutes, BountifulSpeed, BountifulHeadStart, BountifulGrowSpeed;

        // 04 - Muninn's Riddle
        internal static ConfigEntry<bool> MuninnEnabled;
        internal static ConfigEntry<float> MuninnMinutes, MuninnLoreChance;

        // 05 - Wounded Dvergr
        internal static ConfigEntry<bool> DvergerEnabled;
        internal static ConfigEntry<float> DvergerMinutes, TemperedMinutes, TemperedWear;
        internal static ConfigEntry<int> DvergerMeals;

        // 06 - Skald
        internal static ConfigEntry<bool> SkaldEnabled;
        internal static ConfigEntry<float> SkaldMinutes, SkaldSkillGain, SkaldListenSeconds;
        internal static ConfigEntry<int> SkaldComfort;
        internal static ConfigEntry<float> SkaldVolume;

        // 09 - Testing
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Translations.Load(this);

            Config.SaveOnConfigSet = false;
            Synced = new ServerSettings(Guid, Config, Logger);

            Enabled = Synced.Bind("01 - General", "Enabled", true,
                "Good visits can take the place of a raid.");
            VisitChance = Synced.Bind("01 - General", "Visit Chance", 30f,
                "When the game's raid timer picks a raid, the percent chance it is a good visit instead (0 = never, 100 = only visits).",
                new AcceptableValueRange<float>(0f, 100f));

            TraderEnabled = Synced.Bind("02 - Wandering Trader", "Enabled", true,
                "A travelling trader can stop by your base.");
            TraderMinutes = Synced.Bind("02 - Wandering Trader", "Stay Minutes", 6f,
                "How long the trader stays before moving on.", new AcceptableValueRange<float>(1f, 30f));

            BountifulEnabled = Synced.Bind("03 - Bountiful Season", "Enabled", true,
                "A warm season can come over the base: for a while crops grow, beehives fill, and fermenters, smelters, kilns and cooking stations work much faster.");
            BountifulMinutes = Synced.Bind("03 - Bountiful Season", "Minutes", 5f, "How long it lasts.", new AcceptableValueRange<float>(1f, 20f));
            BountifulSpeed = Synced.Bind("03 - Bountiful Season", "Speed", 8f,
                "How much faster things work meanwhile (8: eight times as fast).", new AcceptableValueRange<float>(1f, 30f));
            BountifulGrowSpeed = Synced.Bind("03 - Bountiful Season", "Growing Speed", 20f,
                "Crops, saplings and beehives take far longer than the rest: how much faster they go meanwhile (20: twenty times as fast).",
                new AcceptableValueRange<float>(1f, 60f));
            BountifulHeadStart = Synced.Bind("03 - Bountiful Season", "Head Start Minutes", 0f,
                "Crops, saplings and beehives take far longer than the rest: this much growing time at once when the season first reaches them.",
                new AcceptableValueRange<float>(0f, 120f));

            MuninnEnabled = Synced.Bind("04 - Muninn Riddle", "Enabled", true,
                "Odin's raven of memory can land at the base with one riddle: a reward for the right answer, the land's beasts for a wrong one.");
            MuninnMinutes = Synced.Bind("04 - Muninn Riddle", "Stay Minutes", 5f, "How long he waits for an answer.", new AcceptableValueRange<float>(1f, 30f));
            MuninnLoreChance = Synced.Bind("04 - Muninn Riddle", "Old Riddle Chance", 35f,
                "The percent chance he asks one of the old riddles (from the Eddas and Valheim's story, none repeated until all are asked) instead of one made from what the player remembers. A right answer always brings the land's rare.",
                new AcceptableValueRange<float>(0f, 100f));

            DvergerEnabled = Synced.Bind("05 - Wounded Dvergr", "Enabled", true,
                "A wounded Dvergr can be found near the base: help him up, and he rests by a fire; feed him in time and he mends and tempers everyone's gear, leave him and his kin come for the base.");
            DvergerMinutes = Synced.Bind("05 - Wounded Dvergr", "Stay Minutes", 10f,
                "How long there is to help and feed him before he limps off.", new AcceptableValueRange<float>(3f, 30f));
            DvergerMeals = Synced.Bind("05 - Wounded Dvergr", "Meals", 3, "How many meals he needs.", new AcceptableValueRange<int>(1, 10));
            TemperedMinutes = Synced.Bind("05 - Wounded Dvergr", "Tempered Minutes", 30f,
                "How long gear stays tempered after he's healed.", new AcceptableValueRange<float>(1f, 180f));
            TemperedWear = Synced.Bind("05 - Wounded Dvergr", "Tempered Wear", 50f,
                "The percent of the usual wear tempered gear takes.", new AcceptableValueRange<float>(0f, 100f));

            SkaldEnabled = Synced.Bind("06 - Skald", "Enabled", true,
                "A long-dead skald can rise at the most comfortable spot in the base and play: comfort near him is higher, and listening to him raises every skill a little, once a visit.");
            SkaldMinutes = Synced.Bind("06 - Skald", "Stay Minutes", 6f, "How long he plays.", new AcceptableValueRange<float>(1f, 30f));
            SkaldSkillGain = Synced.Bind("06 - Skald", "Skill Gain", 5f,
                "Each skill rises by this percent of its level (at least half a level) for a player who listens (a death takes 25%).",
                new AcceptableValueRange<float>(0f, 25f));
            SkaldListenSeconds = Synced.Bind("06 - Skald", "Listen Seconds", 30f, "How long a player must stay near him for the skill-ups.",
                new AcceptableValueRange<float>(5f, 300f));
            SkaldComfort = Synced.Bind("06 - Skald", "Comfort", 2, "How much comfort he adds near him while he plays.", new AcceptableValueRange<int>(0, 5));
            SkaldVolume = Config.Bind("06 - Skald", "Song Volume", 80f,
                new ConfigDescription("How loud his song is for you (your own setting; the game's sound effects volume applies too).",
                    new AcceptableValueRange<float>(0f, 100f)));

            DebugLogging = Config.Bind("09 - Testing", "Debug Logging", false,
                "Log why a visit did or didn't happen.");

            Goods.Bind(Config);
            SkaldLook.Bind(Config);

            // Dagny's stock file (the server's applies to everyone), read again when it's saved
            Synced.AddText(StockFile.SyncKey, StockFile.OwnText, StockFile.Use);
            StockFile.Watch();

            Config.SaveOnConfigSet = true;
            Config.Save();

            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            if (ZNetScene.instance) { TraderPrefab.Register(ZNetScene.instance); MuninnPrefab.Register(ZNetScene.instance); DvergerPrefab.Register(ZNetScene.instance); SkaldPrefab.Register(ZNetScene.instance); }   // (after a reload: the scene is already up)
            Commands.Register();
            MapPins.CleanUp();
            Net.Register();   // (after a reload: the world is already up)
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            RiddleWindow.Tick();
            StockFile.Tick();
            DvergerBand.Tick();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();   // hot reload: never leave old patches behind
            Commands.Unregister();
            Net.Unregister();
            BountifulSeason.Stop();
            MapPins.CleanUp();
            TraderPrefab.Unregister();
            MuninnPrefab.Unregister();
            DvergerPrefab.Unregister();
            SkaldPrefab.Unregister();
            Tempered.Unload();
            DvergerBand.Unload();
            RiddleWindow.Close();
            GameUi.Destroy();
            StockFile.Unwatch();
            Synced?.Dispose();
            Translations.Unload();
        }

        internal static void Debug(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
