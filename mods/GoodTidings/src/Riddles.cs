using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>One riddle: Muninn's words, three answers (already in this player's language), and which is right.</summary>
    internal sealed class Riddle
    {
        public string Text;
        public string[] Answers;
        public int Correct;
        /// <summary>One of the old riddles (Lore.cs): a better reward.</summary>
        public bool IsLore;
    }

    /// <summary>
    /// Muninn's "do you remember" riddles, made from this player's own discoveries (the game keeps every material, recipe and crafting
    /// station a character has come across), so they're endless, follow the world's progress and are always fair: he only asks about
    /// what you've seen. Five kinds: what a recipe makes, who drops a thing, where a thing is made, what wakes a boss, where a thing grows.
    /// The wrong answers are of the same kind (other known recipes, creatures, stations...), so they're believable.
    /// </summary>
    internal static class Riddles
    {
        internal static Riddle Make(Player p, System.Random rnd)
        {
            var kinds = new List<Func<Player, System.Random, Riddle>> { Craft, Drop, Station, Boss, Grows };
            while (kinds.Count > 0)
            {
                var kind = kinds[rnd.Next(kinds.Count)];
                kinds.Remove(kind);
                Riddle r = null;
                try { r = kind(p, rnd); }
                catch (Exception e) { Plugin.Debug($"A riddle failed: {e.Message}"); }
                if (r != null) return r;
            }
            return null;
        }

        private static string L(string s) => Localization.instance.Localize(s);

        /// <summary>Whether a name has words in this language (the game shows a missing one as "[key]").</summary>
        private static bool Named(string localized) => !string.IsNullOrEmpty(localized) && !localized.StartsWith("[");

        /// <summary>"A, B and C".</summary>
        private static string List(List<string> things) =>
            things.Count < 2 ? string.Join("", things) : string.Join(", ", things.Take(things.Count - 1)) + L("$goodtidings_riddle_and") + things.Last();

        private static Riddle Build(string text, string right, IEnumerable<string> wrongs, System.Random rnd)
        {
            if (!Named(right)) return null;
            // as many wrong answers as the world's wisdom asks for (Lore.Choices), at least two
            var w = wrongs.Where(x => x != right && Named(x)).Distinct().OrderBy(_ => rnd.Next()).Take(Lore.Choices() - 1).ToList();
            if (w.Count < 2) return null;
            var answers = new List<string> { right }.Concat(w).OrderBy(_ => rnd.Next()).ToList();
            return new Riddle { Text = text, Answers = answers.ToArray(), Correct = answers.IndexOf(right) };
        }

        private static IEnumerable<Recipe> KnownRecipes(Player p) =>
            ObjectDB.instance.m_recipes.Where(r => r && r.m_enabled && r.m_item && r.m_resources != null && r.m_resources.Length > 0
                                                   && p.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name));

        // "From copper and tin, shaped at the forge: what was made?"
        private static Riddle Craft(Player p, System.Random rnd)
        {
            var recipes = KnownRecipes(p).Where(r => r.m_resources.Length >= 2 && r.m_resources.Length <= 3
                && r.m_resources.All(q => q.m_resItem && p.IsMaterialKnown(q.m_resItem.m_itemData.m_shared.m_name))).ToList();
            if (recipes.Count < 3) return null;
            Recipe pick = recipes[rnd.Next(recipes.Count)];
            string ingredients = List(pick.m_resources.Select(q => L(q.m_resItem.m_itemData.m_shared.m_name)).ToList());
            string where = pick.m_craftingStation ? L("$goodtidings_riddle_at").Replace("$1", L(pick.m_craftingStation.m_name)) : L("$goodtidings_riddle_byhand");
            string right = L(pick.m_item.m_itemData.m_shared.m_name);
            // the wrong ones from the same station, as far as there are
            var same = recipes.Where(r => r.m_craftingStation == pick.m_craftingStation).Select(r => L(r.m_item.m_itemData.m_shared.m_name)).ToList();
            var wrongs = same.Distinct().Count() >= 3 ? same : recipes.Select(r => L(r.m_item.m_itemData.m_shared.m_name)).ToList();
            return Build(L("$goodtidings_riddle_craft").Replace("$1", ingredients).Replace("$2", where), right, wrongs, rnd);
        }

        // "One who falls leaves Ooze behind: who?"
        private static Riddle Drop(Player p, System.Random rnd)
        {
            var droppers = new Dictionary<string, HashSet<string>>();   // item name -> creature names
            var creatures = new HashSet<string>();
            foreach (GameObject go in ZNetScene.instance.m_prefabs)
            {
                if (go == null) continue;
                Character c = go.GetComponent<Character>();
                CharacterDrop d = go.GetComponent<CharacterDrop>();
                if (c == null || d == null || c is Player || c.IsBoss() || string.IsNullOrEmpty(c.m_name) || !c.m_name.StartsWith("$")) continue;
                string who = L(c.m_name);
                bool met = false;
                foreach (CharacterDrop.Drop drop in d.m_drops)
                {
                    ItemDrop item = drop.m_prefab ? drop.m_prefab.GetComponent<ItemDrop>() : null;
                    if (item == null) continue;
                    string what = item.m_itemData.m_shared.m_name;
                    if (!p.IsMaterialKnown(what)) continue;
                    met = true;
                    if (!droppers.TryGetValue(what, out var set)) droppers[what] = set = new HashSet<string>();
                    set.Add(who);
                }
                if (met) creatures.Add(who);
            }
            // a thing only one kind of creature leaves (coins and hides come from many)
            var single = droppers.Where(kv => kv.Value.Count == 1 && !kv.Key.Contains("coin")).ToList();
            if (single.Count == 0 || creatures.Count < 3) return null;
            var pick = single[rnd.Next(single.Count)];
            string right = pick.Value.First();
            return Build(L("$goodtidings_riddle_drop").Replace("$1", L(pick.Key)), right, creatures, rnd);
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Player, Dictionary<string, int>> KnownStations =
            HarmonyLib.AccessTools.FieldRefAccess<Player, Dictionary<string, int>>("m_knownStations");   // (private in the game)

        // "Where is the Fine Bow made?"
        private static Riddle Station(Player p, System.Random rnd)
        {
            var known = KnownStations(p).Keys.ToList();
            if (known.Count < 3) return null;
            var recipes = KnownRecipes(p).Where(r => r.m_craftingStation && known.Contains(r.m_craftingStation.m_name)).ToList();
            if (recipes.Count == 0) return null;
            Recipe pick = recipes[rnd.Next(recipes.Count)];
            return Build(L("$goodtidings_riddle_station").Replace("$1", L(pick.m_item.m_itemData.m_shared.m_name)),
                L(pick.m_craftingStation.m_name), known.Select(L), rnd);
        }

        // "What must be laid on Eikthyr's altar to wake him?" (bosses the world has beaten)
        private static readonly (string key, string boss, string offering)[] Bosses =
        {
            ("defeated_eikthyr", "$enemy_eikthyr", "TrophyDeer"), ("defeated_gdking", "$enemy_gdking", "AncientSeed"),
            ("defeated_bonemass", "$enemy_bonemass", "WitheredBone"), ("defeated_dragon", "$enemy_dragon", "DragonEgg"),
            ("defeated_goblinking", "$enemy_goblinking", "GoblinTotem"),
        };

        private static Riddle Boss(Player p, System.Random rnd)
        {
            string Name(string prefab) => ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            var beaten = Bosses.Where(b => ZoneSystem.instance.GetGlobalKey(b.key) && Name(b.offering) != null).ToList();
            if (beaten.Count == 0) return null;
            var pick = beaten[rnd.Next(beaten.Count)];
            var all = Bosses.Select(b => Name(b.offering)).Where(n => n != null).Select(L);
            return Build(L("$goodtidings_riddle_boss").Replace("$1", L(pick.boss)), L(Name(pick.offering)), all, rnd);
        }

        // "In which land does Thistle grow?" (things that grow in one land only)
        private static readonly Heightmap.Biome[] Lands =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain, Heightmap.Biome.Plains,
            Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        private static string LandName(Heightmap.Biome b) => L("$biome_" + b.ToString().ToLowerInvariant());

        private static Riddle Grows(Player p, System.Random rnd)
        {
            var lands = new Dictionary<string, Heightmap.Biome>();   // item name -> the lands it grows in
            foreach (ZoneSystem.ZoneVegetation v in ZoneSystem.instance.m_vegetation)
            {
                if (!v.m_enable || v.m_prefab == null) continue;
                Pickable pick = v.m_prefab.GetComponent<Pickable>();
                ItemDrop item = pick && pick.m_itemPrefab ? pick.m_itemPrefab.GetComponent<ItemDrop>() : null;
                if (item == null) continue;
                string what = item.m_itemData.m_shared.m_name;
                lands[what] = lands.TryGetValue(what, out var b) ? b | v.m_biome : v.m_biome;
            }
            var single = lands.Where(kv => p.IsMaterialKnown(kv.Key) && Lands.Count(l => (kv.Value & l) != 0) == 1).ToList();
            if (single.Count == 0) return null;
            var chosen = single[rnd.Next(single.Count)];
            Heightmap.Biome land = Lands.First(l => (chosen.Value & l) != 0);
            return Build(L("$goodtidings_riddle_grows").Replace("$1", L(chosen.Key)), LandName(land),
                Lands.Where(l => l != Heightmap.Biome.Ocean).Select(LandName), rnd);
        }
    }
}
