using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The wandering trader as an object the game saves and shares: a copy of the Lox prefab, registered in ZNetScene as
    /// "goodtidings_trader". Only what walks stays (the body, its movement and sync): our own AI (TraderAI) instead of the Lox's
    /// MonsterAI, and none of its taming, riding, breeding, drops or trampling. TraderVisitor on it dresses it and runs the shop.
    /// Registered from ZNetScene.Awake (and at once after a reload), so the host never loads her before the game knows her (it would
    /// delete her: for a visitor, a safe failure).
    /// </summary>
    internal static class TraderPrefab
    {
        internal const string Name = "goodtidings_trader";
        private static GameObject _holder, _prefab;

        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabs =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        internal static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (_prefab == null) Build(scene);
            if (_prefab == null) return;
            scene.m_prefabs.RemoveAll(p => p == null || (p.name == Name && p != _prefab));
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            NamedPrefabs(scene)[Name.GetStableHashCode()] = _prefab;
        }

        internal static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null && _prefab != null)
            {
                scene.m_prefabs.Remove(_prefab);
                Dictionary<int, GameObject> named = NamedPrefabs(scene);
                if (named.TryGetValue(Name.GetStableHashCode(), out GameObject p) && p == _prefab) named.Remove(Name.GetStableHashCode());
            }
            if (_holder) Object.Destroy(_holder);
            _holder = _prefab = null;
        }

        private static void Build(ZNetScene scene)
        {
            GameObject lox = scene.GetPrefab("Lox");
            if (lox == null) { Plugin.Log.LogError("No Lox prefab: the wandering trader can't be made"); return; }
            // kept under a switched-off holder: nothing on the copy wakes up, as with the game's own prefabs
            _holder = new GameObject("goodtidings_prefabs");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
            GameObject p = Object.Instantiate(lox, _holder.transform, false);
            p.name = Name;

            // our AI, with the Lox's movement settings (speeds, turning, path type, avoiding fire and water...)
            MonsterAI monster = p.GetComponent<MonsterAI>();
            var ai = p.AddComponent<TraderAI>();
            foreach (FieldInfo f in typeof(BaseAI).GetFields(BindingFlags.Instance | BindingFlags.Public))
                f.SetValue(ai, f.GetValue(monster));
            ai.m_randomMoveRange = 9f;
            ai.m_randomMoveInterval = 7f;
            ai.m_spawnMessage = "";
            Object.DestroyImmediate(monster);

            foreach (System.Type t in new[] { typeof(Tameable), typeof(Procreation), typeof(CharacterDrop), typeof(MovementDamage) })
            {
                Component c = p.GetComponent(t);
                if (c) Object.DestroyImmediate(c);
            }
            Transform saddle = Utils.FindChild(p.transform, "Sadel");
            if (saddle)
            {
                Sadle sadle = saddle.GetComponent<Sadle>();
                if (sadle) Object.DestroyImmediate(sadle);
                saddle.gameObject.SetActive(true);   // (the Lox's Tameable showed it once saddled)
            }
            Transform trample = Utils.FindChild(p.transform, "RunHitDamager");
            if (trample) Object.DestroyImmediate(trample.gameObject);

            p.GetComponent<Humanoid>().m_name = "$goodtidings_trader";
            p.AddComponent<TraderVisitor>();
            _prefab = p;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Register(__instance);
        }
    }

    /// <summary>The trader can't be hurt, and nothing takes her for an enemy (or she anyone): no fights, no reactions to hits.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Character_Damage
    {
        private static bool Prefix(Character __instance) => !TraderVisitor.Is(__instance);
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
    internal static class BaseAI_IsEnemy
    {
        private static void Postfix(Character a, Character b, ref bool __result)
        {
            if (__result && (TraderVisitor.Is(a) || TraderVisitor.Is(b))) __result = false;
        }
    }

    /// <summary>No health bar over her.</summary>
    [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
    internal static class EnemyHud_ShowHud
    {
        private static bool Prefix(Character c) => !TraderVisitor.Is(c);
    }

    /// <summary>Her name and "[E] Trade" when the player looks at her.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    internal static class Character_GetHoverText
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            if (TraderVisitor.Is(__instance))
                __result = Localization.instance.Localize(__instance.m_name + "\n[<color=yellow><b>$KEY_Use</b></color>] $goodtidings_trade");
        }
    }
}
