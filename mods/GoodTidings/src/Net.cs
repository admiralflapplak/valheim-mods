using System;
using System.Collections;
using HarmonyLib;

namespace GoodTidings
{
    /// <summary>
    /// The mod's routed RPCs (sent through the server to everyone): registered on each new ZRoutedRpc (it's made anew for every world:
    /// ZNet.Awake), freed again on unload so a hot-reloaded copy can take the names (the game throws when one is registered twice).
    /// </summary>
    internal static class Net
    {
        internal const string Bountiful = "GoodTidings_Bountiful";
        private static readonly string[] All = { Bountiful };
        private static ZRoutedRpc _registeredOn;

        internal static void Register()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredOn, ZRoutedRpc.instance)) return;
            _registeredOn = ZRoutedRpc.instance;
            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(Bountiful, BountifulSeason.OnRpc);
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
