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
        private enum Mode { Resting, Strolling, GoingHome, Working, Returning, Slacking }
        private float _nextReturnPlan;

        private CoglingNavigator _nav;
        private CoglingJob _job;
        private CoglingDanger _danger;
        private CoglingSlack _slack;
        internal bool Running;
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
            _danger = new CoglingDanger(this);
            _slack = new CoglingSlack(this, _nav, _job);
            SetMood(Carried.MoodCalm);   // (a fright saved when his area unloaded mid-flight would leave his eyes red)
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
            _slack?.Dispose();
            _nav?.CloseDoors(force: true);
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.IsValid()) foreach (string rpc in Rpcs) view.Unregister(rpc);
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt)) return false;   // (not ours to run: another player's game moves him)
            _lastUpdate = Time.time;
            if (FinishStuckDeath()) return true;
            UpdateTask();
            if (Time.time < _greetUntil) { StopMoving(); if (_greeter != Vector3.zero && !_slack.Seated) LookAt(_greeter); return true; }   // (greeted: a moment for you)
            m_character.m_walkSpeed = Plugin.WalkSpeed.Value;
            m_character.m_runSpeed = Plugin.RunSpeed.Value;
            if (Plugin.Paused.Value)
            {
                // testing: he stands still and faces the nearest player (to look at a pose); what he was doing waits
                StopMoving();
                Player near = Player.GetClosestPlayer(transform.position, 30f);
                if (near != null) LookAt(near.transform.position);
                return true;
            }
            _nav.CloseDoors(force: false);

            // danger before anything, wherever he is: an enemy near or a hit, and he runs for cover (what he was doing waits; what he
            // carries stays his). (It came after the "no antenna here: wait" check: once he fled out of his area he froze there.)
            if (_danger.Update(_nav))
            {
                if (!PoweredUp) SetAwake(true);   // (no time to boot up slowly)
                if (_mode == Mode.Working) _job.Abandon();
                _slack.Stop();                    // (a break ends at once: up and off)
                _mode = Mode.GoingHome;           // (afterwards: home, and work again if there is any)
                _homeAfterFright = true;
                return true;
            }

            if (Time.time < _holdUntil) { StopMoving(); return true; }   // (a gesture: he stands still for it)
            LookForPlayers();

            if (Time.time > _nextNetwork) { _network = Antenna.NetworkAt(transform.position); _nextNetwork = Time.time + 2f; }
            List<Antenna> network = _network;
            network.RemoveAll(a => a == null);
            if (network.Count == 0)
            {
                // outside every antenna's area (fled, or pushed): back to the nearest antenna there is; none loaded: he waits
                Antenna back = Antenna.Nearest(transform.position, 80f);
                if (back == null) { StopMoving(); return true; }
                if (!PoweredUp) SetAwake(true);
                if (_mode == Mode.Slacking) _slack.Stop();
                if (_mode != Mode.Returning || Time.time > _nextReturnPlan)
                {
                    _mode = Mode.Returning;
                    _nextReturnPlan = Time.time + 5f;
                    _nav.SetTarget(back.transform.position + back.transform.forward * 1.3f);
                    Log("outside his area: back to the antenna");
                }
                if (_nav.Update(dt) == CoglingNavigator.Result.GaveUp) _nextReturnPlan = 0f;
                return true;
            }
            if (_mode == Mode.Returning) { _mode = Mode.GoingHome; _haveHome = false; }
            if (!_haveHome || !Antenna.InArea(network, _home) || Time.time > _nextHomeCheck) PickHome(network);
            if (_haveHome && _mode == Mode.GoingHome && _nav.Target != _home && !_homeAfterFright && Utils.DistanceXZ(_nav.Target, _home) > 0.5f && _job != null && !_job.Carrying) _nav.SetTarget(_home);
            if (!PoweredUp && _mode != Mode.Resting) { Log($"awake: not resting ({_mode})"); Wake(); }   // (built or loaded powered down, but not resting at home: up, and home)
            if (!PoweredUp && _mode == Mode.Resting && Utils.DistanceXZ(transform.position, _home) > 2.5f) { Log($"awake: {Utils.DistanceXZ(transform.position, _home):F1} m from home"); Wake(); _mode = Mode.GoingHome; _nav.SetTarget(_home); }

            if (_homeAfterFright) { _homeAfterFright = false; _nav.SetTarget(_home); _nextJobLook = 0f; }

            // on a break (CoglingSlack): it goes on till it's over, then home and work again
            if (_mode == Mode.Slacking)
            {
                if (Time.time < _bootUntil) { StopMoving(); return true; }
                if (!_slack.Update(dt)) { _mode = Mode.GoingHome; _nav.SetTarget(_home); _nextJobLook = Time.time + 1f; }
                return true;
            }
            // now and then a break, even with work to do (powered down, he boots up for it)
            if (Time.time >= _bootUntil && _slack.Roll(_mode == Mode.Working, network)) { Wake(); _mode = Mode.Slacking; return true; }

            // work first: something carried from before, else an item on the floor (looked for every few seconds)
            if (_mode != Mode.Working && Time.time > _nextJobLook)
            {
                _nextJobLook = Time.time + 3f;
                if ((_job.Carrying && _job.Resume(network)) || (!_job.Carrying && _job.TryStart(network))) { _mode = Mode.Working; Wake(); }
                else if (!_job.Carrying && Time.time > _nextTidy)
                {
                    // nothing on the floor: the chests, now and then (it reads every chest of the area)
                    _nextTidy = Time.time + 8f;
                    if (_job.TryStartTidy(network) || _job.TryStartRepair(network) || _job.TryStartGear(network)) { _mode = Mode.Working; Wake(); }
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
                    if (PoweredUp && Time.time - _restingSince > Plugin.PowerDownAfter.Value) { Say(CoglingVoice.Line.PowerDown); SetAwake(false); Log("powering down"); }
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
            _slack.Stop();
            if (!_nav.SetTarget(spot)) return false;
            _mode = Mode.Strolling;
            Log($"sent to {spot:F1}");
            return true;
        }

        // ---- chatting (CoglingSlack): asked by another Cogling

        /// <summary>Free to be asked for a chat: not frightened or wary, not on a break or greeted, in his area.</summary>
        internal bool CanChat => _danger != null && !_danger.Afraid && !_danger.Wary && _mode != Mode.Slacking && _mode != Mode.Returning && Time.time >= _greetUntil && !Plugin.Paused.Value;
        internal bool IsWorking => _mode == Mode.Working;

        /// <summary>Another Cogling asks for a chat (he has already said yes): what he was doing waits, and off he goes.</summary>
        internal bool JoinChat(CoglingSlack host, Vector3 spot)
        {
            if (!CanChat) return false;
            if (_mode == Mode.Working) _job.Abandon();
            if (!_slack.Join(host, spot)) return false;
            Wake();
            _mode = Mode.Slacking;
            return true;
        }

        /// <summary>Asked to another's dance (to dance along, or cheer): what he was doing waits.</summary>
        internal bool JoinDance(CoglingSlack dancer, bool dance, int index)
        {
            if (!CanChat) return false;
            if (_mode == Mode.Working) _job.Abandon();
            if (!_slack.JoinDance(dancer, dance, index)) return false;
            Wake();
            _mode = Mode.Slacking;
            return true;
        }

        internal void GoHome()
        {
            Wake();
            _mode = Mode.GoingHome;
            if (_haveHome) _nav.SetTarget(_home);
        }

        internal string Status() =>
            $"mode {_mode}, powered up {PoweredUp}, enabled {enabled}, owner {m_nview?.IsOwner()}, in the AI list {BaseAI.BaseAIInstances.Contains(this)}, "
            + $"brains here {GetComponents<BaseAI>().Length}, running {Running}, {_danger?.Status()}, carrying {_job?.Carrying}, {_slack?.Status()}, updated {Time.time - _lastUpdate:F1} s ago";

        /// <summary>Testing (`cogling slack [what]`): a break now. False if that one can't be had here.</summary>
        internal bool SlackNow(CoglingSlack.Activity? what)
        {
            List<Antenna> network = Antenna.NetworkAt(transform.position);
            if (network.Count == 0) return false;
            CoglingSlack.Activity a = what ?? (CoglingSlack.Activity)Random.Range(1, 7);
            if (_mode == Mode.Working) _job.Abandon();
            if (!_slack.Start(a, network, force: true)) return false;
            Wake();
            _mode = Mode.Slacking;
            return true;
        }

        private float _lastUpdate;

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

        // ---- what he says (CoglingVoice: every player's game hears it) and the gesture it may come with

        private readonly Dictionary<CoglingVoice.Line, float> _saidAt = new Dictionary<CoglingVoice.Line, float>();
        private float _holdUntil, _lastSaid;

        /// <summary>
        /// A line, if he feels like it: how likely by the line (a chatty, sociable one says the small ones more often), never the same
        /// one twice within a while, and small ones not right after another line. A gesture may come with it: he stands still for it.
        /// </summary>
        internal void Say(CoglingVoice.Line line)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner() || !CoglingVoice.Specs.TryGetValue(line, out CoglingVoice.Spec spec)) return;
            bool small = spec.Chance < 1f;
            float gap = line == CoglingVoice.Line.Nervous || line == CoglingVoice.Line.Relieved ? 20f : line == CoglingVoice.Line.Hurt ? 1.5f : small ? 4f : 0.5f;
            if (_saidAt.TryGetValue(line, out float at) && Time.time - at < gap) return;
            if (small && Time.time - _lastSaid < 1.5f) return;
            float social = _slack != null ? _slack.Me.Sociability : 0.5f;
            if (Random.value >= (small ? Mathf.Clamp01(spec.Chance * (0.6f + social * 0.9f)) : spec.Chance)) return;
            _saidAt[line] = Time.time;
            _lastSaid = Time.time;
            int gesture = -1;
            if (spec.Gestures != null && Random.value < spec.GestureChance && (_danger == null || !_danger.Afraid) && (_slack == null || !_slack.Seated))
            {
                string name = spec.Gestures[Random.Range(0, spec.Gestures.Length)];
                gesture = System.Array.IndexOf(CoglingVoice.Gestures, name);
                if (gesture >= 0 && _mode != Mode.Slacking) _holdUntil = Time.time + Mathf.Min(3f, CoglingBody.GestureLength(name));   // (on a break he stands anyway)
            }
            m_nview.InvokeRPC(ZNetView.Everybody, CoglingVoice.SayRpc, (int)line, gesture);
        }

        // ---- a player coming home: he waves (once a player has been away a while)

        private float _nextPlayerLook;

        /// <summary>
        /// A player home after a while away (90 s, no player within 30 m): welcomed when he comes close. When a player was last near is
        /// saved on him (world time), so a long trip counts: far away, his area unloads and he's made again when the player is back.
        /// Powered down, he boots up for it (and powers down again later, as ever).
        /// </summary>
        private void LookForPlayers()
        {
            if (Time.time < _nextPlayerLook || m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner() || ZNet.instance == null) return;
            _nextPlayerLook = Time.time + 1f;
            if (_welcomeAfterBoot)
            {
                // (booted up to say it)
                if (Time.time < _bootUntil) return;
                _welcomeAfterBoot = false;
                Player p = Player.GetClosestPlayer(transform.position, 15f);
                if (p != null) { LookAt(p.transform.position); Say(CoglingVoice.Line.Welcome); }
                return;
            }
            if (Player.GetClosestPlayer(transform.position, 30f) == null) return;
            ZDO zdo = m_nview.GetZDO();
            long now = ZNet.instance.GetTime().Ticks, last = zdo.GetLong(SeenKey, 0L);
            bool away = last != 0L && (now - last) / (double)System.TimeSpan.TicksPerSecond > 90.0;
            Player close = Player.GetClosestPlayer(transform.position, 12f);
            if (away && close == null) return;   // (seen far off: still to be welcomed when he comes close)
            if (away || Time.time >= _nextSeenWrite) { zdo.Set(SeenKey, now); _nextSeenWrite = Time.time + 5f; }
            if (!away || (_slack != null && _slack.Seated) || (_danger != null && (_danger.Afraid || _danger.Wary))) return;
            if (!PoweredUp)
            {
                if (_mode != Mode.Resting) return;
                Wake();
                _restingSince = Time.time;   // (up a while before he powers down again)
                _welcomeAfterBoot = true;
                Log("booting up to welcome a player home");
                return;
            }
            LookAt(close.transform.position);
            Say(CoglingVoice.Line.Welcome);
        }

        private const string SeenKey = "cogling_seen";
        private bool _welcomeAfterBoot;
        private float _nextSeenWrite;

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
            Say(CoglingVoice.Line.BootUp);
        }

        // ---- BaseAI's helpers for the navigator (protected there)
        internal void Walk(Vector3 dir) => MoveTowards(dir, run: Running);

        private bool _homeAfterFright;

        // ---- greeted (E): he turns to you for a moment (his body and voice do the rest on every game)

        private float _greetUntil;
        private Vector3 _greeter;

        internal void Greeted(Vector3 from)
        {
            if (!PoweredUp) SetAwake(true);
            _greeter = from;
            _greetUntil = Time.time + 2.6f;
        }

        // ---- the task line under his name (saved on him; written only when it changes)

        private string _task = "";

        private void UpdateTask()
        {
            (string key, string a, string b) t;
            if (_danger != null && _danger.Afraid) t = ("cogling_task_hiding", _danger.ThreatName, "");
            else if (_danger != null && _danger.Wary) t = ("cogling_task_watching", _danger.ThreatName, "");
            else if (!PoweredUp) t = ("cogling_task_off", "", "");
            else if (Time.time < _bootUntil) t = ("cogling_task_booting", "", "");
            else if (_mode == Mode.Working && _job != null) t = _job.Describe();
            else if (_mode == Mode.Slacking && _slack != null) t = _slack.Describe();
            else if (_mode == Mode.GoingHome || _mode == Mode.Returning) t = ("cogling_task_home", "", "");
            else t = ("cogling_task_resting", "", "");
            string task = $"{t.key}|{t.a}|{t.b}";
            if (task == _task || m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner()) return;
            _task = task;
            m_nview.GetZDO().Set(CoglingTag.TaskKey, task);
        }

        // ---- the game's own icons over his name plate: "aware" (searching) while wary, "alerted" (red !) while frightened

        private static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, bool> AlertedField = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, bool>("m_alerted");

        internal void SetIcons(bool aware, bool alerted)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner()) return;
            ZDO zdo = m_nview.GetZDO();
            if (zdo.GetBool(ZDOVars.s_haveTargetHash) != aware) zdo.Set(ZDOVars.s_haveTargetHash, aware);
            if (AlertedField(this) != alerted) { AlertedField(this) = alerted; zdo.Set(ZDOVars.s_alert, alerted); }   // (not SetAlerted: it changes the walk to a fight stance)
        }

        /// <summary>His mood, saved on him (every player's game shows it).</summary>
        internal void SetMood(int mood)
        {
            if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) m_nview.GetZDO().Set(Carried.MoodKey, mood);
        }

        protected override void OnDamaged(float damage, Character attacker)
        {
            base.OnDamaged(damage, attacker);
            if (this != null) { _danger?.Hurt(attacker); Say(CoglingVoice.Line.Hurt); }
        }
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
