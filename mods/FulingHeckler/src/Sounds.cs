using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace FulingHeckler
{
    /// <summary>The moments he can make a sound. Each has its own folder of recordings.</summary>
    public enum SoundMoment { Arrive, Taunt, Poof, Flee, Dance, Caught, Spotted, Bored, Boo, Steal, CoinDrop, Gift }

    /// <summary>
    /// Your own recordings: BepInEx/config/FulingHeckler_sounds/{arrive,taunt,poof,flee}/*.ogg|*.wav.
    /// One file per moment is picked at random each time. Empty folder = silence for that moment.
    /// </summary>
    internal class Sounds : MonoBehaviour
    {
        public static Sounds Instance;
        public static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, "FulingHeckler_sounds");

        private readonly Dictionary<SoundMoment, List<AudioClip>> _clips = new Dictionary<SoundMoment, List<AudioClip>>();
        private readonly System.Random _rng = new System.Random();
        private AudioMixerGroup _sfxGroup;
        private bool _lookedForMixer;
        private bool _loading;

        private void Awake()
        {
            Instance = this;
            EnsureFolders();
            Reload();
        }

        public static string FolderFor(SoundMoment m) => Path.Combine(Folder, FolderName(m));

        private static string FolderName(SoundMoment m) => m == SoundMoment.CoinDrop ? "coin_drop" : m.ToString().ToLowerInvariant();

        /// <summary>If a scene's own folder is empty, use this more general one instead (null = stay silent).</summary>
        private static SoundMoment? FallbackFor(SoundMoment m)
        {
            switch (m)
            {
                case SoundMoment.Caught:
                case SoundMoment.Spotted: return SoundMoment.Flee;
                case SoundMoment.Boo:
                case SoundMoment.Steal: return SoundMoment.Taunt;
                default: return null;
            }
        }

        private static void EnsureFolders()
        {
            try
            {
                foreach (SoundMoment m in Enum.GetValues(typeof(SoundMoment))) Directory.CreateDirectory(FolderFor(m));
                // Always rewrite the README so it lists every folder, including new ones.
                File.WriteAllText(Path.Combine(Folder, "README.txt"),
                    "Put your Fuling recordings here (.ogg, .wav or .mp3). Short, mono, trimmed tight works best.\n" +
                    "Several files in a folder = he picks one at random each time.\n" +
                    "After adding or changing files, press 'Reload sounds' in the F1 menu (5 - Puffs & Sounds). No restart needed.\n\n" +
                    "  arrive/     he yells one of these as he runs over\n" +
                    "  taunt/      when he does his bit (emote, swing, play dead)\n" +
                    "  poof/       as he vanishes in smoke\n" +
                    "  flee/       when you get too close and he bolts\n\n" +
                    "Scenes (if a folder is empty, the one in brackets is used instead):\n" +
                    "  dance/      while he dances on your grave; clips play one after another (music, humming...)\n" +
                    "  caught/     when you catch him dancing or by your fire     [flee]\n" +
                    "  spotted/    when you spot the Lurker                         [flee]\n" +
                    "  bored/      when the Lurker gives up and leaves\n" +
                    "  boo/        the Red Light Green Light BOO                   [taunt]\n\n" +
                    "Coin thief:\n" +
                    "  steal/      when he grabs your coins                        [taunt]\n" +
                    "  coin_drop/  each coin hitting the ground as he runs\n\n" +
                    "Gifter:\n" +
                    "  gift/       his giggle as he slips things into your pockets\n" +
                    "  (caught/ is used when you catch him)\n");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't create the sounds folder: {e.Message}"); }
        }

        public void Reload()
        {
            if (_loading) return;
            StartCoroutine(LoadAll());
        }

        private IEnumerator LoadAll()
        {
            _loading = true;
            var fresh = new Dictionary<SoundMoment, List<AudioClip>>();
            int total = 0;

            foreach (SoundMoment m in Enum.GetValues(typeof(SoundMoment)))
            {
                var list = new List<AudioClip>();
                string dir = FolderFor(m);
                string[] files = Directory.Exists(dir)
                    ? Directory.GetFiles(dir).Where(f => TypeFor(f) != AudioType.UNKNOWN).OrderBy(f => f).ToArray()
                    : new string[0];

                foreach (var file in files)
                {
                    AudioType type = TypeFor(file);
                    string url = new Uri(Path.GetFullPath(file)).AbsoluteUri; // handles spaces and odd characters
                    using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
                    {
                        yield return req.SendWebRequest();
                        if (!string.IsNullOrEmpty(req.error))
                        {
                            Plugin.Log.LogWarning($"Sound '{Path.GetFileName(file)}' couldn't be loaded: {req.error}");
                            continue;
                        }
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                        if (clip == null || clip.length <= 0f)
                        {
                            Plugin.Log.LogWarning($"Sound '{Path.GetFileName(file)}' is empty or not a supported format.");
                            continue;
                        }
                        clip.name = Path.GetFileNameWithoutExtension(file);
                        list.Add(clip);
                    }
                }
                fresh[m] = list;
                total += list.Count;
            }

            foreach (var old in _clips.Values.SelectMany(l => l)) if (old != null) Destroy(old);
            _clips.Clear();
            foreach (var kv in fresh) _clips[kv.Key] = kv.Value;

            Plugin.Log.LogInfo("Sounds loaded: " + string.Join(", ",
                _clips.Where(kv => kv.Value.Count > 0).Select(kv => $"{FolderName(kv.Key)} {kv.Value.Count}").DefaultIfEmpty("none").ToArray()) + $" (in {Folder}).");
            _loading = false;
            if (total == 0) Plugin.DebugLog("No custom sounds yet; he stays quiet.");
        }

        private static AudioType TypeFor(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".wav" ? AudioType.WAV : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
        }

        private AudioClip Pick(SoundMoment m)
        {
            if (_clips.TryGetValue(m, out var list) && list.Count > 0) return list[_rng.Next(list.Count)];
            var fallback = FallbackFor(m);
            return fallback.HasValue ? Pick(fallback.Value) : null;
        }

        /// <summary>
        /// For continuous sound (his dance): if nothing is playing on his dedicated channel, start the next clip.
        /// Call every frame while he's dancing; call StopLoop when he stops.
        /// </summary>
        public void KeepPlaying(GameObject who, SoundMoment m)
        {
            if (!Plugin.SoundsEnabled.Value || who == null) return;
            var src = LoopSource(who);
            if (src.isPlaying) return;
            var clip = Pick(m);
            if (clip == null) return;
            src.clip = clip;
            src.volume = Plugin.SoundVolume.Value;
            src.Play();
        }

        public void StopLoop(GameObject who)
        {
            var t = who != null ? who.transform.Find("FulingHeckler_LoopAudio") : null;
            var src = t != null ? t.GetComponent<AudioSource>() : null;
            if (src != null) src.Stop();
        }

        private AudioSource LoopSource(GameObject who)
        {
            var t = who.transform.Find("FulingHeckler_LoopAudio");
            if (t == null)
            {
                var go = new GameObject("FulingHeckler_LoopAudio");
                go.transform.SetParent(who.transform, false);
                var created = go.AddComponent<AudioSource>();
                Setup(created);
                return created;
            }
            return t.GetComponent<AudioSource>();
        }

        /// <summary>Plays from the Fuling himself, so it follows him as he runs.</summary>
        public void PlayOn(GameObject who, SoundMoment m)
        {
            if (!Plugin.SoundsEnabled.Value || who == null) return;
            var clip = Pick(m);
            if (clip == null) return;
            var src = who.GetComponent<AudioSource>();
            if (src == null) src = who.AddComponent<AudioSource>();
            Setup(src);
            src.PlayOneShot(clip, Plugin.SoundVolume.Value);
            Plugin.DebugLog($"Sound ({m}): {clip.name}");
        }

        /// <summary>Plays at a spot in the world, for when he's about to vanish (the sound outlives him).</summary>
        public void PlayAt(Vector3 position, SoundMoment m)
        {
            if (!Plugin.SoundsEnabled.Value) return;
            var clip = Pick(m);
            if (clip == null) return;
            var go = new GameObject("FulingHeckler_Sound");
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            Setup(src);
            src.PlayOneShot(clip, Plugin.SoundVolume.Value);
            Destroy(go, clip.length + 0.5f);
            Plugin.DebugLog($"Sound ({m}): {clip.name}");
        }

        private void Setup(AudioSource src)
        {
            src.playOnAwake = false;
            src.spatialBlend = 1f;                 // 3D: comes from where he is
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4f;
            src.maxDistance = 45f;
            src.dopplerLevel = 0f;
            var group = SfxGroup();
            if (group != null) src.outputAudioMixerGroup = group;
        }

        /// <summary>
        /// Routes our sounds through the game's sound-effects channel (so the in-game SFX volume applies)
        /// by borrowing the mixer setting from one of the game's own sound effects.
        /// </summary>
        private AudioMixerGroup SfxGroup()
        {
            if (_lookedForMixer) return _sfxGroup;
            if (ZNetScene.instance == null) return null;
            _lookedForMixer = true;
            try
            {
                var prefabs = Traverse.Create(ZNetScene.instance).Field("m_prefabs").GetValue<List<GameObject>>();
                if (prefabs != null)
                    foreach (var p in prefabs.Where(p => p != null && p.name.StartsWith("sfx_", StringComparison.OrdinalIgnoreCase)))
                    {
                        var a = p.GetComponentInChildren<AudioSource>(true);
                        if (a != null && a.outputAudioMixerGroup != null) { _sfxGroup = a.outputAudioMixerGroup; break; }
                    }
            }
            catch { /* fall back to the master channel */ }
            Plugin.Log.LogInfo(_sfxGroup != null
                ? $"Custom sounds use the game's '{_sfxGroup.name}' volume channel."
                : "Custom sounds couldn't find the game's effects channel; they follow master volume only.");
            return _sfxGroup;
        }
    }
}
