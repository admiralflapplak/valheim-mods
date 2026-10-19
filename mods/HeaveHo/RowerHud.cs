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
        private static readonly Color GameWheelColor = new Color(1f, 0.6413793f, 0f, 1f);

        // The helm's power icon under the minimap (a sail, or the rudder at slow and reverse): a rower sees their own oars there.
        private static Image powerIcon;
        private static Sprite powerSprite;
        private static Color powerColor;
        private static Vector3 powerScale;
        private static bool powerPreserveAspect;
        private static bool powerSwapped;
        private static Sprite wheelSprite;
        private static Color wheelColor;
        private static Quaternion wheelRotation;
        private static bool wheelPreserveAspect;
        private static bool iconSwapped;

        // The picture under the minimap while rowing (rowingPowerIcon.png, drawn like the game's rudder and sail icons).
        private static Sprite rowingIcon;

        /// <summary>Loads oarIcon.png (the helm wheel's replacement) and rowingPowerIcon.png (the one under the minimap) from the plugin folder.</summary>
        internal static void LoadIcon(string pluginDir)
        {
            oarIcon = LoadSprite(Path.Combine(pluginDir, "oarIcon.png"), "HeaveHo_oarIcon");
            rowingIcon = LoadSprite(Path.Combine(pluginDir, "rowingPowerIcon.png"), "HeaveHo_rowingPowerIcon");
        }

        private static Sprite LoadSprite(string path, string name)
        {
            if (!File.Exists(path)) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Plugin.Log.LogWarning($"Couldn't read {Path.GetFileName(path)}.");
                Object.Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            Plugin.Log.LogInfo($"Loaded {Path.GetFileName(path)} ({tex.width}x{tex.height}).");
            return sprite;
        }

        /// <summary>Plugin unload (hot reload): give the helm wheel back and free the oar icon.</summary>
        internal static void Unload()
        {
            RestoreWheel();
            RestorePowerIcon();
            SetWind(true);
            foreach (Sprite s in new[] { oarIcon, rowingIcon })
            {
                if (s == null) continue;
                Object.Destroy(s.texture);
                Object.Destroy(s);
            }
            oarIcon = null;
            rowingIcon = null;
            Working = false;
            initialized = false;
        }

        // Gear -> vanilla icon. Easy = Slow (one arrow), Pull hard = Forward (two arrows), Back water = Backwards.
        private const string RootField = "m_shipHudRoot";
        private const string ControlsRootField = "m_shipControlsRoot";
        private const string BackField = "m_rudderBackward";
        private const string SlowField = "m_rudderSlow";
        private const string ForwardField = "m_rudderForward";
        private const string FastField = "m_rudderFastForward";

        // Hidden while rowing. Only parts the game switches on or off itself every frame, so they come back by themselves at the helm.
        private static readonly string[] HideFields =
        {
            "m_rudderLeft", "m_rudderRight", "m_fullSail", "m_halfSail", "m_shipRudderIndicator"
        };

        // The wind indicator under the minimap: a rower at an oar has no sail to trim, so it is hidden while they row. The game never
        // switches it on again after the ship HUD shows, so this mod does, the moment the player is no longer at an oar.
        private static readonly string[] WindFields = { "m_shipWindIndicatorRoot", "m_shipWindIconRoot" };
        private static readonly List<GameObject> windParts = new List<GameObject>();
        private static bool windHidden;

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
                RestorePowerIcon();
                SetWind(true);
                return;
            }

            SwapWheelForOar();
            ShowPowerIcon(ship);
            SetWind(false);

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

        /// <summary>Shows or hides the wind indicator, only when that changes (the game never touches it itself).</summary>
        private static void SetWind(bool visible)
        {
            if (windHidden == !visible) return;
            foreach (GameObject go in windParts)
                if (go != null) go.SetActive(visible);
            windHidden = !visible;
        }

        private static void SwapWheelForOar()
        {
            if (oarIcon == null || wheelIcon == null) return;
            iconSwapped = true;
            wheelIcon.sprite = oarIcon;
            wheelIcon.color = Color.white;          // show the PNG's own colors
            wheelIcon.preserveAspect = true;
            wheelIcon.rectTransform.localRotation = Quaternion.identity; // vanilla spins the wheel with the rudder
            if (!wheelIcon.gameObject.activeSelf) wheelIcon.gameObject.SetActive(true);
        }

        /// <summary>
        /// The oar icon under the minimap, where the helm shows its power: dim with the oars up, gold at an easy stroke, larger at
        /// pull hard, flipped for back water, and swelling a little with every stroke.
        /// </summary>
        private static void ShowPowerIcon(RowingShip ship)
        {
            Sprite picture = rowingIcon != null ? rowingIcon : oarIcon;
            if (picture == null || powerIcon == null) return;
            powerSwapped = true;

            int gear = ship.LocalGear;
            powerIcon.sprite = picture;
            powerIcon.preserveAspect = true;
            powerIcon.color = gear == 0 ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            float size = (gear == 2 ? 1.3f : 1f) * (1f + (gear != 0 ? 0.1f * ship.LocalStrokeLift : 0f));
            powerIcon.rectTransform.localScale = new Vector3(powerScale.x * size * (gear < 0 ? -1f : 1f), powerScale.y * size, powerScale.z);
            if (!powerIcon.gameObject.activeSelf) powerIcon.gameObject.SetActive(true);
        }

        private static void RestorePowerIcon()
        {
            if (!powerSwapped || powerIcon == null) return;
            powerIcon.sprite = powerSprite;
            powerIcon.color = powerColor;
            powerIcon.preserveAspect = powerPreserveAspect;
            powerIcon.rectTransform.localScale = powerScale;
            powerSwapped = false;
        }

        /// <summary>
        /// Notes what the game's own icon looks like (its sprite, colour and shape), to give it back when the player stops rowing.
        /// Normally the HUD is new and still shows the game's sprite. If an earlier copy of this mod (a hot reload) left its picture on
        /// the HUD, the game's sprite is looked up by name and put back.
        /// </summary>
        private static void RememberGameIcon(Image img, string spriteName, out Sprite sprite, out Color color, out Vector3 scale, out bool preserve)
        {
            sprite = null;
            color = Color.white;
            scale = Vector3.one;
            preserve = false;
            if (img == null) return;

            bool ours = img.sprite != null && img.sprite.name.StartsWith("HeaveHo_");
            if (!ours && img.sprite != null)
            {
                sprite = img.sprite;
                color = img.color;
                scale = img.rectTransform.localScale;
                preserve = img.preserveAspect;
                return;
            }

            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && s.name == spriteName) { sprite = s; break; }
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.rectTransform.localScale = Vector3.one;
                img.preserveAspect = false;
                Plugin.Log.LogInfo($"[HUD] put the game's '{spriteName}' icon back (an earlier copy of this mod had replaced it).");
            }
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
            powerIcon = GetObject(hud, "m_rudder")?.GetComponent<Image>();
            iconSwapped = false;
            powerSwapped = false; // the old images are gone with the old HUD
            RememberGameIcon(wheelIcon, "rudder", out wheelSprite, out wheelColor, out Vector3 wheelScaleUnused, out wheelPreserveAspect);
            wheelRotation = wheelIcon != null ? wheelIcon.rectTransform.localRotation : Quaternion.identity;
            // The wheel is a white glyph the game tints orange (read from the game's HUD: 1, 0.6414, 0). A copy of this mod that replaced
            // the sprite once reset the tint, so it is set here rather than trusted from the image.
            wheelColor = GameWheelColor;
            if (wheelIcon != null) wheelIcon.color = wheelColor;
            RememberGameIcon(powerIcon, "ship_rudder_icon", out powerSprite, out powerColor, out powerScale, out powerPreserveAspect);
            if (wheelIcon == null && oarIcon != null)
                Plugin.Log.LogWarning("[HUD] m_shipRudderIcon not found; oar icon won't replace the wheel.");

            windParts.Clear();
            windHidden = false;
            foreach (string name in WindFields)
            {
                GameObject go = GetObject(hud, name);
                if (go == null) continue;
                windParts.Add(go);
                if (!go.activeSelf) go.SetActive(true); // a HUD rebuilt while rowing (or a hot reload) may have left it hidden
            }

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

            Plugin.Log.LogDebug(sb.ToString());
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
