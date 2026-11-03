using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Danger (step 8): an enemy near, or a hit, and he runs for cover: a spot he can reach where the enemy can't see him (a building's
    /// wall or a rise of ground between them), under a roof if there is one, away from it. There he cowers (his sad idle, eyes red) until
    /// no enemy has been near for a while, then goes back to what he was doing (anything he carries stays saved on him). If the enemy
    /// sees him again, he looks for other cover. Runs on the Cogling's owner, like the rest of his brain; his fright is saved on him
    /// (Carried.MoodKey), so every player's game shows the eyes and plays the cry.
    /// </summary>
    internal sealed class CoglingDanger
    {
        private const float NoticeRange = 12f, SafeRange = 20f, CalmAfter = 6f, WaryRange = 25f;
        private static readonly int SolidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        private static readonly int SightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "viewblock", "vehicle");

        private readonly CoglingAI _ai;
        private Character _threat;
        private float _hurtAt = -100f, _lastThreatSeen = -100f, _nextLook, _nextCoverCheck;
        private Vector3 _cover;
        private bool _haveCover;

        internal CoglingDanger(CoglingAI ai) => _ai = ai;

        internal bool Afraid { get; private set; }
        internal bool Wary { get; private set; }   // an enemy he can see further off: eyes yellow, the "aware" icon, he keeps working
        private Character _seen;
        internal string ThreatName => (_threat != null ? _threat : _seen) is Character c && c != null ? c.m_name : "";

        internal string Status() =>
            $"afraid {Afraid}, threat {(_threat == null ? "none" : _threat.m_name + (_threat.IsDead() ? " (dead)" : ""))}, last seen {Time.time - _lastThreatSeen:F1} s ago, hurt {Time.time - _hurtAt:F1} s ago, cover {(_haveCover ? _cover.ToString("F1") : "none")}";

        /// <summary>He was hit (by anyone but a player in no-PvP, which can't happen): fright.</summary>
        internal void Hurt(Character attacker)
        {
            _hurtAt = Time.time;
            if (attacker != null && attacker != _ai.Body) _threat = attacker;
        }

        /// <summary>The nearest enemy he can notice (within NoticeRange), or the one that hit him lately.</summary>
        private Character Threat()
        {
            Character me = _ai.Body;
            Character best = null;
            float bestDistance = NoticeRange;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == me || c.IsDead() || !BaseAI.IsEnemy(me, c) || Built(c)) continue;
                float d = Vector3.Distance(c.transform.position, me.transform.position);
                if (d < bestDistance) { best = c; bestDistance = d; }
            }
            if (best == null && _threat != null && !_threat.IsDead() && Time.time - _hurtAt < 4f) best = _threat;
            return best;
        }

        /// <summary>
        /// Each update: whether he's afraid now (and moved by fright: true means the brain does nothing else). Looks for enemies twice a
        /// second; stays afraid until none has been within SafeRange for CalmAfter seconds.
        /// </summary>
        internal bool Update(CoglingNavigator nav)
        {
            if (Time.time >= _nextLook)
            {
                _nextLook = Time.time + 0.5f;
                Character t = Threat();
                if (t != null) { _threat = t; _lastThreatSeen = Time.time; }
                else if (_threat != null && (_threat.IsDead() || Vector3.Distance(_threat.transform.position, _ai.transform.position) > SafeRange)) _threat = null;
                else if (_threat != null) _lastThreatSeen = Time.time;   // (still about, a little further off)
            }
            bool afraid = Time.time - _lastThreatSeen < CalmAfter;
            // wary (only when not afraid): an enemy within WaryRange he has a line of sight to (checked with the look, twice a second)
            if (Time.time >= _nextWaryLook)
            {
                _nextWaryLook = Time.time + 0.5f;
                _seen = afraid ? null : Watched();
                bool wary = !afraid && _seen != null;
                if (wary != Wary)
                {
                    Wary = wary;
                    if (!afraid) _ai.SetMood(wary ? Carried.MoodWary : Carried.MoodCalm);
                    _ai.Log(wary ? $"keeping an eye on {_seen.m_name}" : "nothing to watch any more");
                    if (wary) _ai.Say(CoglingVoice.Line.Nervous);
                }
            }
            _ai.SetIcons(aware: Wary && !afraid, alerted: afraid);
            if (afraid != Afraid)
            {
                Afraid = afraid;
                _ai.SetMood(afraid ? Carried.MoodScared : Carried.MoodCalm);
                _ai.Log(afraid ? $"frightened by {(_threat != null ? _threat.m_name : "something")}: running for cover" : "the danger has passed");
                if (!afraid) _ai.Say(CoglingVoice.Line.Relieved);
                _haveCover = false;
                if (!afraid) { _ai.Running = false; return false; }
            }
            if (!afraid) return false;

            // cover: chosen when frightened, and again when the enemy can see him there (or the spot can't be reached)
            if (!_haveCover || (Time.time > _nextCoverCheck && _threat != null && Sees(_threat, _ai.transform.position)))
            {
                _nextCoverCheck = Time.time + 1.5f;
                if (FindCover(out _cover)) { _haveCover = true; nav.SetTarget(_cover); _ai.Log($"hiding at {_cover:F1}"); }
                else _haveCover = false;
            }
            _ai.Running = true;
            if (_haveCover && Utils.DistanceXZ(_ai.transform.position, _cover) > 0.8f)
            {
                var r = nav.Update(Time.deltaTime);
                if (r == CoglingNavigator.Result.GaveUp) _haveCover = false;
            }
            else
            {
                _ai.Stop();
                if (_threat != null) _ai.Face(_ai.transform.position * 2f - _threat.transform.position);   // (his back to it, cowering)
            }
            return true;
        }

        private float _nextWaryLook;

        /// <summary>The nearest enemy within WaryRange he can see (a clear line between them), or null.</summary>
        private Character Watched()
        {
            Character me = _ai.Body;
            Character best = null;
            float bestDistance = WaryRange;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == me || c.IsDead() || !BaseAI.IsEnemy(me, c) || Built(c)) continue;
                float d = Vector3.Distance(c.transform.position, me.transform.position);
                if (d >= bestDistance || !Sees(c, me.transform.position)) continue;
                best = c;
                bestDistance = d;
            }
            return best;
        }

        private static bool Sees(Character watcher, Vector3 spot)
        {
            Vector3 from = watcher.m_eye != null ? watcher.m_eye.position : watcher.transform.position + Vector3.up * 1.5f;
            Vector3 to = spot + Vector3.up * 0.5f;
            return !Physics.Linecast(from, to, SightMask);
        }

        /// <summary>The best hiding spot within reach: out of the enemy's sight, roofed if possible, away from it, not too far.</summary>
        private bool FindCover(out Vector3 cover)
        {
            cover = default;
            Vector3 here = _ai.transform.position;
            Vector3 danger = _threat != null ? _threat.transform.position : here;
            var spots = new List<(Vector3 at, float score)>();
            for (int ring = 1; ring <= 4; ring++)
            {
                float radius = ring * 4.5f;
                for (int i = 0; i < 12; i++)
                {
                    float angle = (i + ring * 0.5f) / 12f * Mathf.PI * 2f;
                    Vector3 p = here + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (!Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out RaycastHit floor, 10f, SolidMask)) continue;
                    p = floor.point;
                    if (Floating.GetLiquidLevel(p, 1f, LiquidType.Water) > p.y + 0.2f) continue;   // (not in water)
                    if (_threat != null && Sees(_threat, p)) continue;
                    bool roof = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.up, 8f, LayerMask.GetMask("piece"));
                    // (inside his antennas' area if he can: out of it he'd have to find his way back)
                    float score = Vector3.Distance(p, danger) - Vector3.Distance(p, here) * 0.6f + (roof ? 6f : 0f) + (Antenna.Covers(p) ? 8f : 0f);
                    spots.Add((p, score));
                }
            }
            var path = new List<Vector3>();
            foreach (var s in spots.OrderByDescending(s => s.score).Take(6))
            {
                path.Clear();
                if (Pathfinding.instance.GetPath(here, s.at, path, _ai.m_pathAgentType, requireFullPath: true)) { cover = s.at; return true; }
            }
            if (_threat != null)
            {
                // nowhere out of sight he can reach: just away from it
                Vector3 away = here - danger;
                away.y = 0f;
                cover = here + away.normalized * 10f;
                return true;
            }
            return false;
        }

        /// <summary>A creature that is also a built piece (T.W.I.G., training dummies): nothing to be afraid of.</summary>
        internal static bool Built(Character c) => c.GetComponent<Piece>() != null;
    }
}
