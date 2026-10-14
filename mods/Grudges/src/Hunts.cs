using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Cross-biome grudges. A nemesis with something personal against you (it killed you, watched you die, or you
    /// scarred it) may follow you out of its own biome, anywhere. You get a warning first ("you hear laughing in
    /// the trees..."), then it turns up a short way off and the camera shows it arrive. In a tougher biome than its
    /// own it's boosted a level per step of toughness, so it isn't one-shot.
    ///
    /// Runs on your own game: it only ever comes for the local player. Rare by design (see F1 → 8c).
    /// </summary>
    internal class Hunts : MonoBehaviour
    {
        private const float CheckEvery = 30f;
        private float _next;
        private float _cooldownUntil;
        private bool _busy;
        private readonly System.Random _rng = new System.Random();
        private static Hunts _instance;

        private void Awake() => _instance = this;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + CheckEvery;
            try { Check(); }
            catch (Exception e) { Plugin.DebugLog($"Hunts: {e.Message}"); }
        }

        private void Check()
        {
            if (!Plugin.HuntsEnabled.Value || _busy || Time.time < _cooldownUntil || Reveal.Active) return;
            var p = Player.m_localPlayer;
            var roster = Net.Known;
            if (p == null || p.IsDead() || roster == null) return;
            if (p.transform.position.y > 3000f) return;             // in a dungeon
            if (Returns.InWardPublic(p.transform.position)) return; // at home: that's what raids are for

            string here = GameInfo.Biome(p.transform.position);
            var hunters = Rules.Hunters(roster, here, p.GetPlayerID(), p.GetPlayerName(), Plugin.HuntAdjacentOnly.Value);
            if (hunters.Count == 0) return;

            double perHour = Plugin.HuntChancePerHour.Value * (IsNight() ? Plugin.HuntNightBoost.Value : 1f);
            double perCheck = 1.0 - Math.Pow(1.0 - Math.Min(0.99, perHour), CheckEvery / 3600.0);
            if (_rng.NextDouble() >= perCheck) return;

            // The Champion is the likeliest to bother.
            var champ = hunters.FirstOrDefault(n => n.Champion);
            var n = champ != null && _rng.NextDouble() < 0.4 ? champ : hunters[_rng.Next(hunters.Count)];
            StartCoroutine(Come(n, p, here));
        }

        /// <summary>Test button: a nemesis with a grudge against you (or any) comes for you now, wherever you are.</summary>
        internal static void ForceNow()
        {
            var p = Player.m_localPlayer;
            var roster = Net.Known;
            if (p == null || roster == null || _instance == null) { Plugin.ShowPlain("Grudges: get into a world first."); return; }
            if (_instance._busy) { Plugin.ShowPlain("Grudges: one is already on its way."); return; }
            string here = GameInfo.Biome(p.transform.position);
            var n = Rules.Hunters(roster, here, p.GetPlayerID(), p.GetPlayerName(), false).FirstOrDefault()
                    ?? roster.Active.FirstOrDefault(x => !string.Equals(x.Biome, here, StringComparison.OrdinalIgnoreCase))
                    ?? roster.Active.FirstOrDefault();
            if (n == null) { Plugin.ShowPlain("Grudges: no nemesis is out in the world."); return; }
            _instance.StartCoroutine(_instance.Come(n, p, here));
        }

        private IEnumerator Come(Nemesis n, Player p, string here)
        {
            _busy = true;
            try
            {
                Plugin.ShowMessage(Fill.Line(Plugin.Lines.Pick(new System.Random(), "msg-hunt-warning") ?? "Something has followed you here...",
                                             n, p.GetPlayerName(), 0, Rules.BiomeName(n.Biome)));
                yield return new WaitForSeconds(Plugin.HuntWarningSeconds.Value);
                if (p == null || p.IsDead() || Net.Known?.FindActive(n.Id) == null) yield break;

                int bonus = Rules.TravelBonus(n.Biome, GameInfo.Biome(p.transform.position));
                var c = Returns.SpawnBody(n, p, "hunt", bonus);
                if (c == null) { Plugin.DebugLog($"Hunts: no spot for {n.FullName} to appear."); yield break; }
                Plugin.Log.LogInfo($"Hunts: {n.FullName} followed {p.GetPlayerName()} from the {Rules.BiomeName(n.Biome)} to the {Rules.BiomeName(here)} (+{bonus} levels).");
                _cooldownUntil = Time.time + Plugin.HuntCooldownMinutes.Value * 60f;

                var rng = new System.Random();
                var info = new RevealInfo
                {
                    NemesisId = n.Id,
                    Zdo = GameInfo.ZdoKey(c),
                    Kind = "hunted",
                    Title = n.FullName,
                    Line = Fill.Line(Server.Rules.PickFor(rng, n, "reveal-hunted") ?? "You can't hide from me, {player}!", n, p.GetPlayerName(), 0, Rules.BiomeName(here)),
                };
                bool shown = Reveal.Instance != null && Reveal.Instance.Begin(info);
                if (!shown && Chat.instance != null)
                    Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.6f, 40f, Plugin.BubbleSeconds.Value, n.FullName, info.Line, false);
            }
            finally { _busy = false; }
        }

        private static bool IsNight()
        {
            try { return EnvMan.instance != null && Traverse.Create(EnvMan.instance).Method("IsNight").GetValue<bool>(); }
            catch { return false; }
        }
    }
}
