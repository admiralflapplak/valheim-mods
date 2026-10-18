using System;
using HarmonyLib;
using UnityEngine;

namespace WeaponOils
{
    /// <summary>
    /// The chill that Rime oil puts on what the weapon hits. The game's own frost effect lasts only as long as the frost damage is a share of
    /// the target's health (a few points on a big creature: a fraction of a second, no slow and no look), so Rime gets an effect of its own
    /// with a time and a slow the settings give, made on the game's frost effect (the same slow that fades away, the same frost effects
    /// around the creature). Bosses and creatures that resist frost are not slowed, as the game's frost doesn't slow them.
    /// </summary>
    internal sealed class SE_Chill : SE_Frost
    {
        public override void Setup(Character character)
        {
            EnsureEffects();
            base.Setup(character);
        }

        /// <summary>The frost look and sound of the game's own frost effect, found by name (the effect made in code starts with none).</summary>
        private void EnsureEffects()
        {
            if (m_startEffects != null && m_startEffects.m_effectPrefabs != null && m_startEffects.m_effectPrefabs.Length > 0) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;
            var list = new System.Collections.Generic.List<EffectList.EffectData>();
            foreach (string name in new[] { "vfx_Frost", "sfx_Frost_Start" })
            {
                GameObject prefab = scene.GetPrefab(name);
                if (prefab != null) list.Add(new EffectList.EffectData { m_prefab = prefab, m_enabled = true, m_attach = true, m_follow = true, m_scale = true });
            }
            m_startEffects = new EffectList { m_effectPrefabs = list.ToArray() };
        }
    }

    internal static class OilChill
    {
        public const string Name = "nick_weaponoils_chill";
        public static readonly int Hash = Name.GetStableHashCode();
        private static SE_Chill _effect;

        public static void Make()
        {
            _effect = StatusEffects.Create<SE_Chill>(Name, "$nick_weaponoils_chilled");
            ApplySettings();
        }

        /// <summary>The time and strength from the settings (a chill already on a creature keeps what it started with).</summary>
        public static void ApplySettings()
        {
            if (_effect == null) return;
            _effect.m_ttl = Plugin.ChillSeconds.Value;
            _effect.m_minSpeedFactor = 1f - Plugin.ChillSlow.Value;   // (the game's frost slows by 1 - this, fading away over its time)
        }

        public static void Forget() => _effect = null;

        /// <summary>Chills a creature the local player has just hit (the effect goes to whichever game owns it).</summary>
        public static void Hit(Character target)
        {
            if (target == null || target.IsBoss() || target.IsDead()) return;
            HitData.DamageModifier frost = target.GetDamageModifiers().m_frost;
            if (frost == HitData.DamageModifier.Immune || frost == HitData.DamageModifier.VeryResistant
                || frost == HitData.DamageModifier.Resistant || frost == HitData.DamageModifier.SlightlyResistant) return;
            StatusEffects.Apply(target, Hash);
        }

        // ---- the game's lists: the effect goes in again on every ObjectDB the game makes -------------------------

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake { private static void Postfix() => Guarded(); }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDB_CopyOtherDB { private static void Postfix() => Guarded(); }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake { private static void Postfix() => Guarded(); }

        private static void Guarded()
        {
            try { StatusEffects.Register(); }
            catch (Exception e) { Plugin.Log.LogError("Registering the chill effect failed: " + e); }
        }
    }
}
