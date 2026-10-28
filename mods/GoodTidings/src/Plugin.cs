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
        public const string Version = "0.3.12";

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

            DebugLogging = Config.Bind("09 - Testing", "Debug Logging", false,
                "Log why a visit did or didn't happen.");

            Goods.Bind(Config);

            Config.SaveOnConfigSet = true;
            Config.Save();

            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            if (ZNetScene.instance) TraderPrefab.Register(ZNetScene.instance);   // (after a reload: the scene is already up)
            Commands.Register();
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update() => Synced?.Update();

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();   // hot reload: never leave old patches behind
            Commands.Unregister();
            TraderPrefab.Unregister();
            Synced?.Dispose();
            Translations.Unload();
        }

        internal static void Debug(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
