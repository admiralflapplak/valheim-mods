using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wanted
{
    /// <summary>
    /// Notices for things the built-in lists don't name, found in the game as it runs: other mods' creatures, items, food and metals (and
    /// the game's own that the lists leave out), so a content mod's things come up on boards with nothing to set up. Run once a world is
    /// loaded (every mod has registered its prefabs by then), the first time a board posts:
    ///   - creatures: every one in the world's spawn lists (players, bosses, Dvergr and tames aside), at the earliest stage of the lands it
    ///     spawns in, worth what a built-in creature of that stage and health is worth; their trophies too;
    ///   - things to gather: what the world's rocks, plants and trees drop, and what those creatures drop, at the earliest stage of where
    ///     they come from (an ore is worth the bar it smelts into);
    ///   - provisions: food and meads made from known things (a recipe, a cooking station, a fermenter), at the stage of their latest
    ///     ingredient, worth their ingredients and a quarter more;
    ///   - rewards: what a smelter, blast furnace or refinery makes from a known ore, at the ore's stage, worth the ore; and for bonuses,
    ///     arrows and bolts (at the stage of what they're made of) and valuables (worth their price at the trader).
    /// Each found notice asks for about as much as a built-in one of its kind and stage is worth. The built-in lists win where both have a
    /// thing; the contracts file's "remove" takes found ones out too, and its "add" sets their numbers by hand. `wanted found` lists them.
    /// </summary>
    internal static class Discovery
    {
        private static readonly System.Reflection.FieldInfo LiveSpawners = HarmonyLib.AccessTools.Field(typeof(SpawnSystem), "m_instances");

        public static List<Entry> Found = new List<Entry>();
        public static List<PayEntry> FoundPay = new List<PayEntry>();
        public static List<PayEntry> Ammo = new List<PayEntry>();        // arrows and bolts, at the stage of what they're made of
        public static List<PayEntry> Valuables = new List<PayEntry>();   // things the trader buys (amber, rubies...): worth their price
        public static Dictionary<string, int> CreatureTiers = new Dictionary<string, int>();   // the last pass's stage for each spawning creature

        /// <summary>For `wanted found`: every creature the game has that no notice asks for, and why.</summary>
        public static List<string> Skipped()
        {
            var lines = new List<string>();
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return lines;
            var asked = new HashSet<string>(ContractData.AllEntries().Where(e => e.Kind == Kind.Kill)
                .Select(e => scene.GetPrefab(e.Prefab)?.GetComponent<Character>()?.m_name).Where(n => n != null));
            foreach (GameObject go in scene.m_prefabs)
            {
                Character c = go != null ? go.GetComponent<Character>() : null;
                if (c == null || c is Player || asked.Contains(c.m_name)) continue;
                string why = c.m_boss ? "a boss"
                    : c.GetComponent<BaseAI>() == null ? "no AI"
                    : c.m_faction == Character.Faction.Players || c.m_faction == Character.Faction.Dverger || c.m_faction == Character.Faction.PlayerSpawned
                      || c.m_faction == Character.Faction.TrainingDummy || c.m_faction == Character.Faction.Boss ? "faction " + c.m_faction
                    : !CreatureTiers.ContainsKey(go.name) ? "not in the world's spawn lists (spawned by a location, an event or its own mod)"
                    : "?";
                lines.Add($"{go.name} ({Contracts.DisplayName(go.name)}): {why}");
            }
            return lines;
        }
        private static ZNetScene _doneFor;

        /// <summary>Finds them, once per world (cheap enough to run at a dawn: a pass over the game's prefabs and recipes).</summary>
        public static void Ensure()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || ObjectDB.instance == null || ZoneSystem.instance == null || ReferenceEquals(scene, _doneFor)) return;
            _doneFor = scene;
            try { Run(scene, ObjectDB.instance); }
            catch (Exception e) { Plugin.Log.LogWarning("Finding other mods' creatures and items: " + e); }
        }

        /// <summary>Find them again (the contracts file changed, or a hot reload).</summary>
        public static void Forget()
        {
            _doneFor = null;
            Found = new List<Entry>();
            FoundPay = new List<PayEntry>();
            Ammo = new List<PayEntry>();
            Valuables = new List<PayEntry>();
            Gathering.Forget();
        }

        // ---- stages -----------------------------------------------------------------------------------------------------

        /// <summary>The earliest stage among the lands a biome mask covers (the open sea counts as the Mountain's: serpents).</summary>
        private static int TierOf(Heightmap.Biome biomes)
        {
            int best = int.MaxValue;
            void Is(Heightmap.Biome b, int tier) { if ((biomes & b) != 0) best = Math.Min(best, tier); }
            Is(Heightmap.Biome.Meadows, 0); Is(Heightmap.Biome.BlackForest, 1); Is(Heightmap.Biome.Swamp, 2); Is(Heightmap.Biome.Mountain, 3);
            Is(Heightmap.Biome.Ocean, 3); Is(Heightmap.Biome.Plains, 4); Is(Heightmap.Biome.Mistlands, 5); Is(Heightmap.Biome.AshLands, 6);
            Is(Heightmap.Biome.DeepNorth, 7);
            return best == int.MaxValue ? -1 : best;
        }

        /// <summary>How many lands a biome mask covers (all of them, -1, counts as many).</summary>
        private static int Lands(Heightmap.Biome biomes)
        {
            int n = 0;
            for (int bits = (int)biomes; bits != 0; bits &= bits - 1) n++;
            return biomes < 0 ? 32 : n;
        }

        /// <summary>The stage a "boss defeated" key opens (a spawn that waits for it belongs there at the earliest); 0 for any other key.</summary>
        private static int KeyTier(string key)
        {
            switch (key)
            {
                case "defeated_eikthyr": return 1;
                case "defeated_gdking": return 2;
                case "defeated_bonemass": return 3;
                case "defeated_dragon": return 4;
                case "defeated_goblinking": return 5;
                case "defeated_queen": return 6;
                case "defeated_fader": return 7;
                default: return 0;
            }
        }

        private static void Lower(Dictionary<string, int> tiers, string prefab, int tier)
        {
            if (string.IsNullOrEmpty(prefab) || tier < 0) return;
            tiers[prefab] = tiers.TryGetValue(prefab, out int had) ? Math.Min(had, tier) : tier;
        }

        private static float Median(IEnumerable<float> values, float fallback)
        {
            var v = values.OrderBy(x => x).ToList();
            return v.Count == 0 ? fallback : v[v.Count / 2];
        }

        // ---- the pass ---------------------------------------------------------------------------------------------------

        private static void Run(ZNetScene scene, ObjectDB db)
        {
            List<Entry> curated = ContractData.Entries;
            List<PayEntry> curatedPay = ContractData.Pay;
            var taken = new HashSet<string>(curated.Select(e => e.Prefab).Concat(curatedPay.Select(p => p.Prefab)).Concat(ContractData.Removed)
                .Concat(ContractData.Bonus.Select(b => b.Prefab)));   // (a bonus, such as Refined Eitr, is never plain pay too)
            foreach (Recipe r in db.m_recipes)   // things used only to upgrade at the Forge of Potential (its idols): never errands
                foreach (Piece.Requirement req in r?.m_resources ?? new Piece.Requirement[0])
                    if (req != null && req.m_upgraderResource && req.m_resItem != null) taken.Add(req.m_resItem.name);
            var found = new List<Entry>();
            var foundPay = new List<PayEntry>();

            // what the built-in lists already know a thing to be worth, and at what stage
            var known = new Dictionary<string, (int tier, float value)>();
            foreach (Entry e in curated.Where(e => e.Kind != Kind.Kill)) if (!known.ContainsKey(e.Prefab)) known[e.Prefab] = (e.Tier, e.Value);
            foreach (PayEntry p in curatedPay) if (!known.ContainsKey(p.Prefab)) known[p.Prefab] = (p.Tier, p.Value);

            // ---- creatures: where they spawn
            var creatureTier = new Dictionary<string, int>();
            var lists = new List<SpawnSystemList>();
            GameObject zoneCtrl = scene.GetPrefab("_ZoneCtrl");
            SpawnSystem own = zoneCtrl != null ? zoneCtrl.GetComponent<SpawnSystem>() : null;
            if (own != null && own.m_spawnLists != null) lists.AddRange(own.m_spawnLists);
            foreach (SpawnSystem live in (LiveSpawners?.GetValue(null) as List<SpawnSystem>) ?? new List<SpawnSystem>())   // (mods that add their spawns at run time)
                if (live != null && live.m_spawnLists != null) lists.AddRange(live.m_spawnLists);
            foreach (SpawnSystemList list in lists.Where(l => l != null).Distinct())
                foreach (SpawnSystem.SpawnData s in list.m_spawners ?? new List<SpawnSystem.SpawnData>())
                {
                    // invasions (a persistent event) and spawns over every land say nothing about where a creature lives
                    if (s == null || !s.m_enabled || s.m_prefab == null || !string.IsNullOrEmpty(s.m_requiredPersistentEvent) || Lands(s.m_biome) > 5) continue;
                    Lower(creatureTier, s.m_prefab.name, Math.Max(TierOf(s.m_biome), KeyTier(s.m_requiredGlobalKey)));   // (late roamers in early lands)
                }
            foreach (Entry e in curated.Where(e => e.Kind == Kind.Kill)) Lower(creatureTier, e.Prefab, e.Tier);
            CreatureTiers = creatureTier;

            // a built-in creature's worth per point of health, by stage: found ones are worth the same for theirs
            var perHealth = new Dictionary<int, float>();
            for (int t = 0; t < Contracts.Tiers; t++)
                perHealth[t] = Median(curated.Where(e => e.Kind == Kind.Kill && e.Tier == t)
                    .Select(e => (e, c: scene.GetPrefab(e.Prefab)?.GetComponent<Character>()))
                    .Where(x => x.c != null && x.c.m_health > 0f).Select(x => x.e.Value / x.c.m_health), t > 0 ? perHealth[t - 1] : 0.08f);

            var itemTier = new Dictionary<string, int>();   // where things can be had: creature drops and the world's plants and rocks
            var names = new HashSet<string>(curated.Where(e => e.Kind == Kind.Kill)
                .Select(e => scene.GetPrefab(e.Prefab)?.GetComponent<Character>()?.m_name).Where(n => n != null));
            foreach (var pair in creatureTier)
            {
                GameObject go = scene.GetPrefab(pair.Key);
                Character c = go != null ? go.GetComponent<Character>() : null;
                if (!Huntable(c)) continue;
                float value = Mathf.Max(1f, c.m_health * perHealth[Math.Min(pair.Value, Contracts.Tiers - 1)]);
                // a bounty names a creature, and kills count by name (Kills.Credit): one notice per name, so a variant ("Skeleton" of the
                // swamp) never gets its own beside the creature it's named for
                if (!taken.Contains(pair.Key) && names.Add(c.m_name))
                    found.Add(new Entry(Kind.Kill, pair.Value, pair.Key, CountFor(curated, Kind.Kill, pair.Value, value), Round(value)));

                foreach (CharacterDrop.Drop d in go.GetComponent<CharacterDrop>()?.m_drops ?? new List<CharacterDrop.Drop>())
                {
                    ItemDrop item = d?.m_prefab != null ? d.m_prefab.GetComponent<ItemDrop>() : null;
                    if (item == null) continue;
                    if (item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy)
                    {
                        if (taken.Contains(d.m_prefab.name) || found.Any(f => f.Prefab == d.m_prefab.name)) continue;
                        float trophy = Round(value * Mathf.Clamp(1f / Mathf.Max(0.05f, d.m_chance), 2f, 6f) * 0.5f);   // (rarer is worth more, up to 3 kills)
                        found.Add(new Entry(Kind.Trophy, pair.Value, d.m_prefab.name, CountFor(curated, Kind.Trophy, pair.Value, trophy), trophy));
                    }
                    else if (d.m_chance >= 0.25f) Lower(itemTier, d.m_prefab.name, pair.Value);   // (a rare drop is no errand to send someone on)
                }
            }

            // ---- the world's plants, rocks and trees
            foreach (ZoneSystem.ZoneVegetation v in ZoneSystem.instance.m_vegetation ?? new List<ZoneSystem.ZoneVegetation>())
            {
                if (v == null || !v.m_enable || v.m_prefab == null || Lands(v.m_biome) > 5) continue;
                int tier = TierOf(v.m_biome);
                foreach (GameObject drop in DropsOf(v.m_prefab)) Lower(itemTier, drop.name, tier);
            }

            // ---- metals: what a fuelled smelter makes from an ore (a smelter, a blast furnace, an eitr refinery; not the kiln or the mill)
            var smelted = new Dictionary<string, string>();   // bar -> ore
            foreach (GameObject prefab in scene.m_prefabs)
            {
                Smelter smelter = prefab != null ? prefab.GetComponent<Smelter>() : null;
                if (smelter == null || smelter.m_fuelItem == null) continue;
                foreach (Smelter.ItemConversion c in smelter.m_conversion ?? new List<Smelter.ItemConversion>())
                    if (c?.m_from != null && c.m_to != null && !smelted.ContainsKey(c.m_to.name)) smelted[c.m_to.name] = c.m_from.name;
            }

            // ---- an ore is worth its bar: one known gives the other its worth; a new pair is worth what the stage's metals are
            foreach (var bar in smelted)
            {
                bool barKnown = known.TryGetValue(bar.Key, out var b), oreKnown = known.TryGetValue(bar.Value, out var o);
                if (barKnown && !oreKnown) known[bar.Value] = b;
                else if (oreKnown && !barKnown) known[bar.Key] = o;
                else if (!barKnown && itemTier.TryGetValue(bar.Value, out int t))
                {
                    float metal = Median(curatedPay.Where(p => p.Tier == t).Select(p => p.Value), Median(curatedPay.Select(p => p.Value), 10f));
                    known[bar.Key] = known[bar.Value] = (t, metal);
                }
            }

            // ---- things to gather, at the stage they come from (worth what a built-in thing of that stage is)
            float GatherValue(int tier) => Median(curated.Where(e => e.Kind == Kind.Gather && e.Tier == NearestTier(curated, Kind.Gather, tier)).Select(e => e.Value), 2f);
            foreach (var pair in itemTier)
            {
                GameObject go = scene.GetPrefab(pair.Key);
                ItemDrop item = go != null ? go.GetComponent<ItemDrop>() : null;
                if (!known.ContainsKey(pair.Key) && Gatherable(item)) known[pair.Key] = (pair.Value, GatherValue(pair.Value));
            }
            foreach (var pair in itemTier)
            {
                if (taken.Contains(pair.Key) || !known.TryGetValue(pair.Key, out var k)) continue;
                ItemDrop item = scene.GetPrefab(pair.Key)?.GetComponent<ItemDrop>();
                if (!Gatherable(item)) continue;
                found.Add(new Entry(Kind.Gather, k.tier, pair.Key, CountFor(curated, Kind.Gather, k.tier, k.value), Round(k.value)));
            }
            foreach (var bar in smelted)
                if (!taken.Contains(bar.Key) && known.TryGetValue(bar.Key, out var k) && scene.GetPrefab(bar.Key)?.GetComponent<ItemDrop>() != null)
                    foundPay.Add(new PayEntry(k.tier, bar.Key, Round(k.value)));

            // ---- provisions: food and meads made from things whose stage and worth are known
            var made = new Maker(scene, db, known);
            foreach (GameObject go in db.m_items)
            {
                ItemDrop item = go != null ? go.GetComponent<ItemDrop>() : null;
                if (item == null || taken.Contains(go.name) || !Edible(item) || !made.IsMade(go.name)) continue;   // (raw berries are gathering)
                var k = made.Of(go.name);
                if (k == null) continue;
                found.Add(new Entry(Kind.Provisions, k.Value.tier, go.name, CountFor(curated, Kind.Provisions, k.Value.tier, k.Value.value), Round(k.Value.value)));
            }

            // ---- bonuses: arrows and bolts by what they're made of; valuables by their price at the trader
            var ammo = new List<PayEntry>();
            var valuables = new List<PayEntry>();
            foreach (GameObject go in db.m_items)
            {
                ItemDrop item = go != null ? go.GetComponent<ItemDrop>() : null;
                if (item == null || ContractData.Removed.Contains(go.name) || item.m_itemData.m_shared.m_maxStackSize <= 1) continue;
                ItemDrop.ItemData.SharedData s = item.m_itemData.m_shared;
                if (s.m_itemType == ItemDrop.ItemData.ItemType.Ammo)
                {
                    var k = made.Of(go.name);
                    if (k != null) ammo.Add(new PayEntry(k.Value.tier, go.name, Mathf.Max(0.2f, k.Value.value)));
                }
                else if (s.m_value > 0 && go.name != "Coins" && s.m_itemType != ItemDrop.ItemData.ItemType.Trophy)
                    valuables.Add(new PayEntry(ContractData.ValuableTiers.TryGetValue(go.name, out int vt) ? vt
                        : itemTier.TryGetValue(go.name, out vt) ? vt : -1, go.name, s.m_value));   // (-1: found nowhere in particular)
            }

            Found = found;
            FoundPay = foundPay;
            Ammo = ammo;
            Valuables = valuables;
            Plugin.Log.LogInfo($"Found {found.Count} more things notices can ask for ({found.Count(e => e.Kind == Kind.Kill)} creatures), " +
                               $"{foundPay.Count} more rewards, {ammo.Count} arrows and bolts and {valuables.Count} valuables (wanted found lists them)");
        }

        /// <summary>A creature a bounty can be put on: a wild one that fights or flees, not a player, boss, Dvergr or dummy.</summary>
        private static bool Huntable(Character c)
        {
            if (c == null || c is Player || c.m_boss || c.m_health <= 0f || c.GetComponent<BaseAI>() == null) return false;
            if (c.m_name == null || c.m_name.Length == 0) return false;
            switch (c.m_faction)
            {
                case Character.Faction.Players: case Character.Faction.Boss: case Character.Faction.Dverger:
                case Character.Faction.PlayerSpawned: case Character.Faction.TrainingDummy:
                    return false;
            }
            return true;   // (a prefab is never tamed: tames are made from it in the world)
        }

        /// <summary>A thing worth asking someone to bring: a material, or a raw food (berries, mushrooms), nothing worth coins by itself.</summary>
        private static bool Gatherable(ItemDrop item)
        {
            if (item == null || item.name == NoticeItem.PrefabName) return false;
            ItemDrop.ItemData.SharedData s = item.m_itemData.m_shared;
            if (s.m_value > 0 || s.m_maxStackSize <= 1) return false;   // (valuables sell for coins; tools and gear don't stack)
            return s.m_itemType == ItemDrop.ItemData.ItemType.Material
                || (s.m_itemType == ItemDrop.ItemData.ItemType.Consumable && s.m_food > 0f);
        }

        /// <summary>Something to eat or drink: a food (it fills you) or a mead (it gives an effect).</summary>
        private static bool Edible(ItemDrop item)
        {
            ItemDrop.ItemData.SharedData s = item.m_itemData.m_shared;
            if (s.m_itemType != ItemDrop.ItemData.ItemType.Consumable || s.m_maxStackSize <= 1) return false;
            return s.m_food > 0f || s.m_foodStamina > 0f || s.m_consumeStatusEffect != null;
        }

        /// <summary>What a plant, rock or tree in the world gives: its pickings, what breaks off it, and its logs' wood.</summary>
        private static IEnumerable<GameObject> DropsOf(GameObject prefab)
        {
            var tables = new List<DropTable>();
            Pickable pick = prefab.GetComponent<Pickable>();
            if (pick != null) { if (pick.m_itemPrefab != null) yield return pick.m_itemPrefab; tables.Add(pick.m_extraDrops); }
            tables.Add(prefab.GetComponent<MineRock>()?.m_dropItems);
            tables.Add(prefab.GetComponent<MineRock5>()?.m_dropItems);
            tables.Add(prefab.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed);
            TreeBase tree = prefab.GetComponent<TreeBase>();
            if (tree != null)
            {
                tables.Add(tree.m_dropWhenDestroyed);
                tables.Add(tree.m_logPrefab != null ? tree.m_logPrefab.GetComponent<TreeLog>()?.m_dropWhenDestroyed : null);
            }
            foreach (DropTable t in tables)
                if (t?.m_drops != null)
                    foreach (DropTable.DropData d in t.m_drops)
                        if (d.m_item != null) yield return d.m_item;
        }

        /// <summary>How many to ask for: about what a built-in notice of this kind and stage is worth, within sensible bounds.</summary>
        private static int CountFor(List<Entry> curated, Kind kind, int tier, float value)
        {
            int near = NearestTier(curated, kind, tier);
            float worth = Median(curated.Where(e => e.Kind == kind && e.Tier == near).Select(e => e.Count * e.Value), 30f);
            int most = kind == Kind.Gather ? 40 : kind == Kind.Kill ? 10 : kind == Kind.Trophy ? 5 : 8;
            return Mathf.Clamp(Mathf.RoundToInt(worth / Mathf.Max(0.1f, value)), 1, most);
        }

        /// <summary>This stage, or the nearest below it the built-in lists have this kind for (the Deep North borrows the Ashlands').</summary>
        private static int NearestTier(List<Entry> curated, Kind kind, int tier)
        {
            while (tier > 0 && !curated.Any(e => e.Kind == kind && e.Tier == tier)) tier--;
            return tier;
        }

        private static float Round(float v) => v < 10f ? Mathf.Round(v * 2f) / 2f : Mathf.Round(v);

        /// <summary>
        /// The stage and worth of a made thing: from its recipe, a cooking station or a fermenter, through its ingredients (each known
        /// or made in turn), at the latest stage among them, worth a quarter more than they are (the work and the fire).
        /// </summary>
        private sealed class Maker
        {
            private readonly Dictionary<string, (int tier, float value)> _known;
            private readonly Dictionary<string, List<(Dictionary<string, float> from, float makes)>> _ways = new Dictionary<string, List<(Dictionary<string, float>, float)>>();
            private readonly Dictionary<string, (int tier, float value)?> _done = new Dictionary<string, (int, float)?>();
            private readonly HashSet<string> _working = new HashSet<string>();

            public Maker(ZNetScene scene, ObjectDB db, Dictionary<string, (int tier, float value)> known)
            {
                _known = known;
                void Way(string to, Dictionary<string, float> from, float makes)
                {
                    if (to == null || from.Count == 0) return;
                    if (!_ways.TryGetValue(to, out var list)) _ways[to] = list = new List<(Dictionary<string, float>, float)>();
                    list.Add((from, Mathf.Max(1f, makes)));
                }
                foreach (Recipe r in db.m_recipes)
                {
                    if (r == null || !r.m_enabled || r.m_item == null || r.m_resources == null || r.m_requireOnlyOneIngredient) continue;
                    var from = new Dictionary<string, float>();
                    foreach (Piece.Requirement req in r.m_resources)
                        if (req?.m_resItem != null && req.m_amount > 0 && !req.m_upgraderResource) from[req.m_resItem.name] = req.m_amount;
                    Way(r.m_item.name, from, r.m_amount);
                }
                foreach (GameObject prefab in scene.m_prefabs)
                {
                    if (prefab == null) continue;
                    foreach (CookingStation.ItemConversion c in prefab.GetComponent<CookingStation>()?.m_conversion ?? new List<CookingStation.ItemConversion>())
                        if (c?.m_from != null && c.m_to != null) Way(c.m_to.name, new Dictionary<string, float> { { c.m_from.name, 1f } }, 1f);
                    foreach (Fermenter.ItemConversion c in prefab.GetComponent<Fermenter>()?.m_conversion ?? new List<Fermenter.ItemConversion>())
                        if (c?.m_from != null && c.m_to != null) Way(c.m_to.name, new Dictionary<string, float> { { c.m_from.name, 1f } }, c.m_producedItems);
                }
            }

            /// <summary>Whether anything makes it (a recipe, a cooking station, a fermenter).</summary>
            public bool IsMade(string prefab) => _ways.ContainsKey(prefab);

            public (int tier, float value)? Of(string prefab)
            {
                if (_known.TryGetValue(prefab, out var k)) return k;
                if (_done.TryGetValue(prefab, out var done)) return done;
                if (!_ways.TryGetValue(prefab, out var ways) || !_working.Add(prefab)) return null;   // (nothing makes it, or it's made of itself)
                (int tier, float value)? best = null;
                foreach (var (from, makes) in ways)
                {
                    int tier = 0;
                    float value = 0f;
                    bool all = true;
                    foreach (var ing in from)
                    {
                        var i = Of(ing.Key);
                        if (i == null) { all = false; break; }
                        tier = Math.Max(tier, i.Value.tier);
                        value += i.Value.value * ing.Value;
                    }
                    if (!all) continue;
                    var way = (tier, value / makes * 1.25f);
                    if (best == null || way.tier < best.Value.tier) best = way;   // (the earliest way to make it sets its stage)
                }
                _working.Remove(prefab);
                _done[prefab] = best;
                return best;
            }
        }
    }
}
