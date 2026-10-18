using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponOils
{
    /// <summary>
    /// The oils' own models, made in code (no asset bundle), after the game's own meads and the Bog Witch's wares: a finished oil is a
    /// six-sided bottle of clear glass with the oil inside it, a leather band round its middle, a pale cork bound
    /// with cord at the neck; an oil base is a wooden mortar full of the oil's paste with the pestle left in it. Both wear a cord with a
    /// charm that says where the oil comes from (a wooden disc for the Meadows, a tooth for the Black Forest, a wax seal for the Swamp...).
    /// The shapes are profiles spun round a few times, as the game's low-poly items are, then bent out of true (Warp: each oil leans and
    /// wobbles its own way) and shaded soft, dressed in copies of the game's own standard material with a coarse pixel mottle like its
    /// painted textures, so they take the game's light as its items do.
    /// </summary>
    internal static class OilModels
    {
        private static readonly Color Glass = new Color(0.84f, 0.88f, 0.86f);
        private static readonly Color Cork = new Color(0.86f, 0.72f, 0.48f);
        private static readonly Color Wood = new Color(0.50f, 0.34f, 0.20f);
        private static readonly Color PaleWood = new Color(0.68f, 0.52f, 0.34f);
        private static readonly Color Leather = new Color(0.36f, 0.22f, 0.12f);

        /// <summary>What marks an oil's biome on its crock and flask.</summary>
        private enum Charm { Disc, Tooth, Seal, Bead, Ring }

        private sealed class BiomeLook
        {
            public Color Cord;
            public Charm Charm;
            public Color CharmColour;
            public float CharmGlow;   // 0: none
        }

        private static BiomeLook LookOf(string biome)
        {
            switch (biome)
            {
                case "Meadows": return new BiomeLook { Cord = new Color(0.80f, 0.70f, 0.42f), Charm = Charm.Disc, CharmColour = Wood };
                case "Black Forest": return new BiomeLook { Cord = Leather, Charm = Charm.Tooth, CharmColour = new Color(0.90f, 0.86f, 0.74f) };
                case "Swamp": return new BiomeLook { Cord = new Color(0.30f, 0.32f, 0.22f), Charm = Charm.Seal, CharmColour = new Color(0.16f, 0.22f, 0.12f) };
                case "Mountain": return new BiomeLook { Cord = new Color(0.85f, 0.85f, 0.82f), Charm = Charm.Bead, CharmColour = new Color(0.78f, 0.80f, 0.84f) };
                case "Plains": return new BiomeLook { Cord = new Color(0.78f, 0.62f, 0.30f), Charm = Charm.Disc, CharmColour = new Color(0.72f, 0.48f, 0.22f) };
                case "Mistlands": return new BiomeLook { Cord = new Color(0.10f, 0.09f, 0.12f), Charm = Charm.Bead, CharmColour = new Color(0.55f, 0.30f, 0.85f), CharmGlow = 1.2f };
                case "Ashlands": return new BiomeLook { Cord = new Color(0.20f, 0.20f, 0.22f), Charm = Charm.Ring, CharmColour = new Color(1f, 0.35f, 0.08f), CharmGlow = 1.5f };
                default: return new BiomeLook { Cord = Leather, Charm = Charm.Disc, CharmColour = Wood };
            }
        }

        // ---- the two models --------------------------------------------------------------------------------------

        /// <summary>The mortar of an oil base, about 0.3 m wide, standing on the origin, its charm to the front (-z).</summary>
        internal static GameObject Mortar(Transform parent, OilDef def, Color colour, Material basis, int layer)
        {
            var root = NewPart("model", parent, layer);
            const float S = 1.6f;   // drawn at a real mortar's size, shown larger as the game's items are
            root.transform.localScale = Vector3.one * S;
            BiomeLook look = LookOf(def.Biome);
            var rnd = new System.Random(Seed(def.Id, 1));
            // hand-carved: a little lopsided, its rim not quite level (the cord and the paste follow the same wobble)
            var bowl = new Warp(rnd, wobble: 0.035f, lean: 0.06f, tip: 0.1f, height: 0.08f);

            // the bowl, turned from one block: out round the foot and the belly, over the rim, and down inside
            Add(root, "mortar", Lathe(new[] { V(0f, 0f), V(0.062f, 0f), V(0.068f, 0.012f), V(0.082f, 0.022f), V(0.09f, 0.05f), V(0.088f, 0.07f),
                V(0.081f, 0.078f), V(0.07f, 0.076f), V(0.066f, 0.06f), V(0.05f, 0.036f), V(0f, 0.032f) }, 10, 0f, bowl), Mat(basis, Wood, 0.1f), layer);
            // the paste heaped in it, in the oil's colour, lumpier than the bowl
            var paste = new Warp(rnd, wobble: 0.12f, lean: 0f, tip: 0.35f, height: 0.088f) { Lean = bowl.Lean };
            Add(root, "paste", Lathe(new[] { V(0.067f, 0.058f), V(0.06f, 0.066f), V(0.042f, 0.078f), V(0.02f, 0.086f), V(0f, 0.088f) }, 9, 0.5f, paste),
                Mat(basis, colour, 0.35f), layer);
            // the pestle, left leaning in it, never twice the same way
            var pestle = Add(root, "pestle", Lathe(new[] { V(0f, 0f), V(0.011f, 0.002f), V(0.014f, 0.018f), V(0.01f, 0.05f), V(0.008f, 0.11f),
                V(0.01f, 0.122f), V(0f, 0.128f) }, 7, 0f, new Warp(rnd, 0.07f, 0.03f, 0f, 0.128f)), Mat(basis, PaleWood, 0.1f), layer,
                new Vector3(0.022f, 0f, 0.012f) + bowl.Centre(0.06f));
            pestle.transform.localRotation = Quaternion.Euler(12f + Range(rnd, -6f, 6f), 20f + Range(rnd, -25f, 25f), -38f + Range(rnd, -6f, 6f));
            Add(root, "cord", Ring(0.091f, 0.004f, 10, 5, 0.05f, bowl), Mat(basis, look.Cord, 0.05f), layer);
            AddCharm(root, look, basis, layer, bowl.Apply(V(0.092f, 0.05f), 0f), bowl.Apply(V(0.088f, 0.028f), 0f), -8f, 0.8f, rnd);
            return root;
        }

        /// <summary>The bottle of a finished oil, about 0.33 m tall, standing on the origin, its charm to the front (-z).</summary>
        internal static GameObject Bottle(Transform parent, OilDef def, Color colour, Material basis, int layer)
        {
            var root = NewPart("model", parent, layer);
            const float S = 1.9f;
            root.transform.localScale = Vector3.one * S;
            BiomeLook look = LookOf(def.Biome);
            const float Fill = 0.07f;   // how high the oil comes
            var rnd = new System.Random(Seed(def.Id, 2));
            // hand-blown: leaning a little, its sides not quite even, its shoulder sloping; the oil, the band and the cords follow the same shape
            var blown = new Warp(rnd, wobble: 0.04f, lean: 0.07f, tip: 0.1f, height: 0.158f);

            // the oil inside, a little smaller than the bottle so the glass shows round it, with a meniscus on top (level, as a liquid is)
            Color liquid = Saturated(colour);   // (richer than the oil's own colour, which came out pastel behind the glass)
            _lastOil = Mat(basis, liquid, 0.75f, OilGlow);
            _lastOilColour = liquid;
            Add(root, "oil", Lathe(new[] { V(0f, 0.006f), V(0.04f, 0.006f), V(0.045f, 0.012f), V(0.046f, Fill - 0.004f), V(0.041f, Fill),
                V(0f, Fill + 0.002f) }, 6, 0.5f, blown.Level()), _lastOil, layer);
            // the bottle round it: clear glass where the game has a see-through material to copy, else pale opaque glass over the lower part
            Material glass = GlassMat();
            if (glass != null)
                Add(root, "glass", Lathe(new[] { V(0f, 0f), V(0.046f, 0f), V(0.051f, 0.008f), V(0.053f, 0.075f), V(0.052f, 0.078f), V(0.046f, 0.09f),
                    V(0.025f, 0.102f), V(0.015f, 0.112f), V(0.014f, 0.148f), V(0.018f, 0.152f), V(0.017f, 0.158f), V(0f, 0.158f) }, 6, 0.5f, blown), glass, layer);
            else
                Add(root, "glass", Lathe(new[] { V(0.053f, Fill), V(0.052f, 0.078f), V(0.046f, 0.09f), V(0.025f, 0.102f), V(0.015f, 0.112f),
                    V(0.014f, 0.148f), V(0.018f, 0.152f), V(0.017f, 0.158f), V(0f, 0.158f) }, 6, 0.5f, blown), Mat(basis, Glass, 0.8f), layer);
            Add(root, "band", Lathe(new[] { V(0.052f, 0.026f), V(0.0555f, 0.027f), V(0.0555f, 0.049f), V(0.052f, 0.05f) }, 6, 0.5f, blown),
                Mat(basis, Leather, 0.15f), layer);
            // the cork, pushed in a little crooked, its top cut unevenly
            var cork = Add(root, "cork", Lathe(new[] { V(0f, 0f), V(0.0125f, 0f), V(0.0145f, 0.024f), V(0.012f, 0.029f), V(0f, 0.03f) }, 8, 0f,
                new Warp(rnd, 0.06f, 0f, 0.4f, 0.03f)), Mat(basis, Cork, 0.05f), layer, blown.Centre(0.148f));
            cork.transform.localRotation = Quaternion.Euler(Range(rnd, -8f, 8f), Range(rnd, 0f, 360f), Range(rnd, -8f, 8f));
            Material cord = Mat(basis, look.Cord, 0.05f);
            Add(root, "cord", Ring(0.0155f, 0.0028f, 8, 5, 0.132f, blown), cord, layer);
            Add(root, "cord", Ring(0.0155f, 0.0028f, 8, 5, 0.138f, blown), cord, layer);
            AddCharm(root, look, basis, layer, blown.Apply(V(0.016f, 0.132f), 0f), blown.Apply(V(0.05f, 0.088f), 0f), 30f, 0.6f, rnd);
            return root;
        }

        /// <summary>The charm, hanging on a short cord from the neck cord at <paramref name="from"/> to rest against the body at <paramref name="at"/>, a little askew.</summary>
        private static void AddCharm(GameObject root, BiomeLook look, Material basis, int layer, Vector3 from, Vector3 at, float tilt, float size, System.Random rnd)
        {
            Material cord = Mat(basis, look.Cord, 0.05f);
            Vector3 d = at - from;
            var hang = Add(root, "hang", Lathe(new[] { V(0f, 0f), V(0.0018f, 0f), V(0.0018f, 1f), V(0f, 1f) }, 5), cord, layer, from);
            hang.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            hang.transform.localScale = new Vector3(1f, d.magnitude, 1f);

            Material m = Mat(basis, look.CharmColour, look.Charm == Charm.Bead ? 0.7f : 0.2f, look.CharmGlow);
            Mesh shape;
            switch (look.Charm)
            {
                case Charm.Tooth: shape = Lathe(new[] { V(0f, -0.03f), V(0.006f, -0.018f), V(0.009f, 0f), V(0.006f, 0.004f), V(0f, 0.005f) }, 6); break;
                case Charm.Bead: shape = Lathe(new[] { V(0f, -0.012f), V(0.009f, -0.008f), V(0.012f, 0f), V(0.009f, 0.008f), V(0f, 0.012f) }, 8); break;
                case Charm.Seal: shape = Lathe(new[] { V(0f, -0.004f), V(0.02f, -0.004f), V(0.023f, 0f), V(0.018f, 0.004f), V(0f, 0.005f) }, 9); break;
                case Charm.Ring: shape = Ring(0.012f, 0.0035f, 10, 5); break;
                default: shape = Lathe(new[] { V(0f, -0.004f), V(0.02f, -0.004f), V(0.02f, 0.004f), V(0f, 0.004f) }, 10); break;
            }
            var charm = Add(root, "charm", shape, m, layer, at);
            charm.transform.localScale = Vector3.one * size;
            // flat charms lie against the body, facing out; a tooth hangs point down
            bool flat = look.Charm == Charm.Disc || look.Charm == Charm.Seal || look.Charm == Charm.Ring;
            Quaternion askew = Quaternion.Euler(0f, Range(rnd, -18f, 18f), Range(rnd, -14f, 14f));
            charm.transform.localRotation = askew * (flat ? Quaternion.Euler(-90f + tilt, 0f, 0f) : Quaternion.Euler(-tilt, 0f, 0f));
        }

        // ---- the hand-made look ----------------------------------------------------------------------------------

        /// <summary>A stable number from an oil's id (not string.GetHashCode, which may change between runs), so each oil always has the same quirks.</summary>
        private static int Seed(string id, int salt)
        {
            unchecked
            {
                int h = (int)2166136261 ^ salt;
                foreach (char c in id) h = (h ^ c) * 16777619;
                return h;
            }
        }

        private static float Range(System.Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

        /// <summary>
        /// How a turned shape strays from true, as a thing made by hand does: its outline wobbles round (a slow two- and three-lobed swell
        /// that drifts with height), it leans to one side, and its upper rings tip (a sloping shoulder, an uneven rim). The bottom stays flat
        /// and centred, so it still stands. Parts that must fit together share one.
        /// </summary>
        private sealed class Warp
        {
            public Vector2 Lean;   // sideways drift per metre of height (x, z)
            private float _wobble, _tip, _height, _p1, _p2, _p3;

            public Warp(System.Random rnd, float wobble, float lean, float tip, float height)
            {
                _wobble = wobble; _tip = tip; _height = height;
                _p1 = Range(rnd, 0f, Mathf.PI * 2f); _p2 = Range(rnd, 0f, Mathf.PI * 2f); _p3 = Range(rnd, 0f, Mathf.PI * 2f);
                float dir = Range(rnd, 0f, Mathf.PI * 2f);
                Lean = new Vector2(Mathf.Cos(dir), Mathf.Sin(dir)) * lean * Range(rnd, 0.6f, 1f);
            }

            /// <summary>The same shape without the tipping: for a liquid, whose surface stays level.</summary>
            public Warp Level()
            {
                var w = (Warp)MemberwiseClone();
                w._tip = 0f;
                return w;
            }

            /// <summary>Where the axis is at a height (for a part set on it, as a cork in the neck).</summary>
            public Vector3 Centre(float y) => new Vector3(Lean.x * y, y, Lean.y * y);

            /// <summary>A profile point (radius, height) at an angle round (0: the front, -z).</summary>
            public Vector3 Apply(Vector2 p, float a)
            {
                float r = p.x * (1f + _wobble * (0.6f * Mathf.Sin(2f * a + _p1 + p.y * 25f) + 0.4f * Mathf.Sin(3f * a + _p2)));
                float y = p.y + _tip * r * Mathf.Sin(a + _p3) * Mathf.Clamp01(p.y / _height);
                return new Vector3(Mathf.Sin(a) * r + Lean.x * p.y, y, -Mathf.Cos(a) * r + Lean.y * p.y);
            }
        }

        // ---- parts and materials ---------------------------------------------------------------------------------

        private static Vector2 V(float r, float y) => new Vector2(r, y);

        private static GameObject NewPart(string name, Transform parent, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject Add(GameObject root, string name, Mesh mesh, Material material, int layer, Vector3? at = null)
        {
            var go = NewPart(name, root.transform, layer);
            if (at.HasValue) go.transform.localPosition = at.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>The game's own see-through material (the crystal wall's), copied for our glass; set by OilItems before the models are made.</summary>
        internal static Material GlassBasis;
        private static Material _glass;

        // Smoky, not pale: pale glass laid over the oil washed every colour to a pastel (0.6 hid it behind milky white altogether), and
        // clearer glass (0.28) all but vanished on an icon.
        private static readonly Color GlassColour = new Color(0.55f, 0.66f, 0.6f, 0.4f);
        private const float OilGlow = 0.45f, IconOilGlow = 0.9f;   // a gentle glow in the world (more would light up like a lamp at night)
        private static Material _lastOil;   // the oil inside the bottle made last (its icon is drawn straight after)
        private static Color _lastOilColour;

        /// <summary>
        /// For an icon, while it is drawn and back after: the oil inside glows harder, so its colour comes through the glass at icon size
        /// as strongly as a mead's. (The icon is drawn at once, so nothing else sees the change.)
        /// </summary>
        internal static void IconGlass(bool on)
        {
            if (_lastOil != null && _lastOil.HasProperty("_EmissionColor"))
                _lastOil.SetColor("_EmissionColor", _lastOilColour * (on ? IconOilGlow : OilGlow));
        }

        /// <summary>The same hue, much richer: pale colours (Storm's light yellow, Rime's ice blue) came out near white behind the glass.</summary>
        private static Color Saturated(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.5f + 0.25f), Mathf.Clamp01(v * 0.95f));
        }

        /// <summary>Clear, faintly green glass: a copy of the game's crystal window (already set up to be see-through), its pattern taken off.</summary>
        private static Material GlassMat()
        {
            if (_glass != null) return _glass;
            if (GlassBasis == null) return null;
            var m = new Material(GlassBasis) { name = "nick_oil_glass", color = GlassColour };
            foreach (string map in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap" })
                if (m.HasProperty(map)) m.SetTexture(map, null);
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICGLOSSMAP");
            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.92f);
            _glass = m;
            return m;
        }
        private static Texture2D _mottle;

        /// <summary>A copy of the game's standard material in a colour, with a faint mottled texture (and a glow, for a few charms).</summary>
        private static Material Mat(Material basis, Color colour, float gloss, float glow = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGB(colour) + gloss.ToString("0.00") + glow.ToString("0.0");
            if (Materials.TryGetValue(key, out Material cached) && cached != null) return cached;
            var m = new Material(basis) { name = "nick_oil_" + key, color = colour };
            if (m.HasProperty("_MainTex")) m.mainTexture = Mottle();
            foreach (string map in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap" })
                if (m.HasProperty(map)) m.SetTexture(map, null);
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICGLOSSMAP");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (glow > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", colour * glow);
            }
            else m.DisableKeyword("_EMISSION");
            Materials[key] = m;
            return m;
        }

        /// <summary>
        /// A small greyish-white texture of coarse square blotches, drawn without smoothing as the game's own item textures are, so a plain
        /// colour reads as painted wood, glass or leather rather than plastic.
        /// </summary>
        private static Texture2D Mottle()
        {
            if (_mottle != null) return _mottle;
            const int N = 32;
            var rnd = new System.Random(5);
            var coarse = new float[5, 5];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) coarse[x, y] = (float)rnd.NextDouble();
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x / (float)N * 4f, fy = y / (float)N * 4f;
                    int ix = (int)fx, iy = (int)fy;
                    float tx = fx - ix, ty = fy - iy;
                    float a = Mathf.Lerp(coarse[ix, iy], coarse[ix + 1, iy], tx), b = Mathf.Lerp(coarse[ix, iy + 1], coarse[ix + 1, iy + 1], tx);
                    float v = 0.76f + 0.14f * Mathf.Lerp(a, b, ty) + 0.10f * (float)rnd.NextDouble();
                    px[y * N + x] = new Color(v, v, v, 1f);
                }
            _mottle = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "nick_oil_mottle", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            _mottle.SetPixels(px);
            _mottle.Apply(false, true);
            return _mottle;
        }

        internal static void Forget() { Materials.Clear(); _glass = null; GlassBasis = null; _lastOil = null; }   // (the materials stay on prefabs already made; a new copy of the mod makes its own)

        // ---- meshes ----------------------------------------------------------------------------------------------

        /// <summary>
        /// A profile (radius, height pairs, bottom to top) spun round the y axis in <paramref name="sides"/> steps, bent by a warp if given. A point at
        /// radius 0 closes that end; one that doesn't is left open (a skin laid over another part). phase: turns it by part of a step (0.5: a flat face to the front).
        /// Shaded soft: faces meeting at less than <paramref name="crease"/> degrees blend into each other, sharper turns (a bottle's foot) stay crisp.
        /// </summary>
        private static Mesh Lathe(Vector2[] profile, int sides, float phase = 0f, Warp warp = null, float crease = 66f, float vScale = 4f)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                for (int s = 0; s < sides; s++)
                {
                    float a0 = (s + phase) * Mathf.PI * 2f / sides, a1 = (s + 1 + phase) * Mathf.PI * 2f / sides;
                    Vector3 v00 = P(p0, a0, warp), v01 = P(p0, a1, warp), v10 = P(p1, a0, warp), v11 = P(p1, a1, warp);
                    int b = verts.Count;
                    verts.Add(v00); verts.Add(v01); verts.Add(v11); verts.Add(v10);
                    float u0 = s / (float)sides, u1 = (s + 1) / (float)sides;
                    uvs.Add(new Vector2(u0, p0.y * vScale)); uvs.Add(new Vector2(u1, p0.y * vScale)); uvs.Add(new Vector2(u1, p1.y * vScale)); uvs.Add(new Vector2(u0, p1.y * vScale));
                    // wound so the outside faces out
                    if (p0.x > 1e-5f) { tris.Add(b); tris.Add(b + 3); tris.Add(b + 1); }
                    tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);   // (a sliver where p1 is on the axis: dropped below)
                }
            }
            var mesh = new Mesh { name = "nick_oil_lathe" };
            // every triangle gets its own corners, so each corner can be shaded with only the faces that should blend with it
            var fv = new List<Vector3>(); var fu = new List<Vector2>(); var ft = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-14f) continue;   // (a sliver where the profile meets the axis)
                int n = fv.Count;
                fv.Add(a); fv.Add(b); fv.Add(c);
                fu.Add(uvs[tris[t]]); fu.Add(uvs[tris[t + 1]]); fu.Add(uvs[tris[t + 2]]);
                ft.Add(n); ft.Add(n + 1); ft.Add(n + 2);
            }
            mesh.SetVertices(fv);
            mesh.SetUVs(0, fu);
            mesh.SetTriangles(ft, 0);
            mesh.SetNormals(SoftNormals(fv, crease));
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A rough round for the oil press (a post, a beam, a band, a pipe): shaped as Unity's cylinder primitive is (1 wide and 2 long on
        /// its y axis, centred, UVs running 0 to 1 round and along it), so it takes the same scaling and the same materials, but with few
        /// sides, a softly chamfered end, an outline that wobbles along it, a slight lean and an uneven cut, the same for a seed every time.
        /// The chamfer and the slope of the cut are a set size in metres (from <paramref name="length"/>), so a tall post ends nearly flat
        /// instead of whittled to a point (the mesh is stretched along its length by the part's scale).
        /// </summary>
        internal static Mesh RoughCylinder(int seed, int sides, float wobble, float lean, float length)
        {
            var rnd = new System.Random(seed);
            const int Rings = 5;
            float half = Mathf.Max(length, 0.01f) * 0.5f;   // metres per unit along it
            float chamfer = Mathf.Min(0.07f, 0.012f / half);
            float tip = Mathf.Min(wobble * 1.5f, 0.008f / half / 0.5f);   // the cut falls at most ~8 mm across the end
            var profile = new List<Vector2> { V(0f, 0f), V(0.43f, 0f), V(0.5f, chamfer) };
            for (int i = 1; i < Rings; i++) profile.Add(V(0.5f * (1f + Range(rnd, -wobble, wobble)), 2f * i / Rings));
            profile.Add(V(0.5f, 2f - chamfer));
            profile.Add(V(0.43f, 2f));
            profile.Add(V(0f, 2f));
            var warp = new Warp(rnd, wobble, lean, tip: tip, height: 2f);
            Mesh mesh = Lathe(profile.ToArray(), sides, Range(rnd, 0f, 1f), warp, vScale: 0.5f);
            mesh.name = "nick_oilpress_round";
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i].y -= 1f;   // centred, as the primitive is
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Each corner's normal: the faces round its point (weighted by size) that turn less than <paramref name="crease"/> degrees from its own face.</summary>
        private static List<Vector3> SoftNormals(List<Vector3> verts, float crease)
        {
            var faces = new Vector3[verts.Count / 3];
            for (int t = 0; t < faces.Length; t++)
                faces[t] = Vector3.Cross(verts[t * 3 + 1] - verts[t * 3], verts[t * 3 + 2] - verts[t * 3]);
            var at = new Dictionary<Vector3Int, List<int>>();   // the corners at each point (rounded to a hundredth of a millimetre: the seam closes)
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i] * 1e5f;
                var key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                if (!at.TryGetValue(key, out List<int> list)) at[key] = list = new List<int>();
                list.Add(i);
            }
            float cos = Mathf.Cos(crease * Mathf.Deg2Rad);
            var normals = new Vector3[verts.Count];
            foreach (List<int> corners in at.Values)
                foreach (int i in corners)
                {
                    Vector3 own = faces[i / 3].normalized, sum = Vector3.zero;
                    foreach (int j in corners)
                        if (Vector3.Dot(own, faces[j / 3].normalized) >= cos) sum += faces[j / 3];
                    normals[i] = sum.sqrMagnitude > 0f ? sum.normalized : own;
                }
            return new List<Vector3>(normals);
        }

        private static Vector3 P(Vector2 p, float a, Warp warp) =>
            warp != null ? warp.Apply(p, a) : new Vector3(Mathf.Sin(a) * p.x, p.y, -Mathf.Cos(a) * p.x);

        /// <summary>A ring (a cord tied round a neck): radius R, thickness r, lying flat at height y (bent by the warp of what it is tied round).</summary>
        private static Mesh Ring(float R, float r, int sides, int around, float y = 0f, Warp warp = null)
        {
            var profile = new Vector2[around + 1];
            for (int i = 0; i <= around; i++)
            {
                float a = -Mathf.PI / 2f + i * Mathf.PI * 2f / around;
                profile[i] = V(R + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r);
            }
            return Lathe(profile, sides, 0f, warp);
        }
    }
}
