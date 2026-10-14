using System.Collections.Generic;
using System.IO;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HeaveHo
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "nick.valheim.heaveho";
        public const string ModName = "Heave Ho";
        public const string ModVersion = "1.8.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Plugin Instance;

        // 1. Rowing
        internal static ConfigEntry<float> RowerStrength;
        internal static ConfigEntry<float> EasyMultiplier;
        internal static ConfigEntry<float> BackMultiplier;
        internal static ConfigEntry<float> TurnStrength;
        internal static ConfigEntry<float> MaxRowingSpeed;       // advanced
        internal static ConfigEntry<string> RowerBonuses;        // advanced

        // 2. Stamina
        internal static ConfigEntry<float> StaminaPerSecond;
        internal static ConfigEntry<float> EasyStaminaMultiplier;
        internal static ConfigEntry<float> RecoverStamina;

        // 3. Oars (all ships)
        internal static ConfigEntry<bool> ShowOars;
        internal static ConfigEntry<float> OarModelScale;
        internal static ConfigEntry<float> StrokesPerSecond;
        internal static ConfigEntry<float> OarLength;            // advanced (placeholder oar only)
        internal static ConfigEntry<bool> UseValheimShader;      // advanced

        // 4. Splashes
        internal static ConfigEntry<bool> Splashes;
        internal static ConfigEntry<string> EntryEffects;
        internal static ConfigEntry<string> ExitEffects;
        internal static ConfigEntry<float> SplashAmount;         // advanced

        // 5. Rower HUD
        internal static ConfigEntry<float> HudOutboard;
        internal static ConfigEntry<float> HudHeight;

        // 9. Advanced
        internal static ConfigEntry<string> RowingSeatAnimations;
        internal static ConfigEntry<KeyboardShortcut> RenderDumpKey;

        // Karve oarlock (hand-tuned) and the fallback for hulls that can't be measured.
        internal const float DefaultOutboard = 1.1f;
        internal const float DefaultHeight = 0.5f;

        /// <summary>Custom oar from BepInEx/plugins/HeaveHo/heaveho, or null to use placeholder primitives.</summary>
        internal static GameObject OarPrefab;

        /// <summary>Custom oar from BepInEx/plugins/HeaveHo/oar.obj (+ texture). Takes priority over the bundle.</summary>
        internal static Mesh OarMesh;
        internal static Texture2D OarTexture;

        private Harmony harmony;
        internal static HeaveHo.Shared.ServerSettings Synced;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            Synced = new HeaveHo.Shared.ServerSettings(ModGuid, Config, Logger);
            Cfg.Init(Config, Synced);
            BindSettings();

            if (!LoadOarObj()) LoadOarBundle();
            RowerHud.LoadIcon(AssetDir());

            harmony = new Harmony(ModGuid);
            harmony.PatchAll();
            RowerHud.Install(harmony); // patched separately so a missing HUD method can't break rowing
        }

        private static void BindSettings()
        {
            const string rowing = "1. Rowing", stamina = "2. Stamina", oars = "3. Oars (all ships)",
                         splashes = "4. Splashes", hud = "5. Rower HUD", adv = "9. Advanced";

            RowerStrength = Cfg.Bind(rowing, "Rower strength", 2.0f,
                "How hard rowers push. Acceleration (m/s²) at Pull hard when the crew's combined bonus is 1.0.",
                new AcceptableValueRange<float>(0f, 10f), from: "1. Rowing/Rower strength");
            EasyMultiplier = Cfg.Bind(rowing, "Easy stroke thrust", 0.5f,
                "Thrust of Easy stroke relative to Pull hard.",
                new AcceptableValueRange<float>(0f, 1f), from: "1. Rowing/Easy stroke thrust");
            BackMultiplier = Cfg.Bind(rowing, "Back water thrust", 0.6f,
                "Reverse thrust of Back water relative to Pull hard.",
                new AcceptableValueRange<float>(0f, 1f), from: "1. Rowing/Back water thrust");
            TurnStrength = Cfg.Bind(rowing, "Turning strength", 0.5f,
                "How much rowing on one side turns the ship. 0 = straight line only, 1 = full physics " +
                "(a lone rower pulls the ship in circles).",
                new AcceptableValueRange<float>(0f, 2f), from: "1. Rowing/Turning strength");
            MaxRowingSpeed = Cfg.Bind(rowing, "Max rowing speed", 12f,
                "Rowers stop adding thrust above this speed (m/s). Safety cap.",
                new AcceptableValueRange<float>(1f, 30f), advanced: true, from: "1. Rowing/Max rowing speed");
            RowerBonuses = Cfg.Bind(rowing, "Rower bonuses", "0.25,0.15,0.10,0.08,0.06,0.05",
                "Share of thrust each extra rower adds (diminishing returns). The last value repeats for larger crews.",
                advanced: true, from: "1. Rowing/Rower bonuses");

            StaminaPerSecond = Cfg.Bind(stamina, "Stamina per second", 3f,
                "Stamina spent per second at Pull hard.",
                new AcceptableValueRange<float>(0f, 20f), from: "2. Stamina/Stamina per second");
            EasyStaminaMultiplier = Cfg.Bind(stamina, "Easy / back water stamina", 0.4f,
                "Stamina cost of Easy stroke and Back water relative to Pull hard.",
                new AcceptableValueRange<float>(0f, 1f), from: "2. Stamina/Easy / back water stamina");
            RecoverStamina = Cfg.Bind(stamina, "Resume rowing at", 30f,
                "After running out, a rower rests until stamina recovers to this value.",
                new AcceptableValueRange<float>(1f, 100f), from: "2. Stamina/Resume rowing at");

            ShowOars = Cfg.Bind(oars, "Show oars", true,
                "Master switch for oar visuals on every ship (each ship also has its own switch).",
                from: "3. Oars/Show oars", local: true);
            OarModelScale = Cfg.Bind(oars, "Oar size", 1f,
                "Size multiplier for the oar model on every ship (each ship also has its own size).",
                new AcceptableValueRange<float>(0.1f, 3f), from: "3. Oars/Oar model scale");
            StrokesPerSecond = Cfg.Bind(oars, "Stroke rate", 0.55f,
                "Strokes per second at Easy stroke. Pull hard strokes faster.",
                new AcceptableValueRange<float>(0.1f, 2f), from: "3. Oars/Strokes per second");
            OarLength = Cfg.Bind(oars, "Placeholder oar length", 3.2f,
                "Outboard length (m) of the built-in placeholder oar, used only when no oar.obj is installed.",
                new AcceptableValueRange<float>(1f, 8f), advanced: true, from: "3. Oars/Oar length");
            UseValheimShader = Cfg.Bind(oars, "Use Valheim shader", true,
                "Draw asset-bundle oars with the ship's own shader (keeps your texture). Applies to newly loaded ships.",
                advanced: true, from: "3. Oars/Use Valheim shader");

            Splashes = Cfg.Bind(splashes, "Oar splashes", true,
                "Splash where each blade enters the water, and a smaller one as it lifts out.",
                from: "5. Splashes/Oar splashes", local: true);
            EntryEffects = Cfg.Bind(splashes, "Effects on blade entry", "vfx_water_surface_fish",
                "Comma-separated Valheim effects (visuals or sfx_ sounds) spawned where the blade bites. " +
                "Water-related names are listed in LogOutput.log under [Splash]. Live.",
                from: "5. Splashes/Extra effects on blade entry");
            ExitEffects = Cfg.Bind(splashes, "Effects on blade exit", "vfx_watersplash_bathtub",
                "Same, for when the blade lifts out. Live.",
                from: "5. Splashes/Extra effects on blade exit");
            SplashAmount = Cfg.Bind(splashes, "Wake splash amount", 0f,
                "Extra particles cloned from the ship's own wake effects (Karve/Longship only). 0 = off.",
                new AcceptableValueRange<float>(0f, 5f), advanced: true, from: "5. Splashes/Splash amount");

            HudOutboard = Cfg.Bind(hud, "Distance outboard", 0.5f,
                "How far past your oarlock (m) the rowing arrows float.",
                new AcceptableValueRange<float>(-3f, 3f), from: "4. HUD/Rower HUD distance outboard");
            HudHeight = Cfg.Bind(hud, "Height", 0.9f,
                "Height (m) of the rowing arrows above your oarlock.",
                new AcceptableValueRange<float>(-2f, 4f), from: "4. HUD/Rower HUD height");

            RowingSeatAnimations = Cfg.Bind(adv, "Rowing seat animations", "attach_sitship",
                "Seat animations that count as rowing seats (helm and mast holds use others). Seats are listed in " +
                "LogOutput.log per ship. Applies to newly loaded ships; all players should match.",
                advanced: true, from: "0. Seats/Rowing seat animations");
            RenderDumpKey = Cfg.Bind(adv, "Render dump key", new KeyboardShortcut(KeyCode.F8),
                "Debug: writes BepInEx/HeaveHo_render_dump.txt about the nearest ship's rendering.",
                advanced: true, from: "9. Debug/Render dump key", local: true);

            Cfg.PurgeOldSections();
        }

        /// <summary>
        /// Folder holding oar.obj / oarIcon.png / the bundle. Next to the dll normally; a dev-loaded dll (BepInEx/scripts)
        /// is read from bytes and has no location, so fall back to the scripts and plugin folders.
        /// </summary>
        private string AssetDir()
        {
            string here = string.IsNullOrEmpty(Info.Location) ? "" : Path.GetDirectoryName(Info.Location) ?? "";
            var dirs = new List<string>();
            if (here != "") dirs.Add(here);
            dirs.Add(Path.Combine(Paths.BepInExRootPath, "scripts"));
            dirs.Add(Path.Combine(Paths.PluginPath, "HeaveHo"));
            foreach (string d in dirs)
                foreach (string f in new[] { "oar.obj", "oarIcon.png", "heaveho" })
                    if (File.Exists(Path.Combine(d, f))) return d;
            return here;
        }

        private bool LoadOarObj()
        {
            string path = Path.Combine(AssetDir(), "oar.obj");
            if (!File.Exists(path)) return false;

            try
            {
                OarMesh = ObjLoader.Load(path);
                OarMesh.name = "HeaveHo_Oar";
                OarTexture = ObjLoader.LoadTexture(path);
                Log.LogInfo($"Loaded custom oar from oar.obj ({OarMesh.vertexCount} verts, " +
                            $"bounds {OarMesh.bounds.min} to {OarMesh.bounds.max}), texture: " +
                            (OarTexture != null ? OarTexture.name : "none found"));
                return true;
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Couldn't load oar.obj: " + e.Message);
                OarMesh = null;
                return false;
            }
        }

        private void LoadOarBundle()
        {
            string path = Path.Combine(AssetDir(), "heaveho");
            if (!File.Exists(path))
            {
                Log.LogInfo("No 'heaveho' asset bundle next to the dll; using placeholder oars.");
                return;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Log.LogWarning("Couldn't load 'heaveho' asset bundle (built with a different Unity version?). Using placeholder oars.");
                return;
            }

            OarPrefab = bundle.LoadAsset<GameObject>("HeaveHo_Oar");
            if (OarPrefab == null)
            {
                Log.LogWarning("Bundle has no prefab named 'HeaveHo_Oar'. Assets in bundle: " +
                               string.Join(", ", bundle.GetAllAssetNames()) + ". Using placeholder oars.");
                return;
            }
            Log.LogInfo("Loaded custom oar model from 'heaveho'.");
        }

        internal delegate bool OarlockMeasure(out float distance, out float height);

        internal const string PlacementFixed = "Fixed";
        internal const string PlacementPerSeat = "Per seat";

        /// <summary>Per-ship F1 settings, section "6. Ship: &lt;name&gt;".</summary>
        internal class ShipOarSettings
        {
            public ConfigEntry<float> Outboard;      // Fixed: oarlock distance from centerline (also Per seat fallback)
            public ConfigEntry<float> Height;        // Fixed: oarlock height above the bench
            public ConfigEntry<string> Placement;    // Fixed / Per seat
            public ConfigEntry<float> PerSeatHeight; // Per seat: extra height above the measured rail
            public ConfigEntry<float> PerSeatOutboard; // Per seat: shift out (+) or in (-) from the measured rail
            public ConfigEntry<float> Spacing;       // Per seat: fore-aft spacing of oars sharing a thwart
            public ConfigEntry<float> Scale;         // oar size for this ship
            public ConfigEntry<bool> Visuals;        // oars + splashes shown
            public ConfigEntry<bool> Physics;        // rowing thrust, turning, stamina cost
        }

        // Ships that need different defaults. Players' saved values always win.
        private static bool IsDrakkar(string ship) => ship == "VikingShip_Ashlands";

        /// <summary>Friendly ship names for the F1 menu; unknown (modded) ships keep their prefab name.</summary>
        internal static string DisplayName(string ship)
        {
            switch (ship)
            {
                case "Karve": return "Karve";
                case "VikingShip": return "Longship";
                case "VikingShip_Ashlands": return "Drakkar";
                case "Raft": return "Raft";
                default: return ship;
            }
        }

        private static string ShipSection(string ship) => "6. Ship: " + DisplayName(ship);
        private static string OldShipKey(string ship, string key) => "3. Oars - " + ship + "/" + key;

        private static readonly Dictionary<string, ShipOarSettings> shipSettings = new Dictionary<string, ShipOarSettings>();
        private static readonly Dictionary<string, ConfigEntry<string>> seatFilters = new Dictionary<string, ConfigEntry<string>>();

        /// <summary>
        /// Which seats on this ship row, by name prefix. Empty = every seat with a rowing animation.
        /// The Drakkar defaults to its side seats ('sit_box'); its center cross-benches don't row. Helm seats never row.
        /// All players should match, since seat numbers are synced.
        /// </summary>
        internal static ConfigEntry<string> GetSeatNameFilter(string ship)
        {
            if (!seatFilters.TryGetValue(ship, out var entry))
            {
                entry = Cfg.Bind(ShipSection(ship), "Rowing seat names", IsDrakkar(ship) ? "sit_box" : "",
                    $"Seat name prefixes that row on the {DisplayName(ship)} (e.g. sit_box). Empty = all seats with a " +
                    "rowing animation. Applies to newly loaded ships; all players should match.",
                    advanced: true, order: 10, from: OldShipKey(ship, "Rowing seat names"));
                seatFilters[ship] = entry;
                Cfg.Save();
            }
            return entry;
        }

        /// <summary>
        /// Per-ship oar settings, created the first time a ship type is seen. The Karve keeps its hand-tuned
        /// oarlock; other hulls default to a measurement of their rail.
        /// </summary>
        internal static ShipOarSettings GetShipSettings(string ship, OarlockMeasure measure)
        {
            if (shipSettings.TryGetValue(ship, out var cfg)) return cfg;

            float defDist = DefaultOutboard, defHeight = DefaultHeight;
            string how = "hand-tuned defaults";
            if (ship != "Karve" && measure(out float d, out float h))
            {
                defDist = d;
                defHeight = h;
                how = "measured from the hull";
            }

            string section = ShipSection(ship), name = DisplayName(ship);
            bool drakkar = IsDrakkar(ship);
            cfg = new ShipOarSettings
            {
                Visuals = Cfg.Bind(section, "Oar visuals", !drakkar,
                    $"Show oars and splashes on the {name}." + (drakkar ? " Off by default until the Drakkar has hand-placed oars." : "") + " Live.",
                    order: 100, from: OldShipKey(ship, "Oar visuals"), local: true),
                Physics = Cfg.Bind(section, "Oar physics", true,
                    $"Seated rowers push and turn the {name} and spend stamina. Off = cosmetic rowing. " +
                    "Both off = ordinary seats. Live.",
                    order: 99, from: OldShipKey(ship, "Oar physics")),
                Scale = Cfg.Bind(section, "Oar size", drakkar ? 1.5f : 1f,
                    $"Oar size on the {name}, on top of the all-ships size. Live.",
                    new AcceptableValueRange<float>(0.5f, 3f), order: 98, from: OldShipKey(ship, "Oar size")),
                Placement = Cfg.Bind(section, "Oarlock placement", drakkar ? PlacementPerSeat : PlacementFixed,
                    "Fixed: one distance and height for every bench. Per seat: the rail is measured beside each seat " +
                    "(for hulls whose benches span the beam). Applies to newly loaded ships.",
                    new AcceptableValueList<string>(PlacementFixed, PlacementPerSeat), order: 90,
                    from: OldShipKey(ship, "Oarlock placement")),
                Outboard = Cfg.Bind(section, "Fixed: distance from centerline", (float)System.Math.Round(defDist, 2),
                    $"Fixed placement: oar pivot distance (m) from the centerline. Default {how}. Live.",
                    new AcceptableValueRange<float>(0f, 10f), order: 89,
                    from: OldShipKey(ship, "Oarlock distance from centerline")),
                Height = Cfg.Bind(section, "Fixed: height above seat", (float)System.Math.Round(defHeight, 2),
                    $"Fixed placement: oar pivot height (m) above the benches. Default {how}. Live.",
                    new AcceptableValueRange<float>(-2f, 4f), order: 88,
                    from: OldShipKey(ship, "Oarlock height above seat")),
                PerSeatOutboard = Cfg.Bind(section, "Per seat: distance from rail", 0f,
                    "Per seat placement: move every oarlock outward (+) or inward (-) from the measured rail (m). Live.",
                    new AcceptableValueRange<float>(-3f, 3f), order: 80,
                    from: OldShipKey(ship, "Per seat: distance from rail")),
                PerSeatHeight = Cfg.Bind(section, "Per seat: height above rail", 0.05f,
                    "Per seat placement: height (m) above the measured rail. Live.",
                    new AcceptableValueRange<float>(-2f, 2f), order: 79,
                    from: OldShipKey(ship, "Per seat: height above rail")),
                Spacing = Cfg.Bind(section, "Per seat: oar spacing", 1.0f,
                    "Per seat placement: fore-aft gap (m) between oars of rowers sharing one bench. Live.",
                    new AcceptableValueRange<float>(0f, 3f), order: 78,
                    from: OldShipKey(ship, "Per seat: oar spacing"))
            };
            shipSettings[ship] = cfg;
            Cfg.Save();

            Log.LogInfo($"[{ship}] '{section}': visuals {(cfg.Visuals.Value ? "on" : "off")}, physics {(cfg.Physics.Value ? "on" : "off")}, " +
                        $"placement {cfg.Placement.Value}, {cfg.Outboard.Value:F2} m out, {cfg.Height.Value:F2} m up, size {cfg.Scale.Value:F2} " +
                        $"(fixed default {how}: {defDist:F2} / {defHeight:F2})");
            return cfg;
        }

        private void Update()
        {
            Synced?.Update();
            if (RenderDumpKey.Value.IsDown())
            {
                try { RenderDump.Write(Path.GetDirectoryName(Info.Location) ?? ""); }
                catch (System.Exception e) { Log.LogError("Render dump failed: " + e); }
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Synced?.Dispose();
        }

        private static string cachedBonusText;
        private static float[] cachedBonuses = new float[0];

        /// <summary>Bonus for the Nth rower (0-based), with diminishing returns.</summary>
        internal static float RowerBonus(int index)
        {
            string text = RowerBonuses.Value;
            if (text != cachedBonusText)
            {
                var parsed = new List<float>();
                foreach (string part in text.Split(','))
                {
                    if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        parsed.Add(Mathf.Max(0f, v));
                }
                cachedBonuses = parsed.ToArray();
                cachedBonusText = text;
            }

            if (cachedBonuses.Length == 0) return 0f;
            return cachedBonuses[Mathf.Min(index, cachedBonuses.Length - 1)];
        }
    }

    [HarmonyPatch(typeof(Ship), "Awake")]
    internal static class ShipAwakePatch
    {
        private static void Postfix(Ship __instance)
        {
            if (!__instance.GetComponent<RowingShip>())
                __instance.gameObject.AddComponent<RowingShip>();
        }
    }

    /// <summary>
    /// While the local player sits in a rowing seat, W/S shift rowing gear instead of standing them up.
    /// Jump still stands up as normal. __0 is the movedir argument (bound by position, not name).
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetControls")]
    internal static class PlayerSetControlsPatch
    {
        private static void Prefix(Player __instance, ref Vector3 __0)
        {
            if (__instance != Player.m_localPlayer) return;

            RowingShip ship = RowingShip.LocalRowingShip;
            if (ship == null) return;

            ship.HandleGearInput(__0.z);
            __0 = Vector3.zero;
        }
    }
}
