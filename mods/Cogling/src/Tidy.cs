using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Tidying the chests of his area (step 5): which stack to move, from which chest to which. One move at a time, the nearest first.
    ///   - Consolidating (always): an item kept in several chests goes to the one holding the most of it (ties: the one nearer him).
    ///   - Re-sorting by groups (the "Re-sort by groups" setting, off by default): a stack alone in its chest (nothing else of its group
    ///     or name pattern there) goes to the chest that holds most of its group, if one does. The broad "same type" rule is left out
    ///     here: it would shuffle every material around.
    /// Only to a chest with room for the whole stack; never a stack the floor job or another Cogling of this game is moving.
    /// </summary>
    internal static class Tidy
    {
        internal sealed class Move
        {
            internal Container From, To;
            internal string Name;
            internal int Quality;
            internal string Why;
        }

        internal static Move Next(List<Container> chests, Vector3 from)
        {
            var moves = new List<Move>();
            Consolidate(chests, moves);
            if (Plugin.ResortGroups.Value && moves.Count == 0) Resort(chests, moves);
            return moves.OrderBy(m => Vector3.Distance(from, m.From.transform.position) + Vector3.Distance(m.From.transform.position, m.To.transform.position) * 0.5f).FirstOrDefault();
        }

        private static void Consolidate(List<Container> chests, List<Move> moves)
        {
            // every item name kept in more than one chest
            var holders = new Dictionary<string, List<Container>>();
            foreach (Container c in chests)
                foreach (string name in c.GetInventory().GetAllItems().Select(i => i.m_shared.m_name).Distinct())
                {
                    if (!holders.TryGetValue(name, out var list)) holders[name] = list = new List<Container>();
                    list.Add(c);
                }
            foreach (var kv in holders.Where(h => h.Value.Count > 1))
            {
                string name = kv.Key;
                List<Container> sorted = kv.Value.OrderByDescending(c => c.GetInventory().CountItems(name)).ToList();
                Container home = sorted[0];
                foreach (Container other in sorted.Skip(1))
                {
                    ItemDrop.ItemData stack = other.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == name).OrderBy(i => i.m_stack).First();
                    if (!home.GetInventory().CanAddItem(stack)) continue;
                    moves.Add(new Move { From = other, To = home, Name = name, Quality = stack.m_quality, Why = $"the chest at {home.transform.position:F1} holds the most {Sorting.Short(stack)}" });
                    break;
                }
            }
        }

        private static void Resort(List<Container> chests, List<Move> moves)
        {
            foreach (Container c in chests)
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems().ToList())
                {
                    // a stray: its only stack in this chest, with no relatives here (a chest kept for one thing is never emptied)
                    if (c.GetInventory().GetAllItems().Count(i => i.m_shared.m_name == item.m_shared.m_name) > 1) continue;
                    if (Sorting.Kin(c.GetInventory(), item) > 0) continue;
                    Container home = chests.Where(o => o != c && o.GetInventory().CanAddItem(item))
                        .Select(o => (o, n: Sorting.Kin(o.GetInventory(), item))).Where(t => t.n > 0)
                        .OrderByDescending(t => t.n).Select(t => t.o).FirstOrDefault();
                    if (home == null) continue;
                    moves.Add(new Move { From = c, To = home, Name = item.m_shared.m_name, Quality = item.m_quality, Why = $"its group lives in the chest at {home.transform.position:F1}" });
                }
        }
    }
}
