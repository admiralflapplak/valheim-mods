using UnityEngine;
using Object = UnityEngine.Object;

namespace WisplightArrows
{
    /// <summary>
    /// The arrow flies faster than the mist can be seen to part: its own clear zone moves on before the eye catches the hole, and the mist
    /// fills in behind. So the projectile drops clear zones along its path (only inside mist), each a bare force field and Demister with
    /// nothing to see, that holds for a while and then shrinks away so the mist closes back in gently. They are local: every game runs its
    /// own copy of the flying arrow, and drops its own trail.
    /// </summary>
    internal class MistTrail : MonoBehaviour
    {
        public const string HitRpc = "nick_wisparrow_hit";   // sent to every game by the shooter's when the arrow hits: its wisp goes out
        private const float Spacing = 0.6f;   // a new point every this much of the clear radius flown, so the holes overlap

        private Vector3 _last;
        private bool _started;
        private bool _hit;

        private void Awake()
        {
            ZNetView nview = GetComponent<ZNetView>();
            if (nview != null) nview.Register(HitRpc, sender => PutOut());
        }

        internal void PutOut()
        {
            _hit = true;
            Transform wisp = transform.Find("wisp");
            if (wisp != null) wisp.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            GameObject point = ArrowItem.TrailPoint;
            float seconds = Plugin.TrailSeconds.Value;
            if (_hit || point == null || seconds <= 0f) return;

            float radius = Plugin.FlightRadius.Value;
            float step = Mathf.Max(1f, radius * Spacing);
            Vector3 now = transform.position;
            if (!_started) { _last = now; _started = true; return; }

            float flown = Vector3.Distance(_last, now);
            if (flown < step) return;
            int drops = Mathf.Min(Mathf.FloorToInt(flown / step), 20);   // (a long frame: fill the gap, but never a flood)
            for (int i = 1; i <= drops; i++)
            {
                Vector3 at = Vector3.Lerp(_last, now, i * step / flown);
                if (!Mister.InsideMister(at, radius)) continue;
                GameObject made = Object.Instantiate(point, at, Quaternion.identity);
                made.GetComponent<TrailPoint>().Begin(radius, seconds);
            }
            _last = Vector3.Lerp(_last, now, drops * step / flown);
        }
    }

    /// <summary>One clear zone of the trail: full size for most of its time, then shrinking to nothing, then gone.</summary>
    internal class TrailPoint : MonoBehaviour
    {
        private const float ShrinkPart = 0.4f;   // the last part of its time it spends closing

        private ParticleSystemForceField _field;
        private float _radius, _seconds, _age;

        public void Begin(float radius, float seconds)
        {
            _field = GetComponent<ParticleSystemForceField>();
            _radius = radius;
            _seconds = seconds;
            if (_field != null) _field.endRange = radius;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_field == null || _age >= _seconds) { Destroy(gameObject); return; }
            float shrinkFrom = _seconds * (1f - ShrinkPart);
            if (_age > shrinkFrom) _field.endRange = _radius * (1f - (_age - shrinkFrom) / (_seconds * ShrinkPart));
        }
    }
}
