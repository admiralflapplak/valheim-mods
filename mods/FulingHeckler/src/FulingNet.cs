using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Sharing the goblin with other players.
    ///
    /// The player he's visiting (the "director") runs him exactly as before, and their game describes what he does:
    /// where he is, which way he faces, how fast he runs, every animation, line, sound and puff. Other players with
    /// the mod draw a "puppet" copy that follows those messages. Puppets are the same stripped visual copy as always:
    /// no network object, no AI, nothing saved in the world. If the messages stop, the puppet quietly disappears.
    ///
    /// Other players can also nudge the director's goblin: their game tells the director "I spotted him",
    /// "I'm watching him" or "I'm standing right next to him", and the scene reacts as if the director did it.
    /// </summary>
    internal partial class FulingActor
    {
        // ---- What he's wearing (so puppets can match) --------------------------

        private string _helmetItem, _emoteNow = "";
        private bool _deadNow;
        private readonly List<string> _outfitWorn = new List<string>();
        private float _speedNow;

        private void DressAsTold()
        {
            if (!string.IsNullOrEmpty(_show.PuppetHelmet)) WearHelmet(_show.PuppetHelmet, logResult: false);
            if (_show.PuppetOutfit != null)
                foreach (var piece in _show.PuppetOutfit)
                    if (Gear.TryAttach(piece, null, _body, _copyVisual, 1f, out string _)) _outfitWorn.Add(piece);
            if (_show.PuppetTorch)
            {
                Transform hand = Gear.FindSlotOnCopy(_prefab, _sourceVisual, _copyVisual, "m_rightHand");
                if (Gear.TryAttach(Look.TorchItem, hand, _body, _copyVisual, Look.HeldItemSize, out string _)) _hasTorch = true;
            }
        }

        // ---- Helpers the scenes use: they act here and tell the other players ---

        private void Sfx(SoundMoment m) { Sounds.Instance?.PlayOn(gameObject, m); _net?.Sound(m, 0, transform.position); }
        private void SfxAt(SoundMoment m) { Sounds.Instance?.PlayAt(transform.position, m); _net?.Sound(m, 1, transform.position); }
        private void SfxLoop(SoundMoment m)
        {
            Sounds.Instance?.KeepPlaying(gameObject, m);
            if (_loopSent != m) { _loopSent = m; _net?.Sound(m, 2, transform.position); }
        }
        private void SfxStop() { Sounds.Instance?.StopLoop(gameObject); if (_loopSent.HasValue) { _loopSent = null; _net?.Sound(SoundMoment.Dance, 3, transform.position); } }
        private SoundMoment? _loopSent;

        private void PoofHere(string effects) { Poof.At(transform.position, effects); _net?.Poof(transform.position, effects); }

        private AnimationClip PlayEmote(IList<string> names)
        {
            var clip = _emotes != null ? _emotes.TryPlay(names, Rng) : null;
            if (clip != null) { _emoteNow = clip.name; _net?.Emote(clip.name); }
            return clip;
        }

        private void RestoreEmote() { _emotes?.Restore(); _emoteNow = ""; _net?.Emote(""); }

        // ---- Nudges from other players ----------------------------------------

        private float _pokeSpotted = -99f, _pokeWatched = -99f, _pokeClose = -99f;

        /// <summary>Did another player report this in the last moment?</summary>
        private static bool RemoteSaw(float when) => Time.time - when < 0.6f;

        internal static void ReceivePoke(int visit, int kind)
        {
            var a = _current;
            if (a == null || a._net == null || a._net.VisitId != visit) return;
            switch (kind)
            {
                case VisitCaster.PokeSpotted: a._pokeSpotted = Time.time; break;
                case VisitCaster.PokeWatching: a._pokeWatched = Time.time; break;
                case VisitCaster.PokeClose: a._pokeClose = Time.time; break;
            }
        }

        // ---- Director side ------------------------------------------------------

        private VisitCaster _net;
        private float _nextMove, _nextState;

        private void StartBroadcasting()
        {
            if (_show.Fitting || _show.Puppet || !Visits.ShouldBroadcast) return;
            _net = new VisitCaster(this);
            _net.State();
            Plugin.DebugLog($"Sharing this visit with other players (visit {_net.VisitId}{(Visits.EchoWanted ? ", echo test" : "")}).");
        }

        private void LateUpdate()
        {
            if (_show != null && _show.Puppet) { PuppetUpdate(); return; }
            if (_net == null) return;
            if (Time.time >= _nextMove) { _nextMove = Time.time + 0.1f; _net.Move(); }
            if (Time.time >= _nextState) { _nextState = Time.time + 1f; _net.State(); }
        }

        private void OnDestroyNet()
        {
            if (_net != null) { _net.End(); _net = null; }
            if (_show != null && _show.Puppet && _puppetKey != null && Puppets.TryGetValue(_puppetKey, out var me) && me == this)
                Puppets.Remove(_puppetKey);
        }

        /// <summary>Builds the description of this visit that puppets need.</summary>
        internal void WriteState(ZPackage p)
        {
            p.Write((int)_show.Scene);
            Pack.V(p, transform.position);
            Pack.F(p, transform.eulerAngles.y);
            p.Write(_helmetItem ?? "");
            p.Write(string.Join(",", _outfitWorn.ToArray()));
            p.Write(_hasTorch);
            Pack.F(p, Plugin.LurkerNoticeDistance.Value);
            Pack.F(p, Plugin.RedLightWatchAngle.Value);
            Pack.F(p, CatchDistanceForScene());
            p.Write(Visits.MyId());
            // What he's in the middle of, for players who arrive mid-scene.
            p.Write(_emoteNow ?? "");
            p.Write(_deadNow);
            p.Write(_loopSent.HasValue ? (int)_loopSent.Value : -1);
        }

        private float CatchDistanceForScene()
        {
            switch (_show.Scene)
            {
                case SceneKind.Heckle: return Plugin.PersonalSpace.Value;
                case SceneKind.Tombstone: return Plugin.TombstoneCatchDistance.Value;
                case SceneKind.Campfire: return Plugin.CampfireCatchDistance.Value;
                case SceneKind.Copycat: return 4f;
                default: return 0f;
            }
        }

        internal void WriteMove(ZPackage p)
        {
            Pack.V(p, transform.position);
            Pack.F(p, transform.eulerAngles.y);
            Pack.F(p, _speedNow);
            Pack.F(p, _animator != null ? _animator.speed : 1f);
        }

        // ---- Puppet side ------------------------------------------------------------

        private static readonly Dictionary<string, FulingActor> Puppets = new Dictionary<string, FulingActor>();
        private static readonly HashSet<string> FailedPuppets = new HashSet<string>();

        private string _puppetKey;
        private Vector3 _offset, _targetPos;
        private float _targetYaw, _lastHeard, _bornAt, _nextPoke, _seenFor;
        private SoundMoment? _puppetLoop;
        private long _directorId;
        private int _visit;
        private float _notice, _watchAngle, _catch;
        private bool _isEcho;

        /// <summary>Is another player's goblin out near this spot? (So scenes don't pile two goblins on top of each other.)</summary>
        internal static bool PuppetNear(Vector3 pos, float range)
        {
            foreach (var a in Puppets.Values)
                if (a != null && Flat(a.transform.position - pos).magnitude < range) return true;
            return false;
        }

        internal static FulingActor FindPuppet(string key) => Puppets.TryGetValue(key, out var a) && a != null ? a : null;

        /// <summary>Draws another player's goblin. Returns null if it can't (wrong body name, no animator...).</summary>
        internal static FulingActor SpawnPuppet(string key, int visit, ZPackage state, bool echo)
        {
            if (FailedPuppets.Contains(key)) return null; // tried and failed once; don't retry every second
            GameObject root = null;
            try
            {
                if (ZNetScene.instance == null) return null;
                var scene = (SceneKind)state.ReadInt();
                Vector3 pos = Pack.V(state);
                float yaw = Pack.F(state);
                string helmet = state.ReadString();
                var outfit = NameList.Parse(state.ReadString());
                bool torch = state.ReadBool();
                float notice = Pack.F(state), watch = Pack.F(state), catchDist = Pack.F(state);
                long director = state.ReadLong();
                string emoteNow = state.ReadString();
                bool deadNow = state.ReadBool();
                int loop = state.ReadInt();

                // Echo test: draw the copy a little to the side so you can see both.
                Vector3 offset = echo ? new Vector3(1.8f, 0f, 1.8f) : Vector3.zero;
                var p = Player.m_localPlayer;
                if (p == null || Flat(pos + offset - p.transform.position).magnitude > 150f) return null; // too far to matter

                GameObject prefab = ZNetScene.instance.GetPrefab(Look.Type);
                if (prefab == null) return null;
                Animator sourceAnim = prefab.GetComponentInChildren<Animator>(true);
                root = BuildVisualCopy(prefab, sourceAnim, pos + offset);
                if (root == null) { FailedPuppets.Add(key); return null; }
                root.name = "FulingHeckler_Puppet";
                root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

                var a = root.AddComponent<FulingActor>();
                a._show = new Performance
                {
                    Scene = scene, Puppet = true, Line = "",
                    PuppetHelmet = helmet, PuppetOutfit = outfit, PuppetTorch = torch
                };
                a._prefab = prefab;
                a._sourceVisual = sourceAnim.transform;
                a._copyVisual = root.transform.Find("Visual");
                a._skin = new SkinColor(a._copyVisual.GetComponentsInChildren<Renderer>(true));
                a._skin.Apply();
                a.DressUp();
                a.InitAnimator();
                // Catch up with whatever he's in the middle of.
                if (!string.IsNullOrEmpty(emoteNow) && a._emotes != null) a._emotes.TryPlay(new[] { emoteNow }, Rng);
                if (deadNow && a._animator != null && a._animParams.Contains("dead")) a._animator.SetBool("dead", true);
                if (loop >= 0) a._puppetLoop = (SoundMoment)loop;

                a._puppetKey = key;
                a._visit = visit;
                a._offset = offset;
                a._targetPos = pos;
                a._targetYaw = yaw;
                a._lastHeard = a._bornAt = Time.time;
                a._directorId = director;
                a._notice = notice; a._watchAngle = watch; a._catch = catchDist;
                a._isEcho = echo;
                Puppets[key] = a;
                Plugin.Log.LogInfo($"Shared goblin: showing another player's {scene}{(echo ? " (echo test copy)" : "")}.");
                return a;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Shared goblin: couldn't draw another player's goblin ({e.GetType().Name}: {e.Message}).");
                FailedPuppets.Add(key);
                if (root != null) Destroy(root);
                return null;
            }
        }

        private void StartPuppet()
        {
            Destroy(gameObject, 300f); // hard cap, whatever happens
        }

        internal void Heard() => _lastHeard = Time.time;

        internal void PuppetMove(Vector3 pos, float yaw, float speed, float animSpeed)
        {
            Heard();
            _targetPos = pos;
            _targetYaw = yaw;
            if (_animator != null)
            {
                if (_animParams.Contains("forward_speed")) _animator.SetFloat("forward_speed", speed);
                _animator.speed = animSpeed;
            }
        }

        internal void PuppetTrigger(string name) { Heard(); if (_animator != null && _animParams.Contains(name)) _animator.SetTrigger(name); }
        internal void PuppetBool(string name, bool v) { Heard(); if (_animator != null && _animParams.Contains(name)) _animator.SetBool(name, v); }

        internal void PuppetEmote(string clip)
        {
            Heard();
            if (_emotes == null) return;
            if (string.IsNullOrEmpty(clip)) _emotes.Restore();
            else _emotes.TryPlay(new[] { clip }, Rng);
        }

        internal void PuppetSay(string text, float seconds)
        {
            Heard();
            try
            {
                if (Chat.instance == null) return;
                Chat.instance.SetNpcText(gameObject, Vector3.up * BubbleHeight, BubbleCullDistance, seconds,
                    _isEcho ? Look.Name + " (echo)" : Look.Name, text, false);
            }
            catch (Exception e) { Plugin.DebugLog($"Shared goblin: bubble failed ({e.Message})."); }
        }

        internal void PuppetSound(SoundMoment m, int mode, Vector3 pos)
        {
            Heard();
            var snd = Sounds.Instance;
            if (snd == null) return;
            switch (mode)
            {
                case 0: snd.PlayOn(gameObject, m); break;
                case 1: snd.PlayAt(pos + _offset, m); break;
                case 2: _puppetLoop = m; break;
                case 3: _puppetLoop = null; snd.StopLoop(gameObject); break;
            }
        }

        internal Vector3 Offset => _offset;

        private void PuppetUpdate()
        {
            // The director's game went quiet (they left, crashed, or walked far away): fade out without fuss.
            if (Time.time - _lastHeard > 4f) { Destroy(gameObject); return; }

            // Glide towards the latest reported spot. Messages come ten times a second; this smooths the gaps.
            Vector3 want = _targetPos + _offset;
            if ((want - transform.position).sqrMagnitude > 100f) transform.position = want; // way off: snap
            else transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _targetYaw, 0f), 1f - Mathf.Exp(-12f * Time.deltaTime));

            if (_puppetLoop.HasValue) Sounds.Instance?.KeepPlaying(gameObject, _puppetLoop.Value);

            // Tell the director if we're spotting, watching or crowding him.
            var p = Player.m_localPlayer;
            if (p == null || p.IsDead()) return;
            float dist = DistanceToPlayer(p);
            int poke = 0;
            switch (_show.Scene)
            {
                case SceneKind.Lurker:
                    bool seen = dist <= _notice && IsLookedAt(Head, 12f, _notice + 10f) && HasLineOfSight(Head);
                    _seenFor = seen ? _seenFor + Time.deltaTime : 0f;
                    if (_seenFor > 0.5f || dist < 7f) poke = VisitCaster.PokeSpotted;
                    break;
                case SceneKind.RedLight:
                case SceneKind.Gifter:
                    // Watching = facing him, able to see him, reasonably close, and for a moment (a quick glance doesn't count).
                    bool watching = dist <= Mathf.Max(_notice, 25f) && IsFacedByPlayer(p, transform.position, _watchAngle) && HasLineOfSight(Head);
                    _seenFor = watching ? _seenFor + Time.deltaTime : 0f;
                    if (_seenFor > 0.3f) poke = VisitCaster.PokeWatching;
                    break;
                default:
                    if (_catch > 0f && dist < _catch) poke = VisitCaster.PokeClose;
                    break;
            }
            if (poke != 0 && Time.time >= _nextPoke && Time.time - _bornAt > 1f) // a moment's grace when he first appears
            {
                _nextPoke = Time.time + 0.2f;
                Visits.SendPoke(_directorId, _visit, poke);
            }
        }
    }

    /// <summary>Sends one goblin visit's messages. Every call is wrapped so a network hiccup never breaks the goblin itself.</summary>
    internal class VisitCaster
    {
        public const int PokeSpotted = 1, PokeWatching = 2, PokeClose = 3;

        public readonly int VisitId = UnityEngine.Random.Range(1, int.MaxValue);
        private readonly FulingActor _actor;

        public VisitCaster(FulingActor actor) { _actor = actor; }

        public void State() => Send(Visits.MsgState, p => _actor.WriteState(p));
        public void Move() => Send(Visits.MsgMove, p => _actor.WriteMove(p));
        public void Trigger(string name) => Send(Visits.MsgTrigger, p => p.Write(name));
        public void Bool(string name, bool v) => Send(Visits.MsgBool, p => { p.Write(name); p.Write(v); });
        public void Emote(string clip) => Send(Visits.MsgEmote, p => p.Write(clip ?? ""));
        public void Say(string text, float seconds) => Send(Visits.MsgSay, p => { p.Write(text ?? ""); Pack.F(p, seconds); });
        public void Sound(SoundMoment m, int mode, Vector3 pos) => Send(Visits.MsgSound, p => { p.Write((int)m); p.Write(mode); Pack.V(p, pos); });
        public void Poof(Vector3 pos, string effects) => Send(Visits.MsgPoof, p => { Pack.V(p, pos); p.Write(effects ?? ""); });
        public void End() => Send(Visits.MsgEnd, p => { });

        private void Send(int type, Action<ZPackage> body)
        {
            try
            {
                var p = Visits.Begin(type, VisitId);
                body(p);
                Visits.Send(p);
            }
            catch (Exception e) { Plugin.DebugLog($"Shared goblin: a message didn't go out ({e.Message})."); }
        }
    }

    /// <summary>The network side of sharing visits.</summary>
    internal static class Visits
    {
        private const string RpcVisit = "FulingHeckler_Visit";
        public const int MsgState = 1, MsgMove = 2, MsgTrigger = 3, MsgBool = 4, MsgEmote = 5, MsgSay = 6,
                         MsgSound = 7, MsgPoof = 8, MsgEnd = 9, MsgPoke = 10;

        private static bool _registered;
        private static bool _loggedNoId;

        public static void Register()
        {
            _registered = false;
            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcVisit, OnVisit);
                _registered = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Shared goblin: couldn't set up ({e.Message}); visits stay private."); }
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>Should goblin visits on this game be shown to other players?</summary>
        public static bool ShouldBroadcast
        {
            get
            {
                if (!_registered || !Plugin.Enabled.Value || ZNet.instance == null || ZRoutedRpc.instance == null) return false;
                if (IsServer) return Plugin.ShareLook.Value && Player.m_localPlayer != null && Net.ModPeerCount > 0; // hosting with friends who have the mod
                if (Net.ServerId == 0L || !Net.ServerCompatible) return false;
                return EchoWanted || Net.ServerShares;
            }
        }

        /// <summary>Echo test: the server sends your own goblin's messages back, so you see what a friend would see.</summary>
        public static bool EchoWanted => Plugin.EchoTest != null && Plugin.EchoTest.Value && !IsServer && Net.ServerCompatible;

        /// <summary>This game's network ID (the one other players see as the sender of our messages).</summary>
        private static long _myId;
        private static object _myIdFor;

        public static long MyId()
        {
            if (_myId != 0L && ReferenceEquals(_myIdFor, ZRoutedRpc.instance)) return _myId; // same world: same ID
            _myId = LookUpMyId();
            _myIdFor = ZRoutedRpc.instance;
            return _myId;
        }

        private static long LookUpMyId()
        {
            try
            {
                var m = Traverse.Create(typeof(ZDOMan)).Method("GetSessionID");
                if (m.MethodExists()) return m.GetValue<long>();
            }
            catch { /* try the next way */ }
            try
            {
                var f = Traverse.Create(ZRoutedRpc.instance).Field("m_id");
                if (f.FieldExists()) return f.GetValue<long>();
            }
            catch { /* give up */ }
            if (!_loggedNoId) { _loggedNoId = true; Plugin.Log.LogWarning("Shared goblin: couldn't find this game's network ID; other players can't nudge your goblin."); }
            return 0L;
        }

        public static ZPackage Begin(int type, int visit)
        {
            var p = new ZPackage();
            p.Write(EchoWanted);
            p.Write(type);
            p.Write(visit);
            return p;
        }

        /// <summary>
        /// A player sends to the server only; the server passes it on to the players who have the mod
        /// (never to players without it). A host sends straight to those players.
        /// </summary>
        public static void Send(ZPackage p)
        {
            if (IsServer) Relay(p.GetArray(), except: 0L);
            else ZRoutedRpc.instance.InvokeRoutedRPC(Net.ServerId, RpcVisit, p);
        }

        private static void Relay(byte[] raw, long except)
        {
            foreach (long peer in Net.ModPeers.ToList())
                if (peer != except) ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcVisit, new ZPackage(raw));
        }

        public static void SendPoke(long director, int visit, int kind)
        {
            try
            {
                if (director == 0L) return;
                if (director == MyId()) { FulingActor.ReceivePoke(visit, kind); return; } // echo test: it's our own goblin
                var p = new ZPackage();
                p.Write(false);
                p.Write(MsgPoke);
                p.Write(visit);
                p.Write(kind);
                ZRoutedRpc.instance.InvokeRoutedRPC(director, RpcVisit, p);
            }
            catch (Exception e) { Plugin.DebugLog($"Shared goblin: nudge didn't go out ({e.Message})."); }
        }

        private static void OnVisit(long sender, ZPackage pkg)
        {
            try
            {
                byte[] raw = pkg.GetArray();
                bool echo = pkg.ReadBool();
                int type = pkg.ReadInt();
                int visit = pkg.ReadInt();

                // On the server: pass a player's goblin messages on to the other players with the mod,
                // and send echo-test messages straight back to whoever sent them.
                if (IsServer && sender != MyId() && type != MsgPoke)
                {
                    if (echo) { ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcVisit, new ZPackage(raw)); return; }
                    if (Plugin.ShareLook.Value) Relay(raw, except: sender);
                    // ...and if the server is also someone playing (hosting), they see it too: carry on below.
                }

                if (!Plugin.Enabled.Value || Player.m_localPlayer == null) return; // dedicated server, or still loading
                long me = MyId();
                if (sender == me && me != 0L) return;                         // our own message, delivered locally too
                if (echo && !EchoWanted) return;                              // (shouldn't happen: echoes only go back to their sender)

                if (type == MsgPoke) { FulingActor.ReceivePoke(visit, pkg.ReadInt()); return; }

                string key = sender + ":" + visit;
                var puppet = FulingActor.FindPuppet(key);

                if (type == MsgState)
                {
                    if (puppet == null) FulingActor.SpawnPuppet(key, visit, pkg, echo);
                    else puppet.Heard();
                    return;
                }
                if (type == MsgPoof)
                {
                    Vector3 pos = Pack.V(pkg);
                    string effects = pkg.ReadString();
                    Vector3 offset = puppet != null ? puppet.Offset : (echo ? new Vector3(1.8f, 0f, 1.8f) : Vector3.zero);
                    var p = Player.m_localPlayer;
                    // Only game effects (vfx_/fx_ names), whatever the other player's settings say.
                    var safe = string.Join(",", NameList.Parse(effects).Where(n => n.StartsWith("vfx", StringComparison.OrdinalIgnoreCase) || n.StartsWith("fx", StringComparison.OrdinalIgnoreCase)).ToArray());
                    if (p != null && safe.Length > 0 && (pos + offset - p.transform.position).magnitude < 150f) Poof.At(pos + offset, safe);
                    return;
                }
                if (puppet == null) return; // haven't seen this visit's description yet (it comes every second)

                switch (type)
                {
                    case MsgMove: puppet.PuppetMove(Pack.V(pkg), Pack.F(pkg), Pack.F(pkg), Pack.F(pkg)); break;
                    case MsgTrigger: puppet.PuppetTrigger(pkg.ReadString()); break;
                    case MsgBool: { string n = pkg.ReadString(); puppet.PuppetBool(n, pkg.ReadBool()); break; }
                    case MsgEmote: puppet.PuppetEmote(pkg.ReadString()); break;
                    case MsgSay: { string t = pkg.ReadString(); puppet.PuppetSay(t, Pack.F(pkg)); break; }
                    case MsgSound: { var m = (SoundMoment)pkg.ReadInt(); int mode = pkg.ReadInt(); puppet.PuppetSound(m, mode, Pack.V(pkg)); break; }
                    case MsgEnd: UnityEngine.Object.Destroy(puppet.gameObject); break;
                }
            }
            catch (Exception e) { Plugin.DebugLog($"Shared goblin: couldn't read a message ({e.GetType().Name}: {e.Message})."); }
        }
    }
}
