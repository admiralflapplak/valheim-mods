using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Slacking off: now and then a Cogling takes a break, even with work to do (never while frightened or wary: the danger check comes
    /// first and ends a break at once). What he carries he drops where he stands; his job is let go (he picks things up again after).
    /// He stares into the distance (at the edge of his area, or where he is), looks around, is bored, dances, or sits on the ground for a
    /// while (by a fire burning in his area, at its edge, or where he is).
    ///
    /// Each Cogling has his own personality, never shown: a few traits drawn once from a number saved on him (cogling_traits), which make
    /// one slack more often and longer, dance more, or sit more. It only shows in what he does: a player who names him may come to know
    /// "Frank is lazy" by watching.
    ///
    /// Run on his owner's game (as his brain); what he's doing is saved on him (cogling_slack), so every player's game shows
    /// the same (his body plays it: CoglingBody).
    /// </summary>
    internal sealed class CoglingSlack
    {
        internal enum Activity { None = 0, Stare = 1, LookAround = 2, Bored = 3, Dance = 4, Sit = 5 }
        internal const string SlackKey = "cogling_slack", TraitsKey = "cogling_traits";

        /// <summary>His personality: each from 0 to 1.</summary>
        internal struct Traits
        {
            internal float Diligence, Curiosity, Playfulness, Restfulness, Dreaminess;
            public override string ToString() =>
                $"diligence {Diligence:F2}, curiosity {Curiosity:F2}, playfulness {Playfulness:F2}, restfulness {Restfulness:F2}, dreaminess {Dreaminess:F2}";
        }

        private enum Step { Going, Facing, Doing, StandingUp }

        private readonly CoglingAI _ai;
        private readonly CoglingNavigator _nav;
        private readonly CoglingJob _job;
        private Step _step;
        private float _until, _nextRoll, _lastEnded = -999f, _stepUntil;
        private Vector3 _face;
        private Traits? _me;

        internal Activity Doing { get; private set; }
        internal bool Seated => Doing == Activity.Sit && (_step == Step.Doing || _step == Step.StandingUp);

        internal CoglingSlack(CoglingAI ai, CoglingNavigator nav, CoglingJob job)
        {
            _ai = ai;
            _nav = nav;
            _job = job;
            _nextRoll = Time.time + Random.Range(20f, 40f);
            Write(Activity.None);   // (a break saved as his area unloaded: over)
        }

        private ZDO Zdo
        {
            get
            {
                ZNetView view = _ai.GetComponent<ZNetView>();
                return view != null && view.IsValid() ? view.GetZDO() : null;
            }
        }

        private bool Owner => _ai.GetComponent<ZNetView>()?.IsOwner() == true;

        /// <summary>His personality (drawn and saved the first time it's asked for).</summary>
        internal Traits Me
        {
            get
            {
                if (_me.HasValue) return _me.Value;
                ZDO zdo = Zdo;
                int seed = zdo?.GetInt(TraitsKey) ?? 0;
                if (seed == 0)
                {
                    seed = Random.Range(1, int.MaxValue);
                    if (zdo != null && Owner) zdo.Set(TraitsKey, seed);
                }
                var r = new System.Random(seed);
                float N() => (float)r.NextDouble();
                _me = new Traits { Diligence = N(), Curiosity = N(), Playfulness = N(), Restfulness = N(), Dreaminess = N() };
                return _me.Value;
            }
        }

        // ---- deciding

        /// <summary>Every few seconds a chance of a break (more for a lazy one, less while working). True: he's on a break now.</summary>
        internal bool Roll(bool working, List<Antenna> network)
        {
            if (Time.time < _nextRoll) return false;
            _nextRoll = Time.time + 10f;
            if (!Plugin.Slacking.Value || Plugin.SlackEvery.Value <= 0f || Time.time - _lastEnded < 45f) return false;
            float lazy = Mathf.Lerp(1.8f, 0.35f, Me.Diligence);
            float chance = 10f / (Plugin.SlackEvery.Value * 60f) * lazy * (working ? 0.7f : 1f);
            if (Random.value >= chance) return false;
            return Start(Choose(network), network);
        }

        /// <summary>Which break, by his personality.</summary>
        private Activity Choose(List<Antenna> network)
        {
            Traits t = Me;
            var weights = new List<(Activity, float)>
            {
                (Activity.Stare, 0.5f + t.Dreaminess * 1.5f),
                (Activity.LookAround, 0.5f + t.Curiosity * 1.5f),
                (Activity.Bored, 0.3f + (1f - t.Diligence) * 1.5f),
                (Activity.Dance, t.Playfulness < 0.15f ? 0.05f : 0.2f + t.Playfulness * 2f),   // (some never dance)
            };
            weights.Add((Activity.Sit, 0.4f + t.Restfulness * 1.8f));
            float total = weights.Sum(w => w.Item2), pick = Random.value * total;
            foreach ((Activity a, float w) in weights) { if (pick < w) return a; pick -= w; }
            return Activity.Stare;
        }

        /// <summary>A break now (testing: `cogling slack`, or a roll). False if it can't be had.</summary>
        internal bool Start(Activity what, List<Antenna> network)
        {
            Stop();
            Traits t = Me;
            Vector3 here = _ai.transform.position;
            float duration;
            Vector3? go = null;
            switch (what)
            {
                case Activity.Sit:
                {
                    // on the ground: by a fire burning in his area (facing it), else at the edge of his area looking out, or where he is
                    duration = Random.Range(20f, 50f) * (0.6f + t.Restfulness);
                    Fireplace fire = Fire(network);
                    Vector3 away = Vector3.forward;
                    Vector3? rim = fire == null && Random.value < 0.5f ? Edge(network, out away) : null;
                    if (fire != null)
                    {
                        Vector3 from = Flat(here - fire.transform.position);
                        go = Floor(fire.transform.position + Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * from * 1.7f);
                        _face = fire.transform.position;
                    }
                    else if (rim.HasValue) { go = rim; _face = rim.Value + away * 10f; }
                    else _face = here + _ai.transform.forward * 5f;
                    break;
                }
                case Activity.Stare:
                    // into the distance: at the edge of his area, facing out (or just where he is)
                    duration = Random.Range(12f, 30f) * (0.7f + t.Dreaminess * 0.6f);
                    Vector3 outward = Vector3.forward;
                    Vector3? edge = Random.value < 0.6f ? Edge(network, out outward) : null;
                    if (edge.HasValue) { go = edge; _face = edge.Value + outward * 10f; }
                    else _face = here + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 10f;
                    break;
                case Activity.LookAround:
                    duration = CoglingBody.ClipLength("looking_around") * (t.Curiosity > 0.6f ? 2f : 1f);
                    _face = here + _ai.transform.forward * 5f;
                    break;
                case Activity.Bored:
                    // (a lazy one stays bored longer: once round, twice, or three times)
                    duration = CoglingBody.ClipLength("bored") * (1 + (t.Diligence < 0.4f ? 1 : 0) + (t.Diligence < 0.15f ? 1 : 0));
                    _face = here + _ai.transform.forward * 5f;
                    break;
                case Activity.Dance:
                    duration = CoglingBody.ClipLength("robot_dance") * (t.Playfulness > 0.7f ? 2f : 1f);
                    Player near = Player.GetClosestPlayer(here, 15f);   // (for an audience, if there is one)
                    _face = near != null ? near.transform.position : here + _ai.transform.forward * 5f;
                    break;
                default:
                    return false;
            }
            if (go.HasValue && !_nav.SetTarget(go.Value)) go = null;   // (no way there: where he is)
            // what he carries falls where he stands; his job is let go
            if (_job.Carrying) _job.Drop(); else _job.Abandon();
            Doing = what;
            _until = duration;   // (counted from when he starts: Step.Doing)
            if (go.HasValue) _step = Step.Going;
            else { _step = Step.Facing; _stepUntil = Time.time + 0.8f; }
            _ai.Log($"slacking off: {what} for {duration:F0} s{(go.HasValue ? $" at {go.Value:F1}" : "")} ({t})");
            return true;
        }

        // ---- on a break

        /// <summary>The break goes on. False: it's over (he goes back to work).</summary>
        internal bool Update(float dt)
        {
            if (Doing == Activity.None) return false;
            switch (_step)
            {
                case Step.Going:
                {
                    var r = _nav.Update(dt);
                    if (r == CoglingNavigator.Result.Moving) return true;
                    _ai.Stop();
                    _step = Step.Facing;
                    _stepUntil = Time.time + 0.8f;
                    return true;
                }
                case Step.Facing:
                    _ai.Stop();
                    _ai.Face(_face);
                    if (Time.time < _stepUntil) return true;
                    _step = Step.Doing;
                    _until += Time.time;
                    Write(Doing);
                    return true;
                case Step.Doing:
                    _ai.Stop();
                    if (Doing != Activity.Sit) _ai.Face(_face);   // (seated, he stays as he sat)
                    if (Time.time < _until && Plugin.Slacking.Value) return true;
                    if (Doing == Activity.Sit)
                    {
                        // standing up takes a while: he's still on his break till he's up
                        Write(Activity.None);
                        _step = Step.StandingUp;
                        _stepUntil = Time.time + CoglingBody.StandUpSeconds;
                        return true;
                    }
                    End();
                    return false;
                case Step.StandingUp:
                    _ai.Stop();
                    if (Time.time < _stepUntil) return true;
                    End();
                    return false;
            }
            return false;
        }

        private void End()
        {
            _ai.Log($"break over ({Doing})");
            Stop();
            _lastEnded = Time.time;
        }

        /// <summary>The break ends now (danger, or another one starts): he's up at once.</summary>
        internal void Stop()
        {
            if (Doing != Activity.None) Write(Activity.None);
            Doing = Activity.None;
        }

        private void Write(Activity what)
        {
            ZDO zdo = Zdo;
            if (zdo == null || !Owner) return;
            if (zdo.GetInt(SlackKey) != (int)what) zdo.Set(SlackKey, (int)what);
        }

        internal (string key, string a, string b) Describe()
        {
            if (_step == Step.Going) return ("cogling_task_slack_going", "", "");
            switch (Doing)
            {
                case Activity.Stare: return ("cogling_task_slack_stare", "", "");
                case Activity.LookAround: return ("cogling_task_slack_look", "", "");
                case Activity.Bored: return ("cogling_task_slack_bored", "", "");
                case Activity.Dance: return ("cogling_task_slack_dance", "", "");
                case Activity.Sit: return ("cogling_task_slack_sit", "", "");
            }
            return ("cogling_task_resting", "", "");
        }

        internal string Status() => $"slacking {Doing} ({_step}), {Me}";

        // ---- places

        /// <summary>A fire burning in his area within 25 m (a hearth, a campfire, a brazier...), the nearest, or null.</summary>
        private Fireplace Fire(List<Antenna> network)
        {
            Vector3 here = _ai.transform.position;
            Fireplace best = null;
            float bestDistance = 25f;
            foreach (Fireplace f in Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
            {
                if (f == null || !Antenna.InArea(network, f.transform.position) || !f.IsBurning()) continue;
                float d = Vector3.Distance(here, f.transform.position);
                if (d < bestDistance) { best = f; bestDistance = d; }
            }
            return best;
        }

        /// <summary>A spot near the edge of his area he can stand on, and the way out from there; null if none was found.</summary>
        private Vector3? Edge(List<Antenna> network, out Vector3 outward)
        {
            outward = Vector3.forward;
            for (int i = 0; i < 6; i++)
            {
                Antenna a = network[Random.Range(0, network.Count)];
                outward = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 p = a.transform.position + outward * Antenna.Radius * 0.85f;
                if (!ZoneSystem.instance.GetSolidHeight(p, out float height)) continue;
                p.y = height;
                if (Antenna.InArea(network, p)) return p;
            }
            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }

        private static Vector3 Floor(Vector3 p)
        {
            // (the floor under it, not the fire itself)
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 3f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain"));
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits) if (hit.collider.GetComponentInParent<Fireplace>() == null) return hit.point;
            return p;
        }

        internal static void Forget() { }
    }
}
