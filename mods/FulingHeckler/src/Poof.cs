using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// A puff of smoke when he appears and disappears, borrowed from the game's own effects (Odin's vanish, by default).
    /// Same safety rule as the Fuling: the effect is copied while inactive and every script is stripped, so it's
    /// purely visual (plus its sound), only on your screen, and cleaned up after a few seconds.
    /// </summary>
    internal static class Poof
    {
        private const float Lifetime = 8f;

        private static GameObject _staging;
        private static bool _loggedCandidates;

        /// <summary>Plays every effect in the list at once, layered. Names that don't exist are skipped (and logged once).</summary>
        public static void At(Vector3 position, string effectList)
        {
            if (!Plugin.PoofEnabled.Value) return;
            LogCandidatesOnce();
            foreach (var name in NameList.Parse(effectList)) PlayOne(name, position);
        }

        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void PlayOne(string name, Vector3 position)
        {
            try
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
                if (prefab == null)
                {
                    if (_warned.Add(name))
                        Plugin.Log.LogWarning($"Smoke puff: no effect called '{name}'. See the 'Effects you could use' log line for real names.");
                    return;
                }

                if (_staging == null)
                {
                    _staging = new GameObject("FulingHeckler_PoofStaging");
                    _staging.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_staging);
                }

                GameObject fx = UnityEngine.Object.Instantiate(prefab, _staging.transform, false);
                Gear.Strip(fx); // removes its scripts, including any network component

                fx.transform.SetParent(null, false);
                fx.transform.position = position;
                fx.transform.rotation = Quaternion.identity;
                fx.transform.localScale = prefab.transform.localScale * Plugin.PoofSize.Value;

                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);
                foreach (var a in fx.GetComponentsInChildren<AudioSource>(true))
                {
                    a.enabled = Plugin.PoofSound.Value;
                    if (a.enabled) a.Play();
                }

                UnityEngine.Object.Destroy(fx, Lifetime);
                Plugin.DebugLog($"Smoke puff: {prefab.name}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Smoke puff '{name}' failed ({e.GetType().Name}: {e.Message}).");
            }
        }

        /// <summary>Lists the game's smoke/spawn/vanish effects once, so better names can be picked.</summary>
        private static void LogCandidatesOnce()
        {
            if (_loggedCandidates || ZNetScene.instance == null) return;
            _loggedCandidates = true;
            try
            {
                var prefabs = ZNetScene.instance.m_prefabs;
                if (prefabs == null) return;
                string[] keys = { "odin", "smoke", "poof", "puff", "spawn", "despawn", "teleport", "ghost", "vanish" };
                var names = prefabs.Where(p => p != null)
                    .Select(p => p.name)
                    .Where(n => (n.StartsWith("vfx", StringComparison.OrdinalIgnoreCase) || n.StartsWith("fx", StringComparison.OrdinalIgnoreCase))
                                && keys.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Distinct().OrderBy(n => n).ToArray();
                Plugin.Log.LogInfo("Effects you could use in 'Arrival Effects' / 'Exit Effects': " + (names.Length > 0 ? string.Join(", ", names) : "none found"));
            }
            catch { /* just a helper */ }
        }
    }
}
