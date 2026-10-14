using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HeaveHo
{
    /// <summary>
    /// Minimal Wavefront OBJ loader for a single static prop (the oar). Reads v / vt / vn / f, triangulates
    /// polygons as fans, and converts Blender's right-handed coordinates to Unity's left-handed ones.
    /// Groups, objects and multiple materials are flattened into one mesh.
    /// </summary>
    internal static class ObjLoader
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static Mesh Load(string path)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();

            var outVerts = new List<Vector3>();
            var outUvs = new List<Vector2>();
            var outNormals = new List<Vector3>();
            var tris = new List<int>();
            bool hasNormals = true;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "v":
                        // Blender OBJ (Forward -Z, Up Y) -> Unity: mirror Z to switch handedness.
                        positions.Add(new Vector3(F(p[1]), F(p[2]), -F(p[3])));
                        break;
                    case "vt":
                        uvs.Add(new Vector2(F(p[1]), p.Length > 2 ? F(p[2]) : 0f));
                        break;
                    case "vn":
                        normals.Add(new Vector3(F(p[1]), F(p[2]), -F(p[3])));
                        break;
                    case "f":
                    {
                        int first = outVerts.Count;
                        int corners = p.Length - 1;
                        for (int c = 1; c < p.Length; c++)
                        {
                            string[] idx = p[c].Split('/');
                            outVerts.Add(positions[Index(idx[0], positions.Count)]);

                            outUvs.Add(idx.Length > 1 && idx[1].Length > 0
                                ? uvs[Index(idx[1], uvs.Count)]
                                : Vector2.zero);

                            if (idx.Length > 2 && idx[2].Length > 0)
                                outNormals.Add(normals[Index(idx[2], normals.Count)]);
                            else
                                hasNormals = false;
                        }

                        // Fan triangulation; winding reversed because Z was mirrored.
                        for (int t = 1; t < corners - 1; t++)
                        {
                            tris.Add(first);
                            tris.Add(first + t + 1);
                            tris.Add(first + t);
                        }
                        break;
                    }
                }
            }

            if (tris.Count == 0)
                throw new InvalidDataException("OBJ has no faces");

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (outVerts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(outVerts);
            mesh.SetUVs(0, outUvs);
            mesh.SetTriangles(tris, 0);
            if (hasNormals && outNormals.Count == outVerts.Count) mesh.SetNormals(outNormals);
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Texture named in the .mtl (map_Kd), else oar.png / oar.jpg next to the OBJ.</summary>
        internal static Texture2D LoadTexture(string objPath)
        {
            string dir = Path.GetDirectoryName(objPath) ?? "";
            var candidates = new List<string>();

            string mtl = Path.ChangeExtension(objPath, ".mtl");
            if (File.Exists(mtl))
            {
                foreach (string raw in File.ReadAllLines(mtl))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("map_Kd", StringComparison.OrdinalIgnoreCase)) continue;
                    // Blender may write an absolute path; we only trust the file name next to the OBJ.
                    string file = line.Substring(6).Trim().Replace('\\', '/');
                    candidates.Add(Path.Combine(dir, Path.GetFileName(file)));
                }
            }

            string stem = Path.GetFileNameWithoutExtension(objPath);
            candidates.Add(Path.Combine(dir, stem + ".png"));
            candidates.Add(Path.Combine(dir, stem + ".jpg"));
            candidates.Add(Path.Combine(dir, stem + ".jpeg"));

            foreach (string file in candidates)
            {
                if (!File.Exists(file)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileName(file) };
                if (!tex.LoadImage(File.ReadAllBytes(file))) continue;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            return null;
        }

        private static float F(string s) => float.Parse(s, NumberStyles.Float, Inv);

        // OBJ indices are 1-based; negative values count back from the end.
        private static int Index(string s, int count)
        {
            int i = int.Parse(s, Inv);
            return i > 0 ? i - 1 : count + i;
        }
    }
}
