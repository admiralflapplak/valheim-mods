using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Repairing (step 6), when there's nothing to fetch or tidy. Buildings: a damaged piece in his area (below "Repair below" percent,
    /// built by a player, where this game's player may build: the hammer's own rule), repaired as the hammer does (WearNTear.Repair,
    /// free), the most damaged first. Gear: a worn tool or piece of gear in a chest of his area, taken to its crafting station when that
    /// station stands in his area at the level the game needs (InventoryGui.CanRepair's rule), repaired there, and put back.
    /// </summary>
    internal static class Repairs
    {
        private static readonly AccessTools.FieldRef<List<WearNTear>> Pieces = AccessTools.StaticFieldRefAccess<List<WearNTear>>(AccessTools.Field(typeof(WearNTear), "s_allInstances"));
        private static readonly AccessTools.FieldRef<List<CraftingStation>> Stations = AccessTools.StaticFieldRefAccess<List<CraftingStation>>(AccessTools.Field(typeof(CraftingStation), "m_allStations"));
        private static readonly Dictionary<ZDOID, float> Skip = new Dictionary<ZDOID, float>();   // pieces he couldn't reach: left a while
        internal static readonly HashSet<ZDOID> Claimed = new HashSet<ZDOID>();   // pieces a Cogling of this game is on his way to mend (two never race)

        internal static ZDOID Id(Component c) => c != null ? c.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None : ZDOID.None;

        internal static void SkipFor(WearNTear w, float seconds)
        {
            ZDOID id = w.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;
            if (id != ZDOID.None) Skip[id] = Time.time + seconds;
        }

        /// <summary>The damaged building piece to repair next, the most damaged first (ties: the nearer), or null.</summary>
        internal static WearNTear NextPiece(List<Antenna> network, Vector3 from)
        {
            if (!Plugin.RepairBuildings.Value) return null;
            float below = Plugin.RepairBelow.Value / 100f;
            WearNTear best = null;
            float bestHealth = below, bestDistance = float.MaxValue;
            foreach (WearNTear w in Pieces())
            {
                if (w == null || !Antenna.InArea(network, w.transform.position)) continue;
                float health = w.GetHealthPercentage();
                if (health >= below || health > bestHealth + 0.0001f) continue;
                ZNetView view = w.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                if (Skip.TryGetValue(view.GetZDO().m_uid, out float until) && Time.time < until) continue;
                if (Claimed.Contains(view.GetZDO().m_uid)) continue;
                Piece piece = w.GetComponent<Piece>();
                if (piece == null || !piece.IsPlacedByPlayer() || CoglingStance.IsCogling(w.GetComponent<Character>())) continue;
                if (!PrivateArea.CheckAccess(w.transform.position, 0f, flash: false)) continue;
                float d = Vector3.Distance(from, w.transform.position);
                if (health < bestHealth - 0.0001f || d < bestDistance) { best = w; bestHealth = health; bestDistance = d; }
            }
            return best;
        }

        /// <summary>The point of a piece nearest a spot (its colliders'), to walk to and hammer at.</summary>
        internal static Vector3 NearestPoint(WearNTear w, Vector3 from)
        {
            Vector3 best = w.transform.position;
            float bestDistance = float.MaxValue;
            foreach (Collider c in w.GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Vector3 p = c is MeshCollider mc && !mc.convex ? c.bounds.ClosestPoint(from) : c.ClosestPoint(from);
                float d = Vector3.Distance(p, from);
                if (d < bestDistance) { best = p; bestDistance = d; }
            }
            return best;
        }

        /// <summary>A worn item in one of these chests whose station stands in the area (at the level it needs): the item and that station.</summary>
        internal static (Container chest, ItemDrop.ItemData item, CraftingStation station) NextGear(List<Antenna> network, List<Container> chests)
        {
            if (!Plugin.RepairGear.Value || ObjectDB.instance == null) return (null, null, null);
            List<CraftingStation> stations = Stations().Where(s => s != null && Antenna.InArea(network, s.transform.position)).ToList();
            if (stations.Count == 0) return (null, null, null);
            foreach (Container c in chests)
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                {
                    if (!item.m_shared.m_useDurability || !item.m_shared.m_canBeReparied || item.m_durability >= item.GetMaxDurability() - 0.01f) continue;
                    CraftingStation station = StationFor(item, stations);
                    if (station != null) return (c, item, station);
                }
            return (null, null, null);
        }

        /// <summary>The station an item is repaired at (its recipe's repair or crafting station, by name, at the level it needs), or null.</summary>
        private static CraftingStation StationFor(ItemDrop.ItemData item, List<CraftingStation> stations)
        {
            Recipe recipe = ObjectDB.instance.GetRecipe(item);
            if (recipe == null) return null;
            foreach (CraftingStation s in stations)
            {
                bool right = (recipe.m_repairStation != null && recipe.m_repairStation.m_name == s.m_name) || (recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == s.m_name);
                if (right && Mathf.Min(s.GetLevel(), 4) >= recipe.m_minStationLevel) return s;
            }
            return null;
        }

        internal static void Forget()
        {
            Skip.Clear();
            Claimed.Clear();
        }
    }
}
