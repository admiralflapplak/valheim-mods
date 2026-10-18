using System.Collections.Generic;
using UnityEngine;

namespace WeaponOils
{
    /// <summary>
    /// Plays one of the game's own sound effects (the "sfx_" prefabs it plays for its pieces and items) so it goes through the game's
    /// mixer and follows the player's volume settings, at a volume and pitch of ours (multiples of the effect's own). Played at the
    /// player, for the player: other players don't hear it. The name is a setting, so a sound that doesn't suit can be swapped for another
    /// without a rebuild. (The same as the Spyglass mod's sound player.)
    /// </summary>
    internal static class Sound
    {
        private static GameObject _holder;
        private static readonly HashSet<string> Missing = new HashSet<string>();   // names already complained about, once each

        public static void PlayAt(string prefabName, Vector3 position, float pitch = 1f, float volume = 1f)
        {
            if (string.IsNullOrWhiteSpace(prefabName)) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            prefabName = prefabName.Trim();
            try
            {
                GameObject prefab = scene.GetPrefab(prefabName);
                if (prefab == null)
                {
                    if (Missing.Add(prefabName)) Plugin.Log.LogWarning($"No sound effect called \"{prefabName}\" (Claude Tools' `prefabs sfx_` lists them)");
                    return;
                }
                if (_holder == null)
                {
                    _holder = new GameObject("nick_weaponoils_sound_holder");
                    _holder.SetActive(false);
                    Object.DontDestroyOnLoad(_holder);
                }
                // A sound plays the moment it wakes, so it is made asleep (under an inactive holder), set up, and woken at the player.
                bool was = ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit = true;   // (never a networked object, whatever the prefab has)
                GameObject copy;
                try { copy = Object.Instantiate(prefab, _holder.transform); }
                finally { ZNetView.m_forceDisableInit = was; }

                foreach (ZSFX sfx in copy.GetComponentsInChildren<ZSFX>(true))
                {
                    sfx.m_minPitch *= pitch;
                    sfx.m_maxPitch *= pitch;
                    sfx.m_minVol *= volume;
                    sfx.m_maxVol *= volume;
                }
                copy.transform.SetPositionAndRotation(position, Quaternion.identity);
                copy.transform.SetParent(null, true);   // (awake from here)
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not play {prefabName}: {e.Message}");
            }
        }

        public static void Reset()
        {
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
            Missing.Clear();
        }
    }
}
