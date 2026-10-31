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
    /// The Wounded Dvergr (a neutral visit, set with the user 2026-10-30): a hurt Dvergr is found slumped near the base ("?" and a yellow
    /// circle on the map). Someone helps him up; he limps to the best rest by a burning fire (a bed near it, then a chair or bench, then
    /// the ground beside it; with no fire about, the ground where he is) and must be fed (food from the hotbar) before his time is up.
    /// Fed: he rests a moment, then mends and tempers the gear of everyone near (Tempered) and goes his way. Not fed in time: he limps off
    /// cursing, and soon after his kin come for the base (DvergerBand). Nothing can hurt him (as Dagny).
    /// </summary>
    internal sealed class DvergerVisit : Visit
    {
        public override string Id => "dverger";
        public override bool Enabled => Plugin.DvergerEnabled.Value;

        public override bool Start(Vector3 basePoint)
        {
            if (DvergerVisitor.Count > 0) { Plugin.Debug("A wounded Dvergr is already here"); return false; }
            GameObject prefab = ZNetScene.instance.GetPrefab(DvergerPrefab.Name);
            if (prefab == null || !FindGround(basePoint, 10f, 18f, out Vector3 kneel) || !FindGround(basePoint, 35f, 45f, out Vector3 at)) return false;
            Vector3 face = kneel - at;
            face.y = 0f;
            GameObject go = UnityEngine.Object.Instantiate(prefab, at, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
            ZDO zdo = go.GetComponent<ZNetView>().GetZDO();
            zdo.Set(DvergerAI.LeaveAtKey, ZNet.instance.GetTime().Ticks + TimeSpan.FromMinutes(Plugin.DvergerMinutes.Value).Ticks);
            zdo.Set(DvergerAI.KneelAtKey, kneel);
            go.GetComponent<DvergerAI>().SetHome(basePoint);
            Net.MessageAll(MessageHud.MessageType.Center, "$goodtidings_dverger_arrives");
            Plugin.Log.LogInfo($"A wounded Dvergr staggers in from {at} to {kneel}, by the base at {basePoint}");
            return true;
        }

        private static int _blockMask;

        /// <summary>Open ground (not a floor or a roof) <paramref name="min"/>-<paramref name="max"/> m from the base, with room to stand.</summary>
        private static bool FindGround(Vector3 home, float min, float max, out Vector3 at)
        {
            if (_blockMask == 0) _blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle", "character");
            for (int i = 0; i < 60; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(min, max);
                Vector3 p = home + new Vector3(r.x, 0f, r.y);
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h, 1000)) continue;
                if (h < ZoneSystem.instance.m_waterLevel + 0.3f) continue;
                if (Mathf.Abs(h - ZoneSystem.instance.GetGroundHeight(p)) > 0.3f) continue;
                p.y = h;
                if (Physics.CheckSphere(p + Vector3.up * 1.2f, 1f, _blockMask, QueryTriggerInteraction.Ignore)) continue;
                at = p + Vector3.up * 0.2f;
                return true;
            }
            at = Vector3.zero;
            return false;
        }
    }

    /// <summary>His saved, shared object: a copy of the Dverger prefab ("goodtidings_dverger"), its AI ours, unarmed, registered as Dagny's is.</summary>
    internal static class DvergerPrefab
    {
        internal const string Name = "goodtidings_dverger";
        private static GameObject _holder, _prefab;
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabs =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        internal static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (_prefab == null) Build(scene);
            if (_prefab == null) return;
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

        private static void Build(ZNetScene scene)
        {
            GameObject dverger = scene.GetPrefab("Dverger");
            if (dverger == null) { Plugin.Log.LogError("No Dverger prefab: the wounded Dvergr can't be made"); return; }
            _holder = new GameObject("goodtidings_prefabs_dverger");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            GameObject p = UnityEngine.Object.Instantiate(dverger, _holder.transform, false);
            p.name = Name;

            MonsterAI monster = p.GetComponent<MonsterAI>();
            var ai = p.AddComponent<DvergerAI>();
            foreach (System.Reflection.FieldInfo f in typeof(BaseAI).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                f.SetValue(ai, f.GetValue(monster));
            ai.m_aggravatable = false;
            ai.m_randomMoveRange = 12f;
            ai.m_randomMoveInterval = 6f;
            ai.m_spawnMessage = "";
            UnityEngine.Object.DestroyImmediate(monster);
            foreach (Type t in new[] { typeof(CharacterDrop), typeof(NpcTalk) })   // (NpcTalk needs the MonsterAI)
                if (p.GetComponent(t) is Component c) UnityEngine.Object.DestroyImmediate(c);

            // unarmed, in the rogue's suit with a red beard (Dverger wear their hair as a "shield"); he limps
            Humanoid h = p.GetComponent<Humanoid>();
            h.m_name = "$goodtidings_dverger";
            GameObject Item(string name) => scene.GetPrefab(name);
            h.m_defaultItems = new GameObject[0];   // (his clothes are put on by each game: WoundedLook)
            h.m_randomWeapon = new GameObject[0];
            h.m_randomShield = new[] { Item("DvergerHairMale_Redbeard") }.Where(x => x).ToArray();
            h.m_randomSets = new Humanoid.ItemSet[0];
            h.m_walkSpeed = 1.1f;
            h.m_speed = 1.1f;
            h.m_runSpeed = 1.4f;
            p.AddComponent<DvergerVisitor>();
            _prefab = p;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Register(__instance);
        }
    }

    /// <summary>
    /// His behaviour, on his owner only (the game's AI rule); everything is in his ZDO, so the next owner carries on. Stages: slumped
    /// where he was found, limping to his rest, resting (and eating), healing a moment, then leaving (thankful or cursing).
    /// </summary>
    internal sealed class DvergerAI : BaseAI
    {
        internal const string HomeKey = "GoodTidings.Home", LeaveAtKey = "GoodTidings.LeaveAt", StageKey = "GoodTidings.Stage",
            SpotKey = "GoodTidings.Spot", SpotRotKey = "GoodTidings.SpotRot", ApproachKey = "GoodTidings.Approach", PoseKey = "GoodTidings.Pose",
            NoFireKey = "GoodTidings.NoFire", MealsKey = "GoodTidings.Meals", StageAtKey = "GoodTidings.StageAt", BandAtKey = "GoodTidings.BandAt",
            BandSentKey = "GoodTidings.BandSent", KneelAtKey = "GoodTidings.KneelAt", TargetKey = "GoodTidings.Target",
            RetriedKey = "GoodTidings.Retried";
        internal const int Arriving = 0, Slumped = 1, Walking = 2, Resting = 3, Healing = 4, Tempering = 5, Crafting = 6, Thankful = 7, Neglected = 8;
        internal const string GroundPose = "Sitting idle", BedPose = "Laying Sleeping";
        private static readonly long LeaveGrace = TimeSpan.FromSeconds(150).Ticks, WalkGiveUp = TimeSpan.FromSeconds(60).Ticks,
            ArriveGiveUp = TimeSpan.FromSeconds(90).Ticks, TemperWalk = TimeSpan.FromSeconds(25).Ticks, CraftTime = TimeSpan.FromSeconds(4.5).Ticks,
            HealTime = TimeSpan.FromSeconds(8).Ticks, BandDelay = TimeSpan.FromSeconds(45).Ticks;

        internal void SetHome(Vector3 point)
        {
            m_spawnPoint = point;
            ZDO zdo = m_nview.GetZDO();
            zdo.Set(HomeKey, point);
            zdo.Set(ZDOVars.s_spawnPoint, point);
        }

        private long Now => ZNet.instance.GetTime().Ticks;

        private void SetStage(ZDO zdo, int stage)
        {
            zdo.Set(StageKey, stage);
            zdo.Set(StageAtKey, Now);
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt)) return false;
            ZDO zdo = m_nview.GetZDO();
            long now = Now;
            m_spawnPoint = zdo.GetVec3(ZDOVars.s_spawnPoint, m_spawnPoint);
            long leaveAt = zdo.GetLong(LeaveAtKey, 0L);
            if (leaveAt == 0L) { leaveAt = now + TimeSpan.FromMinutes(Plugin.DvergerMinutes.Value).Ticks; zdo.Set(LeaveAtKey, leaveAt); }   // (made by hand)
            int stage = zdo.GetInt(StageKey);

            if (stage <= Resting && now >= leaveAt)
            {
                // nobody saw to him in time: he gets up and limps off, and his kin come a little after
                StandUp(zdo);
                SetStage(zdo, Neglected);
                zdo.Set(BandAtKey, now + BandDelay);
                HeadAway(zdo);
                Plugin.Log.LogInfo("The wounded Dvergr was left: he limps off, and his kin will come");
                return true;
            }

            switch (stage)
            {
                case Arriving:
                {
                    // staggering in to where he goes down on one knee (or wherever he is after a minute and a half)
                    if (zdo.GetLong(StageAtKey, 0L) == 0L) zdo.Set(StageAtKey, now);
                    Vector3 kneel = zdo.GetVec3(KneelAtKey, transform.position);
                    if (MoveTo(dt, kneel, 0.8f, false) || now - zdo.GetLong(StageAtKey, now) > ArriveGiveUp)
                    {
                        StopMoving();
                        SetStage(zdo, Slumped);
                    }
                    else Unstick(dt, kneel);
                    return true;
                }
                case Slumped:
                    StopMoving();
                    return true;
                case Walking:
                {
                    Vector3 approach = zdo.GetVec3(ApproachKey, transform.position);
                    if (MoveTo(dt, approach, 0.7f, false)) { SetStage(zdo, Resting); return true; }
                    if (now - zdo.GetLong(StageAtKey, now) > WalkGiveUp)
                    {
                        // still not there after a minute: a bed or seat gives way to the ground by the fire; failing that, he sits where he is
                        if (Vector3.Distance(transform.position, approach) < 1.5f) { SetStage(zdo, Resting); return true; }
                        if (!zdo.GetBool(RetriedKey))
                        {
                            zdo.Set(RetriedKey, true);
                            Rest.Find(transform.position, transform.rotation, m_pathAgentType, out Rest.Place ground, furniture: false);
                            GoTo(zdo, ground);
                            Plugin.Log.LogInfo($"The wounded Dvergr can't get there: he goes to {ground.What} at {ground.Spot}");
                            return true;
                        }
                        zdo.Set(SpotKey, transform.position);
                        zdo.Set(SpotRotKey, transform.rotation);
                        zdo.Set(ApproachKey, transform.position);
                        zdo.Set(PoseKey, GroundPose);
                        SetStage(zdo, Resting);
                        return true;
                    }
                    Unstick(dt, approach);
                    return true;
                }
                case Resting:
                case Healing:
                    StopMoving();
                    Hold(zdo);
                    if (stage == Resting && zdo.GetBool(NoFireKey)) LookForFire(zdo, dt);
                    if (stage == Healing && now - zdo.GetLong(StageAtKey, now) > HealTime)
                    {
                        // up again: over to the nearest player, to see to their gear
                        StandUp(zdo);
                        Player near = Player.GetClosestPlayer(transform.position, 30f);
                        if (near == null) { Finish(zdo); return true; }
                        zdo.Set(TargetKey, near.GetPlayerID());
                        SetStage(zdo, Tempering);
                        Plugin.Log.LogInfo($"The wounded Dvergr is healed: he goes over to {near.GetPlayerName()}");
                    }
                    return true;
                case Tempering:
                {
                    Player target = Target(zdo);
                    if (target == null) { Finish(zdo); return true; }
                    if (MoveTo(dt, target.transform.position, 1.6f, false) || now - zdo.GetLong(StageAtKey, now) > TemperWalk)
                    {
                        StopMoving();
                        SetStage(zdo, Crafting);
                    }
                    else Unstick(dt, target.transform.position);
                    return true;
                }
                case Crafting:
                {
                    // at work on their gear, as at a workbench
                    StopMoving();
                    Player target = Target(zdo);
                    if (target) LookAt(target.transform.position);
                    if (now - zdo.GetLong(StageAtKey, now) > CraftTime) Finish(zdo);
                    return true;
                }
                default:   // leaving
                {
                    if (stage == Neglected && !zdo.GetBool(BandSentKey) && now >= zdo.GetLong(BandAtKey, now))
                    {
                        zdo.Set(BandSentKey, true);
                        DvergerBand.Call(zdo.GetVec3(HomeKey, transform.position));
                    }
                    Vector3 home = zdo.GetVec3(HomeKey, m_spawnPoint);
                    bool bandDone = stage != Neglected || zdo.GetBool(BandSentKey);
                    if (bandDone && (Utils.DistanceXZ(transform.position, home) > 60f || now > zdo.GetLong(StageAtKey, now) + LeaveGrace))
                    {
                        ZNetScene.instance.Destroy(gameObject);
                        return true;
                    }
                    IdleMovement(dt);
                    return true;
                }
            }
        }

        private float _fireSearch;

        /// <summary>The player he's seeing to (gone, or far off: null).</summary>
        private Player Target(ZDO zdo)
        {
            long id = zdo.GetLong(TargetKey, 0L);
            Player p = Player.GetAllPlayers().FirstOrDefault(x => x && x.GetPlayerID() == id);
            return p && Vector3.Distance(p.transform.position, transform.position) < 40f ? p : null;
        }

        /// <summary>Done: everyone near has their gear mended and tempered, and he goes his way.</summary>
        private void Finish(ZDO zdo)
        {
            SetStage(zdo, Thankful);
            Tempered.SendTemper(transform.position);
            HeadAway(zdo);
            Plugin.Log.LogInfo("The wounded Dvergr has tempered the gear of those near: he goes");
        }

        // ---- getting through a house: doors in his way are opened when he stops getting closer

        private float _progressTimer, _lastDistance = float.MaxValue;

        private void Unstick(float dt, Vector3 target)
        {
            _progressTimer += dt;
            if (_progressTimer < 2f) return;
            _progressTimer = 0f;
            float d = Vector3.Distance(transform.position, target);
            if (_lastDistance - d < 0.3f) OpenDoors();
            _lastDistance = d;
        }

        private void OpenDoors()
        {
            Vector3 at = transform.position + Vector3.up;
            foreach (Door door in Physics.OverlapSphere(at, 3f, Physics.AllLayers, QueryTriggerInteraction.Collide)
                         .Select(c => c.GetComponentInParent<Door>()).Where(x => x != null).Distinct())
            {
                ZNetView nv = door.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid() || door.m_keyItem != null || nv.GetZDO().GetInt(ZDOVars.s_state) != 0) continue;
                // the way the game opens it for a player standing here (away from them)
                bool forward = Vector3.Dot(door.transform.forward, (transform.position - door.transform.position).normalized) < 0f;
                nv.InvokeRPC("UseDoor", forward);
                Plugin.Debug($"The wounded Dvergr opens {door.name}");
            }
        }

        /// <summary>Sitting cold with no fire about: every few seconds he looks again, and goes to one once it burns.</summary>
        private void LookForFire(ZDO zdo, float dt)
        {
            _fireSearch -= dt;
            if (_fireSearch > 0f) return;
            _fireSearch = 4f;
            Rest.Find(transform.position, transform.rotation, m_pathAgentType, out Rest.Place place);
            if (!place.ByFire) return;
            StandUp(zdo);
            GoTo(zdo, place);
            Plugin.Log.LogInfo($"The wounded Dvergr sees a fire: he goes to {place.What} at {place.Spot}");
        }

        private void GoTo(ZDO zdo, Rest.Place place)
        {
            zdo.Set(SpotKey, place.Spot);
            zdo.Set(SpotRotKey, place.Rotation);
            zdo.Set(ApproachKey, place.Approach);
            zdo.Set(PoseKey, place.Pose);
            zdo.Set(NoFireKey, !place.ByFire);
            SetStage(zdo, Walking);
        }

        /// <summary>Kept on his bed, seat or patch of ground (his body held still there: see DvergerVisitor).</summary>
        private void Hold(ZDO zdo)
        {
            Vector3 spot = zdo.GetVec3(SpotKey, transform.position);
            Quaternion rot = zdo.GetQuaternion(SpotRotKey, transform.rotation);
            // looking the way he lies: standing still, the game turns a character to where it last looked, against this hold (he shook)
            Vector3 facing = Vector3.ProjectOnPlane(rot * Vector3.forward, Vector3.up);
            if (facing.sqrMagnitude > 0.01f) m_character.SetLookDir(facing);
            if ((transform.position - spot).sqrMagnitude > 0.0004f || Quaternion.Angle(transform.rotation, rot) > 1f)
            {
                Rigidbody body = m_character.GetComponent<Rigidbody>();
                transform.SetPositionAndRotation(spot, rot);
                if (body) { body.position = spot; body.rotation = rot; body.velocity = Vector3.zero; }
            }
        }

        /// <summary>Off the bed or seat: back on his feet beside it.</summary>
        private void StandUp(ZDO zdo)
        {
            int stage = zdo.GetInt(StageKey);
            if (stage != Resting && stage != Healing) return;
            Vector3 at = zdo.GetVec3(ApproachKey, transform.position) + Vector3.up * 0.1f;
            transform.position = at;
            Rigidbody body = m_character.GetComponent<Rigidbody>();
            if (body) { body.position = at; body.velocity = Vector3.zero; }
        }

        /// <summary>His wandering centre moved far out, away from the base: he walks off (as Dagny leaves).</summary>
        private void HeadAway(ZDO zdo)
        {
            Vector3 home = zdo.GetVec3(HomeKey, transform.position);
            Vector3 away = transform.position - home;
            away.y = 0f;
            if (away.sqrMagnitude < 1f) away = transform.forward;
            m_spawnPoint = home + away.normalized * 90f;
            zdo.Set(ZDOVars.s_spawnPoint, m_spawnPoint);
        }

        // ---- asked of his owner by any player's game

        /// <summary>Someone helped him up: he picks his rest and sets off for it.</summary>
        internal void Help()
        {
            ZDO zdo = m_nview.GetZDO();
            if (zdo.GetInt(StageKey) != Slumped) return;
            Rest.Find(transform.position, transform.rotation, m_pathAgentType, out Rest.Place place);
            GoTo(zdo, place);
            Plugin.Log.LogInfo($"The wounded Dvergr was helped up: he goes to {place.What} at {place.Spot}");
        }

        /// <summary>A meal (the game that gave it took it from its player).</summary>
        internal void Fed()
        {
            ZDO zdo = m_nview.GetZDO();
            if (zdo.GetInt(StageKey) != Resting || zdo.GetBool(NoFireKey)) return;
            int meals = zdo.GetInt(MealsKey) + 1;
            zdo.Set(MealsKey, meals);
            if (meals >= Plugin.DvergerMeals.Value) SetStage(zdo, Healing);
        }
    }

    /// <summary>Where he rests: by the burning fire nearest him, the best of a bed, a seat or the ground beside it.</summary>
    internal static class Rest
    {
        internal struct Place
        {
            public Vector3 Spot, Approach;
            public Quaternion Rotation;
            public string Pose, What;
            public bool ByFire;
        }

        internal const float FireSearch = 60f, NearFire = 6f;
        private static readonly System.Reflection.FieldInfo AllAreasField = AccessTools.Field(typeof(EffectArea), "s_allAreas");
        private static List<EffectArea> AllAreas() => AllAreasField?.GetValue(null) as List<EffectArea>;

        /// <summary>The burning fires that warm (a campfire, hearth, brazier...: torches and lamps give no heat) near a point: the game's
        /// warmth areas, which are only switched on while a fire burns.</summary>
        internal static IEnumerable<Fireplace> Fires(Vector3 near, float range) =>
            (AllAreas() ?? new List<EffectArea>()).Where(e => e && e.isActiveAndEnabled && (e.m_type & EffectArea.Type.Heat) != 0)
                .Select(e => e.GetComponentInParent<Fireplace>()).Where(f => f != null && f.IsBurning()
                                                                            && Vector3.Distance(f.transform.position, near) < range).Distinct();

        /// <summary>
        /// His rest by the nearest burning fire: a free bed near it, else a free seat, else the ground beside it (only the ground with
        /// <paramref name="furniture"/> false). A bed or seat counts only with a free spot right beside it that his pathfinding reaches
        /// from where he is (through a doorway, not a wall): he walks to that spot and lies down or sits from it.
        /// </summary>
        internal static void Find(Vector3 from, Quaternion facing, Pathfinding.AgentType agent, out Place place, bool furniture = true)
        {
            Fireplace fire = Fires(from, FireSearch).OrderBy(f => Vector3.Distance(f.transform.position, from)).FirstOrDefault();
            if (fire == null)
            {
                place = new Place { Spot = from, Approach = from, Rotation = facing,
                                    Pose = DvergerAI.GroundPose, What = "the ground (no fire)", ByFire = false };
                return;
            }
            Vector3 f0 = fire.transform.position;
            if (furniture)
            {
                var pieces = Physics.OverlapSphere(f0, NearFire + 2f, Physics.AllLayers, QueryTriggerInteraction.Collide)
                    .Select(c => c.GetComponentInParent<ZNetView>()).Where(v => v != null).Distinct().Select(v => v.gameObject).ToList();

                foreach (Bed bed in pieces.Select(g => g.GetComponent<Bed>()).Where(b => b != null && b.m_spawnPoint
                             && Vector3.Distance(b.transform.position, f0) < NearFire && Free(b.m_spawnPoint.position))
                             .OrderBy(b => Vector3.Distance(b.transform.position, f0)))
                    if (Beside(bed.m_spawnPoint.position, from, agent, out Vector3 step))
                    {
                        place = new Place { Spot = bed.m_spawnPoint.position, Rotation = bed.m_spawnPoint.rotation, Pose = DvergerAI.BedPose,
                                            What = "a bed", Approach = step, ByFire = true };
                        return;
                    }

                foreach (Chair chair in pieces.SelectMany(g => g.GetComponentsInChildren<Chair>()).Where(c => c != null && !c.m_inShip && c.m_attachPoint
                             && Vector3.Distance(c.transform.position, f0) < NearFire && Free(c.m_attachPoint.position))
                             .OrderBy(c => Vector3.Distance(c.transform.position, f0)))
                    if (Beside(chair.m_attachPoint.position, from, agent, out Vector3 step))
                    {
                        Transform a = chair.m_attachPoint;
                        place = new Place { Spot = a.position, Rotation = a.rotation, What = "a seat", ByFire = true, Approach = step,
                                            Pose = chair.m_attachAnimation == "attach_throne" ? "Sit_Throne_Idle" : "Player_Sit_Chair" };
                        return;
                    }
            }

            // the ground beside the fire, facing it: his side of it if he can get there, else round it to where he can
            Vector3 toHim = from - f0;
            toHim.y = 0f;
            if (toHim.sqrMagnitude < 0.01f) toHim = Vector3.forward;
            Vector3 spot = Ground(f0 + toHim.normalized * 1.8f), side = toHim.normalized;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 45f, 0f) * toHim.normalized;
                Vector3 c = Ground(f0 + dir * 1.8f);
                if (Clear(c) && PathLength(from, c, agent) >= 0f) { spot = c; side = dir; break; }
            }
            place = new Place { Spot = spot, Approach = spot, Rotation = Quaternion.LookRotation(-side), Pose = DvergerAI.GroundPose,
                                What = "the ground by the fire", ByFire = true };
        }

        /// <summary>No player on it (sleeping or sitting) now.</summary>
        private static bool Free(Vector3 at) => !Player.GetAllPlayers().Any(p => Vector3.Distance(p.transform.position, at) < 0.8f);

        private static int _solidMask;
        private static int SolidMask => _solidMask != 0 ? _solidMask
            : (_solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"));

        /// <summary>
        /// The free floor spot right beside a bed or seat (<paramref name="at"/>: where he lies or sits) with the shortest path to it from
        /// where he is; false if none can be reached.
        /// </summary>
        private static bool Beside(Vector3 at, Vector3 from, Pathfinding.AgentType agent, out Vector3 best)
        {
            best = at;
            float bestLength = float.MaxValue;
            foreach (float r in new[] { 0.9f, 1.3f })
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = Ground(at + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * r);
                    if (c.y > at.y + 0.3f || c.y < at.y - 1.5f || !Clear(c)) continue;   // (another floor, or a wall or the bed itself)
                    float length = PathLength(from, c, agent);
                    if (length >= 0f && length < bestLength) { best = c; bestLength = length; }
                }
            return bestLength < float.MaxValue;
        }

        /// <summary>Room to stand: nothing solid from his knees to his head.</summary>
        private static bool Clear(Vector3 feet) =>
            !Physics.CheckCapsule(feet + Vector3.up * 0.6f, feet + Vector3.up * 1.5f, 0.3f, SolidMask & ~LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore);

        private static readonly List<Vector3> Path = new List<Vector3>();

        /// <summary>How far he'd walk to get there (-1: he can't).</summary>
        private static float PathLength(Vector3 from, Vector3 to, Pathfinding.AgentType agent)
        {
            if (Pathfinding.instance == null || !Pathfinding.instance.GetPath(from, to, Path, agent, requireFullPath: true)) return -1f;
            float length = 0f;
            for (int i = 1; i < Path.Count; i++) length += Vector3.Distance(Path[i - 1], Path[i]);
            return length;
        }

        /// <summary>The floor under a point (from a little above it down: indoors that's the floor, not the roof).</summary>
        private static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, SolidMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            return p;
        }
    }

    /// <summary>
    /// On his object, on every game: his pose (the player's own resting and sitting animations: human rigs share them), held still while
    /// he rests, the map markers, his words over his head as things happen, and the player's side of helping and feeding him.
    /// </summary>
    internal sealed class DvergerVisitor : MonoBehaviour, Interactable
    {
        private static readonly HashSet<Character> All = new HashSet<Character>();
        internal static int Count => All.Count;
        internal static bool Is(Character c) => c != null && All.Contains(c);

        /// <summary>Resting on his bed, seat or patch of ground: "attached", as a player on a bed is (Character_IsAttached).</summary>
        internal static readonly HashSet<Character> Still = new HashSet<Character>();

        private ZNetView _nview;
        private Character _character;
        private Rigidbody _body;
        private CapsuleCollider _collider;
        private Animator _animator;
        private readonly DvergerMotion _motion = new DvergerMotion();
        private int _stage = -1, _meals = -1;
        private readonly MapPins _pins = new MapPins();
        private bool _draws;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
            _body = GetComponent<Rigidbody>();
            All.Add(_character);
            if (_nview.GetZDO() == null) return;
            _nview.Register("GoodTidings_Help", _ => { if (_nview.IsOwner()) GetComponent<DvergerAI>()?.Help(); });
            _nview.Register("GoodTidings_Feed", _ => { if (_nview.IsOwner()) GetComponent<DvergerAI>()?.Fed(); });
        }

        private void Start()
        {
            _draws = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            Transform visual = transform.Find("Visual");
            _animator = visual ? visual.GetComponent<Animator>() : GetComponentInChildren<Animator>();
            if (_draws && visual)
                try { WoundedLook.Dress(visual.gameObject); }
                catch (Exception e) { Plugin.Log.LogError($"Dressing the wounded Dvergr failed: {e}"); }
        }

        private void OnDestroy()
        {
            All.Remove(_character);
            Still.Remove(_character);
            _pins.Remove();
            _motion.Stop();
        }

        private int Stage => _nview.GetZDO()?.GetInt(DvergerAI.StageKey) ?? 0;

        private void Update()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo == null) return;
            int stage = zdo.GetInt(DvergerAI.StageKey);
            // held still on his bed or seat by his owner (others see him where the owner says)
            bool still = _nview.IsOwner() && (stage == DvergerAI.Resting || stage == DvergerAI.Healing);
            if (still) Still.Add(_character); else Still.Remove(_character);
            if (_body)
            {
                _body.constraints = still ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.FreezeRotation;
                _body.useGravity = !still;
            }
            if (_collider == null) _collider = GetComponent<CapsuleCollider>();
            if (_collider) _collider.isTrigger = still;   // (lying on the bed, not pushing against it; still there to be looked at)
            if (!_draws) return;

            _pins.Update(zdo.GetVec3(DvergerAI.HomeKey, transform.position), transform.position, "", icon: MapPins.Mystery(), circle: MapPins.Neutral);
            Animate(stage, zdo);

            int meals = zdo.GetInt(DvergerAI.MealsKey);
            if (_stage >= 0 && (stage != _stage || meals != _meals)) Say(stage, meals, zdo);
            _stage = stage;
            _meals = meals;
        }

        /// <summary>
        /// His animation for the stage, from the player's own (human rigs share them): staggering in and away (encumbered), down on one
        /// knee, walking to his rest, and resting there.
        /// </summary>
        private void Animate(int stage, ZDO zdo)
        {
            if (_animator == null) return;
            switch (stage)
            {
                case DvergerAI.Arriving:
                case DvergerAI.Neglected: _motion.Walk(_animator, "Encumbered walk", "Encumbered idle", 1.0f); break;
                case DvergerAI.Slumped: _motion.Then(_animator, "Kneel Start", "Kneel Loop", seenStart: _stage == DvergerAI.Arriving); break;
                case DvergerAI.Walking:
                case DvergerAI.Tempering:
                case DvergerAI.Thankful: _motion.Walk(_animator, "Walk New", "IdleTweaked", 1.6f); break;
                case DvergerAI.Crafting: _motion.Hold(_animator, "Workbench"); break;
                default: _motion.Hold(_animator, zdo.GetString(DvergerAI.PoseKey, DvergerAI.GroundPose)); break;
            }
            _motion.Update(transform.position, Time.deltaTime);
        }

        /// <summary>His words, over his head, when something happens (each game sees the change and shows them in its own language).</summary>
        private void Say(int stage, int meals, ZDO zdo)
        {
            string line = null;
            if (stage != _stage)
                switch (stage)
                {
                    case DvergerAI.Walking: line = Lines.Pick(_stage == DvergerAI.Resting ? "goodtidings_dverger_firefound"
                                                               : zdo.GetBool(DvergerAI.NoFireKey) ? "goodtidings_dverger_nofire" : "goodtidings_dverger_helped"); break;
                    case DvergerAI.Resting: line = Lines.Pick(zdo.GetBool(DvergerAI.NoFireKey) ? "goodtidings_dverger_cold" : "goodtidings_dverger_rest"); break;
                    case DvergerAI.Healing: line = Lines.Pick("goodtidings_dverger_full"); break;
                    case DvergerAI.Tempering: line = Lines.Pick("goodtidings_dverger_thanks"); break;
                    case DvergerAI.Thankful: line = Lines.Pick("goodtidings_dverger_done"); break;
                    case DvergerAI.Neglected: line = Lines.Pick("goodtidings_dverger_neglected"); break;
                }
            else if (meals > _meals && stage == DvergerAI.Resting) line = Lines.Pick("goodtidings_dverger_fed");
            Sounds(stage, meals);
            if (line != null && Chat.instance)
                Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 6f, "", Localization.instance.Localize(line), false);
            if (stage != _stage && stage == DvergerAI.Neglected && Player.m_localPlayer
                && Vector3.Distance(Player.m_localPlayer.transform.position, transform.position) < 80f)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$goodtidings_dverger_left");
        }

        private static EffectList _craft, _craftDone, _eat;

        /// <summary>The game's own sounds: eating (a player's), and a workbench at work and done (as an upgrade sounds).</summary>
        private void Sounds(int stage, int meals)
        {
            if (_eat == null)
            {
                _eat = ZNetScene.instance.GetPrefab("Player")?.GetComponent<Player>()?.m_consumeItemEffects ?? new EffectList();
                CraftingStation bench = ZNetScene.instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
                _craft = bench?.m_craftItemEffects ?? new EffectList();
                _craftDone = bench?.m_craftItemDoneEffects ?? new EffectList();
            }
            if (meals > _meals) _eat.Create(transform.position + Vector3.up * 1.2f, transform.rotation, transform);
            if (stage != _stage && stage == DvergerAI.Crafting) _craft.Create(transform.position + Vector3.up, transform.rotation, transform);
            if (stage != _stage && _stage == DvergerAI.Crafting) _craftDone.Create(transform.position + Vector3.up, transform.rotation);
        }

        internal string HoverText()
        {
            ZDO zdo = _nview.GetZDO();
            string name = "$goodtidings_dverger";
            if (zdo == null) return Localization.instance.Localize(name);
            switch (Stage)
            {
                case DvergerAI.Slumped:
                    return Localization.instance.Localize(name + "\n[<color=yellow><b>$KEY_Use</b></color>] $goodtidings_dverger_help");
                case DvergerAI.Resting when zdo.GetBool(DvergerAI.NoFireKey):
                    return Localization.instance.Localize(name + "\n$goodtidings_dverger_needfire");
                case DvergerAI.Resting:
                    string meals = Localization.instance.Localize("$goodtidings_dverger_meals", zdo.GetInt(DvergerAI.MealsKey).ToString(),
                        Plugin.DvergerMeals.Value.ToString());
                    return Localization.instance.Localize(name) + $" ({meals})\n" + Localization.instance.Localize("$goodtidings_dverger_feed");
                default:
                    return Localization.instance.Localize(name);
            }
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            switch (Stage)
            {
                case DvergerAI.Slumped:
                    _nview.InvokeRPC("GoodTidings_Help");
                    return true;
                case DvergerAI.Resting:
                    user.Message(MessageHud.MessageType.Center, _nview.GetZDO().GetBool(DvergerAI.NoFireKey) ? "$goodtidings_dverger_needfire" : "$goodtidings_dverger_hungry");
                    return true;
            }
            return false;
        }

        /// <summary>Food from the hotbar while looking at him: one is taken from this player and his owner counts the meal.</summary>
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (user != Player.m_localPlayer || item == null) return false;
            ZDO zdo = _nview.GetZDO();
            if (zdo == null || Stage != DvergerAI.Resting) return false;
            if (zdo.GetInt(DvergerAI.MealsKey) >= Plugin.DvergerMeals.Value) return false;
            if (zdo.GetBool(DvergerAI.NoFireKey)) { user.Message(MessageHud.MessageType.Center, "$goodtidings_dverger_needfire"); return true; }
            if (item.m_shared.m_food <= 0f) { user.Message(MessageHud.MessageType.Center, "$goodtidings_dverger_notfood"); return true; }
            if (!user.GetInventory().RemoveOneItem(item)) return false;
            _nview.InvokeRPC("GoodTidings_Feed");
            user.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$goodtidings_dverger_gave", item.m_shared.m_name));
            return true;
        }
    }
}

