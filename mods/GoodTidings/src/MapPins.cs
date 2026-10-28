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
        private static Sprite _blueArea;
        private Minimap.PinData _area, _icon;
        private float _moved;

        /// <summary>The visiting area's radius on the map, in metres (a raid's is about 100; she keeps nearer).</summary>
        private const float AreaRadius = 40f;

        internal void Update(Vector3 home, Vector3 at, string name)
        {
            Minimap map = Minimap.instance;
            if (map == null) return;
            if (_area == null)
            {
                _area = map.AddPin(home, Minimap.PinType.EventArea, "", false, false);
                _area.m_worldSize = AreaRadius * 2f;
                Sprite blue = BlueArea(_area.m_icon);
                if (blue) _area.m_icon = blue;
            }
            if (_icon == null)
            {
                _icon = map.AddPin(at, Minimap.PinType.RandomEvent, name, false, false);
                Sprite trader = HaldorIcon(map);
                if (trader) _icon.m_icon = trader;
            }
            if (Time.time - _moved < 0.5f) return;
            _moved = Time.time;
            _area.m_pos = home;
            _icon.m_pos = at;
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

        /// <summary>Haldor's icon on the map (his location's), if the game still has it.</summary>
        private static Sprite HaldorIcon(Minimap map)
        {
            foreach (Minimap.LocationSpriteData l in map.m_locationIcons)
                if (l.m_name == "Vendor_BlackForest") return l.m_icon;
            return null;
        }

        /// <summary>The event-area circle, its red turned blue (made once).</summary>
        private static Sprite BlueArea(Sprite red)
        {
            if (_blueArea) return _blueArea;
            if (red == null) return null;
            Texture2D whole = Cuts.Readable(red.texture);
            Rect r = red.textureRect;
            int x = (int)r.x, y = (int)r.y, w = (int)r.width, h = (int)r.height;
            Color[] px = whole.GetPixels(x, y, w, h);
            Object.Destroy(whole);
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float _, out float s, out float v);
                Color c = Color.HSVToRGB(0.58f, s, v);
                c.a = px[i].a;
                px[i] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            _blueArea = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), red.pixelsPerUnit);
            _blueArea.name = "goodtidings_area";
            return _blueArea;
        }
    }
}
