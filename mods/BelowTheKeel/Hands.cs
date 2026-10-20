using System.Collections.Generic;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// For "keel hands" (pictures for the mod's page): a copy of the player model where the player stands at the ship's side (riding the
    /// ship: it pitches in a storm), leaning on
    /// the gunwale with both hands gripping it, fingers curled over the rail, head tipped to look over the side. The player's own
    /// character is hidden meanwhile, so a camera can stand in the copy's head and see the hands from the eyes. Only on this screen,
    /// gone after a minute (and the player shown again).
    /// </summary>
    internal sealed class Hands : MonoBehaviour
    {
        private const float Seconds = 60f;
        private const float HandApart = 0.26f;     // each hand from the middle, along the rail
        private const float Lean = 6f;             // the upper body over the rail (degrees), on top of the whole body's tilt
        private const float BodyTilt = 0f;         // the whole body tipped toward the rail, from the feet (more and the head
                                                   // goes past the rail: the hands fall behind the eyes)
        private const float StandBack = 0.42f;     // the feet this far back from the rail: close enough for the hands to reach it
        private const float RailBelowFeet = 1.05f; // crouched, the rail at chest height: the feet this far below its top (a longship's
                                                   // rail is only thigh-high; the legs go into the deck, which hides them from the eyes)
        private const float HeadDown = 22f;

        private static Hands _current;
        internal static bool Markers;   // "keel hands marks": small cubes where the hands should grip

        private Transform _holder;
        private Transform _spine, _neck, _head;
        private Vector3 _railLeft, _railRight;      // the hands' places on the rail (in the ship's own space)
        private Vector3 _outLocal;                 // from the player out over the side (in the ship's own space)
        private Vector3 _out;                      // the same, in the world, this frame
        private readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private float _age;

        private sealed class Arm
        {
            public Transform Upper, Fore, Hand;
            public List<Transform[]> Fingers = new List<Transform[]>();
            public Transform[] Thumb;
            public float Side;   // -1 left, +1 right
        }

        private readonly List<Arm> _arms = new List<Arm>();

        /// <summary>Sets it up where the player stands, facing the way they face (toward the rail). A message for the console.</summary>
        internal static string Show(Player player, Ship ship, float raise = 0f, float further = 0f)
        {
            if (_current != null) Destroy(_current.gameObject);

            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            if (!FindRail(player.transform.position, fwd, out float railY, out float railDist))
                return "No gunwale found in front of you: stand at the ship's side, facing over it.";


            var go = new GameObject("BelowTheKeel_Hands");
            // Stand at a set distance from the rail, wherever the player is: the arms can reach it from there.
            Vector3 feet = player.transform.position + fwd * (railDist - StandBack);
            feet.y = railY - RailBelowFeet;
            go.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(fwd));
            go.transform.SetParent(ship.transform, true);   // (no collider under it: the ship's physics never sees it)
            var hands = go.AddComponent<Hands>();
            Visuals.Track(go, Seconds + 1f);
            string error = hands.Build(player, ship.transform, fwd, railY + raise, StandBack + further);   // (up/out: the hands only)
            if (error != null) { Destroy(go); return error; }
            _current = hands;
            hands.LateUpdate();
            // The eyes from the player (who stands still on the deck, and so rides the ship with the copy): a camera that follows them.
            Vector3 eye = player.transform.InverseTransformPoint(hands.Eye());
            return $"Hands on the rail for a minute. Camera at the eyes: view here {eye.x:0.00} {eye.y:0.00} {eye.z:0.00} 0 <pitch>";
        }

        /// <summary>The gunwale's top in front of the player: the first thing well above the deck that rays straight down hit (where the
        /// rail starts: an inner rib or the gunwale itself), then the highest point within a hand's breadth beyond it, no higher than a
        /// rail can be (not the rigging or the shields).</summary>
        private static bool FindRail(Vector3 feet, Vector3 fwd, out float top, out float dist)
        {
            top = float.NegativeInfinity;
            dist = -1f;
            for (float d = 0.2f; d <= 1.6f && dist < 0f; d += 0.03f)
                if (HighestAt(feet, fwd, d, feet.y + 0.3f, feet.y + 1.2f) > float.NegativeInfinity) dist = d;
            if (dist < 0f) return false;
            for (float d = dist; d <= dist + 0.35f; d += 0.03f)
                top = Mathf.Max(top, HighestAt(feet, fwd, d, feet.y + 0.3f, feet.y + 1.2f));
            return true;
        }

        private static float HighestAt(Vector3 feet, Vector3 fwd, float d, float min, float max)
        {
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(feet + fwd * d + Vector3.up * 2.5f, Vector3.down, 3.5f))
            {
                if (hit.collider.GetComponentInParent<Player>() != null) continue;
                if (hit.point.y > min && hit.point.y < max) best = Mathf.Max(best, hit.point.y);
            }
            return best;
        }

        private string Build(Player player, Transform ship, Vector3 fwd, float railY, float railDist)
        {
            _out = fwd;
            _outLocal = ship.InverseTransformDirection(fwd);
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Vector3 centre = transform.position + fwd * (railDist + 0.04f);
            _railLeft = ship.InverseTransformPoint(new Vector3(centre.x, railY, centre.z) - side * HandApart);
            _railRight = ship.InverseTransformPoint(new Vector3(centre.x, railY, centre.z) + side * HandApart);

            GameObject look = Visuals.CloneLook("Player", transform);
            if (look == null) return "Couldn't copy the player model.";
            foreach (var anim in look.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
            foreach (var r in look.GetComponentsInChildren<Renderer>(true))
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) r.enabled = false;

            // The model's own forward, from heel to toe (the draugr's faced backwards to its prefab): turn it inside a holder to face the rail.
            _holder = new GameObject("Holder").transform;
            _holder.SetParent(transform, false);
            look.transform.SetParent(_holder, false);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.identity;
            Transform foot = Find(look.transform, "LeftFoot"), toe = Find(look.transform, "LeftToeBase");
            if (foot != null && toe != null)
            {
                Vector3 toes = toe.position - foot.position;
                toes.y = 0f;
                if (toes.sqrMagnitude > 1e-6f)
                    look.transform.localRotation = Quaternion.FromToRotation(_holder.InverseTransformDirection(toes.normalized), Vector3.forward);
            }

            Transform B(string n) { Transform t = Find(look.transform, n); if (t != null) _rest[t] = t.localRotation; return t; }
            _spine = B("Spine1");
            B("Spine2");
            _neck = B("Neck");
            _head = B("Head");
            foreach (string s in new[] { "Left", "Right" })
            {
                var arm = new Arm { Upper = B(s + "Arm"), Fore = B(s + "ForeArm"), Hand = B(s + "Hand"), Side = s == "Left" ? -1f : 1f };
                if (arm.Upper == null || arm.Fore == null || arm.Hand == null) return "The player model is missing arm bones.";
                foreach (string f in new[] { "Index", "Middle", "Ring", "Pinky" })
                {
                    var chain = new[] { B($"{s}Hand{f}1"), B($"{s}Hand{f}2"), B($"{s}Hand{f}3") };
                    if (chain[0] != null && chain[1] != null && chain[2] != null) arm.Fingers.Add(chain);
                }
                arm.Thumb = new[] { B($"{s}HandThumb1"), B($"{s}HandThumb2"), B($"{s}HandThumb3") };
                _arms.Add(arm);
            }
            if (_spine == null || _head == null) return "The player model is missing spine or head bones.";

            if (Markers)
                foreach (Vector3 local in new[] { _railLeft, _railRight })
                {
                    GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(m.GetComponent<Collider>());
                    m.transform.SetParent(ship, false);
                    m.transform.localPosition = local;
                    m.transform.localScale = Vector3.one * 0.06f;
                    m.GetComponent<Renderer>().sharedMaterial = Look.Rope();
                    m.transform.SetParent(transform, true);
                }

            // Hide the player's own character while the copy stands in its place.
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; _hidden.Add(r); }
            return null;
        }

        /// <summary>Between the eyes, a little in front of the head bone.</summary>
        internal Vector3 Eye() => _neck.position + Vector3.up * 0.17f + _out * 0.08f;   // (from the neck: the head is shrunk away)

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (_age > Seconds) { Destroy(gameObject); return; }
            foreach (var kv in _rest) kv.Key.localRotation = kv.Value;   // the animator is off: start from rest every frame

            Transform ship = transform.parent;
            if (ship == null) { Destroy(gameObject); return; }
            _out = ship.TransformDirection(_outLocal);
            Vector3 side = Vector3.Cross(ship.up, _out).normalized;
            _holder.localRotation = Quaternion.Euler(BodyTilt, 0f, 0f);   // (the holder faces the rail: +x tips it forward)
            _spine.rotation = Quaternion.AngleAxis(Lean, side) * _spine.rotation;
            _head.rotation = Quaternion.AngleAxis(HeadDown, side) * _head.rotation;
            _neck.localScale = Vector3.one * 0.001f;   // no head: the camera is the head (the eye is worked out before this, in Show)

            foreach (Arm arm in _arms)
            {
                Vector3 target = ship.TransformPoint(arm.Side < 0f ? _railLeft : _railRight);
                // The hand bone sits at the wrist: the wrist just on the near edge of the rail top and a little above it, so the palm lies
                // on the top and the fingers reach over the far edge.
                Vector3 wrist = target - _out * 0.02f + Vector3.up * 0.07f;
                SolveArm(arm, wrist, side);

                // The hand lies along the rail top, fingers toward the sea, palm down...
                Point(arm.Hand, arm.Fingers.Count > 1 ? arm.Fingers[1][0] : null, _out - Vector3.up * 0.35f);
                PalmDown(arm);
                // ...each finger curling over it and down the far side; the thumb tucked under the near side.
                foreach (Transform[] f in arm.Fingers)
                {
                    Point(f[0], f[1], _out - Vector3.up * 0.25f);
                    Point(f[1], f[2], _out * 0.3f - Vector3.up);
                    Point(f[2], f[2].childCount > 0 ? f[2].GetChild(0) : null, -_out * 0.4f - Vector3.up);
                }
                if (arm.Thumb[0] != null && arm.Thumb[1] != null && arm.Thumb[2] != null)
                {
                    Point(arm.Thumb[0], arm.Thumb[1], -side * arm.Side * 0.6f + _out * 0.3f - Vector3.up * 0.6f);
                    Point(arm.Thumb[1], arm.Thumb[2], _out * 0.2f - Vector3.up);
                }
            }
        }

        /// <summary>
        /// Turns the hand about its own length so the palm faces down. Aiming a bone sets where it points, not how it's turned; the
        /// knuckles (index to little finger) give the palm's facing: across them and along the hand, mirrored for the left hand.
        /// </summary>
        private static void PalmDown(Arm arm)
        {
            if (arm.Fingers.Count < 4) return;
            Vector3 along = (arm.Fingers[1][0].position - arm.Hand.position).normalized;
            Vector3 across = arm.Fingers[3][0].position - arm.Fingers[0][0].position;   // index to little finger
            Vector3 palm = Vector3.Cross(along, across).normalized * -arm.Side;
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, along);
            if (palm.sqrMagnitude < 1e-6f || down.sqrMagnitude < 1e-6f) return;
            float angle = Vector3.SignedAngle(palm, down.normalized, along);
            arm.Hand.rotation = Quaternion.AngleAxis(angle, along) * arm.Hand.rotation;
        }

        /// <summary>Shoulder, elbow, wrist: the elbow found from the two bones' lengths, bent out to the side and a little down.</summary>
        private static void SolveArm(Arm arm, Vector3 wrist, Vector3 side)
        {
            Vector3 s = arm.Upper.position;
            float l1 = Vector3.Distance(arm.Upper.position, arm.Fore.position);
            float l2 = Vector3.Distance(arm.Fore.position, arm.Hand.position);
            Vector3 toWrist = wrist - s;
            float d = Mathf.Clamp(toWrist.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = toWrist.normalized;
            float a = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f));
            Vector3 pole = (side * arm.Side + Vector3.down * 0.6f).normalized;   // elbows out and down
            Vector3 bendAxis = Vector3.Cross(dir, pole).normalized;
            Vector3 elbow = s + Quaternion.AngleAxis(-a * Mathf.Rad2Deg, bendAxis) * dir * l1;
            Point(arm.Upper, arm.Fore, elbow - s);
            Point(arm.Fore, arm.Hand, wrist - arm.Fore.position);
        }

        private static void Point(Transform bone, Transform child, Vector3 want)
        {
            if (bone == null || child == null) return;
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(now, want) * bone.rotation;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private void OnDestroy()
        {
            foreach (Renderer r in _hidden)
                if (r != null) r.enabled = true;
            _hidden.Clear();
            if (_current == this) _current = null;
        }
    }
}
