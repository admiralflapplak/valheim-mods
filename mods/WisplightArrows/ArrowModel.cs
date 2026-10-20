using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WisplightArrows
{
    /// <summary>
    /// The arrow's look: in place of a head, a wisp sealed in glass. A clear glass orb (a copy of the crystal wall's see-through material)
    /// sits over the wooden arrow's tip, with the Wisplight ball's own glowing core inside it. A dropped arrow also glows faintly at the
    /// orb (a dim light, the ball's soft flare and a few of its sparks), and clears no mist: nothing here is a Demister.
    /// The game's arrow is one mesh (shaft, fletching and tip together); its tip is the mesh's -Z end (the flying arrow turns the mesh
    /// round so that end leads).
    /// </summary>
    internal static class ArrowModel
    {
        private const float GlassSize = 0.095f;   // the orb's width in metres
        private const float PullBack = 0.03f;     // how far back from the very tip the orb's middle sits, so it covers the tapered point
        private const float CoreSize = 0.04f;     // the wisp inside it
        private const float LightRange = 1.2f, LightIntensity = 0.6f;   // faint: a glint on the ground, not a lamp
        private const float FlareScale = 0.3f, SparkScale = 0.35f, SparkRate = 0.25f;   // the ball's effects, made small and sparse
        private static readonly Color GlassColour = new Color(0.78f, 0.9f, 1f, 0.3f);
        private static readonly Color IconGlassColour = new Color(0.6f, 0.8f, 1f, 0.6f);
        private static readonly Color WispColour = new Color(0.7f, 0.85f, 1f);

        private static Material _glass;

        /// <summary>Puts the orb on the tip of the arrow mesh under root; glow: the dropped item's light and sparkle too.</summary>
        public static void Dress(GameObject root, GameObject ball, ZNetScene scene, bool glow)
        {
            MeshFilter arrow = null;
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && f.transform.parent == root.transform) { arrow = f; break; }   // (the item's "model", the projectile's "polySurface20")
            Transform core = ball.transform.Find("demister_ball");
            MeshFilter coreMesh = core != null ? core.GetComponent<MeshFilter>() : null;
            MeshRenderer coreRenderer = core != null ? core.GetComponent<MeshRenderer>() : null;
            if (arrow == null || coreMesh == null || coreRenderer == null) { Plugin.Log.LogWarning("Could not find the arrow's or the wisp's model: the arrow keeps the wooden one's look"); return; }

            var orb = new GameObject("orb") { layer = arrow.gameObject.layer };
            orb.transform.SetParent(arrow.transform, false);
            Vector3 s = arrow.transform.localScale;
            orb.transform.localPosition = Tip(arrow.sharedMesh) + new Vector3(0f, 0f, PullBack / s.z);   // (+Z: back towards the fletching)
            orb.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);   // (sizes below are in metres, whatever the mesh's scale)

            Mesh sphere = coreMesh.sharedMesh;
            float unit = Mathf.Max(sphere.bounds.size.x, 0.0001f);
            Material glass = GlassMat(scene);
            if (glass != null) Part(orb, "glass", sphere, glass, GlassSize / unit);
            Part(orb, "core", sphere, coreRenderer.sharedMaterial, CoreSize / unit);

            if (!glow) return;
            var light = new GameObject("light") { layer = orb.layer };
            light.transform.SetParent(orb.transform, false);
            Light l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = WispColour;
            l.range = LightRange;
            l.intensity = LightIntensity;
            l.shadows = LightShadows.None;

            Effect(ball, "effects/flame/flare", orb.transform, FlareScale, 1f);
            Effect(ball, "effects/flame/embers", orb.transform, SparkScale, SparkRate);
        }

        public static void Forget() => _glass = null;

        private const float IconOrbScale = 3.5f;   // at icon size the real orb is a few pixels: the game's own arrow icons draw their tips large too

        /// <summary>For drawing the icon and back after: the orb drawn larger, so it reads at icon size. (The icon is drawn at once, so
        /// nothing else sees the change.)</summary>
        public static void IconLook(GameObject root, bool on)
        {
            if (_glass != null) _glass.color = on ? IconGlassColour : GlassColour;   // (clear glass all but vanishes at icon size)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "orb")
                {
                    Vector3 s = t.parent.localScale;
                    float k = on ? IconOrbScale : 1f;
                    t.localScale = new Vector3(k / s.x, k / s.y, k / s.z);
                }
        }

        /// <summary>The very tip, in the mesh's own space: the middle of the vertices at its -Z end (the bounds' middle would be the
        /// fletching's, which stands above the shaft).</summary>
        private static Vector3 Tip(Mesh mesh)
        {
            Bounds b = mesh.bounds;
            if (!mesh.isReadable)
            {
                Plugin.Log.LogInfo("The arrow's mesh can't be read: the orb goes at the middle of its tip end");
                return new Vector3(b.center.x, b.center.y, b.min.z);
            }
            Vector3[] verts = mesh.vertices;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Vector3 v in verts)
                if (v.z < b.min.z + 0.02f) { sum += v; n++; }
            return n > 0 ? new Vector3(sum.x / n, sum.y / n, b.min.z) : new Vector3(b.center.x, b.center.y, b.min.z);
        }

        private static void Part(GameObject orb, string name, Mesh mesh, Material material, float scale)
        {
            var go = new GameObject(name) { layer = orb.layer };
            go.transform.SetParent(orb.transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>A copy of one of the ball's particle effects at the orb, its particles smaller and fewer.</summary>
        private static void Effect(GameObject ball, string path, Transform at, float size, float rate)
        {
            Transform source = ball.transform.Find(path);
            if (source == null) { Plugin.Log.LogInfo("The wisp ball has no " + path + ": the dropped arrow goes without it"); return; }
            GameObject copy = Object.Instantiate(source.gameObject, at, false);
            copy.name = source.name;
            copy.transform.localPosition = Vector3.zero;
            foreach (ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.startSizeMultiplier *= size;
                main.startSpeedMultiplier *= size;
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTimeMultiplier *= rate;
                ParticleSystem.ShapeModule shape = ps.shape;
                if (shape.enabled) shape.radius *= size;
            }
        }

        /// <summary>Clear glass, faintly blue: a copy of the game's crystal window (already set up to be see-through), its pattern taken off.</summary>
        private static Material GlassMat(ZNetScene scene)
        {
            if (_glass != null) return _glass;
            Material basis = null;
            GameObject wall = scene.GetPrefab("crystal_wall_1x1");
            if (wall != null)
                foreach (MeshRenderer r in wall.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("crystal_window", StringComparison.Ordinal)) { basis = r.sharedMaterial; break; }
            if (basis == null) { Plugin.Log.LogWarning("The game has no crystal window glass: the orb goes without its glass"); return null; }

            var m = new Material(basis) { name = "nick_wisparrow_glass", color = GlassColour };
            foreach (string map in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap" })
                if (m.HasProperty(map)) m.SetTexture(map, null);
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICGLOSSMAP");
            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.95f);
            _glass = m;
            return m;
        }
    }
}
