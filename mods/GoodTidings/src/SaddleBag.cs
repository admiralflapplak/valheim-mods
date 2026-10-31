using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The trader's saddle bags: the model in SaddleBagModel.cs (drawn with model/saddlebag.py), in the Lox saddle's own leather, so they
    /// match it: "Leather" its plain brown patch, "Coal" its dark strap patch (parts of its atlas, by texture scale and offset), "Iron" the
    /// game's iron. (The leather armour's material, Standard, came out pale grey under the game's light.)
    /// </summary>
    internal static class SaddleBag
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        internal static GameObject Build(Transform parent)
        {
            if (Materials.Count == 0 || Materials.ContainsValue(null)) Harvest();
            GameObject bag = ModelBuilder.Build(parent, SaddleBagModel.Parts, parent.gameObject.layer, name => Materials.TryGetValue(name, out Material m) ? m : Materials["Leather"]);
            bag.name = "goodtidings_saddlebag";
            // Hand-made, not machined: each box becomes a rough one, its faces puffed out as a stuffed bag's are and its outline wobbling
            // (the same for every bag: seeded by the part's place in the list)
            int seed = 0;
            foreach (MeshFilter f in bag.GetComponentsInChildren<MeshFilter>())
            {
                seed++;
                if (f.sharedMesh == null || !f.sharedMesh.name.StartsWith("Cube")) continue;
                string n = f.gameObject.name;
                (float puff, float wobble) = n == "body" ? (0.16f, 0.07f) : n == "pouch" ? (0.22f, 0.08f) : n.StartsWith("flap") ? (0.06f, 0.07f)
                    : n.StartsWith("buckle") ? (0f, 0.02f) : (0.03f, 0.05f);
                f.sharedMesh = Rough(seed * 7919, puff, wobble);
            }
            return bag;
        }

        private static void Harvest()
        {
            Clear();
            Material saddle = Find("SaddleLox", "testloxsaddle_m");
            Material iron = Find("iron_grate", "metalwall");
            if (saddle)
            {
                Materials["Leather"] = new Material(saddle) { mainTextureScale = new Vector2(0.16f, 0.1f), mainTextureOffset = new Vector2(0.33f, 0.84f) };
                Materials["Coal"] = new Material(saddle) { mainTextureScale = new Vector2(0.11f, 0.16f), mainTextureOffset = new Vector2(0.06f, 0.12f) };
            }
            else Materials["Leather"] = Materials["Coal"] = null;
            Materials["Iron"] = iron ? new Material(iron) : null;
            foreach (string k in new List<string>(Materials.Keys))
                if (Materials[k] == null) { Plugin.Log.LogWarning($"Saddle bag: the game's material for {k} is missing"); Materials[k] = Materials["Leather"]; }
        }

        private static readonly Dictionary<int, Mesh> RoughMeshes = new Dictionary<int, Mesh>();

        /// <summary>
        /// A rough box, shaped as Unity's cube primitive is (1 across, centred, each face's UVs 0 to 1) so it takes the part's scale and
        /// material the same way: each face a 5 x 5 grid, bulged out in the middle by <paramref name="puff"/> (of the half size) and every
        /// point moved by a smooth wobble of its position (so the edges two faces share move together and the box stays closed).
        /// </summary>
        private static Mesh Rough(int seed, float puff, float wobble)
        {
            int key = seed * 31 + Mathf.RoundToInt(puff * 1000) * 7 + Mathf.RoundToInt(wobble * 1000);
            if (RoughMeshes.TryGetValue(key, out Mesh cached) && cached) return cached;
            var rnd = new System.Random(seed);
            var ph = new float[9];
            for (int i = 0; i < ph.Length; i++) ph[i] = (float)rnd.NextDouble() * Mathf.PI * 2f;
            Vector3 Wobble(Vector3 p) => wobble * new Vector3(
                Mathf.Sin(p.y * 5.1f + ph[0]) * 0.6f + Mathf.Sin(p.z * 7.3f + ph[1]) * 0.4f,
                Mathf.Sin(p.z * 4.7f + ph[2]) * 0.6f + Mathf.Sin(p.x * 6.9f + ph[3]) * 0.4f,
                Mathf.Sin(p.x * 5.3f + ph[4]) * 0.6f + Mathf.Sin(p.y * 7.7f + ph[5]) * 0.4f);

            const int N = 5;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 normal in normals)
            {
                Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(normal, u);
                int b = verts.Count;
                for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    float a = i * 2f / N - 1f, c = j * 2f / N - 1f;
                    Vector3 p = 0.5f * (normal + a * u + c * v);
                    p += normal * (0.5f * puff * (1f - a * a) * (1f - c * c));
                    verts.Add(p + Wobble(0.5f * (normal + a * u + c * v)));
                    uvs.Add(new Vector2((a + 1f) * 0.5f, (c + 1f) * 0.5f));
                }
                for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int p00 = b + j * (N + 1) + i, p10 = p00 + 1, p01 = p00 + N + 1, p11 = p01 + 1;
                    // wound so the face looks out along its normal
                    bool flip = Vector3.Dot(Vector3.Cross(verts[p01] - verts[p00], verts[p11] - verts[p00]), normal) < 0f;
                    if (!flip) { tris.AddRange(new[] { p00, p01, p11, p00, p11, p10 }); }
                    else { tris.AddRange(new[] { p00, p11, p01, p00, p10, p11 }); }
                }
            }
            var mesh = new Mesh { name = "goodtidings_roughbox" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();   // soft within a face, crisp at the box's edges (the faces don't share points)
            mesh.RecalculateBounds();
            return RoughMeshes[key] = mesh;
        }

        private static Material Find(string prefab, string material)
        {
            GameObject go = ZNetScene.instance.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == material) return m;
            return null;
        }

        internal static void Clear()
        {
            foreach (Mesh m in RoughMeshes.Values) if (m) Object.Destroy(m);
            RoughMeshes.Clear();
            foreach (Material m in Materials.Values) if (m) Object.Destroy(m);
            Materials.Clear();
        }
    }
}
