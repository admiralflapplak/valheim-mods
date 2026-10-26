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
        public const int Slots = 10;   // places a notice can hang; at most Plugin.MaxNotices of them are used at once

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
            GameObject source = scene.GetPrefab("piece_chest_private");
            if (source == null) { Plugin.Log.LogWarning("The game has no personal chest to copy: no Wanted board this session"); return null; }

            _holder = new GameObject("Wanted_prefabs");
            _holder.SetActive(false);   // nothing under it wakes up: they are prefabs, not objects
            Object.DontDestroyOnLoad(_holder);

            GameObject board = Object.Instantiate(source, _holder.transform);
            board.name = PrefabName;

            Piece piece = board.GetComponent<Piece>();
            piece.m_name = PieceName;
            piece.m_description = "$wanted_board_desc";

            // the personal chest's iron-bound box, opened by anyone, with a wooden chest's room (rewards and handed-in things fit)
            Container box = board.GetComponent<Container>();
            if (box != null)
            {
                box.m_name = "$wanted_box";
                box.m_privacy = Container.PrivacySetting.Public;
                box.m_width = 5;
                box.m_height = 2;
            }

            MoveChest(board);
            try { DressChest(board.transform.Find("wanted_chest"), scene); }
            catch (Exception e) { Plugin.Log.LogWarning("Wanted box look: " + e.Message); }

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
        private const float ChestScale = 1.1f, ChestTurn = 22f;
        private static readonly Vector3 ChestAt = new Vector3(0.5f, 0f, 0.18f);   // in front of the right post, its back corner under the panel (7 cm clear of the post)

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
        private static Material _stain;   // the box's weathered wood (kept: boards standing use it)

        /// <summary>
        /// The box made its own: the personal chest's texture recoloured (its red wood to the weathered brown of the board's planks, its
        /// iron bands to aged bronze) in a copy of its material, and a paper label pinned to its front with a gold coin painted on it,
        /// where you're paid.
        /// </summary>
        private static void DressChest(Transform chest, ZNetScene scene)
        {
            if (chest == null) return;
            foreach (MeshRenderer r in chest.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.name.StartsWith("snow", StringComparison.Ordinal)) continue;
                if (_stain == null)
                {
                    _stain = new Material(r.sharedMaterial) { name = "wanted_box_stain" };
                    Texture2D recoloured = Recolour(r.sharedMaterial.mainTexture);
                    if (recoloured != null) _stain.mainTexture = recoloured;
                    else if (_stain.HasProperty("_Color")) _stain.SetColor("_Color", new Color(0.72f, 0.64f, 0.58f));   // (can't read it: just darker)
                }
                r.sharedMaterial = _stain;
            }

            Material label = Paper.Label(scene);
            if (label == null) return;
            // on the box's front (+z before the box is turned round), between the lid and the upper band, beside the clasp, leaning back with the slope of its side
            var sheet = new GameObject("wanted_label");
            sheet.transform.SetParent(chest, false);
            sheet.transform.localPosition = new Vector3(0.115f, 0.44f, 0.262f);
            sheet.transform.localRotation = Quaternion.Euler(-8f, 180f, 4f);
            sheet.transform.localScale = new Vector3(0.1f, 0.13f, 1f);
            sheet.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            sheet.AddComponent<MeshRenderer>().sharedMaterial = label;
        }

        /// <summary>
        /// A copy of a texture with its wood and its metal coloured apart: grey, unsaturated pixels are metal and go towards aged bronze;
        /// the rest is wood and goes to a weathered brown, each keeping its own light and dark (so the grain and the wear stay). Null when
        /// it can't be read.
        /// </summary>
        private static Texture2D Recolour(Texture source)
        {
            if (source == null) return null;
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);   // reads any texture, readable or not
                RenderTexture.active = target;
                var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true) { name = "wanted_box", wrapMode = source.wrapMode };
                tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                Color[] px = tex.GetPixels();
                var bronze = new Color(0.66f, 0.48f, 0.26f);
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    Color.RGBToHSV(c, out float h, out float sat, out float val);
                    Color to = sat < 0.22f
                        ? Color.Lerp(c, bronze * Mathf.Clamp01(val * 1.25f), 0.6f)                          // metal: aged bronze
                        : Color.HSVToRGB(0.075f, Mathf.Clamp01(sat * 0.72f), Mathf.Clamp01(val * 0.82f));  // wood: weathered brown
                    to.a = c.a;
                    px[i] = to;
                }
                tex.SetPixels(px);
                tex.Apply(true);
                return tex;
            }
            catch (Exception e) { Plugin.Log.LogInfo("Could not recolour the box (" + e.Message + "): it's only darkened"); return null; }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
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
            AddPouch(look, scene, iron);
            AddColliders(board);
        }

        /// <summary>
        /// The ten places a notice can hang (two loose rows of five), each a little askew and a few millimetres off its neighbours'
        /// depth (so two that overlap never flicker). Each has its paper, a hover box, and three pins of which NoticeBoard shows one: a
        /// nail, an arrow shot through the paper, a knife driven through it. On the prefab (the build-menu picture, the ghost) every
        /// other place is shown, with an arrow and a knife among them; a placed board shows the ones it holds.
        /// </summary>
        private static void AddNotices(Transform look, ZNetScene scene, Material iron, System.Random rnd)
        {
            float R(float from, float to) => from + (float)rnd.NextDouble() * (to - from);
            Mesh quad = QuadMesh();
            Transform arrow = scene.GetPrefab("ArrowWood")?.transform.Find("model");
            Transform knife = scene.GetPrefab("KnifeCopper")?.transform.Find("attach/knife");
            for (int i = 0; i < Slots; i++)
            {
                float x = -0.8f + 0.4f * (i % 5) + R(-0.03f, 0.03f), y = (i < 5 ? 1.89f : 1.34f) + R(-0.04f, 0.04f);
                var slot = new GameObject("notice_" + i).transform;
                slot.SetParent(look, false);
                slot.localPosition = new Vector3(x, y, PanelFront - 0.025f - 0.004f * (i % 3));   // clear of the planks: closer flickers at a distance
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
                // (the prefab's own look: the arrow-pinned one a red bounty, the rest each kind in turn)
                paper.AddComponent<MeshRenderer>().sharedMaterial = Paper.For(i == 2 ? Kind.Kill : i == 0 ? Kind.Trophy : (Kind)(i % 4), scene);

                var top = new Vector3(0f, Paper.Height / 2f - 0.045f, 0f);   // where it's pinned
                var nail = new GameObject("pin_nail");
                nail.transform.SetParent(slot, false);
                if (iron != null)
                {
                    var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(head.GetComponent<Collider>());
                    head.name = "nail";
                    head.transform.SetParent(nail.transform, false);
                    head.transform.localPosition = top;   // half in the paper, half out
                    head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    head.transform.localScale = new Vector3(0.022f, 0.012f, 0.022f);
                    head.GetComponent<MeshRenderer>().sharedMaterial = iron;
                }
                // shot in from the front, a little from above and to one side: the shaft stands out of the board towards the player
                GameObject a = arrow != null ? Stuck(slot, "pin_arrow", arrow, 2, false, 0.75f, 0.08f, top,
                    Quaternion.Euler(R(8f, 14f), R(-12f, 12f), 0f)) : null;
                // driven in by hand, blade first, the grip standing out and tipped a little down
                GameObject k = knife != null ? Stuck(slot, "pin_knife", knife, 1, true, 0.32f, 0.13f, top,
                    Quaternion.Euler(R(-12f, -6f), R(-8f, 8f), R(0f, 360f))) : null;

                // the prefab's own look: every other place, one with an arrow and one with a knife
                slot.gameObject.SetActive(i % 2 == 0);
                Pin shown = i == 2 ? Pin.Arrow : i == 6 ? Pin.Knife : Pin.Nail;
                nail.SetActive(shown == Pin.Nail);
                if (a != null) a.SetActive(shown == Pin.Arrow);
                if (k != null) k.SetActive(shown == Pin.Knife);
            }
        }

        /// <summary>
        /// A game item's model driven point first into the board through a notice: <paramref name="axis"/> (0 x, 1 y, 2 z) is the
        /// model's length, its point at that axis' high end or low end; it's scaled to <paramref name="length"/> metres, its point
        /// <paramref name="buried"/> metres into the wood under <paramref name="at"/>, and leant by <paramref name="lean"/> from
        /// straight in.
        /// </summary>
        private static GameObject Stuck(Transform slot, string name, Transform source, int axis, bool pointHigh, float length, float buried,
            Vector3 at, Quaternion lean)
        {
            var pin = new GameObject(name);
            pin.transform.SetParent(slot, false);
            Transform model = Copy(pin.transform, "model", source);
            Bounds b = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            var along = Vector3.zero;
            along[axis] = pointHigh ? 1f : -1f;
            Vector3 point = b.center + Vector3.Scale(along, b.extents);
            float scale = length / b.size[axis];
            Quaternion turn = lean * Quaternion.FromToRotation(along, Vector3.forward);   // its point into the board (+z)
            model.localRotation = turn;
            model.localScale = Vector3.one * scale;
            var into = at + lean * Vector3.forward * (0.025f + buried);   // through the paper's gap to the planks, then into them
            model.localPosition = into - turn * (point * scale);
            return pin;
        }

        /// <summary>
        /// An iron hook driven into the front of the sill at the board's left end (the box is on the right), and the coin pouch hanging
        /// from it under the board, clear of the notices: the pouch is shown while a reward waits in the box (NoticeBoard switches it),
        /// hidden on the prefab. The hook stays, as a board would have one.
        /// </summary>
        private static void AddPouch(Transform look, ZNetScene scene, Material iron)
        {
            // out of the sill's front, rising a little to its tip, far enough out that the bag hangs clear of the wood
            var root = new Vector3(-0.62f, PanelBottom - 0.06f, PanelFront + 0.01f);
            var tip = new Vector3(-0.62f, PanelBottom - 0.04f, PanelFront - 0.17f);
            if (iron != null)
            {
                var hook = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(hook.GetComponent<Collider>());
                hook.name = "wanted_hook";
                hook.transform.SetParent(look, false);
                hook.transform.localPosition = (root + tip) / 2f;
                hook.transform.localRotation = Quaternion.FromToRotation(Vector3.up, tip - root);
                hook.transform.localScale = new Vector3(0.018f, (tip - root).magnitude / 2f, 0.018f);   // (the cylinder is 2 m tall)
                hook.GetComponent<MeshRenderer>().sharedMaterial = iron;
            }

            var pouch = new GameObject("wanted_pouch").transform;
            pouch.SetParent(look, false);
            pouch.localPosition = tip + new Vector3(0f, -0.01f, 0.03f);   // the loop over the hook, near its tip
            pouch.localRotation = Quaternion.Euler(0f, 15f, 0f);
            pouch.localScale = Vector3.one * 2.2f;   // (the model is a belt pouch: a bit bigger reads from across a yard)
            if (PouchModel.Make(pouch, scene) == null) Plugin.Log.LogWarning("No material for the coin pouch: the board shows none");
            pouch.gameObject.SetActive(false);
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
