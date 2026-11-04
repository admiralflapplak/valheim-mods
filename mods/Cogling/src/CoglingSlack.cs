using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Slacking off: now and then a Cogling takes a break, even with work to do (never while frightened or wary: the danger check comes
    /// first and ends a break at once). What he carries he drops where he stands; his job is let go (he picks things up again after).
    /// He stares into the distance (at the edge of his area, or where he is), looks around, is bored, dances, or sits on the ground for a
    /// while (by a fire burning in his area, at its edge, or where he is). Or he has a chat with another Cogling: a sociable one asks one
    /// near (who comes if he's sociable too, less likely when busy), they meet halfway and face each other, and take turns talking (the
    /// one who asked decides whose turn it is; the talker plays a talking clip and babbles, the other listens and looks at him).
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
        // (Watch and Chair: numbers of their own, past the poses below; saved as they are)
        internal enum Activity { None = 0, Stare = 1, LookAround = 2, Bored = 3, Dance = 4, Sit = 5, Chat = 6, Watch = 14, Chair = 15, Cheer = 16, Nap = 19, Exercise = 20, Tag = 22, Follow = 23, Animal = 24, Sneak = 25, Treasure = 26, Fiddle = 27, Shelter = 28, Inspect = 29 }
        // (cheering on a dancer: clapping is Activity.Cheer; these two are cheering)
        internal const int PoseCheer2 = 17, PoseCheer3 = 18;
        // (21 was sit-ups: taken out, they looked wonky on him)
        // (tinkering: Working_A is Activity.Fiddle; these are Working_B and C)
        internal const int PoseWork2 = 30, PoseWork3 = 31;
        private int _workPose;
        private static readonly int[] CheerPoses = { (int)Activity.Cheer, PoseCheer2, PoseCheer3 };
        internal static string CheerClip(int pose) =>
            pose == (int)Activity.Cheer ? "social_handclap01" : pose == PoseCheer2 ? "social_cheer01" : pose == PoseCheer3 ? "social_cheer02" : null;
        // (the dance he does, saved for his body: his robot dance is Activity.Dance; these are the game's own)
        internal const int PoseHeadbang = 9, PoseVikingDance = 10;
        private int _dancePose;
        private float _nextCheer;
        // (saved for his body: a break's own number, but in a chat what he does in it: talking, with either clip, or listening)
        // (the talker's clips are the Social pack's: three ways of talking, two of asking)
        internal const int PoseTalk = 6, PoseListen = 7, PoseTalk2 = 8, PoseTalk3 = 11, PoseQuestion = 12, PoseQuestion2 = 13;
        private static readonly int[] TalkPoses = { PoseTalk, PoseTalk2, PoseTalk3 }, AskPoses = { PoseQuestion, PoseQuestion2 };
        internal static bool Talking(int pose) => System.Array.IndexOf(TalkPoses, pose) >= 0 || System.Array.IndexOf(AskPoses, pose) >= 0;
        internal static string TalkClip(int pose) =>
            pose == PoseTalk ? "social_talk01" : pose == PoseTalk2 ? "social_talk02" : pose == PoseTalk3 ? "social_talk03"
            : pose == PoseQuestion ? "social_question01" : pose == PoseQuestion2 ? "social_question02" : null;
        internal const string TreasureKey = "cogling_treasure";   // (what he admires: shown in his hands, never taken from the chest)
        internal const string SlackKey = "cogling_slack", TraitsKey = "cogling_traits", LookKey = "cogling_look", SeatKey = "cogling_seat", WatchKey = "cogling_watch";

        /// <summary>His personality: each from 0 to 1.</summary>
        internal struct Traits
        {
            internal float Diligence, Curiosity, Playfulness, Restfulness, Dreaminess, Sociability;
            public override string ToString() =>
                $"diligence {Diligence:F2}, curiosity {Curiosity:F2}, playfulness {Playfulness:F2}, restfulness {Restfulness:F2}, dreaminess {Dreaminess:F2}, sociability {Sociability:F2}";
        }

        private enum Step { Going, Stepping, Facing, Doing, StandingUp }   // (Stepping: the last bit to a chair, exactly)

        private readonly CoglingAI _ai;
        private readonly CoglingNavigator _nav;
        private readonly CoglingJob _job;
        private Step _step;
        private float _until, _nextRoll, _lastEnded = -999f, _stepUntil;
        private Vector3 _face;
        private Traits? _me;
        private CoglingSlack _partner;   // in a chat: the one who started it (a guest's), or null (the one who started it)
        private bool _host, _asked, _begun;
        private float _turnUntil, _nextJoinLook;
        private CoglingSlack _lastTalker;
        // the one who started a chat: who else is in it (up to three more), and the middle of their circle
        private readonly List<CoglingSlack> _guests = new List<CoglingSlack>();
        private Vector3 _centre;
        internal const int MostInAChat = 4;
        private Chair _chair;            // sitting on a chair: which, and the top of its seat
        private Vector3 _seat, _spot;
        private float _stepGiveUp;
        private Player _watched;         // watching a player: who
        private bool _catchingUp;        // (watching: his body turning to catch up with his head)
        private CoglingSlack _other;     // tag: who he plays with; sneaking: who he creeps up on
        private bool _it;                // tag: he's "it" (he chases)
        private float _graceUntil, _nextAim, _outsideSince, _booAt = -1f;
        private Character _animal;       // visiting a tame animal: which
        private Container _chest;        // treasure: the chest it's from
        private string _treasure;        //   and what (a prefab name)
        private int _phase;              //   0 to the chest, 1 taking it out, 2 off to admire it, 3 admiring, 4 back, 5 putting it back
        private float _phaseUntil;
        private Piece _inspected;        // inspecting: the piece a player built
        private Vector3 _animalWas;
        private CoglingSlack _dancer;    // dancing along or cheering: whose dance
        private float _nextCheerPose;
        private List<Antenna> _network;
        private bool _forced;   // (a break from `cogling slack`: a dance brings everyone free near, for testing)
        /// <summary>Chairs a Cogling on this game has taken (two never pick the same).</summary>
        private static readonly HashSet<Chair> Taken = new HashSet<Chair>();

        /// <summary>Every Cogling brain on this game (a chat's partner is one of them).</summary>
        private static readonly List<CoglingSlack> All = new List<CoglingSlack>();

        internal Activity Doing { get; private set; }
        internal bool Seated => (Doing == Activity.Sit || Doing == Activity.Chair || Doing == Activity.Nap || Doing == Activity.Exercise) && (_step == Step.Doing || _step == Step.StandingUp);
        private bool GetsUp => Doing == Activity.Sit || Doing == Activity.Chair || Doing == Activity.Nap;

        internal CoglingSlack(CoglingAI ai, CoglingNavigator nav, CoglingJob job)
        {
            _ai = ai;
            _nav = nav;
            _job = job;
            _nextRoll = Time.time + Random.Range(20f, 40f);
            Write(0);   // (a break saved as his area unloaded: over)
            if (Owner && !string.IsNullOrEmpty(Zdo?.GetString(TreasureKey))) Zdo.Set(TreasureKey, "");   // (and a treasure he was holding: back)
            All.Add(this);
        }

        /// <summary>His brain is going (unloaded, hot reload).</summary>
        internal void Dispose()
        {
            All.Remove(this);
            if (Owner) Stop();
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
                var m = _me.Value; m.Sociability = N(); _me = m;   // (drawn after the first five: theirs stay as they were)
                return _me.Value;
            }
        }

        // ---- deciding

        /// <summary>Every few seconds a chance of a break (more for a lazy one, less while working). True: he's on a break now.</summary>
        /// <summary>Never power down: nothing to do, so a break now (whichever his personality picks). False if none could be had.</summary>
        internal bool Idle(List<Antenna> network) => Plugin.Slacking.Value && Start(Choose(network), network);

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
            Player near = Player.GetClosestPlayer(_ai.transform.position, 18f);
            // tag (playful ones, with another free one near), tagging along after a player (curious ones), a tame animal, creeping up
            if (Partner(network, false) != null)
            {
                weights.Add((Activity.Tag, t.Playfulness < 0.2f ? 0.03f : 0.1f + t.Playfulness * 1.2f));
                weights.Add((Activity.Sneak, t.Playfulness < 0.5f ? 0.02f : 0.1f + t.Playfulness * 0.5f));
            }
            if (near != null && Antenna.InArea(network, near.transform.position)) weights.Add((Activity.Follow, 0.15f + t.Curiosity * 0.7f));
            if (TameNear(network) != null) weights.Add((Activity.Animal, 0.2f + t.Curiosity * 0.6f + t.Sociability * 0.3f));
            // treasure (curious dreamers), tinkering with the antenna (hard workers), a player's new building (curious ones)
            if (Shiny(network, out _, out _)) weights.Add((Activity.Treasure, 0.1f + (t.Curiosity + t.Dreaminess) * 0.35f));
            weights.Add((Activity.Fiddle, 0.1f + t.Diligence * 0.5f));
            if (Fresh(network) != null) weights.Add((Activity.Inspect, 0.6f + t.Curiosity * 1.8f));
            // raining and out in it: under a roof, mostly
            if (EnvMan.IsWet() && !UnderRoof(_ai.transform.position)) weights.Add((Activity.Shelter, 4f + weights.Sum(w => w.Item2)));
            // a nap (restful ones; more at night), exercise (hard workers: even their breaks are work)
            weights.Add((Activity.Nap, (0.15f + t.Restfulness * 1.2f) * (EnvMan.IsNight() ? 2.5f : 1f)));
            weights.Add((Activity.Exercise, t.Diligence < 0.3f ? 0.03f : 0.1f + t.Diligence * 0.9f));
            // watching a player, a bit too closely (a curious one more)
            if (near != null && Antenna.InArea(network, near.transform.position)) weights.Add((Activity.Watch, 0.25f + t.Curiosity * 0.9f));
            if (Partner(network, false) != null) weights.Add((Activity.Chat, 0.3f + t.Sociability * 2f));
            float total = weights.Sum(w => w.Item2), pick = Random.value * total;
            foreach ((Activity a, float w) in weights) { if (pick < w) return a; pick -= w; }
            return Activity.Stare;
        }

        /// <summary>A break now (testing: `cogling slack`, or a roll). False if it can't be had.</summary>
        internal bool Start(Activity what, List<Antenna> network, bool force = false)
        {
            Stop();
            _network = network;
            _forced = force;
            Traits t = Me;
            Vector3 here = _ai.transform.position;
            float duration;
            Vector3? go = null;
            switch (what)
            {
                case Activity.Sit:
                case Activity.Chair:
                {
                    duration = Random.Range(20f, 50f) * (0.6f + t.Restfulness);
                    // on a chair, stool or bench in his area, if there's a free one (and he feels like it): in front of it, his back to it
                    Chair chair = what == Activity.Chair || Random.value < Plugin.ChairChance.Value ? FreeChair(network, out _seat) : null;
                    if (chair != null)
                    {
                        Vector3 ahead = Flat(chair.m_attachPoint.forward);
                        go = Floor(new Vector3(_seat.x, chair.m_attachPoint.position.y + 0.3f, _seat.z) + ahead * Plugin.ChairStandOff.Value);
                        _spot = go.Value;
                        _face = go.Value + ahead * 5f;
                        _chair = chair;
                        Taken.Add(chair);
                        what = Activity.Chair;
                        break;
                    }
                    if (what == Activity.Chair) return false;
                    // on the ground: by a fire burning in his area (facing it), else at the edge of his area looking out, or where he is
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
                case Activity.Tag:
                {
                    // a game of tag with another free one near (who may say no: yes more likely the more playful he is); he's "it" first
                    CoglingSlack other = Partner(network, false);
                    if (other == null) return false;
                    if (!force && Random.value >= 0.2f + other.Me.Playfulness * 0.8f) { _ai.Log("asked another Cogling to play tag: no"); return false; }
                    if (!other._ai.JoinTag(this)) return false;
                    _other = other;
                    _host = true;   // (he ends the game)
                    _it = true;
                    _graceUntil = 0f;
                    _face = other._ai.transform.position;
                    duration = Random.Range(20f, 35f) * (0.7f + (t.Playfulness + other.Me.Playfulness) / 3f);
                    break;
                }
                case Activity.Follow:
                {
                    // tagging along after a player near, a few steps behind, for a while
                    Player them = Player.GetClosestPlayer(here, 18f);
                    if (them == null) return false;
                    _watched = them;
                    _face = them.transform.position;
                    _outsideSince = -1f;
                    duration = Random.Range(20f, 40f) * (0.7f + t.Curiosity * 0.6f);
                    break;
                }
                case Activity.Animal:
                {
                    // over to a tame animal in his area, to stand and watch it
                    _animal = TameNear(network);
                    if (_animal == null) return false;
                    _animalWas = _animal.transform.position;
                    go = Floor(_animalWas + Flat(here - _animalWas) * 1.8f);
                    _face = _animalWas;
                    duration = Random.Range(15f, 30f) * (0.7f + t.Curiosity * 0.6f);
                    break;
                }
                case Activity.Sneak:
                {
                    // creeping up behind another Cogling near, to give him a fright
                    CoglingSlack victim = Victim(network);
                    if (victim == null) return false;
                    _other = victim;
                    _face = victim._ai.transform.position;
                    duration = 25f;   // (gives up after that)
                    break;
                }
                case Activity.Treasure:
                {
                    // something shiny from a chest of his area (a gem, a trophy...): out, off to admire it, and back (only shown in his
                    // hands: it never leaves the chest, so nothing can be lost)
                    if (!Shiny(network, out _chest, out _treasure)) return false;
                    go = Floor(_chest.transform.position + Flat(here - _chest.transform.position) * 1.0f);
                    _face = _chest.transform.position;
                    _phase = 0;
                    duration = 120f;   // (it ends when it's back in the chest)
                    break;
                }
                case Activity.Fiddle:
                {
                    // tinkering with his antenna: one of Rig_Medium's working clips (the dish twitches: AntennaLife)
                    _workPose = Random.Range(0, 3) == 0 ? (int)Activity.Fiddle : Random.value < 0.5f ? PoseWork2 : PoseWork3;
                    Antenna a = network.Where(x => x != null).OrderBy(x => Vector3.Distance(x.transform.position, here)).FirstOrDefault();
                    if (a == null) return false;
                    go = Floor(a.transform.position + Flat(here - a.transform.position) * 0.55f);
                    _spot = go.Value;   // (the last steps there exactly: Step.Stepping)
                    _face = a.transform.position;
                    duration = Random.Range(8f, 15f) * (0.7f + t.Diligence * 0.6f);
                    break;
                }
                case Activity.Shelter:
                {
                    // out of the rain: the nearest spot under a roof in his area (else, nothing)
                    Vector3? dry = Dry(network);
                    if (!dry.HasValue) return false;
                    go = dry;
                    _face = dry.Value + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 5f;
                    duration = Random.Range(30f, 60f);
                    break;
                }
                case Activity.Inspect:
                {
                    // a player's new building: over to it, to look it up and down
                    _inspected = Fresh(network);
                    if (_inspected == null) return false;
                    Seen.Add(_inspected.transform.position);
                    Vector3 at = _inspected.transform.position;
                    go = Floor(at + Flat(here - at) * 2f);
                    _face = at;
                    duration = Random.Range(6f, 10f);
                    break;
                }
                case Activity.Nap:
                {
                    // lying down for a recharge: at the edge of his area, or where he is
                    duration = Random.Range(25f, 60f) * (0.6f + t.Restfulness);
                    Vector3 away = Vector3.forward;
                    Vector3? rim = Random.value < 0.5f ? Edge(network, out away) : null;
                    if (rim.HasValue) { go = rim; _face = rim.Value + away * 10f; }
                    else _face = here + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 5f;
                    break;
                }
                case Activity.Exercise:
                    // push-ups, where he is (a hard worker keeps at it longer)
                    duration = Random.Range(10f, 20f) * (0.7f + t.Diligence * 0.6f);
                    _face = here + _ai.transform.forward * 5f;
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
                    // his robot dance mostly; now and then the players' headbang or their dance
                    float d = Random.value;
                    _dancePose = d < 0.6f ? (int)Activity.Dance : d < 0.8f ? PoseHeadbang : PoseVikingDance;
                    duration = _dancePose == (int)Activity.Dance ? CoglingBody.ClipLength("robot_dance") * (t.Playfulness > 0.7f ? 2f : 1f)
                             : Random.Range(8f, 16f) * (0.7f + t.Playfulness * 0.6f);
                    Player near = Player.GetClosestPlayer(here, 15f);   // (for an audience, if there is one)
                    _face = near != null ? near.transform.position : here + _ai.transform.forward * 5f;
                    break;
                case Activity.Watch:
                {
                    // a player near: he turns to them and watches what they do, his head following them
                    Player them = Player.GetClosestPlayer(here, 18f);
                    if (them == null) return false;
                    _watched = them;
                    _face = them.transform.position;
                    duration = Random.Range(10f, 22f) * (0.7f + t.Curiosity * 0.6f);
                    break;
                }
                case Activity.Chat:
                {
                    // halfway between them, a little apart, facing each other (the other one is asked first: he may say no)
                    CoglingSlack other = Partner(network, !force);
                    if (other == null) return false;
                    Vector3 there = other._ai.transform.position, side = Flat(here - there), mid = (here + there) / 2f;
                    go = Floor(mid + side * 0.65f);
                    if (!other._ai.JoinChat(this, Floor(mid - side * 0.65f))) return false;
                    _guests.Clear();
                    _guests.Add(other);
                    _centre = mid;
                    _host = true;
                    _turnUntil = 0f;
                    _lastTalker = null;
                    _nextJoinLook = Time.time + Random.Range(6f, 10f);
                    _face = mid;
                    duration = Random.Range(15f, 40f) * (0.6f + (t.Sociability + other.Me.Sociability) / 2f);
                    break;
                }
                default:
                    return false;
            }
            if (go.HasValue && !_nav.SetTarget(go.Value)) go = null;   // (no way there: where he is)
            // what he carries falls where he stands; his job is let go
            if (_job.Carrying) { _job.Drop(); _ai.Say(CoglingVoice.Line.Oops); } else _job.Abandon();
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
                    if (Doing == Activity.Chair || Doing == Activity.Fiddle) { _step = Step.Stepping; _stepGiveUp = Time.time + 3f; return true; }
                    _step = Step.Facing;
                    _stepUntil = Time.time + 0.8f;
                    return true;
                }
                case Step.Stepping:
                {
                    // (the navigator stops within 0.6 m: the last bit straight to the spot in front of the seat, a few small steps)
                    Vector3 to = _spot - _ai.transform.position;
                    to.y = 0f;
                    if (to.magnitude > 0.08f && Time.time < _stepGiveUp) { _ai.Walk(to.normalized); return true; }
                    _ai.Stop();
                    _step = Step.Facing;
                    _stepUntil = Time.time + 0.8f;
                    return true;
                }
                case Step.Facing:
                    _ai.Stop();
                    if (Doing == Activity.Chat)
                    {
                        if (!InChat()) { End(); return false; }
                        _face = Centre;
                    }
                    if (Doing == Activity.Watch) { if (!Watching) { End(); return false; } _face = _watched.transform.position; }
                    if (Doing == Activity.Follow && _watched != null) _face = _watched.transform.position;
                    if ((Doing == Activity.Tag || Doing == Activity.Sneak) && _other != null && _other._ai != null) _face = _other._ai.transform.position;
                    if (Doing == Activity.Animal && _animal != null) _face = _animal.transform.position;
                    if (Doing == Activity.Inspect && _inspected != null) _face = _inspected.transform.position;
                    if (_dancer != null)
                    {
                        if (_dancer._ai == null || _dancer.Doing != Activity.Dance) { End(); return false; }
                        if (Doing == Activity.Cheer) _face = _dancer._ai.transform.position;
                    }
                    if (Doing == Activity.Watch) WatchTurn(); else _ai.Face(_face);
                    if (Time.time < _stepUntil) return true;
                    if (Doing == Activity.Chair)
                    {
                        // (his back square to the chair before he hops up, and near enough; else the ground)
                        Vector3 want = Flat(_face - _ai.transform.position);
                        if (Vector3.Angle(Flat(_ai.transform.forward), want) > 8f && Time.time < _stepUntil + 2f) return true;
                        if (_chair == null || !_chair || Flat2(_ai.transform.position - _seat) > 1.3f || _chair.IsInUse())
                        {
                            _ai.Log("couldn't get onto the chair: on the ground");
                            Release();
                            Doing = Activity.Sit;
                        }
                        else WriteSeat(_seat);
                    }
                    if (Doing == Activity.Chat && !_begun && (_host ? _guests.All(g => g._step < Step.Facing) : _partner._step < Step.Facing))
                    {
                        // (waiting for the other one to get there; too long, and the chat's off)
                        if (Time.time > _stepUntil + 15f) { _ai.Log("the other one never came"); End(); return false; }
                        return true;
                    }
                    _step = Step.Doing;
                    bool first = !_begun;
                    _begun = true;
                    if (first) _until += Time.time;
                    if (Doing == Activity.Chat)
                    {
                        // (in place again after the circle moved: still in the chat, as he was)
                        if (first) { Write(PoseListen); WriteLook(Centre); }
                        return true;
                    }
                    else if (Doing == Activity.Cheer) NextCheer();
                    else
                    {
                        if ((Doing == Activity.Watch || Doing == Activity.Follow) && Owner) Zdo?.Set(WatchKey, _watched.GetZDOID());
                        if (Doing == Activity.Animal && Owner && _animal != null) Zdo?.Set(WatchKey, _animal.GetZDOID());
                        if (Doing == Activity.Inspect && Owner && _inspected != null && _inspected.GetComponent<ZNetView>() is ZNetView pv && pv.IsValid()) Zdo?.Set(WatchKey, pv.GetZDO().m_uid);

                        if (Doing == Activity.Tag || Doing == Activity.Sneak) _ai.Running = Doing == Activity.Tag;
                        if (Doing == Activity.Sneak) _ai.WalkScale = 0.33f;   // (slow: to his sneak's steps)
                        // (sheltering: as staring, his gaze drifting)
                        Write(Doing == Activity.Dance ? _dancePose : Doing == Activity.Shelter ? (int)Activity.Stare : Doing == Activity.Fiddle ? _workPose : (int)Doing);
                        if (Doing == Activity.Dance && _dancer == null) Invite();
                    }
                    if (_dancer != null) _until = Time.time + Mathf.Max(5f, _dancer._until - Time.time);   // (till the dancer stops, about)
                    _nextCheer = Time.time + Random.Range(2f, 5f);
                    return true;
                case Step.Doing:
                    if (Doing == Activity.Tag) return Tag(dt);
                    if (Doing == Activity.Follow) return Follow(dt);
                    if (Doing == Activity.Sneak) return Sneak(dt);
                    if (Doing == Activity.Treasure) return Treasure(dt);
                    if (Doing == Activity.Shelter && !EnvMan.IsWet() && Time.time > _until - 20f) { End(); return false; }   // (the rain's over)
                    if (Doing == Activity.Animal && !Animal(dt)) return false;
                    _ai.Stop();
                    if (Doing == Activity.Chat)
                    {
                        if (!InChat()) { End(); return false; }
                        _face = Centre;
                        if (_host)
                        {
                            if (Time.time >= _turnUntil) NextTurn();
                            if (Time.time >= _nextJoinLook) MoreJoin();
                        }
                    }
                    if (Doing == Activity.Watch)
                    {
                        if (!Watching) { End(); return false; }
                        _face = _watched.transform.position;
                        WatchTurn();
                    }
                    else if (!GetsUp && Doing != Activity.Exercise) _ai.Face(_face);   // (seated, lying or on the ground, he stays as he is)
                    if (_dancer != null && (_dancer._ai == null || _dancer.Doing != Activity.Dance)) { End(); return false; }   // (the dance is over)
                    if (Doing == Activity.Cheer)
                    {
                        _face = _dancer._ai.transform.position;
                        if (Time.time >= _nextCheerPose) NextCheer();
                    }
                    if ((Doing == Activity.Dance || Doing == Activity.Cheer) && Time.time >= _nextCheer) { _nextCheer = Time.time + Random.Range(3f, 7f) * (Doing == Activity.Cheer ? 1.5f : 1f); _ai.Say(CoglingVoice.Line.Excited); }
                    // (in a chat, the one who asked ends it; the other goes on till then, or a minute and a half at most)
                    if (Doing == Activity.Chat && !_host && Time.time < _until + 90f) return true;
                    if (Time.time < _until && Plugin.Slacking.Value) return true;
                    if (Doing == Activity.Inspect) _ai.Say(CoglingVoice.Line.Nod);   // (looked it over: a nod)
                    if (GetsUp)
                    {
                        // standing up takes a while: he's still on his break till he's up
                        Write(0);
                        _step = Step.StandingUp;
                        _stepUntil = Time.time + (Doing == Activity.Chair ? CoglingBody.HopDownSeconds : Doing == Activity.Nap ? CoglingBody.LieUpSeconds : CoglingBody.StandUpSeconds);
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
            // (what a break showed on him, taken off: the hammer, the treasure in his hands)
            if (Doing == Activity.Treasure && Owner) Zdo?.Set(TreasureKey, "");
            if (Doing != Activity.None) Write(0);
            Doing = Activity.None;
            _partner = null;
            _host = false;
            _asked = false;
            _begun = false;
            _guests.Clear();
            _lastTalker = null;
            _watched = null;
            _catchingUp = false;
            _chest = null;
            _inspected = null;
            _other = null;
            _it = false;
            _animal = null;
            _ai.Running = false;
            _ai.WalkScale = 1f;
            _ai.RunScale = 1f;
            _booAt = -1f;
            _dancer = null;
            Release();
        }

        private void Release()
        {
            if (_chair != null) Taken.Remove(_chair);
            _chair = null;
        }

        /// <summary>
        /// Watching: his head follows the player (his body: CoglingBody.Look); his body stays put till they're well off to one side
        /// (Watch: turn after), then turns to catch up till he faces them again.
        /// </summary>
        private void WatchTurn()
        {
            Vector3 to = Flat(_face - _ai.transform.position);
            float off = Vector3.Angle(Flat(_ai.transform.forward), to);
            if (off > Plugin.WatchTurnAfter.Value) _catchingUp = true;
            else if (off < 6f) _catchingUp = false;
            if (_catchingUp) _ai.Face(_face);
        }

        /// <summary>The player he watches is still here, near, and not dead.</summary>
        private bool Watching => _watched != null && _watched && !_watched.IsDead() && Vector3.Distance(_watched.transform.position, _ai.transform.position) < 22f;

        private void Write(int what)
        {
            ZDO zdo = Zdo;
            if (zdo == null || !Owner) return;
            if (zdo.GetInt(SlackKey) != what) zdo.Set(SlackKey, what);
        }

        private void WriteSeat(Vector3 at)
        {
            ZDO zdo = Zdo;
            if (zdo != null && Owner) zdo.Set(SeatKey, at);
        }

        private void WriteLook(Vector3 at)
        {
            ZDO zdo = Zdo;
            if (zdo != null && Owner) zdo.Set(LookKey, at);
        }

        // ---- chatting

        /// <summary>Still in this chat with him: a guest, the one who started it with him in it.</summary>
        private bool Here(CoglingSlack s) => s != null && s._ai != null && s.Doing == Activity.Chat;

        /// <summary>
        /// The chat goes on for him: a guest, while the one who started it is still in it with him; the one who started it, while anyone
        /// is still with him (those gone are let go, and the rest spread round again).
        /// </summary>
        private bool InChat()
        {
            if (!_host) return Here(_partner) && _partner._host && _partner._guests.Contains(this);
            int before = _guests.Count;
            _guests.RemoveAll(g => !Here(g) || g._partner != this);
            if (_guests.Count == 0) return false;
            if (_guests.Count != before) { _ai.Log($"one left the chat: {_guests.Count + 1} now"); Layout(); }
            return true;
        }

        /// <summary>The middle of his chat's circle.</summary>
        private Vector3 Centre => _host ? _centre : _partner != null ? _partner._centre : _ai.transform.position;

        /// <summary>Everyone in the chat, the one who started it first.</summary>
        private List<CoglingSlack> Members() => new[] { this }.Concat(_guests).ToList();

        /// <summary>
        /// Everyone in the chat spread evenly round its middle, each keeping his side of it (the fewest steps), a little further out the
        /// more there are; those not at their place walk to it, then face the middle again.
        /// </summary>
        private void Layout()
        {
            List<CoglingSlack> all = Members();
            float radius = all.Count <= 2 ? 0.65f : all.Count == 3 ? 0.8f : 0.95f;
            float Angle(CoglingSlack s) { Vector3 d = Flat(s._ai.transform.position - _centre); return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
            all = all.OrderBy(s => Mathf.DeltaAngle(Angle(this), Angle(s)) < 0 ? Mathf.DeltaAngle(Angle(this), Angle(s)) + 360f : Mathf.DeltaAngle(Angle(this), Angle(s))).ToList();
            float start = Angle(this);
            for (int i = 0; i < all.Count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, start + 360f * i / all.Count, 0f) * Vector3.forward;
                all[i].Place(Floor(_centre + dir * radius));
            }
        }

        /// <summary>His place in the chat's circle: off to it if he isn't there.</summary>
        private void Place(Vector3 spot)
        {
            if (Flat2(_ai.transform.position - spot) < 0.3f) return;
            if (_nav.SetTarget(spot)) _step = Step.Going;
        }

        /// <summary>
        /// Now and then, while there's room (four at most), another free Cogling near may come over and join in (more likely a sociable
        /// one, less if he's busy); the chat goes on a while longer for him.
        /// </summary>
        private void MoreJoin()
        {
            _nextJoinLook = Time.time + Random.Range(6f, 10f);
            if (_network == null || _guests.Count + 1 >= MostInAChat) return;
            CoglingSlack best = null;
            float bestDistance = 20f;
            foreach (CoglingSlack s in All)
            {
                if (s == this || s._ai == null || s.Doing != Activity.None || !s.Owner || !s._ai.CanChat) continue;
                Vector3 p = s._ai.transform.position;
                float d = Vector3.Distance(_centre, p);
                if (d >= bestDistance || !Antenna.InArea(_network, p)) continue;
                best = s;
                bestDistance = d;
            }
            if (best == null) return;
            float yes = 0.35f * (0.3f + best.Me.Sociability * 0.7f);
            if (best._ai.IsWorking) yes *= 1f - best.Me.Diligence * 0.6f;
            if (!_forced && Random.value >= yes) return;
            if (!best._ai.JoinChat(this, _centre)) return;
            _guests.Add(best);
            _until = Mathf.Max(_until, Time.time + Random.Range(12f, 20f));
            _ai.Log($"another Cogling joined the chat: {_guests.Count + 1} now");
            Layout();
        }

        /// <summary>
        /// Another Cogling near (this game runs him, in the area, not on a break, not frightened or wary) to chat with, the nearest; with
        /// ask, he may say no (yes more likely the more sociable he is, less likely when busy).
        /// </summary>
        private CoglingSlack Partner(List<Antenna> network, bool ask)
        {
            Vector3 here = _ai.transform.position;
            CoglingSlack best = null;
            float bestDistance = 30f;
            foreach (CoglingSlack s in All)
            {
                if (s == this || s._ai == null || s.Doing != Activity.None || !s.Owner || !s._ai.CanChat) continue;
                Vector3 p = s._ai.transform.position;
                float d = Vector3.Distance(here, p);
                if (d >= bestDistance || !Antenna.InArea(network, p)) continue;
                best = s;
                bestDistance = d;
            }
            if (best == null || !ask) return best;
            float yes = 0.3f + best.Me.Sociability * 0.7f;
            if (best._ai.IsWorking) yes *= 1f - best.Me.Diligence * 0.6f;
            if (Random.value < yes) return best;
            _ai.Log("asked another Cogling for a chat: no");
            return null;
        }

        /// <summary>Asked for a chat by another (his brain has agreed): off to his side of the meeting spot.</summary>
        internal bool Join(CoglingSlack host, Vector3 spot)
        {
            Stop();
            if (_job.Carrying) _job.Drop(); else _job.Abandon();
            Doing = Activity.Chat;
            _partner = host;
            _host = false;
            _face = host._centre;
            _until = 0f;
            if (_nav.SetTarget(spot)) _step = Step.Going;
            else { _step = Step.Facing; _stepUntil = Time.time + 0.8f; }
            _ai.Log($"off for a chat ({Me})");
            return true;
        }

        /// <summary>
        /// Whose turn it is to talk, among those in their place (the one who started it first; then the more sociable more often, and
        /// seldom the same one twice running), and which way; the others listen, look at him, and one of them now and then nods, shakes
        /// his head or laughs. After a question, whoever talks next answers it first.
        /// </summary>
        private void NextTurn()
        {
            List<CoglingSlack> here = Members().Where(m => m == this || m._step == Step.Doing).ToList();
            if (here.Count < 2) { _turnUntil = Time.time + 0.5f; return; }
            CoglingSlack talker;
            if (_lastTalker == null) talker = this;   // (the one who asked starts)
            else
            {
                var weights = here.Select(m => (m, w: (0.4f + m.Me.Sociability) * (m == _lastTalker ? (here.Count > 2 ? 0.1f : 0.35f) : 1f))).ToList();
                float pick = Random.value * weights.Sum(x => x.w);
                talker = weights[weights.Count - 1].m;
                foreach (var (m, w) in weights) { if (pick < w) { talker = m; break; } pick -= w; }
            }
            bool answer = _asked && talker != _lastTalker;
            _lastTalker = talker;
            // talking mostly, now and then asking; a turn is the clip once through (they don't loop), a talk now and then twice
            bool ask = Random.value < 0.25f;
            int[] from = ask ? AskPoses : TalkPoses;
            int clip = from[Random.Range(0, from.Length)];
            _asked = ask;
            List<CoglingSlack> listeners = here.Where(m => m != talker).ToList();
            foreach (CoglingSlack m in here)
            {
                m.Write(m == talker ? clip : PoseListen);
                // (the listeners at the talker; the talker at one of them, as if speaking to him)
                m.WriteLook(m == talker ? listeners[Random.Range(0, listeners.Count)]._ai.transform.position : talker._ai.transform.position);
            }
            if (answer) talker._ai.Say(Random.value < 0.65f ? CoglingVoice.Line.Nod : CoglingVoice.Line.Shake);
            else
            {
                // one of the listeners nods along, shakes his head, or laughs, now and then
                CoglingAI listener = listeners[Random.Range(0, listeners.Count)]._ai;
                float r = Random.value;
                if (r < 0.25f) listener.Say(CoglingVoice.Line.Nod);
                else if (r < 0.35f) listener.Say(CoglingVoice.Line.Shake);
                else if (r < 0.5f) listener.Say(CoglingVoice.Line.Laugh);
            }
            _turnUntil = Time.time + CoglingBody.ClipLength(TalkClip(clip)) * (ask ? 1f : Random.value < 0.4f ? 2f : 1f) - 0.2f;
        }

        // ---- tag, tagging along, a tame animal, creeping up

        /// <summary>Tag: "it" chases the other; caught, they swap (the new "it" counts a moment first); the one who asked ends it.</summary>
        private bool Tag(float dt)
        {
            if (_other == null || _other._ai == null || _other.Doing != Activity.Tag || _other._other != this) { End(); return false; }
            if (Time.time >= _until && (_host || Time.time >= _until + 60f)) { _ai.Say(CoglingVoice.Line.Laugh); End(); return false; }
            Vector3 me = _ai.transform.position, them = _other._ai.transform.position;
            if (Time.time < _graceUntil) { _ai.Stop(); _ai.Face(them); return true; }   // (counting)
            _ai.RunScale = _it ? 1f : 0.75f;
            if (_it)
            {
                if (Time.time >= _nextAim) { _nextAim = Time.time + 0.4f; _nav.SetTarget(them); }
                _nav.Update(dt);
                if (Vector3.Distance(me, them) < 1.1f)
                {
                    // caught: the other is "it" now
                    _it = false;
                    _other._it = true;
                    _other._graceUntil = Time.time + 1.2f;
                    _nextAim = 0f;
                    _ai.Say(CoglingVoice.Line.Laugh);
                    _other._ai.Say(CoglingVoice.Line.Excited);
                    _ai.Log("tag: caught him");
                }
                return true;
            }
            // running away: a spot a few metres off, away from "it" (a little to one side), in his area
            if (Time.time >= _nextAim || _nav.Update(dt) != CoglingNavigator.Result.Moving)
            {
                _nextAim = Time.time + 1.2f;
                Vector3 away = Flat(me - them);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 spot = me + Quaternion.Euler(0f, Random.Range(-70f, 70f) + (i >= 3 ? 90f * (i - 2) : 0f), 0f) * away * Random.Range(4f, 6f);
                    if (_network != null && !Antenna.InArea(_network, spot)) continue;
                    if (_nav.SetTarget(Floor(spot))) break;
                }
            }
            return true;
        }

        /// <summary>Tagging along: a few steps behind the player, stopping to look when close; not out of his area (he waits at its edge).</summary>
        private bool Follow(float dt)
        {
            if (_watched == null || !_watched || _watched.IsDead() || Vector3.Distance(_watched.transform.position, _ai.transform.position) > 30f) { End(); return false; }
            if (Time.time >= _until) { End(); return false; }
            Vector3 me = _ai.transform.position, them = _watched.transform.position;
            if (_network != null && !Antenna.InArea(_network, them))
            {
                // (out of his area: he waits, looking after them, and gives up after a little while)
                if (_outsideSince < 0f) _outsideSince = Time.time;
                _ai.Stop();
                _ai.Face(them);
                if (Time.time - _outsideSince > 6f) { End(); return false; }
                return true;
            }
            _outsideSince = -1f;
            if (Vector3.Distance(me, them) > 3.5f)
            {
                if (Time.time >= _nextAim) { _nextAim = Time.time + 0.5f; _nav.SetTarget(Floor(them + Flat(me - them) * 2.2f)); }
                _nav.Update(dt);
            }
            else { _ai.Stop(); _ai.Face(them); }
            return true;
        }

        /// <summary>Watching a tame animal: if it wanders off, he goes after it. False: it's gone (the break is over).</summary>
        private bool Animal(float dt)
        {
            if (_animal == null || !_animal || _animal.IsDead()) { End(); return false; }
            _face = _animal.transform.position;
            if (Vector3.Distance(_animal.transform.position, _animalWas) > 3f)
            {
                _animalWas = _animal.transform.position;
                if (_nav.SetTarget(Floor(_animalWas + Flat(_ai.transform.position - _animalWas) * 1.8f))) { _step = Step.Going; }
            }
            return true;
        }

        /// <summary>Creeping up behind another Cogling, slowly; close behind him, he jumps (and the sneak laughs). Given up after a while.</summary>
        private bool Sneak(float dt)
        {
            if (_other == null || _other._ai == null || Time.time >= _until || !_other.Startlable) { End(); return false; }   // (sat down, lay down...)
            Transform victim = _other._ai.transform;
            Vector3 behind = victim.position - Flat(victim.forward) * 0.7f;
            // there (the navigator stops within 0.6 m of the spot: near enough): a moment's pause behind him, then boo
            bool there = Utils.DistanceXZ(_ai.transform.position, behind) < 0.7f || Utils.DistanceXZ(_ai.transform.position, victim.position) < 1.0f;
            if (there && _booAt < 0f) _booAt = Time.time + Random.Range(0.4f, 0.8f);
            if (_booAt >= 0f)
            {
                _ai.Stop();
                _ai.Face(victim.position);
                if (Time.time < _booAt) return true;
                _other._ai.Startled(_ai.transform.position);
                _ai.Face(victim.position);
                _ai.Say(CoglingVoice.Line.Laugh);
                _ai.Log("crept up on another Cogling: boo");
                End();
                return false;
            }
            if (Time.time >= _nextAim) { _nextAim = Time.time + 0.5f; _nav.SetTarget(Floor(behind)); }
            _nav.Update(dt);
            return true;
        }

        /// <summary>
        /// Treasure, step by step: at the chest a moment (taking it out: shown in his hands), off a few steps to admire it, back to the chest
        /// a moment (putting it back: gone from his hands). The chest gone or emptied of it: it's put away at once.
        /// </summary>
        private bool Treasure(float dt)
        {
            if (_chest == null || !_chest) { Done(); return false; }
            Vector3 me = _ai.transform.position, chest = _chest.transform.position;
            switch (_phase)
            {
                case 0:   // taking it out
                    _ai.Stop();
                    _ai.Face(chest);
                    _phase = 1;
                    _phaseUntil = Time.time + 1.2f;
                    return true;
                case 1:
                    _ai.Stop();
                    _ai.Face(chest);
                    if (Time.time < _phaseUntil) return true;
                    if (Owner) Zdo?.Set(TreasureKey, _treasure);
                    _ai.Say(CoglingVoice.Line.Excited);
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 spot = me + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(3f, 5f);
                        if (_network != null && !Antenna.InArea(_network, spot)) continue;
                        if (_nav.SetTarget(Floor(spot))) break;
                    }
                    _phase = 2;
                    return true;
                case 2:   // off to admire it
                    if (_nav.Update(dt) == CoglingNavigator.Result.Moving) return true;
                    _ai.Stop();
                    _phase = 3;
                    _phaseUntil = Time.time + Random.Range(6f, 10f);
                    return true;
                case 3:   // admiring it (his head bowed to it: CoglingBody)
                    _ai.Stop();
                    if (Time.time < _phaseUntil) return true;
                    _nav.SetTarget(Floor(chest + Flat(me - chest) * 1.0f));
                    _phase = 4;
                    return true;
                case 4:   // back to the chest
                    if (_nav.Update(dt) == CoglingNavigator.Result.Moving) return true;
                    _ai.Stop();
                    _phase = 5;
                    _phaseUntil = Time.time + 1.0f;
                    return true;
                default:  // putting it back
                    _ai.Stop();
                    _ai.Face(chest);
                    if (Time.time < _phaseUntil) return true;
                    Done();
                    return false;
            }
            void Done() { if (Owner) Zdo?.Set(TreasureKey, ""); End(); }
        }

        private static readonly HashSet<string> ShinyNames = new HashSet<string>
        {
            "Ruby", "Amber", "AmberPearl", "SilverNecklace", "Coins", "Thunderstone", "Crystal", "GemstoneRed", "GemstoneBlue", "GemstoneGreen",
        };

        /// <summary>A chest of his area within 25 m with something shiny in it (a gem, a trophy, coins...), and which (picked at random).</summary>
        private bool Shiny(List<Antenna> network, out Container chest, out string what)
        {
            chest = null;
            what = null;
            Vector3 here = _ai.transform.position;
            var found = new List<(Container, string)>();
            foreach (Container c in Chests.Usable(network))
            {
                if (c == null || Vector3.Distance(here, c.transform.position) > 25f || c.GetInventory() == null) continue;
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                {
                    if (item?.m_dropPrefab == null) continue;
                    if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy || ShinyNames.Contains(item.m_dropPrefab.name))
                        found.Add((c, item.m_dropPrefab.name));
                }
            }
            if (found.Count == 0) return false;
            (chest, what) = found[Random.Range(0, found.Count)];
            return true;
        }

        /// <summary>Under cover: a roof, or a tree or rock overhead (the game's own check; the user likes them under trees too).</summary>
        private static bool UnderRoof(Vector3 floor) => Cover.IsUnderRoof(floor + Vector3.up * 0.5f);

        /// <summary>The nearest spot under a roof in his area (within 18 m, he can walk to), or null.</summary>
        private Vector3? Dry(List<Antenna> network)
        {
            Vector3 here = _ai.transform.position;
            Vector3? best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < 40; i++)
            {
                Vector3 p = here + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1f, 18f);
                if (!Antenna.InArea(network, p)) continue;
                Vector3 floor = Floor(p + Vector3.up * 1.5f);
                if (!UnderRoof(floor)) continue;
                float d = Vector3.Distance(here, floor);
                if (d < bestDistance) { best = floor; bestDistance = d; }
            }
            return best;
        }

        // ---- a player's new building (Piece.SetCreator: on the game of the player who placed it)

        private static readonly List<(Piece piece, float at)> Placed = new List<(Piece, float)>();
        private static readonly List<Vector3> Seen = new List<Vector3>();
        private const float FreshFor = 300f;   // (five minutes)

        internal static void Built(Piece p)
        {
            if (p == null || p.GetComponent<Character>() != null) return;   // (not a Cogling)
            Placed.RemoveAll(x => x.piece == null || Time.time - x.at > FreshFor);
            Placed.Add((p, Time.time));
        }

        /// <summary>A piece a player built in his area in the last five minutes, not near one a Cogling has looked at already, or null.</summary>
        private Piece Fresh(List<Antenna> network)
        {
            Seen.RemoveAll(s => Placed.All(x => x.piece == null || Vector3.Distance(x.piece.transform.position, s) > 4f));
            foreach (var (piece, at) in Placed.AsEnumerable().Reverse())
            {
                if (piece == null || Time.time - at > FreshFor || !Antenna.InArea(network, piece.transform.position)) continue;
                if (Seen.Any(s => Vector3.Distance(s, piece.transform.position) < 4f)) continue;
                return piece;
            }
            return null;
        }

        /// <summary>Asked to play tag by another: he runs off first (the other is "it", and counts a moment).</summary>
        internal bool JoinTag(CoglingSlack host)
        {
            Stop();
            if (_job.Carrying) _job.Drop(); else _job.Abandon();
            Doing = Activity.Tag;
            _other = host;
            _host = false;
            _it = false;
            _network = host._network;
            _face = host._ai.transform.position;
            _until = 0f;
            _step = Step.Facing;
            _stepUntil = Time.time + 0.5f;
            host._graceUntil = Time.time + 2f;   // (the one who's "it" counts first)
            _ai.Log($"playing tag ({Me})");
            return true;
        }

        /// <summary>A tame animal in his area within 25 m (a wolf, a boar, a lox...), the nearest, or null.</summary>
        private Character TameNear(List<Antenna> network)
        {
            Vector3 here = _ai.transform.position;
            Character best = null;
            float bestDistance = 25f;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsDead() || !c.IsTamed() || CoglingStance.IsCogling(c)) continue;
                float d = Vector3.Distance(here, c.transform.position);
                if (d < bestDistance && Antenna.InArea(network, c.transform.position)) { best = c; bestDistance = d; }
            }
            return best;
        }

        /// <summary>Standing about (working, or a break on his feet: staring, looking around, bored): one to creep up on. Not sitting,
        /// lying, chatting or playing.</summary>
        internal bool Startlable => Doing == Activity.None || Doing == Activity.Stare || Doing == Activity.LookAround || Doing == Activity.Bored;

        /// <summary>Another Cogling near to creep up on (this game runs him, in the area, not frightened), the nearest, or null.</summary>
        private CoglingSlack Victim(List<Antenna> network)
        {
            Vector3 here = _ai.transform.position;
            CoglingSlack best = null;
            float bestDistance = 20f;
            foreach (CoglingSlack s in All)
            {
                if (s == this || s._ai == null || !s.Owner || !s.Startlable || !s._ai.CanBeStartled) continue;
                float d = Vector3.Distance(here, s._ai.transform.position);
                if (d < bestDistance && Antenna.InArea(network, s._ai.transform.position)) { best = s; bestDistance = d; }
            }
            return best;
        }

        // ---- a dance's audience

        /// <summary>
        /// Started dancing: the free Coglings near (this game runs them, in the area) may come over: a playful one to dance along beside
        /// him, a sociable one to clap and cheer in front of him. Three at most.
        /// </summary>
        private void Invite()
        {
            if (_network == null || !Plugin.DanceAudience.Value) return;
            Vector3 here = _ai.transform.position;
            int came = 0;
            foreach (CoglingSlack s in All.ToList())
            {
                if (came >= 3) break;
                if (s == this || s._ai == null || s.Doing != Activity.None || !s.Owner || !s._ai.CanChat) continue;
                Vector3 p = s._ai.transform.position;
                if (Vector3.Distance(here, p) > 20f || !Antenna.InArea(_network, p)) continue;
                Traits t = s.Me;
                bool dance = Random.value < 0.1f + t.Playfulness * 0.45f;
                if (!dance && !_forced && Random.value >= 0.1f + t.Sociability * 0.4f) continue;
                if (s._ai.JoinDance(this, dance, came)) came++;
            }
            if (came > 0)
            {
                // (an audience takes a while to get there: the dance goes on longer, so they have one too)
                _until = Mathf.Max(_until, Time.time + Random.Range(25f, 40f) * (0.8f + Me.Playfulness * 0.4f));
                _ai.Log($"{came} came to the dance: dancing till {_until - Time.time:F0} s from now");
            }
        }

        /// <summary>Come to another's dance (his brain has said yes): beside him dancing the same, or in front of him cheering.</summary>
        internal bool JoinDance(CoglingSlack dancer, bool dance, int index)
        {
            Stop();
            if (_job.Carrying) _job.Drop(); else _job.Abandon();
            _network = dancer._network;
            _dancer = dancer;
            Vector3 at = dancer._ai.transform.position, ahead = Flat(dancer._face - at), right = Vector3.Cross(Vector3.up, ahead);
            Vector3 spot;
            if (dance)
            {
                // in a row with him, facing his way: right, left, then further right
                float side = index == 0 ? 1.4f : index == 1 ? -1.4f : 2.8f;
                spot = at + right * side;
                _face = spot + ahead * 5f;
                _dancePose = dancer._dancePose;
                Doing = Activity.Dance;
            }
            else
            {
                // in front of him, a little to one side, facing him
                float angle = (index - 1) * 30f + Random.Range(-10f, 10f);
                spot = at + Quaternion.Euler(0f, angle, 0f) * ahead * 2.6f;
                _face = at;
                Doing = Activity.Cheer;
            }
            spot = Floor(spot);
            _until = 0f;
            if (_nav.SetTarget(spot)) _step = Step.Going;
            else { _step = Step.Facing; _stepUntil = Time.time + 0.8f; }
            _ai.Log(dance ? "off to dance along" : "off to cheer a dance");
            return true;
        }

        /// <summary>Cheering: the next clip (clapping or cheering), once through.</summary>
        private void NextCheer()
        {
            int pose = CheerPoses[Random.Range(0, CheerPoses.Length)];
            Write(pose);
            _nextCheerPose = Time.time + CoglingBody.ClipLength(CheerClip(pose)) * (pose == (int)Activity.Cheer ? 2f : 1f) - 0.15f;
        }

        internal (string key, string a, string b) Describe()
        {
            if (_step == Step.Going) return ("cogling_task_slack_going", "", "");
            switch (Doing)
            {
                case Activity.Stare: return ("cogling_task_slack_stare", "", "");
                case Activity.LookAround: return ("cogling_task_slack_look", "", "");
                case Activity.Nap: return ("cogling_task_slack_nap", "", "");
                case Activity.Tag: return ("cogling_task_slack_tag", "", "");
                case Activity.Treasure: return ("cogling_task_slack_treasure", "", "");
                case Activity.Fiddle: return ("cogling_task_slack_fiddle", "", "");
                case Activity.Shelter: return ("cogling_task_slack_shelter", "", "");
                case Activity.Inspect: return ("cogling_task_slack_inspect", "", "");
                case Activity.Follow: return ("cogling_task_slack_follow", "", "");
                case Activity.Animal: return ("cogling_task_slack_animal", "", "");
                case Activity.Sneak: return ("cogling_task_slack_sneak", "", "");
                case Activity.Exercise: return ("cogling_task_slack_exercise", "", "");
                case Activity.Bored: return ("cogling_task_slack_bored", "", "");
                case Activity.Dance: return ("cogling_task_slack_dance", "", "");
                case Activity.Sit:
                case Activity.Chair: return ("cogling_task_slack_sit", "", "");
                case Activity.Watch: return ("cogling_task_slack_watch", "", "");
                case Activity.Cheer: return ("cogling_task_slack_cheer", "", "");
                case Activity.Chat:
                {
                    CoglingSlack host = _host ? this : _partner;
                    int others = (host?._guests.Count ?? 1);   // (everyone but him: the host's guests, less him, plus the host)
                    if (others > 1) return ("cogling_task_slack_chat_group", others.ToString(), "");
                    CoglingSlack other = _host ? _guests.FirstOrDefault() : _partner;
                    string name = other?.Zdo?.GetString(CoglingTag.NameKey) ?? "";
                    return string.IsNullOrEmpty(name) ? ("cogling_task_slack_chat_any", "", "") : ("cogling_task_slack_chat", name, "");
                }
            }
            return ("cogling_task_resting", "", "");
        }

        internal string Status() => $"slacking {Doing} ({_step}{(_host ? $", started a chat of {_guests.Count + 1}" : _partner != null ? ", asked to chat" : "")}), {Me}";

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

        /// <summary>
        /// A chair, stool or bench in his area within 25 m, free (no player on it, no other Cogling's, not on a ship), the nearest, and
        /// the top of its seat (a chair's attach point is low, at the seat's middle: straight down onto it, the lowest of its colliders'
        /// tops, under the backrest's), or null. Seats too high for him are left.
        /// </summary>
        /// <summary>Why the last chair looked at wasn't taken (for `cogling slack chair`).</summary>
        internal static string ChairWhy = "";

        private Chair FreeChair(List<Antenna> network, out Vector3 seat)
        {
            seat = Vector3.zero;
            ChairWhy = "no chair within 25 m in his area";
            Vector3 here = _ai.transform.position;
            Chair best = null;
            float bestDistance = 25f;
            foreach (Chair c in Object.FindObjectsByType<Chair>(FindObjectsSortMode.None))
            {
                if (c == null || c.m_inShip || c.m_attachPoint == null) continue;
                if (Taken.Contains(c) || c.IsInUse()) { ChairWhy = "the chair is taken"; continue; }
                Vector3 at = c.m_attachPoint.position;
                float d = Vector3.Distance(here, at);
                if (d >= bestDistance || !Antenna.InArea(network, at) || c.GetComponentInParent<Ship>() != null) continue;
                if (!SeatTop(c, out Vector3 top)) continue;
                best = c;
                bestDistance = d;
                seat = top;
            }
            return best;
        }

        private static bool SeatTop(Chair c, out Vector3 top)
        {
            top = Vector3.zero;
            Vector3 at = c.m_attachPoint.position;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * 1.6f, Vector3.down, 2.2f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece")))
            {
                if (hit.collider.GetComponentInParent<Chair>() != c && hit.collider.transform.root != c.transform.root) continue;
                if (found && hit.point.y >= top.y) continue;
                top = hit.point;
                found = true;
            }
            if (!found) { ChairWhy = "found no seat on it"; return false; }
            // (a seat from a little above the floor to about his own height; the floor under it, not the chair itself)
            float floor = at.y - 1f;
            foreach (RaycastHit hit in Physics.RaycastAll(top + Vector3.up * 0.05f, Vector3.down, 3f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain")))
            {
                if (hit.collider.GetComponentInParent<Chair>() == c || hit.collider.transform.root == c.transform.root) continue;
                if (hit.point.y > floor) floor = hit.point.y;
            }
            float height = top.y - floor;
            if (height <= 0.15f || height >= Plugin.Height.Value * 0.9f) { ChairWhy = $"its seat is {height:F2} m up"; return false; }
            return true;
        }

        private static float Flat2(Vector3 v) { v.y = 0f; return v.magnitude; }

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

        internal static void Forget() { All.Clear(); Taken.Clear(); Placed.Clear(); Seen.Clear(); }
    }
}
