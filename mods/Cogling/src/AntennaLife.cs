using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The antenna, alive: now and then its dish turns to a new heading and tips a little (a quick, mechanical move: the turn first, the
    /// tip after), with the Ashwood door's opening creak sped up; and a small lamp in the middle of the dish in the colour of the eyes of
    /// the Cogling nearest it (dim when he's powered down). Both can be switched off and tuned (3 - Looks).
    ///
    /// The moves come from the world's clock and the antenna's own id, so every player's game moves it the same way at the same time
    /// without sending anything.
    /// </summary>
    internal sealed class AntennaLife : MonoBehaviour
    {
        private ZNetView _view;
        private Transform _turn, _dish;
        private Light _light;
        private Renderer _bulb;
        private MaterialPropertyBlock _block;
        private AudioSource _sound;
        private long _soundSlot = long.MinValue;
        private float _level = 1f, _nextLook;
        private Color _colour;
        private bool _near;

        private static AudioClip[] _creaks;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;

        private void Start()
        {
            _view = GetComponent<ZNetView>();
            if (_view == null || _view.GetZDO() == null) { enabled = false; return; }   // (the build ghost and pictures stay still)
            Find();
            _colour = Plugin.EyeColor.Value;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Creaks();
            var go = new GameObject("cogling_antenna_sound");
            go.transform.SetParent(_dish != null ? _dish : transform, false);
            _sound = go.AddComponent<AudioSource>();
            _sound.outputAudioMixerGroup = _mixer;
            _sound.spatialBlend = 1f;
            _sound.rolloffMode = AudioRolloffMode.Linear;
            _sound.minDistance = 1.5f;
            _sound.maxDistance = 18f;
            _sound.playOnAwake = false;
        }

        private void OnDestroy()
        {
            if (_sound != null) Destroy(_sound.gameObject);
        }

        /// <summary>Its parts (Antenna.Shape makes them; again after it reshapes one).</summary>
        internal void Find()
        {
            _turn = transform.Find("New/cogling_turn");
            _dish = _turn != null ? _turn.Find("cogling_dish") : null;
            Transform lamp = _dish != null ? _dish.Find("cogling_lamp") : null;
            _light = lamp != null ? lamp.GetComponent<Light>() : null;
            _bulb = lamp != null ? lamp.GetComponentInChildren<MeshRenderer>(true) : null;
        }

        private static void Creaks()
        {
            if (_creaks != null) return;
            _creaks = new AudioClip[0];
            foreach (string name in new[] { "ashwood_door", "wood_door" })
            {
                Door door = ZNetScene.instance?.GetPrefab(name)?.GetComponent<Door>();
                if (door?.m_openEffects?.m_effectPrefabs == null) continue;
                foreach (EffectList.EffectData e in door.m_openEffects.m_effectPrefabs)
                {
                    ZSFX sfx = e?.m_prefab != null ? e.m_prefab.GetComponent<ZSFX>() : null;
                    if (sfx == null || sfx.m_audioClips == null || sfx.m_audioClips.Length == 0) continue;
                    _creaks = sfx.m_audioClips;
                    _mixer = sfx.GetComponent<AudioSource>()?.outputAudioMixerGroup;
                    return;
                }
            }
            Plugin.Log.LogWarning("No door sound to borrow: the antenna moves quietly");
        }

        private void Update()
        {
            if (_turn == null || _dish == null) { Find(); if (_turn == null || _dish == null) return; }
            Move();
            Lamp();
        }

        // ---- moving

        /// <summary>Move number k: when it starts (seconds), the heading it turns to and how far it tips (degrees), the same on every game.</summary>
        private void Slot(long k, float every, out double start, out float yaw, out float tip)
        {
            ZDOID id = _view.GetZDO().m_uid;
            var r = new System.Random(unchecked((int)(id.ID * 73856093L ^ id.UserID * 83492791L ^ k * 19349663L)));
            start = k * (double)every + r.NextDouble() * every * 0.6;
            float turn = Plugin.AntennaTurn.Value;
            yaw = (float)(r.NextDouble() * 2.0 - 1.0) * turn;
            tip = (float)(r.NextDouble() * 2.0 - 1.0) * Plugin.AntennaTip.Value;
        }

        private float MoveSeconds()
        {
            float pitch = Mathf.Max(0.3f, Plugin.AntennaSoundPitch.Value);
            float length = _creaks != null && _creaks.Length > 0 ? _creaks[0].length / pitch : 0.9f;
            return Mathf.Clamp(length, 0.4f, 1.6f);
        }

        private void Move()
        {
            float yaw = 0f, tip = 0f;
            if (Plugin.AntennaMoves.Value && ZNet.instance != null && _view.IsValid())
            {
                float every = Mathf.Max(3f, Plugin.AntennaMoveEvery.Value);
                double now = ZNet.instance.GetTimeSeconds();
                long k = (long)System.Math.Floor(now / every);
                Slot(k, every, out double start, out float toYaw, out float toTip);
                Slot(k - 1, every, out _, out float fromYaw, out float fromTip);
                float u = (float)((now - start) / MoveSeconds());
                if (u >= 0f && u < 1f && _soundSlot != k) { _soundSlot = k; Creak(); }
                // (mechanical: the turn first, the tip in the second half, each eased in and out)
                float a = Ease(Mathf.Clamp01(u * 1.4f)), b = Ease(Mathf.Clamp01(u * 1.6f - 0.6f));
                yaw = Mathf.LerpAngle(fromYaw, toYaw, a);
                tip = Mathf.Lerp(fromTip, toTip, b);
            }
            _turn.localRotation = Quaternion.Euler(0f, yaw, 0f);
            _dish.localRotation = Quaternion.Euler(Plugin.DishTilt.Value + tip, 0f, 0f);
        }

        private static float Ease(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;

        private void Creak()
        {
            if (_sound == null || _creaks == null || _creaks.Length == 0 || Plugin.AntennaSoundVolume.Value <= 0f) return;
            _sound.pitch = Plugin.AntennaSoundPitch.Value;
            _sound.volume = Plugin.AntennaSoundVolume.Value;
            _sound.PlayOneShot(_creaks[Random.Range(0, _creaks.Length)]);
        }

        // ---- the lamp

        private void Lamp()
        {
            bool on = Plugin.AntennaLight.Value;
            if (_light == null || _bulb == null) return;
            if (_light.gameObject.activeSelf != on) _light.gameObject.SetActive(on);
            if (!on) return;
            if (Time.time >= _nextLook)
            {
                // (twice a second: the nearest Cogling's eyes, and whether a camera is near enough to light anything)
                _nextLook = Time.time + 0.5f;
                Camera cam = Utils.GetMainCamera();
                _near = cam != null && Vector3.Distance(cam.transform.position, transform.position) < 45f;
                if (CoglingBody.EyesNear(transform.position, Antenna.Radius, out Color c, out float level)) { _colour = c; _target = Mathf.Max(0.2f, level); }
                else { _colour = Plugin.EyeColor.Value; _target = 1f; }
            }
            _level = Mathf.MoveTowards(_level, _target, Time.deltaTime * 1.5f);
            _light.enabled = _near && Plugin.AntennaLightBrightness.Value > 0f;
            _light.color = _colour;
            _light.intensity = Plugin.AntennaLightBrightness.Value * _level;
            _light.range = Plugin.AntennaLightRange.Value;
            _light.transform.localScale = Vector3.one * Plugin.AntennaLightSize.Value;
            if (_block == null) _block = new MaterialPropertyBlock();
            _bulb.GetPropertyBlock(_block);
            _block.SetColor("_Color", _colour);
            _block.SetColor("_EmissionColor", _colour * Mathf.Max(0.5f, Plugin.EyeGlow.Value) * _level);
            _bulb.SetPropertyBlock(_block);
        }

        private float _target = 1f;
    }
}
