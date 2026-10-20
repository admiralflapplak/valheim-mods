using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Decides when an omen comes. No messages between players: time is cut into slots, and whether a slot brings an omen (and which, and
    /// its seed) is a hash of the ship's id and the slot number on the server's clock. Everyone aboard the same ship with the same
    /// settings (the server's, see Plugin) works out the same answer on their own, so the crew sees the same thing at the same moment,
    /// even when the ship's owner or the server doesn't have the mod.
    /// </summary>
    internal static class Director
    {
        internal const double SlotSeconds = 30.0;

        private static long _lastSlot = long.MinValue;

        internal static void Reset() => _lastSlot = long.MinValue;

        internal static void Update()
        {
            if (ZNet.instance == null || Player.m_localPlayer == null || !Plugin.Enabled.Value) return;

            Ship ship = Ship.GetLocalShip();
            if (ship == null) return;

            long slot = (long)(ZNet.instance.GetTimeSeconds() / SlotSeconds);
            if (slot == _lastSlot) return;
            bool firstLook = _lastSlot == long.MinValue;
            _lastSlot = slot;
            if (firstLook) return;   // just boarded or loaded: don't fire in the middle of a slot

            if (!Sea.IsDeep(ship.transform.position, out _)) return;
            if (!Fires(ShipId(ship), slot, out Omen omen, out uint seed)) return;
            omen = Playable(omen, ship);
            if (omen == null || !omen.IsEnabled) return;

            Plugin.Log.LogInfo($"Omen: {omen.Name}");
            omen.Play(ship, seed);
        }

        /// <summary>
        /// A slot fires when its hash falls under the chance, and no slot within "least minutes apart" before it did. That rule only looks
        /// at raw hashes, never at earlier results, so every player gets the same answer however long they have been aboard.
        /// </summary>
        internal static bool Fires(ulong shipId, long slot, out Omen omen, out uint seed)
        {
            omen = null;
            seed = 0;
            if (!RawHit(shipId, slot)) return false;

            int apart = Mathf.CeilToInt(Plugin.MinutesApart.Value * 60f / (float)SlotSeconds);
            for (long s = slot - 1; s >= slot - apart; s--)
                if (RawHit(shipId, s)) return false;

            uint h = Hash(shipId, slot, 2);
            omen = Omens.All[h % (uint)Omens.All.Length];
            seed = Hash(shipId, slot, 3);
            return true;
        }

        /// <summary>The omen picked, or the next one along that can come now (the Half-Boat needs fog or night).</summary>
        internal static Omen Playable(Omen picked, Ship ship)
        {
            int start = System.Array.IndexOf(Omens.All, picked);
            for (int i = 0; i < Omens.All.Length; i++)
            {
                Omen o = Omens.All[(start + i) % Omens.All.Length];
                if (o.CanPlay(ship)) return o;
            }
            return null;
        }

        private static bool RawHit(ulong shipId, long slot)
        {
            float slotsBetween = Plugin.MinutesBetween.Value * 60f / (float)SlotSeconds;
            float chance = 1f / slotsBetween;
            if (EnvMan.IsNight()) chance *= Plugin.NightFactor.Value;   // (night follows the server's clock too)
            return Hash(shipId, slot, 1) / (float)uint.MaxValue < chance;
        }

        /// <summary>The ship's network id, the same on every machine.</summary>
        internal static ulong ShipId(Ship ship)
        {
            ZNetView nview = ship.GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return 0;
            return (ulong)zdo.m_uid.UserID * 0x9E3779B97F4A7C15UL ^ zdo.m_uid.ID;
        }

        /// <summary>A hash that is the same on every machine (string.GetHashCode and System.Random aren't guaranteed to be).</summary>
        internal static uint Hash(ulong a, long b, uint salt)
        {
            ulong x = a ^ ((ulong)b * 0xBF58476D1CE4E5B9UL) ^ ((ulong)salt * 0x94D049BB133111EBUL);
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (uint)x;
        }
    }
}
