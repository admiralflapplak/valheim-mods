using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// A test for how far down the murk lets you see, which decides how deep the Eye, the Pacer and Rán's Net can be. Two rows of discs
    /// go out from the ship's side, each one deeper than the last (the depths below): dark ones to starboard, faintly glowing ones to
    /// port. They follow the ship and go after two minutes, or with "keel probe off".
    /// </summary>
    internal static class Probe
    {
        private static readonly float[] Depths = { 2f, 4f, 8f, 12f, 20f, 30f, 45f };
        private const float Spacing = 7f;
        private const float DiscSize = 5f;
        private const float Seconds = 120f;

        private static GameObject _root;

        private static Shader LitShader => FirstShader("Custom/Creature", "Legacy Shaders/Diffuse");
        private static Shader GlowShader => FirstShader("Unlit/Color");

        private static Shader FirstShader(params string[] names)
        {
            // Shader.Find misses the game's shaders (they come in asset bundles): look through every loaded one.
            Shader[] loaded = Resources.FindObjectsOfTypeAll<Shader>();
            foreach (string n in names)
                foreach (Shader sh in loaded)
                    if (sh.name == n && sh.isSupported) return sh;
            return Look.DarkSerpent().shader;   // (the serpent's own: always there when a world is)
        }

        internal static string Show(Ship ship)
        {
            Clear();
            _root = new GameObject("BelowTheKeel_Probe");
            _root.transform.SetParent(ship.transform, false);   // (no collider anywhere under it: the ship's physics never sees it)
            Visuals.Track(_root, Seconds);

            // A reference just above the water ahead of the bow: if even this doesn't show, it's the material, not the murk.
            Disc(new Vector3(0f, Sea.WaterLevel + 1f - ship.transform.position.y, 12f), Color.red, Color.red);

            for (int i = 0; i < Depths.Length; i++)
            {
                float outward = 6f + i * Spacing;
                Disc(new Vector3(outward, -Depths[i], 0f), new Color(0.02f, 0.02f, 0.03f), Color.black);
                Disc(new Vector3(-outward, -Depths[i], 0f), new Color(0.6f, 0.7f, 0.65f), new Color(0.35f, 0.45f, 0.4f));
            }
            return $"Shaders {LitShader.name} and {GlowShader.name}. A red disc 1 m above the water, 12 m ahead of the ship. " +
                   "Probe: discs at " + string.Join(", ", System.Array.ConvertAll(Depths, d => d + " m")) +
                   " below, nearest first. Dark to starboard (right), glowing to port (left).";
        }

        internal static void Clear()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        private static void Disc(Vector3 local, Color color, Color glow)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(_root.transform, false);
            disc.transform.localPosition = local;
            disc.transform.localScale = new Vector3(DiscSize, 0.05f, DiscSize);

            // The primitive's own material uses Unity's Standard shader, which the game's build leaves out (it draws nothing).
            var renderer = disc.GetComponent<MeshRenderer>();
            var mat = new Material(glow != Color.black ? GlowShader : LitShader) { color = color };
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
