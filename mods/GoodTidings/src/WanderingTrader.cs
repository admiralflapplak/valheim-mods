using System;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// A travelling trader rides in on her golden Lox, wanders round the base for a few minutes with her shop open to anyone who asks,
    /// then moves on. Server only: makes her object (TraderPrefab), which every game then dresses and runs (TraderVisitor, TraderAI).
    /// </summary>
    internal sealed class WanderingTrader : Visit
    {
        public override string Id => "trader";
        public override bool Enabled => Plugin.TraderEnabled.Value;

        public override bool Start(Vector3 basePoint)
        {
            if (TraderVisitor.Count > 0) { Plugin.Debug("A wandering trader is already here"); return false; }
            GameObject prefab = ZNetScene.instance.GetPrefab(TraderPrefab.Name);
            if (prefab == null) return false;
            if (!FindArrival(basePoint, out Vector3 at)) { Plugin.Debug($"No dry ground to arrive on near {basePoint}"); return false; }
            Spawn(prefab, at, basePoint);
            // Sent as a key: each player's game shows it in their own language.
            MessageHud.instance.MessageAll(MessageHud.MessageType.Center, "$goodtidings_trader_arrives");
            return true;
        }

        /// <summary>Makes her at <paramref name="at"/>, facing and wandering round <paramref name="home"/>; hooded or bare at random.</summary>
        internal static GameObject Spawn(GameObject prefab, Vector3 at, Vector3 home)
        {
            Vector3 face = home - at;
            face.y = 0f;
            GameObject go = UnityEngine.Object.Instantiate(prefab, at, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
            ZDO zdo = go.GetComponent<ZNetView>().GetZDO();
            zdo.Set(TraderAI.LeaveAtKey, ZNet.instance.GetTime().Ticks + TimeSpan.FromMinutes(Plugin.TraderMinutes.Value).Ticks);
            zdo.Set(TraderAI.HoodedKey, UnityEngine.Random.value < 0.5f);
            go.GetComponent<TraderAI>().SetHome(home);
            Plugin.Log.LogInfo($"Wandering trader arrives at {at}, heading for {home}, staying {Plugin.TraderMinutes.Value} minutes");
            return go;
        }

        private static int _blockMask;

        /// <summary>
        /// A spot on open, dry ground 35-45 m from the base, to ride in from: the solid surface there is the ground itself (not a roof or
        /// a floor: she once arrived inside a Draugr house), and nothing stands in the room a Lox takes up.
        /// </summary>
        private static bool FindArrival(Vector3 home, out Vector3 at)
        {
            if (_blockMask == 0) _blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle", "character");
            for (int i = 0; i < 40; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f), d = UnityEngine.Random.Range(35f, 45f);
                Vector3 p = home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h, 1000)) continue;
                if (h < ZoneSystem.instance.m_waterLevel + 0.5f) continue;
                if (Mathf.Abs(h - ZoneSystem.instance.GetGroundHeight(p)) > 0.3f) continue;   // standing on something built, not the ground
                Vector3 feet = new Vector3(p.x, h, p.z);
                if (Physics.CheckCapsule(feet + Vector3.up * 2.4f + Vector3.forward * 1.5f, feet + Vector3.up * 2.4f - Vector3.forward * 1.5f,
                        2f, _blockMask, QueryTriggerInteraction.Ignore)) continue;   // a building, a rock or a tree in the way
                at = feet + Vector3.up * 0.5f;
                return true;
            }
            at = Vector3.zero;
            return false;
        }
    }
}
