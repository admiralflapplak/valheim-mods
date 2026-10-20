using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

namespace WisplightArrows
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.wisplightarrows";
        public const string Name = "WisplightArrows";
        public const string Version = "0.7.3";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Mist (the server's: how much mist an arrow clears is gameplay)
        internal static ConfigEntry<float> FlightRadius;
        internal static ConfigEntry<float> LandingRadius;
        internal static ConfigEntry<float> LandingSeconds;
        internal static ConfigEntry<float> TrailSeconds;
        internal static ConfigEntry<float> DrawRadius;

        // 2 - Arrow and 3 - Crafting (the server's)
        internal static ConfigEntry<float> Damage;
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<string> Station;
        internal static ConfigEntry<int> PerCraft;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            FlightRadius = Synced.Bind("1 - Mist", "Clear radius in flight", 10f,
                "How far round the arrow the mist is pushed away while it flies (metres).", new AcceptableValueRange<float>(1f, 20f));
            LandingRadius = Synced.Bind("1 - Mist", "Clear radius where it lands", 15f,
                "How far round the spot where the arrow lands, or the creature it hit, the mist stays away (metres).", new AcceptableValueRange<float>(1f, 30f));
            LandingSeconds = Synced.Bind("1 - Mist", "Seconds clear where it lands", 60f,
                "How long the mist stays away where the arrow lands, or round the creature it hit. Glows already lit keep their time.", new AcceptableValueRange<float>(5f, 600f));

            TrailSeconds = Synced.Bind("1 - Mist", "Seconds the path stays clear", 4f,
                "How long the mist stays parted along the arrow's path before closing in again (0 = it closes right behind the arrow).",
                new AcceptableValueRange<float>(0f, 30f));

            DrawRadius = Synced.Bind("1 - Mist", "Clear radius while drawn", 5f,
                "How far round the archer the mist is pushed away while a wisplight arrow is drawn on the bow (metres).",
                new AcceptableValueRange<float>(0f, 15f));

            Damage = Synced.Bind("2 - Arrow", "Pierce damage", 6f,
                "The arrow's own pierce damage, added to the bow's (a wooden arrow has 22). It is a light to see by more than a weapon.",
                new AcceptableValueRange<float>(0f, 100f));

            Cost = Synced.Bind("3 - Crafting", "Cost", "Wood:4, Feathers:1, Silver:1, Wisp:1",
                "What one craft costs. " + Costs.Help);
            Station = Synced.Bind("3 - Crafting", "Station", "piece_workbench:1",
                "Where they are crafted, and the station's level. " + Costs.StationHelp);
            PerCraft = Synced.Bind("3 - Crafting", "Arrows per craft", 10,
                "How many arrows one craft makes.", new AcceptableValueRange<int>(1, 100));
            Config.SaveOnConfigSet = true;
            Config.Save();

            FlightRadius.SettingChanged += (s, e) => ArrowItem.ApplyMist();
            LandingRadius.SettingChanged += (s, e) => ArrowItem.ApplyMist();
            LandingSeconds.SettingChanged += (s, e) => ArrowItem.ApplyMist();
            DrawRadius.SettingChanged += (s, e) => NockGlow.ApplyRadius();
            Damage.SettingChanged += (s, e) => ArrowItem.ApplyDamage();
            Cost.SettingChanged += (s, e) => ArrowItem.ApplyRecipeSettings();
            Station.SettingChanged += (s, e) => ArrowItem.ApplyRecipeSettings();
            PerCraft.SettingChanged += (s, e) => ArrowItem.ApplyRecipeSettings();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            // Loaded into a running world (a hot reload): the game's registers have already run.
            ArrowItem.Register(ZNetScene.instance, ObjectDB.instance);
            if (ZNetScene.instance != null)
                ItemReload.Repoint(name => name == ArrowItem.PrefabName ? ArrowItem.Prefab : null);

            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            NockGlow.Update();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            NockGlow.Reset();
            Content.RemoveAll(keepItems: ItemReload.KeepOnUnload);
            Synced?.Dispose();
            Translations.Unload();
            ArrowItem.Forget();
            Instance = null;
            Synced = null;
        }
    }
}
