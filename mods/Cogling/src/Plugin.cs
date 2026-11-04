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
        public const string Version = "0.23.1";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Building (the server's: the client pays when it builds, so it must charge the server's cost)
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<string> Station;
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

        // 9 - Testing (each player's own, for choosing the look)
        internal static ConfigEntry<ItemDrop.ItemData.AnimationState> Stance;
        internal static ConfigEntry<AnimationSet> Animations;
        internal static ConfigEntry<float> StrollEvery;

        // 6 - Floor items (the server's: the owner of a Cogling decides what he takes)
        internal static ConfigEntry<float> DropWait, MaxCarryWeight, ColdEggWait;
        internal static ConfigEntry<bool> CollectColdEggs, ResortGroups, RepairBuildings, RepairGear;
        internal static ConfigEntry<float> RepairBelow;
        internal static ConfigEntry<bool> LogAI;
        internal static ConfigEntry<float> CarryRaise, CarryOutward, CarryTwist, CarryElbow, CarryWrist, CarryOut, CarryHeight, CarryItemSize, CarryItemTurn;
        internal static ConfigEntry<bool> DanceAudience, Slacking, SwingFreeze, CarryPreview, Paused, UseIdle, BendPreview, LogGround, PowerPreview, HammerPreview;
        internal static ConfigEntry<float> WatchTurnAfter, ChairBlendOut, ChairStandOff, ChairChance, ChairHeight, ChairBack, ChairHop, SlackEvery, SitDownUntil, SitBlend, SitHeight, SitForward, SitStandSpeed, SwingFrozenAt, SwingRaise, SwingOut, SwingTwist, SwingHandTurn, SwingWristHigh, SwingWristLow, SwingLeftRaise, SwingLeftOut, SwingLeftElbow, SwingElbowHigh, SwingElbowLow, SwingTime;
        internal static ConfigEntry<float> HammerX, HammerY, HammerZ, HammerTurnX, HammerTurnY, HammerTurnZ, HammerSize;
        internal static ConfigEntry<float> PowerFrame, HappyIdleChance, PowerDownAfter;
        internal static ConfigEntry<float> BendSpine, BendArms;
        internal static ConfigEntry<float> EyeGlow, CopperShine, CopperMetal;
        internal static ConfigEntry<bool> AntennaMoves, AntennaLight;
        internal static ConfigEntry<float> AntennaMoveEvery, AntennaTurn, AntennaTip, AntennaSoundVolume, AntennaSoundPitch, AntennaLightBrightness, AntennaLightRange, AntennaLightSize, AntennaLightHeight, AntennaLightForward, AntennaLightSide;
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
            AntennaLightHeight = Config.Bind("9 - Testing", "Antenna light: height", 0f, new ConfigDescription("The lamp moved up (+) or down (-) on the dish, in metres.", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            AntennaLightForward = Config.Bind("9 - Testing", "Antenna light: forward", 0f, new ConfigDescription("The lamp moved out of (+) or into (-) the dish, in metres.", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            AntennaLightSide = Config.Bind("9 - Testing", "Antenna light: side", 0f, new ConfigDescription("The lamp moved to one side (+) or the other (-) on the dish, in metres.", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            VoicePitch = Config.Bind("4 - Voice", "Pitch", 2f, new ConfigDescription("How high his voice is (1: a Dverger's).", new AcceptableValueRange<float>(0.5f, 3f)));
            VoiceRobot = Config.Bind("4 - Voice", "Robot", 0.6f, new ConfigDescription("How much robot is in his voice (ring modulation).", new AcceptableValueRange<float>(0f, 1f)));
            VoiceRing = Config.Bind("4 - Voice", "Robot tone", 90f, new ConfigDescription("The robot effect's tone in Hz: low buzzes, high rings.", new AcceptableValueRange<float>(10f, 400f)));
            VoiceCrunch = Config.Bind("4 - Voice", "Crunch", 0.2f, new ConfigDescription("How crunchy and distorted his voice is.", new AcceptableValueRange<float>(0f, 1f)));
            VoiceVolume = Config.Bind("4 - Voice", "Volume", 1f, new ConfigDescription("How loud he is.", new AcceptableValueRange<float>(0f, 1f)));
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
            RunSpeed = Synced.Bind("1 - Building", "Run speed", 3.5f, "How fast a frightened Cogling runs for cover, in metres a second.",
                new AcceptableValueRange<float>(1f, 8f));
            RebuildMinutes = Synced.Bind("1 - Building", "Rebuild minutes", 5f,
                "Minutes before the nearest antenna rebuilds a Cogling that broke down (broken by monsters or a fall: no cost back; taken down with the hammer: his cost back, no rebuild).",
                new AcceptableValueRange<float>(0.1f, 120f));
            CoglingsPerAntenna = Synced.Bind("2 - Antenna", "Coglings per antenna", 2,
                "How many Coglings each antenna powers: linked antennas power the sum. A Cogling can't be built past it (those being rebuilt count).",
                new AcceptableValueRange<int>(1, 20));
            StrollEvery = Config.Bind("9 - Testing", "Stroll every", 0f, new ConfigDescription(
                "Testing his walking: about how often a resting Cogling strolls to a spot in his area and back, in seconds (0: never).",
                new AcceptableValueRange<float>(0f, 300f)));
            CarryRaise = Config.Bind("9 - Testing", "Carry: arm raise", 83f, new ConfigDescription("His carry pose: both upper arms, degrees up from hanging down (90: straight ahead, level).", new AcceptableValueRange<float>(0f, 180f)));
            CarryOutward = Config.Bind("9 - Testing", "Carry: arm out", 19f, new ConfigDescription("His carry pose: upper arms turned out to his sides, degrees from straight ahead (below 0: in toward each other).", new AcceptableValueRange<float>(-60f, 90f)));
            CarryTwist = Config.Bind("9 - Testing", "Carry: arm twist", 0f, new ConfigDescription("His carry pose: upper arms twisted about themselves, degrees (mirrored on the left).", new AcceptableValueRange<float>(-180f, 180f)));
            CarryElbow = Config.Bind("9 - Testing", "Carry: elbow bend", 16f, new ConfigDescription("His carry pose: elbow bend, degrees (0: straight).", new AcceptableValueRange<float>(-30f, 160f)));
            CarryWrist = Config.Bind("9 - Testing", "Carry: wrist bend", 0f, new ConfigDescription("His carry pose: wrists bent up from the forearms, degrees.", new AcceptableValueRange<float>(-90f, 90f)));
            CarryOut = Config.Bind("9 - Testing", "Carry: item out", 0.126f, new ConfigDescription("The carried item further out from his hands (in his heights).", new AcceptableValueRange<float>(-0.3f, 0.5f)));
            CarryHeight = Config.Bind("9 - Testing", "Carry: item height", 0.045f, new ConfigDescription("The carried item above (+) or below (-) his hands (in his heights).", new AcceptableValueRange<float>(-0.6f, 0.4f)));
            CarryItemSize = Config.Bind("9 - Testing", "Carry: item size", 0.6f, new ConfigDescription("The biggest a carried item looks: its longest side at most this share of his height (smaller items keep their own size).", new AcceptableValueRange<float>(0.1f, 1f)));
            CarryItemTurn = Config.Bind("9 - Testing", "Carry: item turn", 82.572f, new ConfigDescription("Turns the carried item around the up axis, in degrees (90: a log held across instead of pointing ahead).", new AcceptableValueRange<float>(-180f, 180f)));
            BendPreview = Config.Bind("9 - Testing", "Bend: preview", false, "Show every Cogling bent down as when he picks something up.");
            BendSpine = Config.Bind("9 - Testing", "Bend: spine", -0.9f, new ConfigDescription("How far he bends at the spine to pick up (on his rig -1 is forward, 1 leans back).", new AcceptableValueRange<float>(-1f, 1f)));
            BendArms = Config.Bind("9 - Testing", "Bend: arms", -0.6f, new ConfigDescription("Where his arms reach when he bends (on his rig -1 is forward).", new AcceptableValueRange<float>(-1f, 1f)));
            CarryPreview = Config.Bind("9 - Testing", "Carry: preview", false, "Show every Cogling carrying a log, to tune the carry pose by eye.");
            UseIdle = Config.Bind("9 - Testing", "Own idle", true, "Standing still, he plays his own idle animation (reference/Idle.fbx) instead of the Fuling's.");
            LogGround = Config.Bind("9 - Testing", "Log his footing", false, "Write how his body is lifted onto his feet to the log, ten times a second (testing a bounce).");
            HappyIdleChance = Config.Bind("3 - Looks", "Happy idle chance", 0.3f, new ConfigDescription("How often, standing about, he plays his happy idle instead of his usual one.", new AcceptableValueRange<float>(0f, 1f)));
            PowerDownAfter = Synced.Bind("1 - Building", "Power down after", 10f, "Seconds with nothing to do at his antenna before he powers down (he boots up when there's work).", new AcceptableValueRange<float>(2f, 600f));
            PowerPreview = Config.Bind("9 - Testing", "Power: preview down", false, "Show every Cogling powered down (the frozen frame of his sad idle).");
            PowerFrame = Config.Bind("9 - Testing", "Power: frame", 0.6f, new ConfigDescription("Which moment of his sad idle he freezes on when powered down (0 start, 1 end).", new AcceptableValueRange<float>(0f, 1f)));
            HammerPreview = Config.Bind("9 - Testing", "Hammer: preview", false, "Show every Cogling hammering, with the hammer in his hand, to place it by eye.");
            HammerX = Config.Bind("9 - Testing", "Hammer: x", -0.857f, new ConfigDescription("The hammer in his hand: sideways, in cm.", new AcceptableValueRange<float>(-30f, 30f)));
            HammerY = Config.Bind("9 - Testing", "Hammer: y", 2.308f, new ConfigDescription("The hammer in his hand: up and down, in cm.", new AcceptableValueRange<float>(-30f, 30f)));
            HammerZ = Config.Bind("9 - Testing", "Hammer: z", -6.309f, new ConfigDescription("The hammer in his hand: forward and back, in cm.", new AcceptableValueRange<float>(-30f, 30f)));
            HammerTurnX = Config.Bind("9 - Testing", "Hammer: turn x", 86.359f, new ConfigDescription("The hammer in his hand: turned about x, degrees.", new AcceptableValueRange<float>(-180f, 180f)));
            HammerTurnY = Config.Bind("9 - Testing", "Hammer: turn y", -91.801f, new ConfigDescription("The hammer in his hand: turned about y, degrees.", new AcceptableValueRange<float>(-180f, 180f)));
            HammerTurnZ = Config.Bind("9 - Testing", "Hammer: turn z", -6.358f, new ConfigDescription("The hammer in his hand: turned about z, degrees.", new AcceptableValueRange<float>(-180f, 180f)));
            HammerSize = Config.Bind("9 - Testing", "Hammer: size", 0.624f, new ConfigDescription("The hammer's size (1: a player's).", new AcceptableValueRange<float>(0.1f, 1.5f)));
            Slacking = Config.Bind("8 - Slacking off", "Slacking off", true, "Now and then a Cogling takes a break, even with work to do: stares into the distance, looks around, is bored, dances, or sits on the ground for a while. Each has his own personality: some slack more than others. Never while frightened.");
            SlackEvery = Config.Bind("8 - Slacking off", "Every (minutes)", 4f, new ConfigDescription("How often an average Cogling takes a break, about (a lazy one more often, a hard worker less).", new AcceptableValueRange<float>(0.5f, 30f)));
            DanceAudience = Config.Bind("8 - Slacking off", "Dance audience", true, "When a Cogling dances, others near may come over: a playful one to dance along, a sociable one to clap and cheer.");
            ChairChance = Config.Bind("8 - Slacking off", "Sit on chairs", 0.6f, new ConfigDescription("Sitting down for a break with a free chair, stool or bench in his area: how likely he hops up onto it (else he sits on the ground).", new AcceptableValueRange<float>(0f, 1f)));
            ChairStandOff = Config.Bind("9 - Testing", "Chair: stand off", 0.3f, new ConfigDescription("Sitting on a chair: how far in front of the seat's middle he stands before he hops up (and lands when he hops down), in metres.", new AcceptableValueRange<float>(0.1f, 0.8f)));
            WatchTurnAfter = Config.Bind("9 - Testing", "Watch: turn after", 55f, new ConfigDescription("Watching you: how far off to one side you get (degrees) before his body turns to catch up with his head.", new AcceptableValueRange<float>(15f, 70f)));
            ChairBlendOut = Config.Bind("9 - Testing", "Chair: blend out", 0.5f, new ConfigDescription("Hopping down: over how much of the end of the clip (seconds) he blends back into his idle.", new AcceptableValueRange<float>(0.1f, 0.8f)));
            ChairHeight = Config.Bind("9 - Testing", "Chair: height", 0.05f, new ConfigDescription("On a chair: his hips above the seat, in metres (if he sinks into it or floats).", new AcceptableValueRange<float>(-0.4f, 0.6f)));
            ChairBack = Config.Bind("9 - Testing", "Chair: back", 0.05f, new ConfigDescription("On a chair: his hips moved back (+) or forward (-) from the seat's middle, in metres.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
            ChairHop = Config.Bind("9 - Testing", "Chair: hop", 0.15f, new ConfigDescription("Hopping up onto a chair and down off it: how high the hop goes over the way there, in metres.", new AcceptableValueRange<float>(0f, 0.6f)));
            SitDownUntil = Config.Bind("9 - Testing", "Sit: down clip until", 0.8f, new ConfigDescription("Sitting down: how far into the sit-down clip he goes (seconds) before he is in the sitting pose.", new AcceptableValueRange<float>(0.2f, 2.3f)));
            SitBlend = Config.Bind("9 - Testing", "Sit: blend", 0.5f, new ConfigDescription("Sitting down: how long the sit-down clip blends into the sitting pose, in seconds.", new AcceptableValueRange<float>(0.05f, 1.5f)));
            SitHeight = Config.Bind("9 - Testing", "Sit: height", 0f, new ConfigDescription("Sitting: his body raised (+) or lowered (-), in metres (if he sinks into the ground or floats).", new AcceptableValueRange<float>(-0.6f, 0.6f)));
            SitForward = Config.Bind("9 - Testing", "Sit: forward", 0f, new ConfigDescription("Sitting: his body moved forward (+) or back (-), in metres.", new AcceptableValueRange<float>(-0.6f, 0.6f)));
            SitStandSpeed = Config.Bind("9 - Testing", "Sit: stand up speed", 1f, new ConfigDescription("How fast he gets up (1: the clip as made).", new AcceptableValueRange<float>(0.5f, 3f)));
            SwingFreeze = Config.Bind("9 - Testing", "Swing: freeze", false, "Hold the hammering swing still at one point (Swing: frozen at), to tune the arm there.");
            SwingFrozenAt = Config.Bind("9 - Testing", "Swing: frozen at", 1f, new ConfigDescription("Where the frozen swing stops: 1 is the top, 0 the knock, between them partway.", new AcceptableValueRange<float>(0f, 1f)));
            SwingRaise = Config.Bind("9 - Testing", "Swing: upper arm raise", 80f, new ConfigDescription("Hammering: his upper arm, held still: degrees up from hanging down (90: straight ahead, level).", new AcceptableValueRange<float>(0f, 180f)));
            SwingOut = Config.Bind("9 - Testing", "Swing: upper arm out", 15f, new ConfigDescription("Hammering: his upper arm turned out to his side, degrees from straight ahead (below 0: across his body).", new AcceptableValueRange<float>(-60f, 90f)));
            SwingTwist = Config.Bind("9 - Testing", "Swing: upper arm twist", 46.274f, new ConfigDescription("Hammering: his upper arm twisted about itself, degrees.", new AcceptableValueRange<float>(-180f, 180f)));
            SwingElbowHigh = Config.Bind("9 - Testing", "Swing: elbow at the top", 100f, new ConfigDescription("Hammering: elbow bend at the top of the swing, degrees (0: straight).", new AcceptableValueRange<float>(-30f, 160f)));
            SwingElbowLow = Config.Bind("9 - Testing", "Swing: elbow at the bottom", 15f, new ConfigDescription("Hammering: elbow bend at the knock, degrees.", new AcceptableValueRange<float>(-30f, 160f)));
            SwingWristHigh = Config.Bind("9 - Testing", "Swing: wrist at the top", 25f, new ConfigDescription("Hammering: wrist bent back at the top of the swing, degrees (with the bottom one: a flick).", new AcceptableValueRange<float>(-90f, 90f)));
            SwingWristLow = Config.Bind("9 - Testing", "Swing: wrist at the bottom", -6.238f, new ConfigDescription("Hammering: wrist bend at the knock, degrees.", new AcceptableValueRange<float>(-90f, 90f)));
            SwingHandTurn = Config.Bind("9 - Testing", "Swing: forearm twist", 0f, new ConfigDescription("Hammering: his forearm and hand turned about the forearm, degrees (turns the hammer face).", new AcceptableValueRange<float>(-180f, 180f)));
            SwingLeftRaise = Config.Bind("9 - Testing", "Swing: left arm raise", 20f, new ConfigDescription("Hammering: his other arm, degrees up from hanging down.", new AcceptableValueRange<float>(0f, 180f)));
            SwingLeftOut = Config.Bind("9 - Testing", "Swing: left arm out", 25f, new ConfigDescription("Hammering: his other arm turned out to his side, degrees from straight ahead.", new AcceptableValueRange<float>(-60f, 90f)));
            SwingLeftElbow = Config.Bind("9 - Testing", "Swing: left elbow", 30f, new ConfigDescription("Hammering: his other elbow bend, degrees.", new AcceptableValueRange<float>(-30f, 160f)));
            SwingTime = Config.Bind("9 - Testing", "Swing: seconds per knock", 0.6f, new ConfigDescription("Hammering: how long one swing takes (the knock sound follows it).", new AcceptableValueRange<float>(0.2f, 2f)));
            Paused = Config.Bind("9 - Testing", "Pause Coglings", false, "Every Cogling stands still and faces you (to look at him while you change the sliders).");
            LogAI = Config.Bind("9 - Testing", "Log what he does", true, "Write each Cogling's decisions to the log (testing).");
            DropWait = Synced.Bind("6 - Floor items", "Wait after a drop", 60f,
                "Seconds he leaves an item alone after he first sees it on the floor (you may have dropped it on purpose).", new AcceptableValueRange<float>(0f, 600f));
            MaxCarryWeight = Synced.Bind("6 - Floor items", "Heaviest he carries", 50f,
                "The most weight he carries in one trip: a heavier stack takes several trips, and a single item heavier than this (the dragon egg, 200) he leaves.", new AcceptableValueRange<float>(1f, 500f));
            CollectColdEggs = Synced.Bind("6 - Floor items", "Collect cold eggs", true,
                "Eggs warm enough to hatch (near a fire, under a roof) are always left alone. Cold eggs: collected once they've stayed cold a while.");
            ColdEggWait = Synced.Bind("6 - Floor items", "Cold egg wait", 3f,
                "Minutes an egg must stay cold before he collects it (time to move it somewhere warm).", new AcceptableValueRange<float>(0f, 60f));
            ResortGroups = Synced.Bind("5 - Sorting", "Re-sort by groups", true,
                "Besides gathering an item kept in several chests into the one with the most of it (always), move a lone stray (its only stack "
                + "in a chest with nothing of its group) to the chest where its group lives. Off: he never moves your one-off stacks (only gathers the same item together).");
            RepairBuildings = Synced.Bind("7 - Repairing", "Repair buildings", true,
                "With nothing to fetch or tidy, he repairs damaged building pieces in his area (as the hammer does, free; only where you may build).");
            RepairBelow = Synced.Bind("7 - Repairing", "Repair below", 90f,
                "A piece is repaired once its health is below this percent.", new AcceptableValueRange<float>(10f, 99f));
            RepairGear = Synced.Bind("7 - Repairing", "Repair gear", true,
                "He takes worn tools and gear from his chests to their crafting station (when it stands in his area, at the level it needs), mends them, and puts them back.");
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
            CopperShine.SettingChanged += (s, e) => CoglingBody.ApplyCopper();
            CopperMetal.SettingChanged += (s, e) => CoglingBody.ApplyCopper();
            DishTilt.SettingChanged += (s, e) => Antenna.ApplyTilt();
            AntennaLightHeight.SettingChanged += (s, e) => Antenna.ApplyLamp();
            AntennaLightForward.SettingChanged += (s, e) => Antenna.ApplyLamp();
            AntennaLightSide.SettingChanged += (s, e) => Antenna.ApplyLamp();
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
