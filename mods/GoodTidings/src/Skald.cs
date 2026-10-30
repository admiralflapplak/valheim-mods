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
    /// comfortable spot in the base (the game's own comfort score), his lute on his back, takes it in hand and plays his song for the visit.
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
            Vector3 face = basePoint - at;
            face.y = 0f;
            if (face.sqrMagnitude < 1f) face = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(face));
            ZDO zdo = go.GetComponent<ZNetView>().GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            zdo.Set(SkaldVisitor.ArrivedKey, now);
            zdo.Set(SkaldVisitor.LeaveAtKey, now + TimeSpan.FromMinutes(Plugin.SkaldMinutes.Value).Ticks);
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
    /// On the skald's object, on every game: his look (rising, playing, sinking), his song, the map circle, and this game's player's
    /// listening (the skill-ups, once a visit). His owner ends the visit.
    /// </summary>
    internal sealed class SkaldVisitor : MonoBehaviour, Hoverable
    {
        internal const string ArrivedKey = "GoodTidings.Arrived", LeaveAtKey = "GoodTidings.LeaveAt", HomeKey = "GoodTidings.Home",
            LeavingKey = "GoodTidings.Leaving";
        internal static readonly string[] Outfit = { "HelmetCelebration", "ArmorBronzeChest", "ArmorBronzeLegs" };   // (the user's pick)
        private const float RiseTime = 3.5f, SinkTime = 3.5f, Blending = 0.8f;
        internal const float ListenRange = 12f;
        private static readonly HashSet<SkaldVisitor> All = new HashSet<SkaldVisitor>();
        internal static int Count => All.Count;

        /// <summary>A skald playing within <paramref name="range"/> of a point (his comfort, the listening).</summary>
        internal static bool Near(Vector3 point, float range) =>
            All.Any(s => s && !s.Leaving && Vector3.Distance(s.transform.position, point) < range);

        private ZNetView _nview;
        private GameObject _body, _lute;
        private Animator _animator;
        private MixamoPlayer _player;
        private SkaldLook.Holder _holder;
        private AudioSource _song;
        private PlayableGraph _graph;
        private ParticleSystem _motes;
        private readonly MapPins _pins = new MapPins();
        private int _phase = -1;   // 0 rising, 1 playing, 2 sinking
        private float _phaseAt, _listened;
        private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        private bool Leaving => _nview != null && _nview.GetZDO()?.GetBool(LeavingKey) == true;

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
            // (seen first well after he rose: already playing)
            long arrived = _nview.GetZDO().GetLong(ArrivedKey, 0L);
            bool fresh = arrived > 0 && (ZNet.instance.GetTime().Ticks - arrived) < TimeSpan.FromSeconds(RiseTime).Ticks;
            if (Leaving) Phase(2);
            else if (fresh) Phase(0);
            else Phase(1);
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
            _lute = SkaldLook.Lute(mesh.gameObject.layer);
            _lute.transform.SetParent(at, false);
            _holder = new SkaldLook.Holder(_lute, at, _body.transform, MixamoClip.Get("skald_play")?.Length ?? 6f);
        }

        /// <summary>Rising (the skeletons' own rise, the lute on his back), playing (the lute in hand, his song), or sinking (the rise backwards).</summary>
        private void Phase(int phase)
        {
            if (phase == _phase || _body == null) return;
            _phase = phase;
            _phaseAt = Time.time;
            // (the pose he's in now, for the next animation to blend in from)
            HumanPose was = _animator && _animator.avatar ? MixamoPlayer.PoseOf(_animator) : default;
            if (_graph.IsValid()) _graph.Destroy();
            // playing → sinking: the guitar fades out into the sinking (the player goes by itself once it has)
            if (_player) { if (phase == 2) _player.FadeOut(Blending); else Destroy(_player); _player = null; }
            if (phase == 1)
            {
                _player = MixamoPlayer.Play(_animator, "skald_play");
                if (_player)
                {
                    if (was.muscles != null) _player.BlendFrom(was, Blending);
                    _player.Posed = _holder.Place;
                }
                else SkaldLook.OnBack(_lute, _body.transform);
                if (_song == null) _song = SkaldLook.Play(gameObject);
                else if (!_song.isPlaying) _song.Play();
                if (_motes == null) _motes = SkaldLook.Motes(transform);
                _motes.Play(true);
                if (_motes.GetComponentInChildren<Light>() is Light glow) glow.enabled = true;
                return;
            }
            SkaldLook.OnBack(_lute, _body.transform);
            AnimationClip rise = RiseClip();
            if (rise && _animator)
            {
                AnimationClipPlayable p = AnimationPlayableUtilities.PlayClip(_animator, rise, out _graph);
                if (phase == 2) { p.SetTime(rise.length); p.SetSpeed(-1); }   // (sinking: the rise backwards)
            }
            // the ground breaking (rocks, pebbles, smoke, as a summoned skeleton's) and the skeletons' rising sound, coming and going
            LocalFx.Spawn("fx_summon_skeleton_spawn", transform.position, transform.rotation);
            LocalFx.Spawn("sfx_skeleton_rise", transform.position + Vector3.up, transform.rotation);
            if (_motes)
            {
                _motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (_motes.GetComponentInChildren<Light>() is Light l) l.enabled = false;
            }
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
            if (_graph.IsValid()) _graph.Destroy();
            foreach (UnityEngine.Object o in _made) if (o) Destroy(o);
        }

        private void Update()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo == null) return;
            bool leaving = zdo.GetBool(LeavingKey);
            if (_body)
            {
                // the blue circle on the base (a good visit, as Dagny's) and the game's "?" where he plays, no name (as Muninn)
                _pins.Update(zdo.GetVec3(HomeKey, transform.position), transform.position, "", icon: MapPins.Mystery(), circle: MapPins.Good);
                if (leaving) Phase(2);
                else if (_phase == 0 && Time.time - _phaseAt > RiseTime) Phase(1);
                if (_song) _song.volume = _phase == 2 ? Mathf.Max(0f, SkaldLook.Volume * (1f - (Time.time - _phaseAt) / SinkTime)) : SkaldLook.Volume;
                Listen();
            }

            if (!_nview.IsOwner()) return;
            long now = ZNet.instance.GetTime().Ticks;
            if (!leaving && now >= zdo.GetLong(LeaveAtKey, now))
            {
                zdo.Set(LeavingKey, true);
                zdo.Set(ArrivedKey, now);   // (now: when he started to sink)
                Net.MessageAll(MessageHud.MessageType.Center, "$goodtidings_skald_leaves");
                Plugin.Log.LogInfo("The skald's song ends: he sinks back to his rest");
            }
            // gone once he's sunk (or at once on a game that draws nothing)
            if (leaving && (_body == null || _phase == 2 && Time.time - _phaseAt > SinkTime
                            || now - zdo.GetLong(ArrivedKey, now) > TimeSpan.FromSeconds(SinkTime + 5f).Ticks))
                ZNetScene.instance.Destroy(gameObject);
        }

        // ---- listening: this game's player, near him for Listen Seconds, once a visit (remembered on the player: a relog doesn't repeat it)

        private const string HeardKey = "goodtidings_skald_heard";

        private void Listen()
        {
            Player p = Player.m_localPlayer;
            if (p == null || _phase != 1 || p.IsDead()) return;
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
            Plugin.Log.LogInfo($"The skald's song: {raised} skills raised by {Plugin.SkaldSkillGain.Value}% of their level");
        }

        public string GetHoverText() => Localization.instance.Localize("$goodtidings_skald\n<color=#bdb7a9>$goodtidings_skald_hover</color>");
        public string GetHoverName() => Localization.instance.Localize("$goodtidings_skald");
        public float GetHoverOffset() => 0f;
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
