using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// For "keel plate" (pictures for the mod's page): a still from a camera of its own, placed from the player (x right, y up, z ahead,
    /// turned with them, so it rides the ship), straight to a PNG in BepInEx/claude/shots. The game's own camera and view are untouched.
    /// With "cut", only the ship itself and what is posed on it are drawn (moved for the moment onto a layer nothing else uses, the
    /// camera seeing that layer alone: no water, seabed, sky, land, fog or weather), twice: on black and on white. The
    /// difference between the two is how solid each pixel is, which the page's picture script turns into a cut-out to lay over another
    /// plate (deferred rendering doesn't give a usable alpha of its own).
    /// </summary>
    internal static class Plate
    {
        /// <param name="omens">With cut: the omens showing instead of the ship (the Pacer alone, to lay under a sea plate).</param>
        internal static string Render(Player player, Ship ship, string name, Vector3 local, float yaw, float pitch, float fov, int size, bool cut,
            bool omens = false)
        {
            Camera main = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            if (main == null) return "No game camera to copy.";

            var go = new GameObject("BelowTheKeel_PlateCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(player.transform.TransformPoint(local),
                    player.transform.rotation * Quaternion.Euler(pitch, yaw, 0f));
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.03f;

                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots");
                Directory.CreateDirectory(dir);
                if (!cut)
                {
                    string path = Path.Combine(dir, $"plate_{name}.png");
                    File.WriteAllBytes(path, Shoot(cam, size));
                    return "Plate: " + path;
                }

                int spare = -1;
                for (int i = 31; i > 8 && spare < 0; i--)
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) spare = i;
                if (spare < 0) return "No spare layer for a cut.";

                // The ship's own meshes and the posed copy on it (parented to the ship), not its water mask, wake or effects.
                var moved = new List<(GameObject go, int layer)>();
                foreach (Renderer r in omens ? (IEnumerable<Renderer>)Visuals.Renderers() : ship.GetComponentsInChildren<Renderer>())
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "";
                    if (shader.Contains("WaterMask") || shader.Contains("ShadowBlob") || shader.Contains("Particle")) continue;
                    moved.Add((r.gameObject, r.gameObject.layer));
                    r.gameObject.layer = spare;
                }
                cam.cullingMask = 1 << spare;
                cam.clearFlags = CameraClearFlags.SolidColor;

                bool fog = RenderSettings.fog;
                RenderSettings.fog = false;
                try
                {
                    cam.backgroundColor = Color.black;
                    string black = Path.Combine(dir, $"plate_{name}_black.png");
                    File.WriteAllBytes(black, Shoot(cam, size));
                    cam.backgroundColor = Color.white;
                    string white = Path.Combine(dir, $"plate_{name}_white.png");
                    File.WriteAllBytes(white, Shoot(cam, size));
                    return $"Plate (cut, {moved.Count} parts of {(omens ? "the omens" : "the ship and what's on it")}): {black} and {white}";
                }
                finally
                {
                    RenderSettings.fog = fog;
                    foreach (var (g, layer) in moved)
                        if (g != null) g.layer = layer;
                }
            }
            finally { Object.Destroy(go); }
        }

        private static byte[] Shoot(Camera cam, int size)
        {
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                Object.Destroy(tex);
                return png;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                rt.Release();
                Object.Destroy(rt);
            }
        }
    }
}
