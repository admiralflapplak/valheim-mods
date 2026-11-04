using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Breaking down and being rebuilt (step 8). A Cogling taken down with the hammer gives his cost back, as any piece. Broken any other
    /// way (monsters, a fall, a player in PvP) he gives nothing: the nearest antenna of his network rebuilds him instead after a while
    /// ("Rebuild minutes"), in front of it, powered down; he boots up. Without an antenna near he gives his cost back, so nothing is lost.
    /// The rebuilds waiting are kept on the antenna (its saved data: when, and who built him), so they survive restarts; the antenna's
    /// owner adds them (an RPC from the Cogling's owner) and carries them out.
    /// </summary>
    internal static class Rebuilds
    {
        internal const string Key = "cogling_rebuilds", AddRpc = "cogling_rebuild_add";

        /// <summary>Testing (`cogling break`): the next breakdown is rebuilt after this many seconds instead of Rebuild minutes (0: off).</summary>
        internal static float QuickSeconds;

        /// <summary>The rebuilds an antenna has waiting: when each is due (world ticks), and who built that Cogling.</summary>
        internal static List<(long due, long creator)> Pending(Antenna a)
        {
            var list = new List<(long, long)>();
            string s = a.GetComponent<ZNetView>()?.GetZDO()?.GetString(Key) ?? "";
            foreach (string part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 2 && long.TryParse(f[0], out long due) && long.TryParse(f[1], out long creator)) list.Add((due, creator));
            }
            return list;
        }

        private static void Save(ZDO zdo, List<(long due, long creator)> list) =>
            zdo.Set(Key, string.Join(";", list.Select(r => $"{r.due}:{r.creator}")));

        /// <summary>On the antenna's owner: one more rebuild (registered on every antenna as an RPC).</summary>
        internal static void RPC_Add(Antenna a, long due, long creator)
        {
            ZNetView view = a.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            List<(long, long)> list = Pending(a);
            list.Add((due, creator));
            Save(view.GetZDO(), list);
        }

        /// <summary>On the antenna's owner, now and then: the rebuilds that are due, carried out.</summary>
        internal static void Tick(Antenna a)
        {
            ZNetView view = a.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner() || ZNet.instance == null || CoglingPrefab.Prefab == null) return;
            List<(long due, long creator)> list = Pending(a);
            if (list.Count == 0) return;
            long now = ZNet.instance.GetTime().Ticks;
            var due = list.Where(r => r.due <= now).ToList();
            if (due.Count == 0) return;
            foreach (var r in due)
            {
                list.Remove(r);
                Vector3 at = a.transform.position + a.transform.forward * 1.3f;
                if (ZoneSystem.instance.GetSolidHeight(at, out float height)) at.y = height;
                GameObject cog = UnityEngine.Object.Instantiate(CoglingPrefab.Prefab, at, Quaternion.LookRotation(a.transform.forward));
                ZDO zdo = cog.GetComponent<ZNetView>()?.GetZDO();
                if (zdo != null) { zdo.Set(ZDOVars.s_creator, r.creator); zdo.Set(Carried.AwakeKey, false); }   // (his builder's again; powered down: he boots up)
                ZNetScene.instance.GetPrefab("vfx_Place_wood_pole")?.Let(fx => UnityEngine.Object.Instantiate(fx, at, Quaternion.identity));
                Plugin.Log.LogInfo($"Cogling rebuilt at the antenna at {a.transform.position:F1}");
                if (Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, at) < 30f)
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "$cogling_rebuilt");
            }
            Save(view.GetZDO(), list);
        }

        private static void Let<T>(this T value, Action<T> act) where T : class { if (value != null) act(value); }
    }

    /// <summary>A Cogling's cost: given back only when he's taken down with the hammer (its removal hit); broken otherwise, rebuilt.</summary>
    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    internal static class CoglingRefund
    {
        private static bool Prefix(Piece __instance, HitData hitData)
        {
            if (__instance == null || __instance.gameObject.name.Replace("(Clone)", "") != CoglingPrefab.Name) return true;
            bool hammer = hitData == null || hitData.GetTotalDamage() >= 1e9f;   // (Player.RemovePiece: new HitData(1e10))
            if (hammer) return true;
            List<Antenna> network = Antenna.NetworkAt(__instance.transform.position);
            Antenna nearest = network.Where(a => a != null).OrderBy(a => Vector3.Distance(a.transform.position, __instance.transform.position)).FirstOrDefault();
            if (nearest == null) return true;   // (no antenna to rebuild him: his cost, so nothing is lost)
            // (`cogling break`, testing: this breakdown's rebuild in a few seconds)
            double seconds = Rebuilds.QuickSeconds > 0f ? Rebuilds.QuickSeconds : Plugin.RebuildMinutes.Value * 60.0;
            Rebuilds.QuickSeconds = 0f;
            long due = ZNet.instance.GetTime().AddSeconds(seconds).Ticks;
            nearest.GetComponent<ZNetView>().InvokeRPC(Rebuilds.AddRpc, due, __instance.GetCreator());
            Plugin.Log.LogInfo($"Cogling broke down: the antenna at {nearest.transform.position:F1} rebuilds him in {seconds:F0} s");
            return false;
        }
    }
}
