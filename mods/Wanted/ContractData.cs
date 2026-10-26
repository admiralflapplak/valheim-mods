using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Wanted
{
    /// <summary>One thing a notice can ask for: its kind, the stage it belongs to, the prefab, a usual amount, and what one is worth.</summary>
    internal sealed class Entry
    {
        public Kind Kind;
        public int Tier;
        public string Prefab;
        public int Count;      // a usual amount: a notice asks for it, give or take a fifth
        public float Value;    // what one is worth, in coins (a kill's danger and time, an item's trouble to get or make)

        public Entry(Kind kind, int tier, string prefab, int count, float value) { Kind = kind; Tier = tier; Prefab = prefab; Count = count; Value = value; }
    }

    /// <summary>A bonus a notice can pay on top: a rare find or a refined good, at a stage, and what one is worth in coins.</summary>
    internal sealed class BonusEntry
    {
        public string Group;   // "Rare" or "Refined"
        public int Tier;
        public string Prefab;
        public float Value;

        public BonusEntry(string group, int tier, string prefab, float value) { Group = group; Tier = tier; Prefab = prefab; Value = value; }
    }

    /// <summary>A material a stage's notices pay in, and what one is worth in coins (so a reward of a given worth buys the right amount).</summary>
    internal sealed class PayEntry
    {
        public int Tier;
        public string Prefab;
        public float Value;

        public PayEntry(int tier, string prefab, float value) { Tier = tier; Prefab = prefab; Value = value; }
    }

    /// <summary>
    /// What notices ask for and pay in, by stage (0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountain, 4 Plains, 5 Mistlands, 6 Ashlands;
    /// 7 Deep North uses 6's until it has its own). The built-in lists below, plus a file a server owner can add to:
    /// BepInEx/config/nick.valheim.wanted.contracts.json ("add" entries, "remove" prefab names), made with an example the first time.
    /// Other mods' creatures and items go in there; anything this game doesn't have is skipped (`wanted names` lists them), so the
    /// built-in lists name other mods' things too (Weapon Oils' oils), which only come up when that mod is installed. Discovery adds
    /// what neither names (other mods' creatures, things, food and metals, found in the game). In multiplayer the server's file applies
    /// to everyone (ServerSettings' server data).
    /// </summary>
    internal static class ContractData
    {
        public const string SyncKey = "contracts";
        public static string FilePath => Path.Combine(Paths.ConfigPath, Plugin.Guid + ".contracts.json");

        public static List<Entry> Entries = new List<Entry>(Defaults());       // the built-in lists with the file's changes
        public static List<PayEntry> Pay = new List<PayEntry>(DefaultPay());
        public static List<BonusEntry> Bonus = new List<BonusEntry>(DefaultBonus());
        public static HashSet<string> Removed = new HashSet<string>();         // the file's "remove": out of found ones too

        /// <summary>Everything a notice can ask for: the lists, and what Discovery found that they don't name.</summary>
        public static IEnumerable<Entry> AllEntries()
        {
            Discovery.Ensure();
            return Entries.Concat(Discovery.Found.Where(f => !Removed.Contains(f.Prefab)));
        }

        /// <summary>Everything a notice can pay in: the lists, and the metals Discovery found.</summary>
        public static IEnumerable<PayEntry> AllPay()
        {
            Discovery.Ensure();
            return Pay.Concat(Discovery.FoundPay.Where(f => !Removed.Contains(f.Prefab)));
        }

        // ---- the built-in lists --------------------------------------------------------------------------------------
        // Values are coins for one: a kill by how dangerous and slow the creature is; a thing by what it takes to get or make (an ore
        // is worth its bar, so an ore delivery always pays back more metal than it takes, once the coins are counted). The
        // reward for a notice is its total worth times a premium for the kind (Contracts.MakeReward), so these set the pay.

        private static IEnumerable<Entry> Defaults()
        {
            Entry K(int t, string p, int n, float v) => new Entry(Kind.Kill, t, p, n, v);
            Entry T(int t, string p, int n, float v) => new Entry(Kind.Trophy, t, p, n, v);
            Entry G(int t, string p, int n, float v) => new Entry(Kind.Gather, t, p, n, v);
            Entry P(int t, string p, int n, float v) => new Entry(Kind.Provisions, t, p, n, v);
            return new[]
            {
                // 0 Meadows
                K(0, "Boar", 6, 3), K(0, "Deer", 5, 3), K(0, "Neck", 6, 2), K(0, "Greyling", 8, 2),
                T(0, "TrophyBoar", 3, 10), T(0, "TrophyDeer", 3, 8), T(0, "TrophyNeck", 3, 8),
                G(0, "Wood", 40, 0.5f), G(0, "Stone", 30, 0.5f), G(0, "Flint", 15, 1), G(0, "Resin", 15, 1), G(0, "LeatherScraps", 15, 1),
                G(0, "DeerHide", 8, 2), G(0, "Raspberry", 20, 0.5f), G(0, "Mushroom", 15, 1), G(0, "Feathers", 10, 1), G(0, "Honey", 6, 2),
                P(0, "CookedMeat", 8, 3), P(0, "CookedDeerMeat", 6, 3), P(0, "NeckTailGrilled", 8, 2),
                // 1 Black Forest
                K(1, "Greydwarf", 8, 3), K(1, "Greydwarf_Elite", 3, 10), K(1, "Greydwarf_Shaman", 3, 8), K(1, "Skeleton", 6, 4), K(1, "Troll", 1, 40),
                T(1, "TrophyGreydwarf", 5, 8), T(1, "TrophyGreydwarfBrute", 2, 25), T(1, "TrophyGreydwarfShaman", 2, 25), T(1, "TrophySkeleton", 3, 20),
                T(1, "TrophyFrostTroll", 1, 60),
                G(1, "CopperOre", 15, 5), G(1, "TinOre", 15, 5), G(1, "GreydwarfEye", 15, 1), G(1, "BoneFragments", 15, 1), G(1, "Thistle", 10, 2),
                G(1, "Blueberries", 20, 1), G(1, "SurtlingCore", 4, 8), G(1, "RoundLog", 20, 2), G(1, "TrollHide", 4, 8),
                P(1, "QueensJam", 6, 6), P(1, "DeerStew", 5, 8), P(1, "CarrotSoup", 5, 6), P(1, "MinceMeatSauce", 5, 8), P(1, "BoarJerky", 6, 6),
                P(1, "MeadHealthMinor", 4, 10), P(1, "MeadStaminaMinor", 4, 10), P(1, "MeadTasty", 4, 8),
                // 2 Swamp
                K(2, "Draugr", 6, 8), K(2, "Draugr_Elite", 3, 20), K(2, "Blob", 6, 6), K(2, "Leech", 5, 6), K(2, "Surtling", 5, 8), K(2, "Wraith", 2, 25),
                K(2, "Abomination", 1, 60),
                T(2, "TrophyDraugr", 3, 30), T(2, "TrophyDraugrElite", 2, 40), T(2, "TrophyBlob", 3, 20), T(2, "TrophyLeech", 3, 20),
                T(2, "TrophySurtling", 3, 20), T(2, "TrophyWraith", 1, 60), T(2, "TrophyAbomination", 1, 100),
                G(2, "IronScrap", 20, 14), G(2, "Guck", 10, 3), G(2, "Ooze", 10, 2), G(2, "WitheredBone", 4, 10), G(2, "Bloodbag", 10, 3),
                G(2, "Entrails", 10, 2), G(2, "ElderBark", 20, 2), G(2, "FineWood", 20, 1.5f), G(2, "Chain", 2, 15),
                P(2, "Sausages", 6, 8), P(2, "TurnipStew", 5, 8), P(2, "BlackSoup", 5, 8), P(2, "ShocklateSmoothie", 4, 10),
                P(2, "MeadPoisonResist", 4, 15), P(2, "MeadHealthMedium", 4, 15), P(2, "MeadStaminaMedium", 4, 15),
                // 3 Mountain
                K(3, "Wolf", 5, 12), K(3, "Hatchling", 4, 15), K(3, "Fenring", 2, 30), K(3, "StoneGolem", 1, 80), K(3, "Ulv", 3, 20), K(3, "Bat", 6, 5),
                T(3, "TrophyWolf", 3, 30), T(3, "TrophyHatchling", 3, 30), T(3, "TrophyFenring", 2, 60), T(3, "TrophySGolem", 1, 120), T(3, "TrophyUlv", 2, 50),
                G(3, "SilverOre", 20, 18), G(3, "Obsidian", 20, 3), G(3, "WolfPelt", 8, 5), G(3, "FreezeGland", 8, 5), G(3, "Crystal", 8, 6), G(3, "WolfFang", 8, 4),
                P(3, "OnionSoup", 5, 12), P(3, "Eyescream", 5, 12), P(3, "CookedWolfMeat", 8, 6), P(3, "WolfMeatSkewer", 5, 12), P(3, "WolfJerky", 6, 10),
                P(3, "MeadFrostResist", 4, 20),
                // 4 Plains
                K(4, "Goblin", 6, 15), K(4, "GoblinBrute", 2, 50), K(4, "GoblinShaman", 2, 30), K(4, "Deathsquito", 4, 15), K(4, "Lox", 2, 40), K(4, "BlobTar", 4, 15),
                T(4, "TrophyGoblin", 3, 40), T(4, "TrophyGoblinBrute", 1, 80), T(4, "TrophyGoblinShaman", 1, 60), T(4, "TrophyDeathsquito", 3, 40),
                T(4, "TrophyLox", 2, 60), T(4, "TrophyGrowth", 2, 40),
                G(4, "BlackMetalScrap", 20, 22), G(4, "Barley", 30, 2), G(4, "Flax", 30, 2), G(4, "Needle", 15, 4), G(4, "LoxPelt", 6, 10), G(4, "Tar", 15, 4),
                G(4, "Cloudberry", 20, 2),
                P(4, "Bread", 5, 15), P(4, "LoxPie", 4, 25), P(4, "BloodPudding", 5, 15), P(4, "FishWraps", 5, 20), P(4, "CookedLoxMeat", 8, 10),
                P(4, "BarleyWine", 4, 25),
                // 5 Mistlands
                K(5, "Seeker", 6, 25), K(5, "SeekerBrute", 1, 100), K(5, "Tick", 6, 10), K(5, "Gjall", 1, 120), K(5, "Hare", 6, 8),
                T(5, "TrophySeeker", 3, 50), T(5, "TrophySeekerBrute", 1, 150), T(5, "TrophyTick", 3, 30), T(5, "TrophyGjall", 1, 200), T(5, "TrophyHare", 3, 25),
                G(5, "Softtissue", 20, 6), G(5, "Carapace", 20, 8), G(5, "YggdrasilWood", 30, 3), G(5, "BlackMarble", 20, 3), G(5, "Sap", 15, 4),
                G(5, "MushroomMagecap", 10, 4), G(5, "MushroomJotunPuffs", 10, 4), G(5, "RoyalJelly", 10, 4),
                P(5, "MisthareSupreme", 4, 30), P(5, "Salad", 5, 20), P(5, "MushroomOmelette", 5, 20), P(5, "MeatPlatter", 4, 30),
                P(5, "HoneyGlazedChicken", 4, 30), P(5, "CookedHareMeat", 8, 10), P(5, "MeadEitrMinor", 4, 30), P(5, "MeadStaminaLingering", 4, 30),
                P(5, "MeadHealthLingering", 4, 30),
                // 6 Ashlands
                K(6, "Charred_Melee", 6, 30), K(6, "Charred_Archer", 4, 30), K(6, "Charred_Mage", 2, 50), K(6, "Asksvin", 4, 30), K(6, "Morgen", 1, 150),
                K(6, "Volture", 4, 25), K(6, "FallenValkyrie", 1, 150), K(6, "BlobLava", 4, 20),
                T(6, "TrophyCharredMelee", 3, 60), T(6, "TrophyCharredArcher", 2, 60), T(6, "TrophyCharredMage", 2, 80), T(6, "TrophyAsksvin", 3, 50),
                T(6, "TrophyMorgen", 1, 250), T(6, "TrophyVolture", 3, 50), T(6, "TrophyFallenValkyrie", 1, 250),
                G(6, "FlametalOreNew", 20, 30), G(6, "CharcoalResin", 20, 4), G(6, "Blackwood", 30, 3), G(6, "AskHide", 6, 10),
                G(6, "CelestialFeather", 3, 25), G(6, "Grausten", 30, 1),
                P(6, "ScorchingMedley", 4, 35), P(6, "PiquantPie", 4, 35), P(6, "SizzlingBerryBroth", 4, 35), P(6, "MashedMeat", 4, 35),
                P(6, "CookedVoltureMeat", 8, 12), P(6, "CookedAsksvinMeat", 8, 12),

                // Weapon Oils (only when it's installed): the finished oils, at the stage their cauldron recipe opens
                P(1, "nick_oil_ember", 3, 8), P(1, "nick_oil_keen", 3, 8), P(1, "nick_oil_trollfat", 3, 10), P(1, "nick_oil_barkhide", 3, 10),
                P(2, "nick_oil_venom", 3, 14), P(2, "nick_oil_leech", 3, 14), P(3, "nick_oil_rime", 3, 18),
                P(4, "nick_oil_storm", 3, 24), P(4, "nick_oil_loxfat", 3, 24), P(5, "nick_oil_wisp", 3, 32), P(5, "nick_oil_royal", 3, 32),
                P(6, "nick_oil_cinder", 3, 40),
            };
        }

        private static IEnumerable<PayEntry> DefaultPay() => new[]
        {
            new PayEntry(0, "Resin", 1), new PayEntry(0, "Flint", 1), new PayEntry(0, "LeatherScraps", 1), new PayEntry(0, "DeerHide", 2),
            new PayEntry(1, "Copper", 5), new PayEntry(1, "Tin", 5), new PayEntry(1, "Bronze", 12),
            new PayEntry(2, "Iron", 14),
            new PayEntry(3, "Silver", 18),
            new PayEntry(4, "BlackMetal", 22),
            new PayEntry(5, "BlackMarble", 3), new PayEntry(5, "Carapace", 8),   // (Eitr is a refined bonus: DefaultBonus)
            new PayEntry(6, "FlametalNew", 30),
        };

        /// <summary>
        /// Rare finds (boss summoning items and things that are hard to come by) and refined goods (what takes a station and time to make),
        /// paid now and then on top of a notice's reward. Meads, oils, arrows and valuables are found in the game instead (Contracts.Bonus).
        /// </summary>
        private static IEnumerable<BonusEntry> DefaultBonus()
        {
            BonusEntry R(int t, string p, float v) => new BonusEntry("Rare", t, p, v);
            BonusEntry F(int t, string p, float v) => new BonusEntry("Refined", t, p, v);
            return new[]
            {
                R(1, "AncientSeed", 40), R(1, "SurtlingCore", 15),
                R(2, "WitheredBone", 40), R(2, "Chain", 25),
                R(3, "DragonTear", 80),
                R(4, "GoblinTotem", 80), R(4, "YmirRemains", 60), R(4, "Thunderstone", 60),
                R(5, "DvergrKeyFragment", 100), R(5, "BlackCore", 80),
                R(6, "BellFragment", 150), R(6, "MoltenCore", 80),
                F(1, "BronzeNails", 1.2f), F(2, "IronNails", 1.4f), F(4, "LinenThread", 3), F(5, "Eitr", 25),
            };
        }

        /// <summary>
        /// The stage the game's own valuables belong to (where they're found: chests and locations, which Discovery can't read). Other
        /// mods' valuables go by where Discovery finds them, or failing that by their price.
        /// </summary>
        public static readonly Dictionary<string, int> ValuableTiers = new Dictionary<string, int>
        {
            { "Amber", 0 }, { "AmberPearl", 0 }, { "Ruby", 1 }, { "SilverNecklace", 2 },
            { "AncientCoin", 7 }, { "AncientGemstoneGreen", 7 }, { "AncientGemstoneOrange", 7 }, { "AncientGemstonePurple", 7 }, { "AncientGemstoneBlack", 7 },
        };

        // ---- the file ---------------------------------------------------------------------------------------------------

        /// <summary>The file's text (the example, written the first time), for ServerSettings to send or use.</summary>
        public static string OwnText()
        {
            try
            {
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Example);
                return File.ReadAllText(FilePath);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not read {FilePath}: {e.Message}"); return ""; }
        }

        /// <summary>The lists: the built-in ones, with the file's additions and removals (the server's file on a server that has the mod).</summary>
        public static void Use(string text)
        {
            var entries = new List<Entry>(Defaults());
            var pay = new List<PayEntry>(DefaultPay());
            var removedAll = new HashSet<string>();
            var bonus = new List<BonusEntry>(DefaultBonus());
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    JObject root = JObject.Parse(text);
                    var removed = new HashSet<string>((root["remove"] as JArray)?.Select(t => (string)t).Where(s => s != null) ?? Enumerable.Empty<string>());
                    removedAll = removed;
                    entries.RemoveAll(e => removed.Contains(e.Prefab));
                    pay.RemoveAll(p => removed.Contains(p.Prefab));
                    bonus.RemoveAll(b => removed.Contains(b.Prefab));
                    foreach (JToken t in (root["add"] as JArray) ?? new JArray())
                    {
                        string kind = (string)t["kind"], prefab = (string)t["prefab"];
                        int tier = (int?)t["tier"] ?? 0;
                        if (string.IsNullOrEmpty(prefab) || tier < 0 || tier >= Contracts.Tiers) continue;
                        if (string.Equals(kind, "rare", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "refined", StringComparison.OrdinalIgnoreCase))
                            bonus.Add(new BonusEntry(char.ToUpperInvariant(kind[0]) + kind.Substring(1).ToLowerInvariant(), tier, prefab, (float?)t["value"] ?? 1f));
                        else if (string.Equals(kind, "pay", StringComparison.OrdinalIgnoreCase))
                            pay.Add(new PayEntry(tier, prefab, (float?)t["value"] ?? 1f));
                        else if (Enum.TryParse(kind, true, out Kind k))
                            entries.Add(new Entry(k, tier, prefab, Math.Max(1, (int?)t["count"] ?? 1), (float?)t["value"] ?? 1f));
                        else Plugin.Log.LogWarning($"Contracts file: \"{kind}\" isn't a kind (Kill, Gather, Trophy, Provisions, Pay, Rare or Refined)");
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"The contracts file has a mistake ({e.Message}): using the built-in lists only"); }
            Entries = entries;
            Pay = pay;
            Removed = removedAll;
            Bonus = bonus;
            Discovery.Forget();   // (found again against the new lists at the next posting)
        }

        private const string Example =
@"{
  ""_help"": [
    ""Add things Wanted notices can ask for, or pay in, and take built-in ones out. Kinds: Kill (a creature prefab), Gather, Trophy,"",
    ""Provisions (item prefabs), Pay (an item notices of that stage pay in), Rare and Refined (bonuses now and then paid on top)."",
    ""Other mods' creatures, items, food, meads, arrows and valuables are found by themselves: this is for fine-tuning. Tier: 0 Meadows, 1 Black Forest, 2 Swamp,"",
    ""3 Mountain, 4 Plains, 5 Mistlands, 6 Ashlands, 7 Deep North. Count: a usual amount (a notice asks for it, give or take a fifth)."",
    ""Value: what one is worth in coins; the reward is the notice's worth times a premium for its kind. Remove: prefab names to drop"",
    ""from the built-in lists. Names this game doesn't have are skipped (Claude Tools: wanted names). On a server, the server's file"",
    ""applies to everyone. Changes apply to notices posted afterwards.""
  ],
  ""add"": [
    { ""kind"": ""Provisions"", ""tier"": 1, ""prefab"": ""ExampleModStew"", ""count"": 4, ""value"": 10 }
  ],
  ""remove"": []
}
";
    }
}
