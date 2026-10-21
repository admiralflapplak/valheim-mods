using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// What the goblin looks like right now. On a server that has the mod (and shares its look), the server's
    /// settings win, so everyone gets the same goblin. Anywhere else, your own settings are used.
    /// Your own config is never changed: leave the server and your goblin is back.
    /// </summary>
    internal static class Look
    {
        /// <summary>The server's look, or null when we're using our own settings.</summary>
        internal static SharedLook Server;

        internal static bool FromServer => Server != null;

        internal static string Name => Server?.Name ?? Plugin.FulingName.Value;
        internal static float SayNameChance => Server?.SayNameChance ?? Plugin.SayNameChance.Value;
        internal static string Type => Server?.Type ?? Plugin.FulingType.Value;
        internal static float HelmetChance => Server?.HelmetChance ?? Plugin.HelmetChance.Value;
        internal static string Helmets => Server?.Helmets ?? Plugin.Helmets.Value;
        internal static string OutfitItems => Server?.OutfitItems ?? Plugin.OutfitItems.Value;
        internal static float OutfitPieceChance => Server?.OutfitPieceChance ?? Plugin.OutfitPieceChance.Value;
        internal static float TorchAtNightChance => Server?.TorchAtNightChance ?? Plugin.TorchAtNightChance.Value;
        internal static string TorchItem => Server?.TorchItem ?? Plugin.TorchItem.Value;
        internal static float HeldItemSize => Server?.HeldItemSize ?? Plugin.HeldItemSize.Value;
        internal static bool Recolor => Server?.Recolor ?? Plugin.Recolor.Value;
        internal static float Hue => Server?.Hue ?? Plugin.SkinHue.Value;
        internal static float Saturation => Server?.Saturation ?? Plugin.SkinSaturation.Value;
        internal static float Brightness => Server?.Brightness ?? Plugin.SkinBrightness.Value;

        /// <summary>Helmet fits for wearing: the server's (built-in fits underneath) or your own.</summary>
        internal static FitBook Fits()
        {
            if (Server == null) return Plugin.SavedFits();
            var book = FitBook.Parse(Plugin.BuiltInFitsText);
            foreach (var kv in FitBook.Parse(Server.Fits).Fits) book.Fits[kv.Key] = kv.Value;
            return book;
        }
    }

    /// <summary>
    /// The scenes that hand out or keep real items (the Gifter, the Coin Thief's fee). Their settings and the loot table are the server's
    /// on a server with the mod (Plugin.Synced), whether or not it shares its look: otherwise any player could edit their own loot file
    /// and give themselves anything. In single player, when hosting, and on a server without the mod, your own settings are used.
    /// </summary>
    internal static class Rules
    {
        /// <summary>On someone else's server and it hasn't said yet whether it has the mod: no Gifter until it has.</summary>
        internal static bool Waiting => Plugin.Synced != null && Plugin.Synced.Server == ServerSettings.ServerMod.Checking;

        internal static bool GifterEnabled => !Waiting && Plugin.GifterEnabled.Value;

        /// <summary>Test buttons that hand out items: in single player or as the host, or with the game's cheats on (devcommands).</summary>
        internal static bool CanTestGifts =>
            ZNet.instance == null || ZNet.instance.IsServer() || (Console.instance != null && Console.instance.IsCheatsEnabled());
    }

    /// <summary>
    /// Packing helpers. Decimal numbers travel as text: every mod we checked only uses the game's
    /// text/whole-number/on-off functions, so we stick to those for certain compatibility.
    /// </summary>
    internal static class Pack
    {
        public static void F(ZPackage p, float v) => p.Write(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        public static float F(ZPackage p)
        {
            float.TryParse(p.ReadString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v);
            return v;
        }
        public static void V(ZPackage p, Vector3 v) => p.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", v.x, v.y, v.z));
        public static Vector3 V(ZPackage p)
        {
            var parts = p.ReadString().Split(',');
            float x = 0, y = 0, z = 0;
            if (parts.Length == 3)
            {
                var c = System.Globalization.CultureInfo.InvariantCulture; var st = System.Globalization.NumberStyles.Float;
                float.TryParse(parts[0], st, c, out x); float.TryParse(parts[1], st, c, out y); float.TryParse(parts[2], st, c, out z);
            }
            return new Vector3(x, y, z);
        }
    }

    /// <summary>The server's look, as sent over the network.</summary>
    internal class SharedLook
    {
        public string Name, Type, Helmets, OutfitItems, TorchItem, Fits;
        public float SayNameChance, HelmetChance, OutfitPieceChance, TorchAtNightChance, HeldItemSize, Hue, Saturation, Brightness;
        public bool Recolor;

        public static SharedLook FromConfig() => new SharedLook
        {
            Name = Plugin.FulingName.Value, Type = Plugin.FulingType.Value, Helmets = Plugin.Helmets.Value,
            OutfitItems = Plugin.OutfitItems.Value, TorchItem = Plugin.TorchItem.Value, Fits = Plugin.SavedFits().ToString(),
            SayNameChance = Plugin.SayNameChance.Value, HelmetChance = Plugin.HelmetChance.Value,
            OutfitPieceChance = Plugin.OutfitPieceChance.Value, TorchAtNightChance = Plugin.TorchAtNightChance.Value,
            HeldItemSize = Plugin.HeldItemSize.Value, Recolor = Plugin.Recolor.Value,
            Hue = Plugin.SkinHue.Value, Saturation = Plugin.SkinSaturation.Value, Brightness = Plugin.SkinBrightness.Value
        };

        public void WriteTo(ZPackage p)
        {
            p.Write(Name); p.Write(Type); p.Write(Helmets); p.Write(OutfitItems); p.Write(TorchItem); p.Write(Fits);
            Pack.F(p, SayNameChance); Pack.F(p, HelmetChance); Pack.F(p, OutfitPieceChance); Pack.F(p, TorchAtNightChance);
            Pack.F(p, HeldItemSize); p.Write(Recolor); Pack.F(p, Hue); Pack.F(p, Saturation); Pack.F(p, Brightness);
        }

        public static SharedLook ReadFrom(ZPackage p) => new SharedLook
        {
            Name = p.ReadString(), Type = p.ReadString(), Helmets = p.ReadString(), OutfitItems = p.ReadString(),
            TorchItem = p.ReadString(), Fits = p.ReadString(),
            SayNameChance = Pack.F(p), HelmetChance = Pack.F(p), OutfitPieceChance = Pack.F(p),
            TorchAtNightChance = Pack.F(p), HeldItemSize = Pack.F(p), Recolor = p.ReadBool(),
            Hue = Pack.F(p), Saturation = Pack.F(p), Brightness = Pack.F(p)
        };
    }

    /// <summary>
    /// The mod talking to itself across the network. When you join a server, your game says hello to the server.
    /// If the server has the mod, it answers with its goblin's look, and sends updates if the admin changes it.
    /// If the server doesn't have the mod, nobody answers and everything stays exactly as on your own.
    /// Players without the mod are never bothered: they're only sent anything if they said hello first.
    /// </summary>
    internal class Net : MonoBehaviour
    {
        /// <summary>Bump when the message layout changes; mismatched versions just stay on their own look.</summary>
        private const int Protocol = 2;
        private const string RpcHello = "FulingHeckler_Hello";
        private const string RpcLook = "FulingHeckler_Look";

        public static Net Instance;

        /// <summary>Did the server answer? (False on your own world, or on a server without the mod.)</summary>
        public static bool ServerHasMod { get; private set; }

        /// <summary>Does the server share its goblin (look and visits) with everyone?</summary>
        public static bool ServerShares { get; private set; }

        /// <summary>The server has the mod AND speaks the same message format as us.</summary>
        public static bool ServerCompatible { get; private set; }

        /// <summary>The server's network ID, learned from its reply (0 = unknown).</summary>
        public static long ServerId { get; private set; }

        /// <summary>On the server: players who said hello (so they have the mod). Only they are ever sent goblin messages.</summary>
        public static IEnumerable<long> ModPeers { get { Prune(); return _modPeers; } }
        public static int ModPeerCount { get { Prune(); return _modPeers.Count; } }

        private static float _prunedAt = -99f;

        /// <summary>Forget players who have left (checked every couple of seconds).</summary>
        private static void Prune()
        {
            if (Time.time - _prunedAt < 2f) return;
            _prunedAt = Time.time;
            try
            {
                var peers = ZNet.instance != null ? ZNet.instance.GetPeers() : null;
                if (peers == null) return;
                var live = new HashSet<long>();
                foreach (var peer in peers) if (peer != null) live.Add(peer.m_uid);
                _modPeers.RemoveWhere(id => !live.Contains(id));
            }
            catch { /* keep the list as it is */ }
        }

        private static bool _registered, _helloSent, _reportedNoReply;
        private static float _helloAt;
        private static readonly HashSet<long> _modPeers = new HashSet<long>();

        private void Awake() => Instance = this;

        /// <summary>
        /// The mod is unloading (a hot reload): its message handlers leave the game's table, so the next copy can put its own there.
        /// (The game has no way to unregister one: ZRoutedRpc.m_functions, by the name's hash, as Register adds them.)
        /// </summary>
        internal static void Unregister()
        {
            try
            {
                if (ZRoutedRpc.instance == null) return;
                if (!(AccessTools.Field(typeof(ZRoutedRpc), "m_functions")?.GetValue(ZRoutedRpc.instance) is System.Collections.IDictionary functions)) return;
                foreach (string name in new[] { RpcHello, RpcLook, Visits.RpcVisit }) functions.Remove(name.GetStableHashCode());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Server sync: couldn't let go of its messages ({e.Message}); rejoin the world after a reload."); }
            _registeredOn = null;
        }

        /// <summary>Hooks world start so the mod's messages are set up each time you join or host. Patched by hand: a mismatch only disables syncing.</summary>
        public static void Patch(Harmony harmony)
        {
            try
            {
                // Two hooks for safety (one of them may not run on a dedicated server); setting up twice is harmless.
                var znetAwake = AccessTools.Method(typeof(ZNet), "Awake");
                var gameStart = AccessTools.Method(typeof(Game), "Start");
                if (znetAwake != null) harmony.Patch(znetAwake, postfix: new HarmonyMethod(typeof(Net), nameof(OnGameStart)));
                if (gameStart != null) harmony.Patch(gameStart, prefix: new HarmonyMethod(typeof(Net), nameof(OnGameStart)));
                if (znetAwake == null && gameStart == null) { Plugin.Log.LogWarning("Server sync: couldn't find the game's start-up; he stays single-player."); return; }

                var look = new HashSet<ConfigEntryBase>(Plugin.LookSettings());
                look.Add(Plugin.ShareLook);
                Plugin.Instance.Config.SettingChanged += (_, e) =>
                {
                    if (e != null && look.Contains(e.ChangedSetting)) Instance?.OnLocalLookChanged();
                };
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Server sync: couldn't hook in ({e.Message}); he stays single-player."); }
        }

        private static object _registeredOn;

        /// <summary>The mod's messages, for setting up in a world (also after a hot reload in one).</summary>
        internal static void OnGameStart()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredOn, ZRoutedRpc.instance)) return; // already set up for this world
            _registeredOn = ZRoutedRpc.instance;
            // A fresh world: forget the last server.
            Look.Server = null;
            ServerHasMod = false;
            ServerShares = false;
            ServerCompatible = false;
            ServerId = 0L;
            _helloSent = false;
            _reportedNoReply = false;
            _modPeers.Clear();
            _registered = false;
            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcHello, OnHello);
                ZRoutedRpc.instance.Register<ZPackage>(RpcLook, OnLook);
                _registered = true;
                Visits.Register();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Server sync: couldn't set up ({e.Message}); he stays single-player."); }
        }

        private float _broadcastAt = -1f;

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        private void Update()
        {
            if (!_registered || ZNet.instance == null || ZRoutedRpc.instance == null) return;
            if (IsServer)
            {
                // You're the server (or hosting): your own settings are the shared ones.
                if (_broadcastAt > 0f && Time.time >= _broadcastAt) { _broadcastAt = -1f; BroadcastLook(); }
                return;
            }

            // Joined someone else's server: say hello once you're in the world.
            // Sent to "everybody" (ID 0), the same way the standard ServerSync library reaches the server:
            // the server answers; other players' games see it isn't for them and ignore it.
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
                    Plugin.DebugLog("Server sync: said hello to the server.");
                }
                catch (Exception e)
                {
                    _reportedNoReply = true;
                    Plugin.Log.LogWarning($"Server sync: couldn't reach the server ({e.Message}). Using your own goblin.");
                }
            }

            if (_helloSent && !ServerHasMod && !_reportedNoReply && Time.time - _helloAt > 15f)
            {
                _reportedNoReply = true;
                Plugin.Log.LogInfo("Server sync: this server doesn't have Fuling Heckler. Your goblin, your settings, only you see him.");
            }
        }

        // ---- On the server -------------------------------------------------

        private static void OnHello(long sender, ZPackage pkg)
        {
            if (!IsServer) return;
            try
            {
                int protocol = pkg.ReadInt();
                string version = pkg.ReadString();
                if (protocol == Protocol) _modPeers.Add(sender); // only players who understand our messages get them
                Plugin.Log.LogInfo($"Server sync: a player with Fuling Heckler {version} joined" +
                                   (version != Plugin.Version ? $" (server has {Plugin.Version})." : "."));
                SendLook(sender);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Server sync: odd hello from a player ({e.Message})."); }
        }

        private static void SendLook(long target)
        {
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(Plugin.Version);
            bool share = Plugin.ShareLook.Value;
            pkg.Write(share);
            if (share) SharedLook.FromConfig().WriteTo(pkg);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcLook, pkg);
        }

        /// <summary>The admin changed a look setting on the server (or a host changed theirs): tell everyone with the mod.</summary>
        private void OnLocalLookChanged()
        {
            if (!_registered || !IsServer || ZRoutedRpc.instance == null || _modPeers.Count == 0) return;
            _broadcastAt = Time.time + 1f; // a slider drag fires many changes; send once it settles
        }

        private void BroadcastLook()
        {
            foreach (long peer in _modPeers)
            {
                try { SendLook(peer); }
                catch (Exception e) { Plugin.Log.LogWarning($"Server sync: couldn't update a player ({e.Message})."); }
            }
            Plugin.DebugLog($"Server sync: sent the new look to {_modPeers.Count} player(s).");
        }

        // ---- On a player's game ----------------------------------------------

        private static void OnLook(long sender, ZPackage pkg)
        {
            if (IsServer) return;
            // Only the server decides the goblin and his rules; a message claiming to be from it but sent by a player is ignored.
            var serverPeer = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (serverPeer != null && sender != serverPeer.m_uid) return;
            try
            {
                int protocol = pkg.ReadInt();
                string serverVersion = pkg.ReadString();
                bool firstReply = !ServerHasMod;
                ServerHasMod = true;
                ServerId = sender;

                if (protocol != Protocol)
                {
                    if (firstReply) Warn($"The server has Fuling Heckler {serverVersion} and you have {Plugin.Version}. " +
                                         "Update to match to get the server's goblin; until then you get your own.",
                                         Translations.T("fulingheckler_update", serverVersion, Plugin.Version));
                    return;
                }
                if (firstReply && serverVersion != Plugin.Version)
                    Plugin.Log.LogInfo($"Server sync: server has {serverVersion}, you have {Plugin.Version}. Compatible, but updating is a good idea.");

                ServerCompatible = true;
                bool share = pkg.ReadBool();
                ServerShares = share;
                Look.Server = share ? SharedLook.ReadFrom(pkg) : null;

                if (!share)
                {
                    if (firstReply) Plugin.Log.LogInfo("Server sync: the server has the mod but doesn't share its look. Using your own goblin.");
                }
                else Plugin.Log.LogInfo(firstReply
                    ? $"Server sync: using the server's goblin ('{Look.Server.Name}'). Your own look settings are kept for other worlds."
                    : $"Server sync: the server updated its goblin ('{Look.Server.Name}').");
            }
            catch (Exception e)
            {
                Look.Server = null;
                Plugin.Log.LogWarning($"Server sync: couldn't read the server's goblin ({e.Message}). Using your own.");
            }
        }

        private static void Warn(string msg, string onScreen)
        {
            Plugin.Log.LogWarning("Server sync: " + msg);
            try { Delivery.ShowPlainText("Fuling Heckler: " + onScreen); } catch { /* not important */ }
        }
    }
}
