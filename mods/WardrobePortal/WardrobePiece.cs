using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WardrobePortal
{
    /// <summary>
    /// The Wardrobe Portal: a copy of the game's wooden portal (its TeleportWorld, trigger, swirl, sounds, wear and the game's own pairing
    /// by tag all come with it) wearing the Deep North wardrobe's model instead of the stone-and-wood arch. Registered by name from the
    /// scene (safe to repeat), and its name added to the game's list of portal prefabs so worlds load and pair it as a portal.
    /// </summary>
    internal static class WardrobePiece
    {
        public const string PrefabName = "nick_wardrobeportal";
        public const string PieceName = "$wardrobeportal_piece";
        private const string PortalSource = "portal_wood";
        private const string WardrobeSource = "piece_chest_warderobe";

        internal static GameObject Prefab;
        private static PieceTable _table;   // the build menu it was put on, kept so an unload can take it off at the main menu
        private static GameObject _holder;  // inactive parent of the prefab: nothing under it wakes up

        public static int Hash => PrefabName.GetStableHashCode();

        // ZNetScene's lookup by name hash is private
        private static readonly AccessTools.FieldRef<ZNetScene, System.Collections.Generic.Dictionary<int, GameObject>> NamedPrefabs =
            AccessTools.FieldRefAccess<ZNetScene, System.Collections.Generic.Dictionary<int, GameObject>>("m_namedPrefabs");

        /// <summary>Builds (the first time) and registers the piece in the scene and on the hammer's build menu. Safe to repeat.</summary>
        public static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab == null) Prefab = Build(scene);
            if (Prefab == null) return;

            if (!scene.m_prefabs.Contains(Prefab)) scene.m_prefabs.Add(Prefab);
            NamedPrefabs(scene)[Hash] = Prefab;

            GameObject hammer = scene.GetPrefab("Hammer");
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null)
            {
                table.m_pieces.RemoveAll(p => p == null);   // a destroyed piece left there makes every craft free (valheim-pitfalls)
                if (!table.m_pieces.Contains(Prefab)) table.m_pieces.Add(Prefab);
                _table = table;
            }
            ApplyCost();
            Plugin.Synced.NeedsServer(Prefab);
            AddPortalHash();
        }

        /// <summary>The cost and station from the settings, onto the piece (a mistake in either keeps the last good one).</summary>
        public static void ApplyCost()
        {
            Piece piece = Prefab != null ? Prefab.GetComponent<Piece>() : null;
            if (piece == null) return;
            Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log);
            Costs.ApplyStation(piece, Plugin.Station.Value, Plugin.Log);
        }

        /// <summary>
        /// The game tracks portals (for pairing by tag) by prefab hash, from a list it builds in Game.Awake: ours goes on it too. Kept on
        /// unload: the world's wardrobe portals stay portals until the game closes.
        /// </summary>
        public static void AddPortalHash()
        {
            if (Game.instance != null && !Game.instance.PortalPrefabHash.Contains(Hash)) Game.instance.PortalPrefabHash.Add(Hash);
        }

        /// <summary>Takes the piece off the build menu and out of the scene (the mod reloads at the main menu only).</summary>
        public static void Unload()
        {
            if (_table != null && Prefab != null) _table.m_pieces.Remove(Prefab);
            ZNetScene scene = ZNetScene.instance;
            if (scene != null && Prefab != null)
            {
                scene.m_prefabs.Remove(Prefab);
                if (NamedPrefabs(scene).TryGetValue(Hash, out GameObject p) && p == Prefab) NamedPrefabs(scene).Remove(Hash);
            }
            if (_holder != null) Object.Destroy(_holder);
            Prefab = null;
            _table = null;
            _holder = null;
        }

        // ---- the prefab ------------------------------------------------------------------------------------------

        private static GameObject Build(ZNetScene scene)
        {
            GameObject portal = scene.GetPrefab(PortalSource);
            GameObject wardrobe = scene.GetPrefab(WardrobeSource);
            if (portal == null || wardrobe == null)
            {
                Plugin.Log.LogWarning($"The game has no {(portal == null ? PortalSource : WardrobeSource)} to copy: no Wardrobe Portal this session");
                return null;
            }
            Piece wp = wardrobe.GetComponent<Piece>();   // TEMP: the wardrobe's cost and station, for the default cost
            if (wp != null) Plugin.Log.LogInfo("wardrobe cost: " + string.Join(", ", wp.m_resources.Select(r => $"{r.m_resItem?.name}:{r.m_amount}")) +
                                               $"; station {wp.m_craftingStation?.name}");

            _holder = new GameObject("WardrobePortal_prefabs");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);

            GameObject root = Object.Instantiate(portal, _holder.transform);
            root.name = PrefabName;

            Piece piece = root.GetComponent<Piece>();
            piece.m_name = PieceName;
            piece.m_description = "$wardrobeportal_desc";

            try { Dress(root, wardrobe); }
            catch (Exception e) { Plugin.Log.LogWarning("Wardrobe Portal look: " + e); }

            root.AddComponent<WardrobeDoors>();

            try { BreakLikeAWardrobe(root, wardrobe); }
            catch (Exception e) { Plugin.Log.LogWarning("Wardrobe Portal breaking: " + e.Message); }

            try { piece.m_icon = Icon.Make(root, null, IconPose, fallback: null); }
            catch (Exception e) { Plugin.Log.LogWarning("Wardrobe Portal icon: " + e.Message); }
            if (piece.m_icon == null) piece.m_icon = wardrobe.GetComponent<Piece>()?.m_icon ?? piece.m_icon;
            try   // TEMP: the icon to a file, to look at it
            {
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", "wardrobeportal_icon.png"),
                    ImageConversion.EncodeToPNG(piece.m_icon.texture));
            }
            catch (Exception e) { Plugin.Log.LogWarning("icon dump: " + e.Message); }
            return root;
        }

        // The build-menu picture: the doors open and a swirl inside. Icon.cs lights the model from where the game's pieces show their
        // fronts (-z); ours faces +z, so the copy is turned round first. Icon.cs draws meshes, not particles: the swirl is painted on a
        // plane in the opening, glowing.
        private static void IconPose(GameObject copy)
        {
            copy.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * copy.transform.rotation;
            Transform model = copy.transform.Find("wardrobe");
            if (model == null) return;
            foreach (Transform t in model)
                if (t.name.StartsWith(Cabinet.DoorPrefix, StringComparison.Ordinal))
                    t.localRotation *= Quaternion.Euler(0f, (t.localPosition.x < 0f ? -1f : 1f) * 100f, 0f);

            MeshRenderer cabinet = model.Find("cabinet")?.GetComponent<MeshRenderer>();
            if (cabinet == null || cabinet.sharedMaterial == null) return;
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(plane.GetComponent<Collider>());
            plane.name = "icon_swirl";
            plane.layer = copy.layer;   // Icon.cs's camera sees only the copy's layer, set before the pose
            plane.transform.SetParent(model, false);
            plane.transform.localPosition = new Vector3(0f, 1.32f, 0.05f);
            plane.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // its face towards +z, out of the doors
            plane.transform.localScale = new Vector3(1.22f, 2.2f, 1f);
            // the cabinet's own material (the game's piece shader), its picture the swirl, glowing like the portal's arch does
            Texture2D swirl = SwirlTexture();
            var mat = new Material(cabinet.sharedMaterial) { mainTexture = swirl, color = Color.white };
            mat.SetTexture("_EmissionMap", swirl);
            mat.SetColor("_EmissionColor", Color.white * 1.6f);
            mat.EnableKeyword("_EMISSION");
            plane.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>A painted portal swirl: fiery arms turning in to a dark middle.</summary>
        private static Texture2D SwirlTexture()
        {
            const int w = 64, h = 112;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var deep = new Color(0.55f, 0.08f, 0.02f);
            var bright = new Color(1f, 0.75f, 0.25f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float arms = 0.5f + 0.5f * Mathf.Sin(Mathf.Atan2(v, u) * 3f + r * 9f);
                    Color c = Color.Lerp(deep, bright, arms * Mathf.Clamp01(r * 1.6f));
                    c *= Mathf.SmoothStep(0.05f, 0.45f, r);   // dark in the middle
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Hit and broken, it's a wardrobe: the wardrobe's sounds and splinters, its material and health, and its own model breaking into
        /// pieces (the game makes them from what is drawn) instead of the stone arch's ready-made chunks.
        /// </summary>
        private static void BreakLikeAWardrobe(GameObject root, GameObject wardrobe)
        {
            WearNTear mine = root.GetComponent<WearNTear>();
            WearNTear theirs = wardrobe.GetComponent<WearNTear>();
            if (mine == null || theirs == null) return;
            mine.m_destroyedEffect = theirs.m_destroyedEffect;
            mine.m_hitEffect = theirs.m_hitEffect;
            mine.m_switchEffect = theirs.m_switchEffect;
            mine.m_materialType = theirs.m_materialType;
            mine.m_health = theirs.m_health;
            mine.m_damages = theirs.m_damages;
            mine.m_burnable = theirs.m_burnable;
            mine.m_autoCreateFragments = true;
            mine.m_fragmentRoots = null;
            Transform destruction = root.transform.Find("portal_destruction");
            if (destruction != null) Object.DestroyImmediate(destruction.gameObject);
            // the wear look (new, worn, broken) is the wardrobe's model throughout: it has no worn copy of its own
            GameObject model = root.transform.Find("wardrobe")?.gameObject;
            mine.m_new = model; mine.m_worn = model; mine.m_broken = model;
            Plugin.Log.LogInfo($"wardrobe breaks: health {theirs.m_health}, material {theirs.m_materialType}, its fragments {(theirs.m_autoCreateFragments ? "made by the game" : (theirs.m_fragmentRoots?.Length ?? 0) + " ready-made")}");   // TEMP
        }

        /// <summary>
        /// The arch's own model and solid colliders go (hidden, not destroyed: TeleportWorld keeps a reference to its MeshRenderer); the
        /// portal's working parts (the swirl it shows when a player is near, its trigger, its effects) stay; the wardrobe's model comes in
        /// under "wardrobe" with walls round it that leave the front open, so the player can step in and touch the trigger.
        /// FIRST PASS, written without the game running: which children are which, and where the swirl sits in the wardrobe, are
        /// fitted from LogHierarchy's output in the game.
        /// </summary>
        private static void Dress(GameObject root, GameObject wardrobe)
        {
            TeleportWorld tw = root.GetComponent<TeleportWorld>();
            Transform keep1 = tw.m_proximityRoot;
            Transform keep2 = tw.m_target_found != null ? tw.m_target_found.transform : null;
            Transform trigger = root.GetComponentInChildren<TeleportWorldTrigger>(true)?.transform;

            bool IsWorkingPart(Transform t) =>
                (keep1 != null && t.IsChildOf(keep1)) || (keep2 != null && t.IsChildOf(keep2)) || (trigger != null && t.IsChildOf(trigger));

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (!IsWorkingPart(r.transform) && !(r is ParticleSystemRenderer)) r.enabled = false;
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                if (!IsWorkingPart(c.transform) && !c.isTrigger) Object.DestroyImmediate(c);
            foreach (Transform t in root.transform.Cast<Transform>().Where(t => t.CompareTag("snappoint")).ToList())
                Object.DestroyImmediate(t.gameObject);

            // the wardrobe's look: its cabinet and two doors on hinges (Cabinet), facing +z, the way the portal faces: whoever comes
            // through arrives in front of the doors (the game puts them a metre along the portal's +z)
            var model = new GameObject("wardrobe").transform;
            model.SetParent(root.transform, false);
            MeshRenderer cabinet = Cabinet.Build(model, wardrobe);

            // walls (back, sides, top) round the cabinet: solid like furniture, open at the front
            Bounds b = LocalBounds(root.transform, cabinet.transform);
            if (b.size != Vector3.zero) AddWalls(model, b);

            FitPortal(tw, trigger);
        }

        // the swirl and its light, sound and dark centre, squeezed into the cabinet (the arch is about 2.4 m wide and 2.7 m tall inside,
        // the cabinet 1.25 m by 2.3 m); the trigger just inside the doors
        private static readonly Vector3 SwirlScale = new Vector3(0.38f, 0.62f, 0.38f);
        private static readonly Vector3 SwirlAt = new Vector3(0f, 0.45f, -0.05f);
        private static readonly Vector3 TriggerAt = new Vector3(0f, 1.2f, 0.15f);
        private static readonly Vector3 TriggerScale = new Vector3(0.65f, 1.2f, 0.4f);

        private static void FitPortal(TeleportWorld tw, Transform trigger)
        {
            if (tw.m_target_found != null)
            {
                Transform swirl = tw.m_target_found.transform;
                swirl.localPosition = SwirlAt;
                swirl.localScale = SwirlScale;
                foreach (ParticleSystem ps in swirl.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // follow the squeeze
                }
            }
            if (trigger != null)
            {
                BoxCollider box = trigger.GetComponent<BoxCollider>();
                trigger.localPosition = TriggerAt;
                trigger.localScale = TriggerScale;
            }
        }

        private const float Wall = 0.05f;
        private const float FrontZSign = 1f;   // the doors face +z (see Dress)

        private static void AddWalls(Transform parent, Bounds b)
        {
            void Box(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name) { layer = LayerMask.NameToLayer("piece") };
                go.transform.SetParent(parent, false);
                go.transform.localPosition = center;
                go.AddComponent<BoxCollider>().size = size;
            }
            Vector3 c = b.center, s = b.size;
            float backZ = c.z - FrontZSign * (s.z / 2f - Wall / 2f);
            Box("wall_back", new Vector3(c.x, c.y, backZ), new Vector3(s.x, s.y, Wall));
            Box("wall_left", new Vector3(c.x - s.x / 2f + Wall / 2f, c.y, c.z), new Vector3(Wall, s.y, s.z));
            Box("wall_right", new Vector3(c.x + s.x / 2f - Wall / 2f, c.y, c.z), new Vector3(Wall, s.y, s.z));
            Box("wall_top", new Vector3(c.x, c.y + s.y / 2f - Wall / 2f, c.z), new Vector3(s.x, Wall, s.z));
        }

        /// <summary>The bounds of everything drawn under <paramref name="of"/>, in <paramref name="space"/>'s local space (from the meshes).</summary>
        private static Bounds LocalBounds(Transform space, Transform of)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter mf in of.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance) => WardrobePiece.Register(__instance);
    }

    /// <summary>Right after the game builds its list of portal prefabs, and before the world's objects load (and pair by tag).</summary>
    [HarmonyPatch(typeof(Game), "Awake")]
    internal static class Game_Awake
    {
        private static void Postfix() => WardrobePiece.AddPortalHash();
    }
}
