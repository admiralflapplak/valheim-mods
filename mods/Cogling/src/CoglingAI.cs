using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The Cogling's brain, in place of the Dverger's MonsterAI (which only wanders and fights). It runs on the game that owns him (as
    /// every creature's AI does); the others see him move through the network as usual. For now (step 3, walking) he keeps to his
    /// antennas' area: he rests at the nearest antenna, and now and then strolls to a spot in the area and back, at a peaceful pace,
    /// through doors (CoglingNavigator). Jobs come later and use the same navigator.
    /// </summary>
    internal sealed class CoglingAI : BaseAI
    {
        private enum Mode { Resting, Strolling, GoingHome, Working }

        private CoglingNavigator _nav;
        private CoglingJob _job;
        private float _nextJobLook, _nextTidy;
        private Mode _mode = Mode.GoingHome;
        private float _nextStroll;
        private Vector3 _home;
        private bool _haveHome;

        private static readonly string[] Rpcs = { "Alert", "OnNearProjectileHit", "SetAggravated", "RPC_Wakeup", "RPC_Sleep" };
        private static readonly MethodInfo BaseOnDestroy = AccessTools.Method(typeof(BaseAI), "OnDestroy");
        private static readonly AccessTools.FieldRef<Character, BaseAI> CharacterAI = AccessTools.FieldRefAccess<Character, BaseAI>("m_baseAI");

        private bool _duplicate;

        protected override void Awake()
        {
            // one brain per Cogling: a hot reload could leave two (his body added one before the old one was swapped for this copy's),
            // and two brains gave him orders every frame
            if (GetComponents<BaseAI>().Any(a => a != this && a != null && a.GetType().Name == nameof(CoglingAI)))
            {
                _duplicate = true;
                enabled = false;
                Destroy(this);
                return;
            }
            // a fresh component has defaults (PieceReload gives standing Coglings this copy's, and old ones had MonsterAI): the
            // Dverger's AI settings first, then ours
            BaseAI from = ZNetScene.instance?.GetPrefab("Dverger")?.GetComponent<BaseAI>();
            if (from != null)
                foreach (FieldInfo f in typeof(BaseAI).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    f.SetValue(this, f.GetValue(from));
            m_viewRange = 0f;
            m_hearRange = 0f;
            m_idleSound = new EffectList();
            m_alertedEffects = new EffectList();
            m_pathAgentType = Pathfinding.AgentType.Humanoid;
            m_randomMoveRange = 0f;
            // a player's hit only lands on a friendly creature that can be aggravated (the Dverger can) or with PvP on: like a tame,
            // players hurt him only in PvP (monsters still can)
            m_aggravatable = false;
            // the RPCs the old brain registered on this object would make registering them again throw
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.IsValid()) foreach (string rpc in Rpcs) view.Unregister(rpc);
            if (GetComponent<Character>() is Character c0) DropStaleListeners(c0);
            base.Awake();
            if (GetComponent<Character>() is Character c) CharacterAI(c) = this;
            _nav = new CoglingNavigator(this);
            _job = new CoglingJob(this, _nav);
            _nextStroll = Time.time + Random.Range(5f, 15f);
        }

        private void OnDestroy()
        {
            if (_duplicate) return;
            // the death and damage calls this brain signed up for (BaseAI.Awake): a brain swapped out by a hot reload left them behind,
            // the Cogling's death called the dead brain first, threw, and he stood there at 0 health
            if (m_character != null)
            {
                m_character.m_onDeath = (System.Action)System.Delegate.Remove(m_character.m_onDeath, new System.Action(OnDeath));
                m_character.m_onDamaged = (System.Action<float, Character>)System.Delegate.Remove(m_character.m_onDamaged, new System.Action<float, Character>(OnDamaged));
            }
            BaseOnDestroy?.Invoke(this, null);   // (the game's own clean-up, which this method hides)
            _job?.Abandon();                     // (what he carries stays saved on him: unloaded, he goes on later)
            _nav?.CloseDoors(force: true);
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.IsValid()) foreach (string rpc in Rpcs) view.Unregister(rpc);
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt)) return false;   // (not ours to run: another player's game moves him)
            if (FinishStuckDeath()) return true;
            m_character.m_walkSpeed = Plugin.WalkSpeed.Value;
            if (Plugin.Paused.Value)
            {
                // testing: he stands still and faces the nearest player (to look at a pose); what he was doing waits
                StopMoving();
                Player near = Player.GetClosestPlayer(transform.position, 30f);
                if (near != null) LookAt(near.transform.position);
                return true;
            }
            _nav.CloseDoors(force: false);

            if (Time.time > _nextNetwork) { _network = Antenna.NetworkAt(transform.position); _nextNetwork = Time.time + 2f; }
            List<Antenna> network = _network;
            network.RemoveAll(a => a == null);
            if (network.Count == 0) { StopMoving(); return true; }   // no antenna loaded near: he waits
            if (!_haveHome || !Antenna.InArea(network, _home) || Time.time > _nextHomeCheck) PickHome(network);
            if (!PoweredUp && _mode != Mode.Resting) { Log($"awake: not resting ({_mode})"); Wake(); }   // (built or loaded powered down, but not resting at home: up, and home)
            if (!PoweredUp && _mode == Mode.Resting && Utils.DistanceXZ(transform.position, _home) > 2.5f) { Log($"awake: {Utils.DistanceXZ(transform.position, _home):F1} m from home"); Wake(); _mode = Mode.GoingHome; _nav.SetTarget(_home); }

            // work first: something carried from before, else an item on the floor (looked for every few seconds)
            if (_mode != Mode.Working && Time.time > _nextJobLook)
            {
                _nextJobLook = Time.time + 3f;
                if ((_job.Carrying && _job.Resume(network)) || (!_job.Carrying && _job.TryStart(network))) { _mode = Mode.Working; Wake(); }
                else if (!_job.Carrying && Time.time > _nextTidy)
                {
                    // nothing on the floor: the chests, now and then (it reads every chest of the area)
                    _nextTidy = Time.time + 8f;
                    if (_job.TryStartTidy(network)) { _mode = Mode.Working; Wake(); }
                }
            }

            switch (_mode)
            {
                case Mode.Working:
                    if (Time.time < _bootUntil) { StopMoving(); break; }   // (booting up: eyes on, standing up)
                    if (_job.Update(dt) == CoglingJob.State.Done)
                    {
                        _mode = Mode.GoingHome;
                        _nav.SetTarget(_home);
                        _nextJobLook = Time.time + 1f;
                    }
                    break;
                case Mode.Resting:
                    StopMoving();
                    // nothing to do for a while at his antenna: he powers down (and boots up when there's work)
                    if (PoweredUp && Time.time - _restingSince > Plugin.PowerDownAfter.Value) { SetAwake(false); Log("powering down"); }
                    if (!PoweredUp) break;
                    if (Time.time > _nextStroll && Plugin.StrollEvery.Value > 0f)
                    {
                        Vector3? spot = StrollSpot(network);
                        if (spot.HasValue && _nav.SetTarget(spot.Value)) { _mode = Mode.Strolling; Log($"strolling to {spot.Value:F1}"); }
                        _nextStroll = Time.time + Plugin.StrollEvery.Value * Random.Range(0.7f, 1.3f);
                    }
                    break;
                case Mode.Strolling:
                case Mode.GoingHome:
                    var result = _nav.Update(dt);
                    if (result == CoglingNavigator.Result.Arrived || result == CoglingNavigator.Result.GaveUp)
                    {
                        Log(result == CoglingNavigator.Result.GaveUp ? $"gave up on {_nav.Target:F1}" : $"arrived at {transform.position:F1}");
                        if (_mode == Mode.Strolling) { _mode = Mode.GoingHome; _nav.SetTarget(_home); Log("going home"); }
                        else { _mode = Mode.Resting; _restingSince = Time.time; Log("resting"); }
                    }
                    break;
            }
            return true;
        }

        private float _nextHomeCheck, _nextNetwork;
        private List<Antenna> _network = new List<Antenna>();

        /// <summary>His resting spot: in front of the nearest antenna of his network (antennas face where they were placed facing).</summary>
        private void PickHome(List<Antenna> network)
        {
            _nextHomeCheck = Time.time + 10f;
            Antenna nearest = network.OrderBy(a => Utils.DistanceXZ(a.transform.position, transform.position)).First();
            Vector3 spot = nearest.transform.position + nearest.transform.forward * 1.3f;
            if (!_haveHome || Utils.DistanceXZ(spot, _home) > 0.5f)
            {
                _home = spot;
                _haveHome = true;
                if (_mode == Mode.Resting && Utils.DistanceXZ(transform.position, _home) > 2f) { _mode = Mode.GoingHome; _nav.SetTarget(_home); }
                else if (_mode == Mode.GoingHome) _nav.SetTarget(_home);
            }
        }

        /// <summary>A random spot in the area he can walk to (a few tries), or null.</summary>
        private Vector3? StrollSpot(List<Antenna> network)
        {
            for (int i = 0; i < 6; i++)
            {
                Antenna a = network[Random.Range(0, network.Count)];
                Vector2 r = Random.insideUnitCircle * Antenna.Radius * 0.8f;
                Vector3 p = a.transform.position + new Vector3(r.x, 0f, r.y);
                if (!ZoneSystem.instance.GetSolidHeight(p, out float height)) continue;
                p.y = height;
                if (Antenna.InArea(network, p)) return p;
            }
            return null;
        }

        /// <summary>Testing: walk to a spot (then home, as after a stroll). False if no way there was found at all.</summary>
        internal bool SendTo(Vector3 spot)
        {
            Wake();
            if (_mode == Mode.Working) { _job.Abandon(); }
            if (!_nav.SetTarget(spot)) return false;
            _mode = Mode.Strolling;
            Log($"sent to {spot:F1}");
            return true;
        }

        internal void GoHome()
        {
            Wake();
            _mode = Mode.GoingHome;
            if (_haveHome) _nav.SetTarget(_home);
        }

        internal void Log(string what)
        {
            if (Plugin.LogAI.Value) Plugin.Log.LogInfo($"Cogling AI {GetComponent<ZNetView>()?.GetZDO()?.m_uid.ID}: {what}");
        }

        /// <summary>Taken down or broken: what he carries falls where he stands.</summary>
        private float _deadSince = -1f;

        /// <summary>
        /// A death that never finished: the game marks a creature dead before its death code runs, and when that code stopped partway (a
        /// brain left over from an earlier copy of the mod threw), he stood there "dead" at 0 health, and every hit was ignored. A second
        /// on, he finishes it: what he carries falls, he falls apart (his death effects), and he's gone. His cost isn't given again: the
        /// killing hit already dropped it (Character.ApplyDamage).
        /// </summary>
        private bool FinishStuckDeath()
        {
            if (m_character == null || !m_character.IsDead()) { _deadSince = -1f; return false; }
            if (_deadSince < 0f) { _deadSince = Time.time; return true; }
            if (Time.time - _deadSince < 1f) return true;
            Log("finishing a death that got stuck");
            _job?.Drop();
            if (m_character is Humanoid h) h.m_deathEffects.Create(transform.position, transform.rotation, transform);
            ZNetScene.instance.Destroy(gameObject);
            return true;
        }

        /// <summary>Calls to brains that are gone (left by earlier copies of the mod), taken off the Cogling.</summary>
        private static void DropStaleListeners(Character c)
        {
            if (c.m_onDeath != null)
                foreach (System.Delegate d in c.m_onDeath.GetInvocationList())
                    if (d.Target is Object o && o == null) c.m_onDeath = (System.Action)System.Delegate.Remove(c.m_onDeath, d);
            if (c.m_onDamaged != null)
                foreach (System.Delegate d in c.m_onDamaged.GetInvocationList())
                    if (d.Target is Object o && o == null) c.m_onDamaged = (System.Action<float, Character>)System.Delegate.Remove(c.m_onDamaged, d);
        }

        protected override void OnDeath()
        {
            if (this == null) return;   // (a brain already gone: never let it stop the death)
            if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) _job?.Drop();
            base.OnDeath();
        }

        internal void Face(Vector3 point) => LookAt(point);

        // ---- powered down / up (saved on him, so every player's game shows it: his body plays it, CoglingBody)

        private float _restingSince, _bootUntil;
        private bool PoweredUp => m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetBool(Carried.AwakeKey);

        private void SetAwake(bool awake)
        {
            if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) m_nview.GetZDO().Set(Carried.AwakeKey, awake);
        }

        /// <summary>Powered down: boot up (eyes flicker on, he straightens up) before he moves off.</summary>
        private void Wake()
        {
            if (PoweredUp) return;
            SetAwake(true);
            _bootUntil = Time.time + 1.8f;
            Log("booting up");
        }

        // ---- BaseAI's helpers for the navigator (protected there)
        internal void Walk(Vector3 dir) => MoveTowards(dir, run: false);
        internal void Stop() => StopMoving();
        internal Character Body => m_character;

        // ---- what a MonsterAI answers that other code asks every creature's AI
        public override bool IsSleeping() => false;
        public override Character GetTargetCreature() => null;
        public override bool HuntPlayer() => false;

        /// <summary>A Cogling standing from before he had this brain (MonsterAI): given this one.</summary>
        internal static void Install(GameObject cog)
        {
            // (any copy of the mod's brain: a hot reload gives each its own type)
            if (cog.GetComponents<BaseAI>().Any(a => a != null && a.GetType().Name == nameof(CoglingAI))) return;
            foreach (MonsterAI old in cog.GetComponents<MonsterAI>()) DestroyImmediate(old);
            cog.AddComponent<CoglingAI>();
        }
    }

}
