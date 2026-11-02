using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The chests loaded on this game, kept in our own list (a Container.Awake postfix; never a scene search on a timer), and the ones a
    /// Cogling may use in his area: built chests (ContainerAccess.IsStorage: not graves, carts, ships, creatures, the obliterator), that
    /// this game's player may open (ward, lock), and nobody has open.
    /// </summary>
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Chests
    {
        private static readonly List<Container> All = new List<Container>();

        private static void Postfix(Container __instance) => All.Add(__instance);

        /// <summary>After a hot reload the chests already loaded are found once.</summary>
        internal static void FindLoaded()
        {
            All.Clear();
            All.AddRange(Object.FindObjectsByType<Container>(FindObjectsSortMode.None));   // (once, at load)
        }

        internal static void Forget() => All.Clear();

        /// <summary>The chests in the area of these antennas that he may use now.</summary>
        internal static List<Container> Usable(List<Antenna> network)
        {
            All.RemoveAll(c => c == null);
            return All.Where(c => Antenna.InArea(network, c.transform.position)
                                  && ContainerAccess.IsStorage(c) && !ContainerAccess.InUse(c) && ContainerAccess.MayUse(c)).ToList();
        }
    }

    /// <summary>
    /// Which items on the floor a Cogling may pick up (DESIGN.md "Floor items"). Times are this game's, from when he first saw the item
    /// (the owner of the Cogling decides; other games only watch).
    /// </summary>
    internal static class FloorItems
    {
        private static readonly AccessTools.FieldRef<List<ItemDrop>> Instances = AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));
        private static readonly System.Reflection.MethodInfo EggCanGrow = AccessTools.Method(typeof(EggGrow), "CanGrow");
        private static readonly Dictionary<ZDOID, float> FirstSeen = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<ZDOID, float> ColdSince = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<ZDOID, float> NoHomeUntil = new Dictionary<ZDOID, float>();
        internal static readonly HashSet<ZDOID> Claimed = new HashSet<ZDOID>();   // (by a Cogling on this game: two never race)
        private static readonly int Below = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");

        /// <summary>
        /// How long ago an item was dropped: the drop time the game saves on every item (ZDO s_spawnTime, the world's clock), so it holds
        /// through restarts and hot reloads, and a stack he took part of keeps its own (it's still the same item). Items without one:
        /// from when he first saw them.
        /// </summary>
        private static float Age(ItemDrop d, ZDOID id)
        {
            ZDO zdo = d.GetComponent<ZNetView>()?.GetZDO();
            long ticks = zdo != null ? zdo.GetLong(ZDOVars.s_spawnTime, 0L) : 0L;
            if (ticks > 0L && ZNet.instance != null) return (float)(ZNet.instance.GetTime() - new System.DateTime(ticks)).TotalSeconds;
            if (!FirstSeen.TryGetValue(id, out float seen)) FirstSeen[id] = seen = Time.time;
            return Time.time - seen;
        }

        /// <summary>How many of an item he takes in one trip: the whole stack, or as many as his weight limit allows (at least one).</summary>
        internal static int Load(ItemDrop.ItemData item)
        {
            float w = item.m_shared.m_weight;
            if (w <= 0.0001f) return item.m_stack;
            return Mathf.Clamp(Mathf.FloorToInt(Plugin.MaxCarryWeight.Value / w), 1, item.m_stack);
        }

        internal static ZDOID Id(ItemDrop d) => d.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;

        /// <summary>No chest had room for it: leave it be a while.</summary>
        internal static void NoHome(ItemDrop d, float seconds = 90f) => NoHomeUntil[Id(d)] = Time.time + seconds;

        /// <summary>The items in the area he may take now, nearest first.</summary>
        internal static List<ItemDrop> Candidates(List<Antenna> network, Vector3 from)
        {
            float now = Time.time;
            var found = new List<ItemDrop>();
            List<Character> enemies = null;
            foreach (ItemDrop d in Instances())
            {
                if (d == null || !Antenna.InArea(network, d.transform.position)) continue;
                ZDOID id = Id(d);
                if (id == ZDOID.None || Claimed.Contains(id)) continue;
                if (!Allowed(d, id, Age(d, id))) continue;
                if (enemies == null) enemies = Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && !c.IsTamed() && c.GetFaction() != Character.Faction.Players
                                                                                      && c.GetFaction() != Character.Faction.AnimalsVeg && c.GetFaction() != Character.Faction.TrainingDummy).ToList();
                if (enemies.Any(e => Vector3.Distance(e.transform.position, d.transform.position) < 20f)) continue;   // (a fight near it: later)
                found.Add(d);
            }
            if (FirstSeen.Count > 2000) Prune();
            return found.OrderBy(d => Vector3.Distance(d.transform.position, from)).ToList();
        }

        private static bool Allowed(ItemDrop d, ZDOID id, float age) => WhyNot(d, id, age) == null;

        /// <summary>Why he leaves an item alone (null: he may take it).</summary>
        private static string WhyNot(ItemDrop d, ZDOID id, float age)
        {
            if (NoHomeUntil.TryGetValue(id, out float until) && Time.time < until) return $"no chest for it or out of reach (tries again in {until - Time.time:F0} s)";
            if (age < Plugin.DropWait.Value) return $"freshly dropped ({Plugin.DropWait.Value - age:F0} s to wait)";
            if (d.IsPiece()) return "placed with the build menu";
            ItemDrop.ItemData item = d.m_itemData;
            if (item == null || item.m_shared.m_weight > Plugin.MaxCarryWeight.Value) return $"too heavy (one weighs {item?.m_shared.m_weight:F0}: the dragon egg)";
            Vector3 at = d.transform.position;
            if (Floating.GetLiquidLevel(at, 1f, LiquidType.Water) > at.y + 0.05f) return "in water";
            if (Player.GetAllPlayers().Any(p => Vector3.Distance(p.transform.position, at) < 2f)) return "a player is next to it";
            if (!PrivateArea.CheckAccess(at, 0f, flash: false)) return "inside a ward you can't use";
            if (OnFurniture(d)) return "on furniture (a decoration)";
            if (d.GetComponent<EggGrow>() is EggGrow egg && !ColdEggReady(egg, id)) return "an egg: warm, or not cold for long enough yet";
            return null;
        }

        /// <summary>The same items (same name and quality) lying within a distance of a spot that he may take (not claimed, no reason not to).</summary>
        internal static List<ItemDrop> Near(Vector3 at, float within, ItemDrop.ItemData like)
        {
            var near = new List<ItemDrop>();
            foreach (ItemDrop d in Instances())
            {
                if (d == null || d.m_itemData == null || Vector3.Distance(d.transform.position, at) > within) continue;
                if (d.m_itemData.m_shared.m_name != like.m_shared.m_name || d.m_itemData.m_quality != like.m_quality) continue;
                ZDOID id = Id(d);
                if (id == ZDOID.None || Claimed.Contains(id)) continue;
                if (WhyNot(d, id, Age(d, id)) == null) near.Add(d);
            }
            return near;
        }

        /// <summary>For `cogling items`: every item in the area and what he makes of it.</summary>
        internal static List<string> Explain(List<Antenna> network, Vector3 from)
        {
            var lines = new List<string>();
            List<Container> chests = Chests.Usable(network);
            foreach (ItemDrop d in Instances().Where(x => x != null && Antenna.InArea(network, x.transform.position)).OrderBy(x => Vector3.Distance(x.transform.position, from)).Take(25))
            {
                ZDOID id = Id(d);
                float age = Age(d, id);
                string why = Claimed.Contains(id) ? "he's on his way to it" : WhyNot(d, id, age);
                if (why == null)
                {
                    Container c = Sorting.Choose(d.m_itemData, chests, d.transform.position, out string reason);
                    why = c == null ? "no chest has room for it" : $"will go to the chest at {c.transform.position:F1} ({reason})";
                }
                lines.Add($"{d.m_itemData.m_stack} {Sorting.Short(d.m_itemData)} at {d.transform.position:F1}: {why}");
            }
            if (lines.Count == 0) lines.Add("No items lying in his area.");
            lines.Add($"{chests.Count} chest(s) he may use in his area.");
            return lines;
        }

        private static bool OnFurniture(ItemDrop d)
        {
            if (!Physics.Raycast(d.transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 1.5f, Below)) return false;
            Piece under = hit.collider.GetComponentInParent<Piece>();
            return under != null && (under.m_category == Piece.PieceCategory.Furniture || (under.m_usage & Piece.UsageTagFlags.Furniture) != 0);
        }

        /// <summary>An egg is left alone while it's warm enough to hatch; a cold one, once it has stayed cold a while (if he collects them).</summary>
        private static bool ColdEggReady(EggGrow egg, ZDOID id)
        {
            bool warm = EggCanGrow != null && (bool)EggCanGrow.Invoke(egg, null);
            if (warm || !Plugin.CollectColdEggs.Value) { ColdSince.Remove(id); return false; }
            if (!ColdSince.TryGetValue(id, out float since)) { ColdSince[id] = Time.time; return false; }
            return Time.time - since >= Plugin.ColdEggWait.Value * 60f;
        }

        private static void Prune()
        {
            var live = new HashSet<ZDOID>(Instances().Where(d => d != null).Select(Id));
            foreach (var dict in new[] { FirstSeen, ColdSince, NoHomeUntil })
                foreach (ZDOID gone in dict.Keys.Where(k => !live.Contains(k)).ToList()) dict.Remove(gone);
        }

        internal static void Forget()
        {
            FirstSeen.Clear();
            ColdSince.Clear();
            NoHomeUntil.Clear();
            Claimed.Clear();
        }
    }
}
