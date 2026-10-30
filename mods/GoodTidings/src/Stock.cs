using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// What Dagny brings (DESIGN.md, set with the user 2026-10-28). Fifteen lines a visit:
    /// - 1-3 always: a fishing rod, bait and barrel hoops (Haldor's prices);
    /// - 4-13 at random from every land the world has unlocked (its boss beaten), goods and meads together, newer lands much likelier;
    /// - 14 at random from the newest land;
    /// - 15 the newest land's rare.
    /// Each visit rolls once from a seed kept on her, so every player sees the same goods, and the world's keys decide what's open (she
    /// never sells ahead of its progress). Ordinary lines can be bought 3 times a visit, the rare once, counted on her for everyone.
    /// First prices; to become a file the server decides.
    /// </summary>
    internal static class Stock
    {
        private struct Good
        {
            public string Prefab;
            public int Stack, Price;
            public Good(string prefab, int stack, int price) { Prefab = prefab; Stack = stack; Price = price; }
        }

        private sealed class Land
        {
            public string Name, Key;
            public Good[] Goods, Meads = new Good[0], Rares = new Good[0];
        }

        private static readonly Good[] Always =
        {
            new Good("FishingRod", 1, 350), new Good("FishingBait", 20, 10), new Good("BarrelRings", 3, 100),
        };

        // in the order the world opens them
        private static readonly Land[] Lands =
        {
            new Land { Name = "Meadows", Key = "",
                Goods = new[] { new Good("Raspberry", 10, 15), new Good("Honey", 5, 25), new Good("DeerHide", 5, 25) },
                Rares = new[] { new Good("QueenBee", 1, 150) } },
            new Land { Name = "Black Forest", Key = "defeated_eikthyr",
                Goods = new[] { new Good("Thistle", 5, 30), new Good("Blueberries", 10, 20), new Good("CopperOre", 5, 60), new Good("TinOre", 5, 40) },
                Meads = new[] { new Good("MeadHealthMinor", 3, 60), new Good("MeadStaminaMinor", 3, 60) },
                Rares = new[] { new Good("SurtlingCore", 2, 120) } },
            new Land { Name = "Swamp", Key = "defeated_gdking",
                Goods = new[] { new Good("Bloodbag", 5, 50), new Good("Ooze", 5, 50), new Good("Guck", 5, 50), new Good("IronScrap", 5, 100) },
                Meads = new[] { new Good("MeadPoisonResist", 2, 90), new Good("MeadBugRepellent", 2, 60),
                                new Good("MeadStrength", 2, 90) },   // (the Bog Witch's: she lives in the Swamp)
                Rares = new[] { new Good("Chain", 2, 200) } },
            new Land { Name = "Mountains", Key = "defeated_bonemass",
                Goods = new[] { new Good("WolfPelt", 3, 60), new Good("Obsidian", 5, 60), new Good("SilverOre", 5, 150), new Good("WolfClaw", 3, 90),
                                new Good("WolfHairBundle", 3, 60), new Good("FreezeGland", 3, 60) },
                Meads = new[] { new Good("MeadFrostResist", 2, 120), new Good("MeadHealthMedium", 3, 120), new Good("MeadStaminaMedium", 3, 120) },
                Rares = new[] { new Good("Crystal", 5, 150) } },
            new Land { Name = "Plains", Key = "defeated_dragon",
                Goods = new[] { new Good("Tar", 5, 80), new Good("Barley", 10, 80), new Good("Flax", 10, 80), new Good("BlackMetalScrap", 5, 200),
                                new Good("LoxPelt", 2, 100), new Good("Cloudberry", 10, 50) },
                Meads = new[] { new Good("MeadTasty", 3, 120), new Good("BarleyWine", 2, 150) },
                Rares = new[] { new Good("Needle", 10, 150) } },
            new Land { Name = "Mistlands", Key = "defeated_goblinking",
                Goods = new[] { new Good("RoyalJelly", 5, 100), new Good("Sap", 5, 100), new Good("Softtissue", 5, 150), new Good("MushroomMagecap", 5, 100),
                                new Good("MushroomJotunPuffs", 5, 100), new Good("Carapace", 5, 150), new Good("Mandible", 2, 150),
                                new Good("Lantern", 1, 300), new Good("DvergrNeedle", 1, 300) },
                Meads = new[] { new Good("MeadEitrMinor", 3, 150), new Good("MeadHealthMajor", 3, 200), new Good("MeadStaminaLingering", 2, 150),
                                new Good("MeadLightfoot", 2, 150) },
                Rares = new[] { new Good("BlackCore", 1, 400) } },
            new Land { Name = "Ashlands", Key = "defeated_queen",
                Goods = new[] { new Good("FlametalOreNew", 5, 300), new Good("SulfurStone", 5, 150), new Good("CharredBone", 5, 100), new Good("AskHide", 3, 150),
                                new Good("AskBladder", 3, 120), new Good("MorgenSinew", 2, 250), new Good("CelestialFeather", 3, 200),
                                new Good("BonemawSerpentScale", 3, 200), new Good("GemstoneBlue", 1, 300), new Good("GemstoneGreen", 1, 300),
                                new Good("GemstoneRed", 1, 300) },
                Meads = new[] { new Good("MeadHealthLingering", 2, 200), new Good("MeadEitrLingering", 2, 200) },
                Rares = new[] { new Good("MoltenCore", 1, 500), new Good("MorgenHeart", 1, 600) } },
            new Land { Name = "Deep North", Key = "defeated_fader",
                Goods = new[] { new Good("Frostwood", 10, 200), new Good("SealHide", 3, 200), new Good("SealBlubber", 3, 150), new Good("FrozenFuel", 5, 250),
                                new Good("Ice", 10, 100), new Good("Lingonberry", 10, 150) },
                Rares = new[] { new Good("FrostCore", 1, 600), new Good("OrbFrostFire", 1, 500) } },
        };

        private const int RandomSlots = 10;
        internal const int MaxBuys = 3, MaxRareBuys = 1;
        internal const float Discount = 0.3f;
        private static readonly HashSet<string> Missing = new HashSet<string>();

        /// <summary>One visit's goods, in order, with how many times each can be bought (int.MaxValue: always there).</summary>
        internal static List<(string prefab, int stack, int price, int buys)> Roll(int seed)
        {
            var rnd = new System.Random(seed);
            var open = Lands.Where(l => l.Key == "" || ZoneSystem.instance.GetGlobalKey(l.Key)).ToList();
            Land newest = open[open.Count - 1];
            var lines = new List<(string, int, int, int)>();
            var taken = new HashSet<string>();
            void Add(Good g, int buys) { if (Exists(g.Prefab) && taken.Add(g.Prefab)) lines.Add((g.Prefab, g.Stack, g.Price, buys)); }

            foreach (Good g in Always) Add(g, int.MaxValue);

            // 4-13: every open land's goods and meads, a land's weight growing with its place (squared): newer lands come up far more
            var pool = new List<(Good good, float weight)>();
            for (int i = 0; i < open.Count; i++)
                foreach (Good g in open[i].Goods.Concat(open[i].Meads))
                    pool.Add((g, (i + 1) * (i + 1)));
            for (int n = 0; n < RandomSlots; n++)
            {
                var left = pool.Where(p => !taken.Contains(p.good.Prefab) && Exists(p.good.Prefab)).ToList();
                if (left.Count == 0) break;
                Add(Weighted(rnd, left), MaxBuys);
            }

            // 14: from the newest land; 15: its rare
            var newestLeft = newest.Goods.Concat(newest.Meads).Where(g => !taken.Contains(g.Prefab) && Exists(g.Prefab)).ToList();
            if (newestLeft.Count > 0) Add(newestLeft[rnd.Next(newestLeft.Count)], MaxBuys);
            var rares = newest.Rares.Where(g => Exists(g.Prefab)).ToList();
            if (rares.Count > 0) Add(rares[rnd.Next(rares.Count)], MaxRareBuys);
            return lines;
        }

        private static Good Weighted(System.Random rnd, List<(Good good, float weight)> pool)
        {
            double pick = rnd.NextDouble() * pool.Sum(p => p.weight);
            foreach (var p in pool)
                if ((pick -= p.weight) <= 0) return p.good;
            return pool[pool.Count - 1].good;
        }

        private static bool Exists(string prefab)
        {
            if (ObjectDB.instance && ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()) return true;
            if (Missing.Add(prefab)) Plugin.Log.LogWarning($"Dagny can't sell {prefab}: no such item");
            return false;
        }

        /// <summary>
        /// Her shop's list for a visit: <paramref name="discounts"/> are prefab names at 30% off (Atlas's meals), <paramref name="sold"/>
        /// "prefab:times" bought this visit (sold-out lines are left out).
        /// </summary>
        internal static List<Trader.TradeItem> Items(int seed, string discounts, string sold)
        {
            var items = new List<Trader.TradeItem>();
            foreach (var (prefab, stack, price, buys) in Roll(seed))
            {
                if (buys != int.MaxValue && Count(sold, prefab) >= buys) continue;
                ItemDrop drop = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>();
                bool off = Has(discounts, prefab);
                int left = buys == int.MaxValue ? 0 : buys - Count(sold, prefab);
                string note = (off ? "\n\n<color=yellow>$goodtidings_discounted</color>" : "") +
                              (left > 0 ? $"\n<color=#bdb7a9>{Localization.instance.Localize("$goodtidings_left", left.ToString())}</color>" : "");
                // (every text set, and the buy effects: the store window reads them, and a TradeItem made in code leaves them null)
                items.Add(new Trader.TradeItem
                {
                    m_prefab = drop, m_stack = stack, m_price = off ? Mathf.Max(1, Mathf.RoundToInt(price * (1f - Discount))) : price,
                    m_requiredGlobalKey = "", m_name = "", m_buyKey = "", m_incrementKey = "", m_buyPlayerEffects = new EffectList(),
                    m_tooltip = note.Length > 0 ? drop.m_itemData.GetTooltip(stack) + note : "",
                });
            }
            return items;
        }

        /// <summary>Muninn's reward: two of the newest land's goods or meads, and now and then (1 in 4; always with <paramref name="rare"/>) its rare.</summary>
        internal static List<(string prefab, int stack)> Reward(System.Random rnd, bool rare = false, int extra = 0)
        {
            var open = Lands.Where(l => l.Key == "" || ZoneSystem.instance.GetGlobalKey(l.Key)).ToList();
            Land newest = open[open.Count - 1];
            var pool = newest.Goods.Concat(newest.Meads).Where(g => Exists(g.Prefab)).OrderBy(_ => rnd.Next()).Take(2 + extra).ToList();
            if ((rare || rnd.NextDouble() < 0.25) && newest.Rares.Length > 0) pool.Add(newest.Rares[rnd.Next(newest.Rares.Length)]);
            return pool.Select(g => (g.Prefab, g.Stack)).ToList();
        }

        private static bool Has(string list, string prefab) => ("," + list + ",").Contains("," + prefab + ",");

        internal static string AddDiscount(string list, string prefab) => string.IsNullOrEmpty(list) ? prefab : list + "," + prefab;

        /// <summary>One of this visit's lines that isn't already cheaper or sold out, at random (null: none left).</summary>
        internal static string PickDiscount(int seed, string discounts, string sold)
        {
            var open = Roll(seed).Where(l => !Has(discounts, l.prefab) && (l.buys == int.MaxValue || Count(sold, l.prefab) < l.buys))
                                 .Select(l => l.prefab).ToList();
            return open.Count > 0 ? open[Random.Range(0, open.Count)] : null;
        }

        internal static int Count(string sold, string prefab)
        {
            foreach (string part in (sold ?? "").Split(','))
            {
                int colon = part.LastIndexOf(':');
                if (colon > 0 && part.Substring(0, colon) == prefab && int.TryParse(part.Substring(colon + 1), out int n)) return n;
            }
            return 0;
        }

        internal static string AddSold(string sold, string prefab)
        {
            int n = Count(sold, prefab) + 1;
            var parts = (sold ?? "").Split(',').Where(p => p.Length > 0 && !p.StartsWith(prefab + ":")).ToList();
            parts.Add(prefab + ":" + n);
            return string.Join(",", parts);
        }
    }
}
