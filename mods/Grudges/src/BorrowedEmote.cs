using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Plays a player emote (laugh, point, flex...) on a creature, the same trick the Fuling Heckler uses:
    /// the creature's own "stagger" animation is swapped for the player's emote and then triggered.
    ///
    /// This only looks right when the creature's skeleton matches the player's (the Fuling's does). So each
    /// creature type is checked once: its rig type and how many bone names it shares with the player.
    /// Ones that don't match are left alone and fall back to their own animations.
    /// </summary>
    internal static class BorrowedEmote
    {
        private const int MinSharedBones = 15;
        private static List<AnimationClip> _playerClips;
        private static HashSet<string> _playerBones;
        private static readonly Dictionary<string, bool> _compatible = new Dictionary<string, bool>();
        private static readonly System.Random Rng = new System.Random();

        /// <param name="length">How long the emote plays.</param>
        /// <param name="restore">Puts the creature's own animations back. Call it when nobody's looking: swapping
        /// animation sets resets the creature's pose on the spot, which shows as a snap.</param>
        internal static bool TryPlay(Character c, string emoteList, out float length, out Action restore)
        {
            length = 0f;
            restore = null;
            try
            {
                var anim = c.GetComponentInChildren<Animator>();
                if (anim == null || anim.runtimeAnimatorController == null) return false;
                if (!Compatible(c, anim)) return false;

                // Already mid-emote (its animations are still swapped): don't stack another on top.
                if (anim.runtimeAnimatorController is AnimatorOverrideController) return false;
                var original = anim.runtimeAnimatorController;
                var stagger = original.animationClips.FirstOrDefault(x => x != null && x.name.IndexOf("stagger", StringComparison.OrdinalIgnoreCase) >= 0);
                if (stagger == null) return false;

                var wanted = RuleSettings.ParseList(emoteList).ToList();
                var options = wanted.Select(Find).Where(x => x != null).Distinct().ToList();
                if (options.Count == 0)
                {
                    Plugin.Log.LogInfo($"Emotes: none of [{emoteList}] matched a player animation. See the 'Player animations' log line.");
                    return false;
                }
                var clip = options[Rng.Next(options.Count)];

                var overrides = new AnimatorOverrideController(original);
                overrides[stagger] = clip;
                anim.runtimeAnimatorController = overrides;
                anim.SetTrigger("stagger");
                Plugin.DebugLog($"{GameInfo.Prefab(c)} performs the player's '{clip.name}'.");
                length = Mathf.Clamp(clip.length, 0.5f, 6f);
                restore = () => { if (anim != null && anim.runtimeAnimatorController != original) anim.runtimeAnimatorController = original; };
                return true;
            }
            catch (Exception e)
            {
                Plugin.DebugLog($"Reveal: player emote failed ({e.Message}).");
                return false;
            }
        }

        /// <summary>Checks once per creature type whether its skeleton can take player animations, and logs why.</summary>
        private static bool Compatible(Character c, Animator anim)
        {
            string species = GameInfo.Prefab(c);
            if (_compatible.TryGetValue(species, out bool ok)) return ok;

            var playerAnim = PlayerAnimator();
            if (playerAnim == null) return false; // try again next time
            if (_playerBones == null)
                _playerBones = new HashSet<string>(playerAnim.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            var bones = new HashSet<string>(anim.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            int shared = _playerBones.Count(bones.Contains);

            ok = (anim.isHuman && playerAnim.isHuman) || shared >= MinSharedBones;
            _compatible[species] = ok;
            Plugin.Log.LogInfo($"Player emotes on {species}: {(ok ? "yes" : "no")} " +
                               $"(humanoid rig: {anim.isHuman}, player humanoid: {playerAnim.isHuman}, bone names shared with the player: {shared}). " +
                               (ok ? "" : "It'll use its own animations instead."));
            PlayerClips(); // logs the list once
            return ok;
        }

        /// <summary>The player's animator: from the player prefab, so it works while you're dead.</summary>
        private static Animator PlayerAnimator()
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
            var anim = prefab != null ? prefab.GetComponentInChildren<Animator>() : null;
            if (anim == null && Player.m_localPlayer != null) anim = Player.m_localPlayer.GetComponentInChildren<Animator>();
            return anim;
        }

        private static List<AnimationClip> PlayerClips()
        {
            if (_playerClips != null && _playerClips.Count > 0) return _playerClips;
            var anim = PlayerAnimator();
            _playerClips = anim != null && anim.runtimeAnimatorController != null
                ? anim.runtimeAnimatorController.animationClips.Where(x => x != null).GroupBy(x => x.name).Select(g => g.First()).OrderBy(x => x.name).ToList()
                : new List<AnimationClip>();
            if (_playerClips.Count > 0)
                Plugin.Log.LogInfo($"Player animations ({_playerClips.Count}): " + string.Join(", ", _playerClips.Select(x => x.name).ToArray()));
            return _playerClips;
        }

        /// <summary>Finds a player animation by part of its name, preferring emotes (so "bow" isn't the bow attack).</summary>
        private static AnimationClip Find(string wanted)
        {
            string w = Norm(wanted);
            if (w.Length == 0) return null;
            // The game's emote commands don't always match its animation names.
            if (w == "comehere") w = "getoverhere";
            if (w == "kneel") w = "kneelloop";
            if (w == "challenge") w = "challengeaccepted";
            var hits = PlayerClips().Where(x => Norm(x.name).Contains(w)).ToList();
            if (hits.Count == 0) return null;
            return hits.FirstOrDefault(x => Norm(x.name) == w)
                   ?? hits.FirstOrDefault(x => x.name.IndexOf("emote", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? hits.OrderBy(x => x.name.Length).First();
        }

        private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
