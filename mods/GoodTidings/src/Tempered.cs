using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// "Dvergr-tempered", the wounded Dvergr's thanks: every player near him when he's healed has all their gear mended, and for a while
    /// (Tempered Minutes) it wears down slower (Tempered Wear: the percent of the usual wear it takes). A status effect of our own with
    /// the game's timer and icon. The game wears gear in a few places, all but armour through one number (Game.m_durabilityRate): around
    /// each of them, for a tempered player, that number is lowered and put back; armour (worn by the damage taken) gets the difference back.
    /// </summary>
    internal static class Tempered
    {
        internal const string Name = "GoodTidings_Tempered";
        internal const string Rpc = "GoodTidings_Temper";
        private const float Reach = 30f;
        private static SE_Stats _effect;

        private static SE_Stats Effect()
        {
            if (_effect != null) return _effect;
            _effect = ScriptableObject.CreateInstance<SE_Stats>();
            _effect.name = Name;
            _effect.m_name = "$goodtidings_tempered";
            _effect.m_tooltip = "$goodtidings_tempered_tip";
            _effect.m_icon = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            return _effect;
        }

        internal static void Unload()
        {
            if (_effect) Object.Destroy(_effect);
            _effect = null;
        }

        /// <summary>From his owner: every game near <paramref name="at"/> mends its player's gear and tempers it.</summary>
        internal static void SendTemper(Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, at);

        internal static void OnTemper(long sender, Vector3 at)
        {
            Player p = Player.m_localPlayer;
            if (p == null || Vector3.Distance(p.transform.position, at) > Reach) return;
            int mended = 0;
            foreach (ItemDrop.ItemData item in p.GetInventory().GetAllItems())
                if (item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability())
                {
                    item.m_durability = item.GetMaxDurability();
                    mended++;
                }
            SE_Stats se = Effect();
            se.m_ttl = Plugin.TemperedMinutes.Value * 60f;
            p.GetSEMan().AddStatusEffect(se, resetTime: true);
            p.Message(MessageHud.MessageType.Center, "$goodtidings_tempered_start");
            Plugin.Log.LogInfo($"Tempered: {mended} items mended, slower wear for {Plugin.TemperedMinutes.Value} minutes");
        }

        /// <summary>This game's player, tempered now.</summary>
        internal static bool On(Character c) =>
            c != null && c == Player.m_localPlayer && c.GetSEMan().HaveStatusEffect(Name.GetStableHashCode());

        internal static float Wear => Mathf.Clamp01(Plugin.TemperedWear.Value / 100f);
    }

    /// <summary>The wear that goes through Game.m_durabilityRate: weapons and tools as they're used, a shield blocking, a lantern burning.</summary>
    [HarmonyPatch]
    internal static class Tempered_Wear
    {
        private static readonly AccessTools.FieldRef<Attack, Humanoid> AttackCharacter = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Humanoid), "DrainEquipedItemDurability");
            yield return AccessTools.Method(typeof(Humanoid), "BlockAttack");
            yield return AccessTools.Method(typeof(Player), "UpdatePlacement");
            yield return AccessTools.Method(typeof(Player), "Repair");
            foreach (string m in new[] { "ProjectileAttackTriggered", "DoNonAttack", "DoAreaAttack", "DoMeleeAttack" })
                yield return AccessTools.Method(typeof(Attack), m);
        }

        private static void Prefix(object __instance, out float __state)
        {
            __state = Game.m_durabilityRate;
            Character c = __instance as Character ?? (__instance is Attack a ? AttackCharacter(a) : null);
            if (Tempered.On(c)) Game.m_durabilityRate *= Tempered.Wear;
        }

        private static System.Exception Finalizer(System.Exception __exception, float __state)
        {
            Game.m_durabilityRate = __state;
            return __exception;
        }
    }

    /// <summary>Armour, worn by the damage taken: a tempered player gets back the share of the wear they're spared.</summary>
    [HarmonyPatch(typeof(Player), "DamageArmorDurability")]
    internal static class Tempered_Armour
    {
        private static void Prefix(Player __instance, out List<(ItemDrop.ItemData item, float before)> __state)
        {
            __state = null;
            if (!Tempered.On(__instance)) return;
            __state = __instance.GetInventory().GetEquippedItems().Where(i => i.m_shared.m_useDurability)
                .Select(i => (i, i.m_durability)).ToList();
        }

        private static void Postfix(List<(ItemDrop.ItemData item, float before)> __state)
        {
            if (__state == null) return;
            foreach (var (item, before) in __state)
                if (item.m_durability < before) item.m_durability += (before - item.m_durability) * (1f - Tempered.Wear);
        }
    }
}
