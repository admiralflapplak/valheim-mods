using System.Collections.Generic;
using System.IO;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Nick.Shared;   // (Shared/Translations.cs)

namespace HeaveHo
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "nick.valheim.heaveho";
        public const string ModName = "Heave Ho";
        public const string ModVersion = "1.0.0";

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
        internal static ConfigEntry<bool> ShipUnderSail;
        internal static ConfigEntry<float> OarModelScale;
        internal static ConfigEntry<float> StrokesPerSecond;

        // 4. Splashes
        internal static ConfigEntry<bool> Splashes;
        internal static ConfigEntry<string> EntryEffects;
        internal static ConfigEntry<string> ExitEffects;
        
        // 5. Rower HUD
        internal static ConfigEntry<float> HudOutboard;
        internal static ConfigEntry<float> HudHeight;

        // 9. Advanced
        internal static ConfigEntry<string> RowingSeatAnimations;

        /// <summary>Length (m) of the plain oar drawn when no oar model is installed at all.</summary>
        internal const float PlaceholderOarLength = 3.2f;

        // Karve oarlock (hand-tuned) and the fallback for hulls that can't be measured.
        internal const float DefaultOutboard = 1.1f;
        internal const float DefaultHeight = 0.5f;

        /// <summary>Custom oar from BepInEx/plugins/HeaveHo/heaveho, or null to use placeholder primitives.</summary>
        internal static GameObject OarPrefab;

        /// <summary>Custom oar from BepInEx/plugins/HeaveHo/oar.obj (+ texture). Takes priority over the bundle.</summary>
        internal static Mesh OarMesh;
        internal static Texture2D OarTexture;

        /// <summary>An oar's shape, and its own texture (null: drawn with the ship's hull texture, the model's UVs point into it).</summary>
        internal class OarModel
        {
            public Mesh Mesh;
            public Texture2D Texture;
            public bool HullTexture;
        }

        private static readonly Dictionary<string, OarModel> shipOars = new Dictionary<string, OarModel>();
        private static string assetDir = "";

        /// <summary>
        /// The oar for a ship: its own oar_&lt;name&gt;.obj next to the dll if there is one (oar_drakkar.obj, named as in the F1 menu),
        /// else the common oar.obj, else null (an asset-bundle oar or the placeholder is used).
        /// </summary>
        internal static OarModel OarFor(string ship)
        {
            if (shipOars.TryGetValue(ship, out OarModel found)) return found;
            OarModel model = LoadShipModel("oar_", ship) ?? (OarMesh != null ? new OarModel { Mesh = OarMesh, Texture = OarTexture } : null);
            shipOars[ship] = model;
            return model;
        }

        private static readonly Dictionary<string, OarModel> shipFittings = new Dictionary<string, OarModel>();

        /// <summary>A ship's oarlock fitting, oarlock_&lt;name&gt;.obj (the Karve's wooden oarlock), or null.</summary>
        internal static OarModel FittingFor(string ship)
        {
            if (shipFittings.TryGetValue(ship, out OarModel found)) return found;
            OarModel model = LoadShipModel("oarlock_", ship);
            shipFittings[ship] = model;
            return model;
        }

        private static OarModel LoadShipModel(string prefix, string ship)
        {
            string path = Path.Combine(assetDir, prefix + DisplayName(ship).ToLowerInvariant().Replace(' ', '_') + ".obj");
            if (!File.Exists(path)) return null;
            try
            {
                Mesh mesh = ObjLoader.Load(path);
                mesh.name = "HeaveHo_" + prefix + ship;
                Texture2D tex = ObjLoader.LoadTexture(path);
                Log.LogInfo($"[{ship}] Loaded {Path.GetFileName(path)} ({mesh.vertexCount} verts, x {mesh.bounds.min.x:F2} to " +
                            $"{mesh.bounds.max.x:F2}), " + (tex != null ? "texture " + tex.name : "drawn with the hull's texture"));
                return new OarModel { Mesh = mesh, Texture = tex, HullTexture = tex == null };
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"[{ship}] Couldn't load {Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        private Harmony harmony;
        internal static HeaveHo.Shared.ServerSettings Synced;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            Translations.Load(this);
            Synced = new HeaveHo.Shared.ServerSettings(ModGuid, Config, Logger);
            Cfg.Init(Config, Synced);
            BindSettings();

            assetDir = AssetDir();
            if (!LoadOarObj()) LoadOarBundle();
            RowerHud.LoadIcon(assetDir);

            harmony = new Harmony(ModGuid);
            harmony.PatchAll();
            RowerHud.Install(harmony); // patched separately so a missing HUD method can't break rowing

            // After a hot reload the ships already afloat have no rowing component from this copy.
            foreach (Ship ship in FindObjectsByType<Ship>(FindObjectsSortMode.None))
                if (!ship.GetComponent<RowingShip>()) ship.gameObject.AddComponent<RowingShip>();
        }

        private static void BindSettings()
        {
            // What a player thinks about, in order: how rowing plays, what it costs, how the oars look, the rower's HUD, each ship,
            // and the fine print.
            const string rowing = "1. Rowing", stamina = "2. Stamina", oars = "3. Oars and splashes", hud = "4. Rower HUD",
                         adv = "9. Advanced";

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
                from: "3. Oars (all ships)/Show oars;3. Oars/Show oars", local: true);
            OarModelScale = Cfg.Bind(oars, "Oar size", 1f,
                "Size multiplier for the oar model on every ship (each ship also has its own size).",
                new AcceptableValueRange<float>(0.1f, 3f), from: "3. Oars (all ships)/Oar size;3. Oars/Oar model scale", local: true);
            StrokesPerSecond = Cfg.Bind(oars, "Stroke rate", 0.55f,
                "Strokes per second at Easy stroke. Pull hard strokes faster.",
                new AcceptableValueRange<float>(0.1f, 2f), from: "3. Oars (all ships)/Stroke rate;3. Oars/Strokes per second", local: true);
            ShipUnderSail = Cfg.Bind(oars, "Ship the oars under sail", false,
                "While the sail is up and the ship is moving, oars nobody is pulling slide in out of sight; they come back out the " +
                "moment someone rows or the ship slows. Off: they stay where each ship's \"Oars at rest\" puts them. Live.",
                local: true);
            Splashes = Cfg.Bind(oars, "Oar splashes", true,
                "Splash where each blade enters the water, and a smaller one as it lifts out.",
                from: "4. Splashes/Oar splashes;5. Splashes/Oar splashes", local: true);
            EntryEffects = Cfg.Bind(oars, "Splash effects: blade enters", "vfx_water_surface_fish",
                "Comma-separated Valheim effects (visuals or sfx_ sounds) spawned where the blade bites. " +
                "Water-related names are listed in LogOutput.log under [Splash]. Live.",
                from: "4. Splashes/Effects on blade entry;5. Splashes/Extra effects on blade entry", local: true);
            ExitEffects = Cfg.Bind(oars, "Splash effects: blade lifts out", "vfx_watersplash_bathtub",
                "Same, for when the blade lifts out. Live.",
                from: "4. Splashes/Effects on blade exit;5. Splashes/Extra effects on blade exit", local: true);

            // (these two start from their new defaults: an earlier value isn't carried over)
            HudOutboard = Cfg.Bind(hud, "Arrows: distance outboard", 0.2f,
                "How far past your oarlock (m) the rowing arrows float.",
                new AcceptableValueRange<float>(-3f, 3f), local: true);
            HudHeight = Cfg.Bind(hud, "Arrows: height", 0.2f,
                "Height (m) of the rowing arrows above your oarlock.",
                new AcceptableValueRange<float>(-2f, 4f), local: true);

            RowingSeatAnimations = Cfg.Bind(adv, "Rowing seat animations", "attach_sitship",
                "Seat animations that count as rowing seats (helm and mast holds use others). Seats are listed in " +
                "LogOutput.log per ship. Applies to newly loaded ships; all players should match.",
                advanced: true, from: "0. Seats/Rowing seat animations");

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
            public ConfigEntry<bool> ShowAtRest;     // leave the oars out (stowed, or level in their ports) when nobody rows them
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

        private static string ShipSection(string ship) => "5. Ship: " + DisplayName(ship);
        /// <summary>Where a ship's setting lived in the pre-release builds: "6. Ship: ...", and before that "3. Oars - ...".</summary>
        private static string ShipFrom(string ship, string key, string oldestKey = null) =>
            "6. Ship: " + DisplayName(ship) + "/" + key + (oldestKey != null ? ";3. Oars - " + ship + "/" + oldestKey : "");

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
                    advanced: true, order: 10, from: ShipFrom(ship, "Rowing seat names", "Rowing seat names"));
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

            // A ship whose oars go through oar ports has no placement to choose: the ports are known (see ShipProfile).
            ShipProfile profile = ShipProfile.For(ship);
            bool ported = profile != null;
            float defDist = DefaultOutboard, defHeight = DefaultHeight;
            string how = "hand-tuned defaults";
            if (!ported && ship != "Karve" && measure(out float d, out float h))
            {
                defDist = d;
                defHeight = h;
                how = "measured from the hull";
            }

            string section = ShipSection(ship), name = DisplayName(ship);
            cfg = new ShipOarSettings
            {
                Visuals = Cfg.Bind(section, "Oar visuals", true,
                    $"Show oars and splashes on the {name}. Live.",
                    order: 100, from: ShipFrom(ship, "Oar visuals", "Oar visuals"), local: true),
                Physics = Cfg.Bind(section, "Oar physics", true,
                    $"Seated rowers push and turn the {name} and spend stamina. Off = cosmetic rowing. " +
                    "Both off = ordinary seats. Live.",
                    order: 99, from: ShipFrom(ship, "Oar physics", "Oar physics")),
                Scale = Cfg.Bind(section, "Oar size", 1f,
                    $"Oar size on the {name}, on top of the all-ships size. Live.",
                    new AcceptableValueRange<float>(0.5f, 3f), order: 98, from: ShipFrom(ship, "Oar size", "Oar size"), local: true),
                ShowAtRest = Cfg.Bind(section, "Oars at rest", true,
                    profile != null && profile.StowAlongHull
                        ? $"Leave the {name}'s oars out, stowed along the hull, when nobody is rowing them. Off: an oar slides out when its " +
                          "rower starts and back in when they stop. Live."
                        : $"Leave the {name}'s oars out, level through their ports, when nobody is rowing them. Off: an oar slides out of " +
                          "its port when its rower starts and back in when they stop, so the ship keeps its own shape. Live.",
                    order: 97, local: true)
            };

            if (ported)
            {
                // Moves every oar from where its port puts it (in metres); the defaults are the ports themselves.
                cfg.PerSeatOutboard = Cfg.Bind(section, "Oar position: out", 0f,
                    "Move every oar outward (+) or inward (-) from where the hull puts it (m). Live.",
                    new AcceptableValueRange<float>(-1f, 1f), order: 80, from: ShipFrom(ship, "Oar position: out"));
                cfg.PerSeatHeight = Cfg.Bind(section, "Oar position: up", 0f,
                    "Move every oar up (+) or down (-) from where the hull puts it (m). Live.",
                    new AcceptableValueRange<float>(-1f, 1f), order: 79, from: ShipFrom(ship, "Oar position: up"));
            }
            else
            {
                cfg.Placement = Cfg.Bind(section, "Oarlock placement", PlacementFixed,
                    "Fixed: one distance and height for every bench. Per seat: the rail is measured beside each seat " +
                    "(for hulls whose benches span the beam). Applies to newly loaded ships.",
                    new AcceptableValueList<string>(PlacementFixed, PlacementPerSeat), order: 90,
                    from: ShipFrom(ship, "Oarlock placement", "Oarlock placement"));
                cfg.Outboard = Cfg.Bind(section, "Fixed: distance from centerline", (float)System.Math.Round(defDist, 2),
                    $"Fixed placement: oar pivot distance (m) from the centerline. Default {how}. Live.",
                    new AcceptableValueRange<float>(0f, 10f), order: 89,
                    from: ShipFrom(ship, "Fixed: distance from centerline", "Oarlock distance from centerline"));
                cfg.Height = Cfg.Bind(section, "Fixed: height above seat", (float)System.Math.Round(defHeight, 2),
                    $"Fixed placement: oar pivot height (m) above the benches. Default {how}. Live.",
                    new AcceptableValueRange<float>(-2f, 4f), order: 88,
                    from: ShipFrom(ship, "Fixed: height above seat", "Oarlock height above seat"));
                cfg.PerSeatOutboard = Cfg.Bind(section, "Per seat: distance from rail", 0f,
                    "Per seat placement: move every oarlock outward (+) or inward (-) from the measured rail (m). Live.",
                    new AcceptableValueRange<float>(-3f, 3f), order: 80,
                    from: ShipFrom(ship, "Per seat: distance from rail", "Per seat: distance from rail"));
                cfg.PerSeatHeight = Cfg.Bind(section, "Per seat: height above rail", 0.05f,
                    "Per seat placement: height (m) above the measured rail. Live.",
                    new AcceptableValueRange<float>(-2f, 2f), order: 79,
                    from: ShipFrom(ship, "Per seat: height above rail", "Per seat: height above rail"));
                cfg.Spacing = Cfg.Bind(section, "Per seat: oar spacing", 1.0f,
                    "Per seat placement: fore-aft gap (m) between oars of rowers sharing one bench. Live.",
                    new AcceptableValueRange<float>(0f, 3f), order: 78,
                    from: ShipFrom(ship, "Per seat: oar spacing", "Per seat: oar spacing"));
            }
            shipSettings[ship] = cfg;
            Cfg.PurgeSection("6. Ship: " + name);      // what is left of this ship's settings under their old names
            Cfg.PurgeSection("3. Oars - " + ship);
            Cfg.Save();

            Log.LogInfo($"[{ship}] '{section}': visuals {(cfg.Visuals.Value ? "on" : "off")}, physics {(cfg.Physics.Value ? "on" : "off")}, " +
                        (ported ? (profile.Fitting ? "oars in oarlocks on the gunwale" : "oars through the hull's oar ports") : $"placement {cfg.Placement.Value}, {cfg.Outboard.Value:F2} m out, {cfg.Height.Value:F2} m up") +
                        $", size {cfg.Scale.Value:F2}" + (ported ? "" : $" (fixed default {how}: {defDist:F2} / {defHeight:F2})"));
            return cfg;
        }

        private void Update()
        {
            Synced?.Update();
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Synced?.Dispose();
            Translations.Unload();
            RowerHud.Unload();
            RowingShip.DestroyAll(); // their oars, RPC and held seat go with them; a reload adds fresh ones
            foreach (OarModel m in shipOars.Values)
                if (m != null && m.Mesh != OarMesh) { Destroy(m.Mesh); if (m.Texture != null) Destroy(m.Texture); }
            shipOars.Clear();
            foreach (OarModel m in shipFittings.Values)
                if (m != null) { Destroy(m.Mesh); if (m.Texture != null) Destroy(m.Texture); }
            shipFittings.Clear();
            if (OarMesh != null) Destroy(OarMesh);
            if (OarTexture != null) Destroy(OarTexture);
            OarMesh = null;
            OarTexture = null;
            OarPrefab = null;
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
