using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using static Nick.Shared.Translations;   // T(key, args) (Shared/Translations.cs)

namespace WeaponOils
{
    /// <summary>
    /// Using an oil, and what an oiled weapon does. An oil is a consumable with no status effect: using it from the bag (or the hotbar)
    /// reaches Player.ConsumeItem, which is taken over for our own items only and puts the oil on the equipped melee weapon instead
    /// (refused, with nothing used up, when there is none or it is already oiled). The damage types go into the weapon's own damage
    /// (ItemData.GetDamage, so the tooltip shows them too, and the attacker's game works out the hit as it always does).
    /// </summary>
    internal static class OilApply
    {
        // ---- using an oil ----------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
        private static class Player_ConsumeItem
        {
            // Skips the game's method for our own items only; everything else goes on as it was (and as other mods' patches expect).
            private static bool Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __result)
            {
                OilDef oil = OilItems.OilOf(item);
                if (oil == null) return true;
                __result = false;
                try
                {
                    ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
                    if (weapon == null || !OilState.CanApply(weapon, out string refusal))
                    {
                        string why = weapon == null || !OilState.CanOil(weapon) ? "nick_weaponoils_not_melee" : "nick_weaponoils_already_oiled";
                        __instance.Message(MessageHud.MessageType.Center, T(why));
                        return false;
                    }
                    bool replacing = OilState.Active(weapon, out _) != null;   // (only gets here when the server allows it)
                    bool hands = OilHands.IsHands(weapon);
                    if (hands) OilHands.Apply(__instance, oil, Plugin.DurationMinutes.Value * 60f);
                    else OilState.Apply(weapon, oil, Plugin.DurationMinutes.Value * 60f);
                    inventory.RemoveOneItem(item);
                    __instance.Message(MessageHud.MessageType.Center, hands
                        ? T(replacing ? "nick_weaponoils_replaced_hands" : "nick_weaponoils_applied_hands", Name(item))
                        : T(replacing ? "nick_weaponoils_replaced" : "nick_weaponoils_applied", Name(item), Name(weapon)));
                    // The game is told the item was NOT consumed, on purpose: when it is told it was, Humanoid.UseItem goes on to play the eating
                    // animation and to hide the right-hand weapon for a few seconds with the pouch in its place, which fought the oiling animation
                    // (the eating arm showing through as it began and ended, and the weapon snapping back). The oil has been used up above.
                    if (hands || OilHands.IsFist(weapon)) OilAnimation.BeginPalms(__instance);   // (claws are rubbed like hands: there is no blade to wipe)
                    else OilAnimation.Begin(__instance, weapon);
                }
                catch (System.Exception e) { Plugin.Log.LogError("Putting oil on a weapon failed: " + e); }
                return false;
            }
        }

        private static string Name(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);

        // ---- the boss gate ---------------------------------------------------------------------------------------

