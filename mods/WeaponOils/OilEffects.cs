using HarmonyLib;
using UnityEngine;

namespace WeaponOils
{
    /// <summary>
    /// What the oils that change how a weapon behaves do (the damage-type oils are in OilApply). Each patch looks at the weapon in play
    /// and acts only when it carries that oil: cheaper swings, slower wear, a share of the damage dealt coming back as health, harder
    /// stagger, a firmer block (Barkhide's harder chopping is a damage type, in OilApply). Every one runs on the game of the player who swings or blocks, as the game's own rules do (the attacker's
    /// game builds the hit, the blocker's game decides the block), so nothing here needs to be sent to anyone.
    /// </summary>
    internal static class OilEffects
    {
        /// <summary>The oil's amount (scaled by the server's setting) if this weapon carries an oil with this effect, else 0.</summary>
        private static float Amount(ItemDrop.ItemData weapon, OilEffect effect)
        {
            if (weapon == null) return 0f;
            OilDef oil = OilState.Active(weapon, out _);
            return oil != null ? oil.AmountOf(effect) * Plugin.StrengthScale.Value : 0f;
        }

        // ---- Troll Fat: swings cost less stamina -----------------------------------------------------------------

        [HarmonyPatch(typeof(Attack), "GetAttackStamina")]
        private static class Attack_GetAttackStamina
        {
            private static void Postfix(Attack __instance, ref float __result)
            {
                float less = Amount(__instance.GetWeapon(), OilEffect.StaminaSaving);
                if (less > 0f) __result *= 1f - Mathf.Clamp01(less);
            }
        }

        // ---- Barkhide: the weapon wears slower -------------------------------------------------------------------
        // The game takes durability off inside its melee attacks with a plain subtraction, so what the swing cost is measured and a share
        // of it given back afterwards.

        private static class Wear
        {
            internal static void Before(Attack attack, out float durability) =>
                durability = attack.GetWeapon() != null ? attack.GetWeapon().m_durability : 0f;

            internal static void After(Attack attack, float before)
            {
                ItemDrop.ItemData weapon = attack.GetWeapon();
                if (weapon == null) return;
                float lost = before - weapon.m_durability;
                float share = Amount(weapon, OilEffect.Durability);
                if (lost > 0f && share > 0f) weapon.m_durability += lost * Mathf.Clamp01(share);
            }
        }

        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        private static class Attack_DoMeleeAttack
        {
            private static void Prefix(Attack __instance, out float __state) => Wear.Before(__instance, out __state);
            private static void Postfix(Attack __instance, float __state) => Wear.After(__instance, __state);
        }

        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        private static class Attack_DoAreaAttack
        {
            private static void Prefix(Attack __instance, out float __state) => Wear.Before(__instance, out __state);
            private static void Postfix(Attack __instance, float __state) => Wear.After(__instance, __state);
        }

        // ---- Leech and Lox Fat: life steal and stagger -----------------------------------------------------------
        // Character.Damage is where the attacker's game hands a hit to the one it hit. A hit by the local player's own melee blow, with
        // the weapon in their hand carrying the oil, is changed or paid for here.

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class Character_Damage
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                Player player = Player.m_localPlayer;
                if (player == null || hit == null || hit.m_ranged || hit.m_hitType != HitData.HitType.PlayerHit) return;
                if (__instance == null || __instance == player || hit.m_attacker != player.GetZDOID()) return;   // only their own blows
                ItemDrop.ItemData weapon = player.GetCurrentWeapon();

                float stagger = Amount(weapon, OilEffect.Stagger);
                if (stagger > 0f) hit.m_staggerMultiplier *= 1f + stagger;

                if (Amount(weapon, OilEffect.Frost) > 0f) OilChill.Hit(__instance);   // Rime: a chill the game's frost damage is too small to give

                // The game's Burning and Lightning effects have several looks (burning: 0 orange, 1 blue, 2 green; lightning: 0, 1 red) and the
                // hit says which. A weapon with no hit variant (-1: a club that gains fire from an oil) makes the game spawn every look at once:
                // orange, blue and green together, glowing white. Fire and lightning oils ask for the ordinary look.
                if (hit.m_variant < 0 && (Amount(weapon, OilEffect.Fire) > 0f || Amount(weapon, OilEffect.Lightning) > 0f)) hit.m_variant = 0;

                float steal = Amount(weapon, OilEffect.LifeSteal);
                if (steal > 0f && !__instance.IsTamed())   // (nothing from the creatures a friend has tamed)
                {
                    float health = hit.GetTotalDamage() * steal;
                    if (health >= 0.5f) player.Heal(health, true);
                }
            }
        }

        // ---- Royal: a firmer guard -------------------------------------------------------------------------------
        // Most players block with a shield, not the weapon, so the oil on the weapon in hand strengthens whatever blocks: the weapon
        // itself, or the shield in the other hand (the game blocks with the left-hand shield when there is one).

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float))]
        private static class ItemData_GetBlockPower
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                float more = Amount(__instance, OilEffect.Guard);
                Player player = Player.m_localPlayer;
                if (more <= 0f && player != null && __instance.m_equipped && __instance.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield
                    && player.GetInventory().ContainsItem(__instance))   // (the player's own shield in use, not one in a chest or on another)
                    more = Amount(player.GetCurrentWeapon(), OilEffect.Guard);
                if (more > 0f) __result *= 1f + more;
            }
        }
    }
}
