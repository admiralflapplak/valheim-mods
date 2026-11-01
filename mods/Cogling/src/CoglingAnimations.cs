using System.Collections.Generic;
using UnityEngine;

namespace Cogling
{
    /// <summary>The creature whose animations the Cogling's hidden body plays (his movements are copied from it onto the robot).</summary>
    public enum AnimationSet { DvergerMage, DvergerRogue, Fuling, FulingShaman, FulingBrute, Player }

    /// <summary>
    /// The Animation set setting: the hidden Dverger body's animator plays another creature's animations (its controller). This works
    /// when that creature's animations are humanoid ones: Unity then retargets them onto the Dverger's skeleton, and from there they're
    /// copied onto the robot as always. Changes every Cogling here at once (testing, to choose).
    /// </summary>
    internal static class CoglingAnimations
    {
        private static readonly Dictionary<AnimationSet, string> Prefabs = new Dictionary<AnimationSet, string>
        {
            { AnimationSet.DvergerMage, "DvergerMage" }, { AnimationSet.DvergerRogue, "Dverger" }, { AnimationSet.Fuling, "Goblin" },
            { AnimationSet.FulingShaman, "GoblinShaman" }, { AnimationSet.FulingBrute, "GoblinBrute" }, { AnimationSet.Player, "Player" },
        };
        private static bool _probed;

        /// <summary>The chosen set's animations, or null if its creature can't lend them (not humanoid, or missing).</summary>
        internal static RuntimeAnimatorController Controller(ZNetScene scene)
        {
            if (scene == null) return null;
            if (!_probed)
            {
                _probed = true;
                foreach (var kind in Prefabs)
                {
                    Animator a = scene.GetPrefab(kind.Value)?.GetComponentInChildren<Animator>(true);
                    Plugin.Log.LogInfo($"Cogling animations: {kind.Key} ({kind.Value}) controller {(a ? a.runtimeAnimatorController?.name : "-")}, "
                        + $"avatar {(a && a.avatar ? a.avatar.name + (a.avatar.isHuman ? " (humanoid)" : " (NOT humanoid: can't be used)") : "-")}");
                }
            }
            Animator source = Prefabs.TryGetValue(Plugin.Animations.Value, out string name) ? scene.GetPrefab(name)?.GetComponentInChildren<Animator>(true) : null;
            if (source == null || source.avatar == null || !source.avatar.isHuman)
            {
                Plugin.Log.LogWarning($"The {Plugin.Animations.Value} animations can't be used on the Cogling (not humanoid or missing)");
                return null;
            }
            return source.runtimeAnimatorController;
        }

        /// <summary>The setting, on the prefab and every Cogling here.</summary>
        internal static void Apply()
        {
            RuntimeAnimatorController controller = Controller(ZNetScene.instance);
            if (controller == null) return;
            if (CoglingPrefab.Prefab?.transform.Find("Visual")?.GetComponent<Animator>() is Animator prefab) prefab.runtimeAnimatorController = controller;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (!CoglingStance.IsCogling(c) || !(c.transform.Find("Visual")?.GetComponent<Animator>() is Animator animator)) continue;
                animator.runtimeAnimatorController = controller;
                if (c is Humanoid h) CoglingStance.Apply(h, animator);
            }
        }
    }
}
