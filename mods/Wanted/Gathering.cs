using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wanted
{
    /// <summary>
    /// What a gathering notice's thing takes to get. Each thing's sources in the world (the creatures that drop it, the trees, rocks and
    /// plants that give it) and how many one of them gives on average; from the easiest, the least one is worth and the most a notice
    /// should ask for. A pine cone (a third of one per pine tree felled) is then worth the trees it takes, and a notice asks for a few.
    /// </summary>
    internal static class Gathering
    {
        internal enum Way { Kill, Tree, Rock, Pick, Break }

        internal struct Source { public string Prefab; public Way Way; public float Each; }

        private const int MostChores = 15;   // trees felled, things broken: the most a notice should take
        private const int MostPieces = 40;   // pieces broken off rocks and deposits (two or three deposits)
        private const int MostPicks = 25;    // plants picked
        private const float KillShare = 0.5f;   // a kill's worth that goes to this thing (the creature's other drops are the hunter's)

        private static Dictionary<string, List<Source>> _sources;

        public static void Forget() => _sources = null;

        /// <summary>One felled tree, broken rock or picked plant: a coin in the Meadows, half a coin more each land after.</summary>
        public static float Chore(int tier) => 1f + 0.5f * tier;

        /// <summary>
        /// A gathering notice's price: what one is worth (its listed worth, or what the easiest source takes if that's more) and the usual
        /// amount (its listed amount, or fewer if more would take over 15 trees, 40 pieces of rock, 25 picks, or two bounties' kills).
        /// </summary>
        public static void Price(Entry e, List<Entry> all, out int usual, out float each)
        {
            usual = e.Count;
            each = e.Value;
            if (!Easiest(e, all, out float cost, out int most)) return;
            each = Mathf.Max(e.Value, cost);
            usual = Mathf.Clamp(usual, 1, Math.Max(1, most));
        }

        /// <summary>The cheapest way to get one (its cost in coins) and how many that way gives in a notice's worth of work.</summary>
        public static bool Easiest(Entry e, List<Entry> all, out float cost, out int most)
        {
            cost = float.MaxValue;
            most = 0;
            if (!Sources().TryGetValue(e.Prefab, out List<Source> list)) return false;
            foreach (Source s in list)
            {
                float effort; int tries;
                if (s.Way == Way.Kill)
                {
                    Entry kill = all.FirstOrDefault(k => k.Kind == Kind.Kill && k.Prefab == s.Prefab);
                    if (kill == null) continue;   // (a tame hen, a variant: no bounty, no errand)
                    effort = kill.Value * KillShare;
                    tries = 2 * kill.Count;
                }
                else
                {
                    effort = Chore(e.Tier);
                    tries = s.Way == Way.Pick ? MostPicks : s.Way == Way.Rock ? MostPieces : MostChores;
                }
                float one = effort / s.Each;
                if (one < cost) { cost = one; most = Mathf.FloorToInt(tries * s.Each); }
            }
            return cost < float.MaxValue;
        }

        /// <summary>For each thing, what gives it in the world and how many one of them gives on average (found once a world).</summary>
        public static Dictionary<string, List<Source>> Sources()
        {
            if (_sources != null) return _sources;
            var found = new Dictionary<string, List<Source>>();
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return found;
            var wild = new HashSet<string>((ZoneSystem.instance?.m_vegetation ?? new List<ZoneSystem.ZoneVegetation>())
                .Where(v => v?.m_prefab != null).Select(v => v.m_prefab.name));

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;
                var got = new Dictionary<string, float>();
                Way way;
                Pickable pick = prefab.GetComponent<Pickable>();
                TreeBase tree = prefab.GetComponent<TreeBase>();
                MineRock rock = prefab.GetComponent<MineRock>();
                MineRock5 rock5 = prefab.GetComponent<MineRock5>();
                CharacterDrop drops = prefab.GetComponent<CharacterDrop>();
                if (pick != null)
                {
                    way = Way.Pick;
                    if (pick.m_itemPrefab != null) got[pick.m_itemPrefab.name] = pick.m_amount;
                    Table(pick.m_extraDrops, got);
                }
                else if (tree != null) { way = Way.Tree; Table(tree.m_dropWhenDestroyed, got); Log(tree.m_logPrefab, got, 0); }
                else if (rock != null || rock5 != null) { way = Way.Rock; Table(rock?.m_dropItems, got); Table(rock5?.m_dropItems, got); }   // (per piece)
                else if (drops != null && prefab.GetComponent<Character>() != null)
                {
                    way = Way.Kill;
                    foreach (CharacterDrop.Drop d in drops.m_drops ?? new List<CharacterDrop.Drop>())
                        if (d?.m_prefab != null) Add(got, d.m_prefab.name, d.m_chance * (d.m_amountMin + d.m_amountMax) / 2f);
                }
                else if (wild.Contains(prefab.name)) { way = Way.Break; Table(prefab.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed, got); }
                else continue;   // (barrels and props in dungeons and villages: found once, not gathered from)

                foreach (var pair in got)
                {
                    if (pair.Value <= 0f) continue;
                    if (!found.TryGetValue(pair.Key, out var list)) found[pair.Key] = list = new List<Source>();
                    list.Add(new Source { Prefab = prefab.name, Way = way, Each = pair.Value });
                }
            }
            return _sources = found;
        }

        private static void Add(Dictionary<string, float> into, string name, float amount)
        {
            into.TryGetValue(name, out float had);
            into[name] = had + amount;
        }

        /// <summary>A drop table's average yield of each thing.</summary>
        private static void Table(DropTable t, Dictionary<string, float> into)
        {
            if (t?.m_drops == null || t.m_drops.Count == 0) return;
            float draws = (t.m_dropMin + t.m_dropMax) / 2f * Mathf.Clamp01(t.m_dropChance);
            float weights = t.m_drops.Sum(d => d.m_weight);
            foreach (DropTable.DropData d in t.m_drops)
            {
                if (d.m_item == null) continue;
                float share = t.m_oneOfEach ? Mathf.Min(1f, draws / t.m_drops.Count) : draws * (weights > 0f ? d.m_weight / weights : 1f / t.m_drops.Count);
                Add(into, d.m_item.name, share * (d.m_stackMin + d.m_stackMax) / 2f);
            }
        }

        /// <summary>A felled tree's log, and the logs it splits into, all the way down.</summary>
        private static void Log(GameObject log, Dictionary<string, float> into, int depth)
        {
            TreeLog l = log != null ? log.GetComponent<TreeLog>() : null;
            if (l == null || depth > 4) return;
            Table(l.m_dropWhenDestroyed, into);
            if (l.m_subLogPrefab != null)
                for (int i = 0; i < (l.m_subLogPoints?.Length ?? 0); i++) Log(l.m_subLogPrefab, into, depth + 1);
        }
    }
}
