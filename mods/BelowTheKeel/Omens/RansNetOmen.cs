using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Rán's net: the drowned hang still in the water under the ship, upright, as if caught in something. They rise out of the murk on
    /// the ship's course, so it sails over them, hang there a while, then drop into the deep all at once, as if the net were drawn in.
    /// The bodies are the game's draugr, darkened, their idle slowed to a drift.
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
                if (!net.Add(offset, depth: Range(rng, 4.5f, 7.5f), yaw: Range(rng, 0f, 360f),
                        tilt: Range(rng, -12f, 12f), drift: Range(rng, 0.12f, 0.25f), phase: Range(rng, 0f, 10f)))
                    break;
            }
            net.Begin(duration);
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }
    }

    /// <summary>The bodies, moved together: up out of the murk, a slow bob and turn while they hang, then a fast drop.</summary>
    internal sealed class RansNet : MonoBehaviour
    {
        private const float HiddenDrop = 12f;      // how far below their hanging depth they start and end: out of sight
        private const float RiseSeconds = 8f;
        private const float DropSeconds = 2.5f;

        private sealed class Body
        {
            public Transform T;
            public Vector3 Offset;
            public float Depth, Yaw, Tilt, Phase;
        }

        private readonly List<Body> _bodies = new List<Body>();
        private float _duration, _age;

        internal bool Add(Vector3 offset, float depth, float yaw, float tilt, float drift, float phase)
        {
            GameObject look = Visuals.CloneLook("Draugr", transform);
            if (look == null) return false;

            foreach (var r in look.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = Look.Drowned(r.sharedMaterial);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var anim in look.GetComponentsInChildren<Animator>(true))
            {
                anim.fireEvents = false;   // its event receiver (CharacterAnimEvent) is gone: don't log an error for every footstep
                anim.speed = drift;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            _bodies.Add(new Body { T = look.transform, Offset = offset, Depth = depth, Yaw = yaw, Tilt = tilt, Phase = phase });
            return true;
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

            foreach (Body b in _bodies)
            {
                float bob = 0.25f * Mathf.Sin((_age + b.Phase) * 0.6f);
                float sway = 4f * Mathf.Sin((_age + b.Phase) * 0.3f);
                b.T.localPosition = b.Offset + Vector3.down * (b.Depth + lower - bob);
                b.T.localRotation = Quaternion.Euler(b.Tilt + sway * 0.5f, b.Yaw + sway, sway * 0.7f);
            }
        }
    }
}
