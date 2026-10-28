using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// What the wandering trader sells (DESIGN.md): some of Haldor's goods, finished meads, ingredients that are hard to gather, and the
    /// gemstones once the Queen is beaten. Each needs the world to have beaten that land's boss (a global key), as Haldor's do, so she
    /// never sells ahead of the world's progress. First prices; to become a file the server decides.
    /// </summary>
    internal static class Stock
    {
        // prefab, how many, price in coins, the boss key it waits for ("" = from the start)
        private static readonly (string prefab, int stack, int price, string key)[] Goods =
        {
            // Haldor's
            ("FishingRod", 1, 350, ""),
            ("FishingBait", 20, 40, ""),
            ("BarrelRings", 1, 100, "defeated_bonemass"),
            // finished meads
            ("MeadHealthMinor", 3, 60, "defeated_eikthyr"),
            ("MeadStaminaMinor", 3, 60, "defeated_eikthyr"),
            ("MeadPoisonResist", 2, 90, "defeated_gdking"),
            ("MeadHealthMedium", 3, 120, "defeated_bonemass"),
            ("MeadStaminaMedium", 3, 120, "defeated_bonemass"),
            ("MeadFrostResist", 2, 120, "defeated_bonemass"),
            ("MeadTasty", 3, 120, "defeated_dragon"),
            ("MeadEitrMinor", 3, 150, "defeated_goblinking"),
            ("MeadHealthMajor", 3, 200, "defeated_queen"),
            // hard to gather
            ("Thistle", 5, 40, "defeated_eikthyr"),
            ("Bloodbag", 5, 60, "defeated_gdking"),
            ("Ooze", 5, 50, "defeated_gdking"),
            ("Sap", 5, 80, "defeated_bonemass"),
            ("RoyalJelly", 5, 100, "defeated_goblinking"),
            ("MushroomMagecap", 5, 100, "defeated_goblinking"),
            ("MushroomJotunPuffs", 5, 100, "defeated_goblinking"),
            // after the Queen
            ("GemstoneBlue", 1, 300, "defeated_queen"),
            ("GemstoneGreen", 1, 300, "defeated_queen"),
            ("GemstoneRed", 1, 300, "defeated_queen"),
        };

        private static readonly HashSet<string> Missing = new HashSet<string>();

        /// <summary>A fresh list for her shop (the game shows only those whose key the world has).</summary>
        internal static List<Trader.TradeItem> Items()
        {
            var items = new List<Trader.TradeItem>();
            foreach (var g in Goods)
            {
                GameObject prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(g.prefab) : null;
                ItemDrop drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null)
                {
                    if (Missing.Add(g.prefab)) Plugin.Log.LogWarning($"The wandering trader can't sell {g.prefab}: no such item");
                    continue;
                }
                // (every text set, and the buy effects: the store window reads them, and a TradeItem made in code leaves them null)
                items.Add(new Trader.TradeItem
                {
                    m_prefab = drop, m_stack = g.stack, m_price = g.price, m_requiredGlobalKey = g.key,
                    m_name = "", m_tooltip = "", m_buyKey = "", m_incrementKey = "", m_buyPlayerEffects = new EffectList(),
                });
            }
            return items;
        }
    }
}