namespace GoodTidings
{
    /// <summary>
    /// The wounded Dvergr's animation, played over his own animator (his walk holds a crossbow): the player's clips, copied without their
    /// events (those call the character's own code: the player's death clip has a "Die"). Three ways: a walk and an idle mixed by how fast
    /// he's moving (seen from his position, so it's right on every game), one clip after another (down on one knee, then kneeling), or
    /// one clip held (resting).
    /// </summary>
    internal sealed class DvergerMotion
    {
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mix;
        private AnimationClipPlayable _a, _b;
        private string _key;
        private int _mode;   // 1 walk, 2 one then another, 3 hold
        private float _naturalSpeed, _speed;
        private Vector3 _last;
        private bool _hasLast;

        internal void Walk(Animator anim, string walk, string idle, float naturalSpeed)
        {
            if (Build(anim, "walk:" + walk, walk, idle)) { _mode = 1; _naturalSpeed = naturalSpeed; }
        }

        internal void Then(Animator anim, string first, string then, bool seenStart)
        {
            if (!Build(anim, "then:" + first, first, then)) return;
            _mode = 2;
            if (!seenStart) _a.SetTime(_a.GetAnimationClip().length);   // (already down when this game first saw him)
        }

        internal void Hold(Animator anim, string clip)
        {
            if (Build(anim, "hold:" + clip, clip, clip)) _mode = 3;
        }

