using BepInEx.Configuration;

namespace BelowTheKeel
{
    /// <summary>A vast eye opens far below the ship, its pupil following whoever looks; it blinks once and sinks away.</summary>
    internal sealed class EyeOmen : Omen
    {
        internal override string Name => "eye";
        protected override ConfigEntry<bool> Setting => Plugin.Eye;

        internal override void Play(Ship ship, uint seed)
        {
            Plugin.Log.LogInfo("eye: not built yet");
        }
    }
}
