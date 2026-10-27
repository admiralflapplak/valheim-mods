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
    /// each kind, its word in runes, or WANTED in letters on bounties (its own choice, "Heading"). One drawing per notice wanted (kind, target,
    /// count), made the first time it's shown and kept. Anything without a picture keeps the plain notice.
    /// </summary>
    /// <summary>What's lettered across the top of an illustrated notice (each player's choice).</summary>
    internal enum Heading { None, Wanted, Runes }

    internal static class Sketch
    {
        private const int SW = 240, SH = 304;   // twice the plain notice: the drawing needs the detail

        private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();
        private static readonly Dictionary<Texture, Texture2D> Readable = new Dictionary<Texture, Texture2D>();

        /// <summary>The illustrated paper for this notice, or null (no picture of its target: the plain notice is shown).</summary>
        public static Material For(Contract c, ZNetScene scene, Heading heading)
        {
            string word = WordFor(c.Kind, heading);
            string key = $"{(int)c.Kind}|{c.Target}|{c.Count}|{heading}";
            if (Made.TryGetValue(key, out Material had)) return had;   // (null too: tried, no picture)
            Material m = null;
            try
            {
                Sprite icon = PictureOf(c, scene);
                if (icon != null)
                {
                    float[] mask = Draw(icon, c.Count, word, heading == Heading.Runes, 31.7f + (int)c.Kind * 17.3f);
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

        /// <summary>For `wanted sketch`: the picture's size on its atlas, and the atlas's.</summary>
        public static string PictureInfo(Contract c, ZNetScene scene)
        {
            Sprite p = PictureOf(c, scene);
            return p == null ? "none" : $"{p.name}: rect {p.textureRect.width}x{p.textureRect.height} at {p.textureRect.x},{p.textureRect.y} on {p.texture.name} {p.texture.width}x{p.texture.height}, sprite bounds {p.bounds.size}";
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
        public static Sprite IconFor(Contract c, ZNetScene scene, Heading heading, bool done)
        {
            string word = WordFor(c.Kind, heading);
            string key = $"{(int)c.Kind}|{c.Target}|{heading}|{done}";
            if (Icons.TryGetValue(key, out Sprite had)) return had;
            Sprite sprite = null;
            try
            {
                Sprite picture = PictureOf(c, scene);
                float[] mask = picture != null ? Draw(picture, 0, word, heading == Heading.Runes, 31.7f + (int)c.Kind * 17.3f, forIcon: true) : null;
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

        /// <summary>
        /// The word across the top: in runes, each kind's own (a bounty WANTED, gathering NEEDED, trophies PROOF, provisions SUPPLY,
        /// spelt in the Elder Futhark), so one kind's notices all carry the same runes; or WANTED in letters, on bounties only; or none.
        /// </summary>
        private static string WordFor(Kind kind, Heading heading)
        {
            if (heading == Heading.Wanted) return kind == Kind.Kill ? "WANTED" : null;
            if (heading != Heading.Runes) return null;
            switch (kind)
            {
                case Kind.Kill: return "WANTED";
                case Kind.Gather: return "NEEDED";
                case Kind.Trophy: return "PROOF";
                default: return "SUPPLY";
            }
        }

        private static float[] Draw(Sprite icon, int count, string word, bool runes, float seed, bool forIcon = false)
        {
            bool heading = word != null;
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
            if (heading)
            {
                int hy = Mathf.RoundToInt(SH * (forIcon ? 0.84f : 0.8f));
                if (runes) Runes(mask, word, SW / 2, hy, forIcon ? 38 : 32, seed);
                else Letters(mask, word, SW / 2, hy, forIcon ? 34 : 28, seed);
            }
            if (!forIcon) Tally(mask, Math.Max(1, count), seed);
            return mask;
        }

        /// <summary>
        /// The picture as an ink sketch in a square on the sheet, the way one draws from a thing rather than tracing its shadow:
        ///   - its outline, where it turns from nothing to something;
        ///   - its inner lines: wherever its colour or its light changes sharply (a rim, a face, the stew in its bowl), and the fine dark
        ///     lines its painter drew (a pixel darker than the ones round it);
        ///   - hatching: light and sparse in the middle tones, closer in the darkest parts (crossed where darkest), the faintest wash over the rest;
        /// the line wavering a little, as by hand.
        /// </summary>
        private static void DrawPicture(float[] mask, Color[] pic, int iw, int ih, int x0, int y0, int box, float seed)
        {
            var a = new float[box * box];
            var l = new float[box * box];
            var r = new float[box * box];
            var g = new float[box * box];
            var b = new float[box * box];
            // the picture keeps its own proportions, fitted inside the square (another mod's icon may not be square)
            float fit = box / (float)Mathf.Max(iw, ih), dw = iw * fit, dh = ih * fit, ox = (box - dw) / 2f, oy = (box - dh) / 2f;
            for (int y = 0; y < box; y++)
            for (int x = 0; x < box; x++)
            {
                float pu = (x + 0.5f - ox) / dw, pv = (y + 0.5f - oy) / dh;
                Color c = pu < 0f || pu > 1f || pv < 0f || pv > 1f ? new Color(0, 0, 0, 0) : Sample(pic, iw, ih, pu, pv);
                int k = y * box + x;
                a[k] = c.a;
                r[k] = c.r * c.a; g[k] = c.g * c.a; b[k] = c.b * c.a;
                l[k] = 0.3f * r[k] + 0.59f * g[k] + 0.11f * b[k];
            }
            float At(float[] f, int x, int y) => f[Mathf.Clamp(y, 0, box - 1) * box + Mathf.Clamp(x, 0, box - 1)];
            float Sobel(float[] f, int x, int y)
            {
                float gx = At(f, x + 1, y - 1) + 2 * At(f, x + 1, y) + At(f, x + 1, y + 1) - At(f, x - 1, y - 1) - 2 * At(f, x - 1, y) - At(f, x - 1, y + 1);
                float gy = At(f, x - 1, y + 1) + 2 * At(f, x, y + 1) + At(f, x + 1, y + 1) - At(f, x - 1, y - 1) - 2 * At(f, x, y - 1) - At(f, x + 1, y - 1);
                return Mathf.Sqrt(gx * gx + gy * gy);
            }

            // the light round each pixel (a box blur about a fortieth of the picture across): a pixel darker than it is a drawn line
            int rad = Mathf.Max(1, box / 40);
            var around = new float[box * box];
            for (int y = 0; y < box; y++)
            for (int x = 0; x < box; x++)
            {
                float sum = 0f; int n = 0;
                for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++) { sum += At(l, x + dx, y + dy); n++; }
                around[y * box + x] = sum / n;
            }

            for (int y = 0; y < box; y++)
            for (int x = 0; x < box; x++)
            {
                int sx = x0 + x, sy = y0 + y;
                if (sx < 0 || sy < 0 || sx >= SW || sy >= SH) continue;
                int k = y * box + x;
                float alpha = a[k];
                float outline = Mathf.Clamp01(Sobel(a, x, y) * 1.1f);
                float inner = alpha > 0.4f
                    ? Mathf.Clamp01(Mathf.Max(Sobel(l, x, y) * 1.6f, Mathf.Max(Sobel(r, x, y), Mathf.Max(Sobel(g, x, y), Sobel(b, x, y))) * 1.1f) - 0.12f)
                    : 0f;
                float drawn = alpha > 0.4f ? Mathf.Clamp01((around[k] - l[k]) * 6f) : 0f;
                float dark = alpha > 0.5f ? Mathf.Clamp01(1f - l[k] / Mathf.Max(alpha, 0.01f)) : 0f;
                float hatch = 0f;
                if (dark > 0.4f && Mod(x + y, 9) < 1) hatch = 0.45f;   // mid-tones: light and sparse (a loaf, a pale stone still get shape)
                if (dark > 0.62f && Mod(x + y, 6) < 2) hatch = 0.7f;
                if (dark > 0.8f && Mod(x - y, 6) < 2) hatch = 0.85f;
                float wash = alpha * 0.06f;
                float ink = Mathf.Max(Mathf.Max(outline, inner), Mathf.Max(drawn, Mathf.Max(hatch, wash)));
                ink *= 0.75f + 0.5f * Mathf.PerlinNoise(sx * 0.15f + seed, sy * 0.15f - seed);   // the hand wavers
                // near the edges of the picture's frame the ink thins out raggedly: a picture cut off by its frame (a trophy that fills its
                // icon) ends as if the hand stopped there, not in a straight line
                float pxu = (x + 0.5f - ox) / dw, pyv = (y + 0.5f - oy) / dh;
                float toEdge = Mathf.Min(Mathf.Min(pxu, 1f - pxu), Mathf.Min(pyv, 1f - pyv));
                float ragged = 0.02f + 0.09f * Mathf.PerlinNoise(x * 0.09f + seed * 3f, y * 0.09f - seed);
                ink *= Mathf.Clamp01((toEdge - ragged) / 0.07f);
                int i = sy * SW + sx;
                mask[i] = Mathf.Max(mask[i], Mathf.Clamp01(ink));
            }
        }

        /// <summary>
        /// The count as tally marks: four strokes and one across, in rows of up to four bundles; over 20 (too many to count at a glance),
        /// written instead, as "x34".
        /// </summary>
        private static void Tally(float[] mask, int count, float seed)
        {
            // their own strip along the bottom, clear of the torn edge below and the picture above; the marks keep their full size while
            // they fit, and shrink (in as many rows as gives them the most room) when there are many
            float stripLeft = SW * 0.12f, stripRight = SW * 0.88f, stripBottom = SH * 0.07f, stripTop = SH * 0.25f;
            float stripW = stripRight - stripLeft, stripH = stripTop - stripBottom;
            const float tall = 30f, gap = 8f, between = 14f, rowGap = 10f;
            float bundle = 3 * gap;
            int bundles = (count + 4) / 5;
            int bestRows = 1; float scale = 0f;
            for (int rows = 1; rows <= 4; rows++)
            {
                int perRow = (bundles + rows - 1) / rows;
                float width = perRow * bundle + (perRow - 1) * between + 10f, height = rows * tall + (rows - 1) * rowGap;
                float s = Mathf.Min(1f, Mathf.Min(stripW / width, stripH / height));
                if (s > scale + 0.001f) { scale = s; bestRows = rows; }
            }
            int perRowBest = (bundles + bestRows - 1) / bestRows;
            float t = tall * scale, g = gap * scale, bw = bundle * scale, bt = between * scale, rg = rowGap * scale, width2 = 2.6f * Mathf.Max(0.6f, scale);
            float cx = SW / 2f, cy = (stripBottom + stripTop) / 2f;
            var rnd = new System.Random((int)(seed * 100) + count);
            float J() => ((float)rnd.NextDouble() * 3f - 1.5f) * scale;
            for (int i = 0; i < count; i++)
            {
                int b = i / 5, k = i % 5, row = b / perRowBest, col = b % perRowBest;
                int inRow = Math.Min(perRowBest, bundles - row * perRowBest);
                float rowWidth = inRow * bw + (inRow - 1) * bt;
                float left = cx - rowWidth / 2f + col * (bw + bt);
                float bottom = cy - t / 2f + ((bestRows - 1) / 2f - row) * (t + rg);   // (rows stacked about the strip's middle, the first on top)
                if (k < 4)
                {
                    float x = left + k * g;
                    Stroke(mask, x + J(), bottom + J(), x + J(), bottom + t + J(), width2, seed + i);
                }
                else Stroke(mask, left - 5f * scale + J(), bottom + 6f * scale + J(), left + bw + 5f * scale + J(), bottom + t - 6f * scale + J(), width2, seed + i);
            }
        }

        /// <summary>
        /// Capital letters, digits and a small "x" in straight strokes (corners for curves, as cut or scratched), each in a box
        /// <paramref name="tall"/> high, leaning a little. Letters it doesn't know are left as a space.
        /// </summary>
        private static void Letters(float[] mask, string word, int cx, int cy, float tall, float seed)
        {
            float wide = tall * 0.72f, space = tall * 0.24f;
            float total = 0f;
            foreach (char ch in word) total += (ch == 'x' ? wide * 0.7f : ch == 'I' || ch == '1' ? wide * 0.5f : wide) + space;
            total -= space;
            float lx = cx - total / 2f, y0 = cy - tall / 2f;
            for (int n = 0; n < word.Length; n++)
            {
                char ch = word[n];
                float w = ch == 'x' ? wide * 0.7f : ch == 'I' || ch == '1' ? wide * 0.5f : wide;
                float left = lx;
                Vector2 P(float u, float v) => new Vector2(left + u * w + (v - 0.5f) * tall * 0.12f, y0 + v * tall);
                if (Glyphs.TryGetValue(ch, out float[][] lines))
                    foreach (float[] uv in lines)
                        for (int i = 0; i + 3 < uv.Length; i += 2)
                        {
                            Vector2 a = P(uv[i], uv[i + 1]), b = P(uv[i + 2], uv[i + 3]);
                            Stroke(mask, a.x, a.y, b.x, b.y, 3.2f, seed + n * 7 + i);
                        }
                lx += w + space;
            }
        }

        /// <summary>A word in Elder Futhark runes (one rune for each letter of it), each <paramref name="tall"/> high and narrow, cut straight.</summary>
        private static void Runes(float[] mask, string word, int cx, int cy, float tall, float seed)
        {
            float wide = tall * 0.5f, space = tall * 0.28f;
            float total = word.Length * wide + (word.Length - 1) * space;
            float x0 = cx - total / 2f, y0 = cy - tall / 2f;
            for (int n = 0; n < word.Length; n++)
            {
                float left = x0 + n * (wide + space);
                Vector2 P(float u, float v) => new Vector2(left + u * wide, y0 + v * tall);
                if (!RuneGlyphs.TryGetValue(word[n], out float[][] lines)) continue;
                foreach (float[] uv in lines)
                    for (int i = 0; i + 3 < uv.Length; i += 2)
                    {
                        Vector2 a = P(uv[i], uv[i + 1]), b = P(uv[i + 2], uv[i + 3]);
                        Stroke(mask, a.x, a.y, b.x, b.y, 3.2f, seed + n * 11 + i);
                    }
            }
        }

        // The Elder Futhark rune for each letter the headings use: W wunjo, A ansuz, N naudiz, T tiwaz, E ehwaz, D dagaz, P perth,
        // R raido, O othala, F fehu, S sowilo, U uruz, L laguz, Y (as I) isa. Lines as (u, v) points, 0..1 across its box, 0 at the bottom.
        private static readonly Dictionary<char, float[][]> RuneGlyphs = new Dictionary<char, float[][]>
        {
            { 'W', new[] { new[] { 0.2f, 0, 0.2f, 1 }, new[] { 0.2f, 1, 0.85f, 0.75f, 0.2f, 0.5f } } },
            { 'A', new[] { new[] { 0.2f, 0, 0.2f, 1 }, new[] { 0.2f, 1, 0.85f, 0.72f }, new[] { 0.2f, 0.72f, 0.85f, 0.44f } } },
            { 'N', new[] { new[] { 0.5f, 0, 0.5f, 1 }, new[] { 0.1f, 0.68f, 0.9f, 0.38f } } },
            { 'T', new[] { new[] { 0.5f, 0, 0.5f, 1 }, new[] { 0.05f, 0.68f, 0.5f, 1, 0.95f, 0.68f } } },
            { 'E', new[] { new[] { 0.05f, 0, 0.05f, 1, 0.5f, 0.62f, 0.95f, 1, 0.95f, 0 } } },
            { 'D', new[] { new[] { 0f, 0, 0, 1 }, new[] { 1f, 0, 1, 1 }, new[] { 0f, 1, 1, 0 }, new[] { 0f, 0, 1, 1 } } },
            { 'P', new[] { new[] { 0.1f, 0, 0.1f, 1 }, new[] { 0.1f, 1, 0.55f, 0.72f, 0.95f, 1 }, new[] { 0.1f, 0, 0.55f, 0.28f, 0.95f, 0 } } },
            { 'R', new[] { new[] { 0.15f, 0, 0.15f, 1, 0.8f, 0.78f, 0.15f, 0.55f, 0.85f, 0 } } },
            { 'O', new[] { new[] { 0.05f, 0, 0.95f, 0.62f, 0.5f, 1, 0.05f, 0.62f, 0.95f, 0 } } },
            { 'F', new[] { new[] { 0.15f, 0, 0.15f, 1 }, new[] { 0.15f, 0.78f, 0.9f, 1 }, new[] { 0.15f, 0.52f, 0.9f, 0.76f } } },
            { 'S', new[] { new[] { 0.8f, 1, 0.2f, 0.66f, 0.8f, 0.34f, 0.2f, 0 } } },
            { 'U', new[] { new[] { 0.1f, 0, 0.1f, 1, 0.9f, 0.68f, 0.9f, 0 } } },
            { 'L', new[] { new[] { 0.25f, 0, 0.25f, 1, 0.85f, 0.68f } } },
            { 'Y', new[] { new[] { 0.5f, 0, 0.5f, 1 } } },
        };

        // Each glyph: lines as (u, v) points, 0..1 across its box, 0 at the bottom.
        private static readonly Dictionary<char, float[][]> Glyphs = new Dictionary<char, float[][]>
        {
            { 'A', new[] { new[] { 0f, 0, 0.5f, 1, 1, 0 }, new[] { 0.24f, 0.42f, 0.76f, 0.42f } } },
            { 'C', new[] { new[] { 1f, 0.85f, 0.8f, 1, 0.2f, 1, 0, 0.8f, 0, 0.2f, 0.2f, 0, 0.8f, 0, 1, 0.15f } } },
            { 'D', new[] { new[] { 0f, 0, 0, 1, 0.55f, 1, 1, 0.72f, 1, 0.28f, 0.55f, 0, 0, 0 } } },
            { 'E', new[] { new[] { 1f, 1, 0, 1, 0, 0, 1, 0 }, new[] { 0f, 0.5f, 0.7f, 0.5f } } },
            { 'F', new[] { new[] { 1f, 1, 0, 1, 0, 0 }, new[] { 0f, 0.5f, 0.7f, 0.5f } } },
            { 'G', new[] { new[] { 1f, 0.85f, 0.8f, 1, 0.2f, 1, 0, 0.8f, 0, 0.2f, 0.2f, 0, 0.8f, 0, 1, 0.2f, 1, 0.45f, 0.55f, 0.45f } } },
            { 'H', new[] { new[] { 0f, 0, 0, 1 }, new[] { 1f, 0, 1, 1 }, new[] { 0f, 0.5f, 1, 0.5f } } },
            { 'I', new[] { new[] { 0.5f, 0, 0.5f, 1 } } },
            { 'L', new[] { new[] { 0f, 1, 0, 0, 1, 0 } } },
            { 'N', new[] { new[] { 0f, 0, 0, 1, 1, 0, 1, 1 } } },
            { 'O', new[] { new[] { 0.2f, 0, 0, 0.2f, 0, 0.8f, 0.2f, 1, 0.8f, 1, 1, 0.8f, 1, 0.2f, 0.8f, 0, 0.2f, 0 } } },
            { 'P', new[] { new[] { 0f, 0, 0, 1, 0.8f, 1, 1, 0.85f, 1, 0.6f, 0.8f, 0.45f, 0, 0.45f } } },
            { 'R', new[] { new[] { 0f, 0, 0, 1, 0.8f, 1, 1, 0.85f, 1, 0.6f, 0.8f, 0.45f, 0, 0.45f }, new[] { 0.45f, 0.45f, 1, 0 } } },
            { 'S', new[] { new[] { 1f, 0.85f, 0.8f, 1, 0.2f, 1, 0, 0.82f, 0, 0.65f, 0.2f, 0.52f, 0.8f, 0.48f, 1, 0.35f, 1, 0.18f, 0.8f, 0, 0.2f, 0, 0, 0.15f } } },
            { 'T', new[] { new[] { 0f, 1, 1, 1 }, new[] { 0.5f, 1, 0.5f, 0 } } },
            { 'U', new[] { new[] { 0f, 1, 0, 0.2f, 0.2f, 0, 0.8f, 0, 1, 0.2f, 1, 1 } } },
            { 'V', new[] { new[] { 0f, 1, 0.5f, 0, 1, 1 } } },
            { 'W', new[] { new[] { 0f, 1, 0.25f, 0, 0.5f, 0.6f, 0.75f, 0, 1, 1 } } },
            { 'Y', new[] { new[] { 0f, 1, 0.5f, 0.5f, 1, 1 }, new[] { 0.5f, 0.5f, 0.5f, 0 } } },
            { 'x', new[] { new[] { 0f, 0.62f, 1, 0 }, new[] { 0f, 0, 1, 0.62f } } },
            { '0', new[] { new[] { 0.2f, 0, 0, 0.2f, 0, 0.8f, 0.2f, 1, 0.8f, 1, 1, 0.8f, 1, 0.2f, 0.8f, 0, 0.2f, 0 } } },
            { '1', new[] { new[] { 0.1f, 0.8f, 0.6f, 1, 0.6f, 0 } } },
            { '2', new[] { new[] { 0f, 0.8f, 0.2f, 1, 0.8f, 1, 1, 0.8f, 1, 0.6f, 0, 0, 1, 0 } } },
            { '3', new[] { new[] { 0f, 0.85f, 0.2f, 1, 0.8f, 1, 1, 0.82f, 1, 0.62f, 0.75f, 0.5f, 1, 0.38f, 1, 0.18f, 0.8f, 0, 0.2f, 0, 0, 0.15f }, new[] { 0.4f, 0.5f, 0.75f, 0.5f } } },
            { '4', new[] { new[] { 0.8f, 0, 0.8f, 1, 0, 0.35f, 1, 0.35f } } },
            { '5', new[] { new[] { 1f, 1, 0.1f, 1, 0, 0.55f, 0.7f, 0.6f, 1, 0.4f, 1, 0.18f, 0.8f, 0, 0.2f, 0, 0, 0.15f } } },
            { '6', new[] { new[] { 0.9f, 1, 0.3f, 1, 0, 0.6f, 0, 0.2f, 0.2f, 0, 0.8f, 0, 1, 0.2f, 1, 0.4f, 0.8f, 0.55f, 0.2f, 0.55f, 0, 0.4f } } },
            { '7', new[] { new[] { 0f, 1, 1, 1, 0.35f, 0 } } },
            { '8', new[] { new[] { 0.2f, 0.5f, 0, 0.7f, 0, 0.85f, 0.2f, 1, 0.8f, 1, 1, 0.85f, 1, 0.7f, 0.8f, 0.5f, 0.2f, 0.5f, 0, 0.3f, 0, 0.15f, 0.2f, 0, 0.8f, 0, 1, 0.15f, 1, 0.3f, 0.8f, 0.5f } } },
            { '9', new[] { new[] { 0.1f, 0, 0.7f, 0, 1, 0.4f, 1, 0.8f, 0.8f, 1, 0.2f, 1, 0, 0.8f, 0, 0.6f, 0.2f, 0.45f, 0.8f, 0.45f, 1, 0.6f } } },
        };

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
            // The whole picture as the game shows it (sprite.rect), with its stored part (textureRect: trimmed tight round what's drawn,
            // for some sprites) put back where it sits in it (textureRectOffset), transparent round it. Sketching the trimmed part alone
            // stretched or cropped pictures whose drawing doesn't fill them (Weapon Oils' bottles).
            Rect r = sprite.textureRect;
            int tw = Mathf.RoundToInt(r.width), th = Mathf.RoundToInt(r.height);
            w = Mathf.Max(tw, Mathf.RoundToInt(sprite.rect.width));
            h = Mathf.Max(th, Mathf.RoundToInt(sprite.rect.height));
            Texture source = sprite.texture;
            if (source == null || tw <= 0 || th <= 0) return null;
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
            Color[] part = atlas.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), tw, th);
            if (tw == w && th == h) return part;
            var whole = new Color[w * h];   // (transparent)
            int ox = Mathf.Clamp(Mathf.RoundToInt(sprite.textureRectOffset.x), 0, w - tw), oy = Mathf.Clamp(Mathf.RoundToInt(sprite.textureRectOffset.y), 0, h - th);
            for (int y = 0; y < th; y++) Array.Copy(part, y * tw, whole, (y + oy) * w + ox, tw);
            return whole;
        }
    }
}
