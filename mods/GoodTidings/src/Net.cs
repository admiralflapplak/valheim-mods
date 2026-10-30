using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The mod's routed RPCs (sent through the server to everyone): registered on each new ZRoutedRpc (it's made anew for every world:
    /// ZNet.Awake), freed again on unload so a hot-reloaded copy can take the names (the game throws when one is registered twice).
    /// </summary>
    internal static class Net
    {
        internal const string Bountiful = "GoodTidings_Bountiful";
        private static readonly string[] All = { Bountiful, Tempered.Rpc };
        private static ZRoutedRpc _registeredOn;

        /// <summary>
        /// A message on every player's screen, as MessageHud.MessageAll sends it (its own routed "ShowMessage"), but without needing a
        /// MessageHud: a dedicated server has none, and MessageHud.instance is null there.
        /// </summary>
        internal static void MessageAll(MessageHud.MessageType type, string text) =>
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)type, text);

        internal static void Register()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredOn, ZRoutedRpc.instance)) return;
            _registeredOn = ZRoutedRpc.instance;
            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(Bountiful, BountifulSeason.OnRpc);
                ZRoutedRpc.instance.Register<Vector3>(Tempered.Rpc, Tempered.OnTemper);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't set up networking ({e.Message})."); }
        }

        internal static void Unregister()
        {
            try
            {
                if (_registeredOn is ZRoutedRpc rpc && AccessTools.Field(typeof(ZRoutedRpc), "m_functions")?.GetValue(rpc) is IDictionary table)
                    foreach (string name in All) table.Remove(name.GetStableHashCode());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't let go of the network ({e.Message})."); }
            _registeredOn = null;
        }

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake
        {
            private static void Postfix() => Register();
        }
    }
}
