using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Nick.Shared;   // (Shared/Translations.cs)

namespace Spyglass
{
    /// <summary>
    /// Looking through the spyglass: with it in hand, holding Block (right mouse, or the gamepad's block) raises it. The camera glides to the
    /// player's eyes, the field of view narrows by the magnification, and the mouse wheel changes the magnification instead of the camera's
    /// distance. Block, attack and secondary attack do nothing while it is in hand, so raising it never swings or blocks.
    /// </summary>
    internal static class Zoom
    {
        private const float BlendSpeed = 6f;       // how fast the view goes in and out (per second)
        private const float LeanOfLook = 0.4f;     // degrees the torso leans per degree looked up or down, spread over the spine and chest
        private const float MaxLean = 30f;
        private const float AimOfLook = 0.6f;      // degrees the spyglass tilts per degree looked up or down (more than the torso)
        private const float MaxAim = 45f;
        private const float HeadStill = 0.5f;      // how much of the head's own looking is switched off while the spyglass is up (the torso does it)
        private const float MaxSeatedRight = 75f;  // seated: how far round (degrees) the raised arm follows the look, to the right and to the left
        private const float MaxSeatedLeft = 20f;
        private const float EyeFrom = 0.55f;       // from this much raised, the camera is at the eyes and the body is hidden

        private const float MessageDelay = 0.4f;    // seconds after the last notch of the wheel before the magnification is shown
        private static float _messageAt;            // when the magnification is to be shown (0 = nothing waiting)
        private static bool _brokenShown;       // the "too damaged" message has been shown for this press
        private static bool _raised;                // the player is holding the spyglass up this frame
        private static float _blend;                // 0 not raised .. 1 fully raised
        private static float _magnification;
        private static Camera _camera;
        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static float _baseLodBias = -1f;    // the player's own detail distance, kept while the spyglass raises it

        /// <summary>One player's arm: the local player's, and every other player seen holding a spyglass up (from their synced state).</summary>
        private sealed class Arm
        {
            public float Blend;                 // 0 not raised .. 1 fully raised (others': eased towards the synced state)
            public float Magnification;         // what the owner's game last said (others')
            public float Aim;                   // how far the spyglass is tilted (degrees) in the animation pass, for turning the hand after it
            public Quaternion Facing = Quaternion.identity;   // the direction the arm is held in (the body's, or where a seated player looks)
            public bool HandHeld;               // the hand's IK goal is switched on (so it is switched off once afterwards)
            public readonly Dictionary<HumanBodyBones, Quaternion> Rest = new Dictionary<HumanBodyBones, Quaternion>();   // the upper body's steady pose, from standing still
        }

        private static readonly Dictionary<Player, Arm> Arms = new Dictionary<Player, Arm>();
        private static readonly int UpKey = "nick_spyglass_up".GetStableHashCode();   // synced on the player: 0 down, else the magnification it is raised at
        private static float _sentUp = -1f;
        private static float _lastPing = -10f;

