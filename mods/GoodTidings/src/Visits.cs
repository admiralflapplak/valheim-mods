using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>One kind of good visit. Runs on the server only, at a player's base.</summary>
    internal abstract class Visit
    {
        public abstract string Id { get; }
        public abstract bool Enabled { get; }
        /// <summary>Start the visit at <paramref name="basePoint"/>; false if it can't happen there (the raid then goes ahead).</summary>
        public abstract bool Start(Vector3 basePoint);
    }

    internal static class Visits
    {
        internal static readonly List<Visit> All = new List<Visit> { new WanderingTrader() };

        /// <summary>Picks a base and an enabled visit and starts it. Server only.</summary>
        internal static bool TryStart(string onlyId = null)
        {
            List<Vector3> bases = BasePoints();
            if (bases.Count == 0) { Plugin.Debug("No player is at a base: no visit"); return false; }

            List<Visit> visits = All.Where(v => v.Enabled && (onlyId == null || v.Id == onlyId)).ToList();
            while (visits.Count > 0)
            {
                Visit visit = visits[Random.Range(0, visits.Count)];
                Vector3 at = bases[Random.Range(0, bases.Count)];
                if (visit.Start(at)) { Plugin.Debug($"Started visit {visit.Id} at {at}"); return true; }
                visits.Remove(visit);
            }
            return false;
        }

        /// <summary>Where players are standing at a base, by the game's own measure (the one raids that need a base use).</summary>
        private static List<Vector3> BasePoints()
        {
            var points = new List<Vector3>();
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && !p.IsDead() && p.GetBaseValue() >= 3) points.Add(p.transform.position);
            // TODO: on a dedicated server Player.GetAllPlayers is empty; read ZNet's peers' positions and base values
            // (RandEventSystem.RefreshPlayerEventData does it from each peer's ZDO).
            return points;
        }
    }

    /// <summary>
    /// The game's raid timer (RandEventSystem.UpdateRandomEvent) calls StartRandomEvent when its roll says "raid now". With the chance
    /// in the settings, a good visit happens instead, so visits share the raid timer and raids get rarer by as much.
    /// </summary>
    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.StartRandomEvent))]
    internal static class RandEventSystem_StartRandomEvent
    {
        private static bool Prefix()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer() || !Plugin.Enabled.Value) return true;
            if (RandEventSystem.HaveActiveEvent()) return true;
            if (Random.Range(0f, 100f) >= Plugin.VisitChance.Value) return true;
            return !Visits.TryStart();   // no visit possible: the raid goes ahead
        }
    }
}
