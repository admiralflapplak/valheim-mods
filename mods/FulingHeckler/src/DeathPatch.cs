using System;
using HarmonyLib;

namespace FulingHeckler
{
    /// <summary>
    /// Watches the local player's death. Runs before Player.OnDeath so the last hit is still there to read.
    /// Read-only: never changes anything about the death itself.
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PlayerOnDeathPatch
    {
        private static Player _lastHandled;

        private static void Prefix(Player __instance)
        {
            try
            {
                if (Plugin.Instance == null || __instance == null) return;
                if (__instance != Player.m_localPlayer) return;
                if (__instance == _lastHandled) return; // same body, already counted
                _lastHandled = __instance;

                var info = new DeathInfo();
                // m_lastHit is private on Character, so read it by name.
                var hit = Traverse.Create(__instance).Field("m_lastHit").GetValue<HitData>();
                if (hit != null)
                {
                    info.HitType = hit.m_hitType.ToString();

                    Character attacker = hit.GetAttacker();
                    if (attacker != null)
                    {
                        if (attacker is Player killerPlayer)
                        {
                            info.KillerIsPlayer = true;
                            info.KillerIsSelf = killerPlayer == __instance;
                            info.KillerName = killerPlayer.GetPlayerName();
                        }
                        else
                        {
                            info.KillerPrefab = Utils.GetPrefabName(attacker.gameObject);
                            info.KillerName = Localization.instance != null
                                ? Localization.instance.Localize(attacker.m_name)
                                : attacker.m_name;
                        }
                    }
                }

                Plugin.Instance.OnLocalPlayerDied(__instance, info);
            }
            catch (Exception e)
            {
                // Never let a joke mod interfere with dying properly.
                Plugin.Log?.LogWarning($"Fuling couldn't read that death: {e.Message}");
            }
        }
    }
}
