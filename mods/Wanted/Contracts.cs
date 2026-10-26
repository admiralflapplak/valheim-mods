using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// One notice: what it asks (a kind, a target prefab and how many), the stage of the world it was written for, what it pays, and an id of
    /// its own. Saved as plain data (on a board's ZDO, and on the paper item once taken), never as words: each player's game writes the words
    /// in their own language.
    /// </summary>
    internal sealed class Contract
    {
        public Kind Kind;
        public string Target;    // a creature prefab (Kill) or an item prefab (the rest)
        public int Count;
        public int Tier;         // 0 Meadows ... 7 Deep North: the stage it was written for
        public string Reward;    // "Coins:30,Bronze:4" (prefab:amount)
        public string Id;        // random, so the same notice is never taken or paid twice

        // kind|target|count|tier|reward|id: no field holds '|' or ';' (prefab names, numbers, the reward's ':' and ',')
        public string Write() =>
            string.Join("|", ((int)Kind).ToString(CultureInfo.InvariantCulture), Target, Count.ToString(CultureInfo.InvariantCulture),
                Tier.ToString(CultureInfo.InvariantCulture), Reward, Id);

        public static Contract Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] f = text.Split('|');
            if (f.Length < 6) return null;
            if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)
                || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier)) return null;
            return new Contract { Kind = (Kind)kind, Target = f[1], Count = count, Tier = tier, Reward = f[4], Id = f[5] };
        }

        /// <summary>The reward as (item prefab, amount) pairs, skipping anything this game doesn't know.</summary>
        public IEnumerable<KeyValuePair<string, int>> RewardItems()
        {
            foreach (string part in (Reward ?? "").Split(','))
            {
                string[] p = part.Split(':');
                if (p.Length == 2 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                    yield return new KeyValuePair<string, int>(p[0], n);
            }
        }

        // ---- words (each player's own language) ----------------------------------------------------------------------

        /// <summary>The target's name as the game calls it: "Greydwarf", "Fine Wood".</summary>
        public string TargetName() => Contracts.DisplayName(Target);

        /// <summary>"Slay: Greydwarf ×8".</summary>
        public string Line() => T("wanted_line_" + Kind.ToString().ToLowerInvariant(), TargetName(), Count);

        /// <summary>"30 Coins, 4 Bronze".</summary>
        public string RewardText() =>
            string.Join(", ", RewardItems().Select(r => $"{r.Value} {Contracts.DisplayName(r.Key)}"));
    }

    /// <summary>
    /// What notices ask for and pay at each stage of the world, and the writing of new ones. The stage is how far the world has come: each
    /// boss beaten (the world's global keys) opens the next land's creatures, things and food. Lists are prefab names: one this game doesn't
    /// know (a game update renamed it) is skipped, and `wanted names` lists them.
    /// </summary>
    internal static class Contracts
    {
        public const int Tiers = 8;

        /// <summary>The keys the game sets when each boss falls, in order: the stage is how many of them in a row are set.</summary>
        private static readonly string[] BossKeys =
            { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" };

        private struct Entry
        {
            public string Prefab; public int Count;
            public Entry(string prefab, int count) { Prefab = prefab; Count = count; }
        }

        private static Entry E(string prefab, int count) => new Entry(prefab, count);

        // Per stage (0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountain, 4 Plains, 5 Mistlands, 6 Ashlands; 7 has none of its own yet and uses 6's).
        // The count is a usual amount: a notice asks for it, give or take a fifth.
        private static readonly Entry[][] Kill =
        {
            new[] { E("Boar", 6), E("Deer", 5), E("Neck", 6), E("Greyling", 8) },
            new[] { E("Greydwarf", 8), E("Greydwarf_Elite", 3), E("Greydwarf_Shaman", 3), E("Skeleton", 6), E("Troll", 1) },
            new[] { E("Draugr", 6), E("Draugr_Elite", 3), E("Blob", 6), E("Leech", 5), E("Surtling", 5), E("Wraith", 2), E("Abomination", 1) },
            new[] { E("Wolf", 5), E("Hatchling", 4), E("Fenring", 2), E("StoneGolem", 1), E("Ulv", 3), E("Bat", 6) },
            new[] { E("Goblin", 6), E("GoblinBrute", 2), E("GoblinShaman", 2), E("Deathsquito", 4), E("Lox", 2), E("BlobTar", 4) },
            new[] { E("Seeker", 6), E("SeekerBrute", 1), E("Tick", 6), E("Gjall", 1), E("Hare", 6) },
            new[] { E("Charred_Melee", 6), E("Charred_Archer", 4), E("Charred_Mage", 2), E("Asksvin", 4), E("Morgen", 1), E("Volture", 4), E("FallenValkyrie", 1), E("BlobLava", 4) },
        };

        private static readonly Entry[][] Trophy =
        {
            new[] { E("TrophyBoar", 3), E("TrophyDeer", 3), E("TrophyNeck", 3) },
            new[] { E("TrophyGreydwarf", 5), E("TrophyGreydwarfBrute", 2), E("TrophyGreydwarfShaman", 2), E("TrophySkeleton", 3), E("TrophyFrostTroll", 1) },
            new[] { E("TrophyDraugr", 3), E("TrophyDraugrElite", 2), E("TrophyBlob", 3), E("TrophyLeech", 3), E("TrophySurtling", 3), E("TrophyWraith", 1), E("TrophyAbomination", 1) },
            new[] { E("TrophyWolf", 3), E("TrophyHatchling", 3), E("TrophyFenring", 2), E("TrophySGolem", 1), E("TrophyUlv", 2) },
            new[] { E("TrophyGoblin", 3), E("TrophyGoblinBrute", 1), E("TrophyGoblinShaman", 1), E("TrophyDeathsquito", 3), E("TrophyLox", 2), E("TrophyGrowth", 2) },
            new[] { E("TrophySeeker", 3), E("TrophySeekerBrute", 1), E("TrophyTick", 3), E("TrophyGjall", 1), E("TrophyHare", 3) },
            new[] { E("TrophyCharredMelee", 3), E("TrophyCharredArcher", 2), E("TrophyCharredMage", 2), E("TrophyAsksvin", 3), E("TrophyMorgen", 1), E("TrophyVolture", 3), E("TrophyFallenValkyrie", 1) },
        };

        private static readonly Entry[][] Gather =
        {
            new[] { E("Wood", 40), E("Stone", 30), E("Flint", 15), E("Resin", 15), E("LeatherScraps", 15), E("DeerHide", 8), E("Raspberry", 20), E("Mushroom", 15) },
            new[] { E("CopperOre", 15), E("TinOre", 15), E("GreydwarfEye", 15), E("BoneFragments", 15), E("Thistle", 10), E("Blueberries", 20), E("SurtlingCore", 4), E("RoundLog", 20) },
            new[] { E("IronScrap", 20), E("Guck", 10), E("Ooze", 10), E("WitheredBone", 4), E("Bloodbag", 10), E("Entrails", 10), E("ElderBark", 20), E("FineWood", 20) },
            new[] { E("SilverOre", 20), E("Obsidian", 20), E("WolfPelt", 8), E("FreezeGland", 8), E("Crystal", 8), E("WolfFang", 8) },
            new[] { E("BlackMetalScrap", 20), E("Barley", 30), E("Flax", 30), E("Needle", 15), E("LoxPelt", 6), E("Tar", 15), E("Cloudberry", 20) },
            new[] { E("Softtissue", 20), E("Carapace", 20), E("YggdrasilWood", 30), E("BlackMarble", 20), E("Sap", 15), E("MushroomMagecap", 10), E("MushroomJotunPuffs", 10), E("RoyalJelly", 10) },
            new[] { E("FlametalOreNew", 20), E("CharcoalResin", 20), E("Blackwood", 30), E("AskHide", 6), E("CelestialFeather", 3), E("Grausten", 30) },
        };

        private static readonly Entry[][] Provisions =
        {
            new[] { E("CookedMeat", 8), E("CookedDeerMeat", 6), E("NeckTailGrilled", 8) },
            new[] { E("QueensJam", 6), E("DeerStew", 5), E("CarrotSoup", 5), E("MinceMeatSauce", 5), E("BoarJerky", 6) },
            new[] { E("Sausages", 6), E("TurnipStew", 5), E("BlackSoup", 5), E("ShocklateSmoothie", 4) },
            new[] { E("OnionSoup", 5), E("Eyescream", 5), E("CookedWolfMeat", 8), E("WolfMeatSkewer", 5), E("WolfJerky", 6) },
            new[] { E("Bread", 5), E("LoxPie", 4), E("BloodPudding", 5), E("FishWraps", 5), E("CookedLoxMeat", 8) },
            new[] { E("MisthareSupreme", 4), E("Salad", 5), E("MushroomOmelette", 5), E("MeatPlatter", 4), E("HoneyGlazedChicken", 4), E("CookedHareMeat", 8) },
            new[] { E("ScorchingMedley", 4), E("PiquantPie", 4), E("SizzlingBerryBroth", 4), E("MashedMeat", 4), E("CookedVoltureMeat", 8), E("CookedAsksvinMeat", 8) },
        };

        /// <summary>The materials each stage pays in, one of them picked per notice (an amount from a range).</summary>
        private static readonly (string prefab, int min, int max)[][] Pay =
        {
            new[] { ("Resin", 6, 10), ("Flint", 5, 8), ("LeatherScraps", 5, 8) },
            new[] { ("Copper", 3, 5), ("Tin", 2, 4), ("Bronze", 2, 4) },
            new[] { ("Iron", 3, 6) },
            new[] { ("Silver", 3, 6) },
            new[] { ("BlackMetal", 3, 6) },
            new[] { ("Eitr", 2, 4), ("BlackMarble", 5, 10), ("Carapace", 4, 8) },
            new[] { ("FlametalNew", 3, 5) },
        };

        private static readonly int[] Coins = { 10, 20, 35, 50, 70, 90, 110, 130 };

        /// <summary>How far the world has come: 0 before Eikthyr, 1 after him, ... 7 after Fader.</summary>
        public static int WorldTier()
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) return 0;
            int tier = 0;
            while (tier < BossKeys.Length && zs.GetGlobalKey(BossKeys[tier])) tier++;
            return tier;
        }

        /// <summary>
        /// A new notice for this world, or null when nothing fits. Mostly for the newest land opened, now and then the one before it;
        /// the kind is chosen among those with anything this game knows at that stage.
        /// </summary>
        public static Contract Make(System.Random rnd)
        {
            int newest = WorldTier();
            int tier = newest > 0 && rnd.NextDouble() < 0.3 ? newest - 1 : newest;
            int list = Math.Min(tier, Kill.Length - 1);

            var kinds = new List<(Kind kind, Entry[] entries)>();
            void Offer(Kind kind, Entry[][] table)
            {
                Entry[] known = table[list].Where(e => Known(e.Prefab)).ToArray();
                if (known.Length > 0) kinds.Add((kind, known));
            }
            Offer(Kind.Kill, Kill);
            Offer(Kind.Kill, Kill);   // (hunting comes up twice as often as each of the others)
            Offer(Kind.Gather, Gather);
            Offer(Kind.Trophy, Trophy);
            Offer(Kind.Provisions, Provisions);
            if (kinds.Count == 0) return null;

            var (k, entries) = kinds[rnd.Next(kinds.Count)];
            Entry e = entries[rnd.Next(entries.Length)];
            int count = Math.Max(1, (int)Math.Round(e.Count * (0.8 + rnd.NextDouble() * 0.4)));

            return new Contract
            {
                Kind = k, Target = e.Prefab, Count = count, Tier = tier,
                Reward = MakeReward(rnd, tier, list),
                Id = ((uint)rnd.Next()).ToString("x8") + ((uint)rnd.Next()).ToString("x8"),
            };
        }

        private static string MakeReward(System.Random rnd, int tier, int list)
        {
            float percent = Plugin.RewardPercent.Value / 100f;
            var parts = new List<string>();
            int coins = Mathf.Max(1, Mathf.RoundToInt(Coins[Math.Min(tier, Coins.Length - 1)] * (0.85f + (float)rnd.NextDouble() * 0.3f) * percent));
            parts.Add("Coins:" + coins.ToString(CultureInfo.InvariantCulture));

            var pay = Pay[list].Where(p => Known(p.prefab)).ToArray();
            if (pay.Length > 0)
            {
                var (prefab, min, max) = pay[rnd.Next(pay.Length)];
                int n = Mathf.Max(1, Mathf.RoundToInt(rnd.Next(min, max + 1) * percent));
                parts.Add(prefab + ":" + n.ToString(CultureInfo.InvariantCulture));
            }

            // now and then one of the Forge of Potential's idols, of this stage's tier
            if (rnd.NextDouble() * 100.0 < Plugin.IdolChance.Value)
            {
                string idol = $"Upgrader{Math.Min(tier, 7)}{(rnd.Next(2) == 0 ? "Weapon" : "Armor")}";
                if (Known(idol)) parts.Add(idol + ":1");
            }
            return string.Join(",", parts);
        }

        /// <summary>Whether this game has the prefab (an item or a creature).</summary>
        public static bool Known(string prefab) => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null;

        /// <summary>Every prefab the lists name that this game doesn't have (a game update renamed it), for `wanted names`.</summary>
        public static List<string> Unknown()
        {
            var all = new[] { Kill, Trophy, Gather, Provisions }.SelectMany(t => t).SelectMany(r => r).Select(e => e.Prefab)
                .Concat(Pay.SelectMany(r => r).Select(p => p.prefab));
            return all.Distinct().Where(p => !Known(p)).ToList();
        }

        /// <summary>A creature's or item's name as the game shows it, in the player's language (the prefab name if it has none).</summary>
        public static string DisplayName(string prefab)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            string key = null;
            if (go != null)
            {
                Character c = go.GetComponent<Character>();
                ItemDrop i = go.GetComponent<ItemDrop>();
                key = c != null ? c.m_name : i != null ? i.m_itemData.m_shared.m_name : null;
            }
            if (string.IsNullOrEmpty(key)) return prefab;
            return Localization.instance != null ? Localization.instance.Localize(key) : key;
        }

        /// <summary>The words shown on a notice: its line, its reward, and for a hint what kind it is.</summary>
        public static string Describe(Contract c)
        {
            var sb = new StringBuilder();
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(Color.Lerp(Paper.Ink(c.Kind), Color.white, 0.35f))).Append(">")
              .Append(T("wanted_kind_" + c.Kind.ToString().ToLowerInvariant())).Append("</color>\n");
            sb.Append(c.Line()).Append('\n');
            sb.Append(T("wanted_reward", c.RewardText()));
            return sb.ToString();
        }
    }
}
