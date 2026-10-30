using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoodTidings
{
    /// <summary>
    /// Muninn's Riddle (a neutral visit): Odin's raven of memory lands at the base and asks one riddle, first come first served. The raven
    /// is our own saved, shared object ("goodtidings_muninn": its leave time and answer in its ZDO); each game builds his look from a copy
    /// of the tutorial's white raven (the game's own tutorial birds untouched) and flies it in with its own animations.
    /// </summary>
    internal sealed class MuninnVisit : Visit
    {
        public override string Id => "muninn";
        public override bool Enabled => Plugin.MuninnEnabled.Value;

        public override bool Start(Vector3 basePoint)
        {
            if (MuninnVisitor.Count > 0) return false;
            GameObject prefab = ZNetScene.instance.GetPrefab(MuninnPrefab.Name);
            if (prefab == null || !FindLanding(basePoint, out Vector3 at)) return false;
            GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            ZDO zdo = go.GetComponent<ZNetView>().GetZDO();
            zdo.Set(MuninnVisitor.LeaveAtKey, ZNet.instance.GetTime().Ticks + TimeSpan.FromMinutes(Plugin.MuninnMinutes.Value).Ticks);
            zdo.Set(MuninnVisitor.HomeKey, basePoint);
            Net.MessageAll(MessageHud.MessageType.Center, "$goodtidings_muninn_arrives");
            Plugin.Log.LogInfo($"Muninn lands at {at}, by the base at {basePoint}");
            return true;
        }

        /// <summary>Open ground 6-16 m from the base for him to land on, with sky above (he flies in).</summary>
        private static bool FindLanding(Vector3 home, out Vector3 at)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle");
            for (int i = 0; i < 60; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(6f, 16f);
                Vector3 p = home + new Vector3(r.x, 0f, r.y);
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h, 1000)) continue;
                if (h < ZoneSystem.instance.m_waterLevel + 0.3f) continue;
                p.y = h;
                if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.up, 25f, mask)) continue;
                if (Physics.CheckSphere(p + Vector3.up * 1.6f, 1.3f, mask, QueryTriggerInteraction.Ignore)) continue;
                at = p;
                return true;
            }
            at = Vector3.zero;
            return false;
        }
    }

    /// <summary>The raven's saved object, registered in ZNetScene as the trader is (TraderPrefab).</summary>
    internal static class MuninnPrefab
    {
        internal const string Name = "goodtidings_muninn";
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
            _holder = new GameObject("goodtidings_prefabs_muninn");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            var p = new GameObject(Name);
            p.transform.SetParent(_holder.transform, false);
            p.layer = LayerMask.NameToLayer("piece_nonsolid");
            var nview = p.AddComponent<ZNetView>();
            nview.m_persistent = true;   // (saved: he's still there, and still leaves, after a restart)
            // what the player looks at to talk to him (a trigger: it pushes nothing)
            var col = p.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 1.2f, 0f);
            col.radius = 0.9f;
            col.height = 2.6f;
            p.AddComponent<MuninnVisitor>();
            _prefab = p;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Register(__instance);
        }
    }

    /// <summary>On the raven's object: his look (each game's own), his turning to face the player, the map markers, and his leaving.</summary>
    internal sealed class MuninnVisitor : MonoBehaviour, Hoverable, Interactable
    {
        internal const string LeaveAtKey = "GoodTidings.LeaveAt", HomeKey = "GoodTidings.Home", LeavingKey = "GoodTidings.Leaving",
            AnsweredKey = "GoodTidings.Answered", RightKey = "GoodTidings.Right", AnswererKey = "GoodTidings.Answerer";
        private static readonly HashSet<MuninnVisitor> All = new HashSet<MuninnVisitor>();
        internal static int Count => All.Count;

        private ZNetView _nview;
        private GameObject _visual;
        private Animator _animator;
        private readonly MapPins _pins = new MapPins();
        private float _landed, _leftAt = -1f;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            All.Add(this);
            if (_nview.GetZDO() == null) return;
            _nview.Register<bool, string>("GoodTidings_Answer", RPC_Answer);
        }

        /// <summary>Someone has answered his riddle (first come, first served: then he's done).</summary>
        internal bool Answered => _nview.GetZDO()?.GetBool(AnsweredKey) == true;

        /// <summary>This player's answer, to his owner, who decides (the first to arrive counts).</summary>
        internal void Answer(bool right)
        {
            _nview.InvokeRPC("GoodTidings_Answer", right, Player.m_localPlayer ? Player.m_localPlayer.GetPlayerName() : "");
            if (_animator) _animator.SetTrigger("talk");
        }

        private void RPC_Answer(long sender, bool right, string who)
        {
            if (!_nview.IsOwner()) return;
            ZDO zdo = _nview.GetZDO();
            if (zdo.GetBool(AnsweredKey) || zdo.GetBool(LeavingKey)) return;   // (another was first: his window says so)
            zdo.Set(AnsweredKey, true);
            zdo.Set(RightKey, right);
            zdo.Set(AnswererKey, who ?? "");
            zdo.Set(LeaveAtKey, ZNet.instance.GetTime().Ticks + TimeSpan.FromSeconds(5).Ticks);   // (a parting line, then off)
            Vector3 home = zdo.GetVec3(HomeKey, transform.position);
            if (right) MuninnOutcome.Reward(transform.position + transform.forward * 1.2f + Vector3.up * 0.6f);
            else MuninnOutcome.CallTheBeasts(home);
            Plugin.Log.LogInfo($"Muninn's riddle answered {(right ? "rightly" : "wrongly")} by {who}");
        }

        private bool _toldOutcome;

        /// <summary>On every game near him: once answered, his parting words over his head, and who answered (a message nearby).</summary>
        private void TellOutcome(ZDO zdo)
        {
            if (_toldOutcome || !zdo.GetBool(AnsweredKey)) return;
            _toldOutcome = true;
            bool right = zdo.GetBool(RightKey);
            if (_animator) _animator.SetTrigger("talk");
            if (_visual && Chat.instance)
                Chat.instance.SetNpcText(_visual, Vector3.up * 2.6f, 20f, 5f, "",
                    Localization.instance.Localize(Lines.Pick(right ? "goodtidings_muninn_right" : "goodtidings_muninn_wrong")), false);
            Player me = Player.m_localPlayer;
            if (me && Vector3.Distance(me.transform.position, transform.position) < 60f)
                me.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize(
                    right ? "$goodtidings_muninn_answered_right" : "$goodtidings_muninn_answered_wrong", zdo.GetString(AnswererKey, "?")));
        }

        private void Start()
        {
            if (_nview.GetZDO() == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            try { _visual = MuninnLook.Build(transform, out _animator); }
            catch (Exception e) { Plugin.Log.LogError($"Building Muninn's look failed: {e}"); }
            if (_animator) _animator.SetTrigger(_nview.GetZDO().GetBool(LeavingKey) ? "poff" : "flyin");
            _landed = Time.time;
        }

        private void OnDestroy()
        {
            All.Remove(this);
            _pins.Remove();
        }

        private void Update()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo == null) return;
            bool leaving = zdo.GetBool(LeavingKey);

            if (_visual)
            {
                TellOutcome(zdo);
                // the game's "?" (as Hildir marks her treasure places) where he sits, no name
                _pins.Update(zdo.GetVec3(HomeKey, transform.position), transform.position, "", icon: MapPins.Mystery(), circle: MapPins.Neutral);
                if (leaving && _leftAt < 0f)
                {
                    _leftAt = Time.time;
                    if (_animator) _animator.SetTrigger("flyaway");
                }
                else if (!leaving && Time.time - _landed > 2.5f) FacePlayer();
            }

            if (!_nview.IsOwner()) return;
            long now = ZNet.instance.GetTime().Ticks;
            if (!leaving && now >= zdo.GetLong(LeaveAtKey, now)) Leave();
            // gone once he's flown out of sight (a few seconds), or at once on a game that draws nothing
            if (leaving && (_leftAt < 0f && _visual == null || _leftAt >= 0f && Time.time - _leftAt > 4f))
                ZNetScene.instance.Destroy(gameObject);
        }

        /// <summary>He flies off (on every game); the owner removes him when he's gone.</summary>
        internal void Leave()
        {
            if (!_nview.IsOwner()) return;
            _nview.GetZDO().Set(LeavingKey, true);
        }

        /// <summary>Turns to face the nearest player, as the game's raven does (its "anglevel" turning steps).</summary>
        private void FacePlayer()
        {
            Player p = Player.m_localPlayer;
            if (p == null || _animator == null) return;
            Vector3 to = p.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f || to.magnitude > 20f) { _animator.SetFloat("anglevel", 0f, 0.4f, Time.deltaTime); return; }
            float angle = Vector3.SignedAngle(_visual.transform.forward, to, Vector3.up);
            if (Mathf.Abs(angle) > 15f)
            {
                _animator.SetFloat("anglevel", 120f * Mathf.Sign(angle), 0.4f, Time.deltaTime);
                _visual.transform.rotation = Quaternion.RotateTowards(_visual.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 120f);
            }
            else _animator.SetFloat("anglevel", 0f, 0.4f, Time.deltaTime);
        }

        public string GetHoverText() => Localization.instance.Localize(Answered || _nview.GetZDO()?.GetBool(LeavingKey) == true ? "$goodtidings_muninn"
            : "$goodtidings_muninn\n[<color=yellow><b>$KEY_Use</b></color>] $goodtidings_muninn_listen");
        public string GetHoverName() => Localization.instance.Localize("$goodtidings_muninn");
        public float GetHoverOffset() => 1.5f;   // (a big bird: reachable from a little further)

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer || RiddleWindow.JustClosed) return false;
            if (_nview.GetZDO()?.GetBool(LeavingKey) == true) return false;
            if (Answered) { user.Message(MessageHud.MessageType.Center, "$goodtidings_muninn_taken"); return true; }
            // a riddle from this player's own discoveries (each player who comes gets their own; the first answer counts)
            if (_riddle == null) _riddle = Riddles.Make(Player.m_localPlayer, new System.Random(UnityEngine.Random.Range(0, int.MaxValue)));
            if (_riddle == null) { user.Message(MessageHud.MessageType.Center, "$goodtidings_muninn_nothing"); return true; }
            if (_animator) _animator.SetTrigger("talk");
            RiddleWindow.Open(this, _riddle);
            return true;
        }

        private Riddle _riddle;

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    /// <summary>Muninn's look, from a copy of the tutorial's white raven.</summary>
    internal static class MuninnLook
    {
        internal static GameObject Build(Transform parent, out Animator animator)
        {
            animator = null;
            GameObject ravens = Tutorial.instance ? Tutorial.instance.m_ravenPrefab : null;
            Raven munin = ravens ? ravens.GetComponentsInChildren<Raven>(true).FirstOrDefault(r => r.m_isMunin) : null;
            if (munin == null) { Plugin.Log.LogWarning("No Muninn in the game's ravens"); return null; }
            var holder = new GameObject("goodtidings_build");
            holder.SetActive(false);
            GameObject copy = UnityEngine.Object.Instantiate(munin.gameObject, holder.transform);
            if (copy.GetComponent<Raven>() is Raven r && r.m_exclamation) r.m_exclamation.SetActive(false);   // (no tutorial "!")
            foreach (MonoBehaviour c in copy.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            copy.name = "goodtidings_muninn_look";
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            UnityEngine.Object.Destroy(holder);
            animator = copy.GetComponentInChildren<Animator>(true);
            if (animator) animator.enabled = true;
            return copy;
        }
    }
}
