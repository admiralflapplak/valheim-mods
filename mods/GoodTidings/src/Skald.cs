using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace GoodTidings
{
    /// <summary>
    /// The Skald (a good visit, set with the user 2026-10-30): a long-dead skald, a friendly skeleton, rises out of the ground at the most
    /// comfortable spot in the base (the game's own comfort score), his lyre on his back, strolls about a little, then takes it in hand and
    /// plays his three songs once each, talking now and then (Haldor's voice); then he bows and sinks back.
    /// While he's there the comfort around him is 2 higher; anyone near him for 30 seconds has every skill raised by 5% of its level, once
    /// a visit. At the end he sinks back into the ground. Our own saved object ("goodtidings_skald", as Muninn's): his look is made by each
    /// game (SkaldBones, SkaldLook).
    /// </summary>
    internal sealed class SkaldVisit : Visit
    {
        public override string Id => "skald";
        public override bool Enabled => Plugin.SkaldEnabled.Value;

        public override bool Start(Vector3 basePoint)
        {
            if (SkaldVisitor.Count > 0) { Plugin.Debug("The skald is already here"); return false; }
            GameObject prefab = ZNetScene.instance.GetPrefab(SkaldPrefab.Name);
            if (prefab == null || !FindSpot(basePoint, out Vector3 at, out int comfort)) { Plugin.Debug($"No room for the skald near {basePoint}"); return false; }
            GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(SkaldVisitor.OpenWay(at, basePoint)));
            ZDO zdo = go.GetComponent<ZNetView>().GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            zdo.Set(SkaldVisitor.ArrivedKey, now);
            zdo.Set(SkaldVisitor.StageAtKey, now);
            zdo.Set(SkaldVisitor.SpotKey, at);
            zdo.Set(SkaldVisitor.HomeKey, basePoint);
            Net.MessageAll(MessageHud.MessageType.Center, "$goodtidings_skald_arrives");
            Plugin.Log.LogInfo($"The skald rises at {at} (comfort {comfort}), by the base at {basePoint}");
            return true;
        }

        private static int _solid;

        /// <summary>
        /// The most comfortable spot within 20 m of the base (the game's own count: shelter, then fires, beds, chairs, banners... within
        /// 10 m), on a floor or the ground at about the base's height, with room for him to stand; of equals, the nearest.
        /// </summary>
        internal static bool FindSpot(Vector3 home, out Vector3 at, out int comfort)
        {
            if (_solid == 0) _solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            int noTerrain = _solid & ~LayerMask.GetMask("terrain");
            at = home;
            comfort = -1;
            float bestDistance = float.MaxValue;
            for (float dx = -20f; dx <= 20f; dx += 2f)
                for (float dz = -20f; dz <= 20f; dz += 2f)
                {
                    Vector3 p = home + new Vector3(dx, 0f, dz);
                    float distance = Utils.DistanceXZ(p, home);
                    if (distance > 20f) continue;
                    if (!Physics.Raycast(p + Vector3.up * 1.6f, Vector3.down, out RaycastHit hit, 4f, _solid, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.8f || hit.point.y < ZoneSystem.instance.m_waterLevel + 0.3f) continue;
                    Vector3 feet = hit.point;
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.8f, 0.45f, noTerrain, QueryTriggerInteraction.Ignore)) continue;
                    Cover.GetCoverForPoint(feet + Vector3.up, out float cover, out bool underRoof);
                    int c = SE_Rested.CalculateComfortLevel(cover >= 0.8f && underRoof, feet);
                    if (c > comfort || c == comfort && distance < bestDistance)
                    {
                        comfort = c;
                        bestDistance = distance;
                        at = feet;
                    }
                }
            return comfort >= 0;
        }
    }

    /// <summary>The skald's saved object, registered as Muninn's is: an empty one, his look made by each game.</summary>
    internal static class SkaldPrefab
    {
        internal const string Name = "goodtidings_skald";
        private static GameObject _holder, _prefab;
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabs =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        internal static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (_prefab == null) Build();
            scene.m_prefabs.RemoveAll(p => p == null || (p.name == Name && p != _prefab));
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            NamedPrefabs(scene)[Name.GetStableHashCode()] = _prefab;
        }

        internal static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null && _prefab != null)
            {
                scene.m_prefabs.Remove(_prefab);
                var named = NamedPrefabs(scene);
                if (named.TryGetValue(Name.GetStableHashCode(), out GameObject p) && p == _prefab) named.Remove(Name.GetStableHashCode());
            }
            if (_holder) UnityEngine.Object.Destroy(_holder);
            _holder = _prefab = null;
        }

        private static void Build()
        {
            _holder = new GameObject("goodtidings_prefabs_skald");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            var p = new GameObject(Name);
            p.transform.SetParent(_holder.transform, false);
            p.layer = LayerMask.NameToLayer("piece_nonsolid");
            var nview = p.AddComponent<ZNetView>();
            nview.m_persistent = true;
            var sync = p.AddComponent<ZSyncTransform>();   // (his place while he wanders, from his owner)
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            var col = p.AddComponent<CapsuleCollider>();   // (what the player looks at: a trigger, it pushes nothing)
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.95f, 0f);
            col.radius = 0.45f;
            col.height = 1.9f;
            p.AddComponent<SkaldVisitor>();
            _prefab = p;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Register(__instance);
        }
    }

    /// <summary>
    /// On the skald's object, on every game: his look and its animation for the stage (rising, wandering, playing, bowing, sinking), his
    /// songs and words (with Haldor's voice), the map markers, and this game's player's listening (the skill-ups, once a visit). The
    /// stage is in his ZDO, moved on by his owner, who also walks him while he wanders (his place sent as any object's: ZSyncTransform).
    /// </summary>
    internal sealed class SkaldVisitor : MonoBehaviour, Hoverable
    {
        internal const string ArrivedKey = "GoodTidings.Arrived", LeaveAtKey = "GoodTidings.LeaveAt", HomeKey = "GoodTidings.Home",
            StageKey = "GoodTidings.Stage", StageAtKey = "GoodTidings.StageAt", SpotKey = "GoodTidings.Spot", PlayAtKey = "GoodTidings.PlayAt";
        internal const int Rising = 0, Wandering = 1, Playing = 2, Bowing = 3, Sinking = 4;
        internal static readonly string[] Outfit = { "HelmetCelebration", "ArmorBronzeChest", "ArmorBronzeLegs" };   // (the user's pick)
        private const float RiseTime = 3.5f, SinkTime = 3.5f, Blending = 0.8f, WanderTime = 20f, WanderRange = 4f, WalkSpeed = 1.1f;
        internal const float ListenRange = 12f;
        private static readonly HashSet<SkaldVisitor> All = new HashSet<SkaldVisitor>();
        internal static int Count => All.Count;

        /// <summary>How far the nearest playing skald is from a point (no one playing: very far).</summary>
        internal static float NearestPlaying(Vector3 point)
        {
            float best = float.MaxValue;
            foreach (SkaldVisitor s in All)
                if (s && s.Stage == Playing) best = Mathf.Min(best, Vector3.Distance(s.transform.position, point));
            return best;
        }

        /// <summary>A skald here (not yet sinking) within <paramref name="range"/> of a point (his comfort).</summary>
        internal static bool Near(Vector3 point, float range) =>
            All.Any(s => s && s.Stage < Sinking && Vector3.Distance(s.transform.position, point) < range);

        private ZNetView _nview;
        private GameObject _body, _lyre;
        private Animator _animator;
        private MixamoPlayer _player;
        private readonly DvergerMotion _walk = new DvergerMotion();
        private SkaldLook.Holder _holder;
        private AudioSource _song;
        private AudioClip _lastSong;
        private PlayableGraph _graph;
        private ParticleSystem _motes;
        private readonly MapPins _pins = new MapPins();
        private int _shown = -1;
        private float _shownAt, _listened;
        private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        private int Stage => _nview?.GetZDO()?.GetInt(StageKey) ?? Rising;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            All.Add(this);
        }

        private void Start()
        {
            if (_nview.GetZDO() == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;   // (a dedicated server draws nothing)
            try { Dress(); }
            catch (Exception e) { Plugin.Log.LogError($"Making the skald's look failed: {e}"); }
        }

        private void Dress()
        {
            var at = new GameObject("goodtidings_skald_look").transform;
            at.SetParent(transform, false);
            _body = DvergerLook.BodyOf("Player", "Visual", at);
            SkinnedMeshRenderer mesh = _body ? _body.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "body") : null;
            if (mesh == null) { Plugin.Log.LogWarning("No player body for the skald"); return; }
            _made.AddRange(SkaldBones.Build(at, mesh, _body.transform));
            foreach (string item in Outfit) SkaldBones.Wear(mesh, item, _made);
            _animator = _body.GetComponentInChildren<Animator>();
            if (_animator) _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _lyre = SkaldLook.Lyre(mesh.gameObject.layer);
            _lyre.transform.SetParent(at, false);
            _holder = new SkaldLook.Holder(_lyre, at, _body.transform, MixamoClip.Get("skald_play")?.Length ?? 6f);
        }

        // ---- what every game shows for the stage

        /// <summary>Changes his animation (and the lyre, songs, motes) to a stage's, blending from the pose he's in.</summary>
        private void Show(int stage, bool fresh)
        {
            if (stage == _shown || _body == null || _animator == null) return;
            int was = _shown;
            _shown = stage;
            _shownAt = Time.time;
            HumanPose pose = MixamoPlayer.PoseOf(_animator);
            bool posed = was >= 0 && pose.muscles != null;
            if (_graph.IsValid()) _graph.Destroy();
            if (stage != Wandering) _walk.Stop();
            if (_player)
            {
                // a guitar or a bow ending: it fades into what comes next, unless that's another of its own (which blends in from it)
                if (stage == Sinking) _player.FadeOut(Blending); else Destroy(_player);
                _player = null;
            }
            bool playing = stage == Playing;
            if (_motes) { if (playing) _motes.Play(true); else _motes.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (_motes && _motes.GetComponentInChildren<Light>() is Light light) light.enabled = playing;

            switch (stage)
            {
                case Rising:
                case Sinking:
                {
                    SkaldLook.OnBack(_lyre, _body.transform, glide: was == Playing);
                    AnimationClip rise = RiseClip();
                    if (rise && (fresh || stage == Sinking))
                    {
                        AnimationClipPlayable p = AnimationPlayableUtilities.PlayClip(_animator, rise, out _graph);
                        if (stage == Sinking) { p.SetTime(rise.length); p.SetSpeed(-1); }   // (sinking: the rise backwards)
                        if (posed && stage == Sinking && was != Playing && was != Bowing) PoseBlend.Start(_animator, pose, Blending);
                    }
                    // the ground breaking (rocks, pebbles, smoke, as a summoned skeleton's) and the skeletons' rising sound
                    if (fresh || stage == Sinking)
                    {
                        LocalFx.Spawn("fx_summon_skeleton_spawn", transform.position, transform.rotation);
                        LocalFx.Spawn("sfx_skeleton_rise", transform.position + Vector3.up, transform.rotation);
                    }
                    break;
                }
                case Wandering:
                    SkaldLook.OnBack(_lyre, _body.transform);
                    _walk.Walk(_animator, "Walk New", "IdleTweaked", 1.6f);
                    if (posed) PoseBlend.Start(_animator, pose, Blending);
                    if (fresh) Say("goodtidings_skald_risen", "sfx_haldor_greet");
                    break;
                case Playing:
                    _player = MixamoPlayer.Play(_animator, "skald_play");
                    if (_player)
                    {
                        if (posed) _player.BlendFrom(pose, Blending);
                        _player.Posed = _holder.Place;
                    }
                    if (_song == null)
                        _song = SkaldLook.Play(gameObject, _nview.GetZDO().m_uid.GetHashCode(), _nview.GetZDO().GetLong(PlayAtKey, ZNet.instance.GetTime().Ticks));
                    if (_motes == null) { _motes = SkaldLook.Motes(transform); _motes.Play(true); }
                    if (fresh) Say("goodtidings_skald_begin", "sfx_haldor_greet");
                    break;
                case Bowing:
                    SkaldLook.OnBack(_lyre, _body.transform, glide: true);
                    _player = MixamoPlayer.Play(_animator, "skald_bow", loop: false);
                    if (_player && posed) _player.BlendFrom(pose, Blending * 0.6f);
                    Say("goodtidings_skald_bow", "sfx_haldor_laugh");
                    break;
            }
        }

        /// <summary>A line over his head (each game in its own language) with one of Haldor's sounds.</summary>
        private void Say(string moment, string voice)
        {
            if (Chat.instance && _body)
                Chat.instance.SetNpcText(gameObject, Vector3.up * 2.3f, 20f, 5f, "", Localization.instance.Localize(Lines.Pick(moment)), false);
            LocalFx.Spawn(voice, transform.position + Vector3.up * 1.6f, transform.rotation, transform);
        }

        private static AnimationClip _rise;

        /// <summary>The skeletons' "Skeleton Rise", copied without its events (they'd call the game's character code).</summary>
        private static AnimationClip RiseClip()
        {
            if (_rise) return _rise;
            Animator a = ZNetScene.instance?.GetPrefab("Skeleton")?.GetComponentInChildren<Animator>(true);
            AnimationClip clip = a?.runtimeAnimatorController?.animationClips.FirstOrDefault(c => c.name == "Skeleton Rise");
            if (clip == null) return null;
            _rise = Instantiate(clip);
            _rise.name = clip.name;
            _rise.events = new AnimationEvent[0];
            return _rise;
        }

        private void OnDestroy()
        {
            All.Remove(this);
            _pins.Remove();
            _walk.Stop();
            if (_graph.IsValid()) _graph.Destroy();
            foreach (UnityEngine.Object o in _made) if (o) Destroy(o);
        }

        private void Update()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo == null) return;
            int stage = zdo.GetInt(StageKey);
            if (_body)
            {
                // the blue circle on the base (a good visit, as Dagny's) and the game's "?" where he is, no name (as Muninn)
                _pins.Update(zdo.GetVec3(HomeKey, transform.position), transform.position, "", icon: MapPins.Mystery(), circle: MapPins.Good);
                // (first seen mid-stage: shown as it is, without the rising's effects or its words)
                bool fresh = _shown >= 0 || ZNet.instance.GetTime().Ticks - zdo.GetLong(StageAtKey, 0L) < TimeSpan.FromSeconds(1.5).Ticks;
                Show(stage, fresh);
                if (stage == Wandering) _walk.Update(transform.position, Time.deltaTime);
                if (_song)
                {
                    float fade = stage >= Bowing ? Mathf.Max(0f, 1f - (Time.time - _shownAt) / (stage == Bowing ? 2.5f : SinkTime)) : 1f;
                    _song.volume = stage >= Bowing ? SkaldLook.Volume * (stage == Bowing ? fade : 0f) : SkaldLook.Volume;
                    if (stage == Playing && _song.clip != _lastSong)
                    {
                        if (_lastSong != null) Say("goodtidings_skald_between", "sfx_haldor_yea");   // (on to the next song)
                        _lastSong = _song.clip;
                    }
                }
                Listen(stage);
            }
            if (_nview.IsOwner()) Run(zdo, stage);
        }

        // ---- his owner moves the stages on, and walks him while he wanders

        private Vector3 _target, _facing;
        private float _pause;

        private void SetStage(ZDO zdo, int stage)
        {
            zdo.Set(StageKey, stage);
            zdo.Set(StageAtKey, ZNet.instance.GetTime().Ticks);
        }

        private void Run(ZDO zdo, int stage)
        {
            long now = ZNet.instance.GetTime().Ticks;
            double inStage = (now - zdo.GetLong(StageAtKey, now)) / (double)TimeSpan.TicksPerSecond;
            Vector3 spot = zdo.GetVec3(SpotKey, transform.position);
            switch (stage)
            {
                case Rising:
                    if (inStage > RiseTime) SetStage(zdo, Wandering);
                    break;
                case Wandering:
                {
                    // a few strolls round his spot, then back to it to play (facing where the player stood)
                    bool done = inStage > WanderTime;
                    if (done) _target = spot;
                    else if (_target == Vector3.zero || (Utils.DistanceXZ(transform.position, _target) < 0.2f && (_pause -= Time.deltaTime) < 0f))
                    {
                        _target = Stroll(spot);
                        _pause = UnityEngine.Random.Range(1.5f, 3.5f);
                    }
                    bool there = Step(_target, out bool blocked);
                    if (blocked && !done) _target = transform.position;   // (something in the way: stop, and stroll elsewhere after a pause)
                    if ((there || blocked) && done)
                    {
                        // (blocked on the way back: he plays where he is) turning on the spot to face the open room, leaning toward where
                        // the player stood; playing once he faces it
                        if (_facing == Vector3.zero) _facing = OpenWay(transform.position, zdo.GetVec3(HomeKey, spot));
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(_facing), 120f * Time.deltaTime);
                        if (Quaternion.Angle(transform.rotation, Quaternion.LookRotation(_facing)) < 2f)
                        {
                            zdo.Set(PlayAtKey, now);
                            SetStage(zdo, Playing);
                        }
                    }
                    break;
                }
                case Playing:
                    // his three songs, each once (their lengths known here, a dedicated server's too), then the bow
                    if (now >= zdo.GetLong(PlayAtKey, now) + TimeSpan.FromSeconds(SkaldLook.AllSongs + 1f).Ticks)
                    {
                        SetStage(zdo, Bowing);
                        Net.MessageAll(MessageHud.MessageType.Center, "$goodtidings_skald_leaves");
                    }
                    break;
                case Bowing:
                    if (inStage > (MixamoClip.Get("skald_bow")?.Length ?? 2.8f) + 0.4f) SetStage(zdo, Sinking);
                    break;
                case Sinking:
                    if (inStage > SinkTime + 0.3f)
                    {
                        Plugin.Log.LogInfo("The skald's song ends: he has sunk back to his rest");
                        ZNetScene.instance.Destroy(gameObject);
                    }
                    break;
            }
        }

        private static int _solid;

        /// <summary>A spot near his own he can stroll to in a straight line, with room to stand.</summary>
        private Vector3 Stroll(Vector3 spot)
        {
            if (_solid == 0) _solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * WanderRange;
                Vector3 p = spot + new Vector3(r.x, 0f, r.y);
                if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, _solid | LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore)) continue;
                p = hit.point;
                if (Mathf.Abs(p.y - spot.y) > 0.6f || hit.normal.y < 0.8f) continue;
                if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.7f, 0.35f, _solid, QueryTriggerInteraction.Ignore)) continue;
                // a clear way there from here, and from there back to his spot (he walks back at the end)
                if (!Clear(transform.position, p) || !Clear(p, spot)) continue;
                return p;
            }
            return spot;
        }

        /// <summary>Nothing solid between two places from his knees to his head (a body-wide sweep).</summary>
        private static bool Clear(Vector3 a, Vector3 b)
        {
            if (_solid == 0) _solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
            Vector3 d = b - a;
            d.y = 0f;
            float length = d.magnitude;
            if (length < 0.05f) return true;
            foreach (float h in new[] { 0.5f, 1.1f, 1.6f })
                if (Physics.SphereCast(a + Vector3.up * h, 0.32f, d / length, out _, length, _solid, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        /// <summary>
        /// The way to face from a place: the most open of 16 (the free room straight ahead and to either side, at chest height, up to
        /// 8 m), leaning toward <paramref name="toward"/> (where the player stood) when it's close: never into a corner or a wall.
        /// </summary>
        internal static Vector3 OpenWay(Vector3 at, Vector3 toward)
        {
            if (_solid == 0) _solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
            Vector3 eye = at + Vector3.up * 1.3f;
            float Free(Vector3 dir) => Physics.Raycast(eye, dir, out RaycastHit hit, 8f, _solid, QueryTriggerInteraction.Ignore) ? hit.distance : 8f;
            var free = new float[16];
            for (int i = 0; i < 16; i++) free[i] = Free(Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward);
            Vector3 home = toward - at;
            home.y = 0f;
            Vector3 best = Vector3.forward;
            float bestScore = float.MinValue;
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                float room = free[i] + 0.6f * (free[(i + 1) % 16] + free[(i + 15) % 16]) + 0.3f * (free[(i + 2) % 16] + free[(i + 14) % 16]);
                float lean = home.sqrMagnitude > 1f ? 2f * Vector3.Dot(dir, home.normalized) : 0f;
                if (room + lean > bestScore) { bestScore = room + lean; best = dir; }
            }
            return best;
        }

        /// <summary>
        /// A step toward a point, on the ground (or floor), turning the way he walks; true once there. <paramref name="blocked"/>: something
        /// solid just ahead (he doesn't step into it).
        /// </summary>
        private bool Step(Vector3 to, out bool blocked)
        {
            blocked = false;
            Vector3 d = to - transform.position;
            d.y = 0f;
            if (d.magnitude < 0.05f) return true;
            if (!Clear(transform.position, transform.position + d.normalized * Mathf.Min(d.magnitude, 0.45f))) { blocked = true; return false; }
            Vector3 step = d.normalized * Mathf.Min(d.magnitude, WalkSpeed * Time.deltaTime);
            Vector3 next = transform.position + step;
            if (Physics.Raycast(next + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, (_solid == 0 ? 0 : _solid) | LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore))
                next.y = hit.point.y;
            transform.position = next;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 240f * Time.deltaTime);
            return false;
        }

        // ---- listening: this game's player, near him while he plays for Listen Seconds, once a visit (remembered on the player)

        private const string HeardKey = "goodtidings_skald_heard";

        private void Listen(int stage)
        {
            Player p = Player.m_localPlayer;
            if (p == null || stage != Playing || p.IsDead()) return;
            if (Vector3.Distance(p.transform.position, transform.position) > ListenRange) return;
            string visit = _nview.GetZDO().m_uid.ToString();
            if (p.m_customData.TryGetValue(HeardKey, out string heard) && heard == visit) return;
            _listened += Time.deltaTime;
            if (_listened < Plugin.SkaldListenSeconds.Value) return;
            p.m_customData[HeardKey] = visit;
            int raised = 0;
            float share = Plugin.SkaldSkillGain.Value / 100f;
            foreach (Skills.Skill skill in p.GetSkills().GetSkillList())
            {
                if (skill.m_level >= 100f) continue;
                skill.m_level = Mathf.Min(100f, skill.m_level + Mathf.Max(skill.m_level * share, 0.5f));
                raised++;
            }
            p.Message(MessageHud.MessageType.Center, "$goodtidings_skald_inspired");
            p.m_skillLevelupEffects.Create(p.GetHeadPoint(), p.transform.rotation, p.transform);   // (the game's own skill-up flash and sound)
            // and the game's own "no skill drain" (as after a death): the songs remembered through the next one
            p.GetSEMan().AddStatusEffect(SEMan.s_statusEffectSoftDeath, resetTime: true);
            Plugin.Log.LogInfo($"The skald's song: {raised} skills raised by {Plugin.SkaldSkillGain.Value}% of their level");
        }

        public string GetHoverText() => Localization.instance.Localize("$goodtidings_skald\n<color=#bdb7a9>$goodtidings_skald_hover</color>");
        public string GetHoverName() => Localization.instance.Localize("$goodtidings_skald");
        public float GetHoverOffset() => 0f;
    }

    /// <summary>
    /// Blends a body from a pose into whatever its animator plays now, over a moment (a change of animation without a snap), then goes.
    /// </summary>
    internal sealed class PoseBlend : MonoBehaviour
    {
        private HumanPose _from, _now;
        private HumanPoseHandler _handler;
        private float _time, _length;

        internal static void Start(Animator animator, HumanPose from, float seconds)
        {
            foreach (PoseBlend old in animator.GetComponents<PoseBlend>()) Destroy(old);
            var b = animator.gameObject.AddComponent<PoseBlend>();
            b._from = from;
            b._length = Mathf.Max(0.01f, seconds);
            b._handler = new HumanPoseHandler(animator.avatar, animator.transform);
        }

        private void LateUpdate()
        {
            _time += Time.deltaTime;
            _handler.GetHumanPose(ref _now);
            HumanPose mixed = MixamoPlayer.Blend(_from, _now, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_time / _length)));
            _handler.SetHumanPose(ref mixed);
            if (_time >= _length) Destroy(this);
        }

        private void OnDestroy() => _handler?.Dispose();
    }

    /// <summary>+Skald Comfort near a playing skald (the game counts comfort here for the Rested status and its length).</summary>
    [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(bool), typeof(Vector3))]
    internal static class SE_Rested_CalculateComfortLevel
    {
        private static void Postfix(Vector3 position, ref int __result)
        {
            if (SkaldVisitor.Count > 0 && SkaldVisitor.Near(position, 10f)) __result += Plugin.SkaldComfort.Value;
        }
    }
}
