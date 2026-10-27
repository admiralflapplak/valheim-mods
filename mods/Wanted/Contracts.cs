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
        public string Line()
        {
            switch (Kind)
            {
                case Kind.Kill: return T("wanted_line_kill", TargetName(), Count);
                case Kind.Gather: return T("wanted_line_gather", TargetName(), Count);
                case Kind.Trophy: return T("wanted_line_trophy", TargetName(), Count);
                default: return T("wanted_line_provisions", TargetName(), Count);
            }
        }

        /// <summary>"Bounty", "Gathering"...: the kind's name.</summary>
        public string KindName()
        {
            switch (Kind)
            {
                case Kind.Kill: return T("wanted_kind_kill");
                case Kind.Gather: return T("wanted_kind_gather");
                case Kind.Trophy: return T("wanted_kind_trophy");
                default: return T("wanted_kind_provisions");
            }
        }

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
        /// A new notice for this world, or null when nothing fits. For any land opened so far, the newest most often (PickLand); the kind
        /// is chosen among those with anything this game knows at that stage (hunting twice as often as each of the others).
        /// </summary>
        public static Contract Make(System.Random rnd) => Make(rnd, -1);

        /// <summary>A new notice, written for this stage (-1: for the world as it is, mostly its newest land).</summary>
        public static Contract Make(System.Random rnd, int forTier)
        {
            int newest = WorldTier();
            int tier = forTier >= 0 ? forTier : PickLand(rnd, newest);
            List<Entry> all = ContractData.AllEntries().ToList();
            int list = ListTier(tier, all.Select(e => e.Tier));

            var kinds = new List<List<Entry>>();
            foreach (Kind k in new[] { Kind.Kill, Kind.Kill, Kind.Gather, Kind.Trophy, Kind.Provisions })
            {
                var known = all.Where(e => e.Kind == k && e.Tier == list && Known(e.Prefab)).ToList();
                if (known.Count > 0) kinds.Add(known);
            }
            if (kinds.Count == 0) return null;

            List<Entry> entries = kinds[rnd.Next(kinds.Count)];
            Entry e = entries[rnd.Next(entries.Count)];
            int count = Amount(rnd, e.Count);

            return new Contract
            {
                Kind = e.Kind, Target = e.Prefab, Count = count, Tier = tier,
                Reward = MakeReward(rnd, tier, count * e.Value * Premium(e.Kind)),
                Id = ((uint)rnd.Next()).ToString("x8") + ((uint)rnd.Next()).ToString("x8"),
                Pin = PickPin(rnd),
            };
        }

        /// <summary>
        /// Which opened land a notice is for: the newest with the server's share (50%), each one before it with that share of what's left
        /// (25%, 12.5%...), the oldest taking the rest. Older lands keep turning up late in the game, with their materials and their idols.
        /// </summary>
        private static int PickLand(System.Random rnd, int newest)
        {
            double share = Mathf.Clamp(Plugin.NewestLandShare.Value, 1f, 100f) / 100.0;
            for (int tier = newest; tier > 0; tier--)
                if (rnd.NextDouble() < share) return tier;
            return 0;
        }

        /// <summary>
        /// How many a notice asks for: any whole number from about 60% to 150% of the usual amount, each as likely (a usual 3 is 1 to 5, a
        /// usual 1 is 1 or 2), so two notices for the same thing rarely ask for the same.
        /// </summary>
        private static int Amount(System.Random rnd, int usual)
        {
            int least = Math.Max(1, (int)Math.Floor(usual * 0.6)), most = Math.Max(least + 1, (int)Math.Ceiling(usual * 1.5));
            return rnd.Next(least, most + 1);
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

            List<PayEntry> allPay = ContractData.AllPay().ToList();
            int payTier = ListTier(tier, allPay.Select(p => p.Tier));
            var pay = allPay.Where(p => p.Tier == payTier && Known(p.Prefab)).ToList();
            if (pay.Count > 0)
            {
                PayEntry m = pay[rnd.Next(pay.Count)];
                int n = Math.Max(1, (int)Math.Round(worth * (1f - CoinShare) / Math.Max(0.1f, m.Value)));
                parts.Add(m.Prefab + ":" + n.ToString(CultureInfo.InvariantCulture));
            }

            string bonus = Bonus(rnd, tier, worth * BonusShare);
            if (bonus != null) parts.Add(bonus);

            // now and then one of the Forge of Potential's idols: of any land opened so far, evenly (not the notice's own land: notices for
            // old lands are rare late in the game, and their idols would all but vanish with them)
            if (rnd.NextDouble() * 100.0 < Plugin.IdolChance.Value)
            {
                int idolTier = rnd.Next(0, Math.Max(tier, WorldTier()) + 1);
                string idol = $"Upgrader{Math.Min(idolTier, 7)}{(rnd.Next(2) == 0 ? "Weapon" : "Armor")}";
                if (Known(idol)) parts.Add(idol + ":1");
            }
            return string.Join(",", parts);
        }

        public const float BonusShare = 0.5f;   // a bonus is worth about half the reward it comes with (on top of it)

        /// <summary>
        /// Now and then something more on top of the pay ("prefab:amount"), or null: a rare find (a boss's summoning item, something hard
        /// to come by), a valuable (amber, a ruby: worth its price at the trader), or something useful of the stage: a mead or an oil
        /// (the brews the provisions lists know, other mods' included), arrows or bolts (found by their recipes), or a refined good
        /// (Eitr, linen thread, nails). One draw: rare first, then valuable, then useful, each with its server chance.
        /// </summary>
        private static string Bonus(System.Random rnd, int tier, float worth)
        {
            double roll = rnd.NextDouble() * 100.0;
            float rare = Plugin.RareChance.Value, valuable = Plugin.ValuableChance.Value, useful = Plugin.UsefulChance.Value;
            if (roll < rare) return Pick(rnd, Nearest(ContractData.Bonus.Where(b => b.Group == "Rare" && !ContractData.Removed.Contains(b.Prefab))
                .Select(b => (b.Tier, b.Prefab, b.Value)), tier), worth, 3);
            if (roll < rare + valuable)
            {
                // of a land opened by this notice's stage; one found nowhere in particular, if it's not worth more than the stage's sort
                var gems = Discovery.Valuables.Where(v => Known(v.Prefab) && (v.Tier >= 0 ? v.Tier <= tier : v.Value <= 25f * (tier + 1)))
                    .OrderBy(v => v.Value).ToList();
                var fits = gems.Where(v => v.Value <= worth).ToList();   // (one that a reward of this size can buy; else the cheapest)
                var pick = fits.Count > 0 ? fits[rnd.Next(fits.Count)] : gems.FirstOrDefault();
                return pick == null ? null : Amount(pick.Prefab, pick.Value, worth, 10);
            }
            if (roll < rare + valuable + useful)
            {
                var brews = ContractData.AllEntries().Where(e => e.Kind == Kind.Provisions && IsBrew(e.Prefab)).Select(e => (e.Tier, e.Prefab, e.Value));
                var arrows = Discovery.Ammo.Select(a => (a.Tier, a.Prefab, a.Value));
                var refined = ContractData.Bonus.Where(b => b.Group == "Refined" && !ContractData.Removed.Contains(b.Prefab)).Select(b => (b.Tier, b.Prefab, b.Value));
                var groups = new[] { Nearest(brews, tier), Nearest(arrows, tier), Nearest(refined, tier) }.Where(g => g.Count > 0).ToList();
                if (groups.Count == 0) return null;
                return Pick(rnd, groups[rnd.Next(groups.Count)], worth, 100);
            }
            return null;
        }

        /// <summary>The ones this game has, of this stage or the nearest below it that has any.</summary>
        private static List<(int tier, string prefab, float value)> Nearest(IEnumerable<(int tier, string prefab, float value)> all, int tier)
        {
            var known = all.Where(a => Known(a.prefab)).ToList();
            for (int t = tier; t >= 0; t--)
            {
                var here = known.Where(a => a.tier == t).ToList();
                if (here.Count > 0) return here;
            }
            return new List<(int, string, float)>();
        }

        private static string Pick(System.Random rnd, List<(int tier, string prefab, float value)> from, float worth, int most)
        {
            if (from.Count == 0) return null;
            var p = from[rnd.Next(from.Count)];
            return Amount(p.prefab, p.value, worth, most);
        }

        /// <summary>"prefab:n", as many as the worth buys (at least one; no more than <paramref name="most"/> or a stack).</summary>
        private static string Amount(string prefab, float value, float worth, int most)
        {
            int stack = ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_maxStackSize ?? 1;
            int n = Mathf.Clamp(Mathf.RoundToInt(worth / Mathf.Max(0.1f, value)), 1, Math.Max(1, Math.Min(most, stack)));
            return prefab + ":" + n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A drink or an oil rather than a meal: a consumable that doesn't feed.</summary>
        private static bool IsBrew(string prefab)
        {
            ItemDrop.ItemData.SharedData s = ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
            return s != null && s.m_food <= 0f && s.m_foodStamina <= 0f && s.m_foodEitr <= 0f;
        }

        /// <summary>Whether this game has the prefab (an item or a creature).</summary>
        public static bool Known(string prefab) => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null;

        /// <summary>Every prefab the lists name that this game doesn't have (a game update renamed it, a mod isn't installed), for `wanted names`.</summary>
        public static List<string> Unknown() =>
            ContractData.Entries.Select(e => e.Prefab).Concat(ContractData.Pay.Select(p => p.Prefab)).Concat(ContractData.Bonus.Select(b => b.Prefab))
                .Distinct().Where(p => !Known(p)).ToList();

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
              .Append(c.KindName()).Append("</color>\n");
            sb.Append(c.Line()).Append('\n');
            sb.Append(T("wanted_reward", c.RewardText()));
            return sb.ToString();
        }
    }
}
