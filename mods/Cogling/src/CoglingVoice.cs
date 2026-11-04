using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The Cogling's voice: the Dverger's voice lines (the game's sfx_dverger_vo_* sounds, their clips) from his own sound source on the
    /// game's effects mixer (the player's volume slider), pitched up and made robotic (RobotFilter: ring modulation and a little crunch).
    /// For now he babbles the idle lines every so often; later each mood gets its lines (hurt, alerted, ...). Each player's game babbles
    /// on its own: nothing is sent between players.
    /// </summary>
    internal sealed class CoglingVoice : MonoBehaviour
    {
        /// <summary>What he says, and when (his brain decides: CoglingAI.Say; every player's game hears it, through an RPC).</summary>
        internal enum Line { Pickup, Stored, Tidied, Grumble, Repaired, Nervous, Relieved, Hurt, PowerDown, BootUp, Welcome, Oops, Excited, Laugh, Point, Nod, Shake, WaveBack, Startled }

        /// <summary>
        /// How each line sounds: the Dverger's voice set it comes from, its pitch (times his own) from start to end (a slide: a sigh
        /// down, a boot up), cut short after some seconds (0: whole), how likely he says it (a chatty one more: see CoglingAI.Say), and
        /// the gestures it may come with (the player's emotes), with how likely one is.
        /// </summary>
        internal struct Spec
        {
            internal string Set; internal float From, To, Cut, Chance, GestureChance; internal string[] Gestures;
        }

        internal static readonly Dictionary<Line, Spec> Specs = new Dictionary<Line, Spec>
        {
            { Line.Pickup,    new Spec { Set = "idle",    From = 1.3f,  To = 1.35f, Cut = 0.45f, Chance = 0.35f } },
            { Line.Stored,    new Spec { Set = "idle",    From = 1.15f, To = 1.2f,  Cut = 0.8f,  Chance = 0.3f } },   // (no gesture: the user found one at every chest too much)
            { Line.Tidied,    new Spec { Set = "idle",    From = 1.2f,  To = 1.3f,  Cut = 1.0f,  Chance = 0.6f } },
            { Line.Grumble,   new Spec { Set = "idle",    From = 0.75f, To = 0.7f,  Cut = 1.0f,  Chance = 0.8f,  Gestures = new[] { "Shrug", "Shrug", "social_angry02" }, GestureChance = 0.6f } },   // (now and then cross: a stamp)
            { Line.Repaired,  new Spec { Set = "idle",    From = 1.2f,  To = 1.3f,  Cut = 1.0f,  Chance = 0.7f } },
            { Line.Nervous,   new Spec { Set = "alerted", From = 0.95f, To = 0.9f,  Cut = 0.5f,  Chance = 1f } },
            { Line.Relieved,  new Spec { Set = "idle",    From = 1.1f,  To = 0.85f, Cut = 1.2f,  Chance = 0.85f } },
            { Line.Hurt,      new Spec { Set = "hurt",    From = 1.3f,  To = 1.3f,  Cut = 0f,    Chance = 1f } },   // (no gesture: hit, he is frightened first, and the user had it taken out)
            { Line.PowerDown, new Spec { Set = "idle",    From = 1.0f,  To = 0.5f,  Cut = 1.4f,  Chance = 0.8f } },
            { Line.BootUp,    new Spec { Set = "idle",    From = 0.7f,  To = 1.3f,  Cut = 1.0f,  Chance = 0.8f } },
            { Line.Welcome,   new Spec { Set = "idle",    From = 1.3f,  To = 1.4f,  Cut = 1.1f,  Chance = 1f } },
            { Line.Oops,      new Spec { Set = "idle",    From = 1.45f, To = 1.3f,  Cut = 0.4f,  Chance = 0.9f } },
            { Line.Excited,   new Spec { Set = "attack",  From = 1.4f,  To = 1.5f,  Cut = 0.7f,  Chance = 1f } },
            { Line.Laugh,     new Spec { Set = "idle",    From = 1.5f,  To = 1.6f,  Cut = 0.5f,  Chance = 1f,    Gestures = new[] { "social_laugh01" }, GestureChance = 1f } },
            { Line.Point,     new Spec { Set = null,      Chance = 1f,  Gestures = new[] { "Point" }, GestureChance = 1f } },   // (no longer said: chats use the Social pack)
            { Line.Nod,       new Spec { Set = null,      Chance = 1f,  Gestures = new[] { "social_headnod01" }, GestureChance = 1f } },
            { Line.WaveBack,  new Spec { Set = "idle",    From = 1.35f, To = 1.45f, Cut = 0.6f,  Chance = 1f,    Gestures = new[] { "rig_waving" }, GestureChance = 1f } },   // (a player waved: Rig_Medium's wave, the game's looked off on him)
            { Line.Startled,  new Spec { Set = "alerted", From = 1.5f,  To = 1.4f,  Cut = 0.4f,  Chance = 1f,    Gestures = new[] { "rig_hit_a" }, GestureChance = 1f } },   // (crept up on: a flinch; the Social pack's fear looked wonky on him)
            { Line.Shake,     new Spec { Set = null,      Chance = 1f,  Gestures = new[] { "social_headshake01", "social_headshake02" }, GestureChance = 1f } },
        };

        /// <summary>The gestures by number (the RPC sends the number).</summary>
        internal static readonly string[] Gestures = Specs.Values.Where(s => s.Gestures != null).SelectMany(s => s.Gestures).Distinct().ToArray();

        internal const string SayRpc = "cogling_say", WavedRpc = "cogling_waved";
        private static readonly Dictionary<string, AudioClip[]> Sets = new Dictionary<string, AudioClip[]>();
        private static AudioClip[] _idle, _alerted;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;

        private static readonly System.Collections.Generic.List<CoglingVoice> Live = new System.Collections.Generic.List<CoglingVoice>();
        private AudioSource _source;
        private RobotFilter _filter;
        private float _next;

        private void Start()
        {
            ZNetView view = GetComponent<ZNetView>();
            if (view?.GetZDO() == null) { enabled = false; return; }   // (the build ghost and pictures stay quiet)
            // (registered on every game, a server's too, so a line said anywhere finds its way; a hot reload's old copy let it go)
            view.Unregister(SayRpc);
            view.Register<int, int>(SayRpc, RPC_Say);
            view.Unregister(WavedRpc);
            view.Register<Vector3>(WavedRpc, (sender, at) => GetComponent<CoglingAI>()?.WavedAt(at));
            _view = view;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { enabled = false; return; }
            if (_idle == null)
            {
                ZSFX sfx = ZNetScene.instance?.GetPrefab("sfx_dverger_vo_idle")?.GetComponent<ZSFX>();
                if (sfx == null || sfx.m_audioClips == null || sfx.m_audioClips.Length == 0) { Plugin.Log.LogWarning("No Dverger voice to borrow: the Cogling stays quiet"); enabled = false; return; }
                _idle = sfx.m_audioClips;
                _alerted = ZNetScene.instance.GetPrefab("sfx_dverger_vo_alerted")?.GetComponent<ZSFX>()?.m_audioClips;
                _mixer = sfx.GetComponent<AudioSource>()?.outputAudioMixerGroup;
                Sets["idle"] = _idle;
                Sets["alerted"] = _alerted;
                foreach (string set in new[] { "attack", "hurt", "death" })
                    Sets[set] = ZNetScene.instance.GetPrefab("sfx_dverger_vo_" + set)?.GetComponent<ZSFX>()?.m_audioClips;
            }
            var mouth = new GameObject("cogling_voice");
            mouth.transform.SetParent(transform, false);
            mouth.transform.localPosition = Vector3.up * Plugin.Height.Value * 0.8f;
            _source = mouth.AddComponent<AudioSource>();
            _source.outputAudioMixerGroup = _mixer;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = 25f;
            _source.playOnAwake = false;
            _filter = mouth.AddComponent<RobotFilter>();   // (after the source: Unity runs a source's filters in the order they're added)
            Settings();
            Live.Add(this);
        }

        private ZNetView _view;
        private float _slideFrom = 1f, _slideTo = 1f, _slideStart, _slideLength, _stopAt = -1f;

        private void OnDestroy()
        {
            if (_view != null && _view.IsValid()) { _view.Unregister(SayRpc); _view.Unregister(WavedRpc); }
            Live.Remove(this);
            if (_source != null) Destroy(_source.gameObject);   // (a hot reload gives him the new copy's voice: one mouth, not two)
        }

        private void Settings()
        {
            if (_source == null) return;
            _source.pitch = Plugin.VoicePitch.Value;
            _source.volume = Plugin.VoiceVolume.Value;
            _filter.Robot = Plugin.VoiceRobot.Value;
            _filter.Frequency = Plugin.VoiceRing.Value;
            _filter.Crunch = Plugin.VoiceCrunch.Value;
        }

        private bool _scared, _talking, _listening;

        /// <summary>Greeted: a quick chirp, higher than his babble.</summary>
        internal void Greet()
        {
            if (_source == null || _idle == null || _idle.Length == 0) return;
            Settings();
            _source.Stop();
            _source.pitch = Plugin.VoicePitch.Value * 1.15f;
            _source.PlayOneShot(_idle[Random.Range(0, _idle.Length)]);
        }

        // ---- a line (CoglingAI.Say sends it to every game)

        private void RPC_Say(long sender, int line, int gesture)
        {
            if (gesture >= 0 && gesture < Gestures.Length) GetComponentInChildren<CoglingBody>()?.Gesture(Gestures[gesture]);
            if (_source != null && System.Enum.IsDefined(typeof(Line), line)) Play((Line)line);
        }

        /// <summary>Says a line here (this game only).</summary>
        internal void Play(Line line)
        {
            if (_source == null || _scared || !Specs.TryGetValue(line, out Spec spec) || spec.Set == null) return;
            if (!Sets.TryGetValue(spec.Set, out AudioClip[] clips) || clips == null || clips.Length == 0) return;
            Settings();
            _source.Stop();
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            _slideFrom = spec.From;
            _slideTo = spec.To;
            _slideStart = Time.time;
            _slideLength = spec.Cut > 0f ? spec.Cut : clip.length / Mathf.Max(0.1f, Plugin.VoicePitch.Value * spec.From);
            _source.pitch = Plugin.VoicePitch.Value * spec.From;
            _source.PlayOneShot(clip);
            _stopAt = spec.Cut > 0f ? Time.time + spec.Cut : -1f;
        }

        /// <summary>His death line, where he fell apart (he is gone by then: a voice of its own, for the moment it lasts).</summary>
        internal static void Death(Vector3 at)
        {
            if (!Sets.TryGetValue("death", out AudioClip[] clips) || clips == null || clips.Length == 0) return;
            var mouth = new GameObject("cogling_last_words");
            mouth.transform.position = at + Vector3.up * Plugin.Height.Value * 0.8f;
            var source = mouth.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = _mixer;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 25f;
            source.volume = Plugin.VoiceVolume.Value;
            var filter = mouth.AddComponent<RobotFilter>();
            filter.Robot = Plugin.VoiceRobot.Value;
            filter.Frequency = Plugin.VoiceRing.Value;
            filter.Crunch = Plugin.VoiceCrunch.Value;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            mouth.AddComponent<PowerOff>().Begin(source, Plugin.VoicePitch.Value * 1.1f, Plugin.VoicePitch.Value * 0.45f, clip.length * 0.8f);
            source.PlayOneShot(clip);
            Destroy(mouth, clip.length + 0.5f);
        }

        /// <summary>A pitch sliding down while the source plays (his last words wind down like a power-off).</summary>
        private sealed class PowerOff : MonoBehaviour
        {
            private AudioSource _source;
            private float _from, _to, _start, _length;
            internal void Begin(AudioSource s, float from, float to, float length) { _source = s; _from = from; _to = to; _start = Time.time; _length = Mathf.Max(0.1f, length); s.pitch = from; }
            private void Update() { if (_source != null) _source.pitch = Mathf.Lerp(_from, _to, (Time.time - _start) / _length); }
        }
        private float _nextMoodRead;

        private void Update()
        {
            if (_source == null) return;
            // frightened (his saved mood): his cry, once as it starts; no babbling while it lasts
            if (Time.time > _nextMoodRead)
            {
                _nextMoodRead = Time.time + 0.25f;
                bool scared = GetComponent<ZNetView>()?.GetZDO()?.GetInt(Carried.MoodKey) == Carried.MoodScared;
                if (scared && !_scared && _alerted != null && _alerted.Length > 0) { Settings(); _source.Stop(); _source.PlayOneShot(_alerted[Random.Range(0, _alerted.Length)]); }
                _scared = scared;
                int slack = GetComponent<ZNetView>()?.GetZDO()?.GetInt(CoglingSlack.SlackKey) ?? 0;
                bool talking = CoglingSlack.Talking(slack);
                if (talking && !_talking) _next = Time.time + Random.Range(0.1f, 0.4f);   // (his turn: he starts at once)
                _talking = talking;
                _listening = slack == CoglingSlack.PoseListen;
            }
            // in a chat: talking, one line after another with little gaps, each a little higher or lower; listening, quiet
            if (!_scared && _talking)
            {
                if (_source.isPlaying || Time.time < _next) return;
                Settings();
                _source.pitch = Plugin.VoicePitch.Value * Random.Range(0.92f, 1.1f);
                _source.PlayOneShot(_idle[Random.Range(0, _idle.Length)]);
                _next = Time.time + Random.Range(0.15f, 0.7f);
                return;
            }
            // a line: its pitch slides as it plays, and it's cut short where its spec says (no babble on a timer: he speaks when
            // something happens, CoglingAI.Say)
            if (_stopAt > 0f && Time.time >= _stopAt) { _source.Stop(); _stopAt = -1f; }
            if (_source.isPlaying && _slideLength > 0f && Time.time - _slideStart <= _slideLength)
                _source.pitch = Plugin.VoicePitch.Value * Mathf.Lerp(_slideFrom, _slideTo, (Time.time - _slideStart) / _slideLength);
        }

        /// <summary>The voice settings, on every Cogling here (and he says something at once, to hear it).</summary>
        internal static void TryNow()
        {
            foreach (CoglingVoice v in Live) if (v != null) v.Play(Line.Stored);
        }

        internal static void Forget()
        {
            _idle = null;
            Live.Clear();
        }
    }

    /// <summary>
    /// The robot in the voice, on the audio thread: ring modulation (the sound multiplied by a sine wave, the classic robot voice) mixed
    /// in by Robot, then Crunch (fewer loudness steps, and a soft clip). Settings are plain fields read on the audio thread.
    /// </summary>
    internal sealed class RobotFilter : MonoBehaviour
    {
        internal volatile float Robot = 0.6f, Frequency = 90f, Crunch = 0.2f;
        private double _phase;
        private int _rate;

        private void Awake() => _rate = AudioSettings.outputSampleRate;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float robot = Robot, crunch = Crunch;
            double step = 2.0 * Mathf.PI * Frequency / _rate;
            float steps = Mathf.Lerp(256f, 6f, crunch);   // loudness steps: fewer is crunchier
            for (int i = 0; i < data.Length; i += channels)
            {
                float ring = (float)System.Math.Sin(_phase);
                _phase += step;
                if (_phase > 2.0 * System.Math.PI) _phase -= 2.0 * System.Math.PI;
                for (int c = 0; c < channels; c++)
                {
                    float x = data[i + c];
                    x = Mathf.Lerp(x, x * ring, robot);
                    if (crunch > 0f)
                    {
                        x = Mathf.Round(x * steps) / steps;
                        x = (float)System.Math.Tanh(x * (1f + crunch * 3f)) / (1f + crunch);
                    }
                    data[i + c] = x;
                }
            }
        }
    }
}
