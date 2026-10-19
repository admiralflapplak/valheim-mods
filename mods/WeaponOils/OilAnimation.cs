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
        private static float ReleaseOf(Pose pose) => pose.Sledge ? ReleaseTime * 1.7f : pose.Dual == 1 ? ReleaseTime * 1.4f : ReleaseTime;

        // A sledge comes up off the shoulders and down in front, a long way: its lift is longer too (in 0.5 s it snapped).
        private static float LiftOf(Pose pose) => pose.Sledge ? LiftTime * 1.7f : LiftTime;

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
            public int Upright = -1;                       // a greatsword, held as in its block: -1 not looked at yet this oiling, 0 no, 1 yes
            public bool ShoulderPlanned;                   // (when Upright) the collarbones' turns have been worked out for this oiling
            public Quaternion ShoulderRest, ShoulderLifted;   // (when Upright) the left one's local rotation as the stance had it, and as in the block
            public Quaternion RightShoulderRest, RightShoulderLifted;
            public bool LeftPlanned;                       // (when Upright) RestLeft has been taken for this oiling
            public bool Sledge;                            // a sledge: stood up off the shoulders before it is brought down in front
            public float Choke;                            // a battleaxe, sledge or atgeir: how far (m) it slides through the fist, the hand nearer its head
            public bool Atgeir;                            // an atgeir: held pointing forward, like a spear under the arm
            public float SledgeAhead;                      // (when Sledge) how far its head is from the hand (m)
            public Transform GripModel;                    // (when Choke) the weapon model slid, and where it sat on the hand before
            public Vector3 GripBase;
            public bool Greatsword;                        // (when Upright) a greatsword (Krom), its blade leaning away and braced at head height
            public Vector3 TwoBlade;                       // (when Upright) the way its blade or haft is held, in the body's frame
            public Quaternion TwoTurn;                     // (when Upright) the right hand's turn holding it so
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
            RestoreGrip(pose);
            pose.Dual = -1;
            pose.Upright = -1;
            pose.ShoulderPlanned = false;
            pose.LeftPlanned = false;
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

        internal static void Reset()
        {
            foreach (Pose pose in Poses.Values) RestoreGrip(pose);
            Poses.Clear();
        }

        /// <summary>A weapon slid through the fist for oiling (SlideGrip) put back where the game had it on the hand.</summary>
        private static void RestoreGrip(Pose pose)
        {
            if (pose.GripModel != null) pose.GripModel.localPosition = pose.GripBase;
            pose.GripModel = null;
        }

        /// <summary>How much of the pose is in force, 0 to 1: it eases in as the weapon is lifted and out as it is let go.</summary>
        private static float Weight(Pose pose)
        {
            float t = Time.time - pose.Start;
            float lift = LiftOf(pose), release = ReleaseOf(pose), total = lift + SweepTime + release;
            if (t < 0f || t > total) return 0f;
            if (t < lift) return Ease(t / lift);
            if (t > total - release) return 1f - Ease((t - (total - release)) / release);
            return 1f;
        }

        /// <summary>
        /// The elbows are steered only once the arms have bent a little. With the arm hanging nearly straight (as the pose begins and
        /// ends) the way an elbow can point is barely decided, and steering it even slightly flicked it 10 to 20 cm sideways in one step.
        /// </summary>
        private static float HintWeight(float weight) => Ease((weight - 0.2f) / 0.6f);

        /// <summary>
        /// How far a two-handed pose's hands have gone toward where they are placed, 0 to 1. A sledge's stance rests the haft across the
        /// shoulders behind the neck, at the height of the head: turned from there it went through the head (2 to 7 cm from its middle),
        /// and drawn forward first it was dragged through it. So it is stood up first, the hand still on the shoulder (its turn goes by
        /// way of upright: TurnRight), and the hands come only as it leans into the guard; letting go, the same backward.
        /// </summary>
        private static float ReachWeight(Pose pose, float weight) => pose.Upright == 0 && pose.Sledge ? Ease((weight - 0.25f) / 0.75f) : weight;

        private static void Goals(Animator animator, float right, float left)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, right);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, HintWeight(right));
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, left);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, HintWeight(left));
        }

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
            string attack = ItemOf(vis, held)?.m_shared?.m_attack?.m_attackAnimation ?? "";
            return attack.StartsWith("dual");
        }

        /// <summary>
        /// Whether the weapon in the right hand is held in both hands: a greatsword (Krom), battleaxe, sledge or atgeir. Krom's stance
        /// holds its blade (nearly 2 m) over the left shoulder; laid across the chest like a sword it swept out in front and the left arm
        /// went into the body, and a sledge's head went through the head. They are held as in the game's two-handed block instead.
        /// </summary>
        private static bool IsUpright(ItemDrop.ItemData.AnimationState? state)
        {
            switch (state)
            {
                // the two-handed stances share one block (read from the bones: the same hands, turns and haft to the millimetre), but only
                // a greatsword's blade starts at the hand: the others' heads are 1.2 to 2.5 m away, out of the left hand's reach in it
                case ItemDrop.ItemData.AnimationState.Greatsword:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The item in the right hand: the local player's own, or for another player the one their equipment shows (their items aren't sent).</summary>
        private static ItemDrop.ItemData ItemOf(VisEquipment vis, ItemDrop.ItemData held)
        {
            if (held != null || vis == null || ObjectDB.instance == null) return held;
            return ObjectDB.instance.GetItemPrefab(VisRightItem(vis))?.GetComponent<ItemDrop>()?.m_itemData;
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
                    RestoreGrip(pose);
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
                glow.Blade(out _, out float fullLength);
                if (pose.Upright < 0)
                {
                    ItemDrop.ItemData.AnimationState? state = ItemOf(AnimVisField?.GetValue(__instance) as VisEquipment, held)?.m_shared?.m_animationState;
                    pose.Upright = IsUpright(state) ? 1 : 0;
                    pose.Sledge = state == ItemDrop.ItemData.AnimationState.TwoHandedClub;
                    // A battleaxe, sledge or atgeir slides through the fist toward its head (see SlideGrip): a battleaxe and an atgeir are
                    // then oiled like a sword (an atgeir pointing forward), a sledge is stood leaning out in front, its head touched
                    bool haft = state == ItemDrop.ItemData.AnimationState.TwoHandedAxe || state == ItemDrop.ItemData.AnimationState.TwoHandedClub
                        || state == ItemDrop.ItemData.AnimationState.Atgeir;
                    // (from how far it reaches ahead of the hand, not its length: the hand is in the middle of a battleaxe or a sledge). A
                    // battleaxe's hand stops a little short of the head; an atgeir slides at most a metre, as the shaft behind the hand
                    // (a metre already) went down to the feet
                    pose.Atgeir = state == ItemDrop.ItemData.AnimationState.Atgeir;
                    float ahead = glow.Ahead();
                    // (a sledge is stood on its handle in front, head up: its head, a metre from the hand, slides down to just above it,
                    // at chest height, the butt just above the ground)
                    pose.Choke = !haft ? 0f
                        : pose.Atgeir ? Mathf.Clamp(ahead - 0.95f, 0f, 1f)
                        : pose.Sledge ? Mathf.Clamp(ahead - 0.6f, 0f, 1f)
                        : Mathf.Clamp(ahead - 0.6f, 0f, 1f);
                    pose.SledgeAhead = ahead - pose.Choke;
                    pose.Greatsword = state == ItemDrop.ItemData.AnimationState.Greatsword;
                    // Krom's blade leans away from the body; a haft (battleaxe, atgeir, sledge) is held nearer, by the left hand
                    pose.TwoBlade = (pose.Greatsword ? GreatswordBlade : HaftBlade).normalized;
                    pose.TwoTurn = Quaternion.FromToRotation(GreatswordBlockBlade, pose.TwoBlade) * GreatswordBlockTurn;
                }
                bool upright = pose.Upright == 1;
                float reachW = ReachWeight(pose, weight);
                float length = Mathf.Clamp(pose.Choke > 0f ? glow.Ahead() - pose.Choke : fullLength, 0.35f, 2f);   // (what is left beyond the hand)

                // the right hand, out in front of the chest (a two-handed weapon's where the game's own two-handed block holds it)
                Vector3 right = anchor + frame * (upright ? GreatswordRight
                    : pose.Sledge ? SledgeHand   // (a sledge stood on its handle in front, at the waist)
                    : new Vector3(0.22f, -0.12f, 0.30f));
                // the blade: across to the left, rising (a two-handed weapon's as in its block: up and across to the left)
                Vector3 blade = frame * (upright ? pose.TwoBlade
                    : pose.Atgeir ? new Vector3(-0.5f, 0.3f, 0.8f).normalized   // (an atgeir forward, its shaft back past the right hip)
                    : pose.Sledge ? SledgeFront
                    : new Vector3(-0.9f, 0.4f, 0.2f).normalized);
                pose.Blade = blade;

                // the left hand slides from just above the hilt to a good way up the blade (as far as an arm reaches), under it (a
                // greatsword's from low on the blade up to where its block braces the blade, at head height; a haft's as far up it)
                float sweep = Ease((Time.time - pose.Start - LiftOf(pose)) / SweepTime);
                Vector3 left;
                if (upright)
                {
                    Vector3 beside = frame * new Vector3(-0.06f, 0f, 0.06f);
                    Vector3 top = pose.Greatsword ? anchor + frame * GreatswordLeft : right + blade * 0.5f + beside;
                    left = Vector3.Lerp(right + blade * 0.18f + beside, top, sweep);
                }
                else
                {
                    Vector3 hilt = right + blade * (pose.Atgeir ? 0.32f : 0.14f);   // (an atgeir's stroke starts further up its shaft)
                    Vector3 tip = right + blade * Mathf.Min(length * 0.75f, 0.66f);
                    left = Vector3.Lerp(hilt, tip, sweep) + frame * new Vector3(0f, -0.07f, -0.08f);
                    // a sledge's: palm down across the top of its head, from the left to the right
                    if (pose.Sledge)
                    {
                        // on the side of the hammer's head nearest it (its left), rubbing a little up and down (from where the right hand
                        // is placed: SledgeHand is within the arm's reach, so the hand gets there; the bones read here are the stance's).
                        // Reaching round to its front face, the arm wrapped round the head.
                        Vector3 head = right + blade * (pose.SledgeAhead - 0.1f);
                        left = head + frame * new Vector3(-0.13f, Mathf.Sin(sweep * Mathf.PI * 2f) * 0.04f, -0.02f);
                    }
                }

                animator.SetIKPosition(AvatarIKGoal.RightHand, right);
                animator.SetIKHintPosition(AvatarIKHint.RightElbow, right + frame * (upright ? new Vector3(0.2f, -0.12f, -0.2f)
                    : pose.Sledge ? new Vector3(0.25f, -0.12f, -0.12f) : new Vector3(0.32f, -0.26f, -0.12f)));
                animator.SetIKPosition(AvatarIKGoal.LeftHand, left);
                animator.SetIKHintPosition(AvatarIKHint.LeftElbow, left + frame * (upright ? new Vector3(-0.25f, -0.2f, -0.05f)
                    : pose.Sledge ? new Vector3(-0.28f, -0.22f, -0.08f) : new Vector3(-0.14f, -0.32f, -0.2f)));
                if (upright) LiftShoulders(animator, pose, anchor, frame, reachW);
                Goals(animator, reachW);
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

            // The game's own greatsword block (Krom), read from the bones, in the body's frame from the chest: the right fist on the grip in
            // front of the right shoulder, the blade up and across to the left, the left hand braced on it at head height. The chest is
            // turned as in the greatsword's idle stance, so nothing needs squaring up. Held upright by a pose of our own, the right arm
            // bent into the body.
            // (the right fist 8 cm nearer than the block's: there, from the stance's shoulder, the right arm was straight)
            private static readonly Vector3 GreatswordRight = new Vector3(0.32f, 0.10f, 0.31f), GreatswordLeft = new Vector3(-0.12f, 0.48f, 0.47f);
            // The block holds the blade leaning back toward the body (-0.52, 0.82, -0.22); oiling, it leans away from it a little instead,
            // the right hand turned outward by as much.
            private static readonly Vector3 GreatswordBlockBlade = new Vector3(-0.52f, 0.82f, -0.22f), GreatswordBlade = new Vector3(-0.5f, 0.82f, 0.25f);
            // a battleaxe's, atgeir's or sledge's haft: nearer the body than Krom's blade, so it is nearer the left hand working on it
            private static readonly Vector3 HaftBlade = new Vector3(-0.52f, 0.82f, 0.02f);
            // the way a sledge's haft is stood up first, the head of the sledge raised off the shoulders: upright, a little right and back
            internal static readonly Vector3 SledgeUp = new Vector3(0.1f, 1f, -0.1f).normalized;
            // and then brought down in front, the haft leaning out from the body, the head across the body at chest height, where the
            // left hand rubs its front face
            // (the hand relaxed in front of the body, the haft leaning some 35 degrees out from upright, a little in toward the middle)
            private static readonly Vector3 SledgeHand = new Vector3(0.15f, -0.15f, 0.28f), SledgeFront = new Vector3(-0.15f, 0.8f, 0.55f).normalized;
            // where the block has the shoulders: the left raised and drawn out (the stance's: -0.03, 0.225, 0.22), the right brought
            // forward (the stance's: 0.13, 0.236, -0.18)
            private static readonly Vector3 GreatswordLeftShoulder = new Vector3(-0.105f, 0.284f, 0.165f), GreatswordRightShoulder = new Vector3(0.235f, 0.226f, -0.106f);

            /// <summary>
            /// The collarbones turned as the greatsword block turns them, the left shoulder raised and drawn out and the right brought
            /// forward, so the arms reach from there: the arms' IK moves only the arms, and from the stance's shoulders the left arm looked
            /// stiff (the shoulder locked) and the right went straight. Worked out once as the oiling begins (from where the stance has the
            /// collarbones then), set before the IK pass, by the weight.
            /// </summary>
            private static void LiftShoulders(Animator animator, Pose pose, Vector3 anchor, Quaternion frame, float weight)
            {
                Transform collarL = animator.GetBoneTransform(HumanBodyBones.LeftShoulder), upperL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform collarR = animator.GetBoneTransform(HumanBodyBones.RightShoulder), upperR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (collarL == null || upperL == null || collarL.parent == null || collarR == null || upperR == null || collarR.parent == null) return;
                if (!pose.ShoulderPlanned)
                {
                    Lifted(collarL, upperL, anchor + frame * GreatswordLeftShoulder, out pose.ShoulderRest, out pose.ShoulderLifted);
                    Lifted(collarR, upperR, anchor + frame * GreatswordRightShoulder, out pose.RightShoulderRest, out pose.RightShoulderLifted);
                    pose.ShoulderPlanned = true;
                }
                animator.SetBoneLocalRotation(HumanBodyBones.LeftShoulder, Quaternion.Slerp(pose.ShoulderRest, pose.ShoulderLifted, weight));
                animator.SetBoneLocalRotation(HumanBodyBones.RightShoulder, Quaternion.Slerp(pose.RightShoulderRest, pose.RightShoulderLifted, weight));
            }

            /// <summary>A collarbone's local rotation now, and turned so the shoulder joint at its end comes to a spot in the world.</summary>
            private static void Lifted(Transform collar, Transform upper, Vector3 to, out Quaternion rest, out Quaternion lifted)
            {
                Quaternion turn = Quaternion.FromToRotation(upper.position - collar.position, to - collar.position);   // in the world
                Quaternion parent = collar.parent.rotation;
                rest = collar.localRotation;
                lifted = Quaternion.Inverse(parent) * turn * parent * collar.localRotation;
            }

            private static readonly Quaternion GreatswordBlockTurn = Quaternion.Normalize(new Quaternion(-0.458f, 0.355f, 0.335f, -0.744f));

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

                if (pose.Choke > 0f && glow != null) SlideGrip(model.transform, glow, pose, ReachWeight(pose, weight));
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (right != null && glow != null && TurnRight(animator, right, model.transform, glow, pose, weight)) TurnHead(animator, pose, weight);
            }

            /// <summary>
            /// A battleaxe, sledge or atgeir slid through the fist toward its head, by "amount" of pose.Choke: the hand ends just below the
            /// head, so the left hand can reach the part being oiled (the heads are 1.2 to 2.5 m from where the game has the hand). The
            /// model is moved on the hand, along its own length; where it sat is kept and put back when the pose ends (RestoreGrip).
            /// </summary>
            private static void SlideGrip(Transform model, OilGlow glow, Pose pose, float amount)
            {
                if (pose.GripModel != model)
                {
                    RestoreGrip(pose);
                    pose.GripModel = model;
                    pose.GripBase = model.localPosition;
                }
                glow.Shaft(out Vector3 axis, out _);   // (along the shaft itself, so the hand stays on it)
                Vector3 shift = -model.TransformDirection(axis) * (pose.Choke * amount);   // in the world, in metres
                model.localPosition = pose.GripBase + (model.parent != null ? model.parent.InverseTransformVector(shift) : shift);
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
                Quaternion toBodyNow = Quaternion.Inverse(pose.Facing);
                right.rotation = pose.Facing * Quaternion.Slerp(Rest(pose, pose.Rest, toBodyNow * basisR, weight, 0.06f), pose.Held, weight);
                left.rotation = pose.Facing * Quaternion.Slerp(Rest(pose, pose.RestLeft, toBodyNow * basisL, weight, 0.06f), pose.HeldLeft, weight);
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
            /// The world rotation that points a hand's fingers along "fingersTo" with its palm facing "palmTo", from where its finger bones
            /// are on it (as PalmUp); false for a body without them.
            /// </summary>
            private static bool Facing(Animator animator, Transform hand, bool isRight, Vector3 fingersTo, Vector3 palmTo, out Quaternion held)
            {
                held = hand.rotation;
                Transform middle = animator.GetBoneTransform(isRight ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                Transform index = animator.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                Transform little = animator.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                if (middle == null || index == null || little == null) return false;
                Quaternion undo = Quaternion.Inverse(hand.rotation);
                Vector3 fingers = (undo * (middle.position - hand.position)).normalized;
                Vector3 across = (undo * (index.position - little.position)).normalized;
                Vector3 palm = Vector3.Cross(fingers, across) * (isRight ? 1f : -1f);
                if (fingers.sqrMagnitude < 0.5f || palm.sqrMagnitude < 1e-4f) return false;
                held = Quaternion.LookRotation(fingersTo, palmTo) * Quaternion.Inverse(Quaternion.LookRotation(fingers, palm.normalized));
                return true;
            }

            /// <summary>The left hand turned palm in (to the right), fingers up and a little forward, onto the near side of a sledge's head.</summary>
            private static void TurnLeftPalmIn(Animator animator, Pose pose, float turn, float weight, Quaternion toBody)
            {
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (left == null) return;
                Quaternion basisL = pose.Left.Basis(left.rotation);
                if (!pose.LeftPlanned) { pose.RestLeft = toBody * basisL; pose.LeftPlanned = true; }
                if (!Facing(animator, left, false, pose.Facing * new Vector3(0f, 1f, 0.4f).normalized, pose.Facing * Vector3.right, out Quaternion held)) return;
                Quaternion rest = Rest(pose, pose.RestLeft, toBody * basisL, weight, 0.06f);
                left.rotation = pose.Facing * Quaternion.Slerp(rest, toBody * held, turn);
                pose.Left.Remember(basisL, left.rotation);
            }

            /// <summary>
            /// A two-handed weapon's left hand turned so its palm runs along the haft (facing it, the fingers pointing up it), as it slides:
            /// left as the stance holds it, it ran the back of the hand along the haft. Worked out afresh each step (the hand moves), from
            /// where the hand was when the oiling began, by the weight.
            /// </summary>
            private static void TurnLeftOnHaft(Animator animator, Transform rightHand, Pose pose, float turn, float weight, Quaternion toBody)
            {
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (left == null) return;
                Quaternion basisL = pose.Left.Basis(left.rotation);
                if (!pose.LeftPlanned) { pose.RestLeft = toBody * basisL; pose.LeftPlanned = true; }
                Vector3 haft = pose.Blade;
                Vector3 onHaft = rightHand.position + haft * Vector3.Dot(left.position - rightHand.position, haft);
                Vector3 palmTo = onHaft - left.position;
                palmTo = palmTo.sqrMagnitude < 1e-4f ? pose.Facing * Vector3.right : palmTo.normalized;
                Vector3 fingersTo = Vector3.ProjectOnPlane(haft, palmTo);
                if (fingersTo.sqrMagnitude < 1e-4f || !Facing(animator, left, false, fingersTo.normalized, palmTo, out Quaternion held)) return;
                Quaternion rest = Rest(pose, pose.RestLeft, toBody * basisL, weight, 0.06f);
                left.rotation = pose.Facing * Quaternion.Slerp(rest, toBody * held, turn);
                pose.Left.Remember(basisL, left.rotation);
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
                Quaternion toChestNow = Quaternion.Inverse(pose.DualFacing);
                Quaternion restR = Rest(pose, pose.Rest, toChestNow * basisR, weight), restL = Rest(pose, pose.RestLeft, toChestNow * basisL, weight);
                right.rotation = untwist * pose.DualFacing * Quaternion.Slerp(restR, CharacterAnimEvent_OnAnimatorIK.BlockRightTurn, weight);
                left.rotation = untwist * pose.DualFacing * Quaternion.Slerp(restL, CharacterAnimEvent_OnAnimatorIK.BlockLeftTurn, weight);
                pose.LookAt = (right.position + left.position) * 0.5f + Vector3.up * 0.12f;   // (where the blades cross, now the body has turned)
                pose.Right.Remember(basisR, right.rotation);
                pose.Left.Remember(basisL, left.rotation);
                return true;
            }

            /// <summary>
            /// Where a hand is blended back to: as the game held it when the oiling began, and, letting go, more and more as the game holds it
            /// now. The idle stance keeps moving the hands (a sword's swings between hanging forward and straight down, the Berserkir axes'
            /// more): blending to the turn from the start left the hand 10 to 20 degrees off at the end, a snap into the stance.
            /// "now" is the hand as the game holds it on the arm this mod is placing, so while that arm is still lifted it points the blade
            /// well up: followed through the whole release, a sword rose before it settled. Where the stance barely moves (one weapon,
            /// bare hands) it is followed only as the arm is all but let go (from "until" down to nothing).
            /// </summary>
            private static Quaternion Rest(Pose pose, Quaternion atStart, Quaternion now, float weight, float until = 1f) =>
                Time.time - pose.Start > LiftOf(pose) + SweepTime ? Quaternion.Slerp(atStart, now, Ease(1f - weight / until)) : atStart;

            /// <summary>Turns the hand; false on a frame the animation didn't step (nothing was done, the bones keep what they had).</summary>
            private static bool TurnRight(Animator animator, Transform hand, Transform model, OilGlow glow, Pose pose, float weight)
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
                // The path goes by way of "forward, raised", so neither half is near half a turn.
                if (!pose.Planned)
                {
                    Vector3 axis;
                    if (pose.Choke > 0f) glow.Shaft(out axis, out _);   // (a haft's true line: an atgeir's is tilted in its model)
                    else glow.Blade(out axis, out _);
                    Vector3 now = toBody * ((basis * Quaternion.Inverse(current)) * model.TransformDirection(axis));
                    Vector3 via = new Vector3(0f, 0.45f, 1f).normalized;   // (as high as the held blade: lower, the sword dipped and rose on the way out)
                    pose.Rest = toBody * basis;
                    if (pose.Upright == 1)
                    {
                        // a two-handed weapon's hand turns straight to the game's own turn in its block, a short turn (Krom's about 30
                        // degrees): by way of "forward" it swung its two metres of blade out in front, and by way of the left Krom
                        // tilted in and out. A sledge's goes by way of upright, lifting the haft off the shoulders up away from the head
                        // (see ReachWeight)
                        pose.Held = pose.TwoTurn;
                        pose.Mid = pose.Sledge
                            ? Quaternion.FromToRotation(now, CharacterAnimEvent_OnAnimatorIK.SledgeUp) * pose.Rest
                            : Quaternion.Slerp(pose.Rest, pose.Held, 0.5f);
                    }
                    else
                    {
                        if (pose.Choke > 0f && !pose.Sledge)
                        {
                            // a battleaxe's and an atgeir's stances already hold them near the pose (the battleaxe across the body, 30
                            // degrees off): by way of "forward" they swung out in front and back, wobbling and clipping
                            pose.Held = Quaternion.FromToRotation(now, toBody * pose.Blade) * pose.Rest;
                            pose.Mid = Quaternion.Slerp(pose.Rest, pose.Held, 0.5f);
                        }
                        else
                        {
                            if (pose.Sledge) via = CharacterAnimEvent_OnAnimatorIK.SledgeUp;   // (stood up off the shoulders first, clear of the head)
                            pose.Mid = Quaternion.FromToRotation(now, via) * pose.Rest;
                            pose.Held = Quaternion.FromToRotation(via, toBody * pose.Blade) * pose.Mid;

                        }
                    }
                    pose.Planned = true;
                }
                float turn = weight;
                Quaternion ours = turn < 0.5f
                    ? Quaternion.Slerp(Rest(pose, pose.Rest, toBody * basis, weight, 0.06f), pose.Mid, turn * 2f)
                    : Quaternion.Slerp(pose.Mid, pose.Held, (turn - 0.5f) * 2f);   // (the weight is eased already)
                ours = pose.Facing * ours;
                // At the very start and end the hand is near where it rested, and hands over to the animator's own hand there.
                hand.rotation = ours;
                pose.Right.Remember(basis, hand.rotation);
                // the left palm along the haft or blade it oils (Krom's; a battleaxe's too, which wiped with the back of the hand)
                if (pose.Upright == 1 || (pose.Choke > 0f && !pose.Atgeir && !pose.Sledge)) TurnLeftOnHaft(animator, hand, pose, ReachWeight(pose, weight), weight, toBody);
                else if (pose.Sledge) TurnLeftPalmIn(animator, pose, ReachWeight(pose, weight), weight, toBody);   // (palm onto the head's near side)
                return true;
            }
        }
    }
}
