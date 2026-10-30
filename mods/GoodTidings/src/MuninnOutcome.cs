using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// What a riddle brings, on Muninn's owner (the game that gives and spawns: items handed out by one game only).
    /// Right: a bundle from the world's newest land (two of its goods, now and then its rare). Wrong: he takes flight with a cry and the
    /// land's beasts answer it (ravens follow battle, and hunt with wolves): a few of the newest land's creatures, hunting the base.
    /// </summary>
    internal static class MuninnOutcome
    {
        /// <summary>The reward at his feet; an old riddle (<paramref name="lore"/>) always brings the land's rare too.</summary>
        internal static void Reward(Vector3 at, bool lore = false)
        {
            foreach ((string prefab, int stack) in Stock.Reward(new System.Random(Random.Range(0, int.MaxValue)), lore))
            {
                ItemDrop drop = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
                if (drop == null) continue;
                Vector3 p = at + Random.insideUnitSphere * 0.4f;
                ItemDrop.DropItem(drop.m_itemData, stack, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
        }

        // each land's beasts, in the order the world opens them (the newest unlocked answers the cry)
        private static readonly (string key, string creature)[] Beasts =
        {
            ("", "Neck"), ("defeated_eikthyr", "Greydwarf"), ("defeated_gdking", "Draugr"), ("defeated_bonemass", "Wolf"),
            ("defeated_dragon", "Goblin"), ("defeated_goblinking", "Seeker"), ("defeated_queen", "Charred_Melee"),
        };

        internal static void CallTheBeasts(Vector3 home)
        {
            int land = 0;
            for (int i = 0; i < Beasts.Length; i++) if (Beasts[i].key == "" || ZoneSystem.instance.GetGlobalKey(Beasts[i].key)) land = i;
            GameObject prefab = ZNetScene.instance.GetPrefab(Beasts[land].creature);
            if (prefab == null) return;
            int count = land >= 4 ? 4 : 3;   // (3 at first, a little more in the later lands)
            for (int n = 0; n < count; n++)
            {
                Vector3 p = home + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(22f, 30f);
                if (ZoneSystem.instance.GetSolidHeight(p, out float h)) p.y = h + 0.5f;
                GameObject c = Object.Instantiate(prefab, p, Quaternion.LookRotation(home - p));
                BaseAI ai = c.GetComponent<BaseAI>();
                if (ai) { ai.SetHuntPlayer(true); }
            }
            Plugin.Log.LogInfo($"Muninn's cry: {count} {Beasts[land].creature} come for the base at {home}");
        }
    }
}
