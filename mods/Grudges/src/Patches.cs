using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Your death: who killed you, or what did, and which creatures saw it. Sent to the server.
    /// Runs before Player.OnDeath so the last hit is still there to read. Never changes the death itself.
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PlayerDeathPatch
    {
        private static Player _lastHandled;

        private static void Prefix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer || __instance == _lastHandled) return;
                _lastHandled = __instance;
                Reveal.NoteDeath(__instance.transform.position);
                var report = Build(__instance, Traverse.Create(__instance).Field("m_lastHit").GetValue<HitData>());
                Plugin.DebugLog($"You died: {report.Cause}{(report.Killer != null ? " by " + report.Killer.Species : "")}" +
                                $"{(report.CauseDetail.Length > 0 ? " (" + report.CauseDetail + ")" : "")}, {report.Witnesses.Count} creature(s) saw it.");
                Net.ReportDeath(report);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't read that death: {e.Message}");
            }
        }

        internal static DeathReport Build(Player p, HitData hit, Character forcedKiller = null)
        {
            Vector3 pos = p.transform.position;
            var report = new DeathReport { VictimId = p.GetPlayerID(), VictimName = p.GetPlayerName() };
            try
            {
                // The helmet they died in (the server decides whether it's one a nemesis can take).
                var helmet = Traverse.Create(p).Field("m_helmetItem").GetValue<ItemDrop.ItemData>();
                if (helmet?.m_dropPrefab != null) report.VictimHelmet = helmet.m_dropPrefab.name;
            }
            catch { }

            Character attacker = forcedKiller ?? hit?.GetAttacker();
            if (attacker != null && !(attacker is Player))
            {
                report.Cause = "Kill";
                report.Killer = GameInfo.Describe(attacker, pos);
            }
            else
            {
                var other = attacker as Player;
                report.Cause = Causes.FromHitType(hit != null ? hit.m_hitType.ToString() : "", other != null, other == p, false);
                if (report.Cause == "Friend" && other != null) report.CauseDetail = other.GetPlayerName();
            }

            // Who saw it? Close creatures always do; further ones only if they had noticed you.
            foreach (var c in GameInfo.CreaturesNear(pos, Plugin.WitnessRadius.Value))
            {
                if (c == attacker) continue;
                float d = Vector3.Distance(pos, c.transform.position);
                if (d > Plugin.WitnessCloseEnough.Value && !GameInfo.IsAlerted(c)) continue;
                report.Witnesses.Add(GameInfo.Describe(c, pos));
                if (report.Witnesses.Count >= 12) break;
            }
            return report;
        }
    }

    /// <summary>
    /// A nemesis takes a killing blow. Runs on the game that controls the creature. Either it cheats death
    /// (the death is cancelled, it vanishes in a puff and goes off to recover), or it dies for real and drops
    /// its named trophy. Either way the server is told.
    /// </summary>
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class NemesisDeathPatch
    {
        private static readonly HashSet<int> _handled = new HashSet<int>(); // per body: a nemesis that comes back has a new one
        private static readonly System.Random Rng = new System.Random();

        /// <summary>Test buttons: "escape" or "die" forces the next outcome.</summary>
        internal static string ForceOutcome;

        private static bool Prefix(Character __instance)
        {
            try
            {
                if (__instance == null || __instance is Player) return true;
                string id = GameInfo.TagOf(__instance);
                if (id.Length == 0 || _handled.Contains(__instance.GetInstanceID())) return true;
                var record = Net.Known?.FindActive(id);

                var hit = Traverse.Create(__instance).Field("m_lastHit").GetValue<HitData>();
                var killer = hit?.GetAttacker() as Player;
                string blow = DominantType(hit);

                string forced = ForceOutcome;
                ForceOutcome = null;
                float chance = Rules.SurvivalChance(record, blow, Plugin.CheatDeathChance.Value, Plugin.CheatDeathDecay.Value);
                bool escape = record != null && (forced == "escape" || (forced != "die" && Rng.NextDouble() < chance));
                Plugin.DebugLog($"{record?.FullName ?? id} took a killing blow ({blow}); survival chance {chance:P0}: {(escape ? "escapes" : "dies")}.");

                _handled.Add(__instance.GetInstanceID());
                if (escape)
                {
                    Escape(__instance, record, blow, killer);
                    return false; // no death, no ragdoll, no drops
                }

                Trophies.Drop(__instance, record, killer?.GetPlayerName() ?? "");
                Net.ReportSlain(id, killer?.GetPlayerName() ?? "");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't handle a nemesis death: {e.Message}");
                return true;
            }
        }

        private static void Escape(Character c, Nemesis record, string blow, Player killer)
        {
            Vector3 pos = c.transform.position;
            Net.PlayEffectForAll(pos + Vector3.up * 0.5f, Plugin.RevealEffects.Value);
            Net.ReportEscaped(record.Id, blow, killer != null ? killer.GetPlayerID() : 0L, killer?.GetPlayerName() ?? "");
            // The body goes; it comes back later in a new one.
            var nview = c.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && ZNetScene.instance != null) ZNetScene.instance.Destroy(c.gameObject);
        }

        /// <summary>The damage type that did the most of a hit, e.g. "Fire". "" if unknown.</summary>
        internal static string DominantType(HitData hit)
        {
            if (hit == null) return "";
            var d = hit.m_damage;
            string[] names = { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit" };
            float[] amounts = { d.m_blunt, d.m_slash, d.m_pierce, d.m_fire, d.m_frost, d.m_lightning, d.m_poison, d.m_spirit };
            int best = 0;
            for (int i = 1; i < amounts.Length; i++) if (amounts[i] > amounts[best]) best = i;
            return amounts[best] > 0f ? names[best] : "";
        }
    }

    /// <summary>Brutal nemeses hit harder.</summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    internal static class BrutalPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            try
            {
                // A nemesis still in a borrowed emote gets its own animations back as it flinches (hides the snap).
                var target = __instance != null ? __instance.GetComponent<NemesisBody>() : null;
                if (target != null) target.RestoreNow();

                var attacker = hit?.GetAttacker();
                if (attacker == null || attacker is Player) return;
                var body = attacker.GetComponent<NemesisBody>();
                if (body != null && body.Has(Traits.Brutal)) hit.m_damage.Modify(Plugin.BrutalDamage.Value);
            }
            catch { /* never break combat */ }
        }
    }
}
