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
        private float _nextJobLook;
        private Mode _mode = Mode.GoingHome;
        private float _nextStroll;
        private Vector3 _home;
        private bool _haveHome;

        private static readonly string[] Rpcs = { "Alert", "OnNearProjectileHit", "SetAggravated", "RPC_Wakeup", "RPC_Sleep" };
        private static readonly MethodInfo BaseOnDestroy = AccessTools.Method(typeof(BaseAI), "OnDestroy");
        private static readonly AccessTools.FieldRef<Character, BaseAI> CharacterAI = AccessTools.FieldRefAccess<Character, BaseAI>("m_baseAI");

        protected override void Awake()
        {
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
            // the RPCs the old brain registered on this object would make registering them again throw
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.IsValid()) foreach (string rpc in Rpcs) view.Unregister(rpc);
            base.Awake();
            if (GetComponent<Character>() is Character c) CharacterAI(c) = this;
            _nav = new CoglingNavigator(this);
            _job = new CoglingJob(this, _nav);
            _nextStroll = Time.time + Random.Range(5f, 15f);
        }

        private void OnDestroy()
        {
            BaseOnDestroy?.Invoke(this, null);   // (the game's own clean-up, which this method hides)
            _job?.Abandon();                     // (what he carries stays saved on him: unloaded, he goes on later)
            _nav?.CloseDoors(force: true);
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.IsValid()) foreach (string rpc in Rpcs) view.Unregister(rpc);
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt)) return false;   // (not ours to run: another player's game moves him)
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

            // work first: something carried from before, else an item on the floor (looked for every few seconds)
            if (_mode != Mode.Working && Time.time > _nextJobLook)
            {
                _nextJobLook = Time.time + 3f;
                if ((_job.Carrying && _job.Resume(network)) || (!_job.Carrying && _job.TryStart(network))) _mode = Mode.Working;
            }

            switch (_mode)
            {
                case Mode.Working:
                    if (_job.Update(dt) == CoglingJob.State.Done)
                    {
                        _mode = Mode.GoingHome;
                        _nav.SetTarget(_home);
                        _nextJobLook = Time.time + 1f;
                    }
                    break;
                case Mode.Resting:
                    StopMoving();
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
                        else { _mode = Mode.Resting; Log("resting"); }
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
            if (_mode == Mode.Working) { _job.Abandon(); }
            if (!_nav.SetTarget(spot)) return false;
            _mode = Mode.Strolling;
            Log($"sent to {spot:F1}");
            return true;
        }

        internal void GoHome()
        {
            _mode = Mode.GoingHome;
            if (_haveHome) _nav.SetTarget(_home);
        }

        internal void Log(string what)
        {
            if (Plugin.LogAI.Value) Plugin.Log.LogInfo($"Cogling AI {GetComponent<ZNetView>()?.GetZDO()?.m_uid.ID}: {what}");
        }

        /// <summary>Taken down or broken: what he carries falls where he stands.</summary>
        protected override void OnDeath()
        {
            if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) _job?.Drop();
            base.OnDeath();
        }

        internal void Face(Vector3 point) => LookAt(point);

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
            if (cog.GetComponent<CoglingAI>() != null) return;
            foreach (MonsterAI old in cog.GetComponents<MonsterAI>()) DestroyImmediate(old);
            cog.AddComponent<CoglingAI>();
        }
    }

}
