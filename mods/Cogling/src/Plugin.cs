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
        public const string Version = "0.27.3";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Building (the server's: the client pays when it builds, so it must charge the server's cost)
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<string> Station;
        // tuned by eye in the game (a "9 - Testing" config section while the mod was made): fixed values now
        internal static readonly Fixed<float> AntennaLightHeight = new Fixed<float>(0.041f);   // Antenna light: height
        internal static readonly Fixed<float> AntennaLightForward = new Fixed<float>(0f);   // Antenna light: forward
        internal static readonly Fixed<float> AntennaLightSide = new Fixed<float>(0f);   // Antenna light: side
        internal static readonly Fixed<AnimationSet> Animations = new Fixed<AnimationSet>(AnimationSet.Fuling);   // Animation set
        internal static readonly Fixed<ItemDrop.ItemData.AnimationState> Stance = new Fixed<ItemDrop.ItemData.AnimationState>(ItemDrop.ItemData.AnimationState.Unarmed);   // Stance
        internal static readonly Fixed<float> StrollEvery = new Fixed<float>(0f);   // Stroll every
        internal static readonly Fixed<float> CarryRaise = new Fixed<float>(83f);   // Carry: arm raise
        internal static readonly Fixed<float> CarryOutward = new Fixed<float>(0f);   // Carry: arm out
        internal static readonly Fixed<float> CarryTwist = new Fixed<float>(0f);   // Carry: arm twist
        internal static readonly Fixed<float> CarryElbow = new Fixed<float>(16f);   // Carry: elbow bend
        internal static readonly Fixed<float> CarryWrist = new Fixed<float>(0f);   // Carry: wrist bend
        internal static readonly Fixed<float> CarryOut = new Fixed<float>(0.126f);   // Carry: item out
        internal static readonly Fixed<float> CarryHeight = new Fixed<float>(0.045f);   // Carry: item height
        internal static readonly Fixed<float> CarryItemSize = new Fixed<float>(0.6f);   // Carry: item size
        internal static readonly Fixed<float> CarryItemTurn = new Fixed<float>(82.572f);   // Carry: item turn
        internal static readonly Fixed<bool> BendPreview = new Fixed<bool>(false);   // Bend: preview
        internal static readonly Fixed<float> BendSpine = new Fixed<float>(-0.9f);   // Bend: spine
        internal static readonly Fixed<float> BendArms = new Fixed<float>(-0.6f);   // Bend: arms
        internal static readonly Fixed<bool> CarryPreview = new Fixed<bool>(false);   // Carry: preview
        internal static readonly Fixed<bool> UseIdle = new Fixed<bool>(true);   // Own idle
        internal static readonly Fixed<bool> LogGround = new Fixed<bool>(false);   // Log its footing
        internal static readonly Fixed<bool> PowerPreview = new Fixed<bool>(false);   // Power: preview down
        internal static readonly Fixed<float> PowerFrame = new Fixed<float>(0.6f);   // Power: frame
        internal static readonly Fixed<bool> HammerPreview = new Fixed<bool>(false);   // Hammer: preview
        internal static readonly Fixed<float> HammerX = new Fixed<float>(-0.857f);   // Hammer: x
        internal static readonly Fixed<float> HammerY = new Fixed<float>(2.308f);   // Hammer: y
        internal static readonly Fixed<float> HammerZ = new Fixed<float>(-6.309f);   // Hammer: z
        internal static readonly Fixed<float> HammerTurnX = new Fixed<float>(86.359f);   // Hammer: turn x
        internal static readonly Fixed<float> HammerTurnY = new Fixed<float>(-91.801f);   // Hammer: turn y
        internal static readonly Fixed<float> HammerTurnZ = new Fixed<float>(-6.358f);   // Hammer: turn z
        internal static readonly Fixed<float> HammerSize = new Fixed<float>(0.624f);   // Hammer: size
        internal static readonly Fixed<float> ChairStandOff = new Fixed<float>(0.4f);   // Chair: stand off
        internal static readonly Fixed<float> WatchTurnAfter = new Fixed<float>(55f);   // Watch: turn after
        internal static readonly Fixed<float> ChairBlendOut = new Fixed<float>(0.5f);   // Chair: blend out
        internal static readonly Fixed<float> ChairHeight = new Fixed<float>(0.057f);   // Chair: height
        internal static readonly Fixed<float> ChairBack = new Fixed<float>(-0.14f);   // Chair: back
        internal static readonly Fixed<float> ChairHop = new Fixed<float>(0.15f);   // Chair: hop
        internal static readonly Fixed<float> SitDownUntil = new Fixed<float>(0.8f);   // Sit: down clip until
        internal static readonly Fixed<float> SitBlend = new Fixed<float>(0.5f);   // Sit: blend
        internal static readonly Fixed<float> SitHeight = new Fixed<float>(0f);   // Sit: height
        internal static readonly Fixed<float> SitForward = new Fixed<float>(0f);   // Sit: forward
        internal static readonly Fixed<float> SitStandSpeed = new Fixed<float>(1f);   // Sit: stand up speed
        internal static readonly Fixed<bool> SwingFreeze = new Fixed<bool>(false);   // Swing: freeze
        internal static readonly Fixed<float> SwingFrozenAt = new Fixed<float>(0f);   // Swing: frozen at
        internal static readonly Fixed<float> SwingRaise = new Fixed<float>(80f);   // Swing: upper arm raise
        internal static readonly Fixed<float> SwingOut = new Fixed<float>(15f);   // Swing: upper arm out
        internal static readonly Fixed<float> SwingTwist = new Fixed<float>(46.274f);   // Swing: upper arm twist
        internal static readonly Fixed<float> SwingElbowHigh = new Fixed<float>(100f);   // Swing: elbow at the top
        internal static readonly Fixed<float> SwingElbowLow = new Fixed<float>(15f);   // Swing: elbow at the bottom
        internal static readonly Fixed<float> SwingWristHigh = new Fixed<float>(25f);   // Swing: wrist at the top
        internal static readonly Fixed<float> SwingWristLow = new Fixed<float>(-6.238f);   // Swing: wrist at the bottom
        internal static readonly Fixed<float> SwingHandTurn = new Fixed<float>(0f);   // Swing: forearm twist
        internal static readonly Fixed<float> SwingLeftRaise = new Fixed<float>(20f);   // Swing: left arm raise
        internal static readonly Fixed<float> SwingLeftOut = new Fixed<float>(25f);   // Swing: left arm out
        internal static readonly Fixed<float> SwingLeftElbow = new Fixed<float>(30f);   // Swing: left elbow
        internal static readonly Fixed<float> SwingTime = new Fixed<float>(0.6f);   // Swing: seconds per knock
        internal static readonly Fixed<bool> Paused = new Fixed<bool>(false);   // Pause Coglings
        internal static readonly Fixed<bool> LogAI = new Fixed<bool>(false);   // Log what it does

        internal static ConfigEntry<float> Height;
        internal static ConfigEntry<float> WalkSpeed, RunSpeed, RebuildMinutes;
        internal static ConfigEntry<int> CoglingsPerAntenna;

        // 2 - Antenna (the server's)
        internal static ConfigEntry<string> AntennaCost;
        internal static ConfigEntry<string> AntennaStation;
        internal static ConfigEntry<float> AntennaRadius;
        internal static ConfigEntry<float> AntennaSize;
        internal static ConfigEntry<float> DishTilt;

        // 4 - Voice (each player's own)
        internal static ConfigEntry<float> VoicePitch, VoiceRobot, VoiceRing, VoiceCrunch, VoiceVolume;

        // 6 - Floor items (the server's: the owner of a Cogling decides what he takes)
        internal static ConfigEntry<float> DropWait, MaxCarryWeight, ColdEggWait;
        internal static ConfigEntry<bool> CollectColdEggs, ResortGroups, RepairBuildings, RepairGear;
        internal static ConfigEntry<float> RepairBelow;
        internal static ConfigEntry<bool> NeverPowerDown;
        
        internal static ConfigEntry<bool> DanceAudience, Slacking;
        internal static ConfigEntry<float> ChairChance, SlackEvery;
        
        internal static ConfigEntry<float> HappyIdleChance, PowerDownAfter;
        
        internal static ConfigEntry<float> EyeGlow, CopperShine, CopperMetal;
        internal static ConfigEntry<bool> AntennaMoves, AntennaLight;
        internal static ConfigEntry<float> AntennaMoveEvery, AntennaTurn, AntennaTip, AntennaSoundVolume, AntennaSoundPitch, AntennaLightBrightness, AntennaLightRange, AntennaLightSize;
        internal static ConfigEntry<UnityEngine.Color> EyeColor;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            Cost = Synced.Bind("1 - Building", "Cost", "Copper:10, BronzeNails:10, FineWood:5, Ectoplasm:5",
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
            CopperShine = Config.Bind("3 - Looks", "Copper shine", 0.75f, new ConfigDescription("How glossy the Coglings' copper is (1: as the game's copper bar; lower: duller, a softer highlight).", new AcceptableValueRange<float>(0f, 1.5f)));
            CopperMetal = Config.Bind("3 - Looks", "Copper metal", 1f, new ConfigDescription("How much the Coglings' copper mirrors what's around it (1: as the game's copper bar; lower: more like painted copper).", new AcceptableValueRange<float>(0f, 1.5f)));
            AntennaMoves = Config.Bind("3 - Looks", "Antenna moves", true, "Now and then an antenna's dish turns to a new heading and tips a little, with a creak.");
            AntennaMoveEvery = Config.Bind("3 - Looks", "Antenna moves every", 12f, new ConfigDescription("About how often an antenna moves, in seconds.", new AcceptableValueRange<float>(3f, 120f)));
            AntennaTurn = Config.Bind("3 - Looks", "Antenna turn", 90f, new ConfigDescription("How far the dish may turn either way from where it was built facing, in degrees.", new AcceptableValueRange<float>(0f, 180f)));
            AntennaTip = Config.Bind("3 - Looks", "Antenna tip", 12f, new ConfigDescription("How far the dish may tip up or down from its tilt, in degrees.", new AcceptableValueRange<float>(0f, 30f)));
            AntennaSoundVolume = Config.Bind("3 - Looks", "Antenna creak volume", 0.5f, new ConfigDescription("How loud the creak is when an antenna moves (0: silent).", new AcceptableValueRange<float>(0f, 1f)));
            AntennaSoundPitch = Config.Bind("3 - Looks", "Antenna creak pitch", 1.8f, new ConfigDescription("How high and fast the creak is (1: the Ashwood door's own). The move takes as long as the creak.", new AcceptableValueRange<float>(0.5f, 3f)));
            AntennaLight = Config.Bind("3 - Looks", "Antenna light", true, "A small lamp in the middle of the dish, in the colour of the nearest Cogling's eyes.");
            AntennaLightBrightness = Config.Bind("3 - Looks", "Antenna light brightness", 1.5f, new ConfigDescription("How much light the lamp gives (0: none, the bulb still glows).", new AcceptableValueRange<float>(0f, 5f)));
            AntennaLightRange = Config.Bind("3 - Looks", "Antenna light range", 2.5f, new ConfigDescription("How far the lamp's light reaches, in metres.", new AcceptableValueRange<float>(0.5f, 10f)));
            AntennaLightSize = Config.Bind("3 - Looks", "Antenna light size", 0.35f, new ConfigDescription("How big the bulb is (1: the wisp light's ball).", new AcceptableValueRange<float>(0.05f, 1.5f)));
            VoicePitch = Config.Bind("4 - Voice", "Pitch", 2f, new ConfigDescription("How high its voice is (1: a Dverger's).", new AcceptableValueRange<float>(0.5f, 3f)));
            VoiceRobot = Config.Bind("4 - Voice", "Robot", 0.6f, new ConfigDescription("How much robot is in its voice (ring modulation).", new AcceptableValueRange<float>(0f, 1f)));
            VoiceRing = Config.Bind("4 - Voice", "Robot tone", 90f, new ConfigDescription("The robot effect's tone in Hz: low buzzes, high rings.", new AcceptableValueRange<float>(10f, 400f)));
            VoiceCrunch = Config.Bind("4 - Voice", "Crunch", 0.2f, new ConfigDescription("How crunchy and distorted its voice is.", new AcceptableValueRange<float>(0f, 1f)));
            VoiceVolume = Config.Bind("4 - Voice", "Volume", 1f, new ConfigDescription("How loud it is.", new AcceptableValueRange<float>(0f, 1f)));
            EyeColor = Config.Bind("3 - Looks", "Eye colour", new UnityEngine.Color(0f, 0.792f, 1f),
                "The colour of the Coglings' eyes (each player's own).");
            EyeGlow = Config.Bind("3 - Looks", "Eye glow", 3f, new ConfigDescription("How brightly the Coglings' eyes glow (0: not at all).",
                new AcceptableValueRange<float>(0f, 10f)));
            WalkSpeed = Synced.Bind("1 - Building", "Walk speed", 1.4f, "How fast a Cogling walks, in metres a second (a Dverger walks at 2).",
                new AcceptableValueRange<float>(0.5f, 4f));
            RunSpeed = Synced.Bind("1 - Building", "Run speed", 3.5f, "How fast a frightened Cogling runs for cover, in metres a second.",
                new AcceptableValueRange<float>(1f, 8f));
            RebuildMinutes = Synced.Bind("1 - Building", "Rebuild minutes", 5f,
                "Minutes before the nearest antenna rebuilds a Cogling that broke down (broken by monsters or a fall: no cost back; taken down with the hammer: its cost back, no rebuild).",
                new AcceptableValueRange<float>(0.1f, 120f));
            CoglingsPerAntenna = Synced.Bind("2 - Antenna", "Coglings per antenna", 2,
                "How many Coglings each antenna powers: linked antennas power the sum. A Cogling can't be built past it (those being rebuilt count).",
                new AcceptableValueRange<int>(1, 20));
            HappyIdleChance = Config.Bind("3 - Looks", "Happy idle chance", 0.3f, new ConfigDescription("How often, standing about, it plays its happy idle instead of its usual one.", new AcceptableValueRange<float>(0f, 1f)));
            NeverPowerDown = Synced.Bind("1 - Building", "Never power down", false, "Coglings never power down: with nothing to do, they take a break instead (chat, dance, sit, look around...), so they're always working or slacking off. Needs Slacking off on.");
            PowerDownAfter = Synced.Bind("1 - Building", "Power down after", 10f, "Seconds with nothing to do at its antenna before it powers down (it boots up when there's work).", new AcceptableValueRange<float>(2f, 600f));
            Slacking = Config.Bind("8 - Slacking off", "Slacking off", true, "Now and then a Cogling takes a break, even with work to do: stares into the distance, looks around, is bored, dances, or sits on the ground for a while. Each has its own personality: some slack more than others. Never while frightened.");
            SlackEvery = Config.Bind("8 - Slacking off", "Every (minutes)", 4f, new ConfigDescription("How often an average Cogling takes a break, about (a lazy one more often, a hard worker less).", new AcceptableValueRange<float>(0.5f, 30f)));
            DanceAudience = Config.Bind("8 - Slacking off", "Dance audience", true, "When a Cogling dances, others near may come over: a playful one to dance along, a sociable one to clap and cheer.");
            ChairChance = Config.Bind("8 - Slacking off", "Sit on chairs", 0.6f, new ConfigDescription("Sitting down for a break with a free chair, stool or bench in its area: how likely the Cogling hops up onto it (else it sits on the ground).", new AcceptableValueRange<float>(0f, 1f)));
            DropWait = Synced.Bind("6 - Floor items", "Wait after a drop", 60f,
                "Seconds a Cogling leaves an item alone after first seeing it on the floor (you may have dropped it on purpose).", new AcceptableValueRange<float>(0f, 600f));
            MaxCarryWeight = Synced.Bind("6 - Floor items", "Heaviest it carries", 50f,
                "The most weight it carries in one trip: a heavier stack takes several trips, and a single item heavier than this (the dragon egg, 200) it leaves.", new AcceptableValueRange<float>(1f, 500f));
            CollectColdEggs = Synced.Bind("6 - Floor items", "Collect cold eggs", true,
                "Eggs warm enough to hatch (near a fire, under a roof) are always left alone. Cold eggs: collected once they've stayed cold a while.");
            ColdEggWait = Synced.Bind("6 - Floor items", "Cold egg wait", 3f,
                "Minutes an egg must stay cold before a Cogling collects it (time to move it somewhere warm).", new AcceptableValueRange<float>(0f, 60f));
            ResortGroups = Synced.Bind("5 - Sorting", "Re-sort by groups", true,
                "Besides gathering an item kept in several chests into the one with the most of it (always), move a lone stray (its only stack "
                + "in a chest with nothing of its group) to the chest where its group lives. Off: it never moves your one-off stacks (only gathers the same item together).");
            RepairBuildings = Synced.Bind("7 - Repairing", "Repair buildings", true,
                "With nothing to fetch or tidy, it repairs damaged building pieces in its area (as the hammer does, free; only where you may build).");
            RepairBelow = Synced.Bind("7 - Repairing", "Repair below", 90f,
                "A piece is repaired once its health is below this percent.", new AcceptableValueRange<float>(10f, 99f));
            RepairGear = Synced.Bind("7 - Repairing", "Repair gear", true,
                "It takes worn tools and gear from its chests to their crafting station (when the station stands in its area, at the level the item needs), mends them, and puts them back.");
            Sorting.Bind(Config);
            Config.SaveOnConfigSet = true;
            Config.Save();
            Cost.SettingChanged += (s, e) => CoglingPrefab.ApplyCost();
            Station.SettingChanged += (s, e) => CoglingPrefab.ApplyCost();
            AntennaCost.SettingChanged += (s, e) => Antenna.ApplyCost();
            AntennaStation.SettingChanged += (s, e) => Antenna.ApplyCost();
            AntennaSize.SettingChanged += (s, e) => Antenna.ApplySize();
            EyeGlow.SettingChanged += (s, e) => CoglingBody.UpdateEyes();
            EyeColor.SettingChanged += (s, e) => CoglingBody.UpdateEyes();
            CopperShine.SettingChanged += (s, e) => CoglingBody.ApplyCopper();
            CopperMetal.SettingChanged += (s, e) => CoglingBody.ApplyCopper();
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
            Repairs.Forget();
            CoglingSlack.Forget();
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

namespace Cogling
{
    /// <summary>A value that was a setting while the mod was made (tuned by eye), now fixed: read as a setting's .Value.</summary>
    internal sealed class Fixed<T>
    {
        internal Fixed(T value) { Value = value; }
        internal T Value { get; }
    }
}
