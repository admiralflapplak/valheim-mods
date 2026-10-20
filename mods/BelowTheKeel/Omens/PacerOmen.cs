using BepInEx.Configuration;

namespace BelowTheKeel
{
    /// <summary>Jörmungandr, unnamed: a long dark shape directly beneath the ship, matching its speed and turns. However long you watch, there is always more body and never a head.</summary>
    internal sealed class PacerOmen : Omen
    {
        internal override string Name => "pacer";
        protected override ConfigEntry<bool> Setting => Plugin.Pacer;

        internal override void Play(Ship ship, uint seed)
        {
            Plugin.Log.LogInfo("pacer: not built yet");
        }
    }
}
