using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Grudges
{
    /// <summary>The F1 test buttons. They skip the dice and cooldowns. Host or single player only.</summary>
    internal static class Testing
    {
        private static bool Ready(out Player p)
        {
            p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null) { Plugin.ShowPlain("Grudges: get into a world first."); return false; }
            if (!Server.IsServer)
            {
                Plugin.ShowPlain("Grudges: test buttons only work in single player or when you're hosting (for now).");
                return false;
            }
            return true;
        }

        /// <summary>The nearest eligible creature becomes a nemesis, as its killer or as a witness to a tree death.</summary>
        internal static void PromoteNearest(bool asWitness)
        {
            if (!Ready(out var p)) return;
            var c = GameInfo.NearestEligible(p.transform.position, 25f, false);
            if (c == null)
            {
                Plugin.ShowPlain($"Grudges: no ordinary {Plugin.SpeciesList.Value} within 25 m.");
                return;
            }
            Promote(p, c, asWitness);
        }

        private static void Promote(Player p, Character c, bool asWitness)
        {
            DeathReport report;
            if (asWitness)
            {
                report = new DeathReport { VictimId = p.GetPlayerID(), VictimName = p.GetPlayerName(), Cause = "Tree" };
                report.Witnesses.Add(GameInfo.Describe(c, p.transform.position));
            }
            else report = PlayerDeathPatch.Build(p, null, c);
            report.Forced = true;
            if (report.Killer != null) report.Witnesses.Clear();
            Server.OnDeath(report);
        }

        internal static void NemesisKilledMe()
        {
            if (!Ready(out var p)) return;
            var c = GameInfo.NearestNemesis(p.transform.position, 40f);
            if (c == null) { Plugin.ShowPlain("Grudges: no nemesis within 40 m."); return; }
            var report = PlayerDeathPatch.Build(p, null, c);
            report.Forced = true;
            report.Witnesses.Clear();
            Server.OnDeath(report);
        }

        internal static void SpawnAndPromote()
        {
            if (!Ready(out var p)) return;
            string species = Plugin.SpawnCreature.Value;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(species) : null;
            if (prefab == null) { Plugin.ShowPlain($"Grudges: couldn't find a creature called '{species}'."); return; }

            Vector3 pos = p.transform.position + p.transform.forward * 8f + Vector3.up * 1f;
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(-p.transform.forward));
            Plugin.Instance.StartCoroutine(PromoteNextFrame(p, go));
        }

        private static IEnumerator PromoteNextFrame(Player p, GameObject go)
        {
            yield return null; // let it finish waking up
            yield return null;
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c == null || p == null) { Plugin.ShowPlain("Grudges: the spawned creature vanished."); yield break; }
            Promote(p, c, false);
        }

        internal static void TauntNow()
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            var c = GameInfo.NearestNemesis(p.transform.position, 40f);
            var body = c != null ? c.GetComponent<NemesisBody>() : null;
            var bodies = Plugin.Instance.GetComponent<Bodies>();
            if (body == null || body.Record == null || bodies == null) { Plugin.ShowPlain("Grudges: no nemesis within 40 m."); return; }
            bodies.Say(c, body, p);
        }

        /// <summary>A real killing blow from you, through the game's own damage, with the outcome forced.</summary>
        internal static void Wound(bool escape)
        {
            if (!Ready(out var p)) return;
            var c = GameInfo.NearestNemesis(p.transform.position, 40f);
            if (c == null) { Plugin.ShowPlain("Grudges: no nemesis within 40 m."); return; }
            NemesisDeathPatch.ForceOutcome = escape ? "escape" : "die";
            var hit = new HitData();
            SetDamage(ref hit.m_damage, escape ? Plugin.TestDamageType.Value : "Slash", 100000f);
            hit.m_point = c.transform.position + Vector3.up;
            try { HarmonyLib.Traverse.Create(hit).Method("SetAttacker", new[] { typeof(Character) }, new object[] { p }).GetValue(); }
            catch { /* then nobody gets the credit */ }
            c.Damage(hit);
        }

        private static void SetDamage(ref HitData.DamageTypes d, string type, float amount)
        {
            switch (type)
            {
                case "Blunt": d.m_blunt = amount; break;
                case "Pierce": d.m_pierce = amount; break;
                case "Fire": d.m_fire = amount; break;
                case "Frost": d.m_frost = amount; break;
                case "Lightning": d.m_lightning = amount; break;
                case "Poison": d.m_poison = amount; break;
                case "Spirit": d.m_spirit = amount; break;
                default: d.m_slash = amount; break;
            }
        }

        /// <summary>Gives the nearest nemesis a scar of the chosen type straight away (no death, no recovery wait).</summary>
        internal static void AddScar()
        {
            if (!Ready(out var p)) return;
            var c = GameInfo.NearestNemesis(p.transform.position, 40f);
            string id = c != null ? GameInfo.TagOf(c) : "";
            if (id.Length == 0) { Plugin.ShowPlain("Grudges: no nemesis within 40 m."); return; }
            Server.TestScar(id, Plugin.TestDamageType.Value, p.GetPlayerID(), p.GetPlayerName());
        }

        internal static void SetPersonality()
        {
            if (!Ready(out var p)) return;
            var c = GameInfo.NearestNemesis(p.transform.position, 40f);
            string id = c != null ? GameInfo.TagOf(c) : "";
            if (id.Length == 0) { Plugin.ShowPlain("Grudges: no nemesis within 40 m."); return; }
            Server.TestPersonality(id, Plugin.TestPersonality.Value.Trim());
        }

        internal static void ReplayReveal()
        {
            var p = Player.m_localPlayer;
            if (p == null || Reveal.Instance == null) return;
            var c = GameInfo.NearestNemesis(p.transform.position, 40f);
            var body = c != null ? c.GetComponent<NemesisBody>() : null;
            if (body == null || body.Record == null) { Plugin.ShowPlain("Grudges: no nemesis within 40 m."); return; }
            var info = new RevealInfo
            {
                NemesisId = body.Record.Id,
                Kind = "replay",
                Title = body.Record.FullName,
                Line = Server.Rules.Taunt(body.Record, p.GetPlayerID(), p.GetPlayerName(), new System.Random()),
                IsTest = true
            };
            if (!Reveal.Instance.Begin(info)) Plugin.ShowPlain("Grudges: the reveal can't play (turned off, already playing, or the camera hook failed; see the log).");
        }

        internal static void ShowRoster()
        {
            var roster = Net.Known;
            if (roster == null) { Plugin.ShowPlain("Grudges: no roster yet (not in a world, or the server doesn't have the mod)."); return; }
            var active = roster.Living.OrderBy(n => n.Biome).ThenByDescending(n => n.Level).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Grudges roster: {active.Count} nemeses ({active.Count(n => !n.IsActive)} recovering)");
            foreach (var n in active)
            {
                sb.AppendLine($"  [{n.Biome}] {(n.Champion ? "CHAMPION " : "")}{n.FullName} ({n.SpeciesName}, {n.Stars} star{(n.Stars == 1 ? "" : "s")})" +
                              $" | strong: {(n.Strength.Length > 0 ? n.Strength : "-")}, weak: {n.Weakness}" +
                              $" | {n.Personality}, {n.Palette}, size {Plugin.SizeFor(n):0.00} | {string.Join(", ", n.Traits)} | {n.Origin}: {n.Cause}" +
                              (n.Scars.Count > 0 ? " | scars: " + string.Join(", ", n.Scars.Select(x => $"{x.Type} ({x.Effect}, {x.By})")) : "") +
                              (n.IsActive ? "" : $" | RECOVERING, back in {Math.Max(0, (n.ReturnAt - Server.WorldTime()) / 60):0} min, for {n.ScarredByName}"));
                foreach (var g in n.Grudges)
                    sb.AppendLine($"      vs {g.PlayerName}: {g.Kills} kill(s){(g.Witnessed.Count > 0 ? ", saw: " + string.Join(", ", g.Witnessed) : "")}");
            }
            if (Server.IsServer && Server.Roster != null)
            {
                var retired = Server.Roster.All.Where(n => !n.IsActive).ToList();
                if (retired.Count > 0)
                {
                    sb.AppendLine($"  Fallen ({retired.Count}):");
                    foreach (var n in retired) sb.AppendLine($"    {n.FullName}: {n.RetiredReason}");
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
            Plugin.ShowPlain(active.Count == 0
                ? "Grudges: no nemeses in this world yet."
                : string.Join("\n", active.Take(6).Select(n => $"{(n.Champion ? "[Champion] " : "")}{n.FullName} ({n.Biome}, {n.Stars}*{(n.IsActive ? "" : ", recovering")})")) +
                  (active.Count > 6 ? $"\n...and {active.Count - 6} more (see the log)" : "\n(details in the log)"));
        }

        internal static void ClearRoster()
        {
            if (!Ready(out _)) return;
            Server.Clear();
            Plugin.ShowPlain("Grudges: every nemesis in this world has been forgotten.");
        }
    }
}