        /// <summary>An oil base's recipe stays hidden until the boss before its biome has fallen (when the setting says so).</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Recipe), typeof(bool), typeof(int), typeof(int))]
        private static class Player_HaveRequirements
        {
            private static void Postfix(Recipe recipe, ref bool __result)
            {
                if (!__result || !Plugin.NeedBossKill.Value) return;
                OilDef oil = OilItems.OilOf(recipe);
                if (oil == null || string.IsNullOrEmpty(oil.GlobalKey)) return;
                if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(oil.GlobalKey)) __result = false;
            }
        }

        // ---- the damage ------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float))]
        private static class ItemData_GetDamage
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
            {
                OilDef oil = OilState.Active(__instance, out _);
                if (oil != null) AddDamage(ref __result, oil);
            }
        }

        /// <summary>The oil's damage types, each as a part of what the weapon already does (both of its effects, for an oil with two).</summary>
        private static void AddDamage(ref HitData.DamageTypes damage, OilDef oil)
        {
            float scale = Plugin.StrengthScale.Value;
            float basis = damage.GetTotalPhysicalDamage();   // (before any of it is changed below)
            damage.m_fire += basis * oil.AmountOf(OilEffect.Fire) * scale;
            damage.m_frost += basis * oil.AmountOf(OilEffect.Frost) * scale;
            damage.m_poison += basis * oil.AmountOf(OilEffect.Poison) * scale;
            damage.m_lightning += basis * oil.AmountOf(OilEffect.Lightning) * scale;
            damage.m_spirit += basis * oil.AmountOf(OilEffect.Spirit) * scale;
            damage.m_damage += basis * oil.AmountOf(OilEffect.Pure) * scale;   // untyped: no resistance applies to it
            float sharper = 1f + oil.AmountOf(OilEffect.Physical) * scale;
            damage.m_blunt *= sharper;
            damage.m_slash *= sharper;
            damage.m_pierce *= sharper;
            float gathering = 1f + oil.AmountOf(OilEffect.Gathering) * scale;
            damage.m_chop *= gathering;
            damage.m_pickaxe *= gathering;
        }

        // ---- the tooltip -----------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
        private static class ItemData_GetTooltip
        {
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                OilDef oil = OilState.Active(item, out float left);
                if (oil == null) return;
                string name = Localization.instance.Localize("$" + OilItems.OilPrefix + oil.Id);
                int minutes = Mathf.Max(1, Mathf.CeilToInt(left / 60f));
                if (oil.Has(OilEffect.StaminaSaving)) __result = LessStamina(__result, oil.AmountOf(OilEffect.StaminaSaving) * Plugin.StrengthScale.Value);
                __result += "\n" + T("nick_weaponoils_tooltip", name, T("nick_weaponoils_minutes", minutes));
            }
        }

        private static readonly Regex StaminaLine = new Regex(@"(\$item_staminause: <color=orange>)([^<]+)(</color>)", RegexOptions.Compiled);

        /// <summary>The tooltip's "stamina use" number, the weapon's own, with the oil's saving taken off it (as a swing now costs).</summary>
        private static string LessStamina(string tooltip, float less) =>
            StaminaLine.Replace(tooltip, m => float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.CurrentCulture, out float use)
                ? m.Groups[1].Value + (use * (1f - Mathf.Clamp01(less))).ToString("0.#", CultureInfo.CurrentCulture) + m.Groups[3].Value
                : m.Value);

        // ---- "worn off" ------------------------------------------------------------------------------------------

        private static ItemDrop.ItemData _watched;   // the weapon the player held, last time it had oil on
        private static float _next;

        /// <summary>Once a second: tells the player when the oil on the weapon they hold has run out (Active clears an expired oil).</summary>
        public static void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            Player player = Player.m_localPlayer;
            if (player == null) { _watched = null; _sentHands = null; System.Array.Clear(SentGlow, 0, SentGlow.Length); return; }
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            bool oiled = weapon != null && !OilHands.IsHands(weapon) && OilState.Active(weapon, out _) != null;   // (the hands' effect says when theirs wears off)
            if (!oiled && _watched != null && ReferenceEquals(_watched, weapon))
                player.Message(MessageHud.MessageType.Center, T("nick_weaponoils_worn_off", Name(weapon)));
            _watched = oiled ? weapon : null;
            TellOthers(player);
        }

        private static readonly string[] SentGlow = new string[OilGlow.PlayerSlots.Length];   // what this game last wrote to the player object, per slot (null: nothing yet, in this world)

        /// <summary>
        /// Other players' games can't see these weapons' item data, so the id of the oil on the weapon in each slot (in hand, or put away on
        /// the back) is written to the player object: a few characters, and only when one changes (a player's object is sent whole to
        /// everyone near). "" means none, written and not removed: a removed key stays on the other games.
        /// </summary>
        private static void TellOthers(Player player)
        {
            ZNetView view = player.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            for (int i = 0; i < OilGlow.PlayerSlots.Length; i++)
            {
                OilGlow.Slot slot = OilGlow.PlayerSlots[i];
                ItemDrop.ItemData item = OilGlow.ItemIn(player, slot);
                OilDef oil = item != null ? OilState.Active(item, out _) : null;
                string wanted = oil != null ? oil.Id : "";
                if (wanted == SentGlow[i]) continue;
                view.GetZDO().Set(OilGlow.KeyOf(slot), wanted);
                SentGlow[i] = wanted;
            }
            OilDef hands = OilHands.Active(player, out _);
            string onHands = hands != null ? hands.Id : "";
            if (onHands != _sentHands)
            {
                view.GetZDO().Set(OilHands.GlowKey, onHands);
                _sentHands = onHands;
            }
        }

        private static string _sentHands;   // what this game last wrote for the hands (null: nothing yet, in this world)

        public static void Reset() { _watched = null; _sentHands = null; System.Array.Clear(SentGlow, 0, SentGlow.Length); }
    }
}
