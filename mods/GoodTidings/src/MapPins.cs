using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Dagny on each player's map, as a raid is shown but friendly: the game's event-area circle in blue over the base she visits, and
    /// Haldor's trader icon (half the size his own is shown at) moving with her, her name beside it on the big map. Not saved: made when
    /// she's loaded, moved twice a second, removed when she goes. The game resets every pin's colour each frame, so the blue is a recoloured
    /// copy of the circle's sprite, not a tint.
    /// </summary>
    internal sealed class MapPins
    {
        private static readonly System.Collections.Generic.Dictionary<float, Sprite> Areas = new System.Collections.Generic.Dictionary<float, Sprite>();

        /// <summary>The circle's colour, as a hue: blue for a good visit, yellow for a neutral one (red is the game's, for raids).</summary>
        internal const float Good = 0.58f, Neutral = 0.14f;
        private Minimap.PinData _area, _icon;
        private float _moved;

        /// <summary>A visit's range, as a raid's (every raid in the game has 96 m): its circle drawn as the game draws a raid's, at 0.9 of it.</summary>
        internal const float Range = 96f;
        private const float AreaRadius = Range;

        /// <summary>
        /// The circle on <paramref name="home"/> (in <paramref name="circle"/>'s colour), and (with <paramref name="withIcon"/>) an icon at
        /// <paramref name="at"/>: <paramref name="icon"/>, or Haldor's trader icon.
        /// </summary>
        internal void Update(Vector3 home, Vector3 at, string name, bool withIcon = true, float radius = AreaRadius, Sprite icon = null, float circle = Good)
        {
            Minimap map = Minimap.instance;
            if (map == null) return;
            if (_area == null)
            {
                _area = map.AddPin(home, Minimap.PinType.EventArea, "", false, false);
                _area.m_worldSize = radius * 2f * 0.9f;   // (as Minimap.UpdateEventPin sizes a raid's)
                Sprite coloured = Area(_area.m_icon, circle);
                if (coloured) _area.m_icon = coloured;
            }
            if (_icon == null && withIcon)
            {
                _icon = map.AddPin(at, Minimap.PinType.RandomEvent, name, false, false);
                Sprite shown = icon ? icon : HaldorIcon(map);
                if (shown) _icon.m_icon = shown;
            }
            if (Time.time - _moved < 0.5f) return;
            _moved = Time.time;
            _area.m_pos = home;
            if (_icon != null) _icon.m_pos = at;
        }

        internal void Remove()
        {
            Minimap map = Minimap.instance;
            if (map != null)
            {
                if (_area != null) map.RemovePin(_area);
                if (_icon != null) map.RemovePin(_icon);
            }
            _area = _icon = null;
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Minimap, System.Collections.Generic.List<Minimap.PinData>> Pins =
            HarmonyLib.AccessTools.FieldRefAccess<Minimap, System.Collections.Generic.List<Minimap.PinData>>("m_pins");

        /// <summary>
        /// Takes every marker of ours off the map (on loading and unloading the mod: a copy unloaded mid-visit never removed its own):
        /// unsaved ones with our blue circle, or Haldor's icon as an event marker (the game shows his only at his camp).
        /// </summary>
        internal static void CleanUp()
        {
            Minimap map = Minimap.instance;
            if (map == null) return;
            Sprite trader = HaldorIcon(map);
            foreach (Minimap.PinData pin in Pins(map).ToArray())
                if (!pin.m_save && pin.m_icon && (pin.m_icon.name.StartsWith("goodtidings_")
                        || pin.m_type == Minimap.PinType.RandomEvent && (pin.m_icon == trader || pin.m_icon == Mystery())))
                    map.RemovePin(pin);
        }

        private static Sprite _mystery;

        /// <summary>The game's "?" map icon (Hildir marks her treasure places with it), found once among its loaded sprites.</summary>
        internal static Sprite Mystery()
        {
            if (_mystery) return _mystery;
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s.name == "mapicon_hildir1") return _mystery = s;
            return null;
        }

        /// <summary>Haldor's icon on the map (his location's), if the game still has it.</summary>
        private static Sprite HaldorIcon(Minimap map)
        {
            foreach (Minimap.LocationSpriteData l in map.m_locationIcons)
                if (l.m_name == "Vendor_BlackForest") return l.m_icon;
            return null;
        }

        /// <summary>The event-area circle, its red turned to <paramref name="hue"/> (made once for each).</summary>
        private static Sprite Area(Sprite red, float hue)
        {
            if (Areas.TryGetValue(hue, out Sprite made) && made) return made;
            if (red == null) return null;
            Texture2D whole = Cuts.Readable(red.texture);
            Rect r = red.textureRect;
            int x = (int)r.x, y = (int)r.y, w = (int)r.width, h = (int)r.height;
            Color[] px = whole.GetPixels(x, y, w, h);
            Object.Destroy(whole);
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float _, out float s, out float v);
                Color c = Color.HSVToRGB(hue, s, v);
                c.a = px[i].a;
                px[i] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            made = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), red.pixelsPerUnit);
            made.name = "goodtidings_area";
            return Areas[hue] = made;
        }
    }
}
