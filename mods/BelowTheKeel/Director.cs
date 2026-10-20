using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Decides when an omen comes. No messages between players: time is cut into slots, and whether a slot brings an omen (and which, and
    /// its seed) is a hash of the ship's id and the slot number on the server's clock. Everyone aboard the same ship with the same
    /// settings (the server's, see Plugin) works out the same answer on their own, so the crew sees the same thing at the same moment,
    /// even when the ship's owner or the server doesn't have the mod.
    ///
    /// The fail-safe: bad luck (or omens due while the ship was near land) can leave a long stretch of open sea with none. The ship counts
    /// its time on the open sea since the last omen, in its own ZDO, written only by its owner (so everyone aboard reads the same count,
    /// however long they've been aboard). Past the limit, the next slot out at sea brings one.
    /// </summary>
    internal static class Director
    {
        internal const double SlotSeconds = 30.0;
        internal const string OpenSeaKey = "BelowTheKeel.deepSeconds";   // seconds on the open sea since this ship's last omen

        private static long _lastSlot = long.MinValue;
        private static float _lastPlayed = float.NegativeInfinity;      // (Time.time) this game's last omen: the fail-safe never fires twice
                                                                         // while the owner's reset is still on its way to everyone

        internal static void Reset()
        {
            _lastSlot = long.MinValue;
            _lastPlayed = float.NegativeInfinity;
        }

        internal static void Update()
        {
            if (ZNet.instance == null || Player.m_localPlayer == null) return;

            Ship ship = Ship.GetLocalShip();
            if (ship == null) return;

            long slot = (long)(ZNet.instance.GetTimeSeconds() / SlotSeconds);
            if (slot == _lastSlot) return;
            bool firstLook = _lastSlot == long.MinValue;
            _lastSlot = slot;
            if (firstLook) return;   // just boarded or loaded: don't fire in the middle of a slot

            if (!Sea.IsDeep(ship.transform.position, out _)) return;

            ulong id = ShipId(ship);
            bool due = Fires(id, slot, out Omen omen, out uint seed), failSafe = false;
            if (!due && FailSafeDue(ship))
            {
                Pick(id, slot, out omen, out seed);
                due = failSafe = true;
            }
            if (due) omen = Playable(omen, ship);   // (none playable now: it counts as not come, the open-sea count goes on)

            CountOpenSea(ship, reset: omen != null && due);

            if (!due || omen == null) return;
            _lastPlayed = Time.time;
            if (!Plugin.Enabled.Value || !omen.IsEnabled) return;   // (this player's own choice: it still came for the crew)

            Plugin.Log.LogInfo($"Omen: {omen.Name}" + (failSafe ? " (fail-safe)" : ""));
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

            Pick(shipId, slot, out omen, out seed);
            return true;
        }

        /// <summary>Which omen a slot brings, and its seed: the same for everyone aboard.</summary>
        private static void Pick(ulong shipId, long slot, out Omen omen, out uint seed)
        {
            omen = Omens.All[Hash(shipId, slot, 2) % (uint)Omens.All.Length];
            seed = Hash(shipId, slot, 3);
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

        /// <summary>The ship's time on the open sea since its last omen, as its owner counted it (0 when the owner hasn't the mod).</summary>
        internal static float OpenSeaSeconds(Ship ship)
        {
            ZDO zdo = ShipZdo(ship);
            return zdo != null ? zdo.GetFloat(OpenSeaKey) : 0f;
        }

        private static bool FailSafeDue(Ship ship)
        {
            float limit = Plugin.FailSafeMinutes.Value * 60f;
            if (limit <= 0f) return false;
            if (Time.time - _lastPlayed < Plugin.MinutesApart.Value * 60f) return false;
            return OpenSeaSeconds(ship) >= limit;
        }

        /// <summary>The owner adds this slot's time on the open sea, or starts again from 0 when an omen came. Once a slot at most: the
        /// ship's ZDO is resent to everyone near whenever it changes, so it carries one small number and no more.</summary>
        private static void CountOpenSea(Ship ship, bool reset)
        {
            ZNetView nview = ship.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            ZDO zdo = nview.GetZDO();
            zdo.Set(OpenSeaKey, reset ? 0f : zdo.GetFloat(OpenSeaKey) + (float)SlotSeconds);   // (reset by writing 0: a removed key stays on other games)
        }

        private static bool RawHit(ulong shipId, long slot)
        {
            float slotsBetween = Plugin.MinutesBetween.Value * 60f / (float)SlotSeconds;
            float chance = 1f / slotsBetween;
            if (EnvMan.IsNight()) chance *= Plugin.NightFactor.Value;   // (night follows the server's clock too)
            return Hash(shipId, slot, 1) / (float)uint.MaxValue < chance;
        }

        private static ZDO ShipZdo(Ship ship)
        {
            ZNetView nview = ship.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        /// <summary>The ship's network id, the same on every machine.</summary>
        internal static ulong ShipId(Ship ship)
        {
            ZDO zdo = ShipZdo(ship);
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
