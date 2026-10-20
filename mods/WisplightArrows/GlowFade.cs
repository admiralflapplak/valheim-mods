using System.Collections.Generic;
using UnityEngine;

namespace WisplightArrows
{
    /// <summary>
    /// A glow doesn't vanish at once: over its last seconds its clear radius, light, sparks and wisp shrink to nothing together, so the
    /// mist rolls back in. The game that made it writes when it was lit and how long it lasts on its ZDO (the game's shared clock,
    /// ZNet.GetTimeSeconds), so every game fades it at the same moment, also one that loaded it late. TimedDestruction (on the owner)
    /// takes it away when the fade ends.
    /// </summary>
    internal class GlowFade : MonoBehaviour
    {
        private const string BornKey = "nick_wisparrow_born";
        private const string LastsKey = "nick_wisparrow_lasts";
        private const float FadeSeconds = 5f;   // at most; a short glow fades over its last third

        private ZNetView _nview;
        private double _born;
        private float _lasts;
        private bool _ready;
        private float _shown = 1f;

        private readonly List<ParticleSystemForceField> _fields = new List<ParticleSystemForceField>();
        private readonly List<float> _ranges = new List<float>();
        private readonly List<Light> _lights = new List<Light>();
        private readonly List<float> _intensities = new List<float>();
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private readonly List<float> _rates = new List<float>();
        private readonly List<Transform> _cores = new List<Transform>();
        private readonly List<Vector3> _coreScales = new List<Vector3>();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            foreach (ParticleSystemForceField f in GetComponentsInChildren<ParticleSystemForceField>(true)) { _fields.Add(f); _ranges.Add(f.endRange); }
            foreach (Light l in GetComponentsInChildren<Light>(true)) { _lights.Add(l); _intensities.Add(l.intensity); }
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true)) { _particles.Add(ps); _rates.Add(ps.emission.rateOverTimeMultiplier); }
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true)) { _cores.Add(r.transform); _coreScales.Add(r.transform.localScale); }
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || ZNet.instance == null) return;
            ZDO zdo = _nview.GetZDO();
            if (!_ready)
            {
                if (zdo.GetFloat(LastsKey, 0f) <= 0f)
                {
                    if (!_nview.IsOwner()) return;   // (the maker writes it in its first frame: wait for it)
                    zdo.Set(BornKey, (long)(ZNet.instance.GetTimeSeconds() * 1000.0));
                    zdo.Set(LastsKey, GetComponent<TimedDestruction>()?.m_timeout ?? Plugin.LandingSeconds.Value);
                }
                _born = zdo.GetLong(BornKey, 0L) / 1000.0;
                _lasts = zdo.GetFloat(LastsKey, 0f);
                _ready = true;
            }

            float left = (float)(_born + _lasts - ZNet.instance.GetTimeSeconds());
            float fade = Mathf.Min(FadeSeconds, _lasts / 3f);
            float k = fade > 0f ? Mathf.Clamp01(left / fade) : 1f;
            k = k * k * (3f - 2f * k);   // (eased: it starts and ends the fade gently)
            if (Mathf.Abs(k - _shown) < 0.005f) return;
            _shown = k;

            for (int i = 0; i < _fields.Count; i++) if (_fields[i] != null) _fields[i].endRange = _ranges[i] * k;
            for (int i = 0; i < _lights.Count; i++) if (_lights[i] != null) _lights[i].intensity = _intensities[i] * k;
            for (int i = 0; i < _particles.Count; i++)
                if (_particles[i] != null)
                {
                    ParticleSystem.EmissionModule e = _particles[i].emission;
                    e.rateOverTimeMultiplier = _rates[i] * k;
                }
            for (int i = 0; i < _cores.Count; i++) if (_cores[i] != null) _cores[i].localScale = _coreScales[i] * k;
        }
    }
}
