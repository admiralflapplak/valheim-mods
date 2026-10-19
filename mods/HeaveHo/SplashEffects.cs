using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HeaveHo
{
    /// <summary>
    /// Splash effects pulled from Valheim's own effect prefabs (the kind used for fish jumps, wading, things falling in the
    /// water), spawned where a blade meets the water. The first time one is needed, every water-looking effect name is logged
    /// so they can be tried in F1 under "4. Splashes" without a rebuild.
    /// </summary>
    internal static class SplashEffects
    {
        private static bool discovered;
        private static string cachedEntryText, cachedExitText;
        private static List<GameObject> entryPrefabs = new List<GameObject>();
        private static List<GameObject> exitPrefabs = new List<GameObject>();

        private static readonly string[] Keywords = { "water", "splash", "ripple", "wave", "swim", "wet", "fish", "wade" };

        internal static void Spawn(Vector3 pos, Quaternion rot, bool entry, bool hard)
        {
            if (ZNetScene.instance == null) return;
            if (!discovered) Discover();

            List<GameObject> prefabs = entry ? Resolve(Plugin.EntryEffects.Value, ref cachedEntryText, ref entryPrefabs)
                                             : Resolve(Plugin.ExitEffects.Value, ref cachedExitText, ref exitPrefabs);
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null) continue;
                GameObject go = SpawnLocal(prefab, pos, rot);
                if (go == null) continue;
                if (hard) go.transform.localScale *= 1.25f;
                // Effect prefabs normally clean themselves up; make sure ours never linger.
                Object.Destroy(go, 8f);
            }
        }

        /// <summary>Turns "a, b" into prefabs once per config change. Networked prefabs are skipped (local fx only).</summary>
        private static List<GameObject> Resolve(string text, ref string cachedText, ref List<GameObject> cache)
        {
            if (text == cachedText) return cache;
            cachedText = text;
            cache = new List<GameObject>();
            foreach (string raw in text.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                GameObject prefab = FindPrefab(name);
                if (prefab == null) { Plugin.Log.LogWarning($"Splash effect '{name}' not found."); continue; }
                if (prefab.GetComponent<ZNetView>() != null && ForceDisableInit == null)
                {
                    Plugin.Log.LogWarning($"Splash effect '{name}' is networked and this game version can't spawn it locally; skipping.");
                    continue;
                }
                cache.Add(prefab);
            }
            return cache;
        }

        // Valheim's own switch for spawning a networked prefab as a purely local copy (used for build ghosts).
        private static readonly System.Reflection.FieldInfo ForceDisableInit =
            AccessTools.Field(typeof(ZNetView), "m_forceDisableInit");

        /// <summary>
        /// Spawns an effect only on this client. Every player animates the oars and spawns their own splashes,
        /// so networking them would just duplicate them and spam the server.
        /// </summary>
        private static GameObject SpawnLocal(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            bool networked = prefab.GetComponent<ZNetView>() != null;
            if (!networked) return Object.Instantiate(prefab, pos, rot);
            if (ForceDisableInit == null) return null;

            object previous = ForceDisableInit.GetValue(null);
            ForceDisableInit.SetValue(null, true);
            try { return Object.Instantiate(prefab, pos, rot); }
            finally { ForceDisableInit.SetValue(null, previous); }
        }

        private static GameObject FindPrefab(string name)
        {
            foreach (GameObject go in AllPrefabs())
                if (go != null && go.name == name) return go;
            // Effects that only live inside other objects' EffectLists aren't registered; fall back to any loaded asset.
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == name && go.transform.parent == null && !go.scene.IsValid()) return go;
            return null;
        }

        private static IEnumerable<GameObject> AllPrefabs()
        {
            foreach (string field in new[] { "m_prefabs", "m_nonNetViewPrefabs" })
            {
                if (AccessTools.Field(typeof(ZNetScene), field)?.GetValue(ZNetScene.instance) is List<GameObject> list)
                    foreach (GameObject go in list) yield return go;
            }
        }

        /// <summary>Logs every effect-looking prefab with a water-ish name, once per session.</summary>
        internal static void Discover()
        {
            discovered = true;
            if (ZNetScene.instance == null) return;

            var hits = AllPrefabs()
                .Where(go => go != null)
                .Select(go => go.name)
                .Where(n =>
                {
                    string l = n.ToLowerInvariant();
                    bool fx = l.StartsWith("vfx") || l.StartsWith("fx") || l.StartsWith("sfx");
                    return fx && Keywords.Any(k => l.Contains(k));
                })
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            Plugin.Log.LogInfo("[Splash] water-related effects you can try in F1 (4. Splashes): " +
                               (hits.Count == 0 ? "none found" : string.Join(", ", hits)));
        }
    }
}
