using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace GoodTidings
{
    /// <summary>
    /// The rider's animation, on her body (a Dverger rig with Hildir's face):
    /// - the player's Riding_Idle on the whole body;
    /// - over it, on a second layer for the head and arms only, one of Hildir's gestures (Greet, Talk, Buy, Sell) when asked: faded in,
    ///   played once, faded back to the reins (human rigs share clips, so hers play on the Dverger's);
    /// - her face (jaw, brows, cheeks) moved as Hildir's own: those are bones the human rig doesn't know, so an invisible copy of Hildir's
    ///   skeleton runs her own animator (idle, and the same gestures) and each frame its face bones are copied onto ours.
    /// Only on this game: nothing is sent.
    /// </summary>
    internal sealed class TraderAnim : MonoBehaviour
    {
        private const float FadeIn = 0.25f, FadeOut = 0.4f;
        private static readonly string[] FaceBones = { "jaw", "l.brow", "r.brow", "l.cheek", "r.cheek", "l.mouth", "r.mouth" };

        private PlayableGraph _graph;
        private AnimationLayerMixerPlayable _layers;
        private AnimationMixerPlayable _ride;
        private Character _mount;
        private float _pace;
        // the player's own riding: standing, walking (a Lox walks at 2 m/s) and cantering (it runs at 7)
        private static readonly string[] RideClips = { "Riding_Idle", "Riding_Walk", "Riding_Canter" };
        private const float WalkSpeed = 2f, CanterSpeed = 7f;
        private AnimationClipPlayable _gesture;
        private Dictionary<string, AnimationClip> _hildirClips;
        private float _gestureStart = -1f, _gestureLength;

        private GameObject _faceRig;
        private Animator _faceAnimator;
        private readonly List<(Transform from, Transform to)> _face = new List<(Transform, Transform)>();

        /// <summary>Sets her animation going on <paramref name="body"/> (its Animator), riding.</summary>
        internal static TraderAnim Begin(GameObject body)
        {
            Animator anim = body.GetComponent<Animator>();
            Animator player = ZNetScene.instance.GetPrefab("Player")?.GetComponentInChildren<Animator>(true);
            AnimationClip[] riding = RideClips.Select(n => player?.runtimeAnimatorController?.animationClips.FirstOrDefault(c => c.name == n)).ToArray();
            if (anim == null || riding.Any(c => c == null)) { Plugin.Log.LogWarning("No riding animation for the trader"); return null; }

            var a = body.AddComponent<TraderAnim>();
            a._graph = PlayableGraph.Create("goodtidings_rider");
            a._graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            a._layers = AnimationLayerMixerPlayable.Create(a._graph, 2);
            a._ride = AnimationMixerPlayable.Create(a._graph, riding.Length);
            for (int i = 0; i < riding.Length; i++)
            {
                a._graph.Connect(AnimationClipPlayable.Create(a._graph, riding[i]), 0, a._ride, i);
                a._ride.SetInputWeight(i, i == 0 ? 1f : 0f);
            }
            a._graph.Connect(a._ride, 0, a._layers, 0);
            a._mount = body.GetComponentInParent<Character>();
            a._layers.SetInputWeight(0, 1f);
            a._layers.SetLayerMaskFromAvatarMask(1, UpperBody());
            a._layers.SetInputWeight(1, 0f);
            AnimationPlayableOutput.Create(a._graph, "anim", anim).SetSourcePlayable(a._layers);
            a._graph.Play();

            Animator hildir = ZNetScene.instance.GetPrefab("Hildir")?.GetComponentInChildren<Animator>(true);
            a._hildirClips = hildir?.runtimeAnimatorController?.animationClips.GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First())
                             ?? new Dictionary<string, AnimationClip>();
            a.MakeFaceRig(body.transform);
            return a;
        }

        /// <summary>The head and arms (and fingers): the gesture layer leaves her seat and legs to the riding pose.</summary>
        private static AvatarMask UpperBody()
        {
            var mask = new AvatarMask();
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm
                    || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            return mask;
        }

        private void MakeFaceRig(Transform body)
        {
            Transform ownHead = Utils.FindChild(body, "goodtidings_head_Hildir");
            if (ownHead == null) return;   // (a body without her face)
            _faceRig = DvergerLook.BodyOf("Hildir", "Hildir", transform);
            _faceRig.name = "goodtidings_facerig";
            foreach (Renderer r in _faceRig.GetComponentsInChildren<Renderer>(true)) Destroy(r);
            foreach (LODGroup g in _faceRig.GetComponentsInChildren<LODGroup>(true)) Destroy(g);
            foreach (Component c in _faceRig.GetComponentsInChildren<Component>(true))
                if (c.GetType().Name == "MagicaCloth") Destroy(c);
            _faceAnimator = _faceRig.GetComponent<Animator>();
            _faceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // (it has nothing to draw, and must still move)
            _faceAnimator.SetBool("Stand", true);
            Transform rigHead = Utils.FindChild(_faceRig.transform, "head");
            foreach (string bone in FaceBones)
            {
                Transform from = rigHead ? Utils.FindChild(rigHead, bone) : null, to = Utils.FindChild(ownHead, bone);
                if (from && to) _face.Add((from, to));
            }
        }

        /// <summary>One of Hildir's gestures on her head, arms and face: "Greet", "Talk", "Buy" or "Sell".</summary>
        internal void Gesture(string name)
        {
            if (_faceAnimator) _faceAnimator.SetTrigger(name);
            if (!_graph.IsValid() || !_hildirClips.TryGetValue(name, out AnimationClip clip)) return;
            if (_gesture.IsValid())
            {
                _graph.Disconnect(_layers, 1);
                _gesture.Destroy();
            }
            _gesture = AnimationClipPlayable.Create(_graph, clip);
            _gesture.SetTime(0);
            _graph.Connect(_gesture, 0, _layers, 1);
            _gestureStart = Time.time;
            _gestureLength = clip.length;
            _layers.SetInputWeight(1, Mathf.Max(_layers.GetInputWeight(1), 0.01f));
        }

        private void Update()
        {
            Pace();
            if (_gestureStart < 0f || !_layers.IsValid()) return;
            float t = Time.time - _gestureStart;
            float w = t < FadeIn ? t / FadeIn : t > _gestureLength - FadeOut ? (_gestureLength - t) / FadeOut : 1f;
            _layers.SetInputWeight(1, Mathf.Clamp01(w));
            if (t >= _gestureLength) _gestureStart = -1f;
        }

        /// <summary>Blends standing, walking and cantering by how fast her mount goes (smoothed, so a stop or start isn't a jolt).</summary>
        private void Pace()
        {
            if (!_ride.IsValid() || _mount == null) return;
            Vector3 v = _mount.GetVelocity();
            v.y = 0f;
            _pace = Mathf.MoveTowards(_pace, v.magnitude, Time.deltaTime * 6f);
            float walk = Mathf.Clamp01(_pace / WalkSpeed), canter = Mathf.Clamp01((_pace - WalkSpeed) / (CanterSpeed - WalkSpeed));
            _ride.SetInputWeight(0, 1f - walk);
            _ride.SetInputWeight(1, walk * (1f - canter));
            _ride.SetInputWeight(2, walk * canter);
        }

        /// <summary>Where she looks (a player's head), or null to look ahead. Set each frame.</summary>
        internal Vector3? LookTarget;
        private float _lookWeight;
        private Vector3 _lookPoint, _lookVel;
        private Transform _head, _spine;

        /// <summary>
        /// Turns her head towards <see cref="LookTarget"/>, and her upper spine a little (as the game's LookAt does for its traders, with
        /// Hildir's weights; that works through animator IK, which her layered animation doesn't run, so it's done here, after it):
        /// eased in and out, at most 70 degrees, and let go when the target is round behind her.
        /// </summary>
        private void Look()
        {
            if (_head == null)
            {
                Transform armature = transform.Find("Armature");   // (her own bones: the face rig has a "head" too)
                _head = armature ? Utils.FindChild(armature, "head") : null;
                _spine = armature ? Utils.FindChild(armature, "spine3") : null;
                if (_head == null) return;
            }
            if (LookTarget is Vector3 target)
            {
                if (_lookWeight <= 0.001f) _lookPoint = target;
                _lookPoint = Vector3.SmoothDamp(_lookPoint, target, ref _lookVel, 0.5f);
            }
            _lookWeight = Mathf.MoveTowards(_lookWeight, LookTarget.HasValue ? 1f : 0f, Time.deltaTime * 1.5f);
            if (_lookWeight <= 0f) return;

            Vector3 ahead = transform.forward, to = _lookPoint - _head.position;
            float w = _lookWeight * Mathf.InverseLerp(120f, 80f, Vector3.Angle(ahead, to));   // round behind her: let go
            if (w <= 0f) return;
            Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(ahead, to), 70f);
            if (_spine) _spine.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.1f * w) * _spine.rotation;
            _head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.9f * w) * _head.rotation;
        }

        private void LateUpdate()
        {
            Look();
            foreach ((Transform from, Transform to) in _face)
            {
                if (!from || !to) continue;
                to.localRotation = from.localRotation;
                to.localPosition = from.localPosition;
            }
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_faceRig) Destroy(_faceRig);
        }
    }
}
