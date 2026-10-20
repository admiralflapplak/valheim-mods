using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Rán's net: the drowned hang under the ship, straight as the dead, arms at their sides, toes pointing down, one rope from the feet
    /// down into the dark, as if weighted and caught. They rise out of the murk on the ship's course, so it sails over them, heads bowed
    /// and eyes dark. Then every pair of eyes opens at once, the heads come up and the bodies turn to look at you. Before the end the eyes
    /// go out and they all drop into the deep together, as if the net were drawn in. The bodies are the game's draugr, darkened and posed
    /// bone by bone; their eyes are the draugr's own glow.
    /// </summary>
    internal sealed class RansNetOmen : Omen
    {
        internal override string Name => "ransnet";
        protected override ConfigEntry<bool> Setting => Plugin.RansNet;

        internal override void Play(Ship ship, uint seed)
        {
            var rng = new System.Random((int)seed);
            float duration = Range(rng, 35f, 45f);

            // Where the ship will be in a few seconds, so it passes over them (it is the same for the whole crew: the ship's own motion).
            var body = ship.GetComponent<Rigidbody>();
            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            velocity.y = 0f;
            Vector3 centre = ship.transform.position + velocity * 10f;
            Vector3 along = velocity.sqrMagnitude > 0.5f ? velocity.normalized : Flat(ship.transform.forward);
            Vector3 across = Vector3.Cross(Vector3.up, along);
            float spread = 8f + velocity.magnitude * 5f;   // a longer field when sailing fast, so it doesn't go by in a moment

            GameObject root = Visuals.Spawn("RansNet", new Vector3(centre.x, Sea.WaterLevel, centre.z), Quaternion.identity, duration + 1f);
            var net = root.AddComponent<RansNet>();

            int count = rng.Next(6, 10);
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = along * Range(rng, -spread, spread) + across * Range(rng, -9f, 9f);
                if (!net.Add(offset, depth: Range(rng, 4.5f, 7.5f), yaw: Range(rng, 0f, 360f), tilt: Range(rng, -8f, 8f), phase: Range(rng, 0f, 10f)))
                    break;
            }
            net.Begin(duration);
        }

        /// <summary>For "keel pose": one body hanging just above the water beside the ship, to see the pose and ropes clearly.</summary>
        internal static string Pose(Ship ship)
        {
            Vector3 at = ship.transform.position + ship.transform.right * 7f;
            float yaw = ship.transform.eulerAngles.y + 90f;
            GameObject root = Visuals.Spawn("RansNetPose", new Vector3(at.x, Sea.WaterLevel, at.z), Quaternion.identity, 61f);
            var net = root.AddComponent<RansNet>();
            net.Add(Vector3.zero, depth: -2.5f, yaw: yaw, tilt: 0f, phase: 0f);
            net.Begin(60f);
            return $"feet at {at.x:0.0} {Sea.WaterLevel + 2.5f:0.0} {at.z:0.0}, facing yaw {Mathf.Repeat(yaw, 360f):0} until its eyes open";
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// The bodies, moved together: up out of the murk, a slow bob while they hang, eyes opening and turning to the player, then a fast
    /// drop. Each player's bodies look at that player (the timing is the same for the whole crew, where they look is each one's own).
    /// </summary>
    internal sealed class RansNet : MonoBehaviour
    {
        private const float HiddenDrop = 12f;      // how far below their hanging depth they start and end: out of sight
        private const float RiseSeconds = 8f;
        private const float DropSeconds = 2.5f;
        private const float EyesOpenAt = 11f;      // seconds in: risen, hanging still a moment, then every pair opens at once
        private const float EyesOpenSeconds = 0.3f;
        private const float EyesCloseBeforeEnd = 3.5f;
        private const float EyesCloseSeconds = 0.4f;
        private const float TurnSpeed = 22f;       // degrees per second, turning to the player
        private const float LookUpSpeed = 15f;     // degrees per second, tipping back to look up (and back again)
        private const float MaxLookUp = 30f;
        private const float HeadFollow = 40f;     // the most the head turns from the body to follow the player (degrees)
        private const float HeadLag = 2f;         // how quickly the head catches up with the player (higher is quicker)
        private const float RopeLength = 24f;      // reaches well past what the murk lets through
        private const float RopeThickness = 0.07f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        /// <summary>A bone aimed each frame: the direction from it to its child, in the body's frame (x right, y up, z forward).</summary>
        private sealed class Aim
        {
            public Transform Bone, Child;
            public Vector3 Dir;
            public Quaternion Rest;   // the bone's own rotation before any posing: every frame starts again from it (nothing piles up)
        }

        private sealed class Body
        {
            public Transform T;
            public Vector3 Offset;
            public float Depth, Yaw, Tilt, Phase, TurnDelay, LookUp;
            public Quaternion HeadTurn = Quaternion.identity;   // on top of the body's facing: eases toward the player
            public readonly List<Aim> Limbs = new List<Aim>();
            public Aim Head;
            public Transform LeftToe, RightToe, Rope;
            public readonly List<Renderer> Glows = new List<Renderer>();
            public readonly List<Color> GlowColours = new List<Color>();
        }

        private readonly List<Body> _bodies = new List<Body>();
        private float _duration, _age;

        internal bool Add(Vector3 offset, float depth, float yaw, float tilt, float phase)
        {
            GameObject look = Visuals.CloneLook("Draugr", transform);
            if (look == null) return false;

            // Posed by hand, not animated: off before the clone ever runs a frame.
            foreach (var anim in look.GetComponentsInChildren<Animator>(true)) anim.enabled = false;

            // The draugr model faces backwards to its prefab's forward: turn it round inside a holder, so the holder's frame is the
            // body's own (x its right, y up, z the way it faces) for the pose, the turn to the player and the bowed head.
            var holder = new GameObject("Drowned").transform;
            holder.SetParent(transform, false);
            look.transform.SetParent(holder, false);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var body = new Body { T = holder, Offset = offset, Depth = depth, Yaw = yaw, Tilt = tilt, Phase = phase, TurnDelay = phase * 0.25f };
            foreach (var r in look.GetComponentsInChildren<Renderer>(true))
            {
                // Only the body and its eyes: the draugr's smoke, flies and dripping water have no place under the sea.
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
                {
                    r.enabled = false;
                    continue;
                }
                r.sharedMaterial = Look.Drowned(r.sharedMaterial);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r.sharedMaterial.HasProperty(EmissionColor) && r.sharedMaterial.GetColor(EmissionColor).maxColorComponent > 0f)
                {
                    body.Glows.Add(r);
                    body.GlowColours.Add(r.sharedMaterial.GetColor(EmissionColor));
                }
            }

            // Straight as the dead, but afloat: arms away from the sides, a little forward and bent at the elbow; legs straight and feet
            // together (bound), toes pointed.
            foreach (string side in new[] { "Left", "Right" })
            {
                float outward = side == "Left" ? -1f : 1f;
                AddLimb(body, look, side + "Arm", side + "ForeArm", new Vector3(0.42f * outward, -1f, 0.25f));
                AddLimb(body, look, side + "ForeArm", side + "Hand", new Vector3(0.3f * outward, -1f, 0.6f));
                AddLimb(body, look, side + "Hand", side + "Hand_end", new Vector3(0.25f * outward, -0.9f, 0.6f));
                AddLimb(body, look, side + "UpLeg", side + "Leg", new Vector3(-0.07f * outward, -1f, 0f));
                AddLimb(body, look, side + "Leg", side + "Foot", new Vector3(-0.03f * outward, -1f, 0f));
                AddLimb(body, look, side + "Foot", side + "Foot_end", new Vector3(0f, -1f, 0.3f));
            }
            Transform head = Find(look.transform, "Head"), headEnd = Find(look.transform, "Head_end");
            if (head != null && headEnd != null) body.Head = new Aim { Bone = head, Child = headEnd, Rest = head.localRotation };

            body.LeftToe = Find(look.transform, "LeftFoot_end");
            body.RightToe = Find(look.transform, "RightFoot_end");
            body.Rope = Rope();

            _bodies.Add(body);
            return true;
        }

        private static void AddLimb(Body body, GameObject look, string bone, string child, Vector3 dir)
        {
            Transform b = Find(look.transform, bone), c = Find(look.transform, child);
            if (b == null || c == null)
            {
                Plugin.Log.LogWarning($"Draugr has no {bone} or {child}: that limb keeps its own pose");
                return;
            }
            body.Limbs.Add(new Aim { Bone = b, Child = c, Dir = dir.normalized, Rest = b.localRotation });
        }

        private Transform Rope()
        {
            GameObject rope = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(rope.GetComponent<Collider>());
            rope.name = "Rope";
            rope.transform.SetParent(transform, false);
            rope.transform.localScale = new Vector3(RopeThickness, RopeLength / 2f, RopeThickness);   // (a cylinder is 2 m tall)
            var r = rope.GetComponent<MeshRenderer>();
            r.sharedMaterial = Look.Rope();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return rope.transform;
        }

        internal void Begin(float duration)
        {
            _duration = duration;
            Place();
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            Place();
        }

        private void Place()
        {
            float lower;   // how far below their hanging depth they are now
            float left = _duration - _age;
            if (_age < RiseSeconds) lower = HiddenDrop * (1f - Mathf.SmoothStep(0f, 1f, _age / RiseSeconds));
            else if (left < DropSeconds) lower = HiddenDrop * Mathf.Pow(1f - Mathf.Clamp01(left / DropSeconds), 2f);   // all at once, faster and faster
            else lower = 0f;

            float eyes = Eyes(left);
            float swayWeight = 1f - Mathf.Clamp01((_age - EyesOpenAt) / 2f);   // the drifting stops once they've seen you, for good
            Player player = Player.m_localPlayer;
            foreach (Body b in _bodies)
            {
                float bob = 0.25f * Mathf.Sin((_age + b.Phase) * 0.6f);
                float sway = 4f * Mathf.Sin((_age + b.Phase) * 0.3f);
                b.T.localPosition = b.Offset + Vector3.down * (b.Depth + lower - bob);

                // Once its eyes are open, each turns (a little after the others) to face the player and tips back to look up. Both ease
                // in and out at a set speed, so nothing jumps when the eyes go out.
                float lookUpTarget = 0f;
                if (player != null && _age > EyesOpenAt + b.TurnDelay && left > EyesCloseBeforeEnd)
                {
                    Vector3 to = player.transform.position - b.T.position;
                    float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    b.Yaw = Mathf.MoveTowardsAngle(b.Yaw, targetYaw, TurnSpeed * Time.deltaTime);
                    float flat = new Vector2(to.x, to.z).magnitude;
                    lookUpTarget = Mathf.Clamp(Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg, 0f, MaxLookUp);
                }
                b.LookUp = Mathf.MoveTowards(b.LookUp, lookUpTarget, LookUpSpeed * Time.deltaTime);
                b.T.localRotation = Quaternion.Euler(b.Tilt + sway * 0.5f * swayWeight - b.LookUp, b.Yaw + sway * swayWeight, sway * 0.7f * swayWeight);

                Pose(b, eyes, player);
                Ropes(b);

                for (int i = 0; i < b.Glows.Count; i++)
                {
                    b.Glows[i].GetPropertyBlock(_block);
                    _block.SetColor(EmissionColor, b.GlowColours[i] * eyes);
                    b.Glows[i].SetPropertyBlock(_block);
                }
            }
        }

        /// <summary>Turns each bone (parents first) so the line to its child points the way the pose wants, in the body's frame.</summary>
        private static void Pose(Body b, float eyes, Player player)
        {
            // Animators are off, so a bone keeps last frame's rotation: start each from rest, or turns added on top pile up and spin.
            foreach (Aim a in b.Limbs) a.Bone.localRotation = a.Rest;
            if (b.Head != null) b.Head.Bone.localRotation = b.Head.Rest;

            foreach (Aim a in b.Limbs) Point(a, b.T.TransformDirection(a.Dir));
            if (b.Head == null) return;

            // Chin on the chest while the eyes are dark; the head comes up as they open.
            Vector3 bowed = new Vector3(0f, 1f, 0.55f), raised = new Vector3(0f, 1f, -0.1f);
            Point(b.Head, b.T.TransformDirection(Vector3.Lerp(bowed, raised, eyes).normalized));

            // With its eyes open the head follows the player, a little behind and only so far: the body does the rest of the turning.
            Quaternion want = Quaternion.identity;
            if (player != null && eyes > 0f)
            {
                Vector3 to = player.GetHeadPoint() - b.Head.Bone.position;
                Quaternion full = Quaternion.FromToRotation(b.T.forward, to);
                float angle = Quaternion.Angle(Quaternion.identity, full);
                want = Quaternion.Slerp(Quaternion.identity, full, angle > HeadFollow ? HeadFollow / angle : 1f);
                want = Quaternion.Slerp(Quaternion.identity, want, eyes);
            }
            b.HeadTurn = Quaternion.Slerp(b.HeadTurn, want, 1f - Mathf.Exp(-HeadLag * Time.deltaTime));
            b.Head.Bone.rotation = b.HeadTurn * b.Head.Bone.rotation;
        }

        private static void Point(Aim a, Vector3 want)
        {
            Vector3 now = a.Child.position - a.Bone.position;
            if (now.sqrMagnitude < 1e-8f) return;
            a.Bone.rotation = Quaternion.FromToRotation(now, want) * a.Bone.rotation;
        }

        /// <summary>One rope from between the feet straight down into the dark, with a little slack swing.</summary>
        private void Ropes(Body b)
        {
            if (b.Rope == null || b.LeftToe == null || b.RightToe == null) return;
            Vector3 down = Quaternion.Euler(2f * Mathf.Sin((_age + b.Phase) * 0.4f), 0f, 2f * Mathf.Cos((_age + b.Phase) * 0.33f)) * Vector3.down;
            Vector3 feet = (b.LeftToe.position + b.RightToe.position) / 2f;
            b.Rope.position = feet + down * (RopeLength / 2f);
            b.Rope.rotation = Quaternion.FromToRotation(Vector3.up, -down);
        }

        /// <summary>How open the eyes are, 0 to 1: dark while they rise, all open at once, out again just before the drop.</summary>
        private float Eyes(float left)
        {
            if (_age < EyesOpenAt) return 0f;
            if (left < EyesCloseBeforeEnd) return Mathf.Clamp01(1f - (EyesCloseBeforeEnd - left) / EyesCloseSeconds);
            return Mathf.Clamp01((_age - EyesOpenAt) / EyesOpenSeconds);
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
