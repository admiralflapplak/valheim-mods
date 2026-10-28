using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// A travelling trader stops by the base for a few minutes, then moves on.
    /// Scaffold: announces the visit only. Next: spawn the trader (a clone of Haldor's prefab with our own stock through a
    /// Trader.GetAvailableItems postfix), its leave time saved in its ZDO so it still goes after a restart, and the server
    /// removing it once that time has passed.
    /// </summary>
    internal sealed class WanderingTrader : Visit
    {
        public override string Id => "trader";
        public override bool Enabled => Plugin.TraderEnabled.Value;

        public override bool Start(Vector3 basePoint)
        {
            // Sent as a key: each player's game shows it in their own language.
            MessageHud.instance.MessageAll(MessageHud.MessageType.Center, "$goodtidings_trader_arrives");
            Plugin.Log.LogInfo($"Wandering trader would arrive near {basePoint} for {Plugin.TraderMinutes.Value} minutes (not spawned yet)");
            return true;
        }
    }
}
