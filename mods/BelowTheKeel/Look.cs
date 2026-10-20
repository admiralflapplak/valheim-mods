using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Materials for the omens, made from the game's own. Shader.Find misses the game's shaders (they come in asset bundles), and the
    /// primitives' default material uses Unity's Standard shader, which the game's build leaves out: copy a game material instead. Only
    /// dark, opaque surfaces show under the water (the probe: pale or see-through ones vanish in the murk).
    /// </summary>
    internal static class Look
    {
        private static Material _darkSerpent;

        /// <summary>The sea serpent's scales, darkened almost to black.</summary>
        internal static Material DarkSerpent()
        {
            if (_darkSerpent != null) return _darkSerpent;

            Material source = null;
            GameObject serpent = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Serpent") : null;
            var skin = serpent != null ? serpent.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (skin != null) source = skin.sharedMaterial;

            _darkSerpent = source != null ? new Material(source) : new Material(FindShader("Custom/Creature"));
            _darkSerpent.name = "BelowTheKeel_DarkSerpent";
            _darkSerpent.color = new Color(0.05f, 0.06f, 0.07f);
            return _darkSerpent;
        }

        private static readonly System.Collections.Generic.Dictionary<Material, Material> _drowned =
            new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>A game material made grey-green and dark, as a body long in the sea (one copy per source material).</summary>
        internal static Material Drowned(Material source)
        {
            if (source == null) return null;
            if (_drowned.TryGetValue(source, out Material m) && m != null) return m;
            m = new Material(source) { name = "BelowTheKeel_Drowned_" + source.name };
            m.color = new Color(0.22f, 0.27f, 0.25f);
            _drowned[source] = m;
            return m;
        }

        private static Material _rope;

        /// <summary>Dark, wet rope (the lit shader the probe's dark discs used: shows under the water).</summary>
        internal static Material Rope()
        {
            if (_rope != null) return _rope;
            _rope = new Material(FindShader("Custom/Creature") ?? FindShader("Legacy Shaders/Diffuse"))
            {
                name = "BelowTheKeel_Rope",
                color = new Color(0.12f, 0.1f, 0.07f)
            };
            return _rope;
        }

        internal static Shader FindShader(string name)
        {
            foreach (Shader sh in Resources.FindObjectsOfTypeAll<Shader>())
                if (sh.name == name) return sh;
            return null;
        }

        internal static void Forget()
        {
            if (_darkSerpent != null) Object.Destroy(_darkSerpent);
            _darkSerpent = null;
            foreach (Material m in _drowned.Values)
                if (m != null) Object.Destroy(m);
            _drowned.Clear();
            if (_rope != null) Object.Destroy(_rope);
            _rope = null;
        }
    }
}
