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
        private static AudioClip[] _idle, _alerted;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;

        private static readonly System.Collections.Generic.List<CoglingVoice> Live = new System.Collections.Generic.List<CoglingVoice>();
        private AudioSource _source;
        private RobotFilter _filter;
        private float _next;

        private void Start()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { enabled = false; return; }
            if (GetComponent<ZNetView>()?.GetZDO() == null) { enabled = false; return; }   // (the build ghost and pictures stay quiet)
            if (_idle == null)
            {
                ZSFX sfx = ZNetScene.instance?.GetPrefab("sfx_dverger_vo_idle")?.GetComponent<ZSFX>();
                if (sfx == null || sfx.m_audioClips == null || sfx.m_audioClips.Length == 0) { Plugin.Log.LogWarning("No Dverger voice to borrow: the Cogling stays quiet"); enabled = false; return; }
                _idle = sfx.m_audioClips;
                _alerted = ZNetScene.instance.GetPrefab("sfx_dverger_vo_alerted")?.GetComponent<ZSFX>()?.m_audioClips;
                _mixer = sfx.GetComponent<AudioSource>()?.outputAudioMixerGroup;
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
            _next = Time.time + Random.Range(2f, Plugin.BabbleEvery.Value);
            Live.Add(this);
        }

        private void OnDestroy()
        {
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

        private bool _scared;
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
            }
            if (_scared || Time.time < _next) return;
            _next = Time.time + Plugin.BabbleEvery.Value * Random.Range(0.6f, 1.4f);
            Settings();
            if (!_source.isPlaying) _source.PlayOneShot(_idle[Random.Range(0, _idle.Length)]);
        }

        /// <summary>The voice settings, on every Cogling here (and he says something at once, to hear it).</summary>
        internal static void TryNow()
        {
            foreach (CoglingVoice v in Live) if (v != null) v._next = 0f;
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
