using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// The "heckler" console command (F5): the F1 Test buttons from the console, so scenes can be started without the menu (and by
    /// Claude Tools' "console" for the page's pictures), plus holding him still for a picture. The same rules as the buttons: the
    /// Gifter's test, which hands out items, needs the game's cheats on someone else's server.
    /// </summary>
    internal static class Commands
    {
        private const string Command = "heckler";
        private const string Usage = "heckler summon [emote] | heckler torch | heckler lurker|redlight|tombstone|copycat|campfire|thief|gifter | " +
                                     "heckler freeze | heckler unfreeze | heckler where | heckler portrait|photo [distance] [turn] [height] [size] [above] | heckler status";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        private static readonly Dictionary<string, SceneKind> SceneNames = new Dictionary<string, SceneKind>
        {
            { "lurker", SceneKind.Lurker }, { "redlight", SceneKind.RedLight }, { "tombstone", SceneKind.Tombstone },
            { "copycat", SceneKind.Copycat }, { "campfire", SceneKind.Campfire }, { "thief", SceneKind.CoinThief }, { "gifter", SceneKind.Gifter }
        };

        internal static void Register()
        {
            new Terminal.ConsoleCommand(Command, "Fuling Heckler: " + Usage, args => Run(args.Context, args.Args));
        }

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private const int PortraitLayer = 31;   // a layer the game doesn't draw anything on

        /// <summary>
        /// A picture of him from in front (distance in metres, turned round him by turn degrees, aimed height metres from his head, the
        /// camera raised above metres): alone on a transparent background (portrait), or in the world as the game's camera draws it, 16:9
        /// at size wide (photo). Saved in BepInEx/config/FulingHeckler_portraits.
        /// </summary>
        private static string Portrait(FulingActor actor, float distance, float turn, float height, int size, float above, bool scene = false)
        {
            var renderers = actor.GetComponentsInChildren<Renderer>(true);
            var layers = new Dictionary<GameObject, int>();
            GameObject camObject = null;
            RenderTexture rt = null;
            try
            {
                var anim = actor.GetComponentInChildren<Animator>();
                Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                Vector3 target = (head != null ? head.position : actor.transform.position + Vector3.up * 1.4f) + Vector3.up * height;
                Vector3 from = target + Quaternion.Euler(0f, turn, 0f) * actor.transform.forward * distance + Vector3.up * above;

                if (!scene) foreach (var r in renderers) { layers[r.gameObject] = r.gameObject.layer; r.gameObject.layer = PortraitLayer; }
                camObject = new GameObject("FulingHeckler_PortraitCamera");
                var cam = camObject.AddComponent<Camera>();
                if (scene && Camera.main != null) cam.CopyFrom(Camera.main);   // the world as the game draws it (sky, fog, its layers)
                cam.enabled = false;
                if (!scene)
                {
                    cam.cullingMask = 1 << PortraitLayer;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                }
                cam.fieldOfView = scene ? 40f : 30f;
                cam.nearClipPlane = 0.05f;
                cam.allowHDR = false;
                cam.transform.position = from;
                cam.transform.LookAt(target);
                int w = size, h = scene ? size * 9 / 16 : size;
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                cam.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, scene ? TextureFormat.RGB24 : TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                string folder = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FulingHeckler_portraits");
                System.IO.Directory.CreateDirectory(folder);
                string file = System.IO.Path.Combine(folder, System.DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.Destroy(tex);
                return file;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Portrait failed ({e.GetType().Name}: {e.Message}).");
                return null;
            }
            finally
            {
                foreach (var kv in layers) if (kv.Key != null) kv.Key.layer = kv.Value;
                if (camObject != null) Object.Destroy(camObject);
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            }
        }

        private static void Run(Terminal t, string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "status";
            var actor = FulingActor.Current;
            switch (sub)
            {
                case "summon":
                    Plugin.Instance.TestSummon(false, null, a.Length > 2 ? string.Join(" ", a, 2, a.Length - 2) : null);
                    return;
                case "torch":
                    Plugin.Instance.TestSummon(false, true, null);
                    return;
                case "freeze":
                case "unfreeze":
                    if (actor == null) { t.AddString("No Fuling is out."); return; }
                    actor.Freeze(sub == "freeze");
                    t.AddString(sub == "freeze" ? "He holds still (heckler unfreeze lets him go on)." : "He carries on.");
                    return;
                case "where":
                    if (actor == null) { t.AddString("No Fuling is out."); return; }
                    Vector3 p = actor.transform.position;
                    t.AddString($"Fuling at {p.x:0.00} {p.y:0.00} {p.z:0.00}, facing {actor.transform.eulerAngles.y:0}");
                    return;
                case "portrait":
                case "photo":
                    if (actor == null) { t.AddString("No Fuling is out."); return; }
                    float F(int i, float d) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
                    string file = Portrait(actor, F(2, 1.3f), F(3, 0f), F(4, -0.1f), (int)F(5, 1024f), F(6, 0f), sub == "photo");
                    t.AddString(file != null ? "Portrait saved: " + file : "Couldn't take the portrait (see the log).");
                    return;
                case "status":
                    t.AddString($"Fuling Heckler {Plugin.Version}: {(Plugin.Enabled.Value ? "on" : "off")}, {Plugin.Quips.Count} quips ({Plugin.Language}), " +
                                $"{Plugin.Loot.Entries.Count} loot entries, {Scenes.Fires.Count} fire(s) loaded, a Fuling {(actor != null ? "is out" : "isn't out")}.");
                    return;
            }
            if (SceneNames.TryGetValue(sub, out SceneKind kind)) { Plugin.Instance.TestScene(kind); return; }
            t.AddString(Usage);
        }
    }
}
