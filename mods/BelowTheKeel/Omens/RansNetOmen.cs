using BepInEx.Configuration;

namespace BelowTheKeel
{
    /// <summary>Rán's net: pale drowned figures hanging still in the water far below, as if caught in something. There for a moment, then gone.</summary>
    internal sealed class RansNetOmen : Omen
    {
        internal override string Name => "ransnet";
        protected override ConfigEntry<bool> Setting => Plugin.RansNet;

        internal override void Play(Ship ship, uint seed)
        {
            Plugin.Log.LogInfo("ransnet: not built yet");
        }
    }
}
