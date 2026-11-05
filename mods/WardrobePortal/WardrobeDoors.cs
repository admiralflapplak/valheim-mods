using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static Nick.Shared.Translations;

namespace WardrobePortal
{
    /// <summary>
    /// On each Wardrobe Portal: the doors swing open while a player is near (each player's game swings its own copy: nothing is sent), and
    /// Shift+E shuts them for good, which puts the portal to sleep (no swirl, no way in) until Shift+E opens them again. E sets the tag,
    /// as on any portal. Shut or open is saved in the piece's ZDO and changed only by its owner, through an RPC, so everyone sees the same
    /// and two players pressing at once can't fight.
    /// Someone coming through to a shut wardrobe still gets out: its doors open for a few seconds as they arrive (a time saved in the
    /// ZDO, so everyone near sees it, the traveller too, whose game loads the place only after the jump), then shut again.
    /// </summary>
    internal class WardrobeDoors : MonoBehaviour
    {
        private const string ClosedKey = "nick_wardrobeportal_closed";
        private const string ToggleRpc = "WardrobePortal_Toggle";
        private const string ArrivedKey = "nick_wardrobeportal_arrived";   // until when (world time, ticks) the doors stand open for an arrival
        private const string ArriveRpc = "WardrobePortal_Arrive";
        private const float ArrivalSeconds = 4f;

        // the wardrobe the local player is on the way to, if it's one of ours: told when they get there (cleared then, or after a minute)
        internal static ZDOID Pending = ZDOID.None;
        internal static float PendingSince;

        private ZNetView _nview;
        private TeleportWorld _portal;
        private readonly List<Door> _doors = new List<Door>();
        private float _angle;          // how far open the doors are now (degrees)
        private bool _playerNear;
        private float _nextLook;

        private class Door
        {
            public Transform T;
            public Quaternion Shut;
            public float Sign;         // which way it swings: left and right doors turn opposite ways
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _portal = GetComponent<TeleportWorld>();
            if (_nview == null || _nview.GetZDO() == null) { enabled = false; return; }
            _nview.Register<bool>(ToggleRpc, RPC_Toggle);
            _nview.Register(ArriveRpc, RPC_Arrive);

            // the hinges Cabinet made: the left door (+x) swings one way, the right the other
            Transform model = transform.Find("wardrobe");
            if (model != null)
                foreach (Transform t in model)
                    if (t.name.StartsWith(Cabinet.DoorPrefix, StringComparison.Ordinal))
                        _doors.Add(new Door { T = t, Shut = t.localRotation, Sign = t.localPosition.x < 0f ? -1f : 1f });
            if (_doors.Count == 0) Plugin.Log.LogWarning("Wardrobe Portal: no doors found on the model (they don't swing)");
        }

        public bool Closed => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(ClosedKey);

        /// <summary>Shut, but standing open a moment for someone who just came through.</summary>
        public bool Arriving => Closed && ZNet.instance != null && ZNet.instance.GetTime().Ticks < _nview.GetZDO().GetLong(ArrivedKey);

        /// <summary>Shut for good: no swirl, doors closed.</summary>
        public bool Asleep => Closed && !Arriving;

        /// <summary>Asks the owner to shut (or open) the doors. The access check is the game's own for portals.</summary>
        public void RequestToggle(Humanoid human)
        {
            if (!PrivateArea.CheckAccess(transform.position))
            {
                human.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                return;
            }
            _nview.InvokeRPC(ToggleRpc, !Closed);
        }

