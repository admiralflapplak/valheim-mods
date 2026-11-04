using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Keeping things going (CoglingJob's tending): fires and torches burning low get fuel from the chests (Tend fires), and smelters,
    /// kilns and furnaces that are working get more of what they're working on, and fuel (Tend smelters). A Cogling keeps a station
    /// going; it never starts one (a kiln fed on its own would turn all the building wood into coal): the player decides what a station
    /// is for by starting it. Their finished bars and coal fall on the floor, and the floor job takes them to the chests.
    /// </summary>
    internal static class Tending
    {
        /// <summary>One station to feed: what it takes (a prefab name), how many it can take now, and whether it's fuel or ore.</summary>
        internal sealed class Feed
        {
            internal MonoBehaviour Station;
            internal ZNetView View;
            internal string Item;
            internal int Wants;
            internal bool Ore;
            internal Container Chest;
            internal ItemDrop.ItemData Stack;   // (in that chest)
            internal string Name => Station != null ? Station.GetComponent<Piece>()?.m_name ?? "" : "";
        }

        /// <summary>Stations a Cogling is on his way to feed (two never feed the same one).</summary>
        internal static readonly HashSet<int> Claimed = new HashSet<int>();

        /// <summary>The nearest station in the area that needs feeding and has what it takes in a chest of the area, or null.</summary>
        internal static Feed Next(List<Antenna> network, Vector3 from, List<Container> chests)
        {
            var wants = new List<Feed>();
            if (Plugin.TendFires.Value)
                foreach (Fireplace f in Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
                {
                    ZNetView v = f != null ? f.GetComponent<ZNetView>() : null;
                    if (v == null || !v.IsValid() || f.m_infiniteFuel || f.m_fuelItem == null || !Antenna.InArea(network, f.transform.position)) continue;
                    float fuel = v.GetZDO().GetFloat(ZDOVars.s_fuel);
                    // (burning, and below half: one let go out stays out)
                    if (fuel <= 0f || !f.IsBurning() || fuel >= f.m_maxFuel * 0.5f) continue;
                    wants.Add(new Feed { Station = f, View = v, Item = f.m_fuelItem.name, Wants = Mathf.FloorToInt(f.m_maxFuel - fuel) });
                }
            Smelter[] smelters = Plugin.TendSmelters.Value ? Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
                .Where(s => s != null && s.GetComponent<ZNetView>()?.IsValid() == true && Antenna.InArea(network, s.transform.position)).ToArray() : new Smelter[0];
            // (fuel the working ones burn: a kiln making it is fed only while one of them works, or a kiln once started went on for ever)
            var burning = new HashSet<string>(smelters.Where(s => s.m_fuelItem != null && s.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_queued) > 0)
                .Select(s => s.m_fuelItem.name));
            var fuels = new HashSet<string>(smelters.Where(s => s.m_fuelItem != null).Select(s => s.m_fuelItem.name));
            foreach (Smelter s in smelters)
                {
                    ZNetView v = s.GetComponent<ZNetView>();
                    ZDO zdo = v.GetZDO();
                    int queued = zdo.GetInt(ZDOVars.s_queued);
                    if (queued <= 0) continue;   // (not working: the player starts it)
                    // a kiln (it makes a fuel): fed only while a station burning that fuel works and the chests have none of it left
                    bool makesFuel = s.m_conversion.Any(c => c?.m_to != null && (fuels.Contains(c.m_to.name) || c.m_to.name == "Coal"));
                    if (makesFuel)
                    {
                        string[] made = s.m_conversion.Where(c => c?.m_to != null).Select(c => c.m_to.name).Distinct().ToArray();
                        if (!made.Any(burning.Contains)) continue;
                        if (chests.Any(c => c.GetInventory()?.GetAllItems().Any(i => i.m_dropPrefab != null && made.Contains(i.m_dropPrefab.name)) == true)) continue;
                    }
                    if (s.m_fuelItem != null)
                    {
                        float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                        if (fuel < s.m_maxFuel - 1) wants.Add(new Feed { Station = s, View = v, Item = s.m_fuelItem.name, Wants = Mathf.FloorToInt(s.m_maxFuel - fuel) });
                    }
                    if (queued < s.m_maxOre)
                    {
                        // more of what it's working on (the most of it in its queue)
                        string ore = Enumerable.Range(0, queued).Select(i => zdo.GetString("item" + i)).Where(n => n.Length > 0)
                            .GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                        if (ore != null) wants.Add(new Feed { Station = s, View = v, Item = ore, Wants = s.m_maxOre - queued, Ore = true });
                    }
                }
            foreach (Feed f in wants.OrderBy(w => Vector3.Distance(from, w.Station.transform.position)))
            {
                if (Claimed.Contains(f.Station.GetInstanceID())) continue;
                // the chest nearest the station with it (the biggest stack there)
                foreach (Container c in chests.OrderBy(c => Vector3.Distance(c.transform.position, f.Station.transform.position)))
                {
                    ItemDrop.ItemData stack = c.GetInventory()?.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == f.Item)
                        .OrderByDescending(i => i.m_stack).FirstOrDefault();
                    if (stack == null) continue;
                    f.Chest = c;
                    f.Stack = stack;
                    return f;
                }
            }
            return null;
        }

        /// <summary>The station's count of what he feeds it (fuel, or ore queued): it goes up when it takes one.</summary>
        internal static float Level(Feed f)
        {
            if (f?.View == null || !f.View.IsValid()) return -1f;
            return f.Ore ? f.View.GetZDO().GetInt(ZDOVars.s_queued) : f.View.GetZDO().GetFloat(ZDOVars.s_fuel);
        }

        /// <summary>It can take one more now.</summary>
        internal static bool Room(Feed f)
        {
            float level = Level(f);
            if (level < 0f) return false;
            if (f.Station is Fireplace fire) return Mathf.CeilToInt(level) < fire.m_maxFuel;
            if (f.Station is Smelter s) return f.Ore ? level < s.m_maxOre : level <= s.m_maxFuel - 1;
            return false;
        }

        /// <summary>One into it (the game's own way: an RPC to its owner, who adds it if there's room).</summary>
        internal static void Add(Feed f)
        {
            if (f.Ore) f.View.InvokeRPC("RPC_AddOre", f.Item, false);
            else f.View.InvokeRPC("RPC_AddFuel");
        }

        internal static void Forget() => Claimed.Clear();
    }
}
