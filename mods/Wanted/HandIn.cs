using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// Handing notices in: finished papers (and, for the kinds that ask for things, those things) go into a board's box, and when the lid
    /// closes each notice that has everything it needs turns into its reward in the box. It runs on the game of the player closing the box:
    /// opening a chest makes the opener its owner, and the box is still theirs and in use when the close begins, so nothing else touches
    /// it meanwhile. For each notice, all is checked first, then the paper and the things are taken, then the reward is put in: a notice
    /// short of anything, or a box without room for the reward, is left as it was, with a message saying why. Each notice is paid once
    /// (its id is remembered on the board).
    /// </summary>
    internal static class HandIn
    {
        private const string KeyPaid = "Wanted.paid";            // ids of notices this board paid, ','-separated, the newest last
        public const string KeyPouch = "Wanted.pouch";            // the reward items waiting in the box (prefab names, ','-separated), or ""
        private const int PaidKept = 200;

        /// <summary>How many of this item (by prefab) the inventory holds, notices aside.</summary>
        public static int CountOf(Inventory inv, string prefab) =>
            inv.GetAllItems().Where(i => !NoticeItem.Is(i) && PrefabOf(i) == prefab).Sum(i => i.m_stack);

        private static string PrefabOf(ItemDrop.ItemData item) => item.m_dropPrefab != null ? item.m_dropPrefab.name : null;

        // Every way the game closes a chest (Esc, Tab and E through InventoryGui.Hide, walking away through CloseContainer) ends in
        // SetInUse(false) on it: the box is processed just before, while it's still this player's and in use.
        [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
        private static class Container_SetInUse
        {
            private static void Prefix(Container __instance, bool inUse)
            {
                try
                {
                    if (inUse || __instance.GetComponent<NoticeBoard>() == null || !__instance.IsOwner() || !__instance.IsInUse()) return;
                    Process(__instance);
                }
                catch (Exception e) { Plugin.Log.LogError("Handing in notices: " + e); }
            }
        }

        private static void Process(Container box)
        {
            Player player = Player.m_localPlayer;
            ZNetView view = box.GetComponent<ZNetView>();
            if (player == null || view == null || !view.IsValid()) return;
            Inventory inv = box.GetInventory();
            List<ItemDrop.ItemData> notices = inv.GetAllItems().Where(NoticeItem.Is).ToList();
            if (notices.Count == 0) return;
            Plugin.Log.LogInfo($"Box closed with {notices.Count} notice(s) in it: checking them");

            List<string> paid = (view.GetZDO().GetString(KeyPaid, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var paidNow = new List<Contract>();
            var rewardItems = new HashSet<string>((view.GetZDO().GetString(KeyPouch, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

            foreach (ItemDrop.ItemData notice in notices)
            {
                Contract c = NoticeItem.ContractOf(notice);
                if (c == null) continue;
                if (paid.Contains(c.Id)) { player.Message(MessageHud.MessageType.Center, T("wanted_already_paid")); continue; }

                // 1. everything it needs
                if (c.Kind == Kind.Kill)
                {
                    int n = Kills.Progress(notice);
                    if (n < c.Count) { player.Message(MessageHud.MessageType.Center, T("wanted_not_done", c.Line(), n, c.Count)); continue; }
                }
                else
                {
                    int have = CountOf(inv, c.Target);
                    if (have < c.Count) { player.Message(MessageHud.MessageType.Center, T("wanted_short", c.Line(), have, c.Count)); continue; }
                }

                // 2. room for the reward: the paper's own cell, plus empty ones (taken things may free more; not counted)
                var reward = c.RewardItems().Select(r => (prefab: ZNetScene.instance.GetPrefab(r.Key), amount: r.Value))
                                            .Where(r => r.prefab != null && r.prefab.GetComponent<ItemDrop>() != null).ToList();
                int cells = reward.Sum(r => Mathf.CeilToInt(r.amount / (float)Mathf.Max(1, r.prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize)));
                if (inv.GetEmptySlots() + 1 < cells) { player.Message(MessageHud.MessageType.Center, T("wanted_box_full")); continue; }

                // 3. taken (the id first: if anything below throws, this notice can't be paid again)
                paid.Add(c.Id);
                if (paid.Count > PaidKept) paid.RemoveRange(0, paid.Count - PaidKept);
                view.GetZDO().Set(KeyPaid, string.Join(",", paid));
                inv.RemoveItem(notice);
                if (c.Kind != Kind.Kill) Take(inv, c.Target, c.Count);

                // 4. paid
                foreach (var (prefab, amount) in reward)
                {
                    if (!inv.AddItem(prefab, amount))
                        Plugin.Log.LogWarning($"The box had no room for {amount} {prefab.name} after all (notice {c.Id})");
                    rewardItems.Add(prefab.name);
                }
                paidNow.Add(c);
            }

            if (paidNow.Count == 0) return;
            view.GetZDO().Set(KeyPouch, string.Join(",", rewardItems));
            foreach (Contract c in paidNow)
                player.Message(MessageHud.MessageType.TopLeft, T("wanted_paid", c.Line(), c.RewardText()));
            PlaySound(box.transform.position);
        }

        /// <summary>Takes this many of the item (by prefab) out of the inventory, smallest stacks first.</summary>
        private static void Take(Inventory inv, string prefab, int count)
        {
            foreach (ItemDrop.ItemData item in inv.GetAllItems().Where(i => !NoticeItem.Is(i) && PrefabOf(i) == prefab).OrderBy(i => i.m_stack).ToList())
            {
                if (count <= 0) break;
                int take = Mathf.Min(count, item.m_stack);
                inv.RemoveItem(item, take);
                count -= take;
            }
        }

        /// <summary>The game's sound of coins being put down, at the box (the game's own effect: everyone near hears it).</summary>
        private static void PlaySound(Vector3 at)
        {
            GameObject sfx = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("sfx_coins_placed") : null;
            if (sfx != null) Object.Instantiate(sfx, at + Vector3.up * 0.5f, Quaternion.identity);
        }
    }
}
