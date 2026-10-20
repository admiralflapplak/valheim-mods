using BepInEx.Configuration;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// A vast eye opens below the ship. A dark scaled hide rises out of the murk under the hull, the eye in it shut; then the lids roll
    /// apart on a burning eye with a slit pupil (after the package icon), which looks up, finds whoever is looking down, follows them,
    /// blinks once (the upper lid), and closes. Its upper lid is always the one away from the ship: looking down from the deck, the top.
    /// (0.11 tried a searching eye that locked onto one of the crew and tightened its slit; taken out at the user's choice.)
    /// The hide sinks away. Easy to miss; impossible to forget. Made here: the eyeball's texture is drawn in code, the lids are sphere
    /// shells turning on the eye's long axis (their opening is the almond of an eye); the hide is the sea serpent's darkened scales.
    /// (A milky third lid sweeping across was tried in 0.6.6-0.6.8 and taken out.)
    /// </summary>
    internal sealed class EyeOmen : Omen
    {
        internal override string Name => "eye";
        protected override ConfigEntry<bool> Setting => Plugin.Eye;

        internal const float Width = 36f;   // the eyeball across (metres): tried 6 to 36 in the game, the biggest won

        internal override void Play(Ship ship, uint seed) => Play(ship, seed, Width, float.NaN);

        /// <summary>For "keel play eye at &lt;seconds&gt;": held still at that moment of its life, to look at.</summary>
        internal static float FreezeAt = -1f;

        /// <summary>For "keel play eye classic": the first eye, drawn with its whites and a painted slit, instead of the burning one.</summary>
        internal static bool Classic;

        /// <summary>For "keel play eye [fire] show": the whole model lifted above the water, further out, to look at.</summary>
        internal static bool Show;

        /// <param name="depth">How far under the surface the top of the eye is (0: 6.5 m, give or take a little from the seed: near the
        /// deepest one can see, so the glow carries and the rest comes and goes; 8.5 was too deep). "keel play eye [width] [depth]"
        /// tries others.</param>
        internal static void Play(Ship ship, uint seed, float width, float depth)
        {
            var rng = new System.Random((int)seed);
            float radius = width / 2f;
            if (float.IsNaN(depth)) depth = 6.25f + (float)rng.NextDouble() * 0.5f;   // (negative: above the water, for "show")
            GameObject go = Visuals.Spawn("Eye", ship.transform.position, Quaternion.identity, EyeBody.Duration + 1f);
            go.AddComponent<EyeBody>().Begin(ship, radius,
                lateral: (rng.Next(2) == 0 ? -1f : 1f) * radius * (Show ? 11.5f : 1.15f + (float)rng.NextDouble() * 0.35f),   // beside the hull: seen over the side
                depth: depth, freezeAt: FreezeAt);
            FreezeAt = -1f;
            Classic = false;   // (one test eye at a time: the ones that come on their own keep the default)
            Show = false;
        }
    }

    internal sealed class EyeBody : MonoBehaviour
    {
        internal const float Duration = 26f;

        private const float LidGap = 1.04f;        // the lids sit just outside the eyeball
        private const float HiddenBelow = 2.5f;    // where it starts and ends, in eye radii below its place: past what the murk lets through
        private const float RiseSeconds = 5f, SinkSeconds = 5f;
        private const float OpenAt = 6f, OpenSeconds = 2.5f;
        private const float BlinkAt = 14f;
        private const float CloseAt = 19.5f, CloseSeconds = 1.8f;
        private const float OpenAngle = 62f;       // how far each lid rolls back
        private const float ShutUpper = -47f;      // shut, the upper lid comes all the way over...
        private const float ShutLower = -45f;      // ...to meet the lower one, which only rises a little
        private const float MaxGaze = 35f;         // the most the eyeball turns in its socket
        private const float GazeLag = 2.5f;        // how quickly it catches up with the player
        private const float TurnRate = 0.35f;      // how quickly it follows the ship's turns

        private Ship _ship;
        private float _radius, _lateral, _depth, _age, _freezeAt = -1f, _frozenFor;
        private Vector3 _heading;
        private Transform _socket, _ball, _lidA, _lidB;
        private Quaternion _gaze = Quaternion.identity;


        internal void Begin(Ship ship, float radius, float lateral, float depth, float freezeAt)
        {
            _freezeAt = freezeAt;
            if (freezeAt >= 0f) _age = freezeAt;
            _radius = radius;
            _ship = ship;
            _lateral = lateral;
            _depth = depth;
            _heading = Flat(ship.transform.forward);

            _socket = new GameObject("Socket").transform;
            _socket.SetParent(transform, false);

            Material hideMat = Look.DarkHide();

            // The hide: a vast hump of scales, its top a little above the eye's middle (the eye bulges out of it), all of it deeper than
            // the murk lets one see: no shape to make out, only the eye and the lids round it.
            GameObject hide = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(hide.GetComponent<Collider>());
            hide.name = "Hide";
            hide.transform.SetParent(_socket, false);
            hide.transform.localScale = new Vector3(200f, 60f, 140f) * (radius / 12f);
            hide.transform.localPosition = new Vector3(0f, radius * 0.25f - 30f * (radius / 12f), 0f);
            Setup(hide.GetComponent<MeshRenderer>(), hideMat);

            _ball = Part("Eyeball", EyeMeshes.Sphere(radius, 0f, 180f, 0f, 360f, iris: true), EyeOmen.Classic ? Look.Eye() : Look.EyeFire());

            // Lid A (the upper, away from the ship) is a half-shell, so it can cover the whole eye on its own when it blinks; lid B a
            // quarter.
            _lidA = Part("LidA", EyeMeshes.Sphere(radius * LidGap, 0f, 180f, 0f, 180f, iris: false), hideMat);
            _lidB = Part("LidB", EyeMeshes.Sphere(radius * LidGap, 0f, 90f, 180f, 360f, iris: false), hideMat);

            Place(0f);
        }

        private Transform Part(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_socket, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Setup(go.AddComponent<MeshRenderer>(), mat);
            return go.transform;
        }

        private static void Setup(MeshRenderer r, Material mat)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_ship == null) { Destroy(gameObject); return; }
            if (_freezeAt < 0f) _age += Time.deltaTime;
            else if ((_frozenFor += Time.deltaTime) > 60f) { Destroy(gameObject); return; }
            Place(Time.deltaTime);
        }

        private void OnDestroy()
        {
            foreach (MeshFilter f in GetComponentsInChildren<MeshFilter>())
                if (f.sharedMesh != null && f.sharedMesh.name.StartsWith("BelowTheKeel_")) Destroy(f.sharedMesh);
        }

        private void Place(float dt)
        {
            if (dt > 0f) _heading = Vector3.Slerp(_heading, Flat(_ship.transform.forward), 1f - Mathf.Exp(-TurnRate * dt)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, _heading);
            Vector3 p = _ship.transform.position + right * _lateral;
            p.y = Sea.WaterLevel - _depth - _radius - Lower();
            _socket.position = p;
            _socket.rotation = Quaternion.LookRotation(right * Mathf.Sign(_lateral));   // +z, where the upper lid is, away from the ship: from the deck it is the top

            // Shut, the upper lid comes all the way down to the lower one, which sits low on the ship's side (from the deck the eye
            // bulges toward you, so a seam at the very top would make the lower lid seem to do the closing). Opening, closing and the
            // blink all run between the same two poses: the upper lid always moves, from the top.
            float open = Open(), o = open * (1f - Blink());
            _lidA.localRotation = Quaternion.Euler(Mathf.Lerp(ShutUpper, OpenAngle, o), 0f, 0f);
            _lidB.localRotation = Quaternion.Euler(Mathf.Lerp(ShutLower, -OpenAngle, o), 0f, 0f);

            // The eyeball turns toward the player, only so far and a little behind; it rests looking straight up while shut.
            Quaternion want = Quaternion.identity;
            Player player = Player.m_localPlayer;
            if (player != null && open > 0f)
            {
                Vector3 to = _socket.InverseTransformDirection(player.GetHeadPoint() - _ball.position);
                Quaternion full = Quaternion.FromToRotation(Vector3.up, to);
                float angle = Quaternion.Angle(Quaternion.identity, full);
                want = Quaternion.Slerp(Quaternion.identity, full, angle > MaxGaze ? MaxGaze / angle : 1f);
            }
            _gaze = Quaternion.Slerp(_gaze, want, dt > 0f ? 1f - Mathf.Exp(-GazeLag * dt) : 1f);
            _ball.localRotation = _gaze;
        }

        /// <summary>How far below its place it is: rising out of the murk at the start, sinking back at the end.</summary>
        private float Lower()
        {
            float hidden = HiddenBelow * _radius;
            if (_age < RiseSeconds) return hidden * (1f - Mathf.SmoothStep(0f, 1f, _age / RiseSeconds));
            float left = Duration - _age;
            if (left < SinkSeconds) return hidden * (1f - Mathf.SmoothStep(0f, 1f, left / SinkSeconds));
            return 0f;
        }

        /// <summary>How open the lids are, 0 to 1: a slow opening, a slow closing.</summary>
        private float Open()
        {
            if (_age < OpenAt) return 0f;
            if (_age >= CloseAt) return 1f - Mathf.SmoothStep(0f, 1f, (_age - CloseAt) / CloseSeconds);
            return Mathf.SmoothStep(0f, 1f, (_age - OpenAt) / OpenSeconds);
        }

        /// <summary>The blink, 0 to 1 (shut): the upper lid down fast, up a little slower.</summary>
        private float Blink()
        {
            float b = _age - BlinkAt;
            if (b >= 0f && b < 0.15f) return b / 0.15f;
            if (b >= 0.15f && b < 0.45f) return 1f - (b - 0.15f) / 0.3f;
            return 0f;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }
    }

    /// <summary>Sphere pieces for the eye, and the eye's texture.</summary>
    internal static class EyeMeshes
    {
        /// <summary>
        /// Part of a sphere: polar angle from +y (0 the top) and azimuth from +x toward +z, in degrees. With iris, the texture is laid
        /// straight down from above (the iris at the top), and everything below the middle gets the white of the eye.
        /// </summary>
        internal static Mesh Sphere(float radius, float polarFrom, float polarTo, float azFrom, float azTo, bool iris)
        {
            const int rows = 24, cols = 48;
            var verts = new Vector3[(rows + 1) * (cols + 1)];
            var normals = new Vector3[verts.Length];
            var uv = new Vector2[verts.Length];
            for (int i = 0; i <= rows; i++)
            {
                float polar = Mathf.Lerp(polarFrom, polarTo, i / (float)rows) * Mathf.Deg2Rad;
                for (int j = 0; j <= cols; j++)
                {
                    float az = Mathf.Lerp(azFrom, azTo, j / (float)cols) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(polar) * Mathf.Cos(az), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(az));
                    int k = i * (cols + 1) + j;
                    verts[k] = d * radius;
                    normals[k] = d;
                    uv[k] = !iris ? new Vector2(j / (float)cols, i / (float)rows)
                          : d.y > 0f ? new Vector2(d.x * 0.5f + 0.5f, d.z * 0.5f + 0.5f)
                          : new Vector2(0.02f, 0.02f);
                }
            }
            var tris = new int[rows * cols * 6];
            int t = 0;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    int a = i * (cols + 1) + j, b = a + 1, c = a + cols + 1, e = c + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = e; tris[t++] = c;
                }
            var mesh = new Mesh { name = "BelowTheKeel_EyePart" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The eye, seen from above (uv centre = the top of the eyeball): a slit pupil, a streaked amber iris, a dark ring, and the dark,
        /// bloodshot white. The glow map holds only the iris: under the water only what glows or is dark shows.
        /// </summary>
        /// <summary>
        /// The burning eye (thunderstore/make_icon.py's glow_eye, after Reference/pic2.png): no white, the iris out to where the lids
        /// close, hot yellow at the middle through orange to deep red, darkening at the edge; blotchy in coarse blocks (like the game's own
        /// pixelated textures); a tall, ragged, slightly leaning slit. All of it glows (the glow map is the picture itself).
        /// </summary>
        internal static void DrawFire(out Texture2D albedo, out Texture2D glow)
        {
            const int size = 512, block = 11;
            var rng = new System.Random(3);
            int g1 = size / block + 2, g2 = size / (block * 4) + 3;
            var n1 = new float[g1, g1];
            var n2 = new float[g2, g2];
            for (int i = 0; i < g1; i++) for (int j = 0; j < g1; j++) n1[i, j] = (float)rng.NextDouble();
            for (int i = 0; i < g2; i++) for (int j = 0; j < g2; j++) n2[i, j] = (float)rng.NextDouble();
            Color[] stops = { new Color(0.40f, 0.03f, 0.02f), new Color(0.85f, 0.20f, 0.04f), new Color(1f, 0.55f, 0.08f),
                              new Color(1f, 0.86f, 0.22f), new Color(1f, 0.97f, 0.55f) };
            float[] at = { 0f, 0.35f, 0.62f, 0.85f, 1f };

            albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BelowTheKeel_FireAlbedo", wrapMode = TextureWrapMode.Clamp };
            glow = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BelowTheKeel_FireGlow", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float blocky = n1[y / block, x / block];
                    // smooth large blotches: the coarse grid, eased between its points
                    float gx = x / (float)(block * 4), gy = y / (float)(block * 4);
                    int ix = (int)gx, iy = (int)gy;
                    float fx = gx - ix, fy = gy - iy;
                    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                    float smooth = Mathf.Lerp(Mathf.Lerp(n2[iy, ix], n2[iy, ix + 1], fx), Mathf.Lerp(n2[iy + 1, ix], n2[iy + 1, ix + 1], fx), fy);
                    float heat = Mathf.Clamp01(1.08f - r * 1.05f + (smooth - 0.5f) * 0.6f + (blocky - 0.5f) * 0.22f);
                    Color c = stops[0];
                    for (int k = 0; k < at.Length - 1; k++)
                        if (heat >= at[k] && heat <= at[k + 1]) c = Color.Lerp(stops[k], stops[k + 1], (heat - at[k]) / (at[k + 1] - at[k]));
                    float lean = u - v * 0.06f;
                    float width = 0.07f * (1f - Mathf.Pow(Mathf.Clamp01(Mathf.Abs(v) / 0.9f), 2f)) + (blocky - 0.5f) * 0.02f;
                    if (Mathf.Abs(lean) < width && Mathf.Abs(v) < 0.9f) c = new Color(0.02f, 0.01f, 0.01f);
                    c *= Mathf.Clamp01((1.02f - r) / 0.2f);   // darkening toward where the lids close
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            albedo.SetPixels(px);
            albedo.Apply(true);
            glow.SetPixels(px);
            glow.Apply(true);
        }

        internal static void Draw(out Texture2D albedo, out Texture2D glow)
        {
            const int size = 256;
            albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BelowTheKeel_EyeAlbedo", wrapMode = TextureWrapMode.Clamp };
            glow = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BelowTheKeel_EyeGlow", wrapMode = TextureWrapMode.Clamp };
            var a = new Color[size * size];
            var g = new Color[size * size];
            const float irisR = 0.5f, ringR = 0.56f, slitHalfLength = 0.46f, slitHalfWidth = 0.07f;
            var white = new Color(0.16f, 0.1f, 0.08f);
            var vein = new Color(0.3f, 0.04f, 0.03f);
            var irisCol = new Color(1f, 0.62f, 0.12f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float angle = Mathf.Atan2(v, u);
                    Color ca, cg = Color.black;
                    bool pupil = Mathf.Abs(v) < slitHalfLength && Mathf.Abs(u) < slitHalfWidth * (1f - (v / slitHalfLength) * (v / slitHalfLength));
                    if (pupil) ca = Color.black;
                    else if (r < irisR)
                    {
                        // Streaks out from the pupil, brighter toward it, broken up a little.
                        float streak = 0.6f + 0.25f * Mathf.Sin(angle * 37f) + 0.15f * Mathf.Sin(angle * 11f + r * 20f);
                        float toward = Mathf.Lerp(1.15f, 0.55f, r / irisR);
                        cg = irisCol * (streak * toward);
                        ca = cg * 0.5f;
                    }
                    else if (r < ringR) ca = new Color(0.03f, 0.02f, 0.01f);
                    else
                    {
                        float veins = Mathf.Pow(Mathf.Abs(Mathf.Sin(angle * 9f + Mathf.Sin(r * 14f) * 1.5f)), 30f);
                        ca = Color.Lerp(white, vein, veins * Mathf.Clamp01((r - ringR) * 3f));
                    }
                    ca.a = 1f;
                    cg.a = 1f;
                    a[y * size + x] = ca;
                    g[y * size + x] = cg;
                }
            albedo.SetPixels(a);
            albedo.Apply(true);
            glow.SetPixels(g);
            glow.Apply(true);
        }
    }
}
