using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

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
        internal static readonly List<Visit> All = new List<Visit> { new WanderingTrader(), new BountifulSeason(), new MuninnVisit(), new DvergerVisit() };

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

        /// <summary>
        /// Where players are standing at a base, by the game's own measure and from the same data its raids use
        /// (RandEventSystem.RefreshPlayerEventData): each connected player's game sends the server its "baseValue", and the server knows
        /// where each player is (their reference position). Works the same on a dedicated server (no player of its own) and for a host
        /// (their own data too).
        /// </summary>
        private static List<Vector3> BasePoints()
        {
            var points = new List<Vector3>();
            ZNet net = ZNet.instance;
            if (net == null) return points;
            void Add(Dictionary<string, string> data, Vector3 at)
            {
                if (data != null && data.TryGetValue("baseValue", out string v) && int.TryParse(v, out int value) && value >= 3) points.Add(at);
            }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Add(net.m_serverSyncedPlayerData, net.GetReferencePosition());   // (the host)
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer.IsReady()) Add(peer.m_serverSyncedPlayerData, peer.m_refPos);
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
            // one of ours is going on (Dagny's at a base, or a bountiful season): no raid in the middle of it, this roll is skipped
            if (TraderVisitor.Count > 0 || BountifulSeason.Running || MuninnVisitor.Count > 0 || DvergerVisitor.Count > 0) { Plugin.Debug("A visit is going on: no raid now"); return false; }
            if (Random.Range(0f, 100f) >= Plugin.VisitChance.Value) return true;
            return !Visits.TryStart();   // no visit possible: the raid goes ahead
        }
    }
}
