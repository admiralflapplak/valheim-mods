using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The build ghost keeps its capsule. The game switches off a ghost's colliders on layers it doesn't place against (the Dverger's
    /// capsule is on "character"; TWIG's is on "piece", so TWIG's stays on), and then places a piece by the point of its colliders
    /// nearest where the player aims (Player.UpdatePlacementGhost). With none on, that point stays at the world's origin and the ghost
    /// went to about twice the aimed point plus 50 m up: every hammer-built Cogling stood kilometres away. Switched back on here (the
    /// ghost is on the "ghost" layer, which the placement ray ignores, so it never blocks its own aim).
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    internal static class CoglingGhost
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        private static void Postfix(Player __instance)
        {
            GameObject ghost = Ghost(__instance);
            if (ghost == null || Utils.GetPrefabName(ghost) != CoglingPrefab.Name) return;
            if (ghost.GetComponent<CapsuleCollider>() is CapsuleCollider capsule) capsule.enabled = true;
            // the hammer's copy of the capsule (a child) would be tested against every character near, the ghost's own body among
            // them (Player.CheckPlacementGhostVSPlayers skips only the root's colliders): "blocked" everywhere
            if (ghost.transform.Find("cogling_hammer_target")?.GetComponent<Collider>() is Collider target) target.enabled = false;
        }
    }
}
