using System.Collections.Generic;
using UnityEngine;

namespace Spyglass
{
    /// <summary>
    /// The spyglass's sounds, made from the game's own sound effects (the "sfx_" prefabs it plays for its pieces and items) so they go
    /// through its mixer and follow the player's volume settings. Played at the player, for the player: other players don't hear them.
    /// The names are settings, so a sound that doesn't suit can be swapped for another without a rebuild.
    /// </summary>
    internal static class Sound
    {
        private static GameObject _holder;
        private static readonly HashSet<string> Missing = new HashSet<string>();   // names already complained about, once each

        /// <summary>Plays the game's sound effect prefab by name, at the player: pitch and volume are multiples of its own.</summary>
        public static void Play(string prefabName, float pitch = 1f, float volume = 1f)
        {
            Player player = Player.m_localPlayer;
            if (player != null) PlayAt(prefabName, player.transform.position + Vector3.up * 1.4f, pitch, volume);
        }

        /// <summary>The same, from a place: another player's spyglass.</summary>
        public static void PlayAt(string prefabName, Vector3 position, float pitch = 1f, float volume = 1f)
        {
            if (!Plugin.Sounds.Value || string.IsNullOrEmpty(prefabName)) return;
            Player player = Player.m_localPlayer;
            ZNetScene scene = ZNetScene.instance;
            if (player == null || scene == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
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
                    _holder = new GameObject("nick_spyglass_sound_holder");
                    _holder.SetActive(false);
                    Object.DontDestroyOnLoad(_holder);
                }
                // A sound plays the moment it wakes, so it is made asleep (under an inactive holder), set up, and woken at the player.
                bool was = ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit = true;   // (never a networked object, whatever the prefab has)
                GameObject copy;
                try { copy = Object.Instantiate(prefab, _holder.transform); }
                finally { ZNetView.m_forceDisableInit = was; }

                float level = Mathf.Clamp01(Plugin.SoundVolume.Value) * volume;
                foreach (ZSFX sfx in copy.GetComponentsInChildren<ZSFX>(true))
                {
                    sfx.m_minPitch *= pitch;
                    sfx.m_maxPitch *= pitch;
                    sfx.m_minVol *= level;
                    sfx.m_maxVol *= level;
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
