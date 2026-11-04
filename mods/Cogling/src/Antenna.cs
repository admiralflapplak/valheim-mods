using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cogling
{
    /// <summary>
    /// The Cogling's antenna (home station): the game's Dvergr wisp light without its wisp, its stand in plain wood and its dish tilted
    /// (Dish tilt), and the ward's area circle. Its radius is a Cogling's working area; antennas whose areas overlap link into one area
    /// (a network). A Cogling can only be built inside an area. Registered through PieceReload, as the Cogling is.
    /// </summary>
    internal sealed class Antenna : MonoBehaviour, Hoverable
    {
        // ---- looking at it: its name, with how many Coglings its network powers, who they are, rebuilds waiting and its range (grey)

        public string GetHoverName() => Localization.instance.Localize("$cogling_antenna");

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            List<Antenna> network = NetworkAt(transform.position);
            if (network.Count == 0) network = new List<Antenna> { this };
            int have = CoglingsIn(network), can = network.Count * Plugin.CoglingsPerAntenna.Value;
            var names = Character.GetAllCharacters().Where(c => CoglingStance.IsCogling(c) && !c.IsDead() && c.GetComponent<ZNetView>()?.GetZDO() != null
                                                               && InArea(network, c.transform.position)).Select(CoglingTag.NameOf).ToList();
            long now = ZNet.instance != null ? ZNet.instance.GetTime().Ticks : 0L;
            var waits = network.SelectMany(a => Rebuilds.Pending(a)).Select(r => Mathf.Max(0f, (float)new System.TimeSpan(r.due - now).TotalMinutes)).OrderBy(m => m).ToList();
            var lines = new List<string>
            {
                Nick.Shared.Translations.T(network.Count > 1 ? "cogling_antenna_powers_linked" : "cogling_antenna_powers", have, can, network.Count),
            };
            if (names.Count > 0) lines.Add(string.Join(", ", names));
            foreach (float m in waits) lines.Add(Nick.Shared.Translations.T("cogling_antenna_rebuilding", Mathf.CeilToInt(m)));
            lines.Add(Nick.Shared.Translations.T("cogling_antenna_range", Radius.ToString("0")));
            return GetHoverName() + "\n<color=#bdb7a9>" + string.Join("\n", lines) + "</color>";
        }

        internal const string Name = "cogling_antenna";

        // ---- the antennas standing (loaded) in the world: our own list, never a scene search

        private static readonly List<Antenna> All = new List<Antenna>();
        private ZNetView _view;
        private GameObject _marker;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _marker = transform.Find("AreaMarker")?.gameObject;
            if (_marker != null && _marker.GetComponent<CircleProjector>() is CircleProjector circle) circle.m_radius = Radius;   // (world metres: the circle ignores the antenna's size)
            if (_view != null && _view.GetZDO() != null)   // (not the build ghost: it has no ZDO)
            {
                All.Add(this);
                _view.Unregister(Rebuilds.AddRpc);   // (an older copy's, after a hot reload)
                _view.Register<long, long>(Rebuilds.AddRpc, (sender, due, creator) => Rebuilds.RPC_Add(this, due, creator));
            }
            Shape(gameObject);   // (also an antenna built by an older copy of the mod, given this copy's code by PieceReload)
            // its moves and lamp (an older copy's is swapped for this copy's by PieceReload: never two)
            if (!GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == nameof(AntennaLife))) gameObject.AddComponent<AntennaLife>();
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (_view != null && _view.IsValid()) _view.Unregister(Rebuilds.AddRpc);
        }

        private float _nextRebuildCheck;

        /// <summary>The Size setting, on the prefab and on every antenna standing (the area circle keeps its radius: it's sized in metres).</summary>
        internal static void ApplySize()
        {
            var scale = Vector3.one * Plugin.AntennaSize.Value;
            if (Prefab != null) Prefab.transform.localScale = scale;
            foreach (Antenna a in All) if (a != null) a.transform.localScale = scale;
        }

        private void Update()
        {
            if (Time.time > _nextRebuildCheck) { _nextRebuildCheck = Time.time + 5f; Rebuilds.Tick(this); }
            // the circle shows while the player builds an antenna or a Cogling (the ghost's own too), as a ward's does while placing
            // (only real antennas and the build ghost: not pictures and render copies, where a just-made circle showed as a pale slab)
            bool show = (_view != null && _view.GetZDO() != null || gameObject.layer == GhostLayer) && ShowAreas();
            if (_marker != null && _marker.activeSelf != show)
            {
                _marker.SetActive(show);
                if (show && _marker.GetComponent<CircleProjector>() is CircleProjector circle) circle.m_radius = Radius;   // (world metres: the circle ignores the antenna's size)
            }
        }

        private static readonly int GhostLayer = LayerMask.NameToLayer("ghost");

        private static bool ShowAreas()
        {
            Player p = Player.m_localPlayer;
            if (p == null || !p.InPlaceMode()) return false;
            Piece selected = p.GetSelectedPiece();
            if (selected == null) return false;
            string name = selected.gameObject.name;
            return name == Name || name == CoglingPrefab.Name;
        }

        internal static float Radius => Plugin.AntennaRadius.Value;

        /// <summary>Whether a spot is inside any antenna's area (across the ground: height doesn't matter, as for a ward).</summary>
        /// <summary>The nearest antenna standing (loaded) within a distance, or null.</summary>
        internal static Antenna Nearest(Vector3 at, float within) =>
            All.Where(a => a != null && Vector3.Distance(a.transform.position, at) <= within).OrderBy(a => Vector3.Distance(a.transform.position, at)).FirstOrDefault();

        internal static bool Covers(Vector3 at) => All.Any(a => a != null && Utils.DistanceXZ(a.transform.position, at) <= Radius);

        /// <summary>
        /// The linked antennas whose area holds <paramref name="at"/>: every antenna covering it, and every antenna whose area overlaps
        /// one already in (their centres within two radii). Empty if no antenna covers the spot.
        /// </summary>
        internal static List<Antenna> NetworkAt(Vector3 at)
        {
            var network = All.Where(a => a != null && Utils.DistanceXZ(a.transform.position, at) <= Radius).ToList();
            for (int i = 0; i < network.Count; i++)
                foreach (Antenna other in All)
                    if (other != null && !network.Contains(other) && Utils.DistanceXZ(other.transform.position, network[i].transform.position) <= 2f * Radius)
                        network.Add(other);
            return network;
        }

        /// <summary>The Coglings a network powers: those standing in its area (loaded), and those its antennas are rebuilding.</summary>
        internal static int CoglingsIn(List<Antenna> network) =>
            // (real ones only: the build ghost is a whole Cogling too, without saved data, and it counted itself: "2/2" with one Cogling)
            Character.GetAllCharacters().Count(c => CoglingStance.IsCogling(c) && !c.IsDead() && c.GetComponent<ZNetView>()?.GetZDO() != null
                                                     && InArea(network, c.transform.position))
            + network.Sum(a => a != null ? Rebuilds.Pending(a).Count : 0);

        /// <summary>Whether a spot is inside a network's area.</summary>
        internal static bool InArea(List<Antenna> network, Vector3 at) =>
            network.Any(a => a != null && Utils.DistanceXZ(a.transform.position, at) <= Radius);

        // ---- the piece

        internal static GameObject Prefab;
        private static GameObject _holder;
        private static PieceTable _table;

        internal static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab == null) Prefab = Build(scene);
            if (Prefab == null) return;
            GameObject hammer = scene.GetPrefab("Hammer");
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null) _table = table;
            PieceReload.ReplacePrefab(scene, Prefab, table != null ? table : _table);
            int swapped = PieceReload.Reattach(scene, Prefab);
            if (swapped > 0) Plugin.Log.LogInfo($"Gave {swapped} standing antenna(s) this copy's code");
            ApplyCost();
            if (Prefab.GetComponent<Piece>().m_resources.Length == 0 && _table != null)
            {
                _table.m_pieces.Remove(Prefab);   // never free to build
                Plugin.Log.LogWarning("The Cogling antenna has no cost (see its Cost setting): it isn't in the build menu");
            }
            Plugin.Synced.NeedsServer(Prefab);
        }

        internal static void ApplyCost()
        {
            Piece piece = Prefab != null ? Prefab.GetComponent<Piece>() : null;
            if (piece == null) return;
            Costs.Apply(piece, Plugin.AntennaCost.Value, Plugin.Log);
            Costs.ApplyStation(piece, Plugin.AntennaStation.Value, Plugin.Log);
        }

        internal static void Unregister()
        {
            PieceReload.UnloadPiece(Prefab, _table);
            _holder = Prefab = null;
            _table = null;
            All.Clear();
            _post = _dish = null;
        }

        private static GameObject Build(ZNetScene scene)
        {
            // the Dvergr wisp light (Mistlands), its stand in plain wood and its wisp green; the piece settings (placing, effects, wear)
            // from the Dvergr lantern post, which is a building piece (the wisp light is only found in the world)
            GameObject wisp = scene.GetPrefab("dverger_demister");
            Piece post = scene.GetPrefab("piece_dvergr_lantern_pole")?.GetComponent<Piece>();
            Material wood = scene.GetPrefab("wood_beam")?.transform.Find("new")?.GetComponent<MeshRenderer>()?.sharedMaterial;   // (woodwall: wood all over its texture; the pole's has pale corners the stand's plate landed on)
            GameObject ward = scene.GetPrefab("guard_stone");
            if (wisp == null || post == null) { Plugin.Log.LogWarning("The game has no Dvergr wisp light or lantern post to copy: no Cogling antenna this session"); return null; }

            _holder = new GameObject("cogling_prefabs_antenna");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
            GameObject antenna = Object.Instantiate(wisp, _holder.transform);
            antenna.name = Name;
            antenna.layer = post.gameObject.layer;
            if (antenna.GetComponent<ZNetView>() is ZNetView view) view.m_persistent = true;

            Piece piece = antenna.GetComponent<Piece>() ?? antenna.AddComponent<Piece>();
            foreach (FieldInfo f in typeof(Piece).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(piece, f.GetValue(post));
            piece.m_name = "$cogling_antenna";
            piece.m_description = "$cogling_antenna_desc";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_usage = Piece.UsageTagFlags.Misc;   // (the menu lists by these tags too: the post's "Lighting" hid it from Misc)
            piece.m_icon = post.m_icon;
            piece.m_resources = new Piece.Requirement[0];   // Costs.Apply fills it

            try
            {
                foreach (Renderer r in antenna.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].name.StartsWith("torch") && wood != null) mats[i] = wood;   // the dark stand: plain wood
                    r.sharedMaterials = mats;
                }
                Shape(antenna);
                // the ward's area circle, shown while building (Update)
                Transform marker = ward != null ? ward.transform.Find("AreaMarker") : null;
                if (marker != null)
                {
                    GameObject circle = Object.Instantiate(marker.gameObject, antenna.transform);
                    circle.name = "AreaMarker";
                    circle.transform.localPosition = Vector3.zero;
                    circle.SetActive(false);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cogling antenna look: " + e.Message); }

            antenna.transform.localScale = Vector3.one * Plugin.AntennaSize.Value;
            antenna.AddComponent<Antenna>();
            try { piece.m_icon = Icon.Make(antenna, fallback: null) ?? piece.m_icon; }
            catch (Exception e) { Plugin.Log.LogWarning("Cogling antenna icon: " + e.Message); }
            return antenna;
        }

        // ---- the shape: the wisp light's dish, tilted on its post, without the wisp (its ball, light, sparks, sound, mist clearing)

        private const float DishFrom = 1.16f;   // the stand's mesh: the post below (to 1.02), the dish above (1.10 to 1.47, 0.49 m wide); its pivot
        // (where the mesh is cut: in the gap between them. Cut at the pivot, the bottom of the dish's hub stayed on the post and stood
        // still while the dish turned: the lamp, on the dish, looked to wander about the hub)
        private const float CutAt = 1.06f;
        private static Mesh _post, _dish;
        private static Vector3 _pivot;

        /// <summary>
        /// One antenna (or the prefab) made the tilted dish: the wisp taken off, and the stand's mesh split once into the post and the
        /// dish (its own child, turned by Dish tilt about the top of the post). An antenna built before keeps working: it's reshaped here.
        /// </summary>
        private static void Shape(GameObject antenna)
        {
            Transform wisp = antenna.transform.Find("_enabled");
            if (wisp != null)
            {
                wisp.gameObject.SetActive(false);
                if (antenna.scene.IsValid()) Destroy(wisp.gameObject); else DestroyImmediate(wisp.gameObject, true);
            }
            Transform stand = antenna.transform.Find("New");
            if (stand == null) return;
            // what turns: the dish and its hinge, about the post (an antenna from before has them on the stand: moved onto it)
            Transform turn = stand.Find("cogling_turn");
            if (turn == null)
            {
                turn = new GameObject("cogling_turn") { layer = stand.gameObject.layer }.transform;
                turn.SetParent(stand, false);
            }
            foreach (string part in new[] { "cogling_dish", "cogling_hinge" })
                if (stand.Find(part) is Transform loose) loose.SetParent(turn, false);
            Transform dish = turn.Find("cogling_dish");
            if (dish == null)
            {
                var filter = stand.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !Split(filter.sharedMesh, stand.localScale)) return;
                // the stand at scale 1 from now on (the split meshes have its old scale in them); its box keeps its size
                if (stand.GetComponent<BoxCollider>() is BoxCollider box)
                {
                    box.center = Vector3.Scale(box.center, stand.localScale);
                    box.size = Vector3.Scale(box.size, stand.localScale);
                }
                stand.localScale = Vector3.one;
                filter.sharedMesh = _post;
                dish = new GameObject("cogling_dish") { layer = stand.gameObject.layer }.transform;
                dish.SetParent(turn, false);
                dish.localPosition = _pivot;
                dish.gameObject.AddComponent<MeshFilter>().sharedMesh = _dish;
                dish.gameObject.AddComponent<MeshRenderer>().sharedMaterials = stand.GetComponent<MeshRenderer>().sharedMaterials;
            }
            else if (_post != null && _dish != null)
            {
                // (one made by an older copy: this copy's cut)
                if (stand.GetComponent<MeshFilter>() is MeshFilter post) post.sharedMesh = _post;
                if (dish.GetComponent<MeshFilter>() is MeshFilter bowl) bowl.sharedMesh = _dish;
            }
            dish.localRotation = Quaternion.Euler(Plugin.DishTilt.Value, 0f, 0f);   // (its face toward the antenna's front)
            Transform old = turn.Find("cogling_hinge");
            if (old != null && old.childCount != 4) { old.gameObject.SetActive(false); Destroy(old.gameObject); old = null; }   // (an older hinge)
            if (old == null) Hinge(turn, stand.GetComponent<MeshRenderer>().sharedMaterials);
            if (dish.Find("cogling_lamp") is Transform lamp)
            {
                lamp.localPosition = LampAt(dish);   // (one from before: where this copy puts it)
                if (lamp.Find("bulb") is Transform b) CentreBulb(b);
            }
            else Lamp(dish);
            antenna.GetComponent<AntennaLife>()?.Find();
        }

        private static Material _bulbMaterial;

        /// <summary>
        /// The lamp in the middle of the dish: a small copy of the wisp light's ball and a point light (AntennaLife
        /// colours them as the Coglings' eyes and keeps them to the settings).
        /// </summary>
        private static void Lamp(Transform dish)
        {
            GameObject wisp = ZNetScene.instance?.GetPrefab("dverger_demister");
            Transform stand = wisp != null ? wisp.transform.Find("New") : null;
            Transform ball = wisp != null ? wisp.transform.Find("_enabled/demister_ball (1)") : null;
            if (stand == null || ball == null) return;
            var lamp = new GameObject("cogling_lamp") { layer = dish.gameObject.layer };
            lamp.transform.SetParent(dish, false);
            lamp.transform.localPosition = LampAt(dish);
            lamp.transform.localScale = Vector3.one * Plugin.AntennaLightSize.Value;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.range = Plugin.AntennaLightRange.Value;
            light.intensity = Plugin.AntennaLightBrightness.Value;
            light.color = Plugin.EyeColor.Value;
            var bulb = new GameObject("bulb") { layer = dish.gameObject.layer };
            bulb.transform.SetParent(lamp.transform, false);
            bulb.transform.localScale = ball.lossyScale;
            bulb.AddComponent<MeshFilter>().sharedMesh = ball.GetComponent<MeshFilter>().sharedMesh;
            CentreBulb(bulb.transform);
            var r = bulb.AddComponent<MeshRenderer>();
            if (_bulbMaterial == null)
            {
                _bulbMaterial = new Material(ball.GetComponent<MeshRenderer>().sharedMaterial) { name = "cogling_bulb" };
                _bulbMaterial.EnableKeyword("_EMISSION");
            }
            r.sharedMaterial = _bulbMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Where the lamp goes on the dish: in the middle of its bowl (its mesh's middle; the wisp's ball floated above it), moved by the settings.</summary>
        private static Vector3 LampAt(Transform dish)
        {
            if (_lampCentre == null) _lampCentre = BowlCentre(dish.GetComponent<MeshFilter>()?.sharedMesh);
            return _lampCentre.Value + new Vector3(Plugin.AntennaLightSide.Value, Plugin.AntennaLightHeight.Value, Plugin.AntennaLightForward.Value);
        }

        private static Vector3? _lampCentre;

        /// <summary>The bulb's ball round the lamp's own point (the wisp's ball mesh isn't centred on its pivot: it sat off to one side).</summary>
        private static void CentreBulb(Transform bulb)
        {
            Mesh m = bulb.GetComponent<MeshFilter>()?.sharedMesh;
            if (m == null) return;
            bulb.localPosition = -Vector3.Scale(m.bounds.center, bulb.localScale);
        }


        /// <summary>
        /// The middle of the dish's bowl, on its own axis (a point off the axis seemed to wander as the dish turned): across, the middle
        /// of its rim (its top ring of points: the bowl opens up its own y); up, the inside of the bowl there (the highest point near the
        /// axis) and half the bulb above it.
        /// </summary>
        private static Vector3 BowlCentre(Mesh bowl)
        {
            if (bowl == null || !bowl.isReadable) return Vector3.zero;
            Vector3[] v = bowl.vertices;
            float top = v.Max(p => p.y), bottom = v.Min(p => p.y);
            var rim = v.Where(p => p.y > top - (top - bottom) * 0.1f).ToList();
            float x = rim.Average(p => p.x), z = rim.Average(p => p.z);
            float width = rim.Max(p => Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z)));
            // (across: where the hub's cup is, found from pictures of a real antenna straight on and from the side; the rim's and the
            // spokes' middle is off to one side of it, and a ball off the cup looked to wander as the dish turned. The build-menu
            // render showed it mirrored: don't fit it there)
            x = 0.005f;
            z = 0.025f;
            var middle = v.Where(p => Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z)) < width * 0.15f).ToList();
            float floor = middle.Count > 0 ? middle.Max(p => p.y) : (top + bottom) / 2f;
            float lift = 0.03f;   // (just on the hub: lower, it sank into the wood and showed through behind; higher, it slid off the cup seen from the side)
            var centre = new Vector3(x, floor + lift, z);
            return centre;
        }

        /// <summary>The lamp's place settings, on every antenna standing (and the prefab).</summary>
        internal static void ApplyLamp()
        {
            foreach (Transform dish in All.Where(a => a != null).Select(a => a.transform).Concat(Prefab != null ? new[] { Prefab.transform } : new Transform[0])
                         .Select(t => t.Find("New/cogling_turn/cogling_dish")).Where(d => d != null))
            {
                Transform lamp = dish.Find("cogling_lamp");
                if (lamp == null) continue;
                lamp.gameObject.SetActive(false);
                Destroy(lamp.gameObject);
                lamp.name = "cogling_lamp_old";
                Lamp(dish);
                dish.GetComponentInParent<AntennaLife>()?.Find();
            }
        }

        /// <summary>
        /// What holds the tilted dish to its post: a short neck from the post's top up to the pivot, and a pin across it the dish turns
        /// on (wooden cylinders, in the stand's own wood). Sizes from the stand's mesh: the post's top is 8 cm across at 0.51 m.
        /// </summary>
        private static void Hinge(Transform stand, Material[] wood)   // (stand: what it's on, the turn)
        {
            if (_cylinder == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // (Unity's own mesh: 2 tall, 1 across)
                _cylinder = probe.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(probe);
            }
            var hinge = new GameObject("cogling_hinge") { layer = stand.gameObject.layer }.transform;
            hinge.SetParent(stand, false);
            void Part(string name, Vector3 at, Vector3 size, Quaternion turn)
            {
                var part = new GameObject(name) { layer = stand.gameObject.layer };
                part.transform.SetParent(hinge, false);
                part.transform.localPosition = at;
                part.transform.localRotation = turn;
                part.transform.localScale = new Vector3(size.x, size.y / 2f, size.z);
                part.AddComponent<MeshFilter>().sharedMesh = _cylinder;
                part.AddComponent<MeshRenderer>().sharedMaterials = wood;
            }
            float top = 0.49f, pivot = _pivot.y;
            Part("neck", new Vector3(0f, (top + pivot) / 2f, 0f), new Vector3(0.13f, pivot - top + 0.05f, 0.13f), Quaternion.identity);
            Part("pin", new Vector3(0f, pivot, 0f), new Vector3(0.08f, 0.34f, 0.08f), Quaternion.Euler(0f, 0f, 90f));
            // the pin's ends: two knobs, as a hinge's
            Part("knob_l", new Vector3(-0.17f, pivot, 0f), new Vector3(0.11f, 0.04f, 0.11f), Quaternion.Euler(0f, 0f, 90f));
            Part("knob_r", new Vector3(0.17f, pivot, 0f), new Vector3(0.11f, 0.04f, 0.11f), Quaternion.Euler(0f, 0f, 90f));
        }

        private static Mesh _cylinder;

        /// <summary>The stand's mesh cut in two at DishFrom (a triangle goes by its middle), its scale baked in; the dish around its pivot.</summary>
        private static bool Split(Mesh source, Vector3 scale)
        {
            if (_post != null) return true;
            if (!source.isReadable) { Plugin.Log.LogWarning("The wisp light's mesh can't be read: its dish stays upright"); return false; }
            Vector3[] v = source.vertices;
            Vector3[] n = source.normals;
            Vector2[] uv = source.uv;
            int[] tris = source.triangles;
            _pivot = Vector3.Scale(new Vector3(0f, DishFrom, 0f), scale);
            var post = new List<int>();
            var dish = new List<int>();
            int moved = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                float middle = (v[tris[i]].y + v[tris[i + 1]].y + v[tris[i + 2]].y) / 3f;
                if (middle > CutAt && middle <= DishFrom) moved++;
                (middle > CutAt ? dish : post).AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
            Plugin.Log.LogInfo($"Antenna: {moved} triangle(s) of the dish's hub now turn with the dish (they stayed on the post before)");
            Mesh Part(List<int> part, Vector3 origin, string name)
            {
                var map = new Dictionary<int, int>();
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                foreach (int i in part)
                    if (!map.ContainsKey(i))
                    {
                        map[i] = verts.Count;
                        verts.Add(Vector3.Scale(v[i], scale) - origin);
                        if (n.Length == v.Length) norms.Add(new Vector3(n[i].x / scale.x, n[i].y / scale.y, n[i].z / scale.z).normalized);
                        if (uv.Length == v.Length) uvs.Add(uv[i]);
                    }
                var m = new Mesh { name = name };
                m.SetVertices(verts);
                if (norms.Count == verts.Count) m.SetNormals(norms);
                if (uvs.Count == verts.Count) m.SetUVs(0, uvs);
                m.SetTriangles(part.Select(i => map[i]).ToList(), 0);
                if (norms.Count != verts.Count) m.RecalculateNormals();
                m.RecalculateBounds();
                m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return m;
            }
            _post = Part(post, Vector3.zero, "cogling_antenna_post");
            _dish = Part(dish, _pivot, "cogling_antenna_dish");
            return true;
        }

        /// <summary>The Dish tilt setting, on the prefab and every antenna standing.</summary>
        internal static void ApplyTilt()
        {
            if (Prefab != null) Shape(Prefab);
            foreach (Antenna a in All) if (a != null) Shape(a.gameObject);
        }
    }

    /// <summary>
    /// Where a Cogling may be built: inside an antenna's area, and only while the linked antennas power fewer Coglings than they can
    /// (Coglings per antenna, each; those waiting to be rebuilt count). The ghost shows red otherwise, and placing says why.
    /// </summary>
    [HarmonyPatch]
    internal static class AntennaRule
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> Status = AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");

        /// <summary>Why a Cogling can't be placed where the ghost is (a message for the player), or null.</summary>
        private static string Problem(Player player, out GameObject ghost)
        {
            ghost = Ghost(player);
            if (ghost == null || !ghost.activeSelf || Utils.GetPrefabName(ghost) != CoglingPrefab.Name) return null;
            List<Antenna> network = Antenna.NetworkAt(ghost.transform.position);
            if (network.Count == 0) return "$cogling_needs_antenna";
            int have = Antenna.CoglingsIn(network), can = network.Count * Plugin.CoglingsPerAntenna.Value;
            return have >= can ? Nick.Shared.Translations.T("cogling_network_full", have, can) : null;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static void Ghost_Postfix(Player __instance)
        {
            if (Status(__instance) != Player.PlacementStatus.Valid || Problem(__instance, out GameObject ghost) == null) return;
            Status(__instance) = Player.PlacementStatus.Invalid;
            ghost.GetComponent<Piece>()?.SetInvalidPlacementHeightlight(true);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static bool TryPlace_Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (piece == null || piece.gameObject.name != CoglingPrefab.Name) return true;
            string problem = Problem(__instance, out _);
            if (problem == null) return true;
            __instance.Message(MessageHud.MessageType.Center, problem);
            __result = false;
            return false;
        }
    }
}
