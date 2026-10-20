using BepInEx.Configuration;

namespace BelowTheKeel
{
    /// <summary>The sea draugr of Norwegian folklore: a drowned man rowing half a broken karve through the fog, paying the ship no mind. An omen that someone aboard will drown.</summary>
    internal sealed class HalfBoatOmen : Omen
    {
        internal override string Name => "halfboat";
        protected override ConfigEntry<bool> Setting => Plugin.HalfBoat;

        internal override void Play(Ship ship, uint seed)
        {
            Plugin.Log.LogInfo("halfboat: not built yet");
        }
    }
}
