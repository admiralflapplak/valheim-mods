using System.Globalization;

namespace WeaponOils
{
    /// <summary>
    /// The oil on one weapon, kept on the weapon itself in ItemData.m_customData (our own prefixed keys only: the dictionary holds every
    /// mod's data). It runs out at a point in world time, so it needs no ticking and survives a logout.
    /// Upgrading a weapon makes a new item with no custom data: OilUpgrade carries these keys over (Carry). Weapons stack to 1, so oiled
    /// and plain ones never merge.
    /// </summary>
    internal static class OilState
    {
        private const string IdKey = "WeaponOils.oil";
        private const string UntilKey = "WeaponOils.until";

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;

        public static void Apply(ItemDrop.ItemData weapon, OilDef oil, float seconds)
        {
            weapon.m_customData[IdKey] = oil.Id;
            weapon.m_customData[UntilKey] = (Now + seconds).ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Whether an oil is recorded on this item (working or not: Active clears an expired one).</summary>
        public static bool HasOil(ItemDrop.ItemData item) => item != null && item.m_customData != null && item.m_customData.ContainsKey(IdKey);

        /// <summary>The oil on one item put on another as it is, its time still running (for a weapon remade by an upgrade).</summary>
        public static void Carry(ItemDrop.ItemData from, ItemDrop.ItemData to)
        {
            if (from == null || to == null || from.m_customData == null || to.m_customData == null) return;
            foreach (string key in new[] { IdKey, UntilKey })
                if (from.m_customData.TryGetValue(key, out string value)) to.m_customData[key] = value;
        }

        public static void Clear(ItemDrop.ItemData weapon)
        {
            weapon.m_customData.Remove(IdKey);
            weapon.m_customData.Remove(UntilKey);
        }

        /// <summary>
        /// Whether an oil may go on this weapon: a melee weapon with no oil working on it. A second oil is refused, not swapped, so a
        /// mistake never wastes either (the player waits for the first to wear off), unless the server's setting allows replacing one.
        /// </summary>
        public static bool CanApply(ItemDrop.ItemData weapon, out string refusal)
        {
            refusal = null;
            if (!CanOil(weapon)) { refusal = "$nick_weaponoils_not_melee"; return false; }
            if (Active(weapon, out _) != null && !Plugin.AllowReplace.Value) { refusal = "$nick_weaponoils_already_oiled"; return false; }
            return true;
        }

        /// <summary>The oil still working on this weapon, or null (an expired one is cleared).</summary>
        public static OilDef Active(ItemDrop.ItemData weapon, out float secondsLeft)
        {
            secondsLeft = 0;
            if (OilHands.IsHands(weapon))
            {
                if (weapon.m_customData.ContainsKey(IdKey)) Clear(weapon);   // (oil put on the shared empty-hand item by an earlier version)
                return OilHands.Active(Player.m_localPlayer, out secondsLeft);   // (only the local player's own blows are worked out here)
            }
            if (weapon == null || !weapon.m_customData.TryGetValue(IdKey, out string id)) return null;
            if (!weapon.m_customData.TryGetValue(UntilKey, out string until)
                || !double.TryParse(until, NumberStyles.Float, CultureInfo.InvariantCulture, out double end)
                || end <= Now)
            {
                Clear(weapon);
                return null;
            }
            secondsLeft = (float)(end - Now);
            return OilDefs.Get(id);   // an oil this version doesn't know (mod updated, oil removed) stays in the data, not wiped
        }

        /// <summary>
        /// Oil goes on melee weapons only: ones that do damage (the Spyglass is built on a club, and is no weapon). Bare hands too (the
        /// game's "weapon" for an empty hand): that oil is kept on the player (OilHands). Staffs are two-handed weapons that do damage
        /// too, so a weapon whose main attack shoots something is refused; spears stab first and throw second, so they stay oilable.
        /// </summary>
        public static bool CanOil(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared.m_damages.GetTotalDamage() <= 0f) return false;
            if (OilHands.IsHands(item)) return true;
            var t = item.m_shared.m_itemType;
            if (t != ItemDrop.ItemData.ItemType.OneHandedWeapon && t != ItemDrop.ItemData.ItemType.TwoHandedWeapon
                && t != ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft) return false;
            var skill = item.m_shared.m_skillType;
            if (skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows
                || skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic) return false;
            var attack = item.m_shared.m_attack;
            return attack == null || (attack.m_attackType != Attack.AttackType.Projectile
                && attack.m_attackType != Attack.AttackType.TriggerProjectile);
        }
    }
}
