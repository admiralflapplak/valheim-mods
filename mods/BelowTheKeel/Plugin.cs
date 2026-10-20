using BepInEx;
using BepInEx.Configuration;
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

namespace BelowTheKeel
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.belowthekeel";
        public const string Name = "BelowTheKeel";
        public const string Version = "0.2.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - When (the server's: everyone aboard one ship must work out the same schedule, see Director)
        internal static ConfigEntry<float> MinutesBetween;
        internal static ConfigEntry<float> MinutesApart;
        internal static ConfigEntry<float> MinDepth;
        internal static ConfigEntry<float> LandDistance;
        internal static ConfigEntry<float> NightFactor;

        // 2 - Omens (each player's own: one who turns an omen off just doesn't see it when the rest of the crew does)
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Eye;
        internal static ConfigEntry<bool> Pacer;
        internal static ConfigEntry<bool> HalfBoat;
        internal static ConfigEntry<bool> RansNet;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            MinutesBetween = Synced.Bind("1 - When", "Minutes between omens", 20f,
                "On average, how many minutes of sailing in deep water pass between two omens.", new AcceptableValueRange<float>(2f, 120f));
            MinutesApart = Synced.Bind("1 - When", "Least minutes apart", 6f,
                "Two omens never come closer together than this.", new AcceptableValueRange<float>(1f, 60f));
            MinDepth = Synced.Bind("1 - When", "Least water depth", 40f,
                "Omens only come where the sea is at least this deep under the ship (metres).", new AcceptableValueRange<float>(10f, 200f));
            LandDistance = Synced.Bind("1 - When", "Least distance from land", 250f,
                "Omens only come when no land is this close to the ship (metres).", new AcceptableValueRange<float>(0f, 1000f));
            NightFactor = Synced.Bind("1 - When", "More often at night", 2f,
                "How many times more often omens come at night.", new AcceptableValueRange<float>(1f, 5f));

            Enabled = Config.Bind("2 - Omens", "Enabled", true, "See omens at all.");
            Eye = Config.Bind("2 - Omens", "The Eye", true, "A vast eye opens far below the ship.");
            Pacer = Config.Bind("2 - Omens", "The Pacer", true, "Something long and dark keeps pace beneath the ship, and never seems to end.");
            HalfBoat = Config.Bind("2 - Omens", "The Half-Boat", true, "A drowned man rows half a boat past you in the fog.");
            RansNet = Config.Bind("2 - Omens", "Rans Net", true, "The drowned hang still in the water under the ship.");
            Config.SaveOnConfigSet = true;
            Config.Save();

            Commands.Register();
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Director.Update();
        }

        private void OnDestroy()
        {
            Visuals.ClearAll();
            Look.Forget();
            Director.Reset();
            Commands.Unregister();
            Synced?.Dispose();
            Instance = null;
            Synced = null;
        }
    }
}
