using BepInEx.Configuration;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Jörmungandr, unnamed: a long dark shape directly beneath the ship, matching its speed and turns. However long you watch, there is
    /// always more body and never a head. It rises out of the murk, keeps pace for a while, and sinks again.
    /// </summary>
    internal sealed class PacerOmen : Omen
    {
        internal override string Name => "pacer";
        protected override ConfigEntry<bool> Setting => Plugin.Pacer;

        internal override void Play(Ship ship, uint seed)
        {
            var rng = new System.Random((int)seed);
            float duration = Range(rng, 45f, 60f);

            GameObject go = Visuals.Spawn("Pacer", ship.transform.position, Quaternion.identity, duration + 1f);
            var body = go.AddComponent<PacerBody>();
            body.Init(ship, duration,
                lateral: Range(rng, -5f, 5f),
                depth: Range(rng, 6.5f, 8f),
                radius: Range(rng, 3.2f, 4.2f),
                flow: Range(rng, 1.5f, 3f),
                wave: Range(rng, 4f, 6f));
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }

    /// <summary>
    /// The body: a tube along a slowly swaying line under the ship, rebuilt every frame. The line follows the ship (its turns a little
    /// late), and the sway and the swellings along the body travel forward, so the body seems to slide past underneath without end. Its
    /// ends are tapered and far out in the murk (the probe showed nothing dark is seen deeper than about 8 m, or far off at a slant).
    /// </summary>
    internal sealed class PacerBody : MonoBehaviour
    {
        private const int Rings = 90;
        private const int Sides = 14;
        private const float HalfLength = 80f;
        private const float Taper = 20f;           // metres at each end over which the body narrows to nothing
        private const float HiddenDepth = 16f;     // where it starts and ends: well past what the murk lets through
        private const float RiseSeconds = 9f;
        private const float SinkSeconds = 12f;
        private const float TurnRate = 0.35f;      // how quickly its heading catches up with the ship's (per second)

        private Ship _ship;
        private float _duration, _lateral, _depth, _radius, _flow, _wave, _age;
        private Vector3 _heading;
        private Mesh _mesh;
        private Vector3[] _verts;
        private Vector2[] _uv;

        /// <param name="flow">How fast the swellings slide forward along the body (metres a second).</param>
        /// <param name="wave">How fast its swimming S runs along the body: faster than the swellings, so it writhes as it goes.</param>
        internal void Init(Ship ship, float duration, float lateral, float depth, float radius, float flow, float wave)
        {
            _wave = wave;
            _ship = ship;
            _duration = duration;
            _lateral = lateral;
            _depth = depth;
            _radius = radius;
            _flow = flow;
            _heading = Flat(ship.transform.forward);

            _mesh = new Mesh { name = "BelowTheKeel_Pacer" };
            _mesh.MarkDynamic();
            _verts = new Vector3[Rings * (Sides + 1)];
            _mesh.vertices = _verts;
            _uv = new Vector2[_verts.Length];
            _mesh.uv = _uv;
            _mesh.triangles = Triangles();

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Look.DarkSerpent();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Build();
        }

        private void LateUpdate()
        {
            if (_ship == null) { Destroy(gameObject); return; }
            _age += Time.deltaTime;
            _heading = Vector3.Slerp(_heading, Flat(_ship.transform.forward), 1f - Mathf.Exp(-TurnRate * Time.deltaTime)).normalized;
            Build();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        private void Build()
        {
            Vector3 fwd = _heading;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 shipPos = _ship.transform.position;
            Vector3 centre = new Vector3(shipPos.x, Sea.WaterLevel - CurrentDepth(), shipPos.z) + right * _lateral;
            transform.SetPositionAndRotation(centre, Quaternion.identity);

            float scroll = _age * _flow;   // how far the body has slid forward since it came
            float swim = _age * _wave;     // how far its swimming wave has run
            Vector3 prev = Spine(-HalfLength - 1f, swim, fwd, right);
            for (int i = 0; i < Rings; i++)
            {
                float s = Mathf.Lerp(-HalfLength, HalfLength, i / (float)(Rings - 1));
                Vector3 p = Spine(s, swim, fwd, right);
                Vector3 tangent = (p - prev).normalized;
                prev = p;
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 up = Vector3.Cross(tangent, side);

                float r = Radius(s, scroll);
                for (int j = 0; j <= Sides; j++)
                {
                    float a = j * Mathf.PI * 2f / Sides;
                    int k = i * (Sides + 1) + j;
                    _verts[k] = p + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;
                    _uv[k] = new Vector2(j / (float)Sides, (s - scroll) / 6f);
                }
            }
            _mesh.vertices = _verts;
            _mesh.uv = _uv;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        /// <summary>A point on the body's middle line, relative to its centre: a swimming S running forward along it, a faster ripple
        /// on top, and a slower rise and fall.</summary>
        private Vector3 Spine(float s, float swim, Vector3 fwd, Vector3 right)
        {
            float u = s - swim;
            float sway = 3.5f * Mathf.Sin(u * 2f * Mathf.PI / 34f) + 1f * Mathf.Sin(u * 2f * Mathf.PI / 12f + 1.3f);
            float heave = 0.9f * Mathf.Sin(u * 2f * Mathf.PI / 27f + 0.6f);
            return fwd * s + right * sway + Vector3.up * heave;
        }

        /// <summary>Swellings that slide along with the body (the eye reads them as movement), narrowing to nothing at both far ends.</summary>
        private float Radius(float s, float scroll)
        {
            float u = s - scroll;
            float swell = 1f + 0.12f * Mathf.Sin(u * 2f * Mathf.PI / 9f) + 0.06f * Mathf.Sin(u * 2f * Mathf.PI / 4f + 2f);
            float end = Mathf.Clamp01((HalfLength - Mathf.Abs(s)) / Taper);
            return _radius * swell * Mathf.SmoothStep(0f, 1f, end);
        }

        /// <summary>Rises out of the murk, holds, sinks back into it.</summary>
        private float CurrentDepth()
        {
            float bob = 0.4f * Mathf.Sin(_age * 0.5f);
            if (_age < RiseSeconds)
                return Mathf.Lerp(HiddenDepth, _depth, Mathf.SmoothStep(0f, 1f, _age / RiseSeconds)) + bob;
            float left = _duration - _age;
            if (left < SinkSeconds)
                return Mathf.Lerp(HiddenDepth, _depth, Mathf.SmoothStep(0f, 1f, left / SinkSeconds)) + bob;
            return _depth + bob;
        }

        private static int[] Triangles()
        {
            var tris = new int[(Rings - 1) * Sides * 6];
            int t = 0;
            for (int i = 0; i < Rings - 1; i++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = i * (Sides + 1) + j, b = a + 1, c = a + Sides + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            return tris;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }
    }
}
