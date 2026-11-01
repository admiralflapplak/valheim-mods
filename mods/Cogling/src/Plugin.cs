using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

// The Cogling piece is registered through PieceReload (kept registered across a reload, standing ones given the new copy's body), so
// the mod can be reloaded in a world
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Cogling
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.Cogling";
        public const string Name = "Cogling";
        public const string Version = "0.9.6";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Building (the server's: the client pays when it builds, so it must charge the server's cost)
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<string> Station;
        internal static ConfigEntry<float> Height;
        internal static ConfigEntry<float> WalkSpeed;

        // 2 - Antenna (the server's)
        internal static ConfigEntry<string> AntennaCost;
        internal static ConfigEntry<string> AntennaStation;
        internal static ConfigEntry<float> AntennaRadius;
        internal static ConfigEntry<float> AntennaSize;
        internal static ConfigEntry<float> DishTilt;

        // 4 - Voice (each player's own)
        internal static ConfigEntry<float> VoicePitch, VoiceRobot, VoiceRing, VoiceCrunch, VoiceVolume, BabbleEvery;

        // 9 - Testing (each player's own, for choosing the look)
        internal static ConfigEntry<ItemDrop.ItemData.AnimationState> Stance;
        internal static ConfigEntry<AnimationSet> Animations;
        internal static ConfigEntry<float> StrollEvery;

        // 6 - Floor items (the server's: the owner of a Cogling decides what he takes)
        internal static ConfigEntry<float> DropWait, MaxCarryWeight, ColdEggWait;
        internal static ConfigEntry<bool> CollectColdEggs;
        internal static ConfigEntry<bool> LogAI;
        internal static ConfigEntry<float> CarryArmsUp, CarryArmsForward, CarryElbows, CarryOut, CarryHeight, CarryItemSize, CarryItemTurn;
        internal static ConfigEntry<bool> CarryPreview, Paused, UseIdle;
        internal static ConfigEntry<float> EyeGlow;
        internal static ConfigEntry<UnityEngine.Color> EyeColor;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            Cost = Synced.Bind("1 - Building", "Cost", "Copper:10, BronzeNails:10, FineWood:5",
                "What a Cogling costs to build (all of it back when it's taken down with the hammer). " + Costs.Help);
            Station = Synced.Bind("1 - Building", "Station", "piece_workbench",
                "The crafting station that must be near to build one. " + Costs.StationHelp);
            Height = Synced.Bind("1 - Building", "Height", 0.85f,
                "How tall a Cogling stands, in metres (a Dverger is about 1.45). Takes effect when the game restarts.",
                new AcceptableValueRange<float>(0.4f, 1.4f));
            AntennaCost = Synced.Bind("2 - Antenna", "Cost", "Copper:5, FineWood:2, Resin:3",
                "What a Cogling Antenna costs to build. " + Costs.Help);
            AntennaStation = Synced.Bind("2 - Antenna", "Station", "piece_workbench",
                "The crafting station that must be near to build one. " + Costs.StationHelp);
            AntennaRadius = Synced.Bind("2 - Antenna", "Radius", 20f,
                "How far around its antenna a Cogling works, in metres. Antennas whose areas overlap link into one area.",
                new AcceptableValueRange<float>(5f, 50f));
            AntennaSize = Synced.Bind("2 - Antenna", "Size", 0.75f,
                "How big the antenna is (1 = the size of the Dvergr wisp light it's made from). Changes the antennas standing at once.",
                new AcceptableValueRange<float>(0.5f, 3f));
            DishTilt = Config.Bind("3 - Looks", "Antenna dish tilt", 35f, new ConfigDescription(
                "How far the antenna's dish leans toward its front, in degrees (each player's own look).", new AcceptableValueRange<float>(0f, 80f)));
            VoicePitch = Config.Bind("4 - Voice", "Pitch", 2f, new ConfigDescription("How high his voice is (1: a Dverger's).", new AcceptableValueRange<float>(0.5f, 3f)));
            VoiceRobot = Config.Bind("4 - Voice", "Robot", 0.6f, new ConfigDescription("How much robot is in his voice (ring modulation).", new AcceptableValueRange<float>(0f, 1f)));
            VoiceRing = Config.Bind("4 - Voice", "Robot tone", 90f, new ConfigDescription("The robot effect's tone in Hz: low buzzes, high rings.", new AcceptableValueRange<float>(10f, 400f)));
            VoiceCrunch = Config.Bind("4 - Voice", "Crunch", 0.2f, new ConfigDescription("How crunchy and distorted his voice is.", new AcceptableValueRange<float>(0f, 1f)));
            VoiceVolume = Config.Bind("4 - Voice", "Volume", 1f, new ConfigDescription("How loud he is.", new AcceptableValueRange<float>(0f, 1f)));
            BabbleEvery = Config.Bind("4 - Voice", "Babble every", 30f, new ConfigDescription("About how often he babbles, in seconds.", new AcceptableValueRange<float>(2f, 300f)));
            Animations = Config.Bind("9 - Testing", "Animation set", AnimationSet.Fuling,
                "Whose animations the Cogling moves with (they must be humanoid ones; the log says which are). Changes every Cogling here at once.");
            Stance = Config.Bind("9 - Testing", "Stance", ItemDrop.ItemData.AnimationState.Unarmed,
                "Which of the Dverger's animation sets the Cogling moves with (a Dverger's stance comes from what it holds: Crossbow for the "
                + "rogues, Staves for the mages). Changes the Coglings around you at once.");
            EyeColor = Config.Bind("3 - Looks", "Eye colour", new UnityEngine.Color(0f, 0.792f, 1f),
                "The colour of the Coglings' eyes (each player's own).");
            EyeGlow = Config.Bind("3 - Looks", "Eye glow", 3f, new ConfigDescription("How brightly the Coglings' eyes glow (0: not at all).",
                new AcceptableValueRange<float>(0f, 10f)));
            WalkSpeed = Synced.Bind("1 - Building", "Walk speed", 1.4f, "How fast a Cogling walks, in metres a second (a Dverger walks at 2).",
                new AcceptableValueRange<float>(0.5f, 4f));
            StrollEvery = Config.Bind("9 - Testing", "Stroll every", 25f, new ConfigDescription(
                "Testing his walking: about how often a resting Cogling strolls to a spot in his area and back, in seconds (0: never).",
                new AcceptableValueRange<float>(0f, 300f)));
            CarryArmsUp = Config.Bind("9 - Testing", "Carry: arms up", 0.193f, new ConfigDescription("His carry pose: how high the arms are (-1 down, 1 up).", new AcceptableValueRange<float>(-1f, 1f)));
            CarryArmsForward = Config.Bind("9 - Testing", "Carry: arms forward", -0.425f, new ConfigDescription("His carry pose: how far forward the arms reach (on his rig -1 is forward, 1 back).", new AcceptableValueRange<float>(-1f, 1f)));
            CarryElbows = Config.Bind("9 - Testing", "Carry: elbows", 0.799f, new ConfigDescription("His carry pose: elbows (-1 bent, 1 straight).", new AcceptableValueRange<float>(-1f, 1f)));
            CarryOut = Config.Bind("9 - Testing", "Carry: item out", 0.126f, new ConfigDescription("The carried item further out from his hands (in his heights).", new AcceptableValueRange<float>(-0.3f, 0.5f)));
            CarryHeight = Config.Bind("9 - Testing", "Carry: item height", 0.045f, new ConfigDescription("The carried item above (+) or below (-) his hands (in his heights).", new AcceptableValueRange<float>(-0.6f, 0.4f)));
            CarryItemSize = Config.Bind("9 - Testing", "Carry: item size", 0.6f, new ConfigDescription("The biggest a carried item looks: its longest side at most this share of his height (smaller items keep their own size).", new AcceptableValueRange<float>(0.1f, 1f)));
            CarryItemTurn = Config.Bind("9 - Testing", "Carry: item turn", 82.572f, new ConfigDescription("Turns the carried item around the up axis, in degrees (90: a log held across instead of pointing ahead).", new AcceptableValueRange<float>(-180f, 180f)));
            CarryPreview = Config.Bind("9 - Testing", "Carry: preview", false, "Show every Cogling carrying a log, to tune the carry pose by eye.");
            UseIdle = Config.Bind("9 - Testing", "Own idle", true, "Standing still, he plays his own idle animation (reference/Idle.fbx) instead of the Fuling's.");
            Paused = Config.Bind("9 - Testing", "Pause Coglings", false, "Every Cogling stands still and faces you (to look at him while you change the sliders).");
            LogAI = Config.Bind("9 - Testing", "Log what he does", true, "Write each Cogling's decisions to the log (testing).");
            DropWait = Synced.Bind("6 - Floor items", "Wait after a drop", 60f,
                "Seconds he leaves an item alone after he first sees it on the floor (you may have dropped it on purpose).", new AcceptableValueRange<float>(0f, 600f));
            MaxCarryWeight = Synced.Bind("6 - Floor items", "Heaviest he carries", 50f,
                "The heaviest stack he picks up (weight). The dragon egg is 200.", new AcceptableValueRange<float>(1f, 500f));
            CollectColdEggs = Synced.Bind("6 - Floor items", "Collect cold eggs", true,
                "Eggs warm enough to hatch (near a fire, under a roof) are always left alone. Cold eggs: collected once they've stayed cold a while.");
            ColdEggWait = Synced.Bind("6 - Floor items", "Cold egg wait", 3f,
                "Minutes an egg must stay cold before he collects it (time to move it somewhere warm).", new AcceptableValueRange<float>(0f, 60f));
            Sorting.Bind(Config);
            Config.SaveOnConfigSet = true;
            Config.Save();
            Cost.SettingChanged += (s, e) => CoglingPrefab.ApplyCost();
            Station.SettingChanged += (s, e) => CoglingPrefab.ApplyCost();
            AntennaCost.SettingChanged += (s, e) => Antenna.ApplyCost();
            AntennaStation.SettingChanged += (s, e) => Antenna.ApplyCost();
            AntennaSize.SettingChanged += (s, e) => Antenna.ApplySize();
            Stance.SettingChanged += (s, e) => CoglingStance.RefreshAll();
            Animations.SettingChanged += (s, e) => CoglingAnimations.Apply();
            EyeGlow.SettingChanged += (s, e) => CoglingBody.UpdateEyes();
            EyeColor.SettingChanged += (s, e) => CoglingBody.UpdateEyes();
            DishTilt.SettingChanged += (s, e) => Antenna.ApplyTilt();
            foreach (var voice in new[] { VoicePitch, VoiceRobot, VoiceRing, VoiceCrunch, VoiceVolume }) voice.SettingChanged += (s, e) => CoglingVoice.TryNow();

            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            Commands.Register();
            if (ZNetScene.instance != null) Chests.FindLoaded();   // (after a reload: the chests already here)
            Antenna.Register(ZNetScene.instance);
            CoglingCollapse.Register(ZNetScene.instance);
            CoglingPrefab.Register(ZNetScene.instance);   // after a reload at the main menu: the scene is already there
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update() => Synced?.Update();

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Commands.Unregister();
            Chests.Forget();
            FloorItems.Forget();
            CoglingPrefab.Unregister();
            Antenna.Unregister();
            CoglingCollapse.Forget();
            CoglingVoice.Forget();
            CoglingClip.Forget();
            CoglingBody.Forget();
            CoglingModel.Forget();
            Synced?.Dispose();
            Translations.Unload();
            Synced = null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance)
        {
            Antenna.Register(__instance);
            CoglingCollapse.Register(__instance);
            CoglingPrefab.Register(__instance);
        }
    }
}
