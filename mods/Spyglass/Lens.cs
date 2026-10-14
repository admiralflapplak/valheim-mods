using UnityEngine;
using UnityEngine.UI;

namespace Spyglass
{
    /// <summary>
    /// What you see through the lens: the world inside a circle with black all round it, darkening a little towards the rim; and, as the
    /// spyglass wears, the glass itself showing it: dust and scratches first, then a crack spreading from where it was struck, then a
    /// cloud of condensation and smudges. Drawn on a canvas of its own (built when first needed, torn down on unload) from textures made
    /// in code, so no files and nothing to install. It fades with how far the spyglass is raised.
    /// </summary>
    internal static class Lens
    {
        private const string CanvasName = "nick_spyglass_lens";
        private const float CircleSize = 0.96f;     // the circle's diameter, as a part of the screen's height
        private const int CrackSteps = 16;          // how many times the crack picture is drawn again as the wear grows
        private const int DustSteps = 32;           // and the dust picture (each speck and scratch comes in and strengthens by itself)

        private static GameObject _root;
        private static CanvasGroup _group;
        private static RectTransform _circle, _crack, _haze, _dust, _left, _right, _top, _bottom;
        private static GameObject _crackObject, _circleObject, _hazeObject, _dustObject;
        private static Texture2D _ringTexture, _crackTexture, _hazeTexture, _dustTexture;
        private static RawImage _hazeImage, _dustImage, _crackImage;
        private static int _crackLevel = -1, _dustLevel = -1;
        private static int _width, _height;

        /// <summary>
        /// Each frame: how much of the lens shows (0 not at all, 1 fully), which parts, and how worn the spyglass is (0 new, 1 broken): a
        /// new one has a clear lens, and as it wears the glass collects dust and scratches, cracks from where it was hit, and clouds over.
        /// </summary>
        public static void Show(float amount, bool vignette, bool wearShows, float wear)
        {
            bool worn = wearShows && wear > 0.02f;
            if (amount <= 0.001f || !vignette && !worn)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return;
            }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;   // a dedicated server draws nothing
            if (_root == null && !Build()) return;
            if (!_root.activeSelf) _root.SetActive(true);
            _group.alpha = amount;
            _hazeObject.SetActive(worn);
            _dustObject.SetActive(worn);
            _crackObject.SetActive(worn && wear > 0.06f);
            _circleObject.SetActive(vignette);
            _left.gameObject.SetActive(vignette);
            _right.gameObject.SetActive(vignette);
            _top.gameObject.SetActive(vignette);
            _bottom.gameObject.SetActive(vignette);
            if (worn)
            {
                // The haze comes up through the middle of the wear (hardly there at a third, clearly there at half, heavy by three quarters).
                _hazeImage.color = new Color(0.86f, 0.87f, 0.85f, 0.85f * Smooth(0.15f, 0.85f, wear));
                int dustLevel = Mathf.RoundToInt(Mathf.Clamp01(wear) * DustSteps);   // the dust gathers a speck at a time, so it is drawn again as it does
                if (dustLevel != _dustLevel)
                {
                    if (_dustTexture != null) Object.Destroy(_dustTexture);
                    _dustTexture = MakeDust(512, dustLevel / (float)DustSteps);
                    _dustImage.texture = _dustTexture;
                    _dustLevel = dustLevel;
                }
                int level = Mathf.RoundToInt(Mathf.Clamp01(wear) * CrackSteps);   // the crack is drawn again only when it has grown a step
                if (level != _crackLevel && level >= 1)
                {
                    if (_crackTexture != null) Object.Destroy(_crackTexture);
                    _crackTexture = MakeCrack(1024, level / (float)CrackSteps);
                    _crackImage.texture = _crackTexture;
                    _crackLevel = level;
                }
            }
            if (_width != Screen.width || _height != Screen.height) Layout();
        }

