using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// The moment after your death. The camera turns to the creature, it transforms into its nemesis self
    /// (or, if it already was one, gloats), says its line, turns and walks off as the screen fades to black.
    ///
    /// Only the player who died sees it. The camera is borrowed by pausing the game camera's own update;
    /// everything is put back at the end, or straight away if you respawn early.
    /// </summary>
    internal class Reveal : MonoBehaviour
    {
        internal static Reveal Instance;
        internal static bool Active { get; private set; }

        private static bool _cameraHooked;
        private static bool _cameraHeld;
        private static float _deathTime = -999f;
        private static Vector3 _deathPos;
        private static readonly Dictionary<string, float> _held = new Dictionary<string, float>();
        private static readonly HashSet<string> _loggedAnimators = new HashSet<string>();
        private static MethodInfo _setMoveDir, _setLookDir, _setRun;

        private float _fade, _fadeTarget, _fadeSpeed = 1f;
        private Action _emoteRestore;
        private float _emoteEndsAt;
        private Texture2D _black;

        private void Awake() => Instance = this;

        // ---- Hooks ---------------------------------------------------------------

        public static void Patch(Harmony harmony)
        {
            try
            {
                var camUpdate = AccessTools.Method(typeof(GameCamera), "LateUpdate");
                if (camUpdate == null)
                {
                    Plugin.Log.LogWarning("Reveal: couldn't find the game camera's update, so there's no death reveal (messages still work).");
                    return;
                }
                harmony.Patch(camUpdate, prefix: new HarmonyMethod(typeof(Reveal), nameof(CameraPrefix)));
                _cameraHooked = true;
                _setMoveDir = AccessTools.Method(typeof(Character), "SetMoveDir");
                _setLookDir = AccessTools.Method(typeof(Character), "SetLookDir");
                _setRun = AccessTools.Method(typeof(Character), "SetRun");
                if (_setMoveDir == null) Plugin.Log.LogInfo("Reveal: no way to steer creatures found, so they won't walk off.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Reveal: couldn't hook the camera ({e.Message}). No death reveal."); }
        }

        /// <summary>While the reveal has the camera, the game camera skips its update.</summary>
        private static bool CameraPrefix() => !_cameraHeld;

        /// <summary>Called from the death patch, before the report goes out.</summary>
        internal static void NoteDeath(Vector3 pos)
        {
            _deathTime = Time.time;
            _deathPos = pos;
        }

        /// <summary>A newly promoted creature keeps its old look until the camera is on it.</summary>
        internal static bool IsHeld(string id)
        {
            if (string.IsNullOrEmpty(id) || !_held.TryGetValue(id, out float until)) return false;
            if (Time.time < until) return true;
            _held.Remove(id);
            return false;
        }

        // ---- Start ---------------------------------------------------------------

        /// <summary>Starts the reveal. False if it can't play (then the caller shows plain messages instead).</summary>
        internal bool Begin(RevealInfo info)
        {
            if (!Plugin.RevealEnabled.Value || !_cameraHooked || Active) return false;
            if (info.IsTest && !Plugin.RevealOnTests.Value) return false;
            if (!Live(info) && Time.time - _deathTime > 20f) return false; // not about a death we just had
            if ((info.Kind == "returned" || info.Kind == "hunted") && !Plugin.ReturnCutscene.Value) return false;
            if (IsPromotion(info)) _held[info.NemesisId] = Time.time + 20f;
            Active = true;
            StartCoroutine(Run(info));
            return true;
        }

        private static bool IsPromotion(RevealInfo info) => info.Kind == "promoted" || info.Kind == "promoted-witness";
        /// <summary>A scene while you're alive (a test button, or a nemesis coming back for you): no fade, short.</summary>
        private static bool Live(RevealInfo info) => info.IsTest || info.Kind == "returned" || info.Kind == "hunted";

        private static string EmotesFor(RevealInfo info)
        {
            // Its personality's emotes for this moment, if it has some.
            var n = Net.Known?.Find(info.NemesisId);
            string key = info.Kind == "promoted" ? "emotes-new-killer" : info.Kind == "promoted-witness" ? "emotes-witness"
                       : info.Kind == "stronger" ? "emotes-killed-again" : info.Kind == "saw" ? "emotes-saw-again"
                       : info.Kind == "returned" || info.Kind == "hunted" ? "emotes-returned" : "emotes-on-sight";
            return Plugin.PersonaValue(n, key); // falls back to [Default] in the personalities file
        }

        private static Character Find(RevealInfo info)
        {
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c is Player || c.IsDead()) continue;
                if (GameInfo.TagOf(c) == info.NemesisId) return c;
                if (info.Zdo.Length > 0 && GameInfo.ZdoKey(c) == info.Zdo) return c;
            }
            return null;
        }

        // ---- The scene -------------------------------------------------------------

        private IEnumerator Run(RevealInfo info)
        {
            Character c = null;
            BaseAI ai = null;
            bool aiPaused = false;
            bool live = Live(info);
            bool ambush = info.Kind == "returned" || info.Kind == "hunted";
            Vector3 anchor = live && Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : _deathPos;

            try
            {
                float startAt = live ? Time.time : _deathTime + Plugin.RevealDelay.Value;
                while (Time.time < startAt) yield return null;

                float giveUp = Time.time + 3f;
                while ((c = Find(info)) == null && Time.time < giveUp) yield return new WaitForSeconds(0.25f);
                var cam = Camera.main;
                if (c == null || cam == null)
                {
                    Plugin.DebugLog($"Reveal: couldn't find {(c == null ? "the creature" : "the camera")}; showing a message instead.");
                    Net.Enqueue(info.Messages.FirstOrDefault());
                    yield break;
                }

                bool owner = GameInfo.IsOwner(c);
                ai = c.GetComponent<BaseAI>();
                Vector3 fromPos = cam.transform.position;
                Quaternion fromRot = cam.transform.rotation;
                _cameraHeld = true;

                // A three-quarter shot from the side the player died on.
                Vector3 side = Flat(anchor - c.transform.position);
                if (side.sqrMagnitude < 0.01f) side = Flat(c.transform.forward);
                side = Quaternion.Euler(0f, 30f, 0f) * side.normalized;

                Vector3 shotPos = Vector3.zero, lookAt = Vector3.zero;
                bool transformed = false, spoke = false, captioned = false, walking = false;
                Vector3 walkDir = -side;
                float t0 = Time.time;
                const float EaseIn = 1.0f, TransformAt = 1.3f, SpeakAt = 1.8f, CaptionAt = 3.0f, EarliestWalk = 4.3f;
                float walkAt = EarliestWalk, FadeAt = walkAt + 1.3f, End = walkAt + 2.5f;
                _emoteRestore = null;
                _emoteEndsAt = 0f;

                while (true)
                {
                    float t = Time.time - t0;
                    if (!live && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead() && t > 0.5f) break; // respawned early
                    if (live && Player.m_localPlayer != null && Player.m_localPlayer.IsDead()) break;
                    if (ambush && spoke)
                    {
                        // Coming back for you: say its piece, then hand the camera back so you can fight.
                        float done = Mathf.Max(SpeakAt + 2.2f, t + (_emoteEndsAt - Time.time) + 0.2f);
                        if (t >= Mathf.Min(done, 6f)) break;
                    }
                    if (t >= End) break;
                    if (c == null || cam == null)
                    {
                        // It died or vanished mid-scene: cut to black.
                        if (!live) { _fadeTarget = 1f; _fadeSpeed = 2f; }
                        break;
                    }

                    float size = Mathf.Max(0.6f, c.transform.localScale.y);
                    if (!walking)
                    {
                        shotPos = c.transform.position + side * Plugin.RevealCameraDistance.Value * size + Vector3.up * Plugin.RevealCameraHeight.Value * size;
                        lookAt = c.transform.position + Vector3.up * 1.0f * size;
                    }
                    else
                    {
                        // Let it walk away from a camera that slowly rises.
                        shotPos += Vector3.up * 0.5f * Time.deltaTime;
                        lookAt = Vector3.Lerp(lookAt, c.transform.position + Vector3.up * 1.0f * size, 4f * Time.deltaTime);
                    }

                    Quaternion shotRot = Quaternion.LookRotation(lookAt - shotPos);
                    if (t < EaseIn)
                    {
                        float k = Smooth(t / EaseIn);
                        cam.transform.position = Vector3.Lerp(fromPos, shotPos, k);
                        cam.transform.rotation = Quaternion.Slerp(fromRot, shotRot, k);
                    }
                    else
                    {
                        cam.transform.position = Vector3.Lerp(cam.transform.position, shotPos, 6f * Time.deltaTime);
                        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, shotRot, 6f * Time.deltaTime);
                    }

                    // Face the camera while it has the spotlight.
                    if (owner && !walking && t > EaseIn) Steer(c, Vector3.zero, Flat(cam.transform.position - c.transform.position));

                    if (!transformed && t >= TransformAt)
                    {
                        transformed = true;
                        Transform(c, info);
                    }
                    if (!spoke && t >= SpeakAt)
                    {
                        spoke = true;
                        Speak(c, info);
                    }
                    if (!captioned && t >= CaptionAt)
                    {
                        captioned = true;
                        Plugin.ShowMessage(info.Messages.FirstOrDefault());
                    }
                    if (!walking && t >= EarliestWalk && Time.time < _emoteEndsAt)
                    {
                        // Still mid-emote: push the walk-off (and the fade after it) back. The fade has to finish before
                        // the game respawns you, 10 s after death.
                        walkAt = Mathf.Min(t + (_emoteEndsAt - Time.time), 7.0f - Plugin.RevealDelay.Value);
                        walkAt = Mathf.Max(walkAt, EarliestWalk);
                        FadeAt = walkAt + 1.3f; End = walkAt + 2.5f;
                    }
                    if (!walking && !ambush && t >= walkAt)
                    {
                        walking = true;
                        if (owner && Plugin.RevealWalkOff.Value && _setMoveDir != null && ai != null)
                        {
                            ai.enabled = false;
                            aiPaused = true;
                        }
                    }
                    if (aiPaused) Steer(c, walkDir, walkDir);
                    if (t >= FadeAt && !live) { _fadeTarget = 1f; _fadeSpeed = 1f / 1.2f; }

                    yield return null;
                }

                // Black until you're back on your feet (or a few seconds, at most).
                if (!live)
                {
                    float waitUntil = Time.time + 5f;
                    while (Time.time < waitUntil && (Player.m_localPlayer == null || Player.m_localPlayer.IsDead())) yield return null;
                }
            }
            finally
            {
                // Its own animations go back on behind the black (or, on a test, once it has wandered off).
                if (_emoteRestore != null)
                {
                    // In a live scene it's still in view: let the body put them back when it can't be seen,
                    // or when it gets hit (the flinch hides the snap).
                    var nb = c != null ? c.GetComponent<NemesisBody>() : null;
                    if (live && nb != null) nb.DeferRestore(_emoteRestore);
                    else if (live) StartCoroutine(Later(_emoteRestore, 6f));
                    else _emoteRestore();
                    _emoteRestore = null;
                }
                if (aiPaused && ai != null)
                {
                    if (c != null) Steer(c, Vector3.zero, Vector3.zero);
                    ai.enabled = true;
                }
                if (info != null) _held.Remove(info.NemesisId);
                _cameraHeld = false;
                _fadeTarget = 0f;
                _fadeSpeed = 1f / 0.8f;
                Active = false;
            }
        }

        /// <summary>The change: new look on, effects, a shriek and (if it has one) a taunt animation.</summary>
        private void Transform(Character c, RevealInfo info)
        {
            _held.Remove(info.NemesisId);
            Bodies.Instance?.TendNow(c); // put the new look on right now
            Vector3 at = c.transform.position + Vector3.up * 0.5f;
            if (IsPromotion(info) || info.Kind == "returned") Effects.Play(at, Plugin.RevealEffects.Value);

            try
            {
                var ai = c.GetComponent<BaseAI>();
                if (ai != null && ai.m_alertedEffects != null) Effects.CreateEffectList(ai.m_alertedEffects, c.transform);
            }
            catch (Exception e) { Plugin.DebugLog($"Reveal: no shriek ({e.Message})."); }

            // A player emote if this creature's skeleton can take one (like the Fuling Heckler does), else its own hop.
            bool animated = false;
            if (Plugin.RevealPlayerEmotes.Value && BorrowedEmote.TryPlay(c, EmotesFor(info), out float length, out Action restore))
            {
                animated = true;
                _emoteRestore = restore;
                _emoteEndsAt = Time.time + length; // it walks off once the emote is done, not mid-laugh
            }
            if (!animated)
            {
                string trigger = PickAnimation(c, Net.Known?.Find(info.NemesisId));
                animated = trigger != null;
                if (animated) StartCoroutine(Animate(c, trigger, Plugin.RevealAnimationRepeats.Value));
            }
            if (Plugin.RevealSwell.Value) StartCoroutine(Swell(c, IsPromotion(info) ? 0.35f : (animated ? 0f : 0.15f)));
        }

        /// <summary>
        /// Puffs the creature up and lets it settle back: a "rise" that works on any creature,
        /// whatever animations it has. Purely visual, only on this screen.
        /// </summary>
        private IEnumerator Swell(Character c, float amount)
        {
            if (amount <= 0f || c == null) yield break;
            Vector3 settled = c.transform.localScale;
            const float Up = 0.25f, Down = 0.45f;
            float t = 0f;
            while (t < Up + Down && c != null)
            {
                t += Time.deltaTime;
                float k = t < Up ? Smooth(t / Up) : 1f - Smooth((t - Up) / Down);
                c.transform.localScale = settled * (1f + amount * k);
                yield return null;
            }
            if (c != null) c.transform.localScale = settled;
        }

        private static void Speak(Character c, RevealInfo info)
        {
            var body = c.GetComponent<NemesisBody>();
            if (body != null) body.SayLine(info.Line);
            else if (Chat.instance != null)
                Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.4f, 30f, Plugin.BubbleSeconds.Value, info.Title, info.Line, false);

        }

        /// <summary>Plays an animation trigger a few times, spaced out (a hop, a hop).</summary>
        private IEnumerator Animate(Character c, string trigger, int times)
        {
            for (int i = 0; i < times && c != null; i++)
            {
                if (i > 0) yield return new WaitForSeconds(0.55f);
                var anim = c != null ? c.GetComponentInChildren<Animator>() : null;
                if (anim == null) yield break;
                try { anim.SetTrigger(trigger); } catch { yield break; }
            }
        }

        /// <summary>The first trigger from 'Taunt Animations' that this creature has, or null.</summary>
        private static string PickAnimation(Character c, Nemesis n = null)
        {
            try
            {
                var anim = c.GetComponentInChildren<Animator>();
                if (anim == null) return null;
                var triggers = anim.parameters.Where(p => p.type == AnimatorControllerParameterType.Trigger).Select(p => p.name).ToList();
                string species = GameInfo.Prefab(c);
                if (_loggedAnimators.Add(species))
                    Plugin.Log.LogInfo($"Animations on {species}: {string.Join(", ", anim.parameters.Select(p => $"{p.name} ({p.type})").ToArray())}");
                string list = Plugin.PersonaValue(n, "animations");
                foreach (var wanted in RuleSettings.ParseList(list.Length > 0 ? list : "jump"))
                {
                    string match = triggers.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
                    if (match == null) continue;
                    Plugin.DebugLog($"Reveal: {species} plays '{match}'.");
                    return match;
                }
            }
            catch (Exception e) { Plugin.DebugLog($"Reveal: no animation ({e.Message})."); }
            return null;
        }

        /// <summary>Moves and turns a creature while its AI is paused (owner only).</summary>
        private static void Steer(Character c, Vector3 move, Vector3 look)
        {
            try
            {
                if (_setMoveDir != null) _setMoveDir.Invoke(c, Effects.FillArgs(_setMoveDir, move));
                if (_setRun != null) _setRun.Invoke(c, Effects.FillArgs(_setRun, false));
                if (_setLookDir != null && look.sqrMagnitude > 0.001f) _setLookDir.Invoke(c, Effects.FillArgs(_setLookDir, look.normalized));
            }
            catch { /* cosmetic only */ }
        }

        private static IEnumerator Later(Action a, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            try { a(); } catch { /* the creature may be gone */ }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // ---- Fade ------------------------------------------------------------------

        private void Update()
        {
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, _fadeSpeed * Time.deltaTime);
        }

        private void OnGUI()
        {
            if (_fade <= 0.001f) return;
            if (_black == null) _black = Texture2D.whiteTexture;
            var old = GUI.color;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, _fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _black);
            GUI.color = old;
        }
    }

    /// <summary>Game effects, played purely for looks on this screen (copied with their scripts stripped).</summary>
    internal static class Effects
    {
        private const float Lifetime = 8f;
        private static GameObject _staging;
        private static bool _loggedCandidates;
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Play(Vector3 position, string list)
        {
            LogCandidatesOnce();
            foreach (var name in RuleSettings.ParseList(list)) PlayOne(name, position);
        }

        private static void PlayOne(string name, Vector3 position)
        {
            try
            {
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
                if (prefab == null)
                {
                    if (_warned.Add(name)) Plugin.Log.LogWarning($"Reveal: no effect called '{name}'. See the 'Effects you could use' log line.");
                    return;
                }
                if (_staging == null)
                {
                    _staging = new GameObject("Grudges_EffectStaging");
                    _staging.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_staging);
                }
                var fx = UnityEngine.Object.Instantiate(prefab, _staging.transform, false);
                foreach (var mb in fx.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
                foreach (var col in fx.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
                foreach (var rb in fx.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
                fx.transform.SetParent(null, false);
                fx.transform.position = position;
                fx.transform.rotation = Quaternion.identity;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);
                foreach (var a in fx.GetComponentsInChildren<AudioSource>(true)) a.Play();
                UnityEngine.Object.Destroy(fx, Lifetime);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Reveal: effect '{name}' failed ({e.Message})."); }
        }

        /// <summary>Plays one of the game's own effect lists (e.g. a creature's alert shriek) at a creature.</summary>
        internal static void CreateEffectList(object effectList, Transform at)
        {
            var create = effectList.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length >= 2
                                     && m.GetParameters()[0].ParameterType == typeof(Vector3)
                                     && m.GetParameters()[1].ParameterType == typeof(Quaternion));
            if (create == null) return;
            var args = FillArgs(create, at.position, at.rotation);
            var ps = create.GetParameters();
            for (int i = 2; i < ps.Length; i++)
                if (ps[i].ParameterType == typeof(Transform)) args[i] = at; // follow the creature
            create.Invoke(effectList, args);
        }

        /// <summary>Arguments for a method: the given ones first, then each remaining parameter's default.</summary>
        internal static object[] FillArgs(MethodBase m, params object[] given)
        {
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < given.Length) args[i] = given[i];
                else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                else args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            }
            return args;
        }

        private static void LogCandidatesOnce()
        {
            if (_loggedCandidates || ZNetScene.instance == null) return;
            _loggedCandidates = true;
            try
            {
                var prefabs = Traverse.Create(ZNetScene.instance).Field("m_prefabs").GetValue<List<GameObject>>();
                if (prefabs == null) return;
                string[] keys = { "spawn", "smoke", "puff", "ghost", "lightning", "levelup", "fire", "blood", "summon" };
                var names = prefabs.Where(p => p != null).Select(p => p.name)
                    .Where(n => (n.StartsWith("vfx", StringComparison.OrdinalIgnoreCase) || n.StartsWith("fx", StringComparison.OrdinalIgnoreCase))
                                && keys.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Distinct().OrderBy(n => n).ToArray();
                Plugin.Log.LogInfo("Effects you could use in 'Transform Effects': " + (names.Length > 0 ? string.Join(", ", names) : "none found"));
            }
            catch { /* just a helper */ }
        }
    }
}