        internal void Update(Vector3 position, float dt)
        {
            if (!_graph.IsValid() || dt <= 0f) return;
            float wA;
            switch (_mode)
            {
                case 1:
                {
                    Vector3 d = _hasLast ? position - _last : Vector3.zero;
                    d.y = 0f;
                    _last = position;
                    _hasLast = true;
                    _speed = Mathf.Lerp(_speed, Mathf.Min(d.magnitude / dt, 4f), 1f - Mathf.Exp(-dt * 6f));
                    wA = Mathf.Clamp01(_speed / 0.5f);
                    _a.SetSpeed(Mathf.Clamp(_speed / _naturalSpeed, 0.6f, 1.4f));
                    break;
                }
                case 2:
                    wA = _a.GetTime() < _a.GetAnimationClip().length - 0.15f ? 1f : 0f;
                    break;
                default:
                    wA = 1f;
                    break;
            }
            _mix.SetInputWeight(0, wA);
            _mix.SetInputWeight(1, 1f - wA);
        }

        internal void Stop()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _key = null;
        }

        private bool Build(Animator anim, string key, string a, string b)
        {
            if (key == _key && _graph.IsValid()) return false;
            Stop();
            AnimationClip ca = Quiet(a), cb = Quiet(b);
            if (ca == null || cb == null) { Plugin.Log.LogWarning($"No {a} or {b} animation for the wounded Dvergr"); return false; }
            _key = key;
            _graph = PlayableGraph.Create("goodtidings_dverger");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mix = AnimationMixerPlayable.Create(_graph, 2);
            _a = AnimationClipPlayable.Create(_graph, ca);
            _b = AnimationClipPlayable.Create(_graph, cb);
            _graph.Connect(_a, 0, _mix, 0);
            _graph.Connect(_b, 0, _mix, 1);
            _mix.SetInputWeight(0, 1f);
            AnimationPlayableOutput.Create(_graph, "anim", anim).SetSourcePlayable(_mix);
            _graph.Play();
            _hasLast = false;
            _speed = 0f;
            return true;
        }

