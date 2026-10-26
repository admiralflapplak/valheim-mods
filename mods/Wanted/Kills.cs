using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// Kills counted on Bounty notices. A creature dies on the game that runs it (its owner); that game tells the player who landed the
    /// blow and every player near the creature (they fought it together), each by a routed message to their own game, which counts the
    /// kill on the matching notices that player carries. Only kills a player made count: a creature drowned or killed by another
    /// creature is no one's bounty.
    /// </summary>
    internal static class Kills
    {
        private const string Rpc = "Wanted_Kill";
        private const float ShareRange = 30f;
        private const string KeyProgress = "Wanted.progress";   // m_customData on the notice: kills counted so far

        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        private static readonly System.Reflection.FieldInfo FunctionsField = AccessTools.Field(typeof(ZRoutedRpc), "m_functions");
        private static System.Collections.IDictionary Functions(ZRoutedRpc rpc) => (System.Collections.IDictionary)FunctionsField.GetValue(rpc);
        private static ZRoutedRpc _registeredWith;

        /// <summary>From the plugin's Update: register the message with each new ZRoutedRpc (one per world joined).</summary>
        public static void Update()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredWith)) return;
            _registeredWith = rpc;
            Functions(rpc).Remove(Rpc.GetStableHashCode());   // an older copy's (a hot reload): registering a name twice throws
            rpc.Register<string>(Rpc, (sender, prefab) => Credit(prefab));
        }

        public static void Unregister()
        {
            if (_registeredWith != null) Functions(_registeredWith).Remove(Rpc.GetStableHashCode());
            _registeredWith = null;
        }

        public static int Progress(ItemDrop.ItemData notice) =>
            notice.m_customData.TryGetValue(KeyProgress, out string s) && int.TryParse(s, out int n) ? n : 0;

        /// <summary>Whether a notice has all it needs to be handed in (a bounty: all its kills; the rest: checked at the box).</summary>
        public static bool Done(ItemDrop.ItemData notice, Contract c) => c.Kind == Kind.Kill && Progress(notice) >= c.Count;

        /// <summary>On this player's game: a creature of this kind died by players' hands near them.</summary>
        private static void Credit(string prefab)
        {
            try
            {
                Player player = Player.m_localPlayer;
                if (player == null) return;
                foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                {
                    Contract c = NoticeItem.ContractOf(item);
                    if (c == null || c.Kind != Kind.Kill || c.Target != prefab) continue;
                    int n = Progress(item);
                    if (n >= c.Count) continue;
                    item.m_customData[KeyProgress] = (++n).ToString();
                    if (n >= c.Count)
                    {
                        NoticeItem.MarkDone(item, c);
                        player.Message(MessageHud.MessageType.Center, T("wanted_done", c.Line()));
                    }
                    else player.Message(MessageHud.MessageType.TopLeft, T("wanted_progress", c.TargetName(), n, c.Count));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Counting a kill: " + e.Message); }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Character_OnDeath
        {
            private static void Prefix(Character __instance)
            {
                try
                {
                    if (__instance == null || __instance.IsPlayer()) return;
                    ZNetView view = __instance.GetComponent<ZNetView>();
                    if (view == null || !view.IsValid() || !view.IsOwner()) return;   // (only the game running it: once per death)
                    if (!(LastHit(__instance)?.GetAttacker() is Player killer)) return;

                    string prefab = Utils.GetPrefabName(__instance.gameObject);
                    var near = new List<Player>();
                    Player.GetPlayersInRange(__instance.transform.position, ShareRange, near);
                    if (!near.Contains(killer)) near.Add(killer);
                    foreach (Player p in near)
                    {
                        if (p == Player.m_localPlayer) { Credit(prefab); continue; }
                        ZNetView pv = p.GetComponent<ZNetView>();
                        if (pv != null && pv.IsValid()) ZRoutedRpc.instance?.InvokeRoutedRPC(pv.GetZDO().GetOwner(), Rpc, prefab);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Sharing a kill: " + e.Message); }
            }
        }
    }
}
