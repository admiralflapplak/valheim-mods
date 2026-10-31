using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Parts of an outfit taken away through its texture: the outfits' material cuts out see-through pixels (that is how their frayed
    /// edges are made), so making a part's pixels see-through on our own copy of the texture removes that part (the cape), though the
    /// mesh itself is locked. For finding the parts, a grid of coloured squares can be painted over the copy.
    /// </summary>
    internal static class Cuts
    {
        /// <summary>Squares to cut, by outfit, in texture pixels from the bottom left.</summary>
        internal static readonly Dictionary<string, List<RectInt>> BySuit = new Dictionary<string, List<RectInt>>();
        internal static int Grid;   // squares across the grid (0: none)

        private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();

        internal static readonly Color[] GridColours =
        {
            Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.black,
            new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f), new Color(1f, 0.6f, 0.8f), new Color(0.45f, 0.25f, 0.1f),
            Color.grey, new Color(0.6f, 1f, 0.2f), new Color(0f, 0f, 0.4f), new Color(0f, 0.5f, 0.5f),
        };

        /// <summary>
        /// A new colour for an outfit, painted on our texture copy: every pixel takes <see cref="Hue"/> (0-1), its colour strength becomes
        /// at least <see cref="Saturation"/> (the cloth is greyish, so a hue alone hardly shows), its brightness is multiplied by
        /// <see cref="Value"/>. Pixels in <see cref="Keep"/> stay as they are (the belt buckle).
        /// </summary>
        internal sealed class Tint
        {
            public float Hue, Saturation, Value = 1f;
            /// <summary>No colour at all (a black hood): grey, its brightness multiplied by <see cref="Value"/>.</summary>
            public bool Grey;
            public List<RectInt> Keep = new List<RectInt>();
            /// <summary>Parts made near black (the cape, kept black under recoloured clothes).</summary>
            public List<RectInt> Black = new List<RectInt>();
            /// <summary>Parts cut away (the cape, for the version without it).</summary>
            public List<RectInt> Cut = new List<RectInt>();
            public override string ToString() =>
                $"{Hue:0.00},{Saturation:0.00},{Value:0.00},{Grey},{string.Join("/", Keep)},{string.Join("/", Black)},{string.Join("/", Cut)}";
        }

        internal static Material MaterialFor(Material source, string suit, Tint tint = null)
        {
            BySuit.TryGetValue(suit, out List<RectInt> cuts);
            if (Grid == 0 && (cuts == null || cuts.Count == 0) && tint == null) return source;
            string key = suit + "|" + Grid + "|" + string.Join(";", cuts ?? new List<RectInt>()) + "|" + tint;
            if (Made.TryGetValue(key, out Material m) && m) return m;

            Texture2D tex = Readable(source.mainTexture);
            if (tex == null) return source;
            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            if (tint != null)
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var at = new Vector2Int(x, y);
                    Color32 c = px[y * w + x];
                    if (tint.Cut.Exists(r => r.Contains(at))) { px[y * w + x].a = 0; continue; }
                    if (tint.Keep.Exists(r => r.Contains(at))) continue;
                    Color.RGBToHSV(c, out float _, out float sat, out float val);
                    Color n = tint.Black.Exists(r => r.Contains(at)) ? Color.HSVToRGB(0f, 0f, val * 0.3f)
                        : tint.Grey ? Color.HSVToRGB(0f, 0f, Mathf.Clamp01(val * tint.Value))
                        : Color.HSVToRGB(tint.Hue, Mathf.Max(sat, tint.Saturation), Mathf.Clamp01(val * tint.Value));
                    px[y * w + x] = new Color32((byte)(n.r * 255), (byte)(n.g * 255), (byte)(n.b * 255), c.a);
                }
            if (Grid > 0)
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cell = (y * Grid / h) * Grid + x * Grid / w;
                    Color c = Color.Lerp(px[y * w + x], GridColours[cell % GridColours.Length], 0.75f);
                    px[y * w + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), px[y * w + x].a);
                }
            if (cuts != null)
                foreach (RectInt r in cuts)
                    for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(h, r.yMax); y++)
                    for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(w, r.xMax); x++)
                        px[y * w + x].a = 0;
            tex.SetPixels32(px);
            tex.Apply();
            m = new Material(source) { name = source.name + "_goodtidings", mainTexture = tex };
            return Made[key] = m;
        }

        internal static void Clear()
        {
            foreach (Material m in Made.Values)
                if (m) { Object.Destroy(m.mainTexture); Object.Destroy(m); }
            Made.Clear();
        }

        /// <summary>A copy of a texture we can read and change (the game's own are locked: drawn through the GPU).</summary>
        internal static Texture2D Readable(Texture source)
        {
            if (source == null) return null;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { filterMode = source.filterMode, wrapMode = source.wrapMode };
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
