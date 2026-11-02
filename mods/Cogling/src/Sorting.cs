using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Where an item goes (floor items now, tidying later: one rulebook). Of the chests he may use, in order:
    ///   1. the one already holding the most of that item;
    ///   2. one holding something of the same group (his groups: "5 - Sorting");
    ///   3. by name: trophies with trophies, seeds with seeds;
    ///   4. one holding the same type of item (the game's item types: materials, food, ammo...);
    ///   5. an empty one (a new kind starts its own pile), else the one with the most room.
    /// Only chests with room for the whole stack count. Items are known by their prefab name or their short name (the game's
    /// "$item_copperore" without "$item_"), in any case, so groups copied from Smart Containers work as they are.
    /// </summary>
    internal static class Sorting
    {
        // ---- the groups (settings), one per group, and more in one line

        private static readonly (string name, string items)[] Defaults =
        {
            ("Valuables", "Ruby, Coins, Amber, AmberPearl, SilverNecklace"),
            ("Ores", "CopperOre, TinOre, CopperScrap, IronScrap, IronOre, SilverOre, BlackMetalScrap, FlametalOre, FlametalOreNew"),
            ("Ingots", "Copper, Tin, Bronze, Iron, Silver, BlackMetal, Flametal, FlametalNew"),
            ("Wood", "Wood, FineWood, RoundLog, ElderBark, YggdrasilWood, Blackwood"),
            ("Stone", "Stone, Flint, Obsidian, Grausten, BlackMarble"),
            ("Mushrooms", "Mushroom, MushroomBlue, MushroomYellow, MushroomJotunPuffs, MushroomMagecap, MushroomSmokePuff"),
            ("Berries", "Raspberry, Blueberries, Cloudberry, Honey"),
            ("Vegetables", "Carrot, Turnip, Onion, Barley, Flax"),
            ("Raw meat", "RawMeat, DeerMeat, NeckTail, LoxMeat, SerpentMeat, HareMeat, ChickenMeat, BugMeat, WolfMeat, FishRaw, BoneMawSerpentMeat, VoltureMeat, AsksvinMeat"),
            ("Cooked meat", "CookedMeat, CookedDeerMeat, NeckTailGrilled, CookedLoxMeat, SerpentMeatCooked, CookedChickenMeat, CookedHareMeat, CookedWolfMeat, FishCooked, CookedBugMeat, CookedVoltureMeat, CookedAsksvinMeat"),
            ("Hides", "LeatherScraps, DeerHide, TrollHide, WolfPelt, LoxPelt, ScaleHide, BjornHide, AskHide"),
            ("Seeds", "CarrotSeeds, TurnipSeeds, OnionSeeds, BeechSeeds, PineCone, FirCone, BirchSeeds, Acorn, VineberrySeeds"),
        };

        private static readonly Dictionary<string, ConfigEntry<string>> GroupSettings = new Dictionary<string, ConfigEntry<string>>();
        internal static ConfigEntry<string> MoreGroups;
        internal static ConfigEntry<bool> CopySmartContainers;
        private static List<HashSet<string>> _groups;   // parsed (lowercase names), rebuilt when a setting changes

        internal static void Bind(ConfigFile config)
        {
            GroupSettings.Clear();
            foreach (var (name, items) in Defaults)
            {
                ConfigEntry<string> entry = config.Bind("5 - Sorting", name, items,
                    "A group: items kept together. When no chest holds an item yet, he puts it with others of its group. Item prefab names or "
                    + "short names (copperore), separated by commas; empty: no such group.");
                entry.SettingChanged += (s, e) => _groups = null;
                GroupSettings[name] = entry;
            }
            MoreGroups = config.Bind("5 - Sorting", "More groups", "",
                "Groups of your own, like: Fish = Fish1, Fish2, Fish3 | Potions = MeadHealthMinor, MeadStaminaMinor (groups separated by |).");
            MoreGroups.SettingChanged += (s, e) => _groups = null;
            CopySmartContainers = config.Bind("5 - Sorting", "Copy groups from Smart Containers", false,
                "Switch on to copy Smart Containers' item groups into these settings once (it switches itself off). Nothing of it is needed after.");
            CopySmartContainers.SettingChanged += (s, e) => { if (CopySmartContainers.Value) CopyFromSmartContainers(); };
            _groups = null;
        }

        private static List<HashSet<string>> Groups()
        {
            if (_groups != null) return _groups;
            var groups = new List<HashSet<string>>();
            HashSet<string> Parse(string list) => new HashSet<string>(list.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n.Length > 0));
            foreach (ConfigEntry<string> e in GroupSettings.Values) { var g = Parse(e.Value); if (g.Count > 0) groups.Add(g); }
            foreach (string part in (MoreGroups?.Value ?? "").Split('|'))
            {
                int eq = part.IndexOf('=');
                var g = Parse(eq >= 0 ? part.Substring(eq + 1) : part);
                if (g.Count > 0) groups.Add(g);
            }
            return _groups = groups;
        }

        /// <summary>Smart Containers' [ItemGroup] entries from its settings file: same names into ours, the rest into More groups.</summary>
        private static void CopyFromSmartContainers()
        {
            try
            {
                string file = Directory.GetFiles(Paths.ConfigPath, "*.cfg").FirstOrDefault(f => Path.GetFileName(f).IndexOf("SmartContainers", StringComparison.OrdinalIgnoreCase) >= 0);
                if (file == null) { Plugin.Log.LogWarning("Smart Containers' settings file isn't here: nothing copied"); return; }
                bool inGroups = false;
                var more = new List<string>();
                int copied = 0;
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[")) { inGroups = line == "[ItemGroup]"; continue; }
                    if (!inGroups || line.StartsWith("#") || !line.Contains("=")) continue;
                    string name = line.Substring(0, line.IndexOf('=')).Trim();
                    string items = line.Substring(line.IndexOf('=') + 1).Trim();
                    ConfigEntry<string> same = GroupSettings.FirstOrDefault(g => string.Equals(g.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
                    if (same != null) same.Value = items;
                    else more.Add($"{name} = {items}");
                    copied++;
                }
                if (more.Count > 0) MoreGroups.Value = string.Join(" | ", new[] { MoreGroups.Value }.Where(v => !string.IsNullOrWhiteSpace(v)).Concat(more));
                Plugin.Log.LogInfo($"Copied {copied} item group(s) from Smart Containers");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Copying Smart Containers' groups: " + e.Message); }
            finally { CopySmartContainers.Value = false; _groups = null; }
        }

        // ---- choosing

        internal static string Short(ItemDrop.ItemData item) => (item.m_shared.m_name ?? "").Replace("$item_", "").ToLowerInvariant();
        internal static string Prefab(ItemDrop.ItemData item) => (item.m_dropPrefab != null ? item.m_dropPrefab.name : "").ToLowerInvariant();

        private static bool InGroup(HashSet<string> group, ItemDrop.ItemData item) => group.Contains(Prefab(item)) || group.Contains(Short(item));

        private static bool SameKind(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            string pa = Prefab(a), pb = Prefab(b);
            if (pa.StartsWith("trophy") && pb.StartsWith("trophy")) return true;
            if (pa.EndsWith("seeds") && pb.EndsWith("seeds")) return true;
            return false;
        }

        /// <summary>How many other kinds of thing in an inventory are an item's relatives: of its group, or trophies / seeds with it.</summary>
        internal static int Kin(Inventory inv, ItemDrop.ItemData item)
        {
            var groups = Groups().Where(g => InGroup(g, item)).ToList();
            return inv.GetAllItems().Count(i => i.m_shared.m_name != item.m_shared.m_name && (SameKind(i, item) || groups.Any(g => InGroup(g, i))));
        }

        /// <summary>The chest an item should go into, of those given (each one he may use), and why; null when none has room.</summary>
        internal static Container Choose(ItemDrop.ItemData item, IEnumerable<Container> chests, Vector3 from, out string why)
        {
            var room = chests.Where(c => c != null && c.GetInventory() != null && c.GetInventory().CanAddItem(item)).ToList();
            why = "";
            string reasonFound = "";
            if (room.Count == 0) return null;
            string name = item.m_shared.m_name;

            Container Best(Func<Inventory, int> score, string reason)
            {
                Container best = null;
                int bestScore = 0;
                float bestDistance = float.MaxValue;
                foreach (Container c in room)
                {
                    int s = score(c.GetInventory());
                    if (s <= 0) continue;
                    float d = Vector3.Distance(c.transform.position, from);
                    if (s > bestScore || (s == bestScore && d < bestDistance)) { best = c; bestScore = s; bestDistance = d; }
                }
                if (best != null) reasonFound = reason;
                return best;
            }

            Container chosen = Best(inv => inv.CountItems(name), "it already holds that item");
            if (chosen == null)
                foreach (HashSet<string> group in Groups().Where(g => InGroup(g, item)))
                {
                    chosen = Best(inv => inv.GetAllItems().Count(i => InGroup(group, i)), "it holds things of the same group");
                    if (chosen != null) break;
                }
            if (chosen == null) chosen = Best(inv => inv.GetAllItems().Count(i => SameKind(i, item)), "trophies with trophies, seeds with seeds");
            if (chosen == null) chosen = Best(inv => inv.GetAllItems().Count(i => i.m_shared.m_itemType == item.m_shared.m_itemType), "it holds the same type of thing");
            if (chosen != null) { why = reasonFound; return chosen; }
            chosen = room.Where(c => c.GetInventory().NrOfItems() == 0).OrderBy(c => Vector3.Distance(c.transform.position, from)).FirstOrDefault();
            if (chosen != null) { why = "an empty chest: a new pile"; return chosen; }
            why = "the chest with the most room";
            return room.OrderByDescending(c => c.GetInventory().GetEmptySlots()).ThenBy(c => Vector3.Distance(c.transform.position, from)).First();
        }
    }
}
