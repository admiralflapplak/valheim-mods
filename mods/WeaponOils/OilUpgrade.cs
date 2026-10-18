using HarmonyLib;

namespace WeaponOils
{
    /// <summary>
    /// Upgrading a weapon at a station takes the old one out of the bag and adds a new one at its place, with none of the old one's data:
    /// the oil on it would be lost. The old weapon is remembered before (with its slot), and whatever weapon of the same kind is in that
    /// slot after gets its oil, the time left unchanged. An upgrade that breaks the weapon (the upgrader station) leaves nothing to carry to.
    /// </summary>
    internal static class OilUpgrade
    {
        private static ItemDrop.ItemData _from;
        private static Vector2i _slot;

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class InventoryGui_DoCrafting
        {
            private static void Prefix(ItemDrop.ItemData ___m_craftUpgradeItem)
            {
                _from = ___m_craftUpgradeItem != null && OilState.HasOil(___m_craftUpgradeItem) ? ___m_craftUpgradeItem : null;
                if (_from != null) _slot = _from.m_gridPos;
            }

            private static void Postfix(Player player)
            {
                ItemDrop.ItemData from = _from;
                _from = null;
                if (from == null || player == null) return;
                ItemDrop.ItemData now = player.GetInventory().GetItemAt(_slot.x, _slot.y);
                if (now == null || ReferenceEquals(now, from) || now.m_shared.m_name != from.m_shared.m_name) return;   // (not upgraded, or broken)
                OilState.Carry(from, now);
            }
        }
    }
}
