using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wanted
{
    /// <summary>The kinds of contract a notice can be. The number is saved (on boards and papers): never renumber, only add.</summary>
    internal enum Kind { Kill = 0, Gather = 1, Trophy = 2, Provisions = 3 }

    /// <summary>
    /// The look of a notice: aged paper with torn edges and a painted exclamation mark in its kind's colour. The game has no plain paper
    /// texture (only the cartography table's map), so the paper is drawn here once per kind, from fixed seeds (every player sees the same
    /// notices), and shown with a copy of the cartography table's material: the game's own shader, which cuts the torn edges out.
    /// </summary>
    internal static class Paper
    {
        public const float Width = 0.33f, Height = 0.42f;   // metres
        private const int W = 120, H = 152;                 // pixels: about 4 mm each, as fine as the game's own wood

        /// <summary>The ink of each kind's mark: strong, darker colours that stand out on the paper from a few metres away.</summary>
        public static Color Ink(Kind kind)
        {
            switch (kind)
            {
                case Kind.Kill: return new Color(0.58f, 0.06f, 0.04f);       // blood red
                case Kind.Gather: return new Color(0.16f, 0.36f, 0.10f);     // leaf green
                case Kind.Trophy: return new Color(0.70f, 0.42f, 0.04f);     // amber
                default: return new Color(0.10f, 0.24f, 0.50f);              // woad blue
            }
        }

        private static readonly Dictionary<Kind, Material> Materials = new Dictionary<Kind, Material>();
        private static Material _label;

        /// <summary>The box's label: the same paper with a gold coin painted on it (where you're paid). Null without a material to copy.</summary>
        public static Material Label(ZNetScene scene)
        {
            if (_label != null) return _label;
            Material source = BaseMaterial(scene);
            if (source == null) return null;
            _label = Flat(new Material(source) { name = "wanted_label" });
            _label.mainTexture = Draw(Kind.Kill, coin: true);
            return _label;
        }

        /// <summary>The material of a notice of this kind (made the first time it's asked for). Null when the game has no material to copy.</summary>
        public static Material For(Kind kind, ZNetScene scene)
        {
            if (Materials.TryGetValue(kind, out Material made) && made != null) return made;
            Material source = BaseMaterial(scene);
            if (source == null) return null;
            var m = Flat(new Material(source) { name = "wanted_paper_" + kind });
            m.mainTexture = Draw(kind);
            Materials[kind] = m;
            return m;
        }

        /// <summary>A paper material showing this texture (a copy of the cartography table's, as every notice's). Null without one to copy.</summary>
        public static Material MaterialWith(ZNetScene scene, string name, Texture2D texture)
        {
            Material source = BaseMaterial(scene);
            if (source == null) return null;
            Material m = Flat(new Material(source) { name = name });
            m.mainTexture = texture;
            return m;
        }

        /// <summary>A copy of the cartography table's material made fit for our paper: the whole texture, no bumps or metal, no wobble.</summary>
        private static Material Flat(Material m)
        {
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            // The table's bumps and metal belong to its atlas, not to our paper (its metal map put dark green spots on the notices).
            foreach (string map in new[] { "_BumpMap", "_MetallicTex", "_EmissionMap" })
                if (m.HasProperty(map)) m.SetTexture(map, null);
            m.DisableKeyword("_NORMALMAP");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            // Custom/Piece wobbles vertices up to 5 cm (_ValueNoiseVertex, _RippleDistance) to make the game's pieces look hand-built: on a
            // four-corner sheet it pushes corners back into the planks behind it. Paper lies flat.
            if (m.HasProperty("_ValueNoiseVertex")) m.SetFloat("_ValueNoiseVertex", 0f);
            if (m.HasProperty("_RippleDistance")) m.SetFloat("_RippleDistance", 0f);   // (how far it moves them, in case the switch alone isn't heeded)
            m.DisableKeyword("_VALUENOISEVERTEX_ON");
            return m;
        }

        /// <summary>
        /// The notice's picture in the bag: its paper, a little turned, on a square with nothing around it (the game draws item icons
        /// square). Null when the paper can't be made.
        /// </summary>
        public static Sprite IconFor(Kind kind, ZNetScene scene, bool done = false)
        {
            var paper = For(kind, scene)?.mainTexture as Texture2D;
            if (paper == null) return null;
            return IconFromSheet(paper, "wanted_icon_" + kind + (done ? "_done" : ""), kind == Kind.Kill || kind == Kind.Trophy, done);
        }

        /// <summary>A notice's picture in the bag from a sheet of its paper: a little turned (left or right), square, the seal if done.</summary>
        public static Sprite IconFromSheet(Texture2D paper, string name, bool turnLeft, bool done)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float h = size * 0.92f, w = h * W / H;   // the paper's own proportions
            float turn = (turnLeft ? -6f : 6f) * Mathf.Deg2Rad, cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // from this pixel back into the paper: unturn about the middle, then scale to 0..1
                float dx = x + 0.5f - size / 2f, dy = y + 0.5f - size / 2f;
                float u = (dx * cos + dy * sin) / w + 0.5f, v = (-dx * sin + dy * cos) / h + 0.5f;
                px[y * size + x] = u < 0f || u > 1f || v < 0f || v > 1f ? new Color(0, 0, 0, 0) : paper.GetPixelBilinear(u, v);
            }
            if (done) Seal(px, size, size * 0.72f, size * 0.24f, size * 0.17f);
            tex.SetPixels(px);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A red wax seal pressed on the paper (a finished notice): a blob with a ragged rim, darker at the edge, a pressed ring inside.</summary>
        private static void Seal(Color[] px, int size, float cx, float cy, float radius)
        {
            var wax = new Color(0.62f, 0.08f, 0.06f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float angle = Mathf.Atan2(dy, dx);
                float rim = radius * (1f + 0.12f * Mathf.PerlinNoise(Mathf.Cos(angle) * 2f + 5f, Mathf.Sin(angle) * 2f + 5f) - 0.06f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > rim + 1f) continue;
                float edge = Mathf.Clamp01(rim + 1f - r);                      // soft outer pixel
                float shade = 0.75f + 0.25f * Mathf.Clamp01(1f - r / rim);      // darker towards the rim
                if (Mathf.Abs(r - rim * 0.6f) < 1.2f) shade *= 0.75f;          // the stamp's ring
                Color c = wax * shade;
                c.a = 1f;
                int i = y * size + x;
                px[i] = Color.Lerp(px[i], c, edge);
            }
        }

        public static void Forget()
        {
            // Not destroyed: the old copy's prefab and the boards standing in the world still show them (destroying them left placed
            // boards with no paper or planks after a hot reload). Four small textures per reload, only while making the mod.
            Materials.Clear();
            _label = null;
            Sketch.Forget();
        }

        private static Material BaseMaterial(ZNetScene scene)
        {
            GameObject table = scene != null ? scene.GetPrefab("piece_cartographytable") : null;
            Transform high = table != null ? table.transform.Find("new/high") : null;
            MeshRenderer r = high != null ? high.GetComponent<MeshRenderer>() : null;
            return r != null ? r.sharedMaterial : null;
        }

        // ---- drawing ------------------------------------------------------------------------------------------------

        private static Texture2D Draw(Kind kind, bool coin = false)
        {
            float seed = coin ? 91.1f : 13.7f + (int)kind * 41.3f;
            return Compose(coin ? "wanted_label" : "wanted_paper_" + kind, W, H, seed, coin ? new Color(0.74f, 0.52f, 0.1f) : Ink(kind),
                (x, y, u, v) => coin ? Coin(u, v, seed) : Mark(u, v, seed));
        }

        /// <summary>
        /// A sheet of the notices' paper, <paramref name="w"/> by <paramref name="h"/> pixels (the proportions of a notice), with ink where
        /// <paramref name="paint"/> says (0 none, 1 solid; given the pixel and its place 0..1 on the sheet): warm parchment, cloudy and
        /// stained, browner at its torn edges, the ink laid on with a dry brush. Every size looks the same, only finer.
        /// </summary>
        public static Texture2D Compose(string name, int w, int h, float seed, Color ink, Func<int, int, float, float, float> paint)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            float k = w / (float)W;   // pixels per pixel of the plain notice: the edge's tear and burn keep their size on the sheet

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;

                // the torn edge: how far in from the nearest side, pushed in and out by noise along the edge
                float edge = Mathf.Min(Mathf.Min(u, 1f - u) * w, Mathf.Min(v, 1f - v) * h);
                float tear = (3.5f + 3f * Fbm(u * 9f + seed, v * 9f - seed, 3)) * k;
                if (edge < tear) { px[y * w + x] = new Color(0, 0, 0, 0); continue; }

                // paper: warm parchment, cloudy, with stains, darker and browner towards the edges
                float cloud = Fbm(u * 4f + seed, v * 4f + seed * 0.5f, 4);
                float grain = Mathf.PerlinNoise(u * 60f + seed, v * 60f);
                Color c = Color.Lerp(new Color(0.66f, 0.55f, 0.38f), new Color(0.86f, 0.77f, 0.58f), cloud);
                c *= 0.92f + 0.12f * grain;
                float stain = Mathf.Clamp01((Fbm(u * 2.5f - seed, v * 2.5f + 3.1f, 3) - 0.62f) * 4f);
                c = Color.Lerp(c, new Color(0.48f, 0.36f, 0.22f), stain * 0.55f);
                float burn = Mathf.Clamp01(1f - (edge - tear) / (14f * k));
                c = Color.Lerp(c, new Color(0.38f, 0.26f, 0.14f), burn * burn * 0.8f);

                float ink01 = paint(x, y, u, v);
                if (ink01 > 0f)
                {
                    float dry = 0.75f + 0.25f * Mathf.PerlinNoise(u * 30f - seed, v * 30f + seed);   // dry-brush streaks
                    c = Color.Lerp(c, ink * (0.85f + 0.3f * grain), Mathf.Clamp01(ink01) * dry);
                }
                c.a = 1f;
                px[y * w + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A coin, painted: a thick ring with a dot in the middle, ragged at its edges.</summary>
        private static float Coin(float u, float v, float seed)
        {
            float dx = u - 0.5f, dy = (v - 0.52f) * H / W;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ragged = (Mathf.PerlinNoise(u * 25f + seed, v * 25f) - 0.5f) * 0.03f;
            float ring = Mathf.Clamp01((0.06f + ragged - Mathf.Abs(r - 0.27f)) / 0.012f);
            float dot = Mathf.Clamp01((0.08f + ragged - r) / 0.012f);
            return Mathf.Max(ring, dot);
        }

        /// <summary>How much paint is at (u, v): 0 none, 1 solid, soft at the stroke's ragged edge.</summary>
        private static float Mark(float u, float v, float seed)
        {
            float wobble = (Mathf.PerlinNoise(v * 8f + seed, seed) - 0.5f) * 0.05f;
            float dx = Mathf.Abs(u - 0.5f - wobble);
            float ragged = (Mathf.PerlinNoise(u * 25f + seed, v * 25f) - 0.5f) * 0.03f;

            // the stroke, from 82 % of the height down to 40 %: wide at the top, narrowing to a point
            if (v > 0.40f && v < 0.84f)
            {
                float t = Mathf.InverseLerp(0.40f, 0.84f, v);
                float half = Mathf.Lerp(0.025f, 0.10f, t) + ragged;
                float ends = Mathf.Min(v - 0.40f, 0.84f - v) / 0.03f;   // rounded ends
                return Mathf.Clamp01((half - dx) / 0.012f) * Mathf.Clamp01(ends);
            }
            // the blot, round with a ragged edge
            float dy = (v - 0.27f) * H / W;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01((0.075f + ragged - r) / 0.012f);
        }

        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, y) * amp;
                total += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / total;
        }
    }
}
