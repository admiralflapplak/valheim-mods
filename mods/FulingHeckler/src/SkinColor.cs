using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Recolours the Fuling's body (not his gear). Valheim's creature shader supports hue / saturation / brightness
    /// shifts (the game uses them for its starred creatures), so we set those. If his shader doesn't have them,
    /// we fall back to a plain colour tint.
    /// </summary>
    internal class SkinColor
    {
        private readonly List<Renderer> _body;
        private readonly bool _hsv;
        private readonly Dictionary<Renderer, Color> _originalTint = new Dictionary<Renderer, Color>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private string _applied;
        private static bool _logged;

        /// <param name="bodyRenderers">His renderers captured before any gear was added.</param>
        public SkinColor(IEnumerable<Renderer> bodyRenderers)
        {
            _body = bodyRenderers.Where(r => r != null).ToList();
            _hsv = _body.Any(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Hue"));
            foreach (var r in _body)
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color")) _originalTint[r] = r.sharedMaterial.GetColor("_Color");

            if (!_logged)
            {
                _logged = true;
                var shaders = _body.Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null)
                                   .Select(r => r.sharedMaterial.shader.name).Distinct().ToArray();
                Plugin.Log.LogInfo($"Skin colour: {(_hsv ? "hue/saturation/brightness shift" : "tint fallback (shader has no hue control)")}, " +
                                   $"body shader(s): {string.Join(", ", shaders)}.");
            }
        }

        /// <summary>Applies the current settings; cheap to call every frame (only does work when they change).</summary>
        public void Apply()
        {
            bool on = Look.Recolor;
            float h = Look.Hue, s = Look.Saturation, v = Look.Brightness;
            string key = on ? $"{h:0.###}|{s:0.###}|{v:0.###}" : "off";
            if (key == _applied) return;
            _applied = key;

            foreach (var r in _body)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                if (_hsv)
                {
                    _block.SetFloat("_Hue", on ? h : 0f);
                    _block.SetFloat("_Saturation", on ? s : 0f);
                    _block.SetFloat("_Value", on ? v : 0f);
                }
                else if (_originalTint.TryGetValue(r, out Color baseColor))
                {
                    Color tint = on ? Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(Mathf.Abs(s)), Mathf.Clamp01(1f + v)) : Color.white;
                    _block.SetColor("_Color", on ? baseColor * tint : baseColor);
                }
                r.SetPropertyBlock(_block);
            }
        }
    }
}
