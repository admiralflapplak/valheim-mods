using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HeaveHo
{
    /// <summary>
    /// Reuses Valheim's helm speed HUD for rowers. Vanilla only shows it while steering; when the local
    /// player sits on a rowing bench we switch it on, light the arrow for their gear and hide the
    /// sail/rudder/wind bits rowers don't control.
    ///
    /// Every Hud field is looked up by name. Anything missing is logged and skipped, and if the core pieces
    /// aren't found the mod falls back to center-screen text, so a renamed field never breaks rowing.
    /// </summary>
    internal static class RowerHud
    {
        /// <summary>True once the ship HUD and its arrows were found; otherwise gear changes show as text.</summary>
        internal static bool Working;

        private static bool initialized;
        private static Hud hudInstance;
        private static GameObject root;
        private static GameObject controlsRoot;
        private static GameObject arrowBack;
        private static GameObject arrowSlow;
        private static GameObject arrowForward;
        private static GameObject arrowFast;
        private static readonly List<GameObject> hideWhileRowing = new List<GameObject>();

        // Oar icon swapped in place of the helm wheel while rowing.
        private static Sprite oarIcon;
        private static Image wheelIcon;
        private static Sprite wheelSprite;
        private static Color wheelColor;
        private static Quaternion wheelRotation;
        private static bool wheelPreserveAspect;
        private static bool iconSwapped;

        /// <summary>Loads oarIcon.png from the plugin folder, if present.</summary>
        internal static void LoadIcon(string pluginDir)
        {
            string path = Path.Combine(pluginDir, "oarIcon.png");
            if (!File.Exists(path)) return;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "oarIcon" };
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Plugin.Log.LogWarning("Couldn't read oarIcon.png.");
                return;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            oarIcon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            oarIcon.name = "HeaveHo_oarIcon";
            Plugin.Log.LogInfo($"Loaded oarIcon.png ({tex.width}x{tex.height}).");
        }

        // Gear -> vanilla icon. Easy = Slow (one arrow), Pull hard = Forward (two arrows), Back water = Backwards.
        private const string RootField = "m_shipHudRoot";
        private const string ControlsRootField = "m_shipControlsRoot";
        private const string BackField = "m_rudderBackward";
        private const string SlowField = "m_rudderSlow";
        private const string ForwardField = "m_rudderForward";
        private const string FastField = "m_rudderFastForward";

        private static readonly string[] HideFields =
        {
            "m_rudderLeft", "m_rudderRight", "m_rudder", "m_fullSail", "m_halfSail",
            "m_shipWindIndicatorRoot", "m_shipRudderIndicator", "m_shipWindIconRoot"
        };

        internal static void Install(Harmony harmony)
        {
            MethodInfo update = AccessTools.Method(typeof(Hud), "UpdateShipHud");
            if (update == null)
            {
                Plugin.Log.LogWarning("Hud.UpdateShipHud not found; rowing gears will show as text instead.");
                return;
            }
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(RowerHud), nameof(Postfix)));
        }

        // __0 = the player argument (bound by position so a renamed parameter can't break the patch).
        private static void Postfix(Hud __instance, Player __0)
        {
            // The game rebuilds its HUD (log out/in, hot reload), which destroys the objects cached here.
            if (!initialized || __instance != hudInstance || (Working && root == null)) Init(__instance);
            if (!Working || __0 == null || __0 != Player.m_localPlayer) return;

            RowingShip ship = RowingShip.LocalRowingShip;
            if (ship == null)
            {
                RestoreWheel(); // not on a bench: vanilla HUD as normal
                return;
            }

            SwapWheelForOar();

            int gear = ship.LocalGear;

            root.SetActive(true);
            if (controlsRoot != null) controlsRoot.SetActive(true);

            // Vanilla: Slow = 1 arrow, Forward = 2 arrows, FastForward = 3 arrows.
            arrowBack.SetActive(gear < 0);
            arrowSlow.SetActive(gear == 1);    // Easy stroke
            arrowForward.SetActive(gear == 2); // Pull hard
            arrowFast.SetActive(false);

            foreach (GameObject go in hideWhileRowing)
                if (go.activeSelf) go.SetActive(false);

            // Float the indicator over the rower's own seat (vanilla anchors it to the helm).
            if (controlsRoot != null)
            {
                Camera cam = Utils.GetMainCamera();
                if (cam != null)
                {
                    Vector3 screen = cam.WorldToScreenPoint(ship.LocalHudAnchor);
                    if (screen.z > 0f) controlsRoot.transform.position = screen;
                }
            }
        }

        private static void SwapWheelForOar()
        {
            if (oarIcon == null || wheelIcon == null) return;
            if (!iconSwapped)
            {
                wheelSprite = wheelIcon.sprite;
                wheelColor = wheelIcon.color;
                wheelRotation = wheelIcon.rectTransform.localRotation;
                wheelPreserveAspect = wheelIcon.preserveAspect;
                iconSwapped = true;
            }
            wheelIcon.sprite = oarIcon;
            wheelIcon.color = Color.white;          // show the PNG's own colors
            wheelIcon.preserveAspect = true;
            wheelIcon.rectTransform.localRotation = Quaternion.identity; // vanilla spins the wheel with the rudder
            if (!wheelIcon.gameObject.activeSelf) wheelIcon.gameObject.SetActive(true);
        }

        private static void RestoreWheel()
        {
            if (!iconSwapped || wheelIcon == null) return;
            wheelIcon.sprite = wheelSprite;
            wheelIcon.color = wheelColor;
            wheelIcon.preserveAspect = wheelPreserveAspect;
            wheelIcon.rectTransform.localRotation = wheelRotation;
            iconSwapped = false;
        }

        private static void Init(Hud hud)
        {
            bool first = !initialized;
            initialized = true;
            hudInstance = hud;
            iconSwapped = false; // the old wheel image is gone with the old HUD
            hideWhileRowing.Clear();
            if (first) LogShipHud(hud);

            root = GetObject(hud, RootField);
            controlsRoot = GetObject(hud, ControlsRootField);
            arrowBack = GetObject(hud, BackField);
            arrowSlow = GetObject(hud, SlowField);
            arrowForward = GetObject(hud, ForwardField);
            arrowFast = GetObject(hud, FastField);

            // The wheel the player sees is the RudderIcon image (sprite 'rudder').
            FieldInfo iconField = AccessTools.Field(typeof(Hud), "m_shipRudderIcon");
            wheelIcon = iconField?.GetValue(hud) as Image;
            if (wheelIcon == null && oarIcon != null)
                Plugin.Log.LogWarning("[HUD] m_shipRudderIcon not found; oar icon won't replace the wheel.");

            foreach (string name in HideFields)
            {
                GameObject go = GetObject(hud, name);
                if (go != null && go != controlsRoot && go != root &&
                    (wheelIcon == null || go != wheelIcon.gameObject))
                    hideWhileRowing.Add(go);
            }

            Working = root != null && arrowBack != null && arrowSlow != null && arrowForward != null && arrowFast != null;
            if (Working)
                Plugin.Log.LogInfo("Rower HUD hooked into the ship HUD.");
            else
                Plugin.Log.LogWarning("Rower HUD: core ship HUD fields missing; using text messages instead. " +
                                      "See the [HUD] lines above for the real names.");
        }

        private static GameObject GetObject(Hud hud, string fieldName)
        {
            FieldInfo field = AccessTools.Field(typeof(Hud), fieldName);
            if (field == null)
            {
                Plugin.Log.LogWarning($"[HUD] field '{fieldName}' not found");
                return null;
            }

            object value = field.GetValue(hud);
            if (value is GameObject go) return go;
            if (value is Component c) return c.gameObject;
            return null;
        }

        /// <summary>One-time dump so wrong guesses can be fixed from LogOutput.log.</summary>
        private static void LogShipHud(Hud hud)
        {
            var sb = new StringBuilder("[HUD] ship-related Hud fields:\n");
            foreach (FieldInfo f in AccessTools.GetDeclaredFields(typeof(Hud)))
            {
                string lower = f.Name.ToLowerInvariant();
                if (!(lower.Contains("ship") || lower.Contains("rudder") || lower.Contains("sail") || lower.Contains("wind")))
                    continue;

                object v = f.GetValue(f.IsStatic ? null : hud);
                GameObject go = v is GameObject g ? g : v is Component c ? c.gameObject : null;
                sb.AppendLine(go != null
                    ? $"  {f.Name} ({f.FieldType.Name}) -> '{HierarchyPath(go.transform)}'"
                    : $"  {f.Name} ({f.FieldType.Name})");
            }

            FieldInfo rootField = AccessTools.Field(typeof(Hud), RootField);
            GameObject shipRoot = rootField?.GetValue(hud) as GameObject;
            if (shipRoot != null)
            {
                sb.AppendLine("[HUD] ship HUD hierarchy:");
                DumpTree(shipRoot.transform, 1, sb);
            }

            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void DumpTree(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 5) return;
            Image img = t.GetComponent<Image>();
            string sprite = img != null && img.sprite != null ? $" sprite={img.sprite.name}" : "";
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{sprite}");
            foreach (Transform child in t) DumpTree(child, depth + 1, sb);
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
