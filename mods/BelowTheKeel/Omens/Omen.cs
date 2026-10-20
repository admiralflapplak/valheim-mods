using BepInEx.Configuration;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// One kind of omen. Play gets the ship and a seed: every choice it makes (where, which side, how long) must come from the seed
    /// (new System.Random((int)seed)), never from UnityEngine.Random, so everyone aboard sees the same thing.
    /// </summary>
    internal abstract class Omen
    {
        internal abstract string Name { get; }      // also what "keel play <name>" takes
        protected abstract ConfigEntry<bool> Setting { get; }

        internal bool IsEnabled => Setting.Value;

        internal abstract void Play(Ship ship, uint seed);
    }

    internal static class Omens
    {
        // The order is part of the schedule (Director picks by index): add new ones at the end, never reorder.
        internal static readonly Omen[] All = { new EyeOmen(), new PacerOmen(), new HalfBoatOmen(), new RansNetOmen() };

        internal static Omen Find(string name)
        {
            foreach (Omen o in All)
                if (string.Equals(o.Name, name, System.StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }
    }
}
