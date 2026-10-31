using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // ---- its fit, set by eye with the user (F1 sliders, 2026-10-30/31): held, from his hips in metres along his facing, to his right
        // and up, turned round its neck, its neck aimed past his fretting hand; on his back, offset and turned on the back-shield point

        internal const float HoldForward = 0.123f, HoldSide = 0.08f, HoldUp = 0.3f, HoldTilt = 44.563f, Size = 0.9f, Grain = 4f;
        /// <summary>Where the neck points, from his fretting hand: forward, right, up (metres).</summary>
        internal static readonly Vector3 FretOffset = new Vector3(0f, 0.053f, 0.03f);
        internal static readonly Vector3 BackOffset = new Vector3(-0.013f, -0.028f, -0.131f), BackTurn = new Vector3(83.99f, 17.243f, 26.075f);

        // ---- the lyre: the user's model (Meshy), turned into assets/skald_lyre.bin and .png by tools/convert_lyre.py (which adds its
        // fingerboard); its body at its origin, its neck up (+y), its strings toward +z, as the lute's

        private static Mesh _lyreMesh;
        private static Material _lyreMaterial;

        /// <summary>The lyre (the code-built lute if the model can't be read).</summary>
        internal static GameObject Lyre(int layer)
        {
            if (_lyreMesh == null && !LoadLyre()) return Lute(layer);
            var lyre = new GameObject("goodtidings_lyre") { layer = layer };
            var model = new GameObject("goodtidings_lyre_model") { layer = layer };
            model.transform.SetParent(lyre.transform, false);
            model.transform.localScale = Vector3.one * Size;
            model.AddComponent<MeshFilter>().sharedMesh = _lyreMesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = _lyreMaterial;
            return lyre;
        }

        private static bool LoadLyre()
        {
            try
            {
                var asm = typeof(SkaldLook).Assembly;
                using (Stream s = asm.GetManifestResourceStream("GoodTidings.assets.skald_lyre.bin"))
                using (var r = new BinaryReader(s))
                {
                    if (new string(r.ReadChars(4)) != "LYR1") throw new InvalidDataException("not a lyre");
                    int n = r.ReadInt32();
                    var pos = new Vector3[n]; var nor = new Vector3[n]; var uv = new Vector2[n];
                    for (int i = 0; i < n; i++)
                    {
                        pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        nor[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    }
                    var tris = new int[r.ReadInt32()];
                    for (int i = 0; i < tris.Length; i++) tris[i] = r.ReadInt32();
                    _lyreMesh = new Mesh { name = "goodtidings_lyre" };
                    if (n > 65000) _lyreMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    _lyreMesh.vertices = pos; _lyreMesh.normals = nor; _lyreMesh.uv = uv; _lyreMesh.triangles = tris;
                    _lyreMesh.RecalculateBounds();
                    _lyreMesh.RecalculateTangents();
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "goodtidings_lyre" };
                using (Stream s = asm.GetManifestResourceStream("GoodTidings.assets.skald_lyre.png"))
                using (var m = new MemoryStream())
                {
                    s.CopyTo(m);
                    // (ImageConversion.LoadImage, called by name: its module is built for a newer .NET than this project compiles against)
                    System.Type conversion = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                    conversion?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) })?.Invoke(null, new object[] { tex, m.ToArray() });
                }
                _lyreMaterial = new Material(Wood()) { name = "goodtidings_lyre", mainTexture = tex, color = Color.white, mainTextureScale = Vector2.one };
                if (_lyreMaterial.HasProperty("_BumpMap")) _lyreMaterial.SetTexture("_BumpMap", null);
                if (_lyreMaterial.HasProperty("_MetallicGlossMap")) _lyreMaterial.SetTexture("_MetallicGlossMap", null);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"The skald's lyre couldn't be read ({e.Message}): he plays the old lute");
                _lyreMesh = null;
                return false;
            }
        }

        /// <summary>A lute, its body at its origin, its neck up (+y), its strings toward +z.</summary>
        internal static GameObject Lute(int layer)
        {
            Material wood = Wood();
            Material Tinted(Color c)
            {
                var m = new Material(wood) { name = "goodtidings_lute_" + c };
                m.color = c;
                m.mainTextureScale = Vector2.one / Grain;
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

        /// <summary>The lute on his back (the player's back-shield point); with <paramref name="glide"/>, moved there over a moment.</summary>
        internal static void OnBack(GameObject lute, Transform body, bool glide = false)
        {
            Vector3 fromPos = lute.transform.position;
            Quaternion fromRot = lute.transform.rotation;
            Transform point = Utils.FindChild(body, "BackShield_attach") ?? Utils.FindChild(body, "Spine2");
            lute.transform.SetParent(point, false);
            // (the point is inside the body's rig, which has a scale of its own, 95: the lute keeps its own size, and its offset is in
            // metres; in the point's units it put the lyre 3 m under his feet)
            Vector3 s = point.lossyScale;
            Vector3 inverse = new Vector3(1f / Mathf.Max(s.x, 1e-4f), 1f / Mathf.Max(s.y, 1e-4f), 1f / Mathf.Max(s.z, 1e-4f));
            lute.transform.localPosition = Vector3.Scale(BackOffset, inverse);
            lute.transform.localRotation = Quaternion.Euler(BackTurn);
            lute.transform.localScale = inverse;
            if (glide) Glide.Start(lute.transform, fromPos, fromRot);
        }

        /// <summary>Moves a thing from where it was (in the world) to its place under its parent, over a moment.</summary>
        internal sealed class Glide : MonoBehaviour
        {
            private Vector3 _fromPos, _toPos;
            private Quaternion _fromRot, _toRot;
            private float _time;

            internal static void Start(Transform t, Vector3 worldPos, Quaternion worldRot)
            {
                foreach (Glide old in t.GetComponents<Glide>()) Destroy(old);
                var g = t.gameObject.AddComponent<Glide>();
                g._toPos = t.localPosition;
                g._toRot = t.localRotation;
                Transform parent = t.parent;
                g._fromPos = parent ? parent.InverseTransformPoint(worldPos) : worldPos;
                g._fromRot = parent ? Quaternion.Inverse(parent.rotation) * worldRot : worldRot;
                t.localPosition = g._fromPos;
                t.localRotation = g._fromRot;
            }

            private void LateUpdate()
            {
                _time += Time.deltaTime;
                float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_time / 0.8f));
                transform.localPosition = Vector3.Lerp(_fromPos, _toPos, w);
                transform.localRotation = Quaternion.Slerp(_fromRot, _toRot, w);
                if (w >= 1f) Destroy(this);
            }
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
                    foreach (Glide g in _lute.GetComponents<Glide>()) Object.Destroy(g);
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

        // ---- his songs (assets/skald_song*.ogg, in the DLL): written out once and loaded as the game's audio would be. He plays them one
        // after another in an order shuffled for each visit; where he is in them comes from the server's clock (the time he began), so
        // every player hears the same song at the same moment.

        /// <summary>The song's volume for this player (Song Volume).</summary>
        internal static float Volume => Mathf.Clamp01((Plugin.SkaldVolume?.Value ?? 80f) / 100f);

        private static readonly List<AudioClip> Songs = new List<AudioClip>();

        /// <summary>
        /// His songs' lengths in seconds (assets/skald_song1-3.ogg), known on every game, a dedicated server's too (it loads no audio):
        /// he plays each once, shuffled, then bows.
        /// </summary>
        internal static readonly float[] SongLengths = { 120f, 110f, 120f };
        internal static float AllSongs => SongLengths.Sum();
        private static bool _loaded, _loading;

        /// <summary>
        /// Plays his songs from <paramref name="on"/> (a 3D sound, the game's sound-effects volume): shuffled by <paramref name="seed"/>,
        /// the first begun at <paramref name="began"/> (server time, ticks), each once.
        /// </summary>
        internal static AudioSource Play(GameObject on, int seed, long began)
        {
            AudioSource source = on.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 35f;
            source.volume = Volume;
            source.loop = false;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = MixerGroup();
            var player = on.AddComponent<SongPlayer>();
            player.Source = source;
            player.Seed = seed;
            player.Began = began;
            return source;
        }

        private static AudioMixerGroup MixerGroup() =>
            ZNetScene.instance?.GetPrefab("sfx_skeleton_idle")?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;

        /// <summary>Keeps the source on the song (and the place in it) the visit's clock says, changing at each song's end.</summary>
        private sealed class SongPlayer : MonoBehaviour
        {
            internal AudioSource Source;
            internal int Seed;
            internal long Began;
            private int[] _order;
            private int _playing = -1;

            private IEnumerator Start()
            {
                while (_loading) yield return null;
                if (!_loaded) yield return Load();
                if (Songs.Count == 0) yield break;
                var rnd = new System.Random(Seed);
                _order = Enumerable.Range(0, Songs.Count).OrderBy(_ => rnd.Next()).ToArray();
            }

            private void Update()
            {
                if (_order == null || Source == null || ZNet.instance == null) return;
                float total = _order.Sum(i => Songs[i].length);
                if (total <= 0f) return;
                double elapsed = (ZNet.instance.GetTime().Ticks - Began) / (double)System.TimeSpan.TicksPerSecond;
                if (elapsed >= total || elapsed < 0) { if (Source.isPlaying) Source.Stop(); return; }   // (once through: then he bows)
                float t = (float)elapsed;
                int n = 0;
                while (n < _order.Length - 1 && t >= Songs[_order[n]].length) { t -= Songs[_order[n]].length; n++; }
                if (n == _playing && Source.isPlaying) return;
                _playing = n;
                Source.clip = Songs[_order[n]];
                Source.time = Mathf.Clamp(t, 0f, Source.clip.length - 0.05f);
                Source.Play();
            }

            private static IEnumerator Load()
            {
                _loading = true;
                var asm = typeof(SkaldLook).Assembly;
                foreach (string resource in asm.GetManifestResourceNames().Where(r => r.Contains("skald_song") && r.EndsWith(".ogg")).OrderBy(r => r))
                {
                    string name = resource.Substring(0, resource.Length - 4);
                    name = name.Substring(name.LastIndexOf('.') + 1);
                    string path = Path.Combine(BepInEx.Paths.CachePath, "goodtidings_" + name + ".ogg");
                    try
                    {
                        using (Stream s = asm.GetManifestResourceStream(resource))
                        using (FileStream f = File.Create(path)) s.CopyTo(f);
                    }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"A skald's song couldn't be written out: {e.Message}"); continue; }
                    using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace(Path.DirectorySeparatorChar, '/'), AudioType.OGGVORBIS))
                    {
                        yield return req.SendWebRequest();
                        if (req.result != UnityWebRequest.Result.Success) { Plugin.Log.LogWarning($"A skald's song couldn't be loaded: {req.error}"); continue; }
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                        clip.name = "goodtidings_" + name;
                        Songs.Add(clip);
                    }
                }
                Plugin.Log.LogInfo($"The skald knows {Songs.Count} songs");
                _loaded = true;
                _loading = false;
            }
        }

        // ---- the game's music made quieter near him (its own "music quieter here" answer, which it fades to: no sudden cut)

        /// <summary>How loud the game's music may be at this player's place: Music Near Skald within 10 m of him, rising to full at 25 m.</summary>
        internal static float MusicDuck()
        {
            Player p = Player.m_localPlayer;
            if (p == null || SkaldVisitor.Count == 0) return 1f;
            float d = SkaldVisitor.NearestPlaying(p.transform.position);
            if (d >= 25f) return 1f;
            float low = Mathf.Clamp01((Plugin.SkaldMusicDuck?.Value ?? 20f) / 100f);
            return Mathf.Lerp(low, 1f, Mathf.InverseLerp(10f, 25f, d));
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(MusicVolume), nameof(MusicVolume.UpdateProximityVolumes))]
    internal static class MusicVolume_UpdateProximityVolumes
    {
        private static void Postfix(ref float __result) => __result *= SkaldLook.MusicDuck();
    }
}
