using System.Collections.Generic;
using UnityEngine;

namespace Wanted
{
    /// <summary>
    /// The coin pouch that hangs on a board while a reward waits in its box: a sagging leather bag cinched with a drawstring, coins
    /// showing at its mouth, a loop to hang it by. Made in code from turned shapes in copies of the game's standard material. The model,
    /// the parts and the materials are a copy of Grudges' made props (mods/Grudges/src/ScarModels.cs, its "made:Pouch"), so each mod
    /// carries its own.
    /// </summary>
    internal static class PouchModel
    {
        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> _rods = new Dictionary<string, Mesh>();
        private static Material _basis;
        private static Texture2D _mottle;

        /// <summary>A new pouch under <paramref name="parent"/>, hanging from its origin (the loop) down, its front to -z. Null without a material to copy.</summary>
        public static GameObject Make(Transform parent, ZNetScene scene)
        {
            if (Basis(scene) == null) return null;
            var root = new GameObject("wanted_pouch_model");
            root.transform.SetParent(parent, false);
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // (made facing +z)
            Pouch(root, new System.Random(17));
            return root;
        }

        /// <summary>The game's own standard material, taken from a mead (as Weapon Oils does), to dress ours in.</summary>
        private static Material Basis(ZNetScene scene)
        {
            if (_basis != null) return _basis;
            if (scene == null) return null;
            foreach (string name in new[] { "MeadHealthMinor", "Wood", "AxeStone" })
            {
                var item = scene.GetPrefab(name);
                var r = item != null ? item.GetComponentInChildren<MeshRenderer>(true) : null;
                if (r != null && r.sharedMaterial != null) return _basis = r.sharedMaterial;
            }
            return null;
        }

        /// <summary>
        /// A coin pouch: a sagging leather bag cinched with a drawstring, its mouth ruffled open on a glint of coins,
        /// a loop at the top to hang it by. It hangs from the origin (the loop) down, its front to +z.
        /// </summary>
        private static void Pouch(GameObject root, System.Random rnd)
        {
            Color hide = new Color(0.42f, 0.27f, 0.15f);
            var sag = new Warp(rnd, 0.14f, 0.05f, 0.3f, 0.14f);
            // the bag, heavy at the bottom, pinched at the neck, its top folded out in a ruffle
            Add(root, "bag", Lathe(new[] { V(0f, -0.15f), V(0.03f, -0.147f), V(0.05f, -0.13f), V(0.059f, -0.1f), V(0.056f, -0.07f), V(0.042f, -0.05f),
                V(0.019f, -0.036f), V(0.021f, -0.028f), V(0.034f, -0.016f), V(0.031f, -0.011f), V(0.016f, -0.02f), V(0f, -0.024f) }, 8, 0f, sag),
                Mat(hide, 0.12f));
            // a darker sewn-on patch on its front
            var patch = Add(root, "patch", Lathe(new[] { V(0f, -0.002f), V(0.018f, -0.002f), V(0.02f, 0f), V(0.018f, 0.002f), V(0f, 0.002f) }, 7),
                Mat(new Color(0.28f, 0.17f, 0.09f), 0.1f), new Vector3(0f, -0.098f, 0.054f));
            patch.transform.localRotation = Quaternion.Euler(90f, 0f, Range(rnd, -20f, 20f));
            Material cord = Mat(new Color(0.22f, 0.15f, 0.08f), 0.05f);
            // the drawstring round the neck, its ends hanging with a bead each
            var ring = Add(root, "drawstring", Ring(0.021f, 0.0032f, 9, 5), cord, new Vector3(0f, -0.034f, 0f));
            ring.transform.localRotation = Quaternion.Euler(Range(rnd, -6f, 6f), 0f, Range(rnd, -6f, 6f));
            Material bead = Mat(new Color(0.75f, 0.68f, 0.5f), 0.4f);
            foreach (float side in new[] { -1f, 1f })
            {
                var knot = new Vector3(0.012f * side, -0.036f, 0.018f);
                var end = knot + new Vector3(0.008f * side + Range(rnd, -0.004f, 0.004f), -0.04f - Range(rnd, 0f, 0.012f), 0.008f);
                Rod(root, knot, end, 0.0022f, cord);
                Add(root, "bead", Lathe(new[] { V(0f, -0.005f), V(0.004f, -0.003f), V(0.005f, 0f), V(0.004f, 0.003f), V(0f, 0.005f) }, 6), bead, end);
            }
            // the loop it hangs by
            Rod(root, new Vector3(-0.012f, -0.03f, -0.008f), new Vector3(-0.004f, 0f, 0f), 0.0035f, cord);
            Rod(root, new Vector3(0.012f, -0.03f, -0.008f), new Vector3(0.004f, 0f, 0f), 0.0035f, cord);
            Rod(root, new Vector3(-0.004f, 0f, 0f), new Vector3(0.004f, 0f, 0f), 0.0035f, cord);
            // coins showing in its mouth
            Material gold = Mat(new Color(0.92f, 0.72f, 0.26f), 0.75f, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                var coin = Add(root, "coin", Lathe(new[] { V(0f, -0.0012f), V(0.011f, -0.0012f), V(0.011f, 0.0012f), V(0f, 0.0012f) }, 9), gold,
                    new Vector3(Range(rnd, -0.012f, 0.012f), -0.016f + i * 0.002f, Range(rnd, -0.01f, 0.01f)));
                coin.transform.localRotation = Quaternion.Euler(Range(rnd, -50f, 50f), Range(rnd, 0f, 360f), Range(rnd, -50f, 50f));
            }
        }

