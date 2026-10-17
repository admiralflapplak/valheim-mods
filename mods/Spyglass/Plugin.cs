using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

namespace Spyglass
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.spyglass";
        public const string Name = "Spyglass";
        public const string Version = "1.0.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Looking (each player's own: what they see)
        internal static ConfigEntry<float> StartMagnification;
        internal static ConfigEntry<float> MaxMagnification;
        internal static ConfigEntry<float> MagnificationStep;
        internal static ConfigEntry<bool> LookFromEyes;
        internal static ConfigEntry<bool> RaiseArm;
        internal static ConfigEntry<bool> CrackedLens;
        internal static ConfigEntry<float> FarSight;
        internal static ConfigEntry<float> ClearAir;

        // 2 - Pinging, 3 - Other players (each player's own)
        internal static ConfigEntry<KeyCode> PingKey;
        internal static ConfigEntry<float> PingRange, PingCooldown;
        internal static ConfigEntry<bool> OthersArm, OthersSounds;
        internal static ConfigEntry<float> HearRange;

        // 4 - Crafting and 5 - Wear (the server's)
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<string> Station;
        internal static ConfigEntry<float> MaxDurability;
        internal static ConfigEntry<float> WearSeconds;

        // 6 - Sounds (advanced; each player's own: the names are the game's sound effect prefabs)
        internal static ConfigEntry<bool> Sounds;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<string> RaiseSound, LowerSound, ZoomSound, BreakSound;

        // 7 - Pose (advanced; where the hand holds it, in metres from the head, in the body's own directions)
        internal static ConfigEntry<float> HandRight, HandUp, HandForward, HandRoll, ElbowOut, ElbowDown;
        internal static ConfigEntry<bool> HoldPose;

        private Harmony _harmony;

        private static ConfigDescription Advanced(string text, AcceptableValueBase range = null) =>
            new ConfigDescription(text, range, new ConfigurationManagerAttributes { IsAdvanced = true });

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;

            const string looking = "1 - Looking";
            StartMagnification = Config.Bind(looking, "Starting magnification", 4f,
                new ConfigDescription("How much the spyglass magnifies when you first raise it.", new AcceptableValueRange<float>(1.5f, 30f)));
            MaxMagnification = Config.Bind(looking, "Highest magnification", 10f,
                new ConfigDescription("The most you can zoom in with the mouse wheel while looking through it.", new AcceptableValueRange<float>(2f, 30f)));
            MagnificationStep = Config.Bind(looking, "Wheel step", 1f,
                new ConfigDescription("How much one notch of the mouse wheel changes the magnification.", new AcceptableValueRange<float>(0.25f, 5f)));
            LookFromEyes = Config.Bind(looking, "Look from your eyes", true,
                "Looking through the spyglass moves the camera to your eyes and hides your body, as a real spyglass would. Off keeps the camera behind you.");
            RaiseArm = Config.Bind(looking, "Raise your arm", true,
                "Your right hand brings the spyglass up to your face while you look through it.");
            CrackedLens = Config.Bind(looking, "Show wear on the lens", true,
                "A new spyglass has a clear lens. As it wears, the glass clouds over and a crack spreads from low and to the right.");
            FarSight = Config.Bind(looking, "Far sight", 3f,
                new ConfigDescription("While looking through the spyglass, distant objects keep their detail this many times further out (1 = no change). Only what the game has loaded can be seen.", new AcceptableValueRange<float>(1f, 4f)));
            ClearAir = Config.Bind(looking, "Clear air", 0.8f,
                new ConfigDescription("How much of the fog is cleared while looking through the spyglass (0 = none, 1 = all of it).", new AcceptableValueRange<float>(0f, 1f)));

            const string pinging = "2 - Pinging";
            PingKey = Config.Bind(pinging, "Ping key", KeyCode.Mouse2, "While looking through the spyglass, this key (the middle mouse button) puts a ping on the map where the middle of the view points, for everyone, as a ping from the map does. None turns it off. On a controller the attack button (right trigger) pings, the right bumper zooms in and the left bumper zooms out.");
            PingRange = Config.Bind(pinging, "Ping range", 1000f, new ConfigDescription("How far away the ping can reach (metres).", new AcceptableValueRange<float>(50f, 3000f)));
            PingCooldown = Config.Bind(pinging, "Ping delay", 2f, new ConfigDescription("Seconds between pings.", new AcceptableValueRange<float>(0f, 30f)));

            const string others = "3 - Other players";
            OthersArm = Config.Bind(others, "See others raise theirs", true, "Other players who have the mod are seen holding their spyglass up to their eye.");
            OthersSounds = Config.Bind(others, "Hear other spyglasses", true, "The sounds of raising, lowering and zooming other players' spyglasses, when they are near enough.");
            HearRange = Config.Bind(others, "Hearing distance", 25f, new ConfigDescription("How near another player must be for you to hear their spyglass (metres).", new AcceptableValueRange<float>(5f, 100f)));

            Cost = Synced.Bind("4 - Crafting", "Cost", "FineWood:2, Bronze:2, Resin:4",
                "What one spyglass costs to craft. " + Costs.Help);
            Station = Synced.Bind("4 - Crafting", "Station", "forge:1",
                "Where it is crafted, and the station's level. " + Costs.StationHelp);
            MaxDurability = Synced.Bind("5 - Wear", "Durability", 100f,
                "How much wear a spyglass takes before its lens is too damaged to look through (until it is mended at the forge).",
                new AcceptableValueRange<float>(10f, 1000f));
            WearSeconds = Synced.Bind("5 - Wear", "Seconds of looking per point", 6f,
                "How many seconds of looking through the spyglass use up one point of durability (0 = it never wears).",
                new AcceptableValueRange<float>(0f, 120f));

            // The last two sections are for tinkering: shown only with Configuration Manager's "Advanced settings" ticked.
            const string sounds = "6 - Sounds";
            Sounds = Config.Bind(sounds, "Sounds", true, Advanced("Sounds for raising and lowering the spyglass, changing the magnification, and the lens breaking."));
            SoundVolume = Config.Bind(sounds, "Volume", 1f, Advanced("How loud they are, as a part of each sound's own volume (the game's volume settings apply as well).", new AcceptableValueRange<float>(0f, 1f)));
            RaiseSound = Config.Bind(sounds, "Raise sound", "sfx_grapplinghook_reload", Advanced("The game's sound effect for raising the spyglass to your eye (Claude Tools' `prefabs sfx_` lists them)."));
            LowerSound = Config.Bind(sounds, "Lower sound", "sfx_equip_start", Advanced("The game's sound effect for lowering it (played lower in pitch)."));
            ZoomSound = Config.Bind(sounds, "Zoom step sound", "sfx_pickable_pick", Advanced("The game's sound effect for each step of the magnification (its pitch rises as you zoom in)."));
            BreakSound = Config.Bind(sounds, "Break sound", "sfx_equip", Advanced("The game's sound effect for the lens breaking."));

            const string pose = "7 - Pose";
            HandForward = Config.Bind(pose, "Hand forward", 0.4f, Advanced("How far in front of the head the hand holds the spyglass (metres).", new AcceptableValueRange<float>(0f, 0.8f)));
            HandRight = Config.Bind(pose, "Hand right", 0.06f, Advanced("How far to the character's right (the right eye is at about 0.035; the default sits a little further out, clear of the face).", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            HandUp = Config.Bind(pose, "Hand up", 0.10f, Advanced("How far above the head's centre (the eyes are at about 0.1).", new AcceptableValueRange<float>(-0.4f, 0.6f)));
            HandRoll = Config.Bind(pose, "Hand roll", 57f, Advanced("Turns the hand round the spyglass, in degrees: which way the fingers point.", new AcceptableValueRange<float>(-180f, 180f)));
            ElbowOut = Config.Bind(pose, "Elbow out", 0.3f, Advanced("How far the elbow sits to the right of the hand (metres).", new AcceptableValueRange<float>(-0.5f, 0.8f)));
            ElbowDown = Config.Bind(pose, "Elbow down", 0.3f, Advanced("How far the elbow sits below the hand (metres).", new AcceptableValueRange<float>(-0.5f, 0.9f)));
            HoldPose = Config.Bind(pose, "Hold the pose", false, Advanced("Keeps the spyglass raised whenever it is in your hand, so the pose can be looked at and tuned. Leave off to play."));
            Config.SaveOnConfigSet = true;
            Config.Save();

            Cost.SettingChanged += (s, e) => SpyglassItem.ApplyRecipeSettings();
            Station.SettingChanged += (s, e) => SpyglassItem.ApplyRecipeSettings();
            MaxDurability.SettingChanged += (s, e) => SpyglassItem.ApplyDurability();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            // Loaded into a running world (a hot reload): the game's registers have already run.
            SpyglassItem.Register(ZNetScene.instance, ObjectDB.instance);
            if (ZNetScene.instance != null)
                ItemReload.Repoint(name => name == SpyglassItem.PrefabName ? SpyglassItem.Prefab : null);

            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Zoom.Update();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Zoom.Reset();
            Sound.Reset();
            Content.RemoveAll(keepItems: ItemReload.KeepOnUnload);
            Synced?.Dispose();
            Translations.Unload();
            SpyglassItem.Forget();
            Instance = null;
            Synced = null;
        }
    }
}
