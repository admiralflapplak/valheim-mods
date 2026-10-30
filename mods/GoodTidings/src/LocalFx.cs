using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The game's effects made on this game only. Most of its effect prefabs are networked (a ZNetView): made as they are, every player's
    /// game would add its own copy to the world for everyone (and their TimedDestruction only removes them on their owner). Made here
    /// with the network part held back (ZNetView.m_forceDisableInit, as the game does for previews) and taken off, they're plain
    /// local objects: each game shows its own, removed after <c>life</c> seconds.
    /// </summary>
    internal static class LocalFx
    {
        internal static GameObject Spawn(string prefab, Vector3 at, Quaternion rotation, Transform parent = null, float life = 8f, bool shake = false)
        {
            GameObject source = ZNetScene.instance?.GetPrefab(prefab);
            if (source == null) { Plugin.Log.LogWarning($"No effect {prefab}"); return null; }
            GameObject go;
            ZNetView.m_forceDisableInit = true;
            try { go = parent ? Object.Instantiate(source, at, rotation, parent) : Object.Instantiate(source, at, rotation); }
            finally { ZNetView.m_forceDisableInit = false; }
            foreach (TimedDestruction t in go.GetComponentsInChildren<TimedDestruction>(true)) Object.DestroyImmediate(t);
            if (!shake) foreach (CamShaker c in go.GetComponentsInChildren<CamShaker>(true)) Object.DestroyImmediate(c);
            foreach (ZSyncTransform z in go.GetComponentsInChildren<ZSyncTransform>(true)) Object.DestroyImmediate(z);
            foreach (ZNetView n in go.GetComponentsInChildren<ZNetView>(true)) Object.DestroyImmediate(n);
            if (life > 0f) Object.Destroy(go, life);
            return go;
        }
    }
}
