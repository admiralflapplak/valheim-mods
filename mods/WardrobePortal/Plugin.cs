using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs)

// It registers a build piece, and the game only learns which prefabs are portals when a world loads (Game.Awake): reload at the main menu.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace WardrobePortal
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.wardrobeportal";
        public const string Name = "WardrobePortal";
        public const string Version = "0.4.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Building (the server's: the client pays when it builds, so it must charge the server's cost)
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<string> Station;

        // 2 - Doors (each player's own: only how the doors look to them)
        internal static ConfigEntry<float> OpenDistance;
        internal static ConfigEntry<float> OpenAngle;
        internal static ConfigEntry<float> DoorSpeed;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            Cost = Synced.Bind("1 - Building", "Cost", "Frostwood:10, Tar:2, BlackMetal:6, SurtlingCore:2, GreydwarfEye:10",
                "What a Wardrobe Portal costs to build (by default the wardrobe's cost, plus the two Surtling Cores and ten Greydwarf Eyes a portal takes). " + Costs.Help);
            Station = Synced.Bind("1 - Building", "Station", "piece_workbench",
                "The crafting station that must be near to build it. " + Costs.StationHelp);
            OpenDistance = Config.Bind("2 - Doors", "Open distance", 4f,
                new ConfigDescription("The doors swing open when a player comes this close (metres).", new AcceptableValueRange<float>(1f, 10f)));
            OpenAngle = Config.Bind("2 - Doors", "Open angle", 110f,
                new ConfigDescription("How far the doors swing open (degrees).", new AcceptableValueRange<float>(30f, 170f)));
            DoorSpeed = Config.Bind("2 - Doors", "Door speed", 180f,
                new ConfigDescription("How fast the doors swing (degrees a second).", new AcceptableValueRange<float>(30f, 720f)));
            Config.SaveOnConfigSet = true;
            Config.Save();

            Cost.SettingChanged += (s, e) => WardrobePiece.ApplyCost();
            Station.SettingChanged += (s, e) => WardrobePiece.ApplyCost();

            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);

            // Loaded at the main menu after a reload: the scene may already be there.
            WardrobePiece.Register(ZNetScene.instance);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update() => Synced?.Update();

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            WardrobePiece.Unload();
            Synced?.Dispose();
            Translations.Unload();
            Instance = null;
            Synced = null;
        }
    }
}
