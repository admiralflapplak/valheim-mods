using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeaponOils
{
    /// <summary>
    /// The animation of putting oil on a weapon: the right hand holds the weapon out across the body, blade angled up and to the left, and
    /// the left hand slides along it from the hilt to the tip while the head follows it; then everything lets go. About a second and a
    /// half, laid over whatever the player is doing (it stops at once if they attack or the weapon leaves their hand).
    ///
    /// The oil is used through Player.ConsumeItem, which answers "not consumed" (see OilApply) so that the game doesn't also play its own
    /// eating animation and swap the weapon for the pouch: that fought this one, and showed through as a snap at the start and the end.
    ///
    /// It is done the way the Spyglass raises an arm: the game's animator already runs an IK pass each frame, and the hands' goals and the
    /// head's look-at are set there (a hand is placed, and its elbow is told which way to point); after the animator has finished, the
    /// hands are turned (the IK goal's own turning isn't the bone's, so it is done to the bone). The weapon's blade is found from its model
    /// (OilGlow.Blade). Other players' games play it too, from a counter on the player object that changes each time that player oils a
    /// weapon.
    ///
    /// The turning is absolute, not cumulative: the game's late update can run more than once between two animation updates, and a turn
    /// applied on top of the last one's result each time overshoots and then snaps back (a jitter, worst while the pose fades in and out).
    /// So what the animator gave the bone and what was set on it are remembered, and the turn is always worked out from the animator's own.
    ///
    /// Oil rubbed on bare hands has a pose of its own (Palms): both hands come up in front of the chest, palms up, and the head bows to
    /// look at them, for the same time and with the same sound; it is told to other players by a counter of its own.
    ///
    /// A weapon with a blade in each hand (Skoll and Hati, the Berserkir axes) has a pose of its own too: the game's own dual block with
    /// the torso squared up, the left blade drawn along the right one (DualGoals, TurnDual).
    /// </summary>
    internal static class OilAnimation
    {
        private const string Key = "WeaponOils.oiling";
        private const string PalmsKey = "WeaponOils.palms";
        // The times, in seconds at the setting's speed of 1. The lift and the release carry the arm between wherever the game's own animation
        // holds it (at the side, even behind the back) and the pose in front of the chest: that is a long way for an arm, and was a snap at
        // 0.35 and 0.4 s (seen in captured frames), so they are longer and the ease is gentler (see Ease).
        private const float BaseLift = 0.5f, BaseSweep = 1f, BaseRelease = 0.8f;
        private static float Scale => 1f / Mathf.Max(0.25f, Plugin.AnimationSpeed.Value);
        private static float LiftTime => BaseLift * Scale;
        private static float SweepTime => BaseSweep * Scale;
        private static float ReleaseTime => BaseRelease * Scale;
        // A weapon in each hand lets go more slowly: the Berserkir axes' stance holds the hands low by the hips, a long way from the guard.
        private static float ReleaseOf(Pose pose) => pose.Dual == 1 ? ReleaseTime * 1.4f : ReleaseTime;

        /// <summary>0 to 1, starting and ending very gently (the smoother "smootherstep" curve: speed and acceleration are both zero at the ends).</summary>
        private static float Ease(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (6f * x - 15f) + 10f);
        }

        /// <summary>A bone turned by this mod: what the animator gave it this frame and what was set, so a second pass in one frame changes nothing.</summary>
        private struct Turned
        {
            public Quaternion Given, Set;
            public bool Have;

            /// <summary>The animator's own rotation of the bone: the one remembered if the bone still has what we set, else what it has now.</summary>
            public Quaternion Basis(Quaternion current) => Have && Quaternion.Angle(current, Set) < 0.05f ? Given : current;

            public void Remember(Quaternion given, Quaternion set) { Given = given; Set = set; Have = true; }
        }

        private sealed class Pose
        {
            public float Start = -100f;                    // Time.time when it began
            public bool HandsOn;                           // the IK goals are switched on (so they are switched off once afterwards)
            public Vector3 Blade = Vector3.forward;        // the way the blade is held, worked out in the animation pass for the late update
            public Vector3 LookAt;                         // where the head looks: the left hand (set with Blade)
            public Quaternion Facing = Quaternion.identity;   // the way the body faces, level (set with Blade)
            public Turned Right;
            public bool Planned;                           // the hand's turn has been worked out for this oiling (Rest, Mid, Held)
            public Quaternion Rest, Mid, Held;             // the hand's rotation, in the body's frame: as it was, half way, and in the pose
            public int Seen = -1;                          // another player's counter, as last read (-1: not read yet: nothing to replay)
            public bool Palms;                             // this time it is the bare-hands pose
            public Turned Left;                            // (the palms pose turns both hands)
            public Quaternion RestLeft, HeldLeft;
            public int SeenPalms = -1;
            public int Dual = -1;                          // a weapon with a blade in each hand: -1 not looked at yet this oiling, 0 no, 1 yes
            public bool Squared;                           // (when Dual) DualFacing has been worked out for this oiling
            public Quaternion DualFacing;                  // (when Dual) the way the chest faces, level: the dual pose is placed from it
            public float Twist;                            // (when Dual) how far that is turned from the block's chest, in degrees: undone at the spine
        }

        private static readonly Dictionary<Player, Pose> Poses = new Dictionary<Player, Pose>();
        private static int _counter, _palmsCounter;
        private static float _nextLook;

        private static readonly FieldInfo AnimatorField = AccessTools.Field(typeof(CharacterAnimEvent), "m_animator");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> AnimCharacter = AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");
        private static readonly FieldInfo AnimVisField = AccessTools.Field(typeof(CharacterAnimEvent), "m_visEquipment");
        private static readonly FieldInfo LookAtWeightField = AccessTools.Field(typeof(CharacterAnimEvent), "m_lookAtWeight");
        private static readonly FieldInfo BodyLookField = AccessTools.Field(typeof(CharacterAnimEvent), "m_bodyLookWeight");
        private static readonly FieldInfo HeadLookField = AccessTools.Field(typeof(CharacterAnimEvent), "m_headLookWeight");
        private static readonly FieldInfo EyeLookField = AccessTools.Field(typeof(CharacterAnimEvent), "m_eyeLookWeight");
        private static readonly FieldInfo LookClampField = AccessTools.Field(typeof(CharacterAnimEvent), "m_lookClamp");

        private static Pose PoseOf(Player player)
        {
            if (!Poses.TryGetValue(player, out Pose pose)) Poses[player] = pose = new Pose();
            return pose;
        }

        /// <summary>The local player has put oil on this weapon: play it for them (if they want it) and tell the others.</summary>
        internal static void Begin(Player player, ItemDrop.ItemData weapon)
        {
            if (player == null || OilGlow.ItemIn(player, OilGlow.Slot.RightHand) != weapon) return;   // (a weapon held in the left hand has no animation yet)
            ZNetView view = player.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner()) view.GetZDO().Set(Key, ++_counter);
            if (Plugin.ApplyAnimation.Value) Play(PoseOf(player), palms: false);
            _soundAt = Time.time + LiftTime;   // (the oil is used up and the weapon not hidden, so the game plays no sound of its own: this is it)
            _soundPlayer = player;
        }

        /// <summary>The local player has rubbed oil on their bare hands: the palms pose, the same sound, and the others told.</summary>
        internal static void BeginPalms(Player player)
        {
            if (player == null) return;
            ZNetView view = player.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner()) view.GetZDO().Set(PalmsKey, ++_palmsCounter);
            if (Plugin.ApplyAnimation.Value) Play(PoseOf(player), palms: true);
            _soundAt = Time.time + LiftTime;
            _soundPlayer = player;
        }

        private static void Play(Pose pose, bool palms)
        {
            pose.Start = Time.time;
            pose.Palms = palms;
            pose.Planned = false;
            pose.Right = default;
            pose.Left = default;
            pose.Dual = -1;
            pose.Squared = false;
        }

        private static float _soundAt;
        private static Player _soundPlayer;

        private static void PlaySound()
        {
            Player player = _soundPlayer;
            _soundPlayer = null;
            if (player == null) return;
            Sound.PlayAt(Plugin.OilSound.Value, player.transform.position + Vector3.up, Plugin.OilSoundPitch.Value, Plugin.OilSoundVolume.Value);
        }

        /// <summary>Called every frame: notices other players oiling a weapon (their counter changed) and forgets players who have left.</summary>
        internal static void Update()
        {
            if (_soundPlayer != null && Time.time >= _soundAt) PlaySound();
            if (Time.time < _nextLook) return;
            _nextLook = Time.time + 0.15f;
            Player local = Player.m_localPlayer;
            if (local == null) { Poses.Clear(); return; }
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == local) continue;
                ZDO zdo = other.GetComponent<ZNetView>()?.GetZDO();
                if (zdo == null) continue;
                Pose pose = PoseOf(other);
                int value = zdo.GetInt(Key, 0);
                bool changed = pose.Seen >= 0 && value != pose.Seen;
                pose.Seen = value;
                if (changed && Plugin.ApplyAnimation.Value) Play(pose, palms: false);
                int palms = zdo.GetInt(PalmsKey, 0);
                bool rubbed = pose.SeenPalms >= 0 && palms != pose.SeenPalms;
                pose.SeenPalms = palms;
                if (rubbed && Plugin.ApplyAnimation.Value) Play(pose, palms: true);
            }
            List<Player> gone = null;
            foreach (Player p in Poses.Keys) if (p == null) (gone ?? (gone = new List<Player>())).Add(p);
            if (gone != null) foreach (Player p in gone) Poses.Remove(p);
        }

        internal static void Reset() => Poses.Clear();

        /// <summary>How much of the pose is in force, 0 to 1: it eases in as the weapon is lifted and out as it is let go.</summary>
        private static float Weight(Pose pose)
        {
            float t = Time.time - pose.Start;
            float release = ReleaseOf(pose), total = LiftTime + SweepTime + release;
            if (t < 0f || t > total) return 0f;
            if (t < LiftTime) return Ease(t / LiftTime);
            if (t > total - release) return 1f - Ease((t - (total - release)) / release);
            return 1f;
        }

        /// <summary>
        /// The elbows are steered only once the arms have bent a little. With the arm hanging nearly straight (as the pose begins and
        /// ends) the way an elbow can point is barely decided, and steering it even slightly flicked it 10 to 20 cm sideways in one step.
        /// </summary>
        private static float HintWeight(float weight) => Ease((weight - 0.2f) / 0.6f);

        private static void Goals(Animator animator, float weight)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, HintWeight(weight));
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, HintWeight(weight));
        }

        /// <summary>
        /// Whether the weapon in the right hand has a blade in each hand (Skoll and Hati, the Berserkir axes; their attacks are "dual_knives"
        /// and "dualaxes"). Such a weapon is one model bent to the player's skeleton, not a model on the hand. Another player's weapon is
        /// known from the item their equipment shows, since their items aren't sent.
        /// </summary>
        private static bool IsDual(VisEquipment vis, GameObject model, ItemDrop.ItemData held)
        {
            if (model == null || model.GetComponentInChildren<SkinnedMeshRenderer>() == null) return false;
            ItemDrop.ItemData item = held;
            if (item == null && vis != null && ObjectDB.instance != null)
                item = ObjectDB.instance.GetItemPrefab(VisRightItem(vis))?.GetComponent<ItemDrop>()?.m_itemData;
            string attack = item?.m_shared?.m_attack?.m_attackAnimation ?? "";
            return attack.StartsWith("dual");
        }

        private static readonly AccessTools.FieldRef<VisEquipment, int> VisRightItem = AccessTools.FieldRefAccess<VisEquipment, int>("m_rightItem");

        /// <summary>The thumb side of a fist, in the hand's own space (where a blade held point-up comes out), from the finger bones.</summary>
        internal static Vector3 ThumbSide(Animator animator, Transform hand, bool isRight)
        {
            Transform index = animator.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform little = animator.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (index == null || little == null) return Vector3.up;
            return Quaternion.Inverse(hand.rotation) * (index.position - little.position).normalized;
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), "OnAnimatorIK")]
        private static class CharacterAnimEvent_OnAnimatorIK
        {
            private static void Postfix(CharacterAnimEvent __instance)
            {
                if (Poses.Count == 0 || !(AnimCharacter(__instance) is Player who) || !Poses.TryGetValue(who, out Pose pose)) return;
                var animator = AnimatorField?.GetValue(__instance) as Animator;
                if (animator == null) return;

                float weight = Weight(pose);
                GameObject model = OilGlow.RightHandModelOf(AnimVisField?.GetValue(__instance) as VisEquipment);
                OilGlow glow = model != null ? model.GetComponent<OilGlow>() : null;
                bool busy = who.InAttack() || who.IsDead() || who.IsSwimming() || who.IsAttached();
                ItemDrop.ItemData held = OilGlow.ItemIn(who, OilGlow.Slot.RightHand);
                bool gone = pose.Palms ? held != null && !OilHands.IsFist(held) : glow == null;   // (a weapon taken up, or the weapon put away; claws are fine)
                if (weight > 0f && (gone || busy))
                {
                    pose.Start = -100f;   // (they swung, or the weapon is gone: let go at once)
                    weight = 0f;
                }
                if (weight <= 0f)
                {
                    if (pose.HandsOn) Goals(animator, 0f);
                    pose.HandsOn = false;
                    pose.Right = default;
                    pose.Left = default;
                    pose.Planned = false;
                    return;
                }

                // Everything is placed in the body's own frame (the way it faces, level), from the chest.
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                Vector3 anchor = chest != null ? chest.position : who.transform.position + Vector3.up * 1.3f;
                Quaternion frame = Quaternion.Euler(0f, who.transform.eulerAngles.y, 0f);
                pose.Facing = frame;

                if (pose.Palms)
                {
                    // both hands up in front of the chest, a hand's width apart, elbows low and out; the head looks down between them
                    Vector3 r = anchor + frame * new Vector3(0.12f, -0.2f, 0.33f), l = anchor + frame * new Vector3(-0.12f, -0.2f, 0.33f);
                    animator.SetIKPosition(AvatarIKGoal.RightHand, r);
                    animator.SetIKHintPosition(AvatarIKHint.RightElbow, r + frame * new Vector3(0.22f, -0.32f, -0.16f));
                    animator.SetIKPosition(AvatarIKGoal.LeftHand, l);
                    animator.SetIKHintPosition(AvatarIKHint.LeftElbow, l + frame * new Vector3(-0.22f, -0.32f, -0.16f));
                    Goals(animator, weight);
                    FadeGameLook(__instance, animator, weight);
                    pose.LookAt = (r + l) * 0.5f;
                    pose.HandsOn = true;
                    return;
                }
                if (pose.Dual < 0) pose.Dual = IsDual(AnimVisField?.GetValue(__instance) as VisEquipment, model, held) ? 1 : 0;
                if (pose.Dual == 1)
                {
                    DualGoals(animator, pose, anchor, frame, weight);
                    FadeGameLook(__instance, animator, weight);
                    pose.HandsOn = true;
                    return;
                }
                glow.Blade(out _, out float length);
                length = Mathf.Clamp(length, 0.35f, 1.6f);

                Vector3 right = anchor + frame * new Vector3(0.22f, -0.12f, 0.30f);                  // the right hand, out in front of the chest
                Vector3 blade = frame * new Vector3(-0.9f, 0.4f, 0.2f).normalized;                   // the blade: across to the left, rising
                pose.Blade = blade;

                // the left hand slides from just above the hilt to a good way up the blade (as far as an arm reaches), under it
                float sweep = Ease((Time.time - pose.Start - LiftTime) / SweepTime);
                Vector3 hilt = right + blade * 0.14f;
                Vector3 tip = right + blade * Mathf.Min(length * 0.75f, 0.66f);
                Vector3 left = Vector3.Lerp(hilt, tip, sweep) + frame * new Vector3(0f, -0.07f, -0.08f);

                animator.SetIKPosition(AvatarIKGoal.RightHand, right);
                animator.SetIKHintPosition(AvatarIKHint.RightElbow, right + frame * new Vector3(0.32f, -0.26f, -0.12f));
                animator.SetIKPosition(AvatarIKGoal.LeftHand, left);
                animator.SetIKHintPosition(AvatarIKHint.LeftElbow, left + frame * new Vector3(-0.14f, -0.32f, -0.2f));
                Goals(animator, weight);
                FadeGameLook(__instance, animator, weight);
                pose.LookAt = left;
                pose.HandsOn = true;
            }

            // The game's own block with a weapon in each hand, read from the bones (the same for Skoll and Hati and the Berserkir axes),
            // in the body's frame from the chest: both fists up in front of the face, elbows out, the blades rising toward each other and
            // crossed near their tips. Poses of our own (an arm held out, or low across the chest) looked strained at the shoulder.
            private static readonly Vector3 BlockRight = new Vector3(0.227f, 0.205f, 0.576f), BlockLeft = new Vector3(-0.185f, 0.214f, 0.580f);
            // Where each elbow is pointed, from its own hand: out to the side, down and back, as in that block (a little further out). Set from
            // the chest instead, the Berserkir axes' stance (the left shoulder held elsewhere) put the point inside the shoulder, and the
            // left elbow bent in toward the body.
            private static readonly Vector3 RightElbowOut = new Vector3(0.2f, -0.1f, -0.2f), LeftElbowOut = new Vector3(-0.25f, -0.15f, -0.15f);
            internal static readonly Quaternion BlockRightTurn = Quaternion.Normalize(new Quaternion(-0.766f, 0.234f, 0.338f, -0.492f));
            internal static readonly Quaternion BlockLeftTurn = Quaternion.Normalize(new Quaternion(0.670f, 0.250f, 0.305f, 0.627f));
            private const float BlockChestYaw = 10f;   // the chest bone's turn from the player's facing in that block
            private static readonly Vector3 Lower = new Vector3(0f, -0.08f, -0.06f);      // (a little below the guard and nearer, so the blades are in view and the arms bent)
            // where the left fist starts: in toward the right one, so the blades cross low. Further (beside the right fist) was all of the
            // left arm's reach in the axes' stance: the arm went straight and the elbow flicked inward.
            private static readonly Vector3 LeftIn = new Vector3(0.14f, -0.02f, -0.06f);

            /// <summary>
            /// A blade in each hand (Skoll and Hati, the Berserkir axes): the game's own dual block, a little lower, the right fist held still
            /// and the left one drawn from beside it back out to its place in the guard, so the crossing slides from the hilts to the tips: the
            /// way one knife is stropped on another, each wiped by the other. The head follows the crossing.
            /// </summary>
            private static void DualGoals(Animator animator, Pose pose, Vector3 anchor, Quaternion frame, float weight)
            {
                // The dual stance twists the chest about 30 degrees right of the way the player faces (the right shoulder back); the game
                // squares it up to block, this pose doesn't. Placed from the player's facing, the right arm had to reach from the pulled-back
                // shoulder, stretched and across the body: so it is placed from the chest's own facing, worked out once as the oiling begins.
                if (!pose.Squared)
                {
                    Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                    float twist = chest != null ? Mathf.DeltaAngle(BlockChestYaw, (Quaternion.Inverse(frame) * chest.rotation).eulerAngles.y) : 0f;
                    pose.Twist = Mathf.Clamp(twist, -60f, 60f);
                    pose.DualFacing = frame * Quaternion.Euler(0f, pose.Twist, 0f);
                    pose.Squared = true;
                }
                frame = pose.DualFacing;
                float sweep = Ease((Time.time - pose.Start - LiftTime) / SweepTime);
                Vector3 right = anchor + frame * (BlockRight + Lower);
                Vector3 left = anchor + frame * (BlockLeft + Lower + LeftIn * (1f - sweep));

                animator.SetIKPosition(AvatarIKGoal.RightHand, right);
                animator.SetIKHintPosition(AvatarIKHint.RightElbow, right + frame * RightElbowOut);
                animator.SetIKPosition(AvatarIKGoal.LeftHand, left);
                animator.SetIKHintPosition(AvatarIKHint.LeftElbow, left + frame * LeftElbowOut);
                Goals(animator, weight);
                pose.LookAt = (right + left) * 0.5f + frame * new Vector3(0f, 0.12f, 0f);   // (about where the blades cross)
            }

            /// <summary>
            /// The game's own look (the head, and the body with it, turned toward where the camera aims) fades out as the pose comes in and back
            /// as it goes, at the game's own target: only the weights are changed, so there is nothing to jump when it hands back. The head is
            /// turned toward the hand separately, on top (see TurnHead). The body's turn is let go so the chest the hands hang from stays put.
            /// </summary>
            private static void FadeGameLook(CharacterAnimEvent e, Animator animator, float weight)
            {
                if (LookAtWeightField == null || BodyLookField == null || HeadLookField == null || EyeLookField == null || LookClampField == null) return;
                animator.SetLookAtWeight(
                    Mathf.Lerp((float)LookAtWeightField.GetValue(e), 0f, weight),
                    Mathf.Lerp((float)BodyLookField.GetValue(e), 0f, weight),
                    (float)HeadLookField.GetValue(e),
                    (float)EyeLookField.GetValue(e),
                    (float)LookClampField.GetValue(e));
            }
        }

        // After the animator has done everything (IK too), the right hand is turned so the blade lies along the wanted line. The weapon sits
        // on the hand, so turning the hand turns it; the arm already reaches the right place. The turn is worked out from the animator's own
        // rotation of the bone (see Turned).
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomLateUpdate))]
        private static class CharacterAnimEvent_CustomLateUpdate
        {
            private static void Postfix(CharacterAnimEvent __instance)
            {
                if (Poses.Count == 0 || !(AnimCharacter(__instance) is Player who) || !Poses.TryGetValue(who, out Pose pose) || !pose.HandsOn) return;
                float weight = Weight(pose);
                if (weight <= 0f) return;
                var animator = AnimatorField?.GetValue(__instance) as Animator;
                if (animator == null) return;
                if (pose.Palms)
                {
                    if (TurnPalms(animator, pose, weight)) TurnHead(animator, pose, weight);
                    return;
                }
                if (pose.Dual == 1)
                {
                    if (TurnDual(animator, pose, weight)) TurnHead(animator, pose, weight);
                    return;
                }
                GameObject model = OilGlow.RightHandModelOf(AnimVisField?.GetValue(__instance) as VisEquipment);
                OilGlow glow = model != null ? model.GetComponent<OilGlow>() : null;

                Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (right != null && glow != null && TurnRight(right, model.transform, glow, pose, weight)) TurnHead(animator, pose, weight);
            }

            /// <summary>
            /// The head (and neck, half each) turned toward the left hand as it works, by the pose's weight, on top of whatever the animator
            /// gave it: nothing at the start and end, so it can't jump, and the same from every camera angle (the game's own look, which
            /// follows the camera, is faded out meanwhile). The turn is from straight ahead to the hand, which is always in front.
            /// </summary>
            private static void TurnHead(Animator animator, Pose pose, float weight)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head == null) return;
                Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                Vector3 ahead = pose.Facing * Vector3.forward;
                Vector3 toHand = pose.LookAt - head.position;
                if (toHand.sqrMagnitude < 1e-4f) return;
                Quaternion turn = Quaternion.FromToRotation(ahead, toHand.normalized);
                float amount = weight * 0.85f;
                if (neck != null)
                {
                    neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, amount * 0.5f) * neck.rotation;
                    head.rotation = Quaternion.Slerp(Quaternion.identity, turn, amount * 0.5f) * head.rotation;
                }
                else head.rotation = Quaternion.Slerp(Quaternion.identity, turn, amount) * head.rotation;
            }

            /// <summary>
            /// Both hands turned palm up, fingers forward and a little outward, as the weapon hand is: on the frames the animation steps,
            /// along a path planned once from where the animator had each hand (see TurnRight). Which way a palm faces is read from the
            /// finger bones; false (nothing done) on a frame the animation didn't step, or for a body without them.
            /// </summary>
            private static bool TurnPalms(Animator animator, Pose pose, float weight)
            {
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand), left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (right == null || left == null) return false;
                bool stepped = !pose.Right.Have || Quaternion.Angle(right.rotation, pose.Right.Set) >= 0.05f;
                if (!stepped && pose.Planned) return false;
                Quaternion basisR = pose.Right.Basis(right.rotation), basisL = pose.Left.Basis(left.rotation);
                if (!pose.Planned)
                {
                    Quaternion toBody = Quaternion.Inverse(pose.Facing);
                    if (!PalmUp(animator, right, basisR, true, pose.Facing, out Quaternion heldR)
                        || !PalmUp(animator, left, basisL, false, pose.Facing, out Quaternion heldL)) return true;   // (no fingers: positions only)
                    pose.Rest = toBody * basisR;
                    pose.Held = toBody * heldR;
                    pose.RestLeft = toBody * basisL;
                    pose.HeldLeft = toBody * heldL;
                    pose.Planned = true;
                }
                right.rotation = pose.Facing * Quaternion.Slerp(pose.Rest, pose.Held, weight);
                left.rotation = pose.Facing * Quaternion.Slerp(pose.RestLeft, pose.HeldLeft, weight);
                pose.Right.Remember(basisR, right.rotation);
                pose.Left.Remember(basisL, left.rotation);
                return true;
            }

            /// <summary>
            /// The world rotation that puts a hand palm up (tipped toward the face), fingers forward: worked out from where its finger bones
            /// are on it (with the hand at its animator rotation), so it doesn't depend on how the rig's hand bone happens to be turned.
            /// </summary>
            private static bool PalmUp(Animator animator, Transform hand, Quaternion handRotation, bool isRight, Quaternion facing, out Quaternion held)
            {
                held = handRotation;
                Transform middle = animator.GetBoneTransform(isRight ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                Transform index = animator.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                Transform little = animator.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                if (middle == null || index == null || little == null) return false;
                // in the hand's own frame (from world offsets, undone by the rotation the hand has right now)
                Quaternion undo = Quaternion.Inverse(hand.rotation);
                Vector3 fingers = (undo * (middle.position - hand.position)).normalized;
                Vector3 across = (undo * (index.position - little.position)).normalized;
                // the palm's face: with the fingers forward and the palm down, the index finger is on the thumb's side (left for a right hand)
                Vector3 palm = Vector3.Cross(fingers, across) * (isRight ? 1f : -1f);
                if (fingers.sqrMagnitude < 0.5f || palm.sqrMagnitude < 1e-4f) return false;
                Vector3 fingersTo = facing * new Vector3(isRight ? 0.25f : -0.25f, 0.2f, 1f).normalized;
                Vector3 palmTo = facing * new Vector3(0f, 1f, -0.45f).normalized;
                held = Quaternion.LookRotation(fingersTo, palmTo) * Quaternion.Inverse(Quaternion.LookRotation(fingers, palm.normalized));
                return true;
            }

            /// <summary>
            /// Both hands turned to the game's own dual block (BlockRightTurn, BlockLeftTurn), from wherever the animator had them when the
            /// oiling began: on the frames the animation steps. False on a frame the animation didn't step.
            /// </summary>
            private static bool TurnDual(Animator animator, Pose pose, float weight)
            {
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand), left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (right == null || left == null) return false;
                bool stepped = !pose.Right.Have || Quaternion.Angle(right.rotation, pose.Right.Set) >= 0.05f;
                if (!stepped && pose.Planned) return false;
                Quaternion basisR = pose.Right.Basis(right.rotation), basisL = pose.Left.Basis(left.rotation);
                if (!pose.Planned)
                {
                    Quaternion toChest = Quaternion.Inverse(pose.DualFacing);
                    pose.Rest = toChest * basisR;
                    pose.RestLeft = toChest * basisL;
                    pose.Planned = true;
                }
                // The torso squared up, as the game does to block: the stance's twist undone, half at the spine and half at the chest, which
                // carries the shoulders, the arms placed in the chest's frame and the hands round to the front. (Only on the frames the
                // animation stepped, like the hands: the bones keep what they had otherwise, and turning them again would add up.)
                Quaternion untwist = Quaternion.AngleAxis(-pose.Twist * weight, Vector3.up);
                Quaternion half = Quaternion.AngleAxis(-pose.Twist * weight * 0.5f, Vector3.up);
                Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine), chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (spine != null && chestBone != null)
                {
                    spine.rotation = half * spine.rotation;
                    chestBone.rotation = half * chestBone.rotation;
                }
                else untwist = Quaternion.identity;
                // Letting go, the hands blend back toward where the game holds them now, not where it held them as the oiling began: the
                // Berserkir axes' stance keeps moving the hands, and blending to the old turn left them ~10 degrees off, a snap at the end.
                Quaternion restR = pose.Rest, restL = pose.RestLeft;
                if (Time.time - pose.Start > LiftTime + SweepTime)
                {
                    Quaternion toChest = Quaternion.Inverse(pose.DualFacing);
                    restR = Quaternion.Slerp(pose.Rest, toChest * basisR, 1f - weight);
                    restL = Quaternion.Slerp(pose.RestLeft, toChest * basisL, 1f - weight);
                }
                right.rotation = untwist * pose.DualFacing * Quaternion.Slerp(restR, CharacterAnimEvent_OnAnimatorIK.BlockRightTurn, weight);
                left.rotation = untwist * pose.DualFacing * Quaternion.Slerp(restL, CharacterAnimEvent_OnAnimatorIK.BlockLeftTurn, weight);
                pose.LookAt = (right.position + left.position) * 0.5f + Vector3.up * 0.12f;   // (where the blades cross, now the body has turned)
                pose.Right.Remember(basisR, right.rotation);
                pose.Left.Remember(basisL, left.rotation);
                return true;
            }

            /// <summary>Turns the hand; false on a frame the animation didn't step (nothing was done, the bones keep what they had).</summary>
            private static bool TurnRight(Transform hand, Transform model, OilGlow glow, Pose pose, float weight)
            {
                Quaternion current = hand.rotation, basis = pose.Right.Basis(current);
                Quaternion toBody = Quaternion.Inverse(pose.Facing);
                // The game moves a character's bones only when its animation steps (every second or third frame drawn), and the arm
                // stays put in between. The hand is turned on those same frames only, so it moves with the arm, not ahead of it.
                bool stepped = !pose.Right.Have || Quaternion.Angle(current, pose.Right.Set) >= 0.05f;
                if (!stepped && pose.Planned) return false;

                // The whole turn is worked out once, on the first frame, from where the animator had the hand then; after that the
                // hand follows that fixed path (in the body's frame) and never looks at the animator's hand again until the very end.
                // Working it out afresh every frame from the animator's hand (which hangs the weapon about opposite the pose, and moves
                // with every step) let the way round flip from one frame to the next: the weapon spun as the pose began and ended.
                // The path goes by way of "forward, a little raised", so neither half is near half a turn.
                if (!pose.Planned)
                {
                    glow.Blade(out Vector3 axis, out _);
                    Vector3 now = toBody * ((basis * Quaternion.Inverse(current)) * model.TransformDirection(axis));
                    Vector3 via = new Vector3(0f, 0.2f, 1f).normalized;
                    pose.Rest = toBody * basis;
                    pose.Mid = Quaternion.FromToRotation(now, via) * pose.Rest;
                    pose.Held = Quaternion.FromToRotation(via, toBody * pose.Blade) * pose.Mid;
                    pose.Planned = true;
                }
                Quaternion ours = weight < 0.5f
                    ? Quaternion.Slerp(pose.Rest, pose.Mid, weight * 2f)
                    : Quaternion.Slerp(pose.Mid, pose.Held, (weight - 0.5f) * 2f);   // (the weight is eased already)
                ours = pose.Facing * ours;
                // At the very start and end the hand is near where it rested, and hands over to the animator's own hand there.
                hand.rotation = ours;
                pose.Right.Remember(basis, hand.rotation);
                return true;
            }
        }
    }
}
