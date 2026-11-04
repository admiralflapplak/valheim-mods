using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The Cogling's model, as tools/convert_robot.py wrote it into assets/cogling.bin (embedded in the DLL): its Mixamo skeleton in its
    /// rest T-pose and one skinned mesh, plus its texture. Read once and shared by every Cogling (the mesh, texture and bind poses never
    /// change; each Cogling makes its own bones).
    /// </summary>
    internal static class CoglingModel
    {
        internal static string[] BoneNames;
        internal static int[] Parents;
        internal static Vector3[] RestPos;      // in the model's space: metres, feet on y = 0, facing +z
        internal static Quaternion[] RestRot;
        internal static Mesh Mesh;               // bind poses from the rest pose above
        internal static Texture2D Texture;
        internal static Texture2D CopperDetail;  // the copper parts' shading (rivets, edges) in grey, for the game's copper material
        internal static Texture2D Glow;          // white where the eyes are, black elsewhere: the wood material's emission map (colour: Eye colour)
        private static Color32[] _darkEyes;      // the texture with the eyes' painted green made dark glass (the glow gives their colour)
        internal static float Height;            // its height in metres (Plugin.Height when it was loaded)
        internal static float Size = 1f;         // the scale the model file was loaded at (animations made for it use the same)

        internal static bool Load()
        {
            if (Mesh != null) return true;
            var assembly = typeof(CoglingModel).Assembly;
            bool draws = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;   // a dedicated server draws nothing
            Color32[] texels = null;
            if (draws)
            {
                Texture = LoadTexture(assembly, "Cogling.assets.cogling.png");
                if (Texture != null) { texels = Texture.GetPixels32(); MakeMaps(texels, Texture.width, Texture.height); }
            }
            using (Stream s = assembly.GetManifestResourceStream("Cogling.assets.cogling.bin"))
            {
                if (s == null) { Plugin.Log.LogError("The Cogling's model isn't in the DLL"); return false; }
                var r = new BinaryReader(s);
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "COG1") { Plugin.Log.LogError("The embedded Cogling model isn't one this version reads"); return false; }

                int bones = r.ReadInt32();
                BoneNames = new string[bones];
                Parents = new int[bones];
                RestPos = new Vector3[bones];
                RestRot = new Quaternion[bones];
                for (int i = 0; i < bones; i++)
                {
                    BoneNames[i] = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                    Parents[i] = r.ReadInt32();
                    RestPos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    RestRot[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                }

                int n = r.ReadInt32();
                var pos = new Vector3[n];
                var normal = new Vector3[n];
                var uv = new Vector2[n];
                var weights = new BoneWeight[n];
                float top = 0f;
                for (int i = 0; i < n; i++)
                {
                    pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    normal[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    weights[i] = new BoneWeight
                    {
                        boneIndex0 = r.ReadInt32(), weight0 = r.ReadSingle(),
                        boneIndex1 = r.ReadInt32(), weight1 = r.ReadSingle(),
                        boneIndex2 = r.ReadInt32(), weight2 = r.ReadSingle(),
                        boneIndex3 = r.ReadInt32(), weight3 = r.ReadSingle(),
                    };
                    top = Mathf.Max(top, pos[i].y);
                }
                // the model made Plugin.Height tall (its skeleton too), so the robot's root needs no scale
                float size = Plugin.Height.Value / Mathf.Max(0.01f, top);
                Size = size;
                for (int i = 0; i < n; i++) pos[i] *= size;
                for (int i = 0; i < bones; i++) RestPos[i] *= size;
                top *= size;
                int[] tris = new int[r.ReadInt32()];
                for (int i = 0; i < tris.Length; i++) tris[i] = r.ReadInt32();

                // bind poses: each bone's rest place (in the model's space) inverted
                var bind = new Matrix4x4[bones];
                for (int i = 0; i < bones; i++) bind[i] = Matrix4x4.TRS(RestPos[i], RestRot[i], Vector3.one).inverse;

                Mesh = new Mesh { name = "cogling" };
                Mesh.vertices = pos;
                Mesh.normals = normal;
                Mesh.uv = uv;
                Mesh.boneWeights = weights;
                Mesh.bindposes = bind;
                // two parts: wood (submesh 0, the creature material) and copper (submesh 1, the game's copper), by the texture under
                // each triangle (Meshy paints each plate as its own island, so a triangle is all one or the other)
                var wood = new List<int>(tris.Length);
                var copper = new List<int>(tris.Length / 2);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    bool isCopper = texels != null && IsCopper(texels, Texture.width, Texture.height, uv[tris[t]], uv[tris[t + 1]], uv[tris[t + 2]]);
                    (isCopper ? copper : wood).AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                }
                Mesh.subMeshCount = 2;
                Mesh.SetTriangles(wood, 0);
                Mesh.SetTriangles(copper, 1);
                Plugin.Log.LogInfo($"Cogling model: {wood.Count / 3} wood triangles, {copper.Count / 3} copper");
                // (after the sorting above, which tells the eyes by their painted green)
                if (_darkEyes != null && Texture != null) { Texture.SetPixels32(_darkEyes); Texture.Apply(true); }
                _darkEyes = null;
                Mesh.RecalculateBounds();
                Mesh.RecalculateTangents();
                Mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Height = top;
            }
            return true;
        }

        // Copper and wood in the texture (measured: wood is dark and strong, brightness ~0.2, saturation ~0.65; copper plates are light
        // and muted, ~0.42 and ~0.45): copper where brightness - saturation / 2 is over 0.06. Eyes: clearly more green than red.
        private static float CopperScore(Color32 c)
        {
            Color.RGBToHSV(c, out _, out float sat, out float val);
            return val - sat * 0.5f;
        }

        // (and the eye glass's dark green shading: barely greener than red, but greener than both red and blue; the wood around is
        // brown, redder than green. Without it, dark edges of the eyes stayed unlit)
        private static bool IsEye(Color32 c) => c.g > c.r + 30 || (c.g >= c.r + 8 && c.g >= c.b + 8);

        private static bool IsCopper(Color32[] px, int w, int h, Vector2 a, Vector2 b, Vector2 c)
        {
            // the middle and partway to each corner: most of the triangle's own island
            Vector2 mid = (a + b + c) / 3f;
            float score = 0f;
            int n = 0;
            foreach (Vector2 p in new[] { mid, Vector2.Lerp(mid, a, 0.6f), Vector2.Lerp(mid, b, 0.6f), Vector2.Lerp(mid, c, 0.6f) })
            {
                Color32 t = px[Mathf.Clamp((int)(p.y * h), 0, h - 1) * w + Mathf.Clamp((int)(p.x * w), 0, w - 1)];
                if (t.r + t.g + t.b < 30 || IsEye(t)) continue;   // gaps between islands, and the eyes (they stay on the wood material)
                score += CopperScore(t);
                n++;
            }
            return n > 0 && score / n > 0.06f;
        }

        /// <summary>The copper's grey shading and the eyes' glow, from the texture.</summary>
        private static void MakeMaps(Color32[] px, int w, int h)
        {
            var detail = new Color32[px.Length];
            var glow = new Color32[px.Length];
            const float copperLight = 0.42f;   // the copper plates' usual brightness: shown as white, so the game's copper colour shows true
            bool eyes = false;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                float light = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                byte grey = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Mathf.Pow(light / copperLight, 0.8f) * 0.9f), 0, 255);
                detail[i] = new Color32(grey, grey, grey, 255);
                if (IsEye(c))
                {
                    // the eyes in white for the glow (its colour is the Eye colour setting), and dark glass in the texture itself, so
                    // the painted green doesn't show under another colour
                    byte bright = (byte)Mathf.Clamp(Mathf.Max(c.g + 40, 190), 0, 255);   // (the whole eye lit, its dark shading too)
                    glow[i] = new Color32(bright, bright, bright, 255);
                    byte dark = (byte)(c.g / 5);
                    _darkEyes = _darkEyes ?? (Color32[])px.Clone();
                    _darkEyes[i] = new Color32(dark, dark, dark, 255);
                    eyes = true;
                }
                else glow[i] = new Color32(0, 0, 0, 255);
            }
            if (!eyes) _darkEyes = null;
            CopperDetail = Make(detail, w, h, "cogling_copper");
            Glow = Make(glow, w, h, "cogling_glow");
        }

        private static Texture2D Make(Color32[] px, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true, true);   // mipmaps, and no copy kept in memory
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        private static Texture2D LoadTexture(System.Reflection.Assembly assembly, string resource)
        {
            using (Stream s = assembly.GetManifestResourceStream(resource))
            {
                if (s == null) return null;
                var m = new MemoryStream();
                s.CopyTo(m);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "cogling", wrapMode = TextureWrapMode.Clamp };
                // (ImageConversion.LoadImage, called by name: its module is built for a newer .NET than this project compiles against)
                System.Type conversion = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                conversion?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) })?.Invoke(null, new object[] { tex, m.ToArray() });
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
        }

        /// <summary>Lets go of the shared model (hot reload: statics live on in the old copy).</summary>
        internal static void Forget()
        {
            Mesh = null;
            Texture = CopperDetail = Glow = null;
            BoneNames = null;
        }
    }
}
