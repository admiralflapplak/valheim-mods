using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WardrobePortal
{
    /// <summary>
    /// The wardrobe's look, made over for two doors. The game's wardrobe opens one door (the left): the right door and two shelves are
    /// part of the cabinet's own mesh, which the game keeps on the graphics card only (not readable). So the cabinet's mesh is read back
    /// from the graphics card once, and a copy made without the triangles of the right door and the shelves; the left door's mesh then
    /// hangs on two hinges, as itself and mirrored. Everything here is in the wardrobe mesh's own space: its doors at +z, the left door
    /// (as you face it) at +x, the floor at y = 0.
    /// If the mesh can't be read (a graphics API that refuses, or a dedicated server, which draws nothing), the cabinet stays whole
    /// and only the left door swings.
    /// </summary>
    internal static class Cabinet
    {
        public const string DoorPrefix = "door_hinge";

        // the opening door: the game's mesh spans x 0.005..0.635 at z 0.35..0.55, hinged on its outer edge
        private const float HingeX = 0.635f, HingeZ = 0.45f;

        // what is cut out of the cabinet (mesh space): the right door, and the shelves inside
        private static readonly Bounds RightDoor = Box(-0.645f, 0.01f, 0.10f, 2.58f, 0.34f, 0.60f);
        private static readonly Bounds Shelves = Box(-0.72f, 0.72f, 0.30f, 2.40f, -0.50f, 0.40f);   // shelves at y 1.65 and 2.15, to z 0.38

        private static Mesh _cut;            // the cabinet without them: made once a game, kept (pieces standing use it)
        private static bool _tried;

        private static Bounds Box(float x0, float x1, float y0, float y1, float z0, float z1)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
            return b;
        }

        /// <summary>Builds the cabinet and its two doors under <paramref name="parent"/>. Returns the cabinet's renderer (for bounds).</summary>
        public static MeshRenderer Build(Transform parent, GameObject wardrobe)
        {
            Transform look = wardrobe.transform.Find("New");
            MeshFilter bodySrc = look?.Find("blackmetalchest")?.GetComponent<MeshFilter>();
            MeshFilter doorSrc = look?.Find("Closed/blackmetalchesttop_closed")?.GetComponent<MeshFilter>();
            if (bodySrc == null || doorSrc == null) throw new Exception("the wardrobe's model has changed (no blackmetalchest or its door)");

            Mesh body = CutBody(bodySrc.sharedMesh);
            bool twoDoors = body != bodySrc.sharedMesh;

            var cabinet = new GameObject("cabinet");
            cabinet.transform.SetParent(parent, false);
            cabinet.AddComponent<MeshFilter>().sharedMesh = body;
            MeshRenderer bodyRenderer = cabinet.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterials = bodySrc.GetComponent<MeshRenderer>().sharedMaterials;

            Material[] doorMats = doorSrc.GetComponent<MeshRenderer>().sharedMaterials;
            AddDoor(parent, doorSrc.sharedMesh, doorMats, +1f);
            if (twoDoors) AddDoor(parent, doorSrc.sharedMesh, doorMats, -1f);
            return bodyRenderer;
        }

        /// <summary>A door on its hinge: side +1 is the game's own door (at +x), -1 its mirror image (at -x).</summary>
        private static void AddDoor(Transform parent, Mesh mesh, Material[] mats, float side)
        {
            var hinge = new GameObject($"{DoorPrefix}_{(side > 0 ? "left" : "right")}").transform;
            hinge.SetParent(parent, false);
            hinge.localPosition = new Vector3(side * HingeX, 0f, HingeZ);

            var door = new GameObject("door");
            door.transform.SetParent(hinge, false);
            door.transform.localScale = new Vector3(side, 1f, 1f);                // the mirror image (Unity flips its faces back itself)
            door.transform.localPosition = new Vector3(-side * HingeX, 0f, -HingeZ);   // the mesh's hinge edge on the hinge
            door.AddComponent<MeshFilter>().sharedMesh = mesh;
            door.AddComponent<MeshRenderer>().sharedMaterials = mats;

            // solid, swinging with it: on the hinge, not the door (the mirrored door's scale of -1 doesn't suit a BoxCollider). The door
            // runs from the hinge 0.63 m towards the middle, 2.4 m tall; the panel itself is thinner than its mesh's 0.2 m (the handle).
            hinge.gameObject.layer = LayerMask.NameToLayer("piece");
            BoxCollider box = hinge.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(-side * 0.315f, 1.34f, 0f);
            box.size = new Vector3(0.62f, 2.4f, 0.08f);
        }

        /// <summary>The cabinet's mesh without the right door and the shelves, or the mesh itself if it can't be read.</summary>
        private static Mesh CutBody(Mesh src)
        {
            if (_cut != null) return _cut;
            if (_tried || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return src;
            _tried = true;
            try
            {
                _cut = Cut(src, Remove, out int kept, out int removed);
                Plugin.Log.LogInfo($"Wardrobe cabinet: kept {kept} triangles, cut {removed} (the right door and the shelves)");
                return _cut;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Couldn't read the wardrobe's mesh, so it keeps its one door: " + e.Message);
                return src;
            }
        }

        /// <summary>
        /// Whether a triangle of the cabinet goes: all of it in the right door, or all of it among the shelves but not part of the inside
        /// walls (the walls are cut into strips where the shelves meet them, and those strips lie in the shelves' box too).
        /// </summary>
        private static bool Remove(Vector3 a, Vector3 b, Vector3 c)
        {
            if (RightDoor.Contains(a) && RightDoor.Contains(b) && RightDoor.Contains(c)) return true;
            if (!(Shelves.Contains(a) && Shelves.Contains(b) && Shelves.Contains(c))) return false;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            float minAbsX = Mathf.Min(Mathf.Abs(a.x), Mathf.Min(Mathf.Abs(b.x), Mathf.Abs(c.x)));
            float maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            bool sideWall = Mathf.Abs(n.x) > 0.9f && minAbsX > InnerSide;   // facing sideways, out at a side
            bool backWall = Mathf.Abs(n.z) > 0.9f && maxZ < InnerBack;      // facing front or back, at the back
            return !sideWall && !backWall;
        }

        // the inside faces of the side walls (|x| about 0.66) and the back (z about -0.37), with a little room
        private const float InnerSide = 0.64f, InnerBack = -0.34f;

        /// <summary>
        /// A readable copy of a mesh the game keeps only on the graphics card, without every triangle <paramref name="remove"/>
        /// picks (given its three corners). The vertices are copied byte for byte (whatever their layout), only the triangle list changes.
        /// </summary>
        private static Mesh Cut(Mesh src, Func<Vector3, Vector3, Vector3, bool> remove, out int kept, out int removed)
        {
            if (src.subMeshCount != 1) throw new Exception($"{src.subMeshCount} submeshes");
            SubMeshDescriptor sub = src.GetSubMesh(0);
            if (sub.topology != MeshTopology.Triangles) throw new Exception("not triangles");

            GraphicsBuffer indexBuffer = src.GetIndexBuffer();
            if (indexBuffer == null) throw new Exception("its triangle list isn't on the graphics card");
            byte[] ib = Read(indexBuffer);

            // Only read, never change, the game's mesh: changing a buffer's target (vertexBufferTarget, indexBufferTarget) remakes its
            // buffers from the CPU copy, which a mesh like this hasn't got, and every wardrobe in the game loses its cabinet until restart.

            VertexAttributeDescriptor[] attrs = src.GetVertexAttributes();
            int count = src.vertexCount;
            int posStream = src.GetVertexAttributeStream(VertexAttribute.Position);
            int posOffset = src.GetVertexAttributeOffset(VertexAttribute.Position);
            if (src.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32) throw new Exception("positions not 32-bit floats");

            var dst = new Mesh { name = src.name + "_wardrobeportal" };
            dst.indexFormat = src.indexFormat;
            dst.SetVertexBufferParams(count, attrs);

            var positions = new Vector3[count];
            int streams = src.vertexBufferCount;
            for (int s = 0; s < streams; s++)
            {
                byte[] data = Read(src.GetVertexBuffer(s));
                int stride = src.GetVertexBufferStride(s);
                if (data.Length < stride * count) throw new Exception($"vertex stream {s}: {data.Length} bytes, wanted {stride * count}");
                dst.SetVertexBufferData(data, 0, 0, stride * count, s, MeshUpdateFlags.DontValidateIndices);
                if (s == posStream)
                    for (int i = 0; i < count; i++)
                    {
                        int o = i * stride + posOffset;
                        positions[i] = new Vector3(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8));
                    }
            }

            bool wide = src.indexFormat == IndexFormat.UInt32;
            int Index(int i) => wide ? (int)BitConverter.ToUInt32(ib, i * 4) : BitConverter.ToUInt16(ib, i * 2);

            var tris = new List<int>(sub.indexCount);
            removed = 0;
            for (int i = sub.indexStart; i + 2 < sub.indexStart + sub.indexCount; i += 3)
            {
                int a = Index(i) + sub.baseVertex, b = Index(i + 1) + sub.baseVertex, c = Index(i + 2) + sub.baseVertex;
                if (remove(positions[a], positions[b], positions[c])) { removed++; continue; }
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            kept = tris.Count / 3;

            dst.SetIndexBufferParams(tris.Count, dst.indexFormat);
            if (wide) dst.SetIndexBufferData(tris.ConvertAll(t => (uint)t), 0, 0, tris.Count);
            else dst.SetIndexBufferData(tris.ConvertAll(t => (ushort)t), 0, 0, tris.Count);
            dst.SetSubMesh(0, new SubMeshDescriptor(0, tris.Count, MeshTopology.Triangles));
            dst.RecalculateBounds();
            dst.UploadMeshData(false);
            return dst;
        }

        private static byte[] Read(GraphicsBuffer buffer)
        {
            using (buffer)
            {
                int bytes = buffer.count * buffer.stride;
                var words = new int[(bytes + 3) / 4];
                buffer.GetData(words);
                var data = new byte[bytes];
                Buffer.BlockCopy(words, 0, data, 0, bytes);
                return data;
            }
        }
    }
}
