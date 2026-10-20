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

        private static Material _hide;

        /// <summary>The serpent's darkened scales with its fins' glow taken out: a hide to hold something else's glow.</summary>
        internal static Material DarkHide()
        {
            if (_hide != null) return _hide;
            _hide = new Material(DarkSerpent()) { name = "BelowTheKeel_DarkHide" };
            if (_hide.HasProperty("_EmissionColor")) _hide.SetColor("_EmissionColor", Color.black);
            return _hide;
        }

        private static Material _eye;
        private static Texture2D _eyeAlbedo, _eyeGlow;

        /// <summary>The Eye's eyeball: the serpent's own material (it shows under the water) with the eye drawn on it, the iris aglow.</summary>
        internal static Material Eye()
        {
            if (_eye != null) return _eye;
            EyeMeshes.Draw(out _eyeAlbedo, out _eyeGlow);
            _eye = new Material(DarkSerpent()) { name = "BelowTheKeel_Eye", color = Color.white };
            _eye.SetTexture("_MainTex", _eyeAlbedo);
            _eye.SetTexture("_EmissionMap", _eyeGlow);
            _eye.SetColor("_EmissionColor", new Color(3f, 3f, 3f));
            if (_eye.HasProperty("_BumpMap")) _eye.SetTexture("_BumpMap", null);
            return _eye;
        }

        private static Material _eyeFire;
        private static Texture2D _fireAlbedo, _fireGlow;

        /// <summary>The Eye's eyeball, burning (after the icon): no white, the whole iris aglow, so it burns up through the water.</summary>
        internal static Material EyeFire()
        {
            if (_eyeFire != null) return _eyeFire;
            EyeMeshes.DrawFire(out _fireAlbedo, out _fireGlow);
            _eyeFire = new Material(DarkSerpent()) { name = "BelowTheKeel_EyeFire", color = Color.white };
            _eyeFire.SetTexture("_MainTex", _fireAlbedo);
            _eyeFire.SetTexture("_EmissionMap", _fireGlow);
            _eyeFire.SetColor("_EmissionColor", new Color(2.2f, 2.2f, 2.2f));
            if (_eyeFire.HasProperty("_BumpMap")) _eyeFire.SetTexture("_BumpMap", null);
            return _eyeFire;
        }

        private static Material _wood;

        /// <summary>Old, sea-worn boat wood (the karve wreckage's own material).</summary>
        internal static Material Wood()
        {
            if (_wood != null) return _wood;
            GameObject wreck = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("shipwreck_karve_stern") : null;
            var r = wreck != null ? wreck.GetComponentInChildren<MeshRenderer>(true) : null;
            _wood = r != null ? new Material(r.sharedMaterial) : new Material(FindShader("Custom/Piece") ?? FindShader("Legacy Shaders/Diffuse"));
            _wood.name = "BelowTheKeel_Wood";
            return _wood;
        }

        private static Material _ghost;
        private static Texture2D _ghostTex;
        internal static readonly Color GhostLightColour = new Color(0.12f, 0.8f, 0.65f);   // the draugr's own teal, toned well down: a faint light, not a lamp

        /// <summary>The light under Rán's drowned: black, glowing teal from a soft round spot (the glow is set per body, as they rise).</summary>
        internal static Material GhostLight()
        {
            if (_ghost != null) return _ghost;
            const int size = 128;
            _ghostTex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BelowTheKeel_GhostLight", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
                    float k = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    k = k * k * k;   // falls away fast: bright only near the middle, nothing at the rim
                    px[y * size + x] = new Color(k, k, k, 1f);
                }
            _ghostTex.SetPixels(px);
            _ghostTex.Apply(true);
            _ghost = new Material(DarkHide()) { name = "BelowTheKeel_GhostLight", color = Color.black };
            if (_ghost.HasProperty("_MainTex")) _ghost.SetTexture("_MainTex", null);
            if (_ghost.HasProperty("_BumpMap")) _ghost.SetTexture("_BumpMap", null);
            _ghost.SetTexture("_EmissionMap", _ghostTex);
            _ghost.SetColor("_EmissionColor", GhostLightColour);
            return _ghost;
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
            if (_ghost != null) Object.Destroy(_ghost);
            if (_ghostTex != null) Object.Destroy(_ghostTex);
            _ghost = null;
            _ghostTex = null;
            if (_wood != null) Object.Destroy(_wood);
            _wood = null;
            if (_eye != null) Object.Destroy(_eye);
            if (_eyeFire != null) Object.Destroy(_eyeFire);
            if (_fireAlbedo != null) Object.Destroy(_fireAlbedo);
            if (_fireGlow != null) Object.Destroy(_fireGlow);
            _eyeFire = null;
            _fireAlbedo = _fireGlow = null;
            if (_hide != null) Object.Destroy(_hide);
            _hide = null;
            if (_eyeAlbedo != null) Object.Destroy(_eyeAlbedo);
            if (_eyeGlow != null) Object.Destroy(_eyeGlow);
            _eye = null;
            _eyeAlbedo = _eyeGlow = null;
        }
    }
}