        private static Vector2 V(float r, float y) => new Vector2(r, y);

        private static float Range(System.Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

        /// <summary>A point on a bowed slab's surface at height z (the slab curves back round the body at its sides).</summary>
        private static Vector3 P3(Vector2 p, float z, float bow) => new Vector3(p.x, p.y, z - bow * p.x * p.x);

        private static GameObject Add(GameObject root, string name, Mesh mesh, Material material, Vector3? at = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            if (at.HasValue) go.transform.localPosition = at.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// A cord or stick from one point to another. With a bow it runs over a bowed slab instead of straight across (in a few
        /// pieces, <paramref name="lift"/> above its front).
        /// </summary>
        private static void Rod(GameObject root, Vector3 from, Vector3 to, float radius, Material m, int sides = 5, float bow = 0f, float lift = 0f)
        {
            if (bow > 0f)
            {
                const int Pieces = 4;
                Vector3 prev = P3(new Vector2(from.x, from.y), lift, bow);
                for (int i = 1; i <= Pieces; i++)
                {
                    Vector3 l = Vector3.Lerp(from, to, i / (float)Pieces), next = P3(new Vector2(l.x, l.y), lift, bow);
                    Rod(root, prev, next, radius, m, sides);
                    prev = next;
                }
                return;
            }
            string key = radius.ToString("0.0000") + "/" + sides;
            if (!_rods.TryGetValue(key, out Mesh mesh) || mesh == null)
                _rods[key] = mesh = Lathe(new[] { V(0f, -0.15f * radius), V(radius, -0.15f * radius), V(radius, 1f), V(0f, 1f) }, sides);
            Vector3 d = to - from;
            var go = Add(root, "cord", mesh, m, from);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.up);
            go.transform.localScale = new Vector3(1f, d.magnitude, 1f);
        }

        /// <summary>A copy of the game's standard material in a colour, with a faint mottle so it reads as painted (and a glow, for embers).</summary>
        private static Material Mat(Color colour, float gloss, float glow = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGB(colour) + gloss.ToString("0.00") + glow.ToString("0.0");
            if (_materials.TryGetValue(key, out Material cached) && cached != null) return cached;
            var m = new Material(_basis) { name = "wanted_" + key, color = colour };
            if (m.HasProperty("_MainTex")) m.mainTexture = Mottle();
            foreach (string map in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap" })
                if (m.HasProperty(map)) m.SetTexture(map, null);
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICGLOSSMAP");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (glow > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", colour * glow);
            }
            else m.DisableKeyword("_EMISSION");
            _materials[key] = m;
            return m;
        }