        private static readonly Dictionary<string, AnimationClip> QuietClips = new Dictionary<string, AnimationClip>();

        private static AnimationClip Quiet(string name)
        {
            if (QuietClips.TryGetValue(name, out AnimationClip quiet) && quiet) return quiet;
            Animator player = ZNetScene.instance.GetPrefab("Player")?.GetComponentInChildren<Animator>(true);
            AnimationClip clip = player?.runtimeAnimatorController?.animationClips.FirstOrDefault(c => c.name == name);
            if (clip == null) return null;
            quiet = UnityEngine.Object.Instantiate(clip);
            quiet.name = clip.name;
            quiet.events = new AnimationEvent[0];
            QuietClips[name] = quiet;
            return quiet;
        }
    }
}

namespace GoodTidings
{
    /// <summary>The wounded Dvergr's clothes (as Dagny's are made): the Ashlands outfit, without its hood and cape, in grey-white.</summary>
    internal static class WoundedLook
    {
        internal const float Brightness = 1.05f;

        internal static Cuts.Tint Tint(float value = Brightness)
        {
            var tint = new Cuts.Tint { Grey = true, Value = value, Keep = { new RectInt(108, 0, 20, 20) } };   // (the buckle keeps its colour)
            tint.Cut.Add(new RectInt(64, 96, 64, 32));   // (no cape)
            return tint;
        }

        internal static void Dress(GameObject body, float value = Brightness) =>
            DvergerLook.Outfit(body, TraderLook.Ashlands, DvergerLook.Keep.NoHead, 1f, Tint(value));
    }
}

namespace GoodTidings
{
    /// <summary>
    /// The wounded Dvergr resting counts as attached, as a player lying on a bed does: the game then moves his body to the layer that
    /// doesn't push against the bed and stops his movement (he shook, held in place against both).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.IsAttached))]
    internal static class Character_IsAttached
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            if (!__result && DvergerVisitor.Still.Count > 0 && DvergerVisitor.Still.Contains(__instance)) __result = true;
        }
    }
}