        private void RPC_Toggle(long sender, bool closed)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner()) return;   // only the owner writes; the RPC goes to the owner
                _nview.GetZDO().Set(ClosedKey, closed);
            }
            catch (Exception e) { Plugin.Log.LogError("Wardrobe Portal toggle: " + e); }
        }

        private void RPC_Arrive(long sender)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner()) return;
                _nview.GetZDO().Set(ArrivedKey, ZNet.instance.GetTime().AddSeconds(ArrivalSeconds).Ticks);
            }
            catch (Exception e) { Plugin.Log.LogError("Wardrobe Portal arrival: " + e); }
        }

        /// <summary>The local player has come out of this wardrobe: ask its owner to open it for a moment, if it's shut.</summary>
        private void CheckArrival()
        {
            if (Pending == ZDOID.None) return;
            if (Time.time - PendingSince > 60f) { Pending = ZDOID.None; return; }
            Player p = Player.m_localPlayer;
            if (p == null || p.IsTeleporting() || Pending != _nview.GetZDO().m_uid) return;
            if (Vector3.Distance(p.transform.position, transform.position) > 8f) return;
            Pending = ZDOID.None;
            if (Closed) _nview.InvokeRPC(ArriveRpc);
        }

        private void Update()
        {
            CheckArrival();
            if (Time.time >= _nextLook)
            {
                _nextLook = Time.time + 0.25f;
                _playerNear = Player.GetClosestPlayer(transform.position, Plugin.OpenDistance.Value) != null;
            }
            float target = !Asleep && _playerNear ? Plugin.OpenAngle.Value : 0f;
            if (Mathf.Approximately(_angle, target)) return;
            _angle = Mathf.MoveTowards(_angle, target, Plugin.DoorSpeed.Value * Time.deltaTime);
            foreach (Door d in _doors)
                d.T.localRotation = d.Shut * Quaternion.Euler(0f, d.Sign * _angle, 0f);
        }

        public string HoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "";   // going away (logging out, destroyed)
            string tag = _portal != null ? _portal.GetText() : "";
            string state = Closed ? T("wardrobeportal_closed") : "$piece_portal_connected";
            if (!Closed && _nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == ZDOID.None) state = "$piece_portal_unconnected";
            return Localization.instance.Localize(
                $"{T("wardrobeportal_piece")} $piece_portal_tag:\"{tag}\" [{state}]\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] $piece_portal_settag\n" +
                $"[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] {T(Closed ? "wardrobeportal_open" : "wardrobeportal_close")}");
        }
    }

    // ---- the game's portal, for wardrobes only (every other portal runs the game's code untouched) ----

    /// <summary>Shift+E opens or shuts the doors; E sets the tag (the game's own E, left to the game).</summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorld_Interact
    {
        private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, bool alt, ref bool __result)
        {
            WardrobeDoors doors = __instance.GetComponent<WardrobeDoors>();
            if (doors == null || !alt) return true;
            __result = false;
            if (hold) return false;
            doors.RequestToggle(human);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorld_GetHoverText
    {
        private static void Postfix(TeleportWorld __instance, ref string __result)
        {
            WardrobeDoors doors = __instance.GetComponent<WardrobeDoors>();
            if (doors != null) __result = doors.HoverText();
        }
    }

    /// <summary>
    /// Shut doors: no way in (a wardrobe standing open for an arrival lets nobody in either). Any portal leading to a wardrobe: remember
    /// it, so the wardrobe opens for the player when they come out.
    /// </summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class TeleportWorld_Teleport
    {
        private static bool Prefix(TeleportWorld __instance)
        {
            WardrobeDoors doors = __instance.GetComponent<WardrobeDoors>();
            return doors == null || !doors.Closed;
        }

        private static void Postfix(TeleportWorld __instance, Player player)
        {
            try
            {
                if (player == null || player != Player.m_localPlayer || !player.IsTeleporting()) return;
                ZNetView nview = __instance.GetComponent<ZNetView>();
                ZDO own = nview != null ? nview.GetZDO() : null;
                ZDOID target = own != null ? own.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) : ZDOID.None;
                ZDO zdo = target != ZDOID.None ? ZDOMan.instance.GetZDO(target) : null;
                if (zdo == null || zdo.GetPrefab() != WardrobePiece.Hash) return;
                WardrobeDoors.Pending = target;
                WardrobeDoors.PendingSince = Time.time;
            }
            catch (Exception e) { Plugin.Log.LogError("Wardrobe Portal: " + e); }
        }
    }

    /// <summary>Shut doors: no swirl, even with a player close and the other end found (unless standing open for an arrival).</summary>
    [HarmonyPatch(typeof(TeleportWorld), "UpdatePortal")]
    internal static class TeleportWorld_UpdatePortal
    {
        private static void Postfix(TeleportWorld __instance)
        {
            WardrobeDoors doors = __instance.GetComponent<WardrobeDoors>();
            if (doors != null && doors.Asleep && __instance.m_target_found != null) __instance.m_target_found.SetActive(false);
        }
    }
}
