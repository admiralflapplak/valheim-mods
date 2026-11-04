using UnityEngine;
using HarmonyLib;

namespace Cogling
{
    /// <summary>
    /// The player waves (the wave emote): one Cogling waves back, the one the player is looking at (under the crosshair, else the
    /// nearest to the line of sight, within 15 degrees and 20 m), else the nearest within 10 m. It's told through its own owner's game.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.StartEmote))]
    internal static class WaveBack
    {
        private static void Postfix(Player __instance, string emote, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer || emote != "wave") return;
            Character chosen = Chosen(__instance);
            ZNetView view = chosen != null ? chosen.GetComponent<ZNetView>() : null;
            if (view != null && view.IsValid()) view.InvokeRPC(CoglingVoice.WavedRpc, __instance.transform.position);
        }

        private static Character Chosen(Player player)
        {
            // under the crosshair
            GameObject hover = player.GetHoverObject();
            Character hovered = hover != null ? hover.GetComponentInParent<Character>() : null;
            if (hovered != null && CoglingStance.IsCogling(hovered) && !hovered.IsDead()) return hovered;
            Transform eye = GameCamera.instance != null ? GameCamera.instance.transform : player.transform;
            Character best = null, nearest = null;
            float bestAngle = 15f, nearestDistance = 10f;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (!CoglingStance.IsCogling(c) || c.IsDead() || c.GetComponent<ZNetView>()?.GetZDO() == null) continue;
                Vector3 to = c.GetCenterPoint() - eye.position;
                float angle = Vector3.Angle(eye.forward, to);
                if (to.magnitude < 20f && angle < bestAngle) { best = c; bestAngle = angle; }
                float d = Vector3.Distance(c.transform.position, player.transform.position);
                if (d < nearestDistance) { nearest = c; nearestDistance = d; }
            }
            return best ?? nearest;
        }
    }
}
