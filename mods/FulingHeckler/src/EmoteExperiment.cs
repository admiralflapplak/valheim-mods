using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Lets the Fuling perform the player's emotes.
    ///
    /// The emote test proved the player's animations drive the Fuling's skeleton. The trick: the Fuling's
    /// animator has a "stagger" animation we can trigger at will. We swap that animation for a player emote
    /// (an AnimatorOverrideController), trigger stagger, and he performs the emote instead.
    /// </summary>
    internal class EmotePlayer
    {
        private static List<AnimationClip> _playerClips;
        private static bool _loggedClips;

        private readonly Animator _animator;
        private readonly RuntimeAnimatorController _original;
        private readonly AnimationClip _stagger;
        private readonly Dictionary<AnimationClip, AnimationClip> _base = new Dictionary<AnimationClip, AnimationClip>(); // e.g. torch pose
        private static bool _loggedOwnClips;

        public bool Ready => _original != null && _stagger != null;

        public EmotePlayer(Animator fuling)
        {
            _animator = fuling;
            _original = fuling != null ? fuling.runtimeAnimatorController : null;
            if (_original == null) return;

            _stagger = _original.animationClips
                .FirstOrDefault(c => c != null && c.name.IndexOf("stagger", StringComparison.OrdinalIgnoreCase) >= 0);
            if (_stagger == null)
                Plugin.Log.LogWarning("Emotes: couldn't find the Fuling's stagger animation to borrow; he'll use his own moves.");

            if (!_loggedOwnClips)
            {
                _loggedOwnClips = true;
                Plugin.Log.LogInfo("Fuling's own animations: " +
                    string.Join(", ", _original.animationClips.Where(c => c != null).Select(c => c.name).Distinct().OrderBy(n => n).ToArray()));
            }
        }

        /// <summary>
        /// Swaps his run and idle animations for the player's torch-carrying ones, so he holds the torch up
        /// like a player does instead of swinging it around.
        /// </summary>
        public void SetTorchPose(string runName, string idleName)
        {
            if (_original == null) return;
            AnimationClip run = Find(runName), idle = Find(idleName);
            var mapped = new List<string>();

            foreach (var c in _original.animationClips.Where(c => c != null).Distinct())
            {
                // Match whole words: "Drunk Idle" contains the letters "run" but is not a run.
                var words = new HashSet<string>(System.Text.RegularExpressions.Regex.Split(c.name.ToLowerInvariant(), "[^a-z0-9]+"));
                if (words.Contains("attack") || words.Contains("sleep") || words.Contains("dead") || words.Contains("death")) continue;
                if (run != null && words.Contains("run")) { _base[c] = run; mapped.Add($"{c.name}->{run.name}"); }
                else if (idle != null && words.Any(w => w == "idle" || w.StartsWith("idle"))) { _base[c] = idle; mapped.Add($"{c.name}->{idle.name}"); }
            }

            Plugin.Log.LogInfo(mapped.Count > 0
                ? "Torch pose: " + string.Join(", ", mapped.ToArray())
                : $"Torch pose: nothing to swap (run '{runName}' found={run != null}, idle '{idleName}' found={idle != null}).");
            Apply(null);
        }

        /// <summary>Builds a fresh override (the method proven by the emote test) with the torch pose and an optional emote.</summary>
        private void Apply(AnimationClip emote)
        {
            if (_original == null) return;
            if (_base.Count == 0 && emote == null)
            {
                if (_animator.runtimeAnimatorController != _original) _animator.runtimeAnimatorController = _original;
                return;
            }
            var overrides = new AnimatorOverrideController(_original);
            foreach (var kv in _base) overrides[kv.Key] = kv.Value;
            if (emote != null && _stagger != null) overrides[_stagger] = emote;
            _animator.runtimeAnimatorController = overrides;
        }

        /// <summary>Every animation the player has (emotes aren't reliably named "emote"), found once and cached.</summary>
        public static List<AnimationClip> PlayerClips()
        {
            if (_playerClips != null && _playerClips.Count > 0) return _playerClips;

            var list = new List<AnimationClip>();
            var player = Player.m_localPlayer;
            var anim = player != null ? player.GetComponentInChildren<Animator>() : null;
            if (anim != null && anim.runtimeAnimatorController != null)
                list = anim.runtimeAnimatorController.animationClips
                    .Where(c => c != null)
                    .GroupBy(c => c.name).Select(g => g.First())
                    .OrderBy(c => c.name)
                    .ToList();

            if (!_loggedClips && list.Count > 0)
            {
                _loggedClips = true;
                Plugin.Log.LogInfo($"Player animations ({list.Count}): " + string.Join(", ", list.Select(c => c.name).ToArray()));
            }
            _playerClips = list;
            return list;
        }

        /// <summary>Finds a player animation for one wanted name. Prefers ones that look like emotes, so "bow" doesn't pick the bow attack.</summary>
        public static AnimationClip Find(string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) return null;
            // The game's emote commands don't always match the animation names.
            if (Aliases.TryGetValue(Norm(wanted), out string alias)) wanted = alias;

            // Compare ignoring case, spaces and underscores, so "nonono" finds "No no no".
            string w = Norm(wanted);
            var hits = PlayerClips().Where(c => Norm(c.name).Contains(w)).ToList();
            if (hits.Count == 0) return null;
            return hits.FirstOrDefault(c => Norm(c.name) == w)
                ?? hits.FirstOrDefault(c => c.name.IndexOf("emote", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? hits.OrderBy(c => c.name.Length).First();
        }

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "comehere", "GetOverHere" },
            { "challenge", "ChallengeAccepted" },
        };

        private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        /// <summary>Plays one of the wanted emotes (matched by part of its name). Returns the clip played, or null.</summary>
        public AnimationClip TryPlay(IList<string> wanted, System.Random rng)
        {
            if (!Ready || wanted == null || wanted.Count == 0) return null;

            var options = wanted.Select(Find).Where(c => c != null).Distinct().ToList();
            if (options.Count == 0)
            {
                Plugin.Log.LogInfo($"Emotes: none of [{string.Join(", ", wanted.ToArray())}] matched a player animation. See the 'Player animations' log line for names.");
                return null;
            }

            var clip = options[rng.Next(options.Count)];
            Apply(clip);
            _animator.SetTrigger("stagger");
            Plugin.DebugLog($"Fuling performs {clip.name}.");
            return clip;
        }

        /// <summary>Puts his own animations back.</summary>
        public void Restore() => Apply(null);
    }

    /// <summary>The diagnostic behind the "Run emote test" button: logs rig details and has him wave.</summary>
    internal static class EmoteExperiment
    {
        public static void Run(Animator fuling, EmotePlayer emotes, System.Random rng)
        {
            var log = Plugin.Log;
            var player = Player.m_localPlayer;
            var playerAnim = player != null ? player.GetComponentInChildren<Animator>() : null;
            if (fuling == null || playerAnim == null) { log.LogWarning("Emote test: no Fuling or no player."); return; }

            var playerBones = new HashSet<string>(playerAnim.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            var fulingBones = new HashSet<string>(fuling.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            log.LogInfo($"Emote test: player rig humanoid={playerAnim.isHuman}, Fuling rig humanoid={fuling.isHuman}. " +
                        $"Bone names in common: {playerBones.Count(fulingBones.Contains)} of the player's {playerBones.Count}.");

            EmotePlayer.PlayerClips(); // logs the list
            var clip = emotes != null ? emotes.TryPlay(new[] { "wave" }, rng) : null;
            log.LogInfo(clip != null ? $"Emote test: playing '{clip.name}'." : "Emote test: couldn't play a wave.");
        }
    }
}
