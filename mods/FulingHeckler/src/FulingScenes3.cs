using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// The Gifter: sneaks up from behind and slips things into your pockets. Mostly junk, now and then something nice.
    /// Turn around and catch him and he drops it all on the ground and bolts.
    /// </summary>
    internal partial class FulingActor
    {
        private IEnumerator Gifter()
        {
            const float creepSpeed = 2.6f;   // a walk, not a run: he's sneaking
            const float reach = 1.6f;
            float timeout = 120f, t = 0f, watched = 0f;
            bool caught = false;

            while (t < timeout)
            {
                var p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                float dist = DistanceToPlayer(p);
                if (dist > 60f) { VanishHere(); yield break; } // you walked off; he gives up quietly

                // Caught = you're facing him and can actually see him, for a moment (a quick camera swing doesn't count).
                bool seen = IsFacedByPlayer(p, transform.position, Plugin.RedLightWatchAngle.Value) && HasLineOfSight(Head);
                if (RemoteSaw(_pokeWatched)) seen = true; // another player caught him sneaking
                watched = seen ? watched + Time.deltaTime : 0f;
                if (watched > 0.3f) { caught = true; break; }

                if (dist <= reach + 0.2f) break;
                Vector3 to = Flat(p.transform.position - transform.position);
                MoveToward(to.normalized, Mathf.Min(creepSpeed * Time.deltaTime, dist - reach), creepSpeed);

                t += Time.deltaTime;
                yield return null;
            }
            SetSpeed(0f);

            if (t >= timeout) { VanishHere(); yield break; }

            var player = Player.m_localPlayer;
            if (player == null) { Destroy(gameObject); yield break; }
            var gifts = Gifts.Roll(Rng);

            if (caught)
            {
                // Busted. He flinches, drops the lot at his feet and runs.
                FaceDirection(Flat(player.transform.position - transform.position));
                SetTrigger("stagger");
                TrySayLine(SceneLine("gift_caught", "{me} not here! Is present! Bye!", "Eep! Surprise ruined!", "{me} was never here. Take it!"), 2.5f);
                Sfx(SoundMoment.Caught);
                int dropped = Gifts.DropAt(gifts, transform.position + Vector3.up * 0.6f);
                Plugin.Log.LogInfo($"Gifter: caught! He dropped {dropped} gift(s) on the ground and bolts.");
                yield return new WaitForSeconds(0.5f);
                yield return RunOffAndVanish(RunSpeed * 1.5f, Plugin.ExitRunDistance.Value);
                yield break;
            }

            // Made it. Slip them in, giggle, and gone.
            float slipTime = PlayGrabAnimation(Plugin.ThiefGrabAnimation.Value);
            yield return new WaitForSeconds(Mathf.Min(slipTime, 0.8f));
            RestoreEmote();

            player = Player.m_localPlayer;
            if (player == null || player.IsDead()) { Destroy(gameObject); yield break; }
            var best = Gifts.GiveTo(player, gifts, out int given, out int onGround);

            if (given + onGround == 0)
            {
                Plugin.Log.LogInfo("Gifter: nothing in the loot table could be given (check the item names in the log). He leaves.");
                VanishHere();
                yield break;
            }

            Delivery.ShowPlainText(onGround > 0 ? "Your pockets feel heavier... and something fell out." : "Your pockets feel heavier...");
            Sfx(SoundMoment.Gift);
            string line;
            switch (best)
            {
                case LootTier.Treasure: line = SceneLine("gift_treasure", "{me} found it. {me} not want it. Yours."); break;
                case LootTier.Nice: line = SceneLine("gift_nice", "{me} make that. You welcome."); break;
                default: line = SceneLine("gift_junk", "Present! Hehehe!", "{me} share. {me} generous."); break;
            }
            TrySayLine(line, 2.5f);
            Plugin.Log.LogInfo($"Gifter: slipped you {given} gift(s){(onGround > 0 ? $", {onGround} didn't fit and dropped at your feet" : "")}. Best: {best}.");
            yield return new WaitForSeconds(0.8f);
            VanishHere();
        }
    }

    /// <summary>The real-item side of the Gifter: rolling the loot table and putting things in your pockets safely.</summary>
    internal static class Gifts
    {
        /// <summary>A crafter ID that isn't any real player's, so "Crafted by Fuling" shows on the tooltip.</summary>
        private const long FulingCrafterID = 0x46554C494E47L; // "FULING"

        private static readonly HashSet<string> _warnedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public class Gift
        {
            public LootEntry Entry;
            public GameObject Prefab;
            public int Amount;
        }

        /// <summary>Is there room for his gifts? Enough empty slots, and not near your carry limit.</summary>
        public static bool HasRoom(Player p, out string why)
        {
            why = null;
            try
            {
                var inv = p.GetInventory();
                if (inv == null) { why = "no inventory"; return false; }
                int free = inv.GetEmptySlots();
                if (free < Plugin.GifterFreeSlots.Value)
                {
                    why = $"you need at least {Plugin.GifterFreeSlots.Value} empty inventory slots (you have {free})";
                    return false;
                }
                if (inv.GetTotalWeight() > p.GetMaxCarryWeight() - 15f)
                {
                    why = "you're too close to your carry limit";
                    return false;
                }
                if (Plugin.Loot == null || Plugin.Loot.Entries.Count == 0)
                {
                    why = "the loot table is empty";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                why = "couldn't check your inventory (" + e.Message + ")";
                return false;
            }
        }

        /// <summary>Picks this visit's gifts from the loot table. Unknown item names are skipped (and logged once).</summary>
        public static List<Gift> Roll(System.Random rng)
        {
            var list = new List<Gift>();
            if (Plugin.Loot == null || ObjectDB.instance == null) return list;
            int min = Mathf.Min(Plugin.GiftsMin.Value, Plugin.GiftsMax.Value);
            int max = Mathf.Max(Plugin.GiftsMin.Value, Plugin.GiftsMax.Value);
            int rolls = rng.Next(min, max + 1);

            for (int i = 0; i < rolls; i++)
            {
                var entry = Plugin.Loot.Pick(rng);
                if (entry == null) break;
                var prefab = ObjectDB.instance.GetItemPrefab(entry.Item);
                if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
                {
                    if (_warnedMissing.Add(entry.Item))
                        Plugin.Log.LogWarning($"Loot file: there's no item called '{entry.Item}'. Use prefab names like Pukeberries or ArmorRagsChest.");
                    continue;
                }
                list.Add(new Gift { Entry = entry, Prefab = prefab, Amount = rng.Next(entry.Min, entry.Max + 1) });
            }
            return list;
        }

        /// <summary>A fresh copy of the item, ready to give: full durability, quality 1, signed if it's equipment.</summary>
        private static ItemDrop.ItemData MakeItem(Gift g, int stack)
        {
            var item = g.Prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = g.Prefab;
            item.m_stack = stack;
            item.m_quality = 1;
            item.m_durability = item.GetMaxDurability();
            try
            {
                // Newer game versions scale items by world level; match the world you're in. Older versions don't have it.
                var level = Traverse.Create(typeof(Game)).Field("m_worldLevel");
                var field = Traverse.Create(item).Field("m_worldLevel");
                if (level.FieldExists() && field.FieldExists()) field.SetValue(level.GetValue());
            }
            catch { /* not important */ }
            if (Plugin.SignGifts.Value && item.m_shared.m_maxStackSize <= 1)
            {
                item.m_crafterID = FulingCrafterID;
                item.m_crafterName = Look.Name;
            }
            return item;
        }

        /// <summary>
        /// Puts the gifts in your inventory. Anything that won't fit, or would push you over your carry weight,
        /// drops at your feet instead. Returns the best tier given.
        /// </summary>
        public static LootTier GiveTo(Player p, List<Gift> gifts, out int given, out int onGround)
        {
            given = 0; onGround = 0;
            var best = LootTier.Junk;
            var inv = p.GetInventory();
            if (inv == null) return best;

            foreach (var g in gifts)
            {
                bool any = false;
                int left = g.Amount;
                while (left > 0)
                {
                    int max = Mathf.Max(1, g.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                    int stack = Mathf.Min(left, max);
                    left -= stack;
                    var item = MakeItem(g, stack);
                    try
                    {
                        bool tooHeavy = inv.GetTotalWeight() + item.GetWeight() > p.GetMaxCarryWeight();
                        bool keepFree = inv.GetEmptySlots() <= 1; // never take your last slot
                        if (!tooHeavy && !keepFree && inv.AddItem(item)) { given++; any = true; continue; }
                    }
                    catch (Exception e) { Plugin.Log.LogWarning($"Gifter: couldn't add {g.Entry.Item} ({e.Message}); dropping it instead."); }

                    if (Drop(item, p.transform.position + p.transform.forward * 0.8f + Vector3.up * 0.8f)) { onGround++; any = true; }
                }
                if (any && g.Entry.Tier > best) best = g.Entry.Tier;
            }
            return best;
        }

        /// <summary>Drops all the gifts on the ground at a spot (when you catch him). Returns how many stacks landed.</summary>
        public static int DropAt(List<Gift> gifts, Vector3 position)
        {
            int n = 0;
            foreach (var g in gifts)
            {
                int left = g.Amount;
                while (left > 0)
                {
                    int max = Mathf.Max(1, g.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                    int stack = Mathf.Min(left, max);
                    left -= stack;
                    if (Drop(MakeItem(g, stack), position)) n++;
                }
            }
            return n;
        }

        private static bool Drop(ItemDrop.ItemData item, Vector3 position)
        {
            try
            {
                var spawned = ItemDrop.DropItem(item, item.m_stack, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                return spawned != null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Gifter: dropping an item failed ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }
    }
}
