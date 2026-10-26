using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>What holds a notice to the board. The number is saved: never renumber, only add.</summary>
    internal enum Pin { Nail = 0, Arrow = 1, Knife = 2 }

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
        public Pin Pin;          // what holds it to the board (looks only)

        // kind|target|count|tier|reward|id|pin: no field holds '|' or ';' (prefab names, numbers, the reward's ':' and ',').
        // (Notices from before 0.4 have no pin: a nail.)
        public string Write() =>
            string.Join("|", ((int)Kind).ToString(CultureInfo.InvariantCulture), Target, Count.ToString(CultureInfo.InvariantCulture),
                Tier.ToString(CultureInfo.InvariantCulture), Reward, Id, ((int)Pin).ToString(CultureInfo.InvariantCulture));

        public static Contract Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] f = text.Split('|');
            if (f.Length < 6) return null;
            if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)
                || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier)) return null;
            int pin = f.Length > 6 && int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0;
            return new Contract { Kind = (Kind)kind, Target = f[1], Count = count, Tier = tier, Reward = f[4], Id = f[5], Pin = (Pin)pin };
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
    /// The writing of new notices. The stage is how far the world has come: each boss beaten (the world's global keys) opens the next
    /// land's creatures, things and food (ContractData has the lists). A notice's reward is worked out from what it asks: the worth of
    /// what it asks (its count times each one's value) times a premium for its kind, paid partly in coins and partly in a material of its
    /// stage, and now and then an idol.
    /// </summary>
    internal static class Contracts
    {
        public const int Tiers = 8;

        /// <summary>The keys the game sets when each boss falls, in order: the stage is how many of them in a row are set.</summary>
        private static readonly string[] BossKeys =
            { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" };

        /// <summary>
        /// The premium on a notice's worth, by kind. A bounty pays twice what the kills are worth (the creatures' own drops are the
        /// hunter's too); a delivery half as much again as what's handed over (the things themselves are given up for it).
        /// </summary>
        public static float Premium(Kind kind) => kind == Kind.Kill ? 2f : 1.5f;

        public const float CoinShare = 0.4f;   // of a reward's worth, the part paid in coins; the rest in the stage's material

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
        /// the kind is chosen among those with anything this game knows at that stage (hunting twice as often as each of the others).
        /// </summary>
        public static Contract Make(System.Random rnd)
        {
            int newest = WorldTier();
            int tier = newest > 0 && rnd.NextDouble() < 0.3 ? newest - 1 : newest;
            int list = ListTier(tier, ContractData.Entries.Select(e => e.Tier));

            var kinds = new List<List<Entry>>();
            foreach (Kind k in new[] { Kind.Kill, Kind.Kill, Kind.Gather, Kind.Trophy, Kind.Provisions })
            {
                var known = ContractData.Entries.Where(e => e.Kind == k && e.Tier == list && Known(e.Prefab)).ToList();
                if (known.Count > 0) kinds.Add(known);
            }
            if (kinds.Count == 0) return null;

            List<Entry> entries = kinds[rnd.Next(kinds.Count)];
            Entry e = entries[rnd.Next(entries.Count)];
            int count = Math.Max(1, (int)Math.Round(e.Count * (0.8 + rnd.NextDouble() * 0.4)));

            return new Contract
            {
                Kind = e.Kind, Target = e.Prefab, Count = count, Tier = tier,
                Reward = MakeReward(rnd, tier, count * e.Value * Premium(e.Kind)),
                Id = ((uint)rnd.Next()).ToString("x8") + ((uint)rnd.Next()).ToString("x8"),
                Pin = PickPin(rnd),
            };
        }

        /// <summary>The stage whose list to use: this one, or the nearest below it that has anything (Deep North borrows Ashlands').</summary>
        private static int ListTier(int tier, IEnumerable<int> have)
        {
            var tiers = new HashSet<int>(have);
            while (tier > 0 && !tiers.Contains(tier)) tier--;
            return tier;
        }

        /// <summary>Most notices are nailed up; now and then one is pinned with an arrow or a knife driven through it.</summary>
        private static Pin PickPin(System.Random rnd)
        {
            double roll = rnd.NextDouble() * 100.0;
            if (roll < Plugin.ArrowChance.Value) return Pin.Arrow;
            if (roll < Plugin.ArrowChance.Value + Plugin.KnifeChance.Value) return Pin.Knife;
            return Pin.Nail;
        }

        /// <summary>A reward of this worth (coins): part in coins, the rest in one of the stage's materials, now and then an idol.</summary>
        private static string MakeReward(System.Random rnd, int tier, float worth)
        {
            worth *= Plugin.RewardPercent.Value / 100f;
            var parts = new List<string>();
            int coins = Math.Max(1, (int)Math.Round(worth * CoinShare));
            parts.Add("Coins:" + coins.ToString(CultureInfo.InvariantCulture));

            int payTier = ListTier(tier, ContractData.Pay.Select(p => p.Tier));
            var pay = ContractData.Pay.Where(p => p.Tier == payTier && Known(p.Prefab)).ToList();
            if (pay.Count > 0)
            {
                PayEntry m = pay[rnd.Next(pay.Count)];
                int n = Math.Max(1, (int)Math.Round(worth * (1f - CoinShare) / Math.Max(0.1f, m.Value)));
                parts.Add(m.Prefab + ":" + n.ToString(CultureInfo.InvariantCulture));
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

        /// <summary>Every prefab the lists name that this game doesn't have (a game update renamed it, a mod isn't installed), for `wanted names`.</summary>
        public static List<string> Unknown() =>
            ContractData.Entries.Select(e => e.Prefab).Concat(ContractData.Pay.Select(p => p.Prefab)).Distinct().Where(p => !Known(p)).ToList();

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
