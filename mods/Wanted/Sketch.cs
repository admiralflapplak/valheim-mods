using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wanted
{
    /// <summary>
    /// Illustrated notices (each player's own choice, "Illustrated notices"): instead of the exclamation mark, the paper shows what the
    /// notice wants, sketched in ink of its kind's colour from the game's own picture of it (a creature's trophy, an item's icon): its
    /// outline and its darker parts hatched, a little rough, soaked into the paper. Under it, the count as tally marks; across the top of
    /// a bounty, "WANTED" in straight, rune-like strokes (its own choice, "Wanted heading"). One drawing per notice wanted (kind, target,
    /// count), made the first time it's shown and kept. Anything without a picture keeps the plain notice.
    /// </summary>
    internal static class Sketch
    {
        private const int SW = 240, SH = 304;   // twice the plain notice: the drawing needs the detail

        private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();
        private static readonly Dictionary<Texture, Texture2D> Readable = new Dictionary<Texture, Texture2D>();

        /// <summary>The illustrated paper for this notice, or null (no picture of its target: the plain notice is shown).</summary>
        public static Material For(Contract c, ZNetScene scene, bool heading)
        {
            heading &= c.Kind == Kind.Kill;
            string key = $"{(int)c.Kind}|{c.Target}|{c.Count}|{heading}";
            if (Made.TryGetValue(key, out Material had)) return had;   // (null too: tried, no picture)
            Material m = null;
            try
            {
                Sprite icon = PictureOf(c, scene);
                if (icon != null)
                {
                    float[] mask = Draw(icon, c.Count, heading, 31.7f + (int)c.Kind * 17.3f);
                    if (mask != null)
                    {
                        Texture2D tex = Paper.Compose("wanted_sketch_" + c.Target, SW, SH, 13.7f + (int)c.Kind * 41.3f, Paper.Ink(c.Kind),
                            (x, y, u, v) => mask[y * SW + x]);
                        m = Paper.MaterialWith(scene, "wanted_sketch_" + c.Target, tex);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Sketching {c.Target}: {e.Message}"); }
            Made[key] = m;
            return m;
        }

        public static void Forget()
        {
            Made.Clear();   // (not destroyed: boards standing may show them; a new copy draws its own)
            Icons.Clear();
            foreach (Texture2D t in Readable.Values) if (t != null) Object.Destroy(t);
            Readable.Clear();
        }

        /// <summary>The game's picture of what a notice wants: a creature's trophy, or the item itself.</summary>
        private static Sprite PictureOf(Contract c, ZNetScene scene)
        {
            GameObject go = scene != null ? scene.GetPrefab(c.Target) : null;
            if (go == null) return null;
            if (c.Kind == Kind.Kill)
            {
                foreach (CharacterDrop.Drop d in go.GetComponent<CharacterDrop>()?.m_drops ?? new List<CharacterDrop.Drop>())
                {
                    ItemDrop trophy = d?.m_prefab != null ? d.m_prefab.GetComponent<ItemDrop>() : null;
                    if (trophy != null && trophy.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) return IconOf(trophy);
                }
                return null;
            }
            return IconOf(go.GetComponent<ItemDrop>());
        }

        private static Sprite IconOf(ItemDrop item)
        {
            Sprite[] icons = item != null ? item.m_itemData.m_shared.m_icons : null;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }

        // ---- the drawing (a mask: how much ink at each pixel of the sheet, rows from the bottom) -----------------------------

        /// <summary>
        /// The notice in the bag, illustrated: the same sketch on the same paper as on the board (the heading too), without the tally
        /// marks (they'd blur at the size of a bag's slot; the tooltip has the count), the picture as big as fits; a red wax seal once a
        /// bounty is done. Null when there's no picture (the plain icon is shown).
        /// </summary>
        public static Sprite IconFor(Contract c, ZNetScene scene, bool heading, bool done)
        {
            heading &= c.Kind == Kind.Kill;
            string key = $"{(int)c.Kind}|{c.Target}|{heading}|{done}";
            if (Icons.TryGetValue(key, out Sprite had)) return had;
            Sprite sprite = null;
            try
            {
                Sprite picture = PictureOf(c, scene);
                float[] mask = picture != null ? Draw(picture, 0, heading, 31.7f + (int)c.Kind * 17.3f, forIcon: true) : null;
                if (mask != null)
                {
                    Texture2D sheet = Paper.Compose("wanted_sketch_icon_" + c.Target, SW, SH, 13.7f + (int)c.Kind * 41.3f, Paper.Ink(c.Kind),
                        (x, y, u, v) => mask[y * SW + x]);
                    sprite = Paper.IconFromSheet(sheet, "wanted_sketch_icon_" + c.Target, c.Kind == Kind.Kill || c.Kind == Kind.Trophy, done);
                    Object.Destroy(sheet);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Sketching the icon of {c.Target}: {e.Message}"); }
            Icons[key] = sprite;
            return sprite;
        }

        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        private static float[] Draw(Sprite icon, int count, bool heading, float seed, bool forIcon = false)
        {
            Color[] pic = Pixels(icon, out int iw, out int ih);
            if (pic == null) return null;
            var mask = new float[SW * SH];

            // the picture: a square in the middle, lower down when the heading takes the top; on an icon, bigger (no tally under it)
            float size = forIcon ? (heading ? 0.74f : 0.86f) : (heading ? 0.6f : 0.66f);
            float middle = forIcon ? (heading ? 0.4f : 0.48f) : (heading ? 0.48f : 0.56f);
            int box = Mathf.RoundToInt(SW * size);
            int cx = SW / 2, cy = Mathf.RoundToInt(SH * middle);
            DrawPicture(mask, pic, iw, ih, cx - box / 2, cy - box / 2, box, seed);

            // the heading under the nail, which goes in near the top (an icon has no nail: the heading goes higher and larger)
            if (heading) Letters(mask, "WANTED", SW / 2, Mathf.RoundToInt(SH * (forIcon ? 0.84f : 0.8f)), forIcon ? 34 : 28, seed);
            if (!forIcon) Tally(mask, Mathf.Clamp(count, 1, 40), SW / 2, Mathf.RoundToInt(SH * 0.16f), seed);
            return mask;
        }

        /// <summary>
        /// The picture as an ink sketch in a square on the sheet: its outline (where it turns from nothing to something, or from light to
        /// dark), its darker parts hatched (crossed where darkest), a faint wash over the rest; the line wavers a little, as by hand.
        /// </summary>
        private static void DrawPicture(float[] mask, Color[] pic, int iw, int ih, int x0, int y0, int box, float seed)
        {
            var a = new float[box * box];
            var l = new float[box * box];
            for (int y = 0; y < box; y++)
            for (int x = 0; x < box; x++)
            {
                Color c = Sample(pic, iw, ih, (x + 0.5f) / box, (y + 0.5f) / box);
                a[y * box + x] = c.a;
                l[y * box + x] = (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) * c.a;
            }
            float At(float[] f, int x, int y) => f[Mathf.Clamp(y, 0, box - 1) * box + Mathf.Clamp(x, 0, box - 1)];
            float Sobel(float[] f, int x, int y)
            {
                float gx = At(f, x + 1, y - 1) + 2 * At(f, x + 1, y) + At(f, x + 1, y + 1) - At(f, x - 1, y - 1) - 2 * At(f, x - 1, y) - At(f, x - 1, y + 1);
                float gy = At(f, x - 1, y + 1) + 2 * At(f, x, y + 1) + At(f, x + 1, y + 1) - At(f, x - 1, y - 1) - 2 * At(f, x, y - 1) - At(f, x + 1, y - 1);
                return Mathf.Sqrt(gx * gx + gy * gy);
            }

            for (int y = 0; y < box; y++)
            for (int x = 0; x < box; x++)
            {
                int sx = x0 + x, sy = y0 + y;
                if (sx < 0 || sy < 0 || sx >= SW || sy >= SH) continue;
                float alpha = a[y * box + x];
                float outline = Mathf.Clamp01(Sobel(a, x, y) * 0.9f + Sobel(l, x, y) * 1.1f);
                float dark = alpha > 0.5f ? Mathf.Clamp01(1f - l[y * box + x] / Mathf.Max(alpha, 0.01f)) : 0f;
                float hatch = 0f;
                if (dark > 0.35f && Mod(x + y, 6) < 2) hatch = 0.8f;
                if (dark > 0.6f && Mod(x - y, 6) < 2) hatch = 0.9f;
                float wash = alpha * 0.1f;
                float ink = Mathf.Max(outline, Mathf.Max(hatch, wash));
                ink *= 0.75f + 0.5f * Mathf.PerlinNoise(sx * 0.15f + seed, sy * 0.15f - seed);   // the hand wavers
                int i = sy * SW + sx;
                mask[i] = Mathf.Max(mask[i], Mathf.Clamp01(ink));
            }
        }

        /// <summary>The count as tally marks: four strokes and one across, in rows of up to four bundles.</summary>
        private static void Tally(float[] mask, int count, int cx, int cy, float seed)
        {
            const float tall = 30f, gap = 8f, between = 14f;
            float bundle = 3 * gap;
            int bundles = (count + 4) / 5, perRow = Math.Min(4, bundles), rows = (bundles + 3) / 4;
            float rowWidth = perRow * bundle + (perRow - 1) * between;
            var rnd = new System.Random((int)(seed * 100) + count);
            float J() => (float)rnd.NextDouble() * 3f - 1.5f;
            for (int i = 0; i < count; i++)
            {
                int b = i / 5, k = i % 5, row = b / 4, col = b % 4;
                float left = cx - rowWidth / 2f + col * (bundle + between);
                float bottom = cy - tall / 2f + ((rows - 1) / 2f - row) * (tall + 10f);   // (rows stacked about the middle, the first on top)
                if (k < 4)
                {
                    float x = left + k * gap;
                    Stroke(mask, x + J(), bottom + J(), x + J(), bottom + tall + J(), 2.6f, seed + i);
                }
                else Stroke(mask, left - 5f + J(), bottom + 6f + J(), left + bundle + 5f + J(), bottom + tall - 6f + J(), 2.6f, seed + i);
            }
        }

        /// <summary>Capital letters in straight strokes (only those "WANTED" needs), each in a box <paramref name="tall"/> high.</summary>
        private static void Letters(float[] mask, string word, int cx, int cy, float tall, float seed)
        {
            float wide = tall * 0.72f, space = tall * 0.24f;
            float total = word.Length * wide + (word.Length - 1) * space;
            float x0 = cx - total / 2f, y0 = cy - tall / 2f;
            for (int n = 0; n < word.Length; n++)
            {
                float lx = x0 + n * (wide + space);
                Vector2 P(float u, float v) => new Vector2(lx + u * wide + (v - 0.5f) * tall * 0.12f, y0 + v * tall);   // (a slight lean)
                void Line(params float[] uv)
                {
                    for (int i = 0; i + 3 < uv.Length; i += 2)
                    {
                        Vector2 a = P(uv[i], uv[i + 1]), b = P(uv[i + 2], uv[i + 3]);
                        Stroke(mask, a.x, a.y, b.x, b.y, 3.2f, seed + n * 7 + i);
                    }
                }
                switch (word[n])
                {
                    case 'W': Line(0, 1, 0.25f, 0, 0.5f, 0.6f, 0.75f, 0, 1, 1); break;
                    case 'A': Line(0, 0, 0.5f, 1, 1, 0); Line(0.24f, 0.42f, 0.76f, 0.42f); break;
                    case 'N': Line(0, 0, 0, 1, 1, 0, 1, 1); break;
                    case 'T': Line(0, 1, 1, 1); Line(0.5f, 1, 0.5f, 0); break;
                    case 'E': Line(1, 1, 0, 1, 0, 0, 1, 0); Line(0, 0.5f, 0.7f, 0.5f); break;
                    case 'D': Line(0, 0, 0, 1, 0.55f, 1, 1, 0.72f, 1, 0.28f, 0.55f, 0, 0, 0); break;
                }
            }
        }

        /// <summary>A brush stroke from one point to another, its edge ragged and its width wavering.</summary>
        private static void Stroke(float[] mask, float x0, float y0, float x1, float y1, float width, float seed)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - width - 2)), maxX = Mathf.Min(SW - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + width + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - width - 2)), maxY = Mathf.Min(SH - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + width + 2));
            var d = new Vector2(x1 - x0, y1 - y0);
            float len2 = Mathf.Max(d.sqrMagnitude, 0.001f);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f - x0, y + 0.5f - y0);
                float t = Mathf.Clamp01(Vector2.Dot(p, d) / len2);
                float dist = (p - d * t).magnitude;
                float w = width * (0.75f + 0.5f * Mathf.PerlinNoise(t * 4f + seed, seed * 0.37f));
                float ink = Mathf.Clamp01((w - dist) / 1.3f);
                if (ink <= 0f) continue;
                int i = y * SW + x;
                mask[i] = Mathf.Max(mask[i], ink);
            }
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static Color Sample(Color[] pic, int w, int h, float u, float v)
        {
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x = Mathf.FloorToInt(fx), y = Mathf.FloorToInt(fy);
            float tx = fx - x, ty = fy - y;
            Color P(int px, int py) => pic[Mathf.Clamp(py, 0, h - 1) * w + Mathf.Clamp(px, 0, w - 1)];
            return Color.Lerp(Color.Lerp(P(x, y), P(x + 1, y), tx), Color.Lerp(P(x, y + 1), P(x + 1, y + 1), tx), ty);
        }

        /// <summary>A sprite's pixels (from its place on its atlas), read through the GPU: game textures can't be read directly.</summary>
        private static Color[] Pixels(Sprite sprite, out int w, out int h)
        {
            Rect r = sprite.textureRect;
            w = Mathf.RoundToInt(r.width);
            h = Mathf.RoundToInt(r.height);
            Texture source = sprite.texture;
            if (source == null || w <= 0 || h <= 0) return null;
            if (!Readable.TryGetValue(source, out Texture2D atlas) || atlas == null)
            {
                RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, target);
                    RenderTexture.active = target;
                    atlas = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                    atlas.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    atlas.Apply(false);
                    Readable[source] = atlas;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                }
            }
            return atlas.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h);
        }
    }
}
