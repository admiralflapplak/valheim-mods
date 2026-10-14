using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Nemeses joining raids. Your game watches for a raid on you; if it brings a species that has nemeses (the
    /// Elder's "the forest is moving" raid brings Greydwarfs), it asks the server whether one joins. The first
    /// player to ask in a raid gets the body spawned near them; everyone who asks gets the announcement.
    /// </summary>
    internal class Raids : MonoBehaviour
    {
        private static Raids _instance;
        private float _next;
        private string _lastKey;
        private static bool _warned;

        private void Awake() => _instance = this;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 3f;
            try { Watch(); }
            catch (Exception e) { Plugin.DebugLog($"Raids: {e.Message}"); }
        }

        private void Watch()
        {
            var p = Player.m_localPlayer;
            if (p == null || p.IsDead() || RandEventSystem.instance == null) return;
            object ev = ActiveEvent();
            if (ev == null) { _lastKey = null; return; }

            var t = Traverse.Create(ev);
            string name = t.Field("m_name").GetValue<string>() ?? "raid";
            Vector3 pos = p.transform.position;
            try { pos = t.Field("m_pos").GetValue<Vector3>(); } catch { }
            string key = $"{name}@{Mathf.RoundToInt(pos.x / 50f)},{Mathf.RoundToInt(pos.z / 50f)}";
            if (key == _lastKey) return;
            _lastKey = key;

            var species = RaidSpecies(ev);
            var eligible = RuleSettings.ParseList(Plugin.SpeciesList.Value);
            Plugin.DebugLog($"Raids: '{name}' started, brings {string.Join(", ", species.ToArray())}.");
            if (!species.Any(eligible.Contains)) return;
            Net.RequestRaid(key, species, GameInfo.Biome(p.transform.position), false);
        }

        /// <summary>The raid happening around you, if any.</summary>
        private static object ActiveEvent()
        {
            try
            {
                var es = RandEventSystem.instance;
                var m = AccessTools.Method(typeof(RandEventSystem), "GetActiveEvent");
                object ev = m != null ? m.Invoke(es, null) : Traverse.Create(es).Field("m_activeEvent").GetValue();
                return ev;
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; Plugin.Log.LogWarning($"Raids: can't see the game's raids ({e.Message}); nemeses won't join them."); }
                return null;
            }
        }

        private static List<string> RaidSpecies(object ev)
        {
            var list = new List<string>();
            try
            {
                if (Traverse.Create(ev).Field("m_spawn").GetValue() is IEnumerable spawns)
                    foreach (var sd in spawns)
                    {
                        var prefab = Traverse.Create(sd).Field("m_prefab").GetValue<GameObject>();
                        if (prefab != null) list.Add(prefab.name);
                    }
            }
            catch { }
            return list.Distinct().ToList();
        }

        /// <summary>Test button: a raid on you right now, with a nemesis guaranteed (if any is out in the world).</summary>
        internal static void FakeRaid()
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            var species = RuleSettings.ParseList(Plugin.SpeciesList.Value).ToList();
            Net.RequestRaid($"test@{Time.time:0}", species, GameInfo.Biome(p.transform.position), true);
        }

        /// <summary>The server's answer: who joins, and whether we're the one to bring it.</summary>
        internal static void OnAnswer(Server.RaidAnswer a)
        {
            if (a == null || string.IsNullOrEmpty(a.NemesisId)) return;
            Plugin.ShowMessage(a.Message);
            if (a.Spawn && _instance != null) _instance.StartCoroutine(_instance.Arrive(a));
        }

        private IEnumerator Arrive(Server.RaidAnswer a)
        {
            // Let the raid get going first.
            yield return new WaitForSeconds(Plugin.RaidDelay.Value);
            var p = Player.m_localPlayer;
            var n = Net.Known?.FindActive(a.NemesisId);
            if (p == null || p.IsDead() || n == null) yield break;
            var c = Returns.SpawnBody(n, p, "raid");
            if (c == null) { Plugin.Log.LogInfo($"Raids: no spot for {n.FullName} to appear."); yield break; }
            Plugin.Log.LogInfo($"Raids: {n.FullName} joins the raid.");
            yield return new WaitForSeconds(1.5f);
            if (c != null && Chat.instance != null)
                Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.6f, 40f, Plugin.BubbleSeconds.Value, n.FullName, a.Line, false);
        }
    }
}
