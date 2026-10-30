using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Other mods' items for Dagny's stock, found as the game runs (as Wanted finds them): the items the game itself has are noted when
    /// ObjectDB wakes, before any mod adds to it; everything added on top is placed by where it comes from:
    ///   - gathered: picked, mined or chopped from the world's vegetation, at the earliest land it grows in;
    ///   - dropped by a creature in the world's spawn lists, at the earliest land it spawns in (bosses aside);
    ///   - a mead: fermented from a base whose ingredients are placed, at the latest of their lands.
    /// Materials go to goods, meads to meads, priced as that land's own (the middle of its built-in prices). Weapons, armour and the like are
    /// left out. Found once a world is loaded (a pass over the game's prefabs), again when the stock file changes.
    /// </summary>
    internal static class StockDiscovery
    {
        private static HashSet<string> _vanilla;
        private static readonly System.Reflection.FieldInfo LiveSpawners = AccessTools.Field(typeof(SpawnSystem), "m_instances");

        /// <summary>The game's own items, noted before any mod adds to the list (a hot reload mid-world misses it: see IsVanilla).</summary>
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(ObjectDB __instance)
            {
                if (_vanilla != null) return;
                _vanilla = new HashSet<string>(__instance.m_items.Where(i => i != null).Select(i => i.name));
            }
        }

        private static bool IsVanilla(GameObject item)
        {
            if (_vanilla != null) return _vanilla.Contains(item.name);
            // (loaded after the game's list was made: the game names its own items "$item_...")
            string name = item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? "";
            return name.StartsWith("$item_");
        }

        internal sealed class Found
        {
            public string Item, Slot, From;
            public int Land, Stack, Price;
        }

        /// <summary>The found items, given the built-in lands (for their prices and to leave out what they already have).</summary>
        internal static List<Found> Find(Func<int, string, (int stack, int price)> usual, Func<string, int> knownLand)
        {
            var found = new List<Found>();
            ObjectDB db = ObjectDB.instance;
            ZNetScene scene = ZNetScene.instance;
            ZoneSystem zones = ZoneSystem.instance;
            if (db == null || scene == null || zones == null) return found;

            var land = new Dictionary<string, (int land, string from)>();   // item prefab -> its land, and how it's had
            void Note(GameObject item, Heightmap.Biome biome, string from, int atLeast = 0)
            {
                if (item == null) return;
                int l = LandOf(biome);
                if (l < 0) return;
                l = Math.Max(l, atLeast);   // (late roamers in early lands)
                if (!land.TryGetValue(item.name, out var had) || l < had.land) land[item.name] = (l, from);
            }

            // gathered: what the world's vegetation gives
            foreach (ZoneSystem.ZoneVegetation v in zones.m_vegetation)
            {
                if (!v.m_enable || v.m_prefab == null) continue;
                foreach (GameObject item in Drops(v.m_prefab)) Note(item, v.m_biome, "gathered");
            }
            // dropped by creatures in the spawn lists (the game's, and those mods add at run time, as Wanted finds them)
            var lists = new List<SpawnSystemList>();
            SpawnSystem own = scene.GetPrefab("_ZoneCtrl")?.GetComponent<SpawnSystem>();
            if (own != null && own.m_spawnLists != null) lists.AddRange(own.m_spawnLists);
            foreach (SpawnSystem live in (LiveSpawners?.GetValue(null) as List<SpawnSystem>) ?? new List<SpawnSystem>())
                if (live != null && live.m_spawnLists != null) lists.AddRange(live.m_spawnLists);
            foreach (SpawnSystemList list in lists.Where(l => l != null).Distinct())
                foreach (SpawnSystem.SpawnData s in list.m_spawners ?? new List<SpawnSystem.SpawnData>())
                {
                    // (invasions and spawns over most lands say nothing about where a creature lives)
                    if (s == null || !s.m_enabled || s.m_prefab == null || !string.IsNullOrEmpty(s.m_requiredPersistentEvent) || BiomeCount(s.m_biome) > 5) continue;
                    Character c = s.m_prefab.GetComponent<Character>();
                    CharacterDrop d = s.m_prefab.GetComponent<CharacterDrop>();
                    if (c == null || d == null || c.IsBoss()) continue;
                    foreach (CharacterDrop.Drop drop in d.m_drops) Note(drop.m_prefab, s.m_biome, "dropped by " + s.m_prefab.name, KeyLand(s.m_requiredGlobalKey));
                }

            int LandOfItem(string prefab) => land.TryGetValue(prefab, out var l) ? l.land : knownLand(prefab);

            foreach (GameObject item in db.m_items)
            {
                ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
                if (drop == null || IsVanilla(item) || item.name.StartsWith("goodtidings")) continue;
                var shared = drop.m_itemData.m_shared;
                bool goods = shared.m_itemType == ItemDrop.ItemData.ItemType.Material ||
                             shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && shared.m_food > 0f;   // (berries, mushrooms)
                if (goods && land.TryGetValue(item.name, out var at))
                {
                    var (stack, price) = usual(at.land, "goods");
                    found.Add(new Found { Item = item.name, Slot = "goods", Land = at.land, From = at.from,
                        Stack = Mathf.Min(stack, Mathf.Max(1, shared.m_maxStackSize)), Price = price });
                }
            }

            // meads: a fermenter's product, its base made from ingredients with a land
            foreach (GameObject go in scene.m_prefabs)
            {
                Fermenter f = go ? go.GetComponent<Fermenter>() : null;
                if (f == null) continue;
                foreach (Fermenter.ItemConversion c in f.m_conversion)
                {
                    if (c.m_to == null || c.m_from == null || IsVanilla(c.m_to.gameObject)) continue;
                    Recipe baseRecipe = db.m_recipes.FirstOrDefault(r => r && r.m_item == c.m_from);
                    if (baseRecipe == null || baseRecipe.m_resources == null) continue;
                    int l = baseRecipe.m_resources.Where(q => q.m_resItem).Select(q => LandOfItem(q.m_resItem.name)).DefaultIfEmpty(-1).Max();
                    if (l < 0) continue;
                    var (stack, price) = usual(l, "meads");
                    found.Add(new Found { Item = c.m_to.name, Slot = "meads", Land = l, From = "fermented from " + c.m_from.name, Stack = stack, Price = price });
                }
            }
            return found;
        }

        /// <summary>What a vegetation prefab gives: picked, mined, chopped or broken.</summary>
        private static IEnumerable<GameObject> Drops(GameObject prefab)
        {
            Pickable pick = prefab.GetComponent<Pickable>();
            if (pick && pick.m_itemPrefab) yield return pick.m_itemPrefab;
            foreach (DropTable t in new[]
            {
                prefab.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed, prefab.GetComponent<MineRock>()?.m_dropItems,
                prefab.GetComponent<MineRock5>()?.m_dropItems, prefab.GetComponent<TreeBase>()?.m_dropWhenDestroyed,
            })
                if (t?.m_drops != null)
                    foreach (DropTable.DropData d in t.m_drops)
                        if (d.m_item) yield return d.m_item;
        }

        private static int BiomeCount(Heightmap.Biome biomes)
        {
            int n = 0;
            for (int bits = (int)biomes; bits != 0; bits &= bits - 1) n++;
            return biomes < 0 ? 32 : n;
        }

        /// <summary>The land a "boss defeated" key opens (a spawn that waits for it belongs there at the earliest); 0 for any other key.</summary>
        private static int KeyLand(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            for (int i = 0; i < Stock.LandKeys.Length; i++)
                if (Stock.LandKeys[i] == key) return i;
            return 0;
        }

        /// <summary>A biome mask's earliest land (Stock's order); the open sea counts as the Mountains' (serpents come after Bonemass).</summary>
        private static int LandOf(Heightmap.Biome biome)
        {
            (Heightmap.Biome b, int land)[] order =
            {
                (Heightmap.Biome.Meadows, 0), (Heightmap.Biome.BlackForest, 1), (Heightmap.Biome.Swamp, 2), (Heightmap.Biome.Mountain, 3),
                (Heightmap.Biome.Ocean, 3), (Heightmap.Biome.Plains, 4), (Heightmap.Biome.Mistlands, 5), (Heightmap.Biome.AshLands, 6),
                (Heightmap.Biome.DeepNorth, 7),
            };
            int best = -1;
            foreach (var (b, l) in order)
                if ((biome & b) != 0 && (best < 0 || l < best)) best = l;
            return best;
        }
    }
}