        private static Arm ArmOf(Player p)
        {
            if (!Arms.TryGetValue(p, out Arm arm)) Arms[p] = arm = new Arm();
            return arm;
        }
        private static readonly HumanBodyBones[] Locked = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.RightShoulder };
        private static readonly HumanBodyBones[] Leaning = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };

        private static readonly FieldInfo AnimatorField = AccessTools.Field(typeof(CharacterAnimEvent), "m_animator");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> AnimCharacter = AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");
        private static readonly FieldInfo AnimVisField = AccessTools.Field(typeof(CharacterAnimEvent), "m_visEquipment");
        private static readonly FieldInfo LookAtWeightField = AccessTools.Field(typeof(CharacterAnimEvent), "m_lookAtWeight");
        private static readonly FieldInfo BodyLookField = AccessTools.Field(typeof(CharacterAnimEvent), "m_bodyLookWeight");
        private static readonly FieldInfo HeadLookField = AccessTools.Field(typeof(CharacterAnimEvent), "m_headLookWeight");
        private static readonly FieldInfo EyeLookField = AccessTools.Field(typeof(CharacterAnimEvent), "m_eyeLookWeight");
        private static readonly FieldInfo LookClampField = AccessTools.Field(typeof(CharacterAnimEvent), "m_lookClamp");
        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static readonly FieldInfo RightItemField = AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance");

        private static float Eased => Mathf.SmoothStep(0f, 1f, _blend);

        /// <summary>Seated on a chair or throne, at a ship's wheel, or holding on at a ship's mast or bow (all of them the game's Chair or
        /// ShipControlls): the spyglass can be raised there. Not in a bed, and not in whatever another mod attaches a player to, where Block
        /// may mean something else.</summary>
        private static bool Seated(Player p)
        {
            if (!p.IsAttached()) return true;
            if (p.IsAttachedToShip()) return true;   // (the helm's attach point is not inside the wheel's component, so it is found this way)
            Transform point = p.GetAttachPoint();
            return point != null && (point.GetComponentInParent<Chair>() != null || point.GetComponentInParent<ShipControlls>() != null);
        }

        /// <summary>The way the arm is held. Standing, the body's; seated the body can't turn, so the arm follows where the player looks,
        /// as far round as a seated body could reach: much further to the right than to the left, where the right arm would cross the body.</summary>
        private static Quaternion FacingOf(Player p, Transform eye)
        {
            Transform body = p.transform;
            if (!p.IsAttached() || eye == null) return body.rotation;
            Vector3 look = eye.forward;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return body.rotation;
            float relative = Mathf.DeltaAngle(body.eulerAngles.y, Quaternion.LookRotation(look).eulerAngles.y);
            return body.rotation * Quaternion.Euler(0f, Mathf.Clamp(relative, -MaxSeatedLeft, MaxSeatedRight), 0f);
        }

        public static bool Raised => _raised;

        public static bool IsSpyglass(Humanoid h)
        {
            ItemDrop.ItemData weapon = h != null ? h.GetCurrentWeapon() : null;
            return weapon != null && weapon.m_shared != null && weapon.m_shared.m_name == SpyglassItem.ItemName;
        }

        /// <summary>Each frame (from the plugin): the mouse wheel changes the magnification while raised.</summary>
        public static void Update()
        {
            if (Player.m_localPlayer == null) { _raised = false; Arms.Clear(); _sentUp = -1f; }
            else if (_raised && !IsSpyglass(Player.m_localPlayer)) _raised = false;   // (put away, or dead, while it was up)
            Wear();
            ShowMagnification();
            Publish();
            Others();
            Ping();
            if (!_raised)
            {
                if (_magnification == 0f || _blend <= 0f) _magnification = Plugin.StartMagnification.Value;
                return;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel == 0f) wheel = GamepadZoom();
            if (wheel == 0f) return;
            float before = _magnification;
            // Whole steps of the wheel step, and the limits on that same grid: a limit that was off it (1.5 with steps of 1) left the
            // magnification half a step off for good after one scroll to the bottom.
            float step = Plugin.MagnificationStep.Value;
            float lowest = Mathf.Ceil(1.5f / step - 0.001f) * step;
            float highest = Mathf.Max(lowest, Mathf.Floor(Plugin.MaxMagnification.Value / step + 0.001f) * step);
            _magnification = Mathf.Clamp(Mathf.Round((_magnification + wheel * step) / step) * step, lowest, highest);
            if (!Mathf.Approximately(before, _magnification))
                Sound.Play(Plugin.ZoomSound.Value, 0.85f + 0.5f * Mathf.InverseLerp(1.5f, Plugin.MaxMagnification.Value, _magnification), 0.5f);   // higher as it closes in
            if (!Mathf.Approximately(before, _magnification)) _messageAt = Time.time + MessageDelay;   // (one message once the wheel stops, not one per notch)
        }

        /// <summary>On a controller the right bumper zooms in and the left one out, whatever the layout (in the game's layouts the bumpers
        /// are secondary attack, run, hide and the alt-key modifier, and the layouts differ: Run is the left stick's click in some). While the
        /// spyglass is raised the secondary attack is cleared like the other attacks, running is switched off and Hide is ignored (below).</summary>
        private static float GamepadZoom()
        {
            if (!ZInput.IsGamepadActive()) return 0f;
            if (ZInput.GetButtonDown("JoyRBumper")) return 1f;
            if (ZInput.GetButtonDown("JoyLBumper")) return -1f;
            return 0f;
        }

        // In some layouts the left bumper is Hide, which puts the items away on release: it must not, while it is the zoom out.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), new[] { typeof(string) })]
        private static class ZInput_GetButtonUp
        {
            private static void Postfix(string name, ref bool __result)
            {
                if (__result && _raised && name == "JoyHide") __result = false;
            }
        }

        /// <summary>The magnification is shown once, a moment after the wheel stops: each message is queued by the game and stays up for
        /// a while, so one per notch kept scrolling past long after looking through it was over.</summary>
        private static void ShowMagnification()
        {
            if (_messageAt <= 0f) return;
            if (!_raised || Player.m_localPlayer == null) { _messageAt = 0f; return; }
            if (Time.time < _messageAt) return;
            _messageAt = 0f;
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Translations.T("nick_spyglass_magnification", _magnification.ToString("0.#")));
        }

        /// <summary>Tells the other players when the spyglass goes up or down and how far it is zoomed (a small value on the player's own
        /// object, written only when it changes; 0 is written explicitly, since a removed key would stay on other games).</summary>
        private static void Publish()
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            float up = _raised ? Mathf.Round(_magnification * 100f) / 100f : 0f;
            if (up == _sentUp) return;
            ZNetView view = NView(p);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) return;
            zdo.Set(UpKey, up);
            _sentUp = up;
        }

        /// <summary>The other players' spyglasses: their arms rise and fall with the state they publish, and their sounds are heard nearby.</summary>
        private static void Others()
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            List<Player> all = Player.GetAllPlayers();
            foreach (Player p in all)
            {
                if (p == null || p == me) continue;
                ZNetView view = NView(p);
                ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (zdo == null) continue;
                float up = zdo.GetFloat(UpKey, 0f);
                if (up <= 0f && !Arms.ContainsKey(p)) continue;
                Arm arm = ArmOf(p);
                float before = arm.Magnification;
                arm.Magnification = up;
                bool audible = Plugin.OthersSounds.Value && (p.transform.position - me.transform.position).sqrMagnitude < Plugin.HearRange.Value * Plugin.HearRange.Value;
                if (audible)
                {
                    Vector3 at = p.transform.position + Vector3.up * 1.4f;
                    if (before <= 0f && up > 0f) Sound.PlayAt(Plugin.RaiseSound.Value, at, 1.1f);
                    else if (before > 0f && up <= 0f) Sound.PlayAt(Plugin.LowerSound.Value, at, 0.85f);
                    else if (before > 0f && up > 0f && !Mathf.Approximately(before, up))
                        Sound.PlayAt(Plugin.ZoomSound.Value, at, 0.85f + 0.5f * Mathf.InverseLerp(1.5f, Plugin.MaxMagnification.Value, up), 0.5f);
                }
                arm.Blend = Mathf.MoveTowards(arm.Blend, up > 0f ? 1f : 0f, BlendSpeed * Time.deltaTime);
            }
            var gone = new List<Player>();
            foreach (Player p in Arms.Keys) if (p == null || (p != me && !all.Contains(p))) gone.Add(p);
            foreach (Player p in gone) Arms.Remove(p);
        }

        /// <summary>Middle mouse (a setting) while looking through the spyglass pings what is at the middle of the view: the game's own map ping.</summary>
        private static void Ping()
        {
            if (!_raised || Plugin.PingKey.Value == KeyCode.None) return;
            // (a controller's attack button, whichever it is in the player's layout: attacks are cleared while the spyglass is in hand)
            if (!Input.GetKeyDown(Plugin.PingKey.Value) && !(ZInput.IsGamepadActive() && ZInput.GetButtonDown("JoyAttack"))) return;
            if (Time.time - _lastPing < Plugin.PingCooldown.Value) return;
            Player me = Player.m_localPlayer;
            Camera cam = _camera != null ? _camera : Camera.main;
            if (me == null || cam == null || Chat.instance == null) return;
            if (!FindTarget(new Ray(cam.transform.position, cam.transform.forward), me, Plugin.PingRange.Value, out Vector3 point))
            {
                me.Message(MessageHud.MessageType.TopLeft, Translations.T("nick_spyglass_noping"));
                return;
            }
            _lastPing = Time.time;
            Chat.instance.SendPing(point);
        }

        private static readonly string[] PingLayers = { "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "vehicle" };

        /// <summary>What the ray meets: the first thing near enough to have colliders loaded, else where it meets the ground (or the sea) far away.</summary>
        private static bool FindTarget(Ray ray, Player me, float range, out Vector3 point)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, range, LayerMask.GetMask(PingLayers), QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<Player>() == me) continue;
                point = hit.point;
                return true;
            }
            point = default;
            if (ZoneSystem.instance == null) return false;
            float sea = ZoneSystem.instance.m_waterLevel;
            const float step = 20f;
            float near = 0f;
            for (float t = step; t <= range; t += step)
            {
                Vector3 p = ray.GetPoint(t);
                if (p.y > Mathf.Max(ZoneSystem.instance.GetGroundHeight(p), sea)) { near = t; continue; }
                float far = t;
                for (int i = 0; i < 8; i++)
                {
                    float mid = (near + far) * 0.5f;
                    Vector3 m = ray.GetPoint(mid);
                    if (m.y > Mathf.Max(ZoneSystem.instance.GetGroundHeight(m), sea)) near = mid; else far = mid;
                }
                point = ray.GetPoint(far);
                return true;
            }
            return false;
        }

        /// <summary>The spyglass in the hand, when the item is one that wears.</summary>
        private static ItemDrop.ItemData WearingItem(Humanoid h)
        {
            ItemDrop.ItemData item = h != null ? h.GetCurrentWeapon() : null;
            return item != null && item.m_shared != null && item.m_shared.m_name == SpyglassItem.ItemName && item.m_shared.m_useDurability ? item : null;
        }

        /// <summary>How worn the spyglass in the hand is: 0 new, 1 broken.</summary>
        private static float CurrentWear()
        {
            ItemDrop.ItemData item = WearingItem(Player.m_localPlayer);
            return item != null ? 1f - Mathf.Clamp01(item.GetDurabilityPercentage()) : 0f;
        }

        /// <summary>Looking through it wears it: one point of durability per so many seconds with it raised (a setting, 0 for never).</summary>
        private static void Wear()
        {
            if (!_raised || _blend < 0.9f || Plugin.WearSeconds.Value <= 0f) return;
            ItemDrop.ItemData item = WearingItem(Player.m_localPlayer);
            if (item == null || item.m_durability <= 0f) return;
            float before = item.m_durability;
            item.m_durability = Mathf.Max(0f, item.m_durability - Time.deltaTime / Plugin.WearSeconds.Value);
            if (Mathf.FloorToInt(before) != Mathf.FloorToInt(item.m_durability))
                Player.m_localPlayer.GetInventory().m_onChanged?.Invoke();   // (the durability bar, and the item saves its new value)
            if (item.m_durability <= 0f) Sound.Play(Plugin.BreakSound.Value);   // (the game shows "broke" and puts it away; it has no sound for that)
        }

        /// <summary>Puts everything back (the plugin unloading, or no player any more).</summary>
        public static void Reset()
        {
            _raised = false;
            _sentUp = -1f;
            Publish();
            Arms.Clear();
            _blend = 0f;
            ShowBody();
            RestoreDetail();
            Lens.Reset();
        }

        // ---- the view ---------------------------------------------------------------------------------------------

        private static void HideBody()
        {
            if (Hidden.Count > 0 || Player.m_localPlayer == null) return;
            GameObject visual = Player.m_localPlayer.GetVisual();
            if (visual == null) return;
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                Hidden.Add(r);
            }
        }

        private static void ShowBody()
        {
            foreach (Renderer r in Hidden) if (r != null) r.enabled = true;
            Hidden.Clear();
        }

        private static float ZoomedFov(float baseFov)
        {
            float magnification = Mathf.Max(1f, _magnification);
            return 2f * Mathf.Atan(Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / magnification) * Mathf.Rad2Deg;
        }

        /// <summary>Detail holds out further while looking: the game's level-of-detail distance scaled up, put back exactly afterwards.</summary>
        private static void FarDetail()
        {
            if (_blend <= 0f) { RestoreDetail(); return; }
            if (_baseLodBias < 0f) _baseLodBias = QualitySettings.lodBias;
            QualitySettings.lodBias = _baseLodBias * Mathf.Lerp(1f, Plugin.FarSight.Value, Eased);
        }

        private static void RestoreDetail()
        {
            if (_baseLodBias < 0f) return;
            QualitySettings.lodBias = _baseLodBias;
            _baseLodBias = -1f;
        }

        // ---- patches ----------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls
        {
            private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
                ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool run)
            {
                if (__instance != Player.m_localPlayer) return;
                if (!IsSpyglass(__instance)) { _raised = false; return; }
                bool wanted = block || blockHold || Plugin.HoldPose.Value;
                ItemDrop.ItemData worn = WearingItem(__instance);
                if (wanted && worn != null && worn.m_durability <= 0f)   // broken, as the game's own items are at no durability
                {
                    if (!_brokenShown) __instance.Message(MessageHud.MessageType.Center, Translations.T("nick_spyglass_broken"));
                    _brokenShown = true;
                    wanted = false;
                }
                else if (!block && !blockHold) _brokenShown = false;
                bool wasRaised = _raised;
                _raised = wanted && !__instance.InPlaceMode() && Seated(__instance) && !__instance.InCutscene();
                if (_raised && ZInput.IsGamepadActive()) run = false;   // (the left bumper zooms out while it is up)
                if (_raised && !wasRaised) Sound.Play(Plugin.RaiseSound.Value, 1.1f);
                else if (!_raised && wasRaised) Sound.Play(Plugin.LowerSound.Value, 0.85f);
                attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
            }
        }

        // The body turns with the camera while the spyglass is up, as it does while blocking (which the spyglass switches off): without
        // it the raised arm twists round a body that stays put. The head follows the look direction by itself once the body faces it.
        [HarmonyPatch(typeof(Player), "AlwaysRotateCamera")]
        private static class Player_AlwaysRotateCamera
        {
            private static void Postfix(Player __instance, ref bool __result)
            {
                if (_raised && __instance == Player.m_localPlayer) __result = true;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class GameCamera_UpdateCamera
        {
            private static void Prefix()
            {
                float target = _raised ? 1f : 0f;
                _blend = Mathf.MoveTowards(_blend, target, BlendSpeed * Time.deltaTime);
                if (_blend >= EyeFrom && Plugin.LookFromEyes.Value) HideBody();
                else if (_blend < EyeFrom) ShowBody();
            }

            private static void Postfix(GameCamera __instance)
            {
                FarDetail();
                Lens.Show(Eased, true, Plugin.CrackedLens.Value, CurrentWear());
                if (_blend <= 0f) return;
                if (_camera == null) _camera = __instance.GetComponent<Camera>();
                if (_camera == null) return;
                float eased = Mathf.SmoothStep(0f, 1f, _blend);
                float fov = Mathf.Lerp(__instance.m_fov, ZoomedFov(__instance.m_fov), eased);
                _camera.fieldOfView = fov;
                if (__instance.m_skyCamera != null) __instance.m_skyCamera.fieldOfView = fov;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
        private static class GameCamera_GetCameraPosition
        {
            private static void Postfix(ref Vector3 pos)
            {
                Player player = Player.m_localPlayer;
                if (_blend <= 0f || !Plugin.LookFromEyes.Value || player == null || player.m_eye == null) return;
                Vector3 eyes = player.m_eye.position + player.m_eye.forward * 0.12f;
                pos = Vector3.Lerp(pos, eyes, Mathf.SmoothStep(0f, 1f, _blend));
            }
        }

        // The air clears while looking: the game sets the fog's density in SetEnv (about fifty times a second, from absolute values, so
        // taking some off here never builds up).
        [HarmonyPatch(typeof(EnvMan), "SetEnv")]
        private static class EnvMan_SetEnv
        {
            private static void Postfix()
            {
                if (_blend > 0f) RenderSettings.fogDensity *= 1f - Plugin.ClearAir.Value * Eased;
            }
        }

        // The right hand brings the spyglass up to the face. The game's animator already runs an IK pass (feet and head): the hand's goal is
        // set there, worked out from where the held spyglass sits in the hand now, so it ends up at the eye whatever the hand's own pose.
        [HarmonyPatch(typeof(CharacterAnimEvent), "OnAnimatorIK")]
        internal static class CharacterAnimEvent_OnAnimatorIK
        {
            private static void Postfix(CharacterAnimEvent __instance)
            {
                if (Player.m_localPlayer == null || !(AnimCharacter(__instance) is Player who)) return;
                var animator = AnimatorField?.GetValue(__instance) as Animator;
                if (animator == null) return;

                bool mine = who == Player.m_localPlayer;
                Arm arm;
                float weight;
                if (mine) { arm = ArmOf(who); weight = Plugin.RaiseArm.Value && IsSpyglass(who) ? Eased : 0f; }
                else if (Arms.TryGetValue(who, out arm)) weight = Plugin.RaiseArm.Value && Plugin.OthersArm.Value ? Mathf.SmoothStep(0f, 1f, arm.Blend) : 0f;
                else return;
                StillHead(__instance, animator, who.IsAttached() ? 0f : weight);   // (seated the body can't turn: the head does the looking)
                if (weight <= 0f)
                {
                    RememberRest(who, arm, animator);
                    if (arm.HandHeld) Hand(animator, 0f);
                    arm.HandHeld = false;
                    return;
                }
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var vis = AnimVisField?.GetValue(__instance) as VisEquipment;
                var item = vis != null ? RightItemField?.GetValue(vis) as GameObject : null;
                Transform eye = who.m_eye;
                if (hand == null || item == null || eye == null) return;

                // A steady pose held against the body, not the camera: the arm brings the spyglass up to the right eye once, and from then
                // on only the torso (leaning with the look) and the body's turning move it. The upper body is held in its standing pose, so
                // running and strafing don't sway it or swing the right arm (the left arm and the legs go on as they were), and the hand is
                // placed in the body's own frame, the character's yaw with the torso's lean, from where the head will be.
                Transform body = who.transform;
                if (!Torso(who, arm, animator, eye, weight, out float tilt, out float aim, out Vector3 headAt)) return;
                arm.Facing = FacingOf(who, eye);
                Vector3 across = arm.Facing * Vector3.right;
                Quaternion frame = Quaternion.AngleAxis(tilt, across) * arm.Facing;
                Quaternion aimFrame = Quaternion.AngleAxis(aim, across) * arm.Facing;
                arm.Aim = aim;

                // The IK places the hand and the elbow; the hand's turning is done afterwards (see the late update below), because the IK
                // goal's rotation isn't the bone's own (a constant turn that, applied as if it were, sent the spyglass off pointing at the sky).
                Quaternion turn = Aim(aimFrame, item.transform);
                Vector3 grip = headAt + frame * new Vector3(Plugin.HandRight.Value, Plugin.HandUp.Value, Plugin.HandForward.Value);
                Vector3 offset = item.transform.position - hand.position;

                animator.SetIKPosition(AvatarIKGoal.RightHand, grip - turn * offset);
                animator.SetIKHintPosition(AvatarIKHint.RightElbow, grip + frame * new Vector3(Plugin.ElbowOut.Value, -Plugin.ElbowDown.Value, -0.05f));
                Hand(animator, weight);
                arm.HandHeld = true;
            }

            /// <summary>
            /// The head stops looking up and down on its own while the spyglass is raised: the torso's lean does the looking, and the head
            /// just goes with the body, so it can't dip into the hand and the spyglass in front of it. (The game sets the look-at weights
            /// every pass; this runs after it and sets them again, smaller.)
            /// </summary>
            private static void StillHead(CharacterAnimEvent e, Animator animator, float amount)
            {
                if (amount <= 0f || LookAtWeightField == null || BodyLookField == null || HeadLookField == null || EyeLookField == null || LookClampField == null) return;
                float overall = (float)LookAtWeightField.GetValue(e) * (1f - HeadStill * amount);
                animator.SetLookAtWeight(overall, (float)BodyLookField.GetValue(e), (float)HeadLookField.GetValue(e), (float)EyeLookField.GetValue(e), (float)LookClampField.GetValue(e));
            }

            /// <summary>The turn that puts the spyglass the way it is held: lying along the view, barrel forward and the eyepiece back at the
            /// eye, the hand rolled by the setting. The spyglass lies along its own Z with the eyepiece forward, so it faces the player.</summary>
            internal static Quaternion Aim(Quaternion frame, Transform item)
            {
                Quaternion wanted = frame * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(0f, 0f, Plugin.HandRoll.Value);
                return wanted * Quaternion.Inverse(item.rotation);
            }

            /// <summary>The upper body's standing pose, kept from every moment the player stands still on the ground (animated, so any pose
            /// they were in then is the one held while the spyglass is up).</summary>
            private static void RememberRest(Player p, Arm arm, Animator animator)
            {
                if (!p.IsOnGround() || p.IsCrouching() || p.IsSwimming() || p.IsAttached() || p.GetVelocity().magnitude > 0.2f) return;
                foreach (HumanBodyBones id in Locked)
                {
                    Transform bone = animator.GetBoneTransform(id);
                    if (bone != null) arm.Rest[id] = bone.localRotation;
                }
            }

            /// <summary>
            /// The torso while the spyglass is up. Held in the standing pose (so the run and strafe animations don't move it), then leaning
            /// back to look up and forward to look down, spread over the spine and chest. Walked from the hips to the head one bone at a
            /// time, so each leans on top of the one below it and the head's resulting position is known (it is where the hand goes).
            /// Gives the torso's tilt, how far the spyglass itself tilts (more), and the head's position. (Bones are set in the IK pass,
            /// which the animator redoes every frame.)
            /// </summary>
            private static bool Torso(Player who, Arm arm, Animator animator, Transform eye, float weight, out float tilt, out float aim, out Vector3 headAt)
            {
                float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(eye.forward.y, -1f, 1f)) * Mathf.Rad2Deg, -70f, 70f);   // up is positive
                tilt = Mathf.Clamp(-pitch * LeanOfLook, -MaxLean, MaxLean) * weight;   // about the body's right axis: back for up
                aim = Mathf.Clamp(-pitch * AimOfLook, -MaxAim, MaxAim) * weight;
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                headAt = head != null ? head.position : Vector3.zero;
                if (head == null || hips == null) return false;

                var chain = new List<Transform>();
                for (Transform t = head; t != null && t != hips; t = t.parent) chain.Add(t);
                if (chain.Count == 0 || chain[chain.Count - 1].parent != hips) return false;
                chain.Reverse();

                var ids = new Dictionary<Transform, HumanBodyBones>();
                foreach (HumanBodyBones id in Locked)
                {
                    Transform bone = animator.GetBoneTransform(id);
                    if (bone != null) ids[bone] = id;
                }
                int leaning = 0;
                foreach (Transform t in chain) if (ids.TryGetValue(t, out HumanBodyBones id) && System.Array.IndexOf(Leaning, id) >= 0) leaning++;
                float share = leaning > 0 ? tilt / leaning : 0f;
                Vector3 right = who.transform.right;

                Vector3 position = hips.position;
                Quaternion rotation = hips.rotation;
                foreach (Transform t in chain)
                {
                    position += rotation * Vector3.Scale(t.localPosition, t.parent.lossyScale);
                    Quaternion local = t.localRotation;
                    bool locked = ids.TryGetValue(t, out HumanBodyBones id);
                    if (locked && !who.IsAttached() && arm.Rest.TryGetValue(id, out Quaternion rest)) local = Quaternion.Slerp(local, rest, weight);   // from the animated pose to the steady one
                    Quaternion world = rotation * local;
                    if (locked && System.Array.IndexOf(Leaning, id) >= 0)
                    {
                        world = Quaternion.AngleAxis(share, right) * world;
                        local = Quaternion.Inverse(rotation) * world;
                    }
                    if (locked) animator.SetBoneLocalRotation(id, local);
                    rotation = world;
                }
                headAt = position;

                // the shoulder isn't on the way to the head: held in its standing pose too, so the arm's root doesn't sway
                Transform shoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                if (shoulder != null && !who.IsAttached() && arm.Rest.TryGetValue(HumanBodyBones.RightShoulder, out Quaternion shoulderRest))
                    animator.SetBoneLocalRotation(HumanBodyBones.RightShoulder, Quaternion.Slerp(shoulder.localRotation, shoulderRest, weight));
                return true;
            }

            private static void Hand(Animator animator, float weight)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
            }
        }

        // After the animator has done everything (IK too), the hand is turned so the spyglass lies right. The item sits on the hand, so turning
        // the hand turns it; the arm already reaches the right place.
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomLateUpdate))]
        private static class CharacterAnimEvent_CustomLateUpdate
        {
            private static void Postfix(CharacterAnimEvent __instance)
            {
                if (Player.m_localPlayer == null || !Plugin.RaiseArm.Value) return;
                if (!(AnimCharacter(__instance) is Player who) || !Arms.TryGetValue(who, out Arm arm) || !arm.HandHeld) return;
                bool mine = who == Player.m_localPlayer;
                if (mine ? !IsSpyglass(who) : !Plugin.OthersArm.Value) return;
                var animator = AnimatorField?.GetValue(__instance) as Animator;
                var vis = AnimVisField?.GetValue(__instance) as VisEquipment;
                var item = vis != null ? RightItemField?.GetValue(vis) as GameObject : null;
                Transform hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null || item == null) return;

                Transform body = who.transform;
                Quaternion frame = Quaternion.AngleAxis(arm.Aim, arm.Facing * Vector3.right) * arm.Facing;
                Quaternion turn = CharacterAnimEvent_OnAnimatorIK.Aim(frame, item.transform);
                hand.rotation = Quaternion.Slerp(Quaternion.identity, turn, mine ? Eased : Mathf.SmoothStep(0f, 1f, arm.Blend)) * hand.rotation;
            }
        }

        // The wheel is the magnification while the spyglass is up, not the camera's distance.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
        private static class ZInput_GetMouseScrollWheel
        {
            private static void Postfix(ref float __result)
            {
                if (_raised) __result = 0f;
            }
        }
    }
}
