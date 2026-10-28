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
        internal const string HeadReach = "goodtidings_reach_head";
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
            // what Atlas eats: the Lox's own food (Plains) and the earlier lands' berries and roots, so he can be treated from the start
            ai.m_consumeItems = new List<ItemDrop>(monster.m_consumeItems);
            foreach (string food in new[] { "Raspberry", "Blueberries", "Carrot", "Turnip", "Onion" })
            {
                ItemDrop item = scene.GetPrefab(food)?.GetComponent<ItemDrop>();
                if (item && !ai.m_consumeItems.Contains(item)) ai.m_consumeItems.Add(item);
            }
            Object.DestroyImmediate(monster);

            var visitor = p.AddComponent<TraderVisitor>();
            Tameable tame = p.GetComponent<Tameable>();
            if (tame) visitor.m_petEffect = tame.m_petEffect;   // (Atlas can still be petted)
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

            p.GetComponent<Humanoid>().m_name = "$goodtidings_atlas";
            // reach: the game lets a player use things within about 3 m of their eyes; a Lox's head is far out front and the rider high
            // up, so the reach to him is 2.5 m longer (the game's own hover offset)
            p.GetComponent<Humanoid>().m_hoverOffset = 2.5f;

            // His long body and head shapes are on a layer the game's "what is the player looking at" skips (only a 3 m capsule round his
            // middle counted, ending at his shoulders): copies of them as triggers (they push nothing) on the saddle's layer, which it sees.
            // Looking at his head then hits "goodtidings_reach_head" (TraderVisitor.AtHead: pet him).
            int layer = LayerMask.NameToLayer("piece_nonsolid");
            foreach ((string from, string to) in new[] { ("Capsule", "goodtidings_reach_body"), ("Capsule (1)", HeadReach) })
            {
                Transform shape = Utils.FindChild(p.transform, from);
                if (shape == null || shape.GetComponent<CapsuleCollider>() == null) continue;
                GameObject copy = Object.Instantiate(shape.gameObject, shape.parent, false);
                copy.name = to;
                copy.layer = layer;
                copy.GetComponent<CapsuleCollider>().isTrigger = true;
            }
            _prefab = p;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Register(__instance);
        }
    }

    /// <summary>
    /// The trader can't be hurt, and nothing takes her for an enemy (or she anyone): no fights, no reactions to hits. Every way in is
    /// closed: a hit (Damage, and its RPC on the owner), damage applied straight to health (poison, fire and smoke do: a beehive's bees
    /// hurt Atlas through that), and status effects, which never take hold on her.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Character_Damage
    {
        private static bool Prefix(Character __instance) => !TraderVisitor.Is(__instance);
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage
    {
        private static bool Prefix(Character __instance) => !TraderVisitor.Is(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage
    {
        private static bool Prefix(Character __instance) => !TraderVisitor.Is(__instance);
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short))]
    internal static class SEMan_AddStatusEffect
    {
        private static readonly AccessTools.FieldRef<SEMan, Character> Owner = AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        private static bool Prefix(SEMan __instance, ref StatusEffect __result)
        {
            if (!TraderVisitor.Is(Owner(__instance))) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short))]
    internal static class SEMan_AddStatusEffect_ByHash
    {
        private static readonly AccessTools.FieldRef<SEMan, Character> Owner = AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        private static bool Prefix(SEMan __instance, ref StatusEffect __result)
        {
            if (!TraderVisitor.Is(Owner(__instance))) return true;
            __result = null;
            return false;
        }
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
                __result = Localization.instance.Localize(TraderVisitor.AtHead(Player.m_localPlayer)
                    ? "$goodtidings_atlas\n[<color=yellow><b>$KEY_Use</b></color>] $goodtidings_pet"
                    : "$goodtidings_dagny\n[<color=yellow><b>$KEY_Use</b></color>] $goodtidings_trade");
        }
    }
}
