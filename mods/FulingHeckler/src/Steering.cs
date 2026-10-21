using System.Collections.Generic;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// How he gets about without the creature AI he was stripped of. He asks the game's pathfinding (the service every creature asks,
    /// no AI needed) for a route and follows its corners, so he goes round walls and through doors. Each step he may only step up a
    /// little, as a creature does, and a wall at his chest stops him: before, he snapped onto whatever was under the next step, so he
    /// ran up walls, over pillars and onto roofs. Where the game has no route yet (it builds them as areas load), he heads straight
    /// for his goal by the same step rules, sidestepping what's in his way.
    /// Spots he appears at are ones with a route to the player first (never a roof or a pillar top), then open ground, then anything.
    /// </summary>
    internal partial class FulingActor
    {
        // A real Fuling's body as the game's routes see it (MonsterAI.m_pathAgentType Humanoid; Pathfinding.AddAgent, private there):
        private const float StepUp = 0.3f;          // agentClimb: the highest ledge he steps onto; anything taller is in his way
        private const float BodyRadius = 0.4f;      // agentRadius: how close he comes to a wall
        private const float KneeHeight = StepUp + 0.05f;   // something just too tall to step onto blocks him here...
        private const float ChestHeight = 0.9f;            // ...and a wall here
        private const float RepathSeconds = 0.5f;   // how often he asks for a fresh route while moving
        private static readonly float[] Sidesteps = { 35f, -35f, 70f, -70f, 105f, -105f };

        private static int _groundMask = -1;
        private static int _terrainLayer = -1;
        private static readonly List<Vector3> _probePath = new List<Vector3>();

        private readonly List<Vector3> _path = new List<Vector3>();
        private bool _frozen;

        /// <summary>"heckler freeze": he holds his pose and his place (for a picture) until unfrozen; the scene's own timers run on.</summary>
        internal void Freeze(bool on)
        {
            _frozen = on;
            if (_animator != null) _animator.speed = on ? 0f : 1f;
        }
        private Vector3 _pathGoal;
        private float _pathAt = -99f;
        private bool _havePath;

        private static int GroundMask
        {
            get
            {
                if (_groundMask == -1)
                {
                    _groundMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");
                    _terrainLayer = LayerMask.NameToLayer("terrain");
                }
                return _groundMask;
            }
        }

        private static bool TryGround(Vector3 p, float referenceY, out Vector3 ground) => TryGround(p, referenceY, out ground, out _);

        /// <summary>The top surface at a spot, near the reference height (a cliff far above or below doesn't count).</summary>
        private static bool TryGround(Vector3 p, float referenceY, out Vector3 ground, out bool onTerrain)
        {
            Vector3 from = new Vector3(p.x, Mathf.Max(referenceY, p.y) + 6f, p.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 40f, GroundMask))
            {
                ground = hit.point;
                onTerrain = hit.collider != null && hit.collider.gameObject.layer == _terrainLayer;
                return Mathf.Abs(ground.y - referenceY) < 12f;
            }
            ground = p;
            onTerrain = false;
            return false;
        }

        /// <summary>Can he walk from here to there, by the game's own routes?</summary>
        private static bool Reachable(Vector3 from, Vector3 to)
        {
            var pf = Pathfinding.instance;
            if (pf == null) return false;
            try { return pf.GetPath(from, to, _probePath, Pathfinding.AgentType.Humanoid, true, false, false); }
            catch { return false; }
        }

        /// <summary>
        /// The first candidate (in the order given) he can walk to the player from; failing that, the first on open ground;
        /// failing that, the first with any dry ground under it.
        /// </summary>
        private static bool PickSpot(Player player, IEnumerable<Vector3> candidates, out Vector3 spot)
        {
            Vector3 origin = player.transform.position;
            Vector3? onTerrain = null, any = null;
            foreach (Vector3 c in candidates)
            {
                if (!TryGround(c, origin.y, out Vector3 ground, out bool terrain) || IsUnderwater(ground)) continue;
                if (Reachable(ground, origin)) { spot = ground; return true; }
                if (terrain && onTerrain == null) onTerrain = ground;
                if (any == null) any = ground;
            }
            spot = onTerrain ?? any ?? Vector3.zero;
            return onTerrain.HasValue || any.HasValue;
        }

        /// <summary>One step of a run or a creep: along the route to goal (or towards where dir points), by the step rules.</summary>
        private void MoveToward(Vector3 dir, float step, float animSpeed = RunSpeed, Vector3? goal = null)
        {
            if (step <= 0f || _frozen) return;
            dir = Flat(dir);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            Vector3 pos = transform.position;
            Vector3 want = RouteDirection(goal ?? pos + dir * 15f) ?? dir;
            if (TryStep(pos, want, step, out Vector3 next)) { Go(want, next, animSpeed); return; }
            foreach (float a in Sidesteps)
            {
                Vector3 side = Quaternion.Euler(0f, a, 0f) * want;
                if (TryStep(pos, side, step, out next)) { Go(side, next, animSpeed); return; }
            }
            FaceDirection(want); // boxed in: he waits (the scene's own timer moves things on)
            SetSpeed(0f);
        }

        private void Go(Vector3 dir, Vector3 next, float animSpeed)
        {
            FaceDirection(dir);
            SetSpeed(animSpeed);
            transform.position = next;
        }

        /// <summary>A step forward, if nothing at his knee or chest is in the way; up a ledge no higher than StepUp, or down any slope.</summary>
        private static bool TryStep(Vector3 pos, Vector3 dir, float step, out Vector3 next)
        {
            next = pos;
            if (Physics.Raycast(pos + Vector3.up * ChestHeight, dir, step + BodyRadius, GroundMask)) return false; // a wall, a pillar
            if (Physics.Raycast(pos + Vector3.up * KneeHeight, dir, step + BodyRadius, GroundMask)) return false;  // a crate, a ledge
            Vector3 to = pos + dir * step;
            if (Physics.Raycast(to + Vector3.up * StepUp, Vector3.down, out RaycastHit hit, StepUp + 8f, GroundMask))
            {
                next = hit.point;
                return true;
            }
            next = new Vector3(to.x, pos.y, to.z); // nothing below within reach: keep his height
            return true;
        }

        /// <summary>Which way the game's route to the goal goes from here, or null when there's no route (yet).</summary>
        private Vector3? RouteDirection(Vector3 goal)
        {
            var pf = Pathfinding.instance;
            if (pf == null) return null;
            Vector3 pos = transform.position;
            if (Time.time - _pathAt > RepathSeconds || Flat(goal - _pathGoal).magnitude > 3f)
            {
                _pathAt = Time.time;
                _pathGoal = goal;
                try { _havePath = pf.GetPath(pos, goal, _path, Pathfinding.AgentType.Humanoid, false, true, false); }
                catch { _havePath = false; }
            }
            if (!_havePath) return null;
            while (_path.Count > 0 && Flat(_path[0] - pos).magnitude < 0.4f) _path.RemoveAt(0);
            if (_path.Count == 0) return null;
            Vector3 d = Flat(_path[0] - pos);
            return d.sqrMagnitude > 0.0001f ? d.normalized : (Vector3?)null;
        }
    }
}
