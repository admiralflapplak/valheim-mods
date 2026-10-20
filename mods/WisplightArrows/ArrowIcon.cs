using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WisplightArrows
{
    /// <summary>
    /// The inventory icon: the game's own silver arrow icon (hand-painted, like every arrow beside it in the bag, and with the white
    /// fletching the model has), with a wisp in a glass
    /// orb painted over its tip and a soft blue haze round it, as the poison arrow's icon has its green cloud. The game's icons live in a
    /// texture the code can't read, so the silver arrow's is copied out through the graphics card first. When that fails, the icon drawn
    /// from the model (Icon.MakeItem) is used instead, with the haze only.
    /// </summary>
    internal static class ArrowIcon
    {
        private const float Shrink = 0.9f;       // the arrow drawn this much smaller, kept to the left, so the haze has room on the right
        private const float OrbRadius = 0.075f;  // parts of the icon's width
        private const float HeadLength = 0.18f;  // how much of the base icon's right end is the arrowhead (parts of its width)
        private const float ShaftHalf = 0.022f;  // half the shaft's thickness (parts of the height): what is kept of the head
        private const float CorePart = 0.45f;    // the wisp inside, as a part of the orb
        private const float HazeRadius = 0.24f, HazeStrength = 0.85f;
        private const float EdgeFade = 0.08f;    // the haze fades out this close to the picture's edges (no hard line where it's cut off)

        private static readonly Color GlassTint = new Color(0.62f, 0.8f, 0.95f);
        private static readonly Color CoreColour = new Color(0.55f, 0.95f, 1f);
        private static readonly Color HazeColour = new Color(0.45f, 0.75f, 1f);

        private static readonly string[] Bases = { "ArrowSilver", "ArrowFlint", "ArrowWood" };   // the first the game has

        /// <summary>The icon from the game's silver arrow (null if it can't be copied).</summary>
        public static Sprite FromGame(ZNetScene scene)
        {
            Sprite source = null;
            foreach (string name in Bases)
            {
                GameObject arrow = scene.GetPrefab(name);
                source = arrow != null ? arrow.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
                if (source != null) break;
            }
            Texture2D tex = Copy(source);
            if (tex == null) return null;
            Paint(tex, orb: true);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>The haze (no painted orb: it is in the picture already) on an icon drawn from the model.</summary>
        public static void AddHaze(Sprite drawn)
        {
            Texture2D tex = drawn != null ? drawn.texture : null;
            if (tex != null && tex.isReadable) Paint(tex, orb: false);
        }

        /// <summary>The sprite's part of its texture, as a texture of its own that can be read and painted.</summary>
        private static Texture2D Copy(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;   // (a dedicated server draws nothing)
            Rect r = sprite.textureRect;
            RenderTexture rt = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture was = RenderTexture.active;
            try
            {
                Graphics.Blit(sprite.texture, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(r.x, r.y, tex.width, tex.height), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo("Could not copy the arrow icon (" + e.Message + "): the icon is drawn from the model");
                return null;
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Paint(Texture2D tex, bool orb)
        {
            int w = tex.width, h = tex.height;

            // smaller, kept to the left and the middle
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / (w * Shrink), v = 0.5f + ((y + 0.5f) / h - 0.5f) / Shrink;
                    px[y * w + x] = u > 1f || v < 0f || v > 1f ? Color.clear : tex.GetPixelBilinear(u, v);
                }

            // the tip: the right-most thing drawn, at the middle of what is drawn near it
            int right = -1;
            for (int x = w - 1; x >= 0 && right < 0; x--)
                for (int y = 0; y < h; y++)
                    if (px[y * w + x].a > 0.3f) { right = x; break; }
            if (right < 0) return;
            int from = right - Mathf.RoundToInt(w * (orb ? 0.05f : 0.14f));
            float cx = 0f, cy = 0f;
            int n = 0;
            for (int y = 0; y < h; y++)
                for (int x = Mathf.Max(0, from); x <= right; x++)
                    if (px[y * w + x].a > 0.3f) { cx += x; cy += y; n++; }
            if (n == 0) return;
            cx /= n;
            cy /= n;
            float orbR = w * OrbRadius;
            if (orb)
            {
                // the arrowhead's barbs (and their shadow) off: only the shaft's own line runs on into the orb
                int headFrom = right - Mathf.RoundToInt(w * HeadLength);
                float band = h * ShaftHalf;
                for (int y = 0; y < h; y++)
                    if (Mathf.Abs(y - cy) > band)
                        for (int x = Mathf.Max(0, headFrom); x <= right; x++) px[y * w + x] = Color.clear;
                cx = right - orbR * 0.6f;   // (the orb over the arrowhead, its point inside the glass)
            }

            float hazeR = w * HazeRadius, fade = w * EdgeFade;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy, dist = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = px[y * w + x];

                    // behind: the haze
                    if (dist < hazeR)
                    {
                        float d = dist / hazeR;
                        float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y)) / fade);
                        c = Under(c, HazeColour, HazeStrength * (1f - d) * (1f - d) * edge * edge);
                    }

                    // in front: the orb
                    if (orb && dist < orbR + 1f)
                    {
                        float d = dist / orbR;
                        float cover = Mathf.Clamp01(orbR + 0.5f - dist);                // (a soft one-pixel rim)
                        float glassA = Mathf.Lerp(0.35f, 0.85f, d * d * d) * cover;     // clear in the middle, denser at the rim
                        c = Over(c, GlassTint, glassA);
                        float core = Mathf.Clamp01(1f - d / CorePart);
                        c = Over(c, CoreColour, Mathf.Clamp01(core * 2.2f));
                        c = Over(c, Color.white, Mathf.Clamp01(core * core * 1.5f));    // its bright heart
                        float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-0.38f, 0.42f) * orbR) / (orbR * 0.28f));
                        c = Over(c, Color.white, shine * shine * 0.9f);                 // a glint on the glass, top left
                    }
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
        }

        /// <summary>colour painted on top with alpha a.</summary>
        private static Color Over(Color under, Color colour, float a)
        {
            if (a <= 0f) return under;
            float outA = a + under.a * (1f - a);
            if (outA <= 0f) return Color.clear;
            Color rgb = (colour * a + under * under.a * (1f - a)) / outA;
            return new Color(rgb.r, rgb.g, rgb.b, outA);
        }

        /// <summary>colour painted behind with alpha a (it shows only where the picture is see-through).</summary>
        private static Color Under(Color over, Color colour, float a)
        {
            if (a <= 0f) return over;
            float outA = over.a + a * (1f - over.a);
            if (outA <= 0f) return Color.clear;
            Color rgb = (over * over.a + colour * a * (1f - over.a)) / outA;
            return new Color(rgb.r, rgb.g, rgb.b, outA);
        }
    }
}
