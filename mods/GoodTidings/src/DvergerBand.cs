using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The neglected Dvergr's kin: a few angry Dverger come for the base. Dverger are Mistlands folk, so their strength (health and the
    /// damage they deal) is scaled to the newest land the world has opened, and they drop nothing (no Mistlands loot early). Rogues, a
    /// mage from the Mountains on, more later. They give up and go after Band Minutes. Marked in their ZDO (survives a restart). A red
    /// circle on the base while any are about, as a raid's; each drops a little gold, more in later lands.
    /// </summary>
    internal static class DvergerBand
    {
        internal const string BandKey = "GoodTidings.Band", UntilKey = "GoodTidings.BandUntil", ScaleKey = "GoodTidings.BandScale",
            HomeKey = "GoodTidings.BandHome", LandKey = "GoodTidings.BandLand";
        // coins each drops, by land (a quarter either way)
        private static readonly int[] Gold = { 10, 15, 20, 30, 40, 55, 70, 85 };
        private const float BandMinutes = 8f;
        // by land: Meadows ... Deep North
        private static readonly float[] Scale = { 0.25f, 0.35f, 0.5f, 0.65f, 0.8f, 1f, 1.3f, 1.6f };

        internal static void Call(Vector3 home)
        {
            int land = Stock.NewestLand();
            var kinds = new List<string> { "Dverger", "Dverger" };
            if (land >= 3) kinds.Add(land >= 5 ? (UnityEngine.Random.value < 0.5f ? "DvergerMageFire" : "DvergerMageIce") : "DvergerMage");
            if (land >= 6) kinds.Add("Dverger");
            float scale = Scale[Mathf.Clamp(land, 0, Scale.Length - 1)];
            long until = ZNet.instance.GetTime().Ticks + TimeSpan.FromMinutes(BandMinutes).Ticks;
            Vector3 from = home + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(26f, 32f);
            int made = 0;
            foreach (string kind in kinds)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(kind);
                if (prefab == null) continue;
                Vector3 p = from + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f));
                if (ZoneSystem.instance.GetSolidHeight(p, out float h)) p.y = h + 0.5f;
                GameObject go = UnityEngine.Object.Instantiate(prefab, p, Quaternion.LookRotation(Vector3.ProjectOnPlane(home - p, Vector3.up)));
                ZDO zdo = go.GetComponent<ZNetView>().GetZDO();
                zdo.Set(BandKey, true);
                zdo.Set(UntilKey, until);
                zdo.Set(ScaleKey, scale);
                zdo.Set(HomeKey, home);
                zdo.Set(LandKey, land);
                Character c = go.GetComponent<Character>();
                c.SetLevel(1);
                c.SetMaxHealth(c.m_health * scale);
                c.SetHealth(c.m_health * scale);
                Band.Add(c);
                BaseAI ai = go.GetComponent<BaseAI>();
                if (ai)
                {
                    ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);
                    ai.SetHuntPlayer(true);
                }
                made++;
            }
            Net.MessageAll(MessageHud.MessageType.Center, "$goodtidings_dverger_band");
            Plugin.Log.LogInfo($"The Dvergr's kin come for the base at {home}: {made} of them at {scale:0.##} strength");
        }

        // ---- telling them apart (known on every game from their ZDO)

        private static readonly HashSet<Character> Band = new HashSet<Character>();

        internal static bool Is(Character c, out float scale)
        {
            scale = 1f;
            if (c == null || !Band.Contains(c)) return false;
            scale = c.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(ScaleKey, 1f) ?? 1f;
            return true;
        }

        [HarmonyPatch(typeof(Character), "Start")]
        private static class Character_Start
        {
            private static void Postfix(Character __instance)
            {
                if (__instance.GetFaction() != Character.Faction.Dverger) return;
                if (__instance.GetComponent<ZNetView>()?.GetZDO()?.GetBool(BandKey) == true) Band.Add(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), "OnDestroy")]
        private static class Character_OnDestroy
        {
            private static void Postfix(Character __instance) => Band.Remove(__instance);
        }

        /// <summary>Their blows at the strength of the land.</summary>
        [HarmonyPatch(typeof(Attack), "GetLevelDamageFactor")]
        private static class Attack_GetLevelDamageFactor
        {
            private static readonly AccessTools.FieldRef<Attack, Humanoid> Owner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
            private static void Postfix(Attack __instance, ref float __result)
            {
                if (Is(Owner(__instance), out float scale)) __result *= scale;
            }
        }

        /// <summary>Only gold (none of their Mistlands drops): the land's amount, give or take a quarter.</summary>
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class CharacterDrop_GenerateDropList
        {
            private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
            {
                Character c = __instance.GetComponent<Character>();
                if (!Is(c, out _)) return;
                __result.Clear();
                int land = Mathf.Clamp(c.GetComponent<ZNetView>()?.GetZDO()?.GetInt(LandKey) ?? 0, 0, Gold.Length - 1);
                GameObject coins = ObjectDB.instance?.GetItemPrefab("Coins");
                int amount = Mathf.RoundToInt(Gold[land] * UnityEngine.Random.Range(0.75f, 1.25f));
                if (coins && amount > 0) __result.Add(new KeyValuePair<GameObject, int>(coins, amount));
            }
        }

        // ---- the red circle, on every game that has them loaded

        private static readonly MapPins Pins = new MapPins();
        private static bool _shown;

        internal static void Tick()
        {
            Character any = null;
            foreach (Character c in Band) if (c) { any = c; break; }
            ZDO zdo = any ? any.GetComponent<ZNetView>()?.GetZDO() : null;
            if (zdo == null)
            {
                if (_shown) { Pins.Remove(); _shown = false; }
                return;
            }
            Vector3 home = zdo.GetVec3(HomeKey, any.transform.position);
            Pins.Update(home, home, "", withIcon: false, circle: MapPins.Raid);
            _shown = true;
        }

        internal static void Unload()
        {
            Pins.Remove();
            _shown = false;
        }

        /// <summary>Gone when their time is up (on their owner), in a puff as a raid's leave.</summary>
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class MonsterAI_UpdateAI
        {
            private static readonly AccessTools.FieldRef<BaseAI, Character> Of = AccessTools.FieldRefAccess<BaseAI, Character>("m_character");

            private static bool Prefix(MonsterAI __instance)
            {
                Character c = Of(__instance);
                if (Band.Count == 0 || !Is(c, out _)) return true;
                ZNetView nview = c.GetComponent<ZNetView>();
                if (!nview.IsOwner() || ZNet.instance.GetTime().Ticks < nview.GetZDO().GetLong(UntilKey, long.MaxValue)) return true;
                Plugin.Log.LogInfo("One of the Dvergr's kin gives up and goes");
                ZNetScene.instance.Destroy(c.gameObject);
                return false;
            }
        }
    }
}
