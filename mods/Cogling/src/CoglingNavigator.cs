using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Getting a Cogling to a spot: the game's pathfinding (around buildings, up stairs and slopes), and what it can't do.
    ///
    /// Doors: a closed door is a wall to the pathfinder, and it only learns of an opened one seconds later. So when there's no whole way
    /// to the spot, he plans through a door: the one he can reach on his side whose other side has a whole way on to the spot (the
    /// shortest walk in all). He walks to just in front of it, opens it, waits for it to swing, walks straight through the doorway (not by
    /// the pathfinder, which may not know yet), and goes on from the other side. A door he opened is closed once he's through and a little
    /// past it, or when he leaves without going through; never with a player standing in it.
    ///
    /// Stuck (under half a metre in three seconds): a step aside and a fresh plan; after a few tries he gives up on the spot.
    /// </summary>
    internal sealed class CoglingNavigator
    {
        internal enum Result { Moving, Arrived, GaveUp }
        private enum Phase { Walking, ToDoor, Opening, Through }

        private const float DoorStep = 1.2f;   // how far in front of / behind a door he stands
        private static readonly int PieceMask = LayerMask.GetMask("piece", "piece_nonsolid");

        private readonly CoglingAI _ai;
        private readonly List<Vector3> _path = new List<Vector3>();
        private Phase _phase;
        private bool _fullPath;
        private float _nextPath, _phaseUntil, _nextDoorLook;
        private Vector3 _lastProgressAt;
        private float _lastProgressTime;
        private int _stuckTries;
        private float _closeSince = -1f;   // when he stopped close to the spot with no way closer (a chest in the way): stuck after a while
        private float _sidestepUntil;
        private Vector3 _sidestepDir;

        private Door _door;                    // the door he's going through, and its two sides
        private Vector3 _doorNear, _doorFar;
        private readonly List<(Door door, bool through)> _opened = new List<(Door, bool)>();

        internal Vector3 Target { get; private set; }

        internal CoglingNavigator(CoglingAI ai) => _ai = ai;

        private Vector3 Here => _ai.transform.position;

        /// <summary>Head for a spot. False if no way there was found at all (not even part way, nor through a door).</summary>
        internal bool SetTarget(Vector3 target)
        {
            Target = target;
            _stuckTries = 0;
            _door = null;
            Progress();
            return Plan();
        }

        /// <summary>A way to the target: whole if there is one; else through a door; else as far as the pathfinder goes.</summary>
        private bool Plan()
        {
            _nextPath = Time.time + 3f;
            _phase = Phase.Walking;
            if (PathTo(Target, whole: true)) return true;
            if (PlanDoor()) return true;
            return PathTo(Target, whole: false);
        }

        private bool PathTo(Vector3 to, bool whole)
        {
            _path.Clear();
            _fullPath = Pathfinding.instance.GetPath(Here, to, _path, _ai.m_pathAgentType, requireFullPath: whole);
            if (!_fullPath) _path.Clear();
            if (!whole && !_fullPath) Pathfinding.instance.GetPath(Here, to, _path, _ai.m_pathAgentType);
            return _path.Count > 0;
        }

        private bool PlanDoor()
        {
            var tried = new HashSet<Door>();
            Door best = null;
            float bestLength = float.MaxValue;
            Vector3 bestNear = default, bestFar = default;
            var scratch = new List<Vector3>();
            foreach (Vector3 around in new[] { Here, Target, (Here + Target) / 2f })
                foreach (Collider c in Physics.OverlapSphere(around, 12f, PieceMask))
                {
                    Door d = c.GetComponentInParent<Door>();
                    if (d == null || !tried.Add(d) || !Usable(d)) continue;
                    Sides(d, out Vector3 near, out Vector3 far);
                    // his side: a whole way there; the other side: a whole way on to the spot
                    scratch.Clear();
                    if (!Pathfinding.instance.GetPath(Here, near, scratch, _ai.m_pathAgentType, requireFullPath: true)) continue;
                    float length = Length(Here, scratch);
                    scratch.Clear();
                    if (!Pathfinding.instance.GetPath(far, Target, scratch, _ai.m_pathAgentType, requireFullPath: true)) continue;
                    length += Length(far, scratch) + DoorStep * 2f;
                    if (length < bestLength) { best = d; bestLength = length; bestNear = near; bestFar = far; }
                }
            if (best == null) return false;
            _door = best;
            _doorNear = bestNear;
            _doorFar = bestFar;
            _phase = Phase.ToDoor;
            PathTo(_doorNear, whole: true);
            _ai.Log($"going through the door at {best.transform.position:F1}");
            return true;
        }

        private static float Length(Vector3 from, List<Vector3> path)
        {
            float length = 0f;
            foreach (Vector3 p in path) { length += Vector3.Distance(from, p); from = p; }
            return length;
        }

        /// <summary>The spots just in front of and behind a door, on the floor (the door's middle is about a metre up): near is his side.</summary>
        private void Sides(Door d, out Vector3 near, out Vector3 far)
        {
            Vector3 middle = d.transform.position + Vector3.down * 1f;
            Vector3 normal = d.transform.forward;
            normal.y = 0f;
            normal.Normalize();
            float side = Mathf.Sign(Vector3.Dot(Here - d.transform.position, normal));
            if (side == 0f) side = 1f;
            near = middle + normal * side * DoorStep;
            far = middle - normal * side * DoorStep;
        }

        internal Result Update(float dt)
        {
            if (_phase == Phase.Walking && Utils.DistanceXZ(Here, Target) < 0.6f && Mathf.Abs(Here.y - Target.y) < 1.5f) { _ai.Stop(); return Result.Arrived; }
            if (Time.time < _sidestepUntil) { _ai.Walk(_sidestepDir); return Result.Moving; }

            switch (_phase)
            {
                case Phase.ToDoor:
                    if (Utils.DistanceXZ(Here, _doorNear) < 0.5f || (_path.Count == 0 && Utils.DistanceXZ(Here, _doorNear) < 1.2f))
                    {
                        _ai.Stop();
                        if (!IsOpen(_door) && !TryOpen(_door)) { _door = null; return Stuck("the door won't open"); }
                        _phase = Phase.Opening;
                        _phaseUntil = Time.time + 0.9f;   // (it swings)
                        return Result.Moving;
                    }
                    return Follow(_doorNear);
                case Phase.Opening:
                    _ai.Stop();
                    if (Time.time >= _phaseUntil) { _phase = Phase.Through; _phaseUntil = Time.time + 5f; Progress(); }
                    return Result.Moving;
                case Phase.Through:
                    // straight through the doorway: the pathfinder may not know it's open yet
                    if (Utils.DistanceXZ(Here, _doorFar) < 0.5f)
                    {
                        MarkThrough(_door);
                        _door = null;
                        _phase = Phase.Walking;
                        PathTo(Target, whole: false);
                        _nextPath = Time.time + 2f;
                        return Result.Moving;
                    }
                    if (Time.time > _phaseUntil) { _phase = Phase.Walking; return Stuck("couldn't get through the doorway"); }
                    Vector3 dir = _doorFar - Here;
                    dir.y = 0f;
                    _ai.Walk(dir.normalized);
                    return CheckProgress();
                default:
                    if (Time.time > _nextPath || _path.Count == 0) Plan();
                    if (_phase != Phase.Walking) return Result.Moving;   // (the plan found a door)
                    return Follow(Target);
            }
        }

        /// <summary>Along the path toward a point.</summary>
        private Result Follow(Vector3 to)
        {
            while (_path.Count > 0 && Utils.DistanceXZ(_path[0], Here) < 0.4f) _path.RemoveAt(0);
            if (_path.Count == 0)
            {
                if (Utils.DistanceXZ(Here, to) < 1.2f)
                {
                    // (close, but no closer: once stood there a moment it counts as stuck, so he steps aside, tries again, and in
                    // the end gives up, which every job handles; before, he stood there for good)
                    _ai.Stop();
                    if (_closeSince < 0f) _closeSince = Time.time;
                    else if (Time.time - _closeSince > 1.5f) { _closeSince = -1f; return Stuck("can't get any closer"); }
                    return Result.Moving;
                }
                if (Time.time > _nextPath) { if (_phase == Phase.ToDoor) PathTo(_doorNear, whole: false); else Plan(); _nextPath = Time.time + 2f; }
                if (_path.Count == 0) return Stuck("no way on");
            }
            _closeSince = -1f;
            Vector3 dir = _path[0] - Here;
            dir.y = 0f;
            if (Time.time > _nextDoorLook) { _nextDoorLook = Time.time + 0.25f; if (DoorAhead(dir)) return Result.Moving; }
            _ai.Walk(dir.normalized);
            return CheckProgress();
        }

        /// <summary>
        /// A closed door right in his way (a ray at his chest along where he walks): the door routine from here (open, through). The
        /// pathfinder can think a door is open that has since been shut (it learns of doors late): this catches it.
        /// </summary>
        private bool DoorAhead(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return false;
            if (!Physics.Raycast(Here + Vector3.up * 0.5f, dir.normalized, out RaycastHit hit, 1.4f, PieceMask)) return false;
            Door d = hit.collider.GetComponentInParent<Door>();
            if (d == null || IsOpen(d) || !Usable(d)) return false;
            _door = d;
            Sides(d, out _doorNear, out _doorFar);
            _ai.Stop();
            if (!TryOpen(d)) { _door = null; return false; }
            _phase = Phase.Opening;
            _phaseUntil = Time.time + 0.9f;
            _ai.Log($"a closed door in the way at {d.transform.position:F1}");
            return true;
        }

        private void Progress()
        {
            _lastProgressAt = Here;
            _lastProgressTime = Time.time;
        }

        private Result CheckProgress()
        {
            if (Vector3.Distance(Here, _lastProgressAt) > 0.5f) Progress();
            else if (Time.time - _lastProgressTime > 3f) return Stuck("no progress");
            return Result.Moving;
        }

        private Result Stuck(string why)
        {
            _stuckTries++;
            Progress();
            _ai.Log($"stuck ({why}), try {_stuckTries}");
            if (_stuckTries > 4) { _ai.Stop(); CloseDoors(force: true); return Result.GaveUp; }
            // a step aside, then a fresh plan
            Vector3 toward = Target - Here;
            toward.y = 0f;
            _sidestepDir = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * toward.normalized;
            _sidestepUntil = Time.time + 0.8f;
            _phase = Phase.Walking;
            _door = null;
            _nextPath = _sidestepUntil;
            return Result.Moving;
        }

        // ---- doors

        private static bool IsOpen(Door d) => d != null && d.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_state) != 0;

        /// <summary>A door a Cogling may use: no key doors, none that never close, none a ward keeps from this game's player.</summary>
        private static bool Usable(Door d)
        {
            ZNetView view = d.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (d.m_keyItem != null || d.m_canNotBeClosed) return false;
            return !d.m_checkGuardStone || PrivateArea.CheckAccess(d.transform.position, 0f, flash: false);
        }

        /// <summary>Open a door as a player would (its own UseDoor call), swinging away from him.</summary>
        private bool TryOpen(Door door)
        {
            if (door == null || !Usable(door)) return false;
            if (IsOpen(door)) return true;
            Vector3 from = (Here - door.transform.position).normalized;
            door.GetComponent<ZNetView>().InvokeRPC("UseDoor", Vector3.Dot(door.transform.forward, from) < 0f);
            if (!_opened.Any(o => o.door == door)) _opened.Add((door, false));
            _ai.Log($"opened the door at {door.transform.position:F1}");
            return true;
        }

        private void MarkThrough(Door door)
        {
            int i = _opened.FindIndex(o => o.door == door);
            if (i >= 0) _opened[i] = (door, true);
        }

        /// <summary>
        /// Doors he opened, closed again: once he's through and 2 m past (or 8 m away without going through), nobody standing in them;
        /// all of them when forced (he gives up or goes).
        /// </summary>
        internal void CloseDoors(bool force)
        {
            for (int i = _opened.Count - 1; i >= 0; i--)
            {
                (Door door, bool through) = _opened[i];
                if (door == null) { _opened.RemoveAt(i); continue; }
                if (door == _door && !force) continue;   // (the one he's on his way through)
                float distance = Vector3.Distance(door.transform.position, Here);
                if (!force && distance < (through ? 2f : 8f)) continue;
                if (Player.GetAllPlayers().Any(p => Vector3.Distance(p.transform.position, door.transform.position) < 1.8f)) { if (force) _opened.RemoveAt(i); continue; }
                ZNetView view = door.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && IsOpen(door)) view.InvokeRPC("UseDoor", false);
                _opened.RemoveAt(i);
                _ai.Log($"closed the door at {door.transform.position:F1}");
            }
        }
    }
}
