using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// The sea draugr of Norwegian folklore: a drowned man rowing half a boat through the fog, an omen that someone aboard will drown. He
    /// comes out of the fog ahead, rows past off one side, broken end first, and is gone into the fog behind, paying the ship no mind.
    /// The half-boat is the game's karve wreckage (the stern and its post: the wreck's bottom boards are a whole boat's length, so they
    /// stay out); the rower a draugr posed bone by bone, rowing two oars made here.
    /// </summary>
    internal sealed class HalfBoatOmen : Omen
    {
        internal override string Name => "halfboat";
        protected override ConfigEntry<bool> Setting => Plugin.HalfBoat;

        private const float MaxSight = 250f;   // more than this and it would be seen coming from far off: fog or night only
        private const float RowSpeed = 2.5f;   // metres per second over the water

        internal override bool CanPlay(Ship ship) => EnvMan.IsNight() || Sea.SightDistance() <= MaxSight;

        internal override void Play(Ship ship, uint seed) => Play(ship, seed, lateral: 0f);

        /// <param name="lateral">How far off the ship's side it passes (0: chosen from the seed). For "keel play halfboat near" it holds
        /// still there, to starboard, rowing in place for a minute: to look at it closely.</param>
        internal static void Play(Ship ship, uint seed, float lateral)
        {
            var rng = new System.Random((int)seed);

            // From just past what the fog lets you see ahead to just past it behind, at a rower's pace plus the ship's own speed.
            float pass = Mathf.Clamp(Sea.SightDistance() * 1.1f, 60f, MaxSight);
            var body = ship.GetComponent<Rigidbody>();
            float shipSpeed = body != null ? Mathf.Max(0f, Vector3.Dot(body.linearVelocity, ship.transform.forward)) : 0f;
            float duration = Mathf.Clamp(2f * pass / (shipSpeed + RowSpeed), 35f, 150f);
            bool hold = lateral != 0f;
            if (hold) duration = 60f;
            else
            {
                lateral = 35f + (float)rng.NextDouble() * 15f;
                if (rng.Next(2) == 0) lateral = -lateral;
            }

            GameObject root = Visuals.Spawn("HalfBoat", ship.transform.position, Quaternion.identity, duration + 1f);
            var boat = root.AddComponent<HalfBoat>();
            if (!boat.Build()) { Object.Destroy(root); return; }
            boat.Begin(ship, duration, lateral, pass, stroke: 3f + (float)rng.NextDouble() * 0.8f, hold);
        }
    }

    /// <summary>The half-boat, its rower and his oars; moved past the ship and rowed every frame.</summary>
    internal sealed class HalfBoat : MonoBehaviour
    {
        private static readonly string[] Wreck = { "shipwreck_karve_stern", "shipwreck_karve_sternpost" };   // (stern toward the wreck's +z)

        private const float HullScale = 0.6f;      // the karve wreck shrunk to a one-man boat (the rower stays full size)
        private const float Draft = 0.25f;         // how deep the hull sits below the wave under it
        private const float SeatHeight = 1.05f;    // the rower's hips above the boat's root (in the wreck's own size: times HullScale)
        private const float HipHeight = 0.95f;     // a standing draugr's hips above its feet (the holder's pivot)
        private const float SeatAft = 0.2f;        // the seat, from the middle of the wreck toward its stern (boat +z)
        private const float OarlockAft = 0.4f;     // the oarlocks, from the seat toward the stern
        private const float OarlockOut = 0.95f;    // half the width between the oarlocks
        private const float OarlockHeight = 1.5f;
        private const float OarLength = 2.8f;

        private Ship _ship;
        private float _duration, _lateral, _pass, _stroke, _age;
        private bool _hold;
        private Vector3 _heading, _lastPos;
        private Transform _boat, _rower;
        private Transform _leftOar, _rightOar;
        private Transform _spine, _lArm, _lFore, _lHand, _rArm, _rFore, _rHand;
        private readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        private readonly List<(Transform bone, Transform child, Vector3 dir)> _legs = new List<(Transform, Transform, Vector3)>();
        private Transform _head, _headEnd;
        private float _seatZ;

        internal bool Build()
        {
            _boat = new GameObject("Wreck").transform;
            _boat.SetParent(transform, false);
            var hull = new GameObject("Hull").transform;
            hull.SetParent(_boat, false);
            hull.localScale = Vector3.one * HullScale;
            foreach (string piece in Wreck)
            {
                GameObject look = Visuals.CloneLook(piece, hull, part: null);   // (scenery: no Character on the root to worry about)
                if (look == null) return false;
                look.transform.localPosition = Vector3.zero;
                look.transform.localRotation = Quaternion.identity;
            }

            // Where the wreck actually is, measured from its meshes: the seat goes a little aft of its middle.
            var bounds = new Bounds(_boat.position, Vector3.zero);
            foreach (Renderer r in _boat.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            Vector3 centre = _boat.InverseTransformPoint(bounds.center);
            _seatZ = centre.z + SeatAft * HullScale;
            Plugin.Log.LogInfo($"Half-boat: wreck {bounds.size.x:0.0} x {bounds.size.y:0.0} x {bounds.size.z:0.0} m, centre {centre}");

            if (!BuildRower()) return false;
            _leftOar = Oar();
            _rightOar = Oar();
            return true;
        }

        private bool BuildRower()
        {
            GameObject look = Visuals.CloneLook("Draugr", transform);
            if (look == null) return false;
            foreach (var anim in look.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
            foreach (var r in look.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) { r.enabled = false; continue; }
                r.sharedMaterial = Look.Drowned(r.sharedMaterial);
            }

            // As in Rán's net: the model faces backwards to its prefab, so it goes in a holder turned round. The rower faces the stern
            // (boat +z), the way one rows.
            _rower = new GameObject("Rower").transform;
            _rower.SetParent(_boat, false);
            _rower.localPosition = new Vector3(0f, SeatHeight * HullScale - HipHeight, _seatZ);
            _rower.localRotation = Quaternion.identity;
            look.transform.SetParent(_rower, false);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Transform B(string n) { Transform t = Find(look.transform, n); if (t != null) _rest[t] = t.localRotation; return t; }
            _spine = B("Spine1");
            _lArm = B("LeftArm"); _lFore = B("LeftForeArm"); _lHand = B("LeftHand");
            _rArm = B("RightArm"); _rFore = B("RightForeArm"); _rHand = B("RightHand");
            _head = B("Head"); _headEnd = Find(look.transform, "Head_end");
            foreach (string side in new[] { "Left", "Right" })
            {
                float outward = side == "Left" ? -1f : 1f;
                // Seated: thighs forward along the bottom boards, shins down, feet flat-ish.
                Leg(look, side + "UpLeg", side + "Leg", new Vector3(0.12f * outward, -0.15f, 1f));
                Leg(look, side + "Leg", side + "Foot", new Vector3(0.05f * outward, -1f, 0.25f));
                Leg(look, side + "Foot", side + "Foot_end", new Vector3(0f, -0.3f, 1f));
            }
            if (_spine == null || _lArm == null || _rArm == null || _lFore == null || _rFore == null || _lHand == null || _rHand == null)
            {
                Plugin.Log.LogWarning("Draugr is missing bones the rower needs");
                return false;
            }
            return true;
        }

        private void Leg(GameObject look, string bone, string child, Vector3 dir)
        {
            Transform b = Find(look.transform, bone), c = Find(look.transform, child);
            if (b == null || c == null) return;
            _rest[b] = b.localRotation;
            _legs.Add((b, c, dir.normalized));
        }

        private Transform Oar()
        {
            var oar = new GameObject("Oar").transform;
            oar.SetParent(_boat, false);
            Material wood = Look.Wood();

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(oar, false);
            shaft.transform.localScale = new Vector3(0.07f, OarLength / 2f, 0.07f);   // along the oar's y, handle at 0
            shaft.transform.localPosition = new Vector3(0f, OarLength / 2f, 0f);
            shaft.GetComponent<Renderer>().sharedMaterial = wood;

            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(oar, false);
            blade.transform.localScale = new Vector3(0.18f, 0.7f, 0.035f);
            blade.transform.localPosition = new Vector3(0f, OarLength - 0.35f, 0f);
            blade.GetComponent<Renderer>().sharedMaterial = wood;
            return oar;
        }

        internal void Begin(Ship ship, float duration, float lateral, float pass, float stroke, bool hold)
        {
            _pass = pass;
            _hold = hold;
            _ship = ship;
            _duration = duration;
            _lateral = lateral;
            _stroke = stroke;
            _heading = Flat(ship.transform.forward);
            Place(0f);
            _lastPos = transform.position;
        }

        private void LateUpdate()
        {
            if (_ship == null) { Destroy(gameObject); return; }
            _age += Time.deltaTime;
            Place(Time.deltaTime);
        }

        private void Place(float dt)
        {
            // Along the ship's side from ahead to behind, whatever the ship does: it passes, and the fog has it at both ends.
            if (dt > 0f) _heading = Vector3.Slerp(_heading, Flat(_ship.transform.forward), 1f - Mathf.Exp(-0.3f * dt)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, _heading);
            float along = _hold ? 0f : Mathf.Lerp(_pass, -_pass, _age / _duration);
            Vector3 pos = _ship.transform.position + _heading * along + right * _lateral;
            pos.y = Floating.GetLiquidLevel(pos, 1f, LiquidType.Water) - Draft;
            transform.position = pos;

            // It faces the way it really moves over the water (broken end first), turning slowly.
            // The wreck's +z is its stern: it goes the other way, broken end first.
            if (_hold) transform.rotation = Quaternion.LookRotation(-_heading);
            else if (dt > 0f)
            {
                Vector3 moved = Flat(pos - _lastPos);
                if ((pos - _lastPos).sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(-moved), 1f - Mathf.Exp(-1f * dt));
                _lastPos = pos;
            }
            float roll = 3f * Mathf.Sin(_age * 0.9f), pitch = 2f * Mathf.Sin(_age * 0.7f + 1f);
            _boat.localRotation = Quaternion.Euler(pitch, 0f, roll);

            Row();
        }

        /// <summary>
        /// One stroke: the handles move on a loop in front of the rower (pushed away and low on the recovery, high and pulled in on the
        /// drive, which puts the blades in the water); the oars pivot on their oarlocks; the arms reach for the handles; the body leans in.
        /// </summary>
        private void Row()
        {
            foreach (var kv in _rest) kv.Key.localRotation = kv.Value;   // the animator is off: start from rest every frame

            float phase = _age / _stroke * Mathf.PI * 2f;   // 0: the catch (arms out); pi: the finish (handles at the chest)
            float reach = Mathf.Cos(phase), lift = Mathf.Sin(phase);

            // The rower's own frame: _rower faces the stern.
            Vector3 fwd = _rower.forward, up = _rower.up, side = _rower.right;
            Vector3 chest = _spine.position + up * 0.35f;

            // Lean forward to the catch, back at the finish.
            _spine.rotation = Quaternion.AngleAxis(12f * reach + 4f, side) * _spine.rotation;

            foreach (var (bone, child, dir) in _legs) Point(bone, child, _rower.TransformDirection(dir));

            foreach (int s in new[] { -1, 1 })
            {
                Vector3 handle = chest + fwd * (0.45f + 0.25f * reach) + up * (-0.2f + 0.08f * lift) + side * (0.25f * s);
                Vector3 oarlock = _boat.TransformPoint(new Vector3(0f, OarlockHeight * HullScale, _seatZ + OarlockAft * HullScale)) +
                                  side * (OarlockOut * HullScale * s);
                Transform oar = s < 0 ? _leftOar : _rightOar;
                oar.position = handle;
                oar.rotation = Quaternion.FromToRotation(Vector3.up, oarlock - handle);

                Transform arm = s < 0 ? _lArm : _rArm, fore = s < 0 ? _lFore : _rFore, hand = s < 0 ? _lHand : _rHand;
                Point(arm, fore, (handle - arm.position).normalized - up * 0.35f + side * 0.1f * s);
                Point(fore, hand, handle - fore.position);
            }

            // Head a little down, looking at nothing.
            if (_head != null && _headEnd != null) Point(_head, _headEnd, up + fwd * 0.25f);
        }

        private static void Point(Transform bone, Transform child, Vector3 want)
        {
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(now, want) * bone.rotation;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
