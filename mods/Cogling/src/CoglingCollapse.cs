using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cogling
{
    /// <summary>
    /// The Cogling falling apart when he's taken down (the hammer) or broken: an effect of his death (Humanoid.m_deathEffects, beside
    /// TWIG's wood chips and breaking sound), so every player sees it. His body is split by bone (head, chest, arms, legs...) into
    /// pieces that tumble apart and shrink away. The pose is the dying Cogling's when he's still here on this game, else his rest pose.
    /// Nothing is saved: the pieces are this game's own and go with the effect (TimedDestruction).
    /// </summary>
    internal sealed class CoglingCollapse : MonoBehaviour
    {
        internal const string Name = "cogling_collapse";
        private const float Lifetime = 6f, ShrinkFrom = 4f;

        // small bones join the part they hang from: one piece each for head, chest, upper/lower arms and legs, hands and feet
        private static readonly Dictionary<string, string> Joins = new Dictionary<string, string>
        {
            { "HeadTop_End", "Head" }, { "Neck", "Head" }, { "Spine1", "Spine" },
            { "LeftToeBase", "LeftFoot" }, { "LeftToe_End", "LeftFoot" }, { "RightToeBase", "RightFoot" }, { "RightToe_End", "RightFoot" },
            { "LeftShoulder", "Spine2" }, { "RightShoulder", "Spine2" },
        };

        private readonly List<Transform> _pieces = new List<Transform>();
        private float _age;

        private void Start()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || !CoglingModel.Load()) return;
            try { CoglingVoice.Death(transform.position); }   // (his last words, winding down)
            catch (System.Exception e) { Plugin.Log.LogWarning("Cogling last words: " + e.Message); }
            try { Fall(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Cogling collapse: " + e.Message); }
        }

        private void Fall()
        {
            Mesh source = CoglingModel.Mesh;
            Vector3[] world;
            Material[] materials;
            CoglingBody body = CoglingBody.Nearest(transform.position, 1.5f);
            if (body != null && body.Skin != null)
            {
                var baked = new Mesh();
                body.Skin.BakeMesh(baked);
                Transform t = body.Skin.transform;
                world = baked.vertices.Select(v => t.TransformPoint(v)).ToArray();
                materials = body.Skin.sharedMaterials;
                Object.Destroy(baked);
            }
            else
            {
                if (CoglingBody.SharedMaterials == null) return;
                world = source.vertices.Select(v => transform.TransformPoint(v)).ToArray();
                materials = CoglingBody.SharedMaterials;
            }

            // each triangle goes with the part its first corner's strongest bone belongs to
            BoneWeight[] weights = source.boneWeights;
            string Part(int vertex)
            {
                string bone = CoglingModel.BoneNames[weights[vertex].boneIndex0];
                bone = bone.StartsWith("mixamorig:") ? bone.Substring(10) : bone;
                return Joins.TryGetValue(bone, out string to) ? to : bone;
            }
            var parts = new Dictionary<string, List<int>[]>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] tris = source.GetTriangles(sub);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    string part = Part(tris[i]);
                    if (!parts.TryGetValue(part, out List<int>[] lists))
                        parts[part] = lists = Enumerable.Range(0, source.subMeshCount).Select(_ => new List<int>()).ToArray();
                    lists[sub].Add(tris[i]);
                    lists[sub].Add(tris[i + 1]);
                    lists[sub].Add(tris[i + 2]);
                }
            }

            Vector3 middle = transform.position + Vector3.up * CoglingModel.Height * 0.5f;
            Vector2[] uv = source.uv;
            Vector3[] normals = source.normals;
            int layer = LayerMask.NameToLayer("Default_small");
            foreach (var part in parts)
            {
                // the part's own mesh, around its own centre
                var map = new Dictionary<int, int>();
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                foreach (int v in part.Value.SelectMany(l => l))
                    if (!map.ContainsKey(v)) { map[v] = verts.Count; verts.Add(world[v]); uvs.Add(uv[v]); }
                if (verts.Count < 3) continue;
                Vector3 centre = verts.Aggregate(Vector3.zero, (a, b) => a + b) / verts.Count;
                var mesh = new Mesh { name = "cogling_piece_" + part.Key };
                mesh.SetVertices(verts.Select(v => v - centre).ToList());
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = part.Value.Length;
                for (int sub = 0; sub < part.Value.Length; sub++) mesh.SetTriangles(part.Value[sub].Select(v => map[v]).ToList(), sub);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var piece = new GameObject(mesh.name) { layer = layer };
                piece.transform.SetParent(transform, true);
                piece.transform.position = centre;
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = materials;
                var box = piece.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.05f);
                var body2 = piece.AddComponent<Rigidbody>();
                body2.mass = 1f;
                Vector3 away = centre - middle;
                away.y = 0f;
                body2.linearVelocity = away.normalized * Random.Range(0.8f, 1.8f) + Vector3.up * Random.Range(1f, 2.5f);
                body2.angularVelocity = Random.insideUnitSphere * 6f;
                _pieces.Add(piece.transform);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age < ShrinkFrom) return;
            float s = Mathf.Clamp01(1f - (_age - ShrinkFrom) / (Lifetime - ShrinkFrom - 0.3f));
            foreach (Transform p in _pieces) if (p != null) p.localScale = Vector3.one * s;
        }

        private void OnDestroy()
        {
            foreach (Transform p in _pieces)
                if (p != null && p.GetComponent<MeshFilter>()?.sharedMesh is Mesh m) Object.Destroy(m);
        }

        // ---- the effect prefab (networked, not saved), registered by name like the pieces

        internal static GameObject Prefab;
        private static GameObject _holder;

        internal static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab == null)
            {
                _holder = new GameObject("cogling_prefabs_collapse");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
                Prefab = new GameObject(Name);
                Prefab.transform.SetParent(_holder.transform, false);
                var view = Prefab.AddComponent<ZNetView>();
                view.m_persistent = false;
                var timed = Prefab.AddComponent<TimedDestruction>();
                timed.m_timeout = Lifetime;
                timed.m_triggerOnAwake = true;
                Prefab.AddComponent<CoglingCollapse>();
            }
            PieceReload.ReplacePrefab(scene, Prefab, null);
        }

        internal static void Forget() => _holder = Prefab = null;   // (kept registered and alive for an older copy's effects)
    }
}