        public static void Reset()
        {
            if (_root != null) Object.Destroy(_root);
            if (_ringTexture != null) Object.Destroy(_ringTexture);
            if (_crackTexture != null) Object.Destroy(_crackTexture);
            if (_hazeTexture != null) Object.Destroy(_hazeTexture);
            if (_dustTexture != null) Object.Destroy(_dustTexture);
            _root = null; _group = null; _ringTexture = null; _crackTexture = null; _hazeTexture = null; _dustTexture = null;
            _circle = _crack = _haze = _dust = _left = _right = _top = _bottom = null;
            _crackObject = _circleObject = _hazeObject = _dustObject = null;
            _hazeImage = _dustImage = _crackImage = null;
            _crackLevel = -1;
            _dustLevel = -1;
        }

        // ---- building ---------------------------------------------------------------------------------------------

        private static bool Build()
        {
            try
            {
                // (an old copy's canvas, left by a hot reload)
                foreach (Canvas old in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (old != null && old.gameObject.name == CanvasName) Object.Destroy(old.gameObject);

                _root = new GameObject(CanvasName);
                Object.DontDestroyOnLoad(_root);
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -100;   // under the game's own windows and messages
                _group = _root.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;

                _ringTexture = MakeRing(512);
                _hazeTexture = MakeHaze(256);
                _dustTexture = MakeDust(512, 0f);
                _dustLevel = 0;
                _crackTexture = MakeCrack(1024, 1f / CrackSteps);
                _crackLevel = 1;

                // Under the black ring, from the glass's own marks to the crack on top of them.
                _haze = Part("haze", _hazeTexture, new Color(1f, 1f, 1f, 0f), out _hazeObject);
                _hazeImage = _haze.GetComponent<RawImage>();
                _dust = Part("dust", _dustTexture, Color.white, out _dustObject);
                _dustImage = _dust.GetComponent<RawImage>();
                _crack = Part("crack", _crackTexture, Color.white, out _crackObject);
                _crackImage = _crack.GetComponent<RawImage>();
                _circle = Part("circle", _ringTexture, Color.white, out _circleObject);
                _left = Part("left", Texture2D.whiteTexture, Color.black, out _);
                _right = Part("right", Texture2D.whiteTexture, Color.black, out _);
                _top = Part("top", Texture2D.whiteTexture, Color.black, out _);
                _bottom = Part("bottom", Texture2D.whiteTexture, Color.black, out _);
                Layout();
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not build the lens overlay: " + e);
                Reset();
                return false;
            }
        }

        private static RectTransform Part(string name, Texture texture, Color color, out GameObject go)
        {
            go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var image = go.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return rect;
        }

        /// <summary>The circle in the middle of the screen, with plain black filling what is left of the sides.</summary>
        private static void Layout()
        {
            _width = Screen.width;
            _height = Screen.height;
            float w = _width, h = _height, s = h * CircleSize;
            Place(_circle, 0f, 0f, s, s);
            Place(_crack, 0f, 0f, s, s);
            Place(_haze, 0f, 0f, s, s);
            Place(_dust, 0f, 0f, s, s);
            float side = Mathf.Max(0f, (w - s) * 0.5f) + 2f;
            Place(_left, -(w + s) * 0.25f, 0f, side, h + 4f);
            Place(_right, (w + s) * 0.25f, 0f, side, h + 4f);
            float edge = Mathf.Max(0f, (h - s) * 0.5f) + 2f;
            Place(_top, 0f, (h + s) * 0.25f, s + 4f, edge);
            Place(_bottom, 0f, -(h + s) * 0.25f, s + 4f, edge);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        // ---- the pictures -----------------------------------------------------------------------------------------

        /// <summary>Clear in the middle, a little dark towards the rim, then a soft edge to black that is complete before the picture's own edge.</summary>
        private static Texture2D MakeRing(int size)
        {
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;   // 1 at the picture's edge
                    float shade = 0.45f * Smooth(0.55f, 0.93f, r);
                    float edge = Smooth(0.93f, 0.985f, r);
                    float a = Mathf.Clamp01(Mathf.Max(shade, edge));
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return Finish(pixels, size);
        }

        /// <summary>0 below edge0, 1 above edge1, a smooth curve between (not Mathf.SmoothStep, which blends two values by a 0 to 1 amount).</summary>
        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// A crack in toughened glass, as worn as the spyglass is (0 none, 1 spread right across). It starts at the point it was struck: a
        /// spot of crushed glass with shards, and lines running out of it, thick at the start and finer towards their ends, fading as they
        /// go, mostly straight with the odd kink and splitting at sharp angles; later, rings of cracks joining them. Each line has its own
        /// wear at which it starts and grows until it has reached its length, so the crack creeps outward. A pale line over a dark one a
        /// pixel away, so it reads on bright sky and on dark ground. The same crack every time, at any wear.
        /// </summary>
        private static Texture2D MakeCrack(int size, float wear)
        {
            var pixels = new Color32[size * size];
            var random = new System.Random(7);
            float Next(float from, float to) => from + (float)random.NextDouble() * (to - from);

            var impact = new Vector2(0.69f * size, 0.30f * size);
            const int Rays = 9;
            const int Steps = 8;
            var points = new Vector2[Rays, Steps + 1];
            for (int i = 0; i < Rays; i++)
            {
                // (every random number is drawn whatever the wear, so a crack is the same shape at every stage)
                float birth = 0.05f + 0.5f * ((i * 4) % Rays) / (Rays - 1f);                    // the wear at which this line starts
                int drawn = Mathf.CeilToInt(Steps * Mathf.Clamp01((wear - birth) / 0.3f));    // how many of its pieces it has reached
                float angle = i * Mathf.PI * 2f / Rays + Next(-0.25f, 0.25f);
                float length = size * Next(0.18f, 0.48f);
                Vector2 p = impact;
                points[i, 0] = p;
                for (int s = 1; s <= Steps; s++)
                {
                    float kink = random.NextDouble() < 0.22 ? Next(-0.4f, 0.4f) : 0f;
                    angle += Next(-0.07f, 0.07f) + kink;
                    float step = length / Steps * (s < 3 ? 0.75f : 1f);
                    Vector2 next = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * step;
                    float along = s / (float)Steps;
                    if (s <= drawn) Line(pixels, size, p, next, Mathf.Lerp(2.8f, 0.8f, along), Mathf.Lerp(1f, 0.4f, along));
                    points[i, s] = next;
                    p = next;
                    bool branches = s >= 2 && s < Steps && random.NextDouble() < 0.4;
                    if (branches)
                    {
                        float branch = angle + (random.NextDouble() < 0.5 ? -1f : 1f) * Next(0.7f, 1.2f);
                        Vector2 q = p;
                        int pieces = random.Next(2, 4);
                        for (int b = 0; b < pieces; b++)
                        {
                            branch += Next(-0.12f, 0.12f);
                            Vector2 end = q + new Vector2(Mathf.Cos(branch), Mathf.Sin(branch)) * (step * 0.8f);
                            if (s < drawn || s == drawn && wear >= birth + 0.3f)   // (branches come once the line has passed them)
                                Line(pixels, size, q, end, Mathf.Lerp(1.4f, 0.7f, (b + 1f) / pieces), Mathf.Lerp(0.8f, 0.3f, (b + 1f) / pieces));
                            q = end;
                        }
                    }
                }
            }
            for (int ring = 2; ring <= 5; ring += 3)   // two rings joining neighbouring cracks, with gaps, once the cracks have spread that far
                for (int i = 0; i < Rays; i++)
                    if (random.NextDouble() < 0.6 && wear >= (ring == 2 ? 0.55f : 0.88f))
                        Line(pixels, size, points[i, ring], points[(i + 1) % Rays, ring], 1f, 0.55f);

            Impact(pixels, size, impact, wear);
            Round(pixels, size);
            return Finish(pixels, size);
        }

        /// <summary>The point the glass was struck: crushed glass, a bright spot with a ragged edge and a few small shards round it,
        /// growing a little with the wear.</summary>
        private static void Impact(Color32[] pixels, int size, Vector2 at, float wear)
        {
            var random = new System.Random(13);   // (its own, so it never changes the crack's shape)
            float radius = 4f + 9f * Mathf.Clamp01(wear);
            int shards = 9;
            for (int i = 0; i < shards; i++)
            {
                float angle = i * Mathf.PI * 2f / shards + (float)random.NextDouble() * 0.5f;
                float length = radius * (1.4f + (float)random.NextDouble() * 1.8f);
                var a = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * 0.5f;
                var b = at + new Vector2(Mathf.Cos(angle + 0.18f), Mathf.Sin(angle + 0.18f)) * length;
                Line(pixels, size, a, b, 1.6f, 0.9f);
            }
            Dot(pixels, size, at + new Vector2(1.5f, -1.5f), radius * 1.05f, new Color32(10, 12, 16, 150));
            Dot(pixels, size, at, radius, new Color32(245, 248, 250, 235));
        }

        /// <summary>
        /// Dust and scratches on the glass, as worn as the spyglass is (0 none, 1 a lot). They gather over time: each speck and scratch has
        /// a wear at which it first shows and then strengthens over a stretch of wear, so the glass slowly collects them one by one. Dust
        /// is uneven, not sprinkled evenly: it gathers in drifts (more in some places than others, more towards the rim), the bigger specks
        /// fainter and softer, and the scratches differ in length, strength and curve. Every random number is drawn whatever the wear, so
        /// each is in the same place at every stage. Round, like the cloud.
        /// </summary>
        private static Texture2D MakeDust(int size, float wear)
        {
            var pixels = new Color32[size * size];
            var random = new System.Random(11);
            float Next(float from, float to) => from + (float)random.NextDouble() * (to - from);
            float half = size * 0.5f;

            // a place on the glass, more likely in a drift of dust and towards the rim
            Vector2 Spot()
            {
                for (; ; )
                {
                    var p = new Vector2(Next(0f, size), Next(0f, size));
                    float u = p.x / size, v = p.y / size;
                    float r = Vector2.Distance(p, new Vector2(half, half)) / half;
                    if (r > 0.92f) continue;
                    float drift = Smooth(0.35f, 0.75f, Mathf.PerlinNoise(u * 3f + 7f, v * 3f + 7f)) * (0.55f + 0.45f * Smooth(0.2f, 0.95f, r));
                    if (random.NextDouble() < 0.06 + 0.94 * drift) return p;
                }
            }
            // how much of it there is by now: 0 before its wear, rising to 1 over the next stretch
            float Arrived(float birth) => Mathf.Clamp01((wear - birth) / 0.14f);

            for (int i = 0; i < 300; i++)
            {
                Vector2 at = Spot();
                float radius = 0.7f + Mathf.Pow(Next(0f, 1f), 3f) * 3.2f;
                float strength = Next(0.35f, 1f) * Mathf.Lerp(1f, 0.45f, radius / 3.9f);   // the bigger, the fainter
                float birth = 0.02f + 0.85f * Mathf.Pow(Next(0f, 1f), 1.4f);               // most of them come in the first half
                float arrived = Arrived(birth);
                if (arrived > 0f) Dot(pixels, size, at, radius, new Color32(255, 255, 255, (byte)Mathf.RoundToInt(215f * strength * arrived)));
            }
            for (int i = 0; i < 14; i++)
            {
                Vector2 p = Spot();
                float angle = Next(0f, Mathf.PI * 2f);
                int pieces = random.Next(3, 8);
                float strength = Next(0.3f, 1f);
                float width = Next(0.6f, 1.2f);
                float curve = Next(-0.2f, 0.2f);
                float birth = 0.1f + 0.75f * Next(0f, 1f);
                float arrived = Arrived(birth);
                for (int s = 0; s < pieces; s++)
                {
                    var next = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Next(7f, 18f);
                    if (arrived > 0f) Stroke(pixels, size, p, next, width, new Color32(255, 255, 255, (byte)Mathf.RoundToInt(130f * strength * arrived)));
                    p = next;
                    angle += curve + Next(-0.08f, 0.08f);
                }
            }
            Round(pixels, size);
            return Finish(pixels, size);
        }

        /// <summary>
        /// Condensation and smudges: a veil that gathers at the rim first and creeps inwards, wiped streaks across it all one way (noise
        /// stretched along a slant), and cloudy patches with clear glass between; white-grey, with a varying see-through-ness; tinted and
        /// faded in with the wear, and round.
        /// </summary>
        private static Texture2D MakeHaze(int size)
        {
            var pixels = new Color32[size * size];
            const float cos = 0.883f, sin = 0.469f;   // the slant of the wiping, about 28 degrees
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float along = u * cos + v * sin, across = -u * sin + v * cos;
                    float streak = Mathf.PerlinNoise(along * 2.5f + 5f, across * 16f + 9f);                      // long and thin, along the wipe
                    float cloud = Mathf.PerlinNoise(u * 4f + 20f, v * 4f + 20f) * 0.6f + Mathf.PerlinNoise(u * 11f + 50f, v * 11f + 50f) * 0.4f;
                    float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                    float rim = Smooth(0.3f, 0.95f, r);                                                          // the veil, thickest at the rim
                    float patches = Mathf.Clamp01((cloud - 0.45f) * 2.2f) * (0.45f + 0.55f * streak);            // the smudges
                    float a = Mathf.Clamp01(0.3f * rim + 0.6f * patches);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            Round(pixels, size);
            return Finish(pixels, size);
        }

        /// <summary>
        /// Fades a picture out at the lens's rim, so it is a circle and not a square. It goes as far as the black ring does (the ring is
        /// complete at 0.985 of the radius): stopping short of it left a band, and a square shows as a box once the ring fades faster.
        /// </summary>
        private static void Round(Color32[] pixels, int size)
        {
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    float keep = 1f - Smooth(0.96f, 1.0f, r);
                    Color32 c = pixels[y * size + x];
                    pixels[y * size + x] = new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(c.a * keep));
                }
            }
        }

        private static Texture2D Finish(Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "nick_spyglass_lens",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);   // (kept in memory no more than the GPU needs)
            return texture;
        }

        /// <summary>A pale line with a dark one a pixel down and to the right; strength fades both (a crack's far end, its branches).</summary>
        private static void Line(Color32[] pixels, int size, Vector2 from, Vector2 to, float width, float strength = 1f)
        {
            Stroke(pixels, size, from + new Vector2(1.5f, -1.5f), to + new Vector2(1.5f, -1.5f), width, new Color32(10, 12, 16, (byte)(150 * strength)));
            Stroke(pixels, size, from, to, width, new Color32(238, 242, 246, (byte)(215 * strength)));
        }

        /// <summary>A line with soft edges (a pixel's ramp, not a hard step), the stronger mark winning where two cross.</summary>
        private static void Stroke(Color32[] pixels, int size, Vector2 from, Vector2 to, float width, Color32 color)
        {
            float length = Vector2.Distance(from, to);
            int n = Mathf.Max(1, Mathf.CeilToInt(length * 1.5f));
            float half = width * 0.5f;
            int reach = Mathf.CeilToInt(half + 1.5f);
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)n);
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                        if (x < 0 || y < 0 || x >= size || y >= size) continue;
                        float d = Mathf.Sqrt((x - p.x) * (x - p.x) + (y - p.y) * (y - p.y));
                        float k = Mathf.Clamp01(half + 0.7f - d);   // full inside the line's half width, ramping to nothing over the next pixel
                        if (k <= 0f) continue;
                        byte a = (byte)Mathf.RoundToInt(color.a * k);
                        if (a > pixels[y * size + x].a) pixels[y * size + x] = new Color32(color.r, color.g, color.b, a);
                    }
                }
            }
        }

        /// <summary>A soft disc: full at the middle, fading to nothing at its edge.</summary>
        private static void Dot(Color32[] pixels, int size, Vector2 at, float radius, Color32 color)
        {
            int reach = Mathf.CeilToInt(radius + 1f);
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = Mathf.RoundToInt(at.x) + dx, y = Mathf.RoundToInt(at.y) + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    float d = Mathf.Sqrt((x - at.x) * (x - at.x) + (y - at.y) * (y - at.y));
                    float k = Mathf.Clamp01((radius + 0.8f - d) / Mathf.Max(1f, radius * 0.6f + 0.8f));
                    if (k <= 0f) continue;
                    byte a = (byte)Mathf.RoundToInt(color.a * k);
                    if (a > pixels[y * size + x].a) pixels[y * size + x] = new Color32(color.r, color.g, color.b, a);
                }
            }
        }
    }
}
