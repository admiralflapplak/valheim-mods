using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace GoodTidings
{
    /// <summary>
    /// The skald's lute (the game has no instrument: built from simple shapes in the game's own wood) and his song. The lute is held as
    /// the guitar-playing animation holds one, its body before his belly and its neck toward his fretting hand, or carried on his back
    /// (on the player's back-shield point). The fit is set by eye (`goodtidings lute ...`, `goodtidings luteback ...`).
    /// </summary>
    internal static class SkaldLook
    {
        // ---- its fit, set by eye in the settings (F1, "98 - Skald Lute"): held, from his hips in metres along his facing, to his right
        // and up, and turned round its neck; on his back, offset and turned on the back-shield point; the wood grain's size

        private static ConfigEntry<float> _forward, _side, _up, _tilt, _fretForward, _fretSide, _fretUp, _backX, _backY, _backZ, _turnX, _turnY, _turnZ, _grain;
        internal static float HoldForward { get => _forward?.Value ?? 0.123f; set { if (_forward != null) _forward.Value = value; } }
        internal static float HoldSide { get => _side?.Value ?? 0.08f; set { if (_side != null) _side.Value = value; } }
        internal static float HoldUp { get => _up?.Value ?? 0.3f; set { if (_up != null) _up.Value = value; } }
        internal static float HoldTilt { get => _tilt?.Value ?? 44.563f; set { if (_tilt != null) _tilt.Value = value; } }
        /// <summary>Where the neck points, from his fretting hand: forward, right, up (metres).</summary>
        internal static Vector3 FretOffset => new Vector3(_fretForward?.Value ?? 0f, _fretSide?.Value ?? 0.053f, _fretUp?.Value ?? 0.03f);
        internal static Vector3 BackOffset
        {
            get => new Vector3(_backX?.Value ?? 0f, _backY?.Value ?? 0f, _backZ?.Value ?? -0.05f);
            set { if (_backX == null) return; _backX.Value = value.x; _backY.Value = value.y; _backZ.Value = value.z; }
        }
        internal static Vector3 BackTurn
        {
            get => new Vector3(_turnX?.Value ?? 0f, _turnY?.Value ?? 0f, _turnZ?.Value ?? 35f);
            set { if (_turnX == null) return; _turnX.Value = value.x; _turnY.Value = value.y; _turnZ.Value = value.z; }
        }
        /// <summary>A lute's back placement changed: put back on again (the held one follows each frame by itself).</summary>
        internal static event System.Action BackChanged;

        internal static void Bind(ConfigFile config)
        {
            const string section = "98 - Skald Lute";
            int order = 100;
            ConfigEntry<float> F(string key, float value, float min, float max, string what, bool back = false)
            {
                var e = config.Bind(section, key, value, new ConfigDescription(what, new AcceptableValueRange<float>(min, max),
                    new ConfigurationManagerAttributes { Order = order-- }));
                if (back) e.SettingChanged += (_, __) => BackChanged?.Invoke();
                return e;
            }
            _forward = F("Held forward", 0.123f, -0.5f, 0.8f, "How far in front of his hips the lute's body is (metres).");
            _side = F("Held side", 0.08f, -0.5f, 0.5f, "How far to his right (metres; left below 0).");
            _up = F("Held up", 0.3f, -0.5f, 1f, "How far above his hips (metres).");
            _tilt = F("Held tilt", 44.563f, -180f, 180f, "Turns the lute round its own neck (degrees).");
            _fretForward = F("Neck point forward", 0f, -0.5f, 0.5f, "Where the neck points, from his fretting (left) hand: forward (metres; back below 0).");
            _fretSide = F("Neck point side", 0.053f, -0.5f, 0.5f, "Where the neck points, from his fretting hand: to his right (metres; left below 0).");
            _fretUp = F("Neck point up", 0.03f, -0.5f, 0.5f, "Where the neck points, from his fretting hand: up (metres; down below 0).");
            _backX = F("Back X", 0f, -0.5f, 0.5f, "On his back: sideways on the back point (metres).", true);
            _backY = F("Back Y", 0f, -0.5f, 0.5f, "On his back: up or down (metres).", true);
            _backZ = F("Back Z", -0.05f, -0.5f, 0.5f, "On his back: out from or into his back (metres).", true);
            _turnX = F("Back turn X", 0f, -180f, 180f, "On his back: turned (degrees).", true);
            _turnY = F("Back turn Y", 0f, -180f, 180f, "On his back: turned (degrees).", true);
            _turnZ = F("Back turn Z", 35f, -180f, 180f, "On his back: turned (degrees).", true);
            _grain = F("Wood grain scale", 4f, 0.25f, 20f, "How big the wood's grain is on the lute (bigger: coarser).");
            _grain.SettingChanged += (_, __) => Grain();
        }

        private static readonly List<Material> Tints = new List<Material>();

        /// <summary>The wood grain's size on every lute material (the texture tiled less: bigger grain).</summary>
        private static void Grain()
        {
            float g = Mathf.Max(0.01f, _grain?.Value ?? 4f);
            foreach (Material m in Tints)
                if (m) m.mainTextureScale = Vector2.one / g;
        }

        /// <summary>A lute, its body at its origin, its neck up (+y), its strings toward +z.</summary>
        internal static GameObject Lute(int layer)
        {
            Material wood = Wood();
            Material Tinted(Color c)
            {
                var m = new Material(wood) { name = "goodtidings_lute_" + c };
                m.color = c;
                float g = Mathf.Max(0.01f, _grain?.Value ?? 4f);
                m.mainTextureScale = Vector2.one / g;
                Tints.RemoveAll(x => !x);
                Tints.Add(m);
                return m;
            }
            Material back = Tinted(new Color(0.55f, 0.36f, 0.22f)), top = Tinted(new Color(0.92f, 0.78f, 0.55f)),
                     dark = Tinted(new Color(0.18f, 0.12f, 0.08f)), gut = Tinted(new Color(0.95f, 0.92f, 0.82f));
            var lute = new GameObject("goodtidings_lute") { layer = layer };
            void Part(PrimitiveType type, Material m, Vector3 at, Vector3 size, Vector3 turn = default)
            {
                GameObject p = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.name = "goodtidings_lute_part";
                p.layer = layer;
                p.transform.SetParent(lute.transform, false);
                p.transform.localPosition = at;
                p.transform.localRotation = Quaternion.Euler(turn);
                p.transform.localScale = size;
                p.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            Part(PrimitiveType.Sphere, back, new Vector3(0f, 0f, -0.035f), new Vector3(0.30f, 0.38f, 0.15f));          // the rounded back
            Part(PrimitiveType.Cylinder, top, new Vector3(0f, 0f, 0.035f), new Vector3(0.30f, 0.005f, 0.38f), new Vector3(90f, 0f, 0f));   // the soundboard
            Part(PrimitiveType.Cylinder, dark, new Vector3(0f, 0.06f, 0.04f), new Vector3(0.07f, 0.002f, 0.07f), new Vector3(90f, 0f, 0f));  // the rosette
            Part(PrimitiveType.Cube, dark, new Vector3(0f, -0.1f, 0.045f), new Vector3(0.08f, 0.012f, 0.015f));          // the bridge
            Part(PrimitiveType.Cube, back, new Vector3(0f, 0.33f, 0.02f), new Vector3(0.045f, 0.32f, 0.025f));           // the neck
            Part(PrimitiveType.Cube, dark, new Vector3(0f, 0.53f, -0.01f), new Vector3(0.05f, 0.12f, 0.02f), new Vector3(-35f, 0f, 0f));   // the pegbox
            foreach (float x in new[] { -0.009f, -0.003f, 0.003f, 0.009f })
                Part(PrimitiveType.Cube, gut, new Vector3(x, 0.195f, 0.05f), new Vector3(0.002f, 0.59f, 0.002f));        // the strings
            return lute;
        }

        private static Material _wood;

        /// <summary>The game's wood, for the lute: a wooden item's (the club's: items take a colour; building pieces' shader showed white).</summary>
        private static Material Wood()
        {
            if (_wood) return _wood;
            foreach (string item in new[] { "Club", "ShieldWood", "Bow" })
            {
                Renderer r = ObjectDB.instance?.GetItemPrefab(item)?.transform.Find("attach")?.GetComponentInChildren<MeshRenderer>(true);
                if (r && r.sharedMaterial) return _wood = r.sharedMaterial;
            }
            return _wood = new Material(Shader.Find("Standard"));
        }

        /// <summary>The lute on his back (the player's back-shield point).</summary>
        internal static void OnBack(GameObject lute, Transform body)
        {
            Transform point = Utils.FindChild(body, "BackShield_attach") ?? Utils.FindChild(body, "Spine2");
            lute.transform.SetParent(point, false);
            lute.transform.localPosition = BackOffset;
            lute.transform.localRotation = Quaternion.Euler(BackTurn);
            // (the point is inside the body's rig, which has a scale of its own: the lute keeps its own size)
            Vector3 s = point.lossyScale;
            lute.transform.localScale = new Vector3(1f / Mathf.Max(s.x, 1e-4f), 1f / Mathf.Max(s.y, 1e-4f), 1f / Mathf.Max(s.z, 1e-4f));
        }

        /// <summary>
        /// Holds the lute as he plays, each frame after the animation has posed him (MixamoPlayer.Posed): fixed to his lower spine, as a
        /// strapped instrument sways with its player, its body where the settings put it from his hips. Its neck points where his fretting
        /// hand is on average (moved by the "Neck point" settings): learnt over the animation's first loop (meanwhile toward the hand itself),
        /// then fixed, so the hand slides along the neck rather than the neck following it.
        /// </summary>
        internal sealed class Holder
        {
            private readonly GameObject _lute;
            private readonly Transform _root, _spine, _hips, _left;
            private readonly float _learn;
            private Vector3 _handSum, _anchor, _face, _neck;
            private int _handCount;
            private float _started = -1f;
            private (float, float, float, float) _fit;

            internal Holder(GameObject lute, Transform root, Transform body, float loop)
            {
                _lute = lute;
                _root = root;
                _hips = Utils.FindChild(body, "Hips");
                _spine = Utils.FindChild(body, "Spine") ?? _hips;
                _left = Utils.FindChild(body, "LeftHand");
                _learn = Mathf.Max(1f, loop);
                lute.transform.SetParent(root, true);
            }

            internal void Place()
            {
                if (_lute == null || _spine == null || _hips == null || _left == null) return;
                if (_lute.transform.parent != _root)
                {
                    // off his back: back under him, at its own size, gliding from where it was into his hands
                    _glideFrom = (_lute.transform.position, _lute.transform.rotation);
                    _glide = 0f;
                    _lute.transform.SetParent(_root, false);
                    _lute.transform.localScale = Vector3.one;
                }
                if (_started < 0f) _started = Time.time;
                // where it sits, in the spine's own space (set again when the settings change)
                var fit = (HoldForward, HoldSide, HoldUp, HoldTilt);
                if (fit != _fit || _anchor == Vector3.zero)
                {
                    _fit = fit;
                    Vector3 at = _hips.position + _root.forward * HoldForward + _root.right * HoldSide + _root.up * HoldUp;   // (measured from his hips)
                    _anchor = _spine.InverseTransformPoint(at);
                    _face = _spine.InverseTransformDirection(_root.forward);
                }
                Vector3 hand = _spine.InverseTransformPoint(_left.position);
                if (Time.time - _started < _learn)
                {
                    _handSum += hand;
                    _handCount++;
                    _neck = hand - _anchor;
                }
                else if (_handCount > 0) _neck = _handSum / _handCount - _anchor;
                // the point it aims at, moved from the hand by the settings (in his facing: forward, right, up)
                Vector3 fret = FretOffset;
                Vector3 aim = _neck + _spine.InverseTransformVector(_root.forward * fret.x + _root.right * fret.y + _root.up * fret.z);
                Vector3 neck = _spine.TransformDirection(aim).normalized;
                Vector3 face = Vector3.ProjectOnPlane(_spine.TransformDirection(_face), neck).normalized;
                Vector3 pos = _spine.TransformPoint(_anchor);
                Quaternion rot = Quaternion.AngleAxis(HoldTilt, neck) * Quaternion.LookRotation(face, neck);
                if (_glide >= 0f)
                {
                    _glide += Time.deltaTime;
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_glide / GlideTime));
                    pos = Vector3.Lerp(_glideFrom.pos, pos, w);
                    rot = Quaternion.Slerp(_glideFrom.rot, rot, w);
                    if (_glide >= GlideTime) _glide = -1f;
                }
                _lute.transform.SetPositionAndRotation(pos, rot);
            }

            private const float GlideTime = 0.8f;
            private float _glide = -1f;
            private (Vector3 pos, Quaternion rot) _glideFrom;
        }

        /// <summary>
        /// While he plays: soft golden motes drifting up round him, as if from the strings (the game's glow sprite, a particle system of
        /// our own), and a little warm light.
        /// </summary>
        internal static ParticleSystem Motes(Transform skald)
        {
            var go = new GameObject("goodtidings_skald_motes");
            go.transform.SetParent(skald, false);
            go.transform.localPosition = new Vector3(0f, 1.1f, 0.15f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.4f), new Color(1f, 0.95f, 0.7f));
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var emission = ps.emission;
            emission.rateOverTime = 8f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.5f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ZNetScene.instance?.GetPrefab("vfx_prespawn")?.GetComponentInChildren<ParticleSystemRenderer>(true)?.sharedMaterial;
            var light = new GameObject("goodtidings_skald_light").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.5f);
            light.intensity = 0.8f;
            light.range = 3.5f;
            return ps;
        }

        // ---- the song (assets/skald_song.ogg, in the DLL): written out once and loaded as the game's audio would be

        /// <summary>The song's volume for this player (Song Volume).</summary>
        internal static float Volume => Mathf.Clamp01((Plugin.SkaldVolume?.Value ?? 80f) / 100f);

        private static AudioClip _song;
        private static bool _loading;

        /// <summary>Plays the song from <paramref name="on"/> (a 3D sound, the game's sound-effects volume), looping.</summary>
        internal static AudioSource Play(GameObject on)
        {
            AudioSource source = on.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 35f;
            source.volume = Volume;
            source.loop = true;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = MixerGroup();
            if (_song) { source.clip = _song; source.Play(); }
            else on.AddComponent<SongLoader>().Source = source;
            return source;
        }

        private static AudioMixerGroup MixerGroup() =>
            ZNetScene.instance?.GetPrefab("sfx_skeleton_idle")?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;

        private sealed class SongLoader : MonoBehaviour
        {
            internal AudioSource Source;

            private IEnumerator Start()
            {
                while (_loading) yield return null;
                if (_song == null)
                {
                    _loading = true;
                    string path = Path.Combine(BepInEx.Paths.CachePath, "goodtidings_skald_song.ogg");
                    try
                    {
                        string resource = typeof(SkaldLook).Assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("skald_song.ogg"));
                        using (Stream s = typeof(SkaldLook).Assembly.GetManifestResourceStream(resource))
                        using (FileStream f = File.Create(path)) s.CopyTo(f);
                    }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"The skald's song couldn't be written out: {e.Message}"); _loading = false; yield break; }
                    using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.OGGVORBIS))
                    {
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success) _song = DownloadHandlerAudioClip.GetContent(req);
                        else Plugin.Log.LogWarning($"The skald's song couldn't be loaded: {req.error}");
                    }
                    if (_song) _song.name = "goodtidings_skald_song";
                    _loading = false;
                }
                if (_song && Source) { Source.clip = _song; Source.Play(); }
                Destroy(this);
            }
        }
    }
}
