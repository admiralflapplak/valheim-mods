using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wanted
{
    /// <summary>
    /// The Wanted board: a copy of the game's wooden chest (its box is where notices are handed in: its storage, lid, sounds and wear come
    /// with it) with a notice board behind it, built from the game's own log poles and planks, and room for five notices. Registered by
    /// name through PieceReload (safe to repeat; a hot reload replaces it without the game ever missing the prefab).
    /// </summary>
    internal static class Board
    {
        public const string PrefabName = "nick_wanted_board";
        public const string PieceName = "$wanted_board";
        public const int Slots = 5;

        internal static GameObject Prefab;
        private static PieceTable _table;   // the build menu it was put on, kept so an unload can take it off again at the main menu
        private static GameObject _holder;  // inactive parent of the prefab: lives as long as the game does (an old copy's board stays valid)
        private static Material _flatPlanks; // the panel's wood, without the game's vertex wobble

        /// <summary>Registers (and builds, the first time) the board in the scene and the hammer's build menu. Safe to repeat.</summary>
        public static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab == null) Prefab = Build(scene);
            if (Prefab == null) return;

            GameObject hammer = scene.GetPrefab("Hammer");
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null) _table = table;
            PieceReload.ReplacePrefab(scene, Prefab, table != null ? table : _table);
            int swapped = PieceReload.Reattach(scene, Prefab);   // boards already standing: this copy's NoticeBoard, papers and faces
            if (swapped > 0) Plugin.Log.LogInfo($"Gave {swapped} standing board(s) this copy's components");
            ApplyCost();
            Plugin.Synced.NeedsServer(Prefab);
        }

        /// <summary>The cost and station from the settings, onto the piece (a mistake in either keeps the last good one).</summary>
        public static void ApplyCost()
        {
            Piece piece = Prefab != null ? Prefab.GetComponent<Piece>() : null;
            if (piece == null) return;
            Costs.Apply(piece, Plugin.BoardCost.Value, Plugin.Log);
            Costs.ApplyStation(piece, Plugin.BoardStation.Value, Plugin.Log);
        }

        /// <summary>Takes the board off the build menu when this copy unloads. The prefab stays registered: no area loading meanwhile loses a board.</summary>
        public static void Unload() => PieceReload.UnloadPiece(Prefab, _table);

        public static void Forget()
        {
            Prefab = null;   // the old prefab and its holder stay alive for boards already standing; the next copy replaces it by name
            _table = null;
            _holder = null;
            Paper.Forget();
            _flatPlanks = null;   // not destroyed: the old prefab and the boards standing still show it (as the prefab itself is kept)
        }

        // ---- the prefab ------------------------------------------------------------------------------------------

        private static GameObject Build(ZNetScene scene)
        {
            GameObject source = scene.GetPrefab("piece_chest_wood");
            if (source == null) { Plugin.Log.LogWarning("The game has no wooden chest to copy: no Wanted board this session"); return null; }

            _holder = new GameObject("Wanted_prefabs");
            _holder.SetActive(false);   // nothing under it wakes up: they are prefabs, not objects
            Object.DontDestroyOnLoad(_holder);

            GameObject board = Object.Instantiate(source, _holder.transform);
            board.name = PrefabName;

            Piece piece = board.GetComponent<Piece>();
            piece.m_name = PieceName;
            piece.m_description = "$wanted_board_desc";

            Container box = board.GetComponent<Container>();
            if (box != null) box.m_name = "$wanted_box";

            MoveChest(board);

            try { AddLook(board, scene); }
            catch (Exception e) { Plugin.Log.LogWarning("Wanted board look: " + e); }
            board.AddComponent<NoticeBoard>();   // (after the look: it finds the notice slots by name)

            try { piece.m_icon = Icon.Make(board, fallback: null); }
            catch (Exception e) { Plugin.Log.LogWarning("Wanted board icon: " + e.Message); }
            if (piece.m_icon == null) piece.m_icon = source.GetComponent<Piece>().m_icon;
            return board;
        }

        // ---- the look ---------------------------------------------------------------------------------------------
        // Metres from the piece's origin (the middle of its base); the front, where the player stands, faces -z. The board stands on two
        // log posts, its planks nailed across their fronts, a little plank roof resting on the posts' tops; the chest stands in front of it,
        // smaller, turned to face the player and set off to one side at an angle, so it doesn't hide the board.

        private const float PostX = 0.98f, PostZ = 0.52f, PostHeight = 3.0f, PostRadius = 0.15f;
        private const float PostSink = 0.3f;   // how far the posts go into the ground, so they stand in it on uneven ground
        private const float ChestScale = 0.8f, ChestTurn = 22f;
        private static readonly Vector3 ChestAt = new Vector3(0.55f, 0f, 0.05f);   // in front of the right post, its back corner under the panel

        /// <summary>
        /// The chest's model and its collider move under one child, turned round (the game's chest faces +z, our front is -z), scaled down
        /// and set off to the side. Its snap points go: they were made for a chest standing in the middle of the piece.
        /// </summary>
        private static void MoveChest(GameObject board)
        {
            var chest = new GameObject("wanted_chest").transform;
            chest.SetParent(board.transform, false);
            foreach (Transform child in board.transform.Cast<Transform>().ToList())
            {
                if (child == chest) continue;
                if (child.CompareTag("snappoint")) { Object.DestroyImmediate(child.gameObject); continue; }
                child.SetParent(chest, false);
            }
            chest.localPosition = ChestAt;
            chest.localRotation = Quaternion.Euler(0f, 180f + ChestTurn, 0f);
            chest.localScale = Vector3.one * ChestScale;
        }
        private const float PanelBottom = 1.0f, PanelTop = 2.2f, PlankDepth = 0.05f;
        private static float PanelFront => PostZ - PostRadius - PlankDepth;   // the planks' front face

        private static void AddLook(GameObject board, ZNetScene scene)
        {
            Transform log = scene.GetPrefab("wood_pole_log")?.transform.Find("New/log (1)");
            Transform plank = scene.GetPrefab("wood_beam")?.transform.Find("new");
            if (log == null || plank == null) { Plugin.Log.LogWarning("The game's log pole or beam has changed: the board is only its box"); return; }
            Material iron = MaterialOf(scene, "Iron") ?? MaterialOf(scene, "Bronze");

            var look = new GameObject("wanted_look").transform;
            look.SetParent(board.transform, false);
            var rnd = new System.Random(7);   // hand-made, the same way every time
            float R(float from, float to) => from + (float)rnd.NextDouble() * (to - from);

            // the posts: the game's log, thinned and a little taller, leaning a touch
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var post = Copy(look, "post", log);
                post.localPosition = new Vector3(side * PostX, (PostHeight - PostSink) / 2f, PostZ);
                post.localRotation = Quaternion.Euler(R(-1.5f, 1.5f), R(0f, 360f), R(-1.5f, 1.5f)) * Quaternion.Euler(90f, 90f, 0f);
                post.localScale = new Vector3(PostRadius * 2f / 0.55f, PostRadius * 2f / 0.52f, (PostHeight + PostSink) / 2.22f);   // the log mesh is 0.55 x 0.52 x 2.22
            }

            // the panel: four planks across the posts' fronts, each a little different, with gaps between
            const int planks = 4;
            float step = (PanelTop - PanelBottom) / planks;
            for (int i = 0; i < planks; i++)
            {
                var p = Copy(look, "plank", plank);
                Flatten(p);
                p.localPosition = new Vector3(R(-0.04f, 0.04f), PanelBottom + step * (i + 0.5f), PanelFront + PlankDepth / 2f + R(-0.008f, 0.008f));
                p.localRotation = Quaternion.Euler(0f, 0f, R(-0.8f, 0.8f));
                p.localScale = new Vector3(PostX * 2f + R(0.2f, 0.32f), step - R(0.015f, 0.03f), PlankDepth);
                Fit(p);
            }

            // a beam along the top of the panel, and a sill under it
            Flatten(Beam(look, plank, new Vector3(0f, PanelTop + 0.05f, PanelFront + 0.02f), new Vector3(PostX * 2f + 0.5f, 0.12f, 0.14f), R(-0.6f, 0.6f)));
            Flatten(Beam(look, plank, new Vector3(0f, PanelBottom - 0.05f, PanelFront + 0.03f), new Vector3(PostX * 2f + 0.36f, 0.1f, 0.12f), R(-0.6f, 0.6f)));

            // the roof: two rows of planks each side of a ridge resting on the posts' tips, overhanging front and back, clear of the panel
            const float ridgeY = PostHeight - 0.03f, slope = 38f, rowLength = 0.36f;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float dir = sideIndex == 0 ? -1f : 1f;   // -1: the front slope
                for (int row = 0; row < 2; row++)
                {
                    float along = rowLength * (row + 0.5f) - 0.02f;   // down the slope from the ridge
                    var r = Copy(look, "roof", plank);
                    r.localPosition = new Vector3(R(-0.03f, 0.03f), ridgeY - along * Mathf.Sin(slope * Mathf.Deg2Rad) + row * 0.025f,
                        PostZ + dir * along * Mathf.Cos(slope * Mathf.Deg2Rad));
                    r.localRotation = Quaternion.Euler(dir * slope + R(-1f, 1f), 0f, R(-0.6f, 0.6f));
                    r.localScale = new Vector3(PostX * 2f + 0.62f, 0.035f, rowLength + 0.06f);
                    Fit(r);
                }
            }
            Beam(look, plank, new Vector3(0f, ridgeY + 0.02f, PostZ), new Vector3(PostX * 2f + 0.7f, 0.1f, 0.1f), 0f);

            AddNotices(look, scene, iron, rnd);
            AddPouch(look, scene);
            AddColliders(board);
        }

        /// <summary>The five places a notice can hang (three across the top, two below), each a little askew. All shown on the prefab (the build-menu picture); NoticeBoard shows the ones a placed board holds.</summary>
        private static void AddNotices(Transform look, ZNetScene scene, Material iron, System.Random rnd)
        {
            float R(float from, float to) => from + (float)rnd.NextDouble() * (to - from);
            var places = new[]
            {
                new Vector2(-0.6f, 1.86f), new Vector2(0f, 1.9f), new Vector2(0.6f, 1.85f),
                new Vector2(-0.31f, 1.38f), new Vector2(0.33f, 1.36f),
            };
            Mesh quad = QuadMesh();
            for (int i = 0; i < places.Length; i++)
            {
                var slot = new GameObject("notice_" + i).transform;
                slot.SetParent(look, false);
                slot.localPosition = new Vector3(places[i].x + R(-0.03f, 0.03f), places[i].y + R(-0.03f, 0.03f), PanelFront - 0.025f);   // clear of the planks: closer flickers at a distance
                slot.localRotation = Quaternion.Euler(0f, 0f, R(-7f, 7f));
                slot.gameObject.layer = look.parent.gameObject.layer;   // (the piece's layer: the one the player's hover ray looks at)
                var hit = slot.gameObject.AddComponent<BoxCollider>();   // in front of the panel's, so the crosshair finds the paper first
                hit.center = new Vector3(0f, 0f, -0.005f);
                hit.size = new Vector3(Paper.Width, Paper.Height, 0.02f);
                slot.gameObject.AddComponent<NoticePaper>();

                var paper = new GameObject("paper");
                paper.transform.SetParent(slot, false);
                paper.transform.localScale = new Vector3(Paper.Width, Paper.Height, 1f);
                paper.AddComponent<MeshFilter>().sharedMesh = quad;
                paper.AddComponent<MeshRenderer>().sharedMaterial = Paper.For((Kind)(i % 4), scene);

                if (iron != null)
                {
                    var nail = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(nail.GetComponent<Collider>());
                    nail.name = "nail";
                    nail.transform.SetParent(slot, false);
                    nail.transform.localPosition = new Vector3(0f, Paper.Height / 2f - 0.04f, 0f);   // half in the paper, half out
                    nail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    nail.transform.localScale = new Vector3(0.022f, 0.012f, 0.022f);
                    nail.GetComponent<MeshRenderer>().sharedMaterial = iron;
                }
            }
        }

        /// <summary>
        /// A little heap of the game's coins on the sill, under the notices on the box's side: shown while a reward waits in the box
        /// (NoticeBoard switches it). Hidden on the prefab.
        /// </summary>
        private static void AddPouch(Transform look, ZNetScene scene)
        {
            Transform coins = scene.GetPrefab("Coins")?.transform.Find("attach");
            if (coins == null) return;
            var pouch = Object.Instantiate(coins.gameObject, look, false);
            pouch.name = "wanted_pouch";
            foreach (Collider c in pouch.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            pouch.transform.localPosition = new Vector3(0.5f, PanelBottom + 0.002f, PanelFront - 0.02f);   // on the sill's top
            pouch.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            pouch.transform.localScale = Vector3.one * 0.55f;
            pouch.SetActive(false);
        }

        /// <summary>Boxes the player and the building checks bump into: the posts and the panel with its roof (the chest keeps its own).</summary>
        private static void AddColliders(GameObject board)
        {
            void Box(string name, Vector3 centre, Vector3 size)
            {
                var go = new GameObject(name) { layer = board.layer };
                go.transform.SetParent(board.transform, false);
                var box = go.AddComponent<BoxCollider>();
                box.center = centre;
                box.size = size;
                go.AddComponent<BoardFace>();
            }
            Box("wanted_collider_post_l", new Vector3(-PostX, PostHeight / 2f, PostZ), new Vector3(PostRadius * 2f, PostHeight, PostRadius * 2f));
            Box("wanted_collider_post_r", new Vector3(PostX, PostHeight / 2f, PostZ), new Vector3(PostRadius * 2f, PostHeight, PostRadius * 2f));
            Box("wanted_collider_panel", new Vector3(0f, (PanelBottom + PanelTop) / 2f, PanelFront + PlankDepth / 2f),
                new Vector3(PostX * 2f + 0.3f, PanelTop - PanelBottom + 0.2f, PlankDepth + 0.04f));
            Box("wanted_collider_roof", new Vector3(0f, PostHeight - 0.2f, PostZ), new Vector3(PostX * 2f + 0.6f, 0.4f, 0.9f));
        }

        /// <summary>
        /// The panel's wood without the game's vertex wobble: Custom/Piece moves every vertex up to 5 cm by a noise taken from where it
        /// stands in the world, so on boards facing some ways the planks bulged through the notices pinned on them.
        /// </summary>
        private static void Flatten(Transform part)
        {
            var r = part.GetComponent<MeshRenderer>();
            if (_flatPlanks == null)
            {
                _flatPlanks = new Material(r.sharedMaterial) { name = "wanted_planks" };
                if (_flatPlanks.HasProperty("_ValueNoiseVertex")) _flatPlanks.SetFloat("_ValueNoiseVertex", 0f);
                if (_flatPlanks.HasProperty("_RippleDistance")) _flatPlanks.SetFloat("_RippleDistance", 0f);
                _flatPlanks.DisableKeyword("_VALUENOISEVERTEX_ON");
            }
            r.sharedMaterial = _flatPlanks;
        }

        private static Transform Beam(Transform parent, Transform plank, Vector3 at, Vector3 size, float tilt)
        {
            var b = Copy(parent, "beam", plank);
            b.localPosition = at;
            b.localRotation = Quaternion.Euler(0f, 0f, tilt);
            b.localScale = size;
            Fit(b);
            return b;
        }

        /// <summary>
        /// Read the part's position and scale as where its middle should be and how big it should be, in metres, and make its mesh so: the
        /// game's cube meshes are neither exactly 1 m nor centred on their origin, and a plank's face a centimetre off where we think it is
        /// cuts into the notices on it.
        /// </summary>
        private static void Fit(Transform t)
        {
            Bounds b = t.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 size = t.localScale;
            var scale = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
            t.localScale = scale;
            t.localPosition -= t.localRotation * Vector3.Scale(scale, b.center);
        }

        /// <summary>A new object showing a game part's mesh in its materials (no colliders, no scripts).</summary>
        private static Transform Copy(Transform parent, string name, Transform source)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            return go.transform;
        }

        /// <summary>A 1 x 1 quad facing -z (towards the player), its UVs the whole texture.</summary>
        private static Mesh QuadMesh()
        {
            var m = new Mesh { name = "wanted_quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The material of a game item's model (the game's own look; Shader.Find would not have the shaders it strips).</summary>
        private static Material MaterialOf(ZNetScene scene, string itemPrefab)
        {
            GameObject item = scene.GetPrefab(itemPrefab);
            MeshRenderer r = item != null ? item.GetComponentInChildren<MeshRenderer>(true) : null;
            return r != null ? r.sharedMaterial : null;
        }

        // The host deletes a placed piece it doesn't know, so the board goes into ZNetScene as early as the scene exists.
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance)
            {
                try { Register(__instance); }
                catch (Exception e) { Plugin.Log.LogError("Registering the Wanted board failed: " + e); }
            }
        }
    }
}
