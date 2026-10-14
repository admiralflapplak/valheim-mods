using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// The mod talking to itself across the network.
    ///   Player → server: hello, "I died" (with killer and witnesses), "I killed a nemesis".
    ///   Server → players: the roster (whenever it changes), and messages for a player.
    /// In single player or when hosting, everything is handled locally with no messages sent.
    /// Grudges must be installed on the server and every player's game.
    /// </summary>
    internal class Net : MonoBehaviour
    {
        private const int Protocol = 5;
        private const string RpcHello = "Grudges_Hello";
        private const string RpcRoster = "Grudges_Roster";
        private const string RpcDeath = "Grudges_Death";
        private const string RpcSlain = "Grudges_Slain";
        private const string RpcMessage = "Grudges_Msg";
        private const string RpcReveal = "Grudges_Reveal";
        private const string RpcEscaped = "Grudges_Escaped";
        private const string RpcReturned = "Grudges_Returned";
        private const string RpcFx = "Grudges_Fx";
        private const string RpcRaid = "Grudges_Raid";
        private const string RpcRaidAnswer = "Grudges_RaidAnswer";

        internal static Net Instance;

        /// <summary>The roster as this game knows it (the server's own object when we are the server).</summary>
        internal static Roster Known;
        /// <summary>True once we have a roster to trust (always on the server; after the server answers otherwise).</summary>
        internal static bool HaveRoster;
        /// <summary>Bumped whenever the roster changes, so bodies know to refresh.</summary>
        internal static int RosterVersion;

        private static object _registeredOn;
        private static bool _registered, _helloSent, _warnedNoServer;
        private static float _helloAt;
        private static readonly HashSet<long> _modPeers = new HashSet<long>();

        private void Awake() => Instance = this;

        internal static bool IsServer => Server.IsServer;

        public static void Patch(Harmony harmony)
        {
            try
            {
                var znetAwake = AccessTools.Method(typeof(ZNet), "Awake");
                var gameStart = AccessTools.Method(typeof(Game), "Start");
                if (znetAwake != null) harmony.Patch(znetAwake, postfix: new HarmonyMethod(typeof(Net), nameof(OnGameStart)));
                if (gameStart != null) harmony.Patch(gameStart, prefix: new HarmonyMethod(typeof(Net), nameof(OnGameStart)));
                if (znetAwake == null && gameStart == null) Plugin.Log.LogWarning("Couldn't find the game's start-up; Grudges can't run.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't hook in ({e.Message}); Grudges can't run."); }
        }

        private static void OnGameStart()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredOn, ZRoutedRpc.instance)) return;
            _registeredOn = ZRoutedRpc.instance;
            Known = null;
            HaveRoster = false;
            RosterVersion++;
            _helloSent = false;
            _warnedNoServer = false;
            _modPeers.Clear();
            _pending.Clear();
            _registered = false;
            Server.Forget();
            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcHello, OnHello);
                ZRoutedRpc.instance.Register<ZPackage>(RpcRoster, OnRoster);
                ZRoutedRpc.instance.Register<ZPackage>(RpcDeath, OnDeathRpc);
                ZRoutedRpc.instance.Register<ZPackage>(RpcSlain, OnSlainRpc);
                ZRoutedRpc.instance.Register<ZPackage>(RpcMessage, OnMessage);
                ZRoutedRpc.instance.Register<ZPackage>(RpcReveal, OnReveal);
                ZRoutedRpc.instance.Register<ZPackage>(RpcEscaped, OnEscapedRpc);
                ZRoutedRpc.instance.Register<ZPackage>(RpcReturned, OnReturnedRpc);
                ZRoutedRpc.instance.Register<ZPackage>(RpcFx, OnFx);
                ZRoutedRpc.instance.Register<ZPackage>(RpcRaid, OnRaidRpc);
                ZRoutedRpc.instance.Register<ZPackage>(RpcRaidAnswer, OnRaidAnswer);
                _registered = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't set up networking ({e.Message})."); }
        }

        private void Update()
        {
            FlushMessages();
            if (!_registered || ZNet.instance == null || ZRoutedRpc.instance == null) return;

            if (IsServer)
            {
                var r = Server.Roster;
                if (!ReferenceEquals(Known, r)) { Known = r; RosterVersion++; }
                HaveRoster = Known != null;
                return;
            }

            if (!_helloSent && Player.m_localPlayer != null)
            {
                _helloSent = true;
                _helloAt = Time.time;
                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(Protocol);
                    pkg.Write(Plugin.Version);
                    ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcHello, pkg);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't reach the server ({e.Message})."); }
            }
            if (_helloSent && !HaveRoster && !_warnedNoServer && Time.time - _helloAt > 15f)
            {
                _warnedNoServer = true;
                Plugin.Log.LogWarning("This server doesn't have Grudges (or an incompatible version). No nemeses here.");
            }
        }

        // ---- Player → server ---------------------------------------------------

        internal static void ReportDeath(DeathReport d)
        {
            if (IsServer) { Server.OnDeath(d); return; }
            if (!_registered || !HaveRoster) return; // server without the mod
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            Wire.Write(pkg, d);
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcDeath, pkg);
        }

        internal static void ReportSlain(string nemesisId, string killerName)
        {
            if (IsServer) { Server.OnSlain(nemesisId, killerName); return; }
            if (!_registered || !HaveRoster) return;
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(nemesisId ?? "");
            pkg.Write(killerName ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcSlain, pkg);
        }

        internal static void ReportEscaped(string id, string blow, long killerId, string killerName)
        {
            if (IsServer) { Server.OnEscaped(id, blow, killerId, killerName); return; }
            if (!_registered || !HaveRoster) return;
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(id ?? ""); pkg.Write(blow ?? ""); pkg.Write(killerId); pkg.Write(killerName ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcEscaped, pkg);
        }

        /// <summary>A nemesis has a new body ("return" after cheating death, or "raid").</summary>
        internal static void ReportNewBody(string id, string zdoKey, int gen, string reason, string playerName)
        {
            if (IsServer) { Server.OnNewBody(id, zdoKey, gen, reason, playerName); return; }
            if (!_registered || !HaveRoster) return;
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(id ?? ""); pkg.Write(zdoKey ?? ""); pkg.Write(gen); pkg.Write(reason ?? ""); pkg.Write(playerName ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcReturned, pkg);
        }

        /// <summary>Your base is being raided: ask the server whether a nemesis joins.</summary>
        internal static void RequestRaid(string raidKey, List<string> species, string biome, bool forced)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            if (IsServer) { Raids.OnAnswer(Server.OnRaid(raidKey, species, biome, p.GetPlayerID(), p.GetPlayerName(), forced)); return; }
            if (!_registered || !HaveRoster) return;
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(raidKey ?? ""); pkg.Write(string.Join(",", species)); pkg.Write(biome ?? "");
            pkg.Write(p.GetPlayerID()); pkg.Write(p.GetPlayerName()); pkg.Write(forced);
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcRaid, pkg);
        }

        private static void OnRaidRpc(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                string key = pkg.ReadString();
                var species = pkg.ReadString().Split(',').Where(x => x.Length > 0).ToList();
                string biome = pkg.ReadString();
                long id = pkg.ReadLong();
                string name = pkg.ReadString();
                bool forced = pkg.ReadBool();
                var a = Server.OnRaid(key, species, biome, id, name, forced);
                if (a.NemesisId.Length == 0) return;
                var reply = new ZPackage();
                reply.Write(Protocol);
                reply.Write(a.NemesisId); reply.Write(a.Spawn); reply.Write(a.Message); reply.Write(a.Line);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcRaidAnswer, reply);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't handle a raid request ({e.Message})."); }
        }

        private static void OnRaidAnswer(long sender, ZPackage pkg)
        {
            if (IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                Raids.OnAnswer(new Server.RaidAnswer { NemesisId = pkg.ReadString(), Spawn = pkg.ReadBool(), Message = pkg.ReadString(), Line = pkg.ReadString() });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read a raid answer ({e.Message})."); }
        }

        private static void OnEscapedRpc(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                Server.OnEscaped(pkg.ReadString(), pkg.ReadString(), pkg.ReadLong(), pkg.ReadString());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read an escape ({e.Message})."); }
        }

        private static void OnReturnedRpc(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                Server.OnNewBody(pkg.ReadString(), pkg.ReadString(), pkg.ReadInt(), pkg.ReadString(), pkg.ReadString());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read a return ({e.Message})."); }
        }

        // ---- Effects everyone sees (the puff when a nemesis escapes) ---------------

        private static int _lastFxToken;
        private static readonly System.Random FxRng = new System.Random();

        internal static void PlayEffectForAll(Vector3 pos, string effects)
        {
            int token = FxRng.Next(1, int.MaxValue);
            _lastFxToken = token;
            Effects.Play(pos, effects);
            if (!_registered || ZRoutedRpc.instance == null) return;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(Protocol);
                pkg.Write(token);
                pkg.Write(pos.x); pkg.Write(pos.y); pkg.Write(pos.z);
                pkg.Write(effects ?? "");
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcFx, pkg);
            }
            catch (Exception e) { Plugin.DebugLog($"Couldn't share an effect ({e.Message})."); }
        }

        private static void OnFx(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                int token = pkg.ReadInt();
                var pos = new Vector3(pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle());
                string effects = pkg.ReadString();
                if (token == _lastFxToken) return; // our own, already played
                if (Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, pos) > 80f) return;
                Effects.Play(pos, effects);
            }
            catch { /* only a puff */ }
        }

        private static void OnHello(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                int protocol = pkg.ReadInt();
                string version = pkg.ReadString();
                if (protocol != Protocol)
                {
                    Plugin.Log.LogWarning($"A player has Grudges {version}, the server has {Plugin.Version}. They need to match.");
                    return;
                }
                _modPeers.Add(sender);
                Plugin.Log.LogInfo($"A player with Grudges {version} joined.");
                SendRoster(sender);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Odd hello from a player ({e.Message})."); }
        }

        private static void OnDeathRpc(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                Server.OnDeath(Wire.ReadDeath(pkg));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read a death report ({e.Message})."); }
        }

        private static void OnSlainRpc(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                string id = pkg.ReadString();
                string killer = pkg.ReadString();
                Server.OnSlain(id, killer);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read a slain report ({e.Message})."); }
        }

        // ---- Server → players --------------------------------------------------

        private static List<long> LivePeers()
        {
            if (ZNet.instance == null) return new List<long>();
            var live = new HashSet<long>(ZNet.instance.GetPeers().Select(p => p.m_uid));
            _modPeers.RemoveWhere(p => !live.Contains(p));
            return _modPeers.ToList();
        }

        internal static void BroadcastRoster()
        {
            RosterVersion++;
            if (!IsServer) return;
            foreach (long peer in LivePeers())
            {
                try { SendRoster(peer); }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't send the roster to a player ({e.Message})."); }
            }
        }

        private static void SendRoster(long peer)
        {
            var r = Server.Roster ?? new Roster();
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            // Retired ones aren't needed by players; only the server keeps history.
            pkg.Write(new Roster { All = r.Living.ToList() }.Serialize());
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcRoster, pkg);
        }

        /// <summary>Shows a message to the named player ("" = everyone), wherever they are.</summary>
        internal static void SendMessage(string toPlayer, string text, string exceptName = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            ShowIfForMe(toPlayer, text, exceptName); // the host's own game
            if (!IsServer) return;
            foreach (long peer in LivePeers())
            {
                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(Protocol);
                    pkg.Write(toPlayer ?? "");
                    pkg.Write(exceptName ?? "");
                    pkg.Write(text);
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcMessage, pkg);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't send a message ({e.Message})."); }
            }
        }

        /// <summary>The on-screen moment for the player who died. Other players' games ignore it.</summary>
        internal static void SendReveal(string toPlayer, RevealInfo info)
        {
            if (IsForMe(toPlayer, null)) HandleReveal(info); // the host's own game
            if (!IsServer) return;
            foreach (long peer in LivePeers())
            {
                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(Protocol);
                    pkg.Write(toPlayer ?? "");
                    pkg.Write(info.NemesisId ?? "");
                    pkg.Write(info.Zdo ?? "");
                    pkg.Write(info.Kind ?? "");
                    pkg.Write(info.Title ?? "");
                    pkg.Write(info.Line ?? "");
                    pkg.Write(info.IsTest);
                    pkg.Write(info.Messages.Count);
                    foreach (var m in info.Messages) pkg.Write(m ?? "");
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcReveal, pkg);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't send a reveal ({e.Message})."); }
            }
        }

        private static void OnReveal(long sender, ZPackage pkg)
        {
            if (IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                string to = pkg.ReadString();
                var info = new RevealInfo
                {
                    NemesisId = pkg.ReadString(),
                    Zdo = pkg.ReadString(),
                    Kind = pkg.ReadString(),
                    Title = pkg.ReadString(),
                    Line = pkg.ReadString(),
                    IsTest = pkg.ReadBool()
                };
                int n = pkg.ReadInt();
                for (int i = 0; i < n && i < 16; i++) info.Messages.Add(pkg.ReadString());
                if (IsForMe(to, null)) HandleReveal(info);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read a reveal ({e.Message})."); }
        }

        /// <summary>Plays the reveal if it can; otherwise the messages just appear after you respawn.</summary>
        private static void HandleReveal(RevealInfo info)
        {
            bool played = false;
            try { played = Reveal.Instance != null && Reveal.Instance.Begin(info); }
            catch (Exception e) { Plugin.Log.LogWarning($"Reveal failed to start ({e.Message})."); }
            if (played)
            {
                // The first message is the reveal's caption; any others follow after respawn.
                for (int i = 1; i < info.Messages.Count; i++) _pending.Enqueue(info.Messages[i]);
            }
            else foreach (var m in info.Messages) _pending.Enqueue(m);
        }

        private static void OnRoster(long sender, ZPackage pkg)
        {
            if (IsServer) return;
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                var warnings = new List<string>();
                Known = Roster.Parse(pkg.ReadString(), warnings);
                foreach (var w in warnings) Plugin.Log.LogWarning(w);
                if (!HaveRoster) Plugin.Log.LogInfo($"The server has Grudges: {Known.All.Count} active nemeses in this world.");
                HaveRoster = true;
                RosterVersion++;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read the roster from the server ({e.Message})."); }
        }

        private static void OnMessage(long sender, ZPackage pkg)
        {
            if (IsServer) return; // the host already showed it
            try
            {
                if (pkg.ReadInt() != Protocol) return;
                string to = pkg.ReadString();
                string except = pkg.ReadString();
                string text = pkg.ReadString();
                ShowIfForMe(to, text, except);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read a message ({e.Message})."); }
        }

        private static string _myName;
        private static readonly Queue<string> _pending = new Queue<string>();
        private static float _aliveSince = -1f;

        private static bool IsForMe(string to, string except)
        {
            if (Player.m_localPlayer != null) _myName = Player.m_localPlayer.GetPlayerName();
            string me = _myName;
            if (string.IsNullOrEmpty(me)) return false;
            if (!string.IsNullOrEmpty(except) && string.Equals(me, except, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(to) && !string.Equals(me, to, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static void ShowIfForMe(string to, string text, string except)
        {
            if (IsForMe(to, except)) _pending.Enqueue(text); // shown once you're up and about (most arrive while you're dead)
        }

        /// <summary>Shows queued messages one at a time, a moment after you've respawned.</summary>
        private static float _nextShow;
        /// <summary>Queues a message to show after respawn.</summary>
        internal static void Enqueue(string text)
        {
            if (!string.IsNullOrEmpty(text)) _pending.Enqueue(text);
        }

        private static void FlushMessages()
        {
            if (Reveal.Active) return;
            var p = Player.m_localPlayer;
            if (p == null || p.IsDead()) { _aliveSince = -1f; return; }
            _myName = p.GetPlayerName();
            if (_aliveSince < 0f) _aliveSince = Time.time;
            if (_pending.Count == 0 || Time.time - _aliveSince < 2.5f || Time.time < _nextShow) return;
            Plugin.ShowMessage(_pending.Dequeue());
            _nextShow = Time.time + 4f;
        }
    }

    /// <summary>What the victim's game needs to stage the moment after their death.</summary>
    internal class RevealInfo
    {
        public string NemesisId = "", Zdo = "", Kind = "", Title = "", Line = "";
        public bool IsTest;
        public List<string> Messages = new List<string>();
    }

    /// <summary>Packs a death report into a network package and back.</summary>
    internal static class Wire
    {
        public static void Write(ZPackage p, DeathReport d)
        {
            p.Write(d.VictimId);
            p.Write(d.VictimName ?? "");
            p.Write(d.Cause ?? "");
            p.Write(d.CauseDetail ?? "");
            p.Write(d.Forced);
            p.Write(d.VictimHelmet ?? "");
            p.Write(d.Killer != null);
            if (d.Killer != null) Write(p, d.Killer);
            p.Write(d.Witnesses.Count);
            foreach (var w in d.Witnesses) Write(p, w);
        }

        public static DeathReport ReadDeath(ZPackage p)
        {
            var d = new DeathReport
            {
                VictimId = p.ReadLong(),
                VictimName = p.ReadString(),
                Cause = p.ReadString(),
                CauseDetail = p.ReadString(),
                Forced = p.ReadBool(),
                VictimHelmet = p.ReadString()
            };
            if (p.ReadBool()) d.Killer = ReadCreature(p);
            int n = p.ReadInt();
            for (int i = 0; i < n && i < 64; i++) d.Witnesses.Add(ReadCreature(p));
            return d;
        }

        private static void Write(ZPackage p, CreatureRef c)
        {
            p.Write(c.Zdo ?? "");
            p.Write(c.Species ?? "");
            p.Write(c.SpeciesName ?? "");
            p.Write(c.NemesisId ?? "");
            p.Write(c.Level);
            p.Write(c.Biome ?? "");
            p.Write(c.Distance);
        }

        private static CreatureRef ReadCreature(ZPackage p) => new CreatureRef
        {
            Zdo = p.ReadString(),
            Species = p.ReadString(),
            SpeciesName = p.ReadString(),
            NemesisId = p.ReadString(),
            Level = p.ReadInt(),
            Biome = p.ReadString(),
            Distance = p.ReadSingle()
        };
    }
}