        private static Texture2D Mottle()
        {
            if (_mottle != null) return _mottle;
            const int N = 32;
            var rnd = new System.Random(5);
            var coarse = new float[5, 5];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) coarse[x, y] = (float)rnd.NextDouble();
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x / (float)N * 4f, fy = y / (float)N * 4f;
                    int ix = (int)fx, iy = (int)fy;
                    float tx = fx - ix, ty = fy - iy;
                    float a = Mathf.Lerp(coarse[ix, iy], coarse[ix + 1, iy], tx), b = Mathf.Lerp(coarse[ix, iy + 1], coarse[ix + 1, iy + 1], tx);
                    float v = 0.76f + 0.14f * Mathf.Lerp(a, b, ty) + 0.10f * (float)rnd.NextDouble();
                    px[y * N + x] = new Color(v, v, v, 1f);
                }
            _mottle = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "wanted_mottle", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            _mottle.SetPixels(px);
            _mottle.Apply(false, true);
            return _mottle;
        }

        /// <summary>How a turned shape strays from true: its outline wobbles round, it leans, its upper rings tip.</summary>
        private sealed class Warp
        {
            private readonly Vector2 _lean;
            private readonly float _wobble, _tip, _height, _p1, _p2, _p3;

            public Warp(System.Random rnd, float wobble, float lean, float tip, float height)
            {
                _wobble = wobble; _tip = tip; _height = Mathf.Max(height, 0.001f);
                _p1 = Range(rnd, 0f, Mathf.PI * 2f); _p2 = Range(rnd, 0f, Mathf.PI * 2f); _p3 = Range(rnd, 0f, Mathf.PI * 2f);
                float dir = Range(rnd, 0f, Mathf.PI * 2f);
                _lean = new Vector2(Mathf.Cos(dir), Mathf.Sin(dir)) * lean * Range(rnd, 0.6f, 1f);
            }

            public Vector3 Apply(Vector2 p, float a)
            {
                float r = p.x * (1f + _wobble * (0.6f * Mathf.Sin(2f * a + _p1 + p.y * 25f) + 0.4f * Mathf.Sin(3f * a + _p2)));
                float y = p.y + _tip * r * Mathf.Sin(a + _p3) * Mathf.Clamp01(p.y / _height);
                return new Vector3(Mathf.Sin(a) * r + _lean.x * p.y, y, -Mathf.Cos(a) * r + _lean.y * p.y);
            }
        }

        /// <summary>A profile (radius, height pairs, bottom to top) spun round the y axis; a point at radius 0 closes that end.</summary>
        private static Mesh Lathe(Vector2[] profile, int sides, float phase = 0f, Warp warp = null, float crease = 66f)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                for (int s = 0; s < sides; s++)
                {
                    float a0 = (s + phase) * Mathf.PI * 2f / sides, a1 = (s + 1 + phase) * Mathf.PI * 2f / sides;
                    int b = verts.Count;
                    verts.Add(P(p0, a0, warp)); verts.Add(P(p0, a1, warp)); verts.Add(P(p1, a1, warp)); verts.Add(P(p1, a0, warp));
                    float u0 = s / (float)sides, u1 = (s + 1) / (float)sides;
                    uvs.Add(new Vector2(u0, p0.y * 4f)); uvs.Add(new Vector2(u1, p0.y * 4f)); uvs.Add(new Vector2(u1, p1.y * 4f)); uvs.Add(new Vector2(u0, p1.y * 4f));
                    if (p0.x > 1e-5f) { tris.Add(b); tris.Add(b + 3); tris.Add(b + 1); }
                    tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                }
            }
            var fv = new List<Vector3>(); var fu = new List<Vector2>(); var ft = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-16f) continue;   // (a sliver where the profile meets the axis)
                int n = fv.Count;
                fv.Add(a); fv.Add(b); fv.Add(c);
                fu.Add(uvs[tris[t]]); fu.Add(uvs[tris[t + 1]]); fu.Add(uvs[tris[t + 2]]);
                ft.Add(n); ft.Add(n + 1); ft.Add(n + 2);
            }
            var mesh = new Mesh { name = "wanted_lathe" };
            mesh.SetVertices(fv);
            mesh.SetUVs(0, fu);
            mesh.SetTriangles(ft, 0);
            mesh.SetNormals(SoftNormals(fv, crease));
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A ring lying flat round the y axis: radius R, thickness r.</summary>
        private static Mesh Ring(float R, float r, int sides, int around)
        {
            var profile = new Vector2[around + 1];
            for (int i = 0; i <= around; i++)
            {
                float a = -Mathf.PI / 2f + i * Mathf.PI * 2f / around;
                profile[i] = V(R + Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return Lathe(profile, sides);
        }

        private static Vector3 P(Vector2 p, float a, Warp warp) =>
warp != null ? warp.Apply(p, a) : new Vector3(Mathf.Sin(a) * p.x, p.y, -Mathf.Cos(a) * p.x);

        /// <summary>Each corner's normal: the faces round its point that turn less than <paramref name="crease"/> degrees from its own.</summary>
        private static List<Vector3> SoftNormals(List<Vector3> verts, float crease)
        {
            var faces = new Vector3[verts.Count / 3];
            for (int t = 0; t < faces.Length; t++)
                faces[t] = Vector3.Cross(verts[t * 3 + 1] - verts[t * 3], verts[t * 3 + 2] - verts[t * 3]);
            var at = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i] * 1e5f;
                var key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                if (!at.TryGetValue(key, out List<int> list)) at[key] = list = new List<int>();
                list.Add(i);
            }
            float cos = Mathf.Cos(crease * Mathf.Deg2Rad);
            var normals = new Vector3[verts.Count];
            foreach (List<int> corners in at.Values)
                foreach (int i in corners)
                {
                    Vector3 own = faces[i / 3].normalized, sum = Vector3.zero;
                    foreach (int j in corners)
                        if (Vector3.Dot(own, faces[j / 3].normalized) >= cos) sum += faces[j / 3];
                    normals[i] = sum.sqrMagnitude > 0f ? sum.normalized : own;
                }
            return new List<Vector3>(normals);
        }
    }
}
