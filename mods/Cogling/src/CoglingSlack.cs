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
        internal enum Activity { None = 0, Stare = 1, LookAround = 2, Bored = 3, Dance = 4, Sit = 5, Chat = 6, Watch = 14, Chair = 15, Cheer = 16 }
        // (cheering on a dancer: clapping is Activity.Cheer; these two are cheering)
        internal const int PoseCheer2 = 17, PoseCheer3 = 18;
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
        private CoglingSlack _partner;   // in a chat: the other one
        private bool _host, _hostTalking, _asked;
        private float _turnUntil;
        private Chair _chair;            // sitting on a chair: which, and the top of its seat
        private Vector3 _seat, _spot;
        private float _stepGiveUp;
        private Player _watched;         // watching a player: who
        private bool _catchingUp;        // (watching: his body turning to catch up with his head)
        private CoglingSlack _dancer;    // dancing along or cheering: whose dance
        private float _nextCheerPose;
        private List<Antenna> _network;
        private bool _forced;   // (a break from `cogling slack`: a dance brings everyone free near, for testing)
        /// <summary>Chairs a Cogling on this game has taken (two never pick the same).</summary>
        private static readonly HashSet<Chair> Taken = new HashSet<Chair>();

        /// <summary>Every Cogling brain on this game (a chat's partner is one of them).</summary>
        private static readonly List<CoglingSlack> All = new List<CoglingSlack>();

        internal Activity Doing { get; private set; }
        internal bool Seated => (Doing == Activity.Sit || Doing == Activity.Chair) && (_step == Step.Doing || _step == Step.StandingUp);

        internal CoglingSlack(CoglingAI ai, CoglingNavigator nav, CoglingJob job)
        {
            _ai = ai;
            _nav = nav;
            _job = job;
            _nextRoll = Time.time + Random.Range(20f, 40f);
            Write(0);   // (a break saved as his area unloaded: over)
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
            // watching a player, a bit too closely (a curious one more)
            Player near = Player.GetClosestPlayer(_ai.transform.position, 18f);
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
                    _partner = other;
                    _host = true;
                    _turnUntil = 0f;
                    _face = there;
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
                    if (Doing == Activity.Chair) { _step = Step.Stepping; _stepGiveUp = Time.time + 3f; return true; }
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
                        if (!PartnerHere) { End(); return false; }
                        _face = _partner._ai.transform.position;
                    }
                    if (Doing == Activity.Watch) { if (!Watching) { End(); return false; } _face = _watched.transform.position; }
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
                    if (Doing == Activity.Chat && _partner._step < Step.Facing)
                    {
                        // (waiting for the other one to get there; too long, and the chat's off)
                        if (Time.time > _stepUntil + 15f) { _ai.Log("the other one never came"); End(); return false; }
                        return true;
                    }
                    _step = Step.Doing;
                    _until += Time.time;
                    if (Doing == Activity.Chat)
                    {
                        Write(PoseListen);
                        WriteLook(_partner._ai.transform.position);
                    }
                    else if (Doing == Activity.Cheer) NextCheer();
                    else
                    {
                        if (Doing == Activity.Watch && Owner) Zdo?.Set(WatchKey, _watched.GetZDOID());
                        Write(Doing == Activity.Dance ? _dancePose : (int)Doing);
                        if (Doing == Activity.Dance && _dancer == null) Invite();
                    }
                    if (_dancer != null) _until = Time.time + Mathf.Max(5f, _dancer._until - Time.time);   // (till the dancer stops, about)
                    _nextCheer = Time.time + Random.Range(2f, 5f);
                    return true;
                case Step.Doing:
                    _ai.Stop();
                    if (Doing == Activity.Chat)
                    {
                        if (!PartnerHere) { End(); return false; }
                        _face = _partner._ai.transform.position;
                        if (_host && Time.time >= _turnUntil) NextTurn();
                    }
                    if (Doing == Activity.Watch)
                    {
                        if (!Watching) { End(); return false; }
                        _face = _watched.transform.position;
                        WatchTurn();
                    }
                    else if (Doing != Activity.Sit && Doing != Activity.Chair) _ai.Face(_face);   // (seated, he stays as he sat)
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
                    if (Doing == Activity.Sit || Doing == Activity.Chair)
                    {
                        // standing up takes a while: he's still on his break till he's up
                        Write(0);
                        _step = Step.StandingUp;
                        _stepUntil = Time.time + (Doing == Activity.Chair ? CoglingBody.HopDownSeconds : CoglingBody.StandUpSeconds);
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
            if (Doing != Activity.None) Write(0);
            Doing = Activity.None;
            _partner = null;
            _host = false;
            _asked = false;
            _watched = null;
            _catchingUp = false;
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

        /// <summary>The other one is still in this chat with him.</summary>
        private bool PartnerHere => _partner != null && _partner._ai != null && _partner.Doing == Activity.Chat && _partner._partner == this;

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
            _face = host._ai.transform.position;
            _until = 0f;
            if (_nav.SetTarget(spot)) _step = Step.Going;
            else { _step = Step.Facing; _stepUntil = Time.time + 0.8f; }
            _ai.Log($"off for a chat ({Me})");
            return true;
        }

        /// <summary>Whose turn it is to talk (mostly the other's; the more sociable talks more), and which way.</summary>
        private void NextTurn()
        {
            float lean = (Me.Sociability - _partner.Me.Sociability) * 0.3f;
            bool hostTalks = _hostTalking ? Random.value < 0.3f + lean : Random.value < 0.8f + lean;
            if (_turnUntil == 0f) hostTalks = true;   // (the one who asked starts)
            bool answer = _asked;   // (the last turn was a question: whoever talks now starts with a nod or a shake of the head)
            _hostTalking = hostTalks;
            // talking mostly, now and then asking; a turn is the clip once through (they don't loop), a talk now and then twice
            bool ask = Random.value < 0.25f;
            int[] from = ask ? AskPoses : TalkPoses;
            int clip = from[Random.Range(0, from.Length)];
            _asked = ask;
            Write(hostTalks ? clip : PoseListen);
            _partner.Write(hostTalks ? PoseListen : clip);
            CoglingAI talker = hostTalks ? _ai : _partner._ai, listener = hostTalks ? _partner._ai : _ai;
            if (answer) talker.Say(Random.value < 0.65f ? CoglingVoice.Line.Nod : CoglingVoice.Line.Shake);
            else
            {
                // the listener nods along, shakes his head, or laughs, now and then
                float r = Random.value;
                if (r < 0.25f) listener.Say(CoglingVoice.Line.Nod);
                else if (r < 0.35f) listener.Say(CoglingVoice.Line.Shake);
                else if (r < 0.5f) listener.Say(CoglingVoice.Line.Laugh);
            }
            WriteLook(_partner._ai.transform.position);
            _partner.WriteLook(_ai.transform.position);
            _turnUntil = Time.time + CoglingBody.ClipLength(TalkClip(clip)) * (ask ? 1f : Random.value < 0.4f ? 2f : 1f) - 0.2f;
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
            if (came > 0) _ai.Log($"{came} came to the dance");
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
                case Activity.Bored: return ("cogling_task_slack_bored", "", "");
                case Activity.Dance: return ("cogling_task_slack_dance", "", "");
                case Activity.Sit:
                case Activity.Chair: return ("cogling_task_slack_sit", "", "");
                case Activity.Watch: return ("cogling_task_slack_watch", "", "");
                case Activity.Cheer: return ("cogling_task_slack_cheer", "", "");
                case Activity.Chat:
                {
                    string name = _partner?.Zdo?.GetString(CoglingTag.NameKey) ?? "";
                    return string.IsNullOrEmpty(name) ? ("cogling_task_slack_chat_any", "", "") : ("cogling_task_slack_chat", name, "");
                }
            }
            return ("cogling_task_resting", "", "");
        }

        internal string Status() => $"slacking {Doing} ({_step}{(_partner != null ? (_host ? ", asked a chat" : ", asked to chat") : "")}), {Me}";

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

        internal static void Forget() { All.Clear(); Taken.Clear(); }
    }
}
