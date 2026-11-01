using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Which animation set the Cogling's Dverger body moves with. The game chooses a humanoid's stance from what it holds
    /// (Humanoid.SetupAnimationState: the item's m_animationState into the animator's "statef"/"statei"): the Dverger rogues hold
    /// crossbows, the mages staves, and with empty hands it's the unarmed one. The Cogling holds nothing, so after the game sets the
    /// stance, the one from the Stance setting goes in its place.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "SetupAnimationState")]
    internal static class CoglingStance
    {
        private static readonly int StateF = ZSyncAnimation.GetHash("statef");
        private static readonly int StateI = ZSyncAnimation.GetHash("statei");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly AccessTools.FieldRef<Character, ZNetView> View = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        internal static bool IsCogling(Character c) => c != null && Utils.GetPrefabName(c.gameObject) == CoglingPrefab.Name;

        private static void Postfix(Humanoid __instance)
        {
            if (!IsCogling(__instance)) return;
            ZNetView view = View(__instance);
            ZSyncAnimation anim = Anim(__instance);
            if (anim == null || view == null || !view.IsValid() || !view.IsOwner()) return;   // the owner's animator values reach everyone
            var state = Plugin.Stance.Value;
            anim.SetFloat(StateF, (float)state);
            anim.SetInt(StateI, (int)state);
        }

        /// <summary>A Cogling as it appears: through the network for a real one, straight into the animator for the build ghost and pictures.</summary>
        internal static void Apply(Humanoid h, Animator animator)
        {
            if (h != null) Postfix(h);
            if (animator == null) return;
            animator.SetFloat("statef", (float)Plugin.Stance.Value);
            animator.SetInteger("statei", (int)Plugin.Stance.Value);
        }

        /// <summary>The Coglings around this player take the setting now.</summary>
        internal static void RefreshAll()
        {
            foreach (Character c in Character.GetAllCharacters())
                if (IsCogling(c) && c is Humanoid h) Postfix(h);
        }
    }
}
