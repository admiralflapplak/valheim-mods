using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// No Wanted board closer than the server's spacing to another one, so a yard full of boards can't hand out a board's worth of notices
    /// each. While placing, the ghost goes red near one; building there is refused with a message saying why. The boards checked are the
    /// ones this game has loaded, which covers the spacing (the game loads everything within about 60 m of the player).
    /// </summary>
    internal static class Spacing
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> Status =
            AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
        private static readonly MethodInfo SetGhostValid = AccessTools.Method(typeof(Player), "SetPlacementGhostValid");

        /// <summary>How far the nearest other board is, if one is closer than the spacing; otherwise null.</summary>
        public static float? TooClose(Vector3 at)
        {
            float spacing = Plugin.BoardSpacing.Value;
            if (spacing <= 0f) return null;
            float nearest = float.MaxValue;
            foreach (NoticeBoard b in NoticeBoard.Loaded)
                if (b != null) nearest = Mathf.Min(nearest, Vector3.Distance(b.transform.position, at));
            return nearest < spacing ? nearest : (float?)null;
        }

        private static bool IsBoard(GameObject go) => go != null && Utils.GetPrefabName(go) == Board.PrefabName;

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class Player_UpdatePlacementGhost
        {
            private static void Postfix(Player __instance)
            {
                try
                {
                    GameObject ghost = Ghost(__instance);
                    if (!IsBoard(ghost) || Status(__instance) != Player.PlacementStatus.Valid) return;
                    if (TooClose(ghost.transform.position) == null) return;
                    Status(__instance) = Player.PlacementStatus.MoreSpace;
                    SetGhostValid?.Invoke(__instance, new object[] { false });
                }
                catch (Exception e) { Plugin.Log.LogWarning("Board spacing (ghost): " + e.Message); }
            }
        }

        // TryPlacePiece checks the ghost first (the postfix above makes it "needs more space"); this says why instead, before it does.
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class Player_TryPlacePiece
        {
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                try
                {
                    if (piece == null || !IsBoard(piece.gameObject)) return true;
                    GameObject ghost = Ghost(__instance);
                    float? near = ghost != null ? TooClose(ghost.transform.position) : null;
                    if (near == null) return true;
                    __instance.Message(MessageHud.MessageType.Center, T("wanted_too_close", Mathf.CeilToInt(near.Value), Mathf.RoundToInt(Plugin.BoardSpacing.Value)));
                    __result = false;
                    return false;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Board spacing: " + e.Message); return true; }
            }
        }
    }
}
