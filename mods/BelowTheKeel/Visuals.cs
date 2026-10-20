using System.Collections.Generic;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Everything an omen shows: plain scene objects, never networked (no ZNetView, so no ZDO and nothing saved in the world), with no
    /// colliders or physics (nothing can touch the ship or a player). Each goes away on its own after its time; all of them go when the
    /// mod unloads, so a hot reload leaves nothing behind.
    /// </summary>
    internal static class Visuals
    {
        private static readonly List<GameObject> Live = new List<GameObject>();
        private static GameObject _holder;   // inactive: a game prefab cloned under it never runs its Awake

        /// <summary>An empty object at a place in the world, gone after the given seconds.</summary>
        internal static GameObject Spawn(string name, Vector3 position, Quaternion rotation, float seconds)
        {
            var go = new GameObject("BelowTheKeel_" + name);
            go.transform.SetPositionAndRotation(position, rotation);
            Track(go, seconds);
            return go;
        }

        /// <summary>
        /// The look of one of the game's prefabs (its meshes, materials, animator) with everything else taken off: no ZNetView, AI,
        /// colliders, physics or the game's own scripts. Only its look (the child named <paramref name="part"/>, "Visual" on creatures)
        /// is copied, never the root: a creature's root carries Character, which registers itself with the game as soon as it wakes.
        /// Null when the game has no prefab by that name.
        /// </summary>
        internal static GameObject CloneLook(string prefabName, Transform parent, string part = "Visual")
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"No prefab named {prefabName}");
                return null;
            }

            if (_holder == null)
            {
                _holder = new GameObject("BelowTheKeel_Holder");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
            }

            Transform source = part != null ? prefab.transform.Find(part) : prefab.transform;
            if (source == null)
            {
                Plugin.Log.LogWarning($"{prefabName} has no {part}");
                return null;
            }

            GameObject copy = Object.Instantiate(source.gameObject, _holder.transform);
            // In passes: Unity refuses to remove a script another one still depends on (RequireComponent).
            for (int pass = 0; pass < 5; pass++)
            {
                MonoBehaviour[] left = copy.GetComponentsInChildren<MonoBehaviour>(true);
                if (left.Length == 0) break;
                foreach (MonoBehaviour mb in left) Object.DestroyImmediate(mb);
            }
            if (copy.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
            {
                Plugin.Log.LogWarning($"Couldn't strip every script off {prefabName}: not shown");
                Object.DestroyImmediate(copy);
                return null;
            }
            foreach (Joint j in copy.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);

            copy.name = "BelowTheKeel_" + prefabName;
            copy.transform.SetParent(parent, false);
            return copy;
        }

        internal static void Track(GameObject go, float seconds)
        {
            Live.Add(go);
            Object.Destroy(go, seconds);
        }

        internal static int Count
        {
            get
            {
                Live.RemoveAll(go => go == null);
                return Live.Count;
            }
        }

        /// <summary>Where each one showing is (for "keel status": aiming a camera at one).</summary>
        internal static IEnumerable<string> Where()
        {
            Live.RemoveAll(go => go == null);
            foreach (GameObject go in Live)
            {
                Vector3 p = go.transform.position;
                yield return $"{go.name.Replace("BelowTheKeel_", "")} at {p.x:0.0} {p.y:0.0} {p.z:0.0}, facing {go.transform.eulerAngles.y:0}";
            }
        }

        /// <summary>Every renderer of the omens showing (for "keel plate ... cut omens").</summary>
        internal static List<Renderer> Renderers()
        {
            Live.RemoveAll(go => go == null);
            var all = new List<Renderer>();
            foreach (GameObject go in Live) all.AddRange(go.GetComponentsInChildren<Renderer>());
            return all;
        }

        internal static void ClearAll()
        {
            foreach (GameObject go in Live)
                if (go != null) Object.Destroy(go);
            Live.Clear();
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
        }
    }
}
