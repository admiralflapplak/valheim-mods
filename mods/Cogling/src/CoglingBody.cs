using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The Cogling's look: on the Visual of a Dverger body (whose animator, movement and sounds the Cogling keeps), the Dverger's own
    /// renderers are hidden and the robot is shown instead: its own skeleton, built from the model's rest pose, with the skinned mesh on it.
    /// Both skeletons are humanoids to Unity (the Dverger's avatar is; the robot's is built here with AvatarBuilder, as GoodTidings'
    /// Retarget does for Mixamo animations), so each frame the Dverger's pose is copied onto the robot with HumanPoseHandler: the robot's
    /// own proportions, the Dverger's movements.
    /// </summary>
    internal sealed class CoglingBody : MonoBehaviour
    {
        // Mixamo's bone names (without "mixamorig:") to Unity's humanoid ones
        private static readonly Dictionary<string, string> Human = MakeHuman();

        private static Dictionary<string, string> MakeHuman()
        {
            var map = new Dictionary<string, string>
            {
                { "Hips", "Hips" }, { "Spine", "Spine" }, { "Spine1", "Chest" }, { "Spine2", "UpperChest" }, { "Neck", "Neck" }, { "Head", "Head" },
            };
            foreach (string side in new[] { "Left", "Right" })
            {
                map[side + "Shoulder"] = side + "Shoulder";
                map[side + "Arm"] = side + "UpperArm";
                map[side + "ForeArm"] = side + "LowerArm";
                map[side + "Hand"] = side + "Hand";
                map[side + "UpLeg"] = side + "UpperLeg";
                map[side + "Leg"] = side + "LowerLeg";
                map[side + "Foot"] = side + "Foot";
                map[side + "ToeBase"] = side + "Toes";
            }
            return map;
        }

        private static string Plain(string bone) => bone.StartsWith("mixamorig:") ? bone.Substring("mixamorig:".Length) : bone;

        private static Avatar _avatar;       // the robot's, shared: every Cogling's skeleton has the same names and rest pose
        private static Material _material;   // the Dverger's body material with the robot's texture: the wood (and glowing eyes)
        private static Material _copper;     // the game's copper bar material with the plates' shading: the copper parts

        // the Coglings shown on this game (for the collapse to find the dying one, and the eye colour)
        private static readonly List<CoglingBody> Live = new List<CoglingBody>();
        internal SkinnedMeshRenderer Skin { get; private set; }
        internal static Material[] SharedMaterials => _material != null ? new[] { _material, _copper ?? _material } : null;
        private MaterialPropertyBlock _eyes;
        private Color? _eyeColor;   // this Cogling's own eye colour (later: what he's doing); null: the Eye colour setting

        private Animator _animator;
        private Transform _root;
        private HumanPoseHandler _from, _to;
        private HumanPose _pose;

        private void Awake()
        {
            if (transform.parent != null)
            {
                CoglingPrefab.HammerTarget(transform.parent.gameObject);
                // (a Cogling standing from before the voice existed)
                if (transform.parent.GetComponent<CoglingVoice>() == null) transform.parent.gameObject.AddComponent<CoglingVoice>();
                if (transform.parent.GetComponent<CoglingTag>() == null) transform.parent.gameObject.AddComponent<CoglingTag>();
                CoglingAI.Install(transform.parent.gameObject);   // (and from before he had his own brain)
            }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;   // dedicated server: nothing to show
            _animator = GetComponent<Animator>();
            if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
            {
                Plugin.Log.LogWarning("The Cogling's body has no humanoid animator: shown as a Dverger");
                return;
            }
            if (!CoglingModel.Load()) return;

            Renderer[] dvergerRenderers = GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.GetComponentsInParent<Transform>(true).Any(t => t.name == "cogling_body")).ToArray();
            Material material = Material(dvergerRenderers.OfType<SkinnedMeshRenderer>().Select(r => r.sharedMaterial).FirstOrDefault(m => m != null));
            if (material == null) { Plugin.Log.LogWarning("The Dverger has no body material to copy: the Cogling shows as a Dverger"); return; }
            foreach (Renderer r in dvergerRenderers) r.enabled = false;

            _root = Build(transform, out Transform[] bones);   // (already Plugin.Height tall: CoglingModel scales the model as it loads)
            _leftHand = bones.FirstOrDefault(b => b.name.EndsWith("LeftHand"));
            _rightHand = bones.FirstOrDefault(b => b.name.EndsWith("RightHand"));
            _chest = bones.FirstOrDefault(b => b.name.EndsWith("Spine2"));
            _head = bones.FirstOrDefault(b => b.name.EndsWith("Head"));
            _upperArmR = bones.FirstOrDefault(b => b.name.EndsWith("RightArm")); _foreArmR = bones.FirstOrDefault(b => b.name.EndsWith("RightForeArm"));
            _upperArmL = bones.FirstOrDefault(b => b.name.EndsWith("LeftArm")); _foreArmL = bones.FirstOrDefault(b => b.name.EndsWith("LeftForeArm"));
            if (_rightHand != null) _handRestR = _rightHand.localRotation;
            if (_leftHand != null) _handRestL = _leftHand.localRotation;
            _hips = bones[0];
            _leftFoot = bones.FirstOrDefault(b => b.name.EndsWith("LeftFoot"));
            _rightFoot = bones.FirstOrDefault(b => b.name.EndsWith("RightFoot"));
            int lf = System.Array.FindIndex(CoglingModel.BoneNames, n => n.EndsWith("LeftFoot")), rf = System.Array.FindIndex(CoglingModel.BoneNames, n => n.EndsWith("RightFoot"));
            _footRest = lf >= 0 && rf >= 0 ? Mathf.Min(CoglingModel.RestPos[lf].y, CoglingModel.RestPos[rf].y) : 0f;
            _view = GetComponentInParent<ZNetView>();

            var mesh = new GameObject("cogling_mesh") { layer = gameObject.layer };
            mesh.transform.SetParent(_root, false);
            var skin = mesh.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = CoglingModel.Mesh;
            skin.bones = bones;
            skin.rootBone = bones[0];
            skin.sharedMaterials = new[] { material, Copper() ?? material };   // submesh 0 wood, 1 copper (CoglingModel)
            Skin = skin;
            Live.Add(this);
            ShowEyes();

            // the Dverger's animator must keep moving its (hidden) bones: they are what the robot copies
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (_avatar == null) _avatar = MakeAvatar();
            if (_avatar == null) return;
            _from = new HumanPoseHandler(_animator.avatar, _animator.transform);
            _to = new HumanPoseHandler(_avatar, _root);
            CoglingClip idle = CoglingClip.Get("idle"), happy = CoglingClip.Get("happy_idle"), sad = CoglingClip.Get("sad_idle");
            if (idle != null) _idle = new ClipPlayer(idle, _avatar);
            if (happy != null) _happy = new ClipPlayer(happy, _avatar);
            if (sad != null) _sad = new ClipPlayer(sad, _avatar);
            // as he was saved: powered down (a new Cogling starts so, and boots up) or awake
            _asleep = _view == null || !_view.IsValid() || !_view.GetZDO().GetBool(Carried.AwakeKey);
            _sleepWeight = _asleep ? 1f : 0f;
            _eyeLevel = _asleep ? 0f : 1f;
            ShowEyes();
            _idleStart = Random.Range(0f, 10f);   // (two Coglings side by side don't idle in step)
            _lastPos = transform.position;
        }

        /// <summary>
        /// For the build-menu picture: the robot put onto a copy of the Cogling that has no scripts (Icon makes it so), the Dverger's
        /// body hidden, standing in the first moment of his idle. Nothing of it stays: Icon removes the copy after drawing it.
        /// </summary>
        internal static void Picture(GameObject copy)
        {
            if (!CoglingModel.Load()) return;
            Transform visual = copy.transform.Find("Visual");
            if (visual == null) return;
            Renderer[] dverger = visual.GetComponentsInChildren<Renderer>(true);
            Material material = Material(dverger.OfType<SkinnedMeshRenderer>().Select(r => r.sharedMaterial).FirstOrDefault(m => m != null));
            if (material == null) return;
            foreach (Renderer r in dverger) r.enabled = false;
            Transform root = Build(visual, out Transform[] bones);
            var mesh = new GameObject("cogling_mesh") { layer = visual.gameObject.layer };
            mesh.transform.SetParent(root, false);
            var skin = mesh.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = CoglingModel.Mesh;
            skin.bones = bones;
            skin.rootBone = bones[0];
            skin.sharedMaterials = new[] { material, Copper() ?? material };
            skin.updateWhenOffscreen = true;
            if (_avatar == null) _avatar = MakeAvatar();
            CoglingClip idle = CoglingClip.Get("idle");
            if (_avatar == null || idle == null) return;
            var player = new ClipPlayer(idle, _avatar);
            var pose = new HumanPose();
            player.Pose(0f, ref pose);
            player.Dispose();
            using (var to = new HumanPoseHandler(_avatar, root)) to.SetHumanPose(ref pose);
        }

        /// <summary>The robot's skeleton in its rest pose, under <paramref name="parent"/>; bones in the model's order.</summary>
        internal static Transform BuildSkeleton(Transform parent, out Transform[] bones) => Build(parent, out bones);

        private static Transform Build(Transform parent, out Transform[] bones)
        {
            var root = new GameObject("cogling_body") { layer = parent != null ? parent.gameObject.layer : 0 }.transform;
            root.SetParent(parent, false);
            bones = new Transform[CoglingModel.BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                int p = CoglingModel.Parents[i];
                var b = new GameObject(CoglingModel.BoneNames[i]) { layer = root.gameObject.layer }.transform;
                b.SetParent(p >= 0 ? bones[p] : root, false);
                Quaternion parentRot = p >= 0 ? CoglingModel.RestRot[p] : Quaternion.identity;
                Vector3 parentPos = p >= 0 ? CoglingModel.RestPos[p] : Vector3.zero;
                b.localPosition = Quaternion.Inverse(parentRot) * (CoglingModel.RestPos[i] - parentPos);
                b.localRotation = Quaternion.Inverse(parentRot) * CoglingModel.RestRot[i];
                bones[i] = b;
            }
            return root;
        }

        private static Avatar MakeAvatar()
        {
            // described from a skeleton of its own at scale 1, kept out of the world
            var template = Build(null, out Transform[] bones).gameObject;
            template.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var description = new HumanDescription
                {
                    human = bones.Where(b => Human.ContainsKey(Plain(b.name)))
                        .Select(b => new HumanBone { boneName = b.name, humanName = Human[Plain(b.name)], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = new[] { new SkeletonBone { name = template.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one } }
                        .Concat(bones.Select(b => new SkeletonBone { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = Vector3.one })).ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(template, description);
                if (avatar == null || !avatar.isValid || !avatar.isHuman) { Plugin.Log.LogWarning("The Cogling's skeleton isn't a valid humanoid"); return null; }
                // never unloaded with the game's unused assets: only the native pose handlers use it (GoodTidings learnt this with a crash)
                avatar.hideFlags = HideFlags.DontUnloadUnusedAsset;
                avatar.name = "cogling";
                return avatar;
            }
            finally { Destroy(template); }
        }

        private static Material Material(Material dverger)
        {
            if (_material != null) return _material;
            if (dverger == null) return null;
            _material = new Material(dverger);
            _material.name = "cogling";
            _material.mainTexture = CoglingModel.Texture;
            foreach (string map in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap", "_StyleTex" })
                if (_material.HasProperty(map)) _material.SetTexture(map, null);
            _material.color = Color.white;
            _material.DisableKeyword("_NORMALMAP");
            // the eyes glow green: the Dverger's own emission (its blue runes) with our glow map, which is black but for the eyes
            if (CoglingModel.Glow != null && _material.HasProperty("_EmissionMap"))
            {
                _material.SetTexture("_EmissionMap", CoglingModel.Glow);
                _material.SetColor("_EmissionColor", Plugin.EyeColor.Value * Plugin.EyeGlow.Value);   // (each Cogling's own: ShowEyes)
                _material.EnableKeyword("_EMISSION");
            }
            else
            {
                _material.DisableKeyword("_EMISSION");
                if (_material.HasProperty("_EmissionColor")) _material.SetColor("_EmissionColor", Color.black);
            }
            _material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _material;
        }

        /// <summary>A copy of the game's copper (the copper bar's material: its colour, metal and shine) with the plates' shading as its
        /// texture. Null if the game has no copper bar to copy (the plates then stay in the wood material).</summary>
        private static readonly string[] ShineProps = { "_Glossiness", "_Smoothness", "_GlossMapScale", "_Gloss" };
        private static readonly string[] MetalProps = { "_Metallic", "_Metal" };
        private static readonly Dictionary<string, float> _copperBase = new Dictionary<string, float>();

        /// <summary>The Copper shine and Copper metal settings on the copper (live).</summary>
        internal static void ApplyCopper()
        {
            if (_copper == null) return;
            foreach (var kv in _copperBase)
                _copper.SetFloat(kv.Key, kv.Value * (ShineProps.Contains(kv.Key) ? Plugin.CopperShine.Value : Plugin.CopperMetal.Value));
        }

        private static Material Copper()
        {
            if (_copper != null) return _copper;
            Material bar = ZNetScene.instance?.GetPrefab("Copper")?.GetComponentInChildren<MeshRenderer>(true)?.sharedMaterial;
            if (bar == null || CoglingModel.CopperDetail == null) return null;
            _copper = new Material(bar) { name = "cogling_copper" };
            _copper.mainTexture = CoglingModel.CopperDetail;
            _copper.mainTextureScale = Vector2.one;
            _copper.mainTextureOffset = Vector2.zero;
            foreach (string map in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_DetailAlbedoMap", "_DetailNormalMap", "_ParallaxMap" })
                if (_copper.HasProperty(map)) _copper.SetTexture(map, null);
            _copper.DisableKeyword("_NORMALMAP");
            _copper.DisableKeyword("_METALLICGLOSSMAP");
            _copper.hideFlags = HideFlags.DontUnloadUnusedAsset;
            // its shine and metal as the bar has them, kept: the settings scale them (Copper shine, Copper metal)
            _copperBase.Clear();
            foreach (string p in ShineProps.Concat(MetalProps))
                if (_copper.HasProperty(p)) _copperBase[p] = _copper.GetFloat(p);
            Plugin.Log.LogInfo($"Cogling copper: shader {_copper.shader.name}, " + string.Join(", ", _copperBase.Select(kv => $"{kv.Key} {kv.Value:F2}")));
            ApplyCopper();
            return _copper;
        }

        // ---- the eyes: a colour of each Cogling's own (the glow map is white where the eyes are; the colour is the emission's), set on
        // his renderer alone (a MaterialPropertyBlock), so one Cogling's eyes can show what he's doing without changing the others'

        /// <summary>This Cogling's eyes in <paramref name="colour"/>; null: back to the Eye colour setting.</summary>
        internal void SetEyes(Color? colour)
        {
            _eyeColor = colour;
            ShowEyes();
        }

        /// <summary>The eyes of the Cogling nearest a spot within a distance: their colour now, and how lit (0: powered down).</summary>
        internal static bool EyesNear(Vector3 at, float within, out Color colour, out float level)
        {
            colour = Plugin.EyeColor.Value;
            level = 1f;
            CoglingBody best = null;
            float bestDistance = within;
            foreach (CoglingBody b in Live)
            {
                if (b == null) continue;
                float d = Vector3.Distance(b.transform.position, at);
                if (d < bestDistance) { best = b; bestDistance = d; }
            }
            if (best == null) return false;
            colour = best._eyeColor ?? Plugin.EyeColor.Value;
            level = best._eyeLevel;
            return true;
        }

        private void ShowEyes()
        {
            if (Skin == null) return;
            if (_eyes == null) _eyes = new MaterialPropertyBlock();
            Skin.GetPropertyBlock(_eyes, 0);
            _eyes.SetColor("_EmissionColor", (_eyeColor ?? Plugin.EyeColor.Value) * Plugin.EyeGlow.Value * _eyeLevel);
            Skin.SetPropertyBlock(_eyes, 0);   // (the wood submesh only: the copper has no glow)
        }

        /// <summary>The Eye colour and Eye glow settings, on every Cogling here.</summary>
        internal static void UpdateEyes()
        {
            foreach (CoglingBody b in Live) if (b != null) b.ShowEyes();
        }

        /// <summary>The Cogling shown nearest a spot, within <paramref name="range"/> metres; null if none.</summary>
        internal static CoglingBody Nearest(Vector3 at, float range)
        {
            CoglingBody best = null;
            float bestDistance = range;
            foreach (CoglingBody b in Live)
            {
                if (b == null || b.Skin == null) continue;
                float d = Vector3.Distance(b.transform.position, at);
                if (d <= bestDistance) { best = b; bestDistance = d; }
            }
            return best;
        }

        private void Start()
        {
            if (_to != null) CoglingStance.Apply(GetComponentInParent<Humanoid>(), _animator);
        }

        private void LateUpdate()
        {
            if (_from == null || _to == null) return;
            // (both roots unscaled: with the creature's root scaled, the robot was drawn far from its body)
            _from.GetHumanPose(ref _pose);
            Idle();
            Work();
            _to.SetHumanPose(ref _pose);
            Ground();
            Carrying();
            Hammering();
            Look();
            Hold();
        }

        // ---- standing still: his own idle (reference/Idle.fbx), eased in when he stops and out when he walks (the Fuling walk)

        private ClipPlayer _idle;
        private HumanPose _idlePose;
        private float _idleWeight, _idleStart, _speed, _stillFor;
        private HumanPose _walkSnap;   // his pose as he stopped walking
        private bool _snapped;
        private Vector3 _lastPos;

        private void Idle()
        {
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector3 moved = transform.position - _lastPos;
            moved.y = 0f;
            _lastPos = transform.position;
            _speed = Mathf.Lerp(_speed, moved.magnitude / dt, 1f - Mathf.Exp(-dt * 8f));   // (smoothed: the same on every player's game)
            // the idle once he has stood still a tenth of a second (slowing for a turn mid-walk made him half straighten and hunch
            // again), blended from a snapshot of his last walking pose (the walk's own animator switches to the Fuling idle as soon as
            // he stops, and that showed for a moment), gently; out quickly, back to the live walk, when he moves again
            _stillFor = _speed < 0.25f ? _stillFor + dt : 0f;
            // (seated or getting up: still, whatever nudges his body gets, else the Fuling animation showed through)
            bool idle = _stillFor > 0.1f || _sleepWeight > 0f || Time.time < _greetUntil || _sitStart >= 0f || (_standStart >= 0f && _speed < 0.5f);
            if (idle && !_snapped && _pose.muscles != null)
            {
                _walkSnap = new HumanPose { bodyPosition = _pose.bodyPosition, bodyRotation = _pose.bodyRotation, muscles = (float[])_pose.muscles.Clone() };
                _snapped = true;
            }
            if (!idle && _idleWeight <= 0f) _snapped = false;
            // a new idle is chosen as he comes to a stop (only then: while walking with something, choosing every frame flipped his
            // arms between the two idles many times a second, and he shook)
            if (idle && !_wasIdle) _variant = null;
            _wasIdle = idle;
            _idleWeight = Mathf.MoveTowards(_idleWeight, idle ? 1f : 0f, dt * (idle ? 1.4f : 3f));
            if (_sleepWeight > 0f) _idleWeight = Mathf.Max(_idleWeight, _sleepWeight);
            // powered down: into the sad idle, frozen on one frame; booting up: out of it (Plugin.PowerPreview shows it)
            bool asleep = _asleep || Plugin.PowerPreview.Value;
            _sleepWeight = Mathf.MoveTowards(_sleepWeight, asleep ? 1f : 0f, dt / (asleep ? 1.6f : 1.2f));
            Flicker(dt);
            if (_idle == null || !Plugin.UseIdle.Value || _pose.muscles == null) return;
            if (_idleWeight <= 0f && _carry <= 0f) return;

            // which idle: now and then the happy one; chosen as he stops, and again each time the one playing comes round. Walking
            // (with something to carry), his arms are his usual idle's
            if (idle && Time.time >= _greetUntil) { if (_variant == null || Time.time >= _variantEnds) PickVariant(); }
            ClipPlayer playing = idle && _variant != null ? _variant : _idle;
            float at = Time.time - (playing == _variant ? _variantStart : _idleStart);
            if (_scared && idle && _sad != null) { playing = _sad; at = Time.time; }   // (frightened, standing: cowering)
            playing.Pose(at, ref _idlePose);
            if (_idlePose.muscles == null) return;
            if (idle && _variantBlend < 1f && _previous != null)
            {
                _variantBlend = Mathf.MoveTowards(_variantBlend, 1f, dt / 0.6f);
                _previous.Pose(Time.time - _previousStart, ref _otherPose);
                if (_otherPose.muscles != null) Mix(ref _idlePose, _otherPose, 1f - Mathf.SmoothStep(0f, 1f, _variantBlend));
            }
            if (_sleepWeight > 0f && _sad != null)
            {
                _sad.Pose(_sad.Length * Mathf.Clamp01(Plugin.PowerFrame.Value) * 0.999f, ref _otherPose);
                if (_otherPose.muscles != null) Mix(ref _idlePose, _otherPose, Mathf.SmoothStep(0f, 1f, _sleepWeight));
            }

            Sitting(dt);
            PlayGesture(idle);

            float w = Mathf.SmoothStep(0f, 1f, _idleWeight);
            if (w > 0f)
            {
                // standing: from the snapshot (not the live Fuling idle); walking off: from the live walk
                HumanPose under = idle && _snapped ? _walkSnap : _pose;
                _pose.bodyPosition = Vector3.Lerp(under.bodyPosition, _idlePose.bodyPosition, w);
                _pose.bodyRotation = Quaternion.Slerp(under.bodyRotation, _idlePose.bodyRotation, w);
                for (int i = 0; i < _pose.muscles.Length && i < _idlePose.muscles.Length && i < under.muscles.Length; i++)
                    _pose.muscles[i] = Mathf.Lerp(under.muscles[i], _idlePose.muscles[i], w);
            }
            // carrying, his arms and shoulders are the idle's whether he walks or not (the Fuling walk swings them apart), and the
            // carry pose goes on top (Work)
            if (_carry > 0f)
                foreach (int i in ArmMuscles())
                    if (i < _pose.muscles.Length && i < _idlePose.muscles.Length) _pose.muscles[i] = Mathf.Lerp(_pose.muscles[i], _idlePose.muscles[i], _carry);
        }

        private ClipPlayer _happy, _sad, _variant, _previous;
        private HumanPose _otherPose;
        private float _variantStart, _variantEnds, _previousStart, _variantBlend = 1f;
        private bool _asleep, _wasIdle, _scared;
        private int _mood = -1;
        private float _greetUntil;

        /// <summary>Greeted: his happy idle for a moment, wherever he was (his owner stops him and turns him to the greeter).</summary>
        internal void Greet()
        {
            _greetUntil = Time.time + 2.6f;
            if (_happy == null) return;
            if (_variant != _happy) { _previous = _variant; _previousStart = _variantStart; _variantBlend = _variant != null ? 0f : 1f; }
            _variant = _happy;
            _variantStart = Time.time;
            _variantEnds = Time.time + _happy.Length * 2f;
        }
        private float _sleepWeight, _eyeLevel = 1f, _flickerUntil, _nextFlick;

        /// <summary>The idle to play next: the happy one now and then (Happy idle chance), else his usual; a change is cross-faded.</summary>
        private void PickVariant()
        {
            ClipPlayer slack = SlackClip();
            ClipPlayer next = slack ?? (_happy != null && Random.value < Plugin.HappyIdleChance.Value ? _happy : _idle);
            if (_variant != null && next != _variant)
            {
                _previous = _variant;
                _previousStart = _variantStart;
                _variantBlend = 0f;
            }
            if (next != _variant) _variantStart = Time.time;
            _variant = next;
            // his usual idle once round (8 s), the happy one twice (it's short): then choose again
            _variantEnds = slack != null ? float.MaxValue : Time.time + next.Length * (next == _happy ? 2f : 1f);
        }

        // ---- slacking off (CoglingSlack decides; saved on him, so every game shows it)

        private int _slack;
        private float _slackStart, _sitStart = -1f, _standStart = -1f, _seatWeight;
        private HumanPose _sitPose, _sitPose2;
        private readonly Dictionary<string, ClipPlayer> _clips = new Dictionary<string, ClipPlayer>();

        /// <summary>One of his clips, made the first time it's needed (each plays on a hidden skeleton of its own).</summary>
        private ClipPlayer Clip(string name)
        {
            if (_clips.TryGetValue(name, out ClipPlayer p)) return p;
            CoglingClip clip = CoglingClip.Get(name);
            return _clips[name] = clip != null && _avatar != null ? new ClipPlayer(clip, _avatar) : null;
        }

        internal static float ClipLength(string name) => CoglingClip.Get(name)?.Length ?? 10f;
        internal static float GameClipLength(string name) => ClipPlayer.GameClips().FirstOrDefault(c => c.name == name)?.length ?? 2f;

        /// <summary>One of the game's player animations, made the first time it's needed.</summary>
        private ClipPlayer GameClip(string name)
        {
            string key = "game:" + name;
            if (_clips.TryGetValue(key, out ClipPlayer p)) return p;
            return _clips[key] = _avatar != null ? ClipPlayer.Game(name, _avatar) : null;
        }

        // ---- a gesture (one of the player's emotes, once through, over his idle while he stands; CoglingVoice's RPC starts it)

        private ClipPlayer _gesture;
        private float _gestureStart;


        /// <summary>A gesture's clip: the Social pack's (social_...) or one of the player's emotes.</summary>
        /// <summary>A gesture's clip: one of his own (the Social pack's social_..., Rig_Medium's rig_...) or one of the player's emotes.</summary>
        private static bool Own(string name) => name.StartsWith("social_") || name.StartsWith("rig_");
        private ClipPlayer GestureClip(string name) => Own(name) ? Clip(name) : GameClip(name);
        internal static float GestureLength(string name) => Own(name) ? ClipLength(name) : GameClipLength(name);

        internal void Gesture(string name)
        {
            ClipPlayer g = GestureClip(name);
            if (g == null) return;
            _gesture = g;
            _gestureStart = Time.time;
        }

        private void PlayGesture(bool idle)
        {
            if (_gesture == null) return;
            float t = Time.time - _gestureStart;
            if (t >= _gesture.Length || _sitStart >= 0f) { _gesture = null; return; }
            _gesture.Pose(t, ref _otherPose);
            if (_otherPose.muscles == null) return;
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_gesture.Length - t) / 0.4f));
            if (!idle) w *= 0.5f;   // (walking: only half of it, over his walk)
            Mix(ref _idlePose, _otherPose, w);
        }
        internal static float StandUpSeconds => ClipLength("sit_floor_standup") / Mathf.Max(0.1f, Plugin.SitStandSpeed.Value);
        internal static float HopDownSeconds => ClipLength("sit_chair_standup") + HopDownOut + 0.35f;   // (his body reads his state every 0.2 s: a little margin)
        private const float HopDownOut = 0f;   // (into his idle while the clip still plays: a frozen last frame after it hitched)

        /// <summary>The clip a break plays standing (in place of his idle), or null.</summary>
        private ClipPlayer SlackClip()
        {
            switch ((CoglingSlack.Activity)_slack)
            {
                case CoglingSlack.Activity.LookAround: return Clip("looking_around");
                case CoglingSlack.Activity.Bored: return Clip("bored");
                case CoglingSlack.Activity.Dance: return Clip("robot_dance");
            }
            if (_slack == CoglingSlack.PoseHeadbang) return GameClip("Headbang");
            if (_slack == CoglingSlack.PoseVikingDance) return GameClip("Dance");
            string talk = CoglingSlack.TalkClip(_slack) ?? CoglingSlack.CheerClip(_slack);
            if (talk != null) return Clip(talk);
            if (_slack == (int)CoglingSlack.Activity.Watch) return _idle;
            if (_slack == (int)CoglingSlack.Activity.Exercise) return Clip("rig_push_ups");
            if (_slack == CoglingSlack.PoseSitUps) return Clip("rig_sit_ups");   // (watching you: his usual idle, never the happy one)
            return null;
        }

        private void SlackChanged(int slack)
        {
            bool wasSitting = _slack == (int)CoglingSlack.Activity.Sit || _slack == (int)CoglingSlack.Activity.Chair || _slack == (int)CoglingSlack.Activity.Nap;
            _slack = slack;
            _slackStart = Time.time;
            if (slack == (int)CoglingSlack.Activity.Sit || slack == (int)CoglingSlack.Activity.Chair || slack == (int)CoglingSlack.Activity.Nap)
            {
                _sitStart = Time.time;
                _standStart = -1f;
                _lying = slack == (int)CoglingSlack.Activity.Nap;
                _onChair = slack == (int)CoglingSlack.Activity.Chair;
                if (_onChair) _seat = _view != null && _view.IsValid() ? _view.GetZDO().GetVec3(CoglingSlack.SeatKey, transform.position) : transform.position;
            }
            else if (wasSitting) { _sitStart = -1f; _standStart = Time.time; }
            // the clip changes at once (cross-faded), from its start
            if (_variant != null || SlackClip() != null) PickVariant();
            if (SlackClip() != null) _variantStart = Time.time;
        }

        /// <summary>
        /// Sitting: sitting down (Rig_Medium's Sit_Floor_Down, another rig's clip retargeted, up to Sit: down clip until, then into
        /// his sitting pose), the sitting pose (Mixamo's), and getting up (Sit_Floor_StandUp, at Sit: stand up speed), over his idle,
        /// on the ground (Ground keeps his feet there). Walking off (fleeing) ends it at once.
        /// </summary>
        private void Sitting(float dt)
        {
            if (_onChair) { OnChair(dt); return; }
            float target = 0f;
            if (_sitStart >= 0f)
            {
                ClipPlayer down = Clip(_lying ? "rig_lie_down" : "sit_floor_down"), sit = Clip(_lying ? "rig_lie_idle" : "sitting");
                // (a nap: the lying-down clip whole, then the lying idle, looped; a sit: part of the sit-down clip, then the sitting pose)
                float t = Time.time - _sitStart, cut = _lying && down != null ? down.Length - 0.05f : Plugin.SitDownUntil.Value;
                float blend = _lying ? 0.4f : Mathf.Max(0.05f, Plugin.SitBlend.Value);
                if (down != null && sit != null)
                {
                    down.Pose(Mathf.Min(t, cut, down.Length - 0.001f), ref _sitPose);
                    sit.Pose(_lying ? Mathf.Max(0f, t - cut) : 0f, ref _sitPose2);
                    if (_sitPose.muscles != null && _sitPose2.muscles != null)
                    {
                        // (eased out, not in: the sit-down slows to a stop at its end, and an eased-in blend on top of that paused him)
                        float into = Mathf.InverseLerp(cut - blend, cut, t);
                        Mix(ref _sitPose, _sitPose2, 1f - (1f - into) * (1f - into));
                        Mix(ref _idlePose, _sitPose, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f)));
                        // sat down: his head bobs along to his happy idle
                        if (!_lying) HappyHead(Time.time - _sitStart, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cut, cut + 0.8f, t)));
                    }
                }
                target = Mathf.Clamp01(t / Mathf.Max(0.1f, cut));
            }
            else if (_standStart >= 0f)
            {
                float speed = _lying ? 1f : Mathf.Max(0.1f, Plugin.SitStandSpeed.Value), s = (Time.time - _standStart) * speed;
                ClipPlayer up = Clip(_lying ? "rig_lie_standup" : "sit_floor_standup");
                if (up == null || s >= up.Length || _speed > 0.5f) _standStart = -1f;   // (up, or off and walking: his walk at once)
                else
                {
                    up.Pose(s, ref _sitPose);
                    if (_sitPose.muscles != null)
                    {
                        // in from the sitting pose, out into his idle over the last part
                        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(up.Length, up.Length - 0.8f, s));
                        Mix(ref _idlePose, _sitPose, w);
                        if (!_lying) HappyHead(Time.time - _standStart, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 0.6f)));   // (eased out as he gets up)
                    }
                    target = 1f - Mathf.Clamp01(s / (up.Length * 0.5f));
                }
            }
            _seatWeight = Mathf.MoveTowards(_seatWeight, target, dt * 3f);
        }

        private bool _onChair, _lying;

        /// <summary>Napping (lain down, not yet getting up): his eyes dim, as if recharging.</summary>
        private bool Recharging => _lying && _sitStart >= 0f && Time.time - _sitStart > ClipLength("rig_lie_down") * 0.7f;
        internal static float LieUpSeconds => ClipLength("rig_lie_standup");
        private Vector3 _seat;
        private float _hop;   // 0: on his feet in front of the chair, 1: on its seat
        private float _leave = 1f;   // (walking off before the hop down has eased into his idle: what's left of it, faded out)

        /// <summary>
        /// On a chair (Rig_Medium's Sit_Chair_Down, Idle and StandUp, retargeted): he stands with his back to it and hops up, his
        /// hips carried from where they stand to the seat (Ground) with a little hop over the way; down again the same way back.
        /// </summary>
        private void OnChair(float dt)
        {
            float hop = 0f;
            if (_sitStart >= 0f)
            {
                float t = Time.time - _sitStart;
                ClipPlayer down = Clip("sit_chair_down"), sit = Clip("sit_chair_idle");
                if (down != null && sit != null)
                {
                    float length = down.Length;
                    down.Pose(Mathf.Min(t, length - 0.001f), ref _sitPose);
                    sit.Pose(Mathf.Max(0f, t - length), ref _sitPose2);
                    if (_sitPose.muscles != null && _sitPose2.muscles != null)
                    {
                        float into = Mathf.InverseLerp(length - 0.25f, length, t);
                        Mix(ref _sitPose, _sitPose2, 1f - (1f - into) * (1f - into));
                        Mix(ref _idlePose, _sitPose, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.2f)));
                    }
                    hop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(length * 0.1f, length * 0.85f, t));
                }
            }
            else if (_standStart >= 0f)
            {
                float s = Time.time - _standStart;
                ClipPlayer up = Clip("sit_chair_standup");
                // (walking off early: what's left of the clip fades out quickly, not at once: that snapped)
                if (_speed > 0.5f) _leave = Mathf.MoveTowards(_leave, 0f, dt * 4f);
                if (up == null || s >= up.Length + HopDownOut || _leave <= 0f) { _standStart = -1f; _onChair = false; _leave = 1f; }
                else
                {
                    up.Pose(Mathf.Min(s, up.Length - 0.001f), ref _sitPose);
                    if (_sitPose.muscles != null)
                        Mix(ref _idlePose, _sitPose, _leave * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(up.Length - Plugin.ChairBlendOut.Value, up.Length, s))));
                    hop = _leave * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(up.Length * 0.1f, up.Length * 0.8f, s)));
                }
            }
            else _onChair = false;
            _hop = hop;
            _seatWeight = 0f;
        }

        private static int[] _headMuscles;

        /// <summary>His head and neck as in his happy idle (looping from a time), over the pose so far, by a weight.</summary>
        private void HappyHead(float time, float weight)
        {
            if (weight <= 0f || _happy == null) return;
            if (_headMuscles == null)
                _headMuscles = HumanTrait.MuscleName.Select((n, i) => (n, i)).Where(x => x.n.StartsWith("Neck ") || x.n.StartsWith("Head ")).Select(x => x.i).ToArray();
            _happy.Pose(time, ref _otherPose);
            if (_otherPose.muscles == null) return;
            foreach (int i in _headMuscles)
                if (i < _idlePose.muscles.Length && i < _otherPose.muscles.Length) _idlePose.muscles[i] = Mathf.Lerp(_idlePose.muscles[i], _otherPose.muscles[i], weight);
        }

        // ---- where he looks (Good Tidings' Dagny: after the pose, his head turned toward a point, his upper spine a little)

        private Transform _head;
        private Vector3 _chatWith;
        private bool Chatting => CoglingSlack.Talking(_slack) || _slack == CoglingSlack.PoseListen;
        private ZDOID _watchId = ZDOID.None;
        private float _lookWeight;
        private Vector3 _lookPoint, _lookVel;

        /// <summary>Where he looks now, or null (straight on): staring into the distance, his gaze drifts slowly.</summary>
        private Vector3? LookTarget()
        {
            if (_head == null) return null;
            if (Chatting) return _chatWith + Vector3.up * Plugin.Height.Value * 0.8f;   // (at the other one's face)
            if (_slack == (int)CoglingSlack.Activity.Watch && !_watchId.IsNone())
            {
                // watching you: his eyes on yours, wherever you go
                GameObject who = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(_watchId) : null;
                Character c = who != null ? who.GetComponent<Character>() : null;
                return c != null ? c.GetHeadPoint() : (Vector3?)null;
            }
            if (_slack != (int)CoglingSlack.Activity.Stare) return null;
            float t = Time.time - _slackStart;
            return _head.position + transform.forward * 10f + transform.right * (Mathf.Sin(t * 0.21f) * 2.5f + Mathf.Sin(t * 0.07f) * 1.5f)
                   + Vector3.up * (1.2f + Mathf.Sin(t * 0.13f) * 1.2f);
        }

        private void Look()
        {
            if (_head == null) return;
            Vector3? target = LookTarget();
            if (target is Vector3 p)
            {
                if (_lookWeight <= 0.001f) _lookPoint = p;
                _lookPoint = Vector3.SmoothDamp(_lookPoint, p, ref _lookVel, _slack == (int)CoglingSlack.Activity.Watch ? 0.25f : 0.8f);   // (watching: close behind)
            }
            _lookWeight = Mathf.MoveTowards(_lookWeight, target.HasValue ? 1f : 0f, Time.deltaTime * 1.2f);
            if (_lookWeight <= 0f) return;
            Vector3 ahead = transform.forward, to = _lookPoint - _head.position;
            float w = _lookWeight * Mathf.InverseLerp(120f, 80f, Vector3.Angle(ahead, to));   // (round behind him: let go)
            if (w <= 0f) return;
            Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(ahead, to), 70f);
            if (_chest != null) _chest.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.15f * w) * _chest.rotation;
            _head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.85f * w) * _head.rotation;
        }

        private static void Mix(ref HumanPose a, HumanPose b, float w)
        {
            a.bodyPosition = Vector3.Lerp(a.bodyPosition, b.bodyPosition, w);
            a.bodyRotation = Quaternion.Slerp(a.bodyRotation, b.bodyRotation, w);
            for (int i = 0; i < a.muscles.Length && i < b.muscles.Length; i++) a.muscles[i] = Mathf.Lerp(a.muscles[i], b.muscles[i], w);
        }

        /// <summary>Powered down or up (his saved state, as his owner decides): the eyes flicker, then stay off or on.</summary>
        private void SetAsleep(bool asleep)
        {
            if (asleep == _asleep) return;
            _asleep = asleep;
            _flickerUntil = Time.time + 0.9f;
        }

        private void Flicker(float dt)
        {
            float target = _asleep ? 0f : Recharging ? 0.25f : 1f;
            if (Time.time < _flickerUntil)
            {
                // a few uneven blinks, like a bulb catching (or failing)
                if (Time.time >= _nextFlick)
                {
                    _nextFlick = Time.time + Random.Range(0.04f, 0.14f);
                    float settle = 1f - (_flickerUntil - Time.time) / 0.9f;   // 0 at the start, 1 at the end
                    _eyeLevel = Random.value < Mathf.Lerp(0.5f, target > 0f ? 0.9f : 0.1f, settle) ? 1f : 0.05f;
                    ShowEyes();
                }
                return;
            }
            if (!Mathf.Approximately(_eyeLevel, target)) { _eyeLevel = target; ShowEyes(); }
        }

        private static int[] _armMuscles;

        private static int[] ArmMuscles()
        {
            if (_armMuscles != null) return _armMuscles;
            var list = new List<int>();
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
                if (names[i].Contains("Shoulder") || names[i].Contains(" Arm ") || names[i].Contains("Forearm") || names[i].Contains("Hand")) list.Add(i);
            return _armMuscles = list.ToArray();
        }

        private Transform _hips, _leftFoot, _rightFoot;
        private float _footRest;   // how high his ankles are above the ground standing in his model (the soles are below them)

        /// <summary>
        /// His feet on the ground: the idle clip's hips were placed for Mixamo's own standing pose (copied onto his body they left him
        /// floating), and the walk carries him at its own height. The lower ankle is kept at its standing height above the ground (his
        /// body's origin), every frame.
        /// </summary>
        private void Ground()
        {
            // always, walking too, and smoothed: easing it out as he started walking let the walk's own height pop back (a little hop)
            if (_hips == null || _leftFoot == null || _rightFoot == null) return;
            float ankle = Mathf.Min(_leftFoot.position.y, _rightFoot.position.y);
            float off = (transform.position.y + _footRest) - ankle;
            // (exactly, every frame: smoothing it lagged behind the pose, and the body rose and settled as he straightened into the idle)
            _hips.position += Vector3.up * off;
            // sitting on the ground: the pose's feet are on the ground already; Sit: height / forward nudge him, eased on and off
            if (_seatWeight > 0f)
                _hips.position += (Vector3.up * Plugin.SitHeight.Value + transform.forward * Plugin.SitForward.Value) * Mathf.SmoothStep(0f, 1f, _seatWeight);
            // on a chair: his hips on its seat (Chair: height / back), hopped up to and down from (Chair: hop)
            if (_onChair && _hop > 0f)
            {
                Vector3 seated = _seat + Vector3.up * Plugin.ChairHeight.Value - transform.forward * Plugin.ChairBack.Value;
                Vector3 was = _hips.position;
                _hips.position = Vector3.Lerp(_hips.position, seated, _hop) + Vector3.up * Mathf.Sin(Mathf.PI * _hop) * Plugin.ChairHop.Value;
                Shift(_hips.position - was);
            }
            else Shift(Vector3.zero);
            if (Plugin.LogGround.Value && Time.time > _nextGroundLog) { _nextGroundLog = Time.time + 0.1f; Plugin.Log.LogInfo($"Cogling ground: idle {_idleWeight:F2} speed {_speed:F2} lift {off:F3} hips {_hips.position.y - transform.position.y:F3}"); }
        }

        private float _nextGroundLog;

        // ---- his name and health bar over him wherever his body is (on a chair it's moved off his character: CoglingTopPoint)

        /// <summary>Coglings whose body is moved off their character now, and by how much.</summary>
        internal static readonly Dictionary<Character, Vector3> Shifted = new Dictionary<Character, Vector3>();
        private Character _character;

        private void Shift(Vector3 by)
        {
            if (_character == null) _character = GetComponentInParent<Character>();
            if (_character == null) return;
            if (by.sqrMagnitude < 0.0001f) Shifted.Remove(_character);
            else Shifted[_character] = by;
        }

        private void OnDisable() { if (_character != null) Shifted.Remove(_character); }

        // ---- working: the carry pose, bending down, and the carried item in his hands, from his saved state (every player sees them)

        private Transform _leftHand, _rightHand, _chest, _upperArmR, _foreArmR, _upperArmL, _foreArmL;
        private Quaternion _handRestR = Quaternion.identity, _handRestL = Quaternion.identity;
        private ZNetView _view;
        private string _heldName = "";
        private GameObject _held;
        private Vector3 _heldCentre;
        private float _heldBiggest;
        private float _carry, _bend, _hammer, _nextKnock, _nextRead;
        private static GameObject _knockSound;

        /// <summary>
        /// Hammering: both arms aimed in his own body's directions (up, forward, out to his side), not by Unity's arm muscles, whose
        /// zero hangs about 40 degrees down and 30 forward, so every muscle swing ran on a slant. The right arm's upper arm held still,
        /// the forearm swinging at the elbow and the wrist flicking, in the arm's own upright plane; the left arm held still.
        /// </summary>
        private void Carrying()
        {
            if (_carry <= 0f) return;
            float raise = Plugin.CarryRaise.Value, outward = Plugin.CarryOutward.Value, twist = Plugin.CarryTwist.Value, elbow = Plugin.CarryElbow.Value, wrist = Plugin.CarryWrist.Value;
            Aim(_upperArmR, _foreArmR, _rightHand, _handRestR, 1f, raise, outward, twist, elbow, wrist, 0f, _carry);
            Aim(_upperArmL, _foreArmL, _leftHand, _handRestL, -1f, raise, outward, -twist, elbow, wrist, 0f, _carry);
        }

        private void Hammering()
        {
            if (_hammer <= 0f) return;
            float top = Plugin.SwingFreeze.Value ? Plugin.SwingFrozenAt.Value   // (testing: held still at one point of the swing, to tune)
                : Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, Plugin.SwingTime.Value)) * 0.5f + 0.5f;   // 1: the top, 0: the knock
            Aim(_upperArmR, _foreArmR, _rightHand, _handRestR, 1f, Plugin.SwingRaise.Value, Plugin.SwingOut.Value, Plugin.SwingTwist.Value,
                Mathf.Lerp(Plugin.SwingElbowLow.Value, Plugin.SwingElbowHigh.Value, top), Mathf.Lerp(Plugin.SwingWristLow.Value, Plugin.SwingWristHigh.Value, top), Plugin.SwingHandTurn.Value, _hammer);
            Aim(_upperArmL, _foreArmL, _leftHand, _handRestL, -1f, Plugin.SwingLeftRaise.Value, Plugin.SwingLeftOut.Value, 0f, Plugin.SwingLeftElbow.Value, 0f, 0f, _hammer);
        }

        /// <summary>
        /// One arm aimed: the upper arm raised (degrees from hanging down; 90 is straight ahead, level) and turned out to his side (degrees
        /// from straight ahead; below 0 is across him), twisted about itself; the forearm bent up from it at the elbow, in the upright
        /// plane through the upper arm; the hand bent on from the forearm and turned about it.
        /// </summary>
        private void Aim(Transform upper, Transform fore, Transform hand, Quaternion handRest, float side, float raise, float outward, float twist, float elbow, float wrist, float handTurn, float w)
        {
            if (upper == null || fore == null || hand == null) return;
            Transform body = transform;
            Vector3 up = body.up, ahead = body.forward, sideways = body.right * side;
            float r = raise * Mathf.Deg2Rad, o = outward * Mathf.Deg2Rad;
            Vector3 dir = (-up * Mathf.Cos(r) + (ahead * Mathf.Cos(o) + sideways * Mathf.Sin(o)) * Mathf.Sin(r)).normalized;
            Vector3 bendToward = Vector3.ProjectOnPlane(up, dir);   // (the way the elbow bends: up, square to the arm)
            if (bendToward.sqrMagnitude < 0.01f) bendToward = Vector3.ProjectOnPlane(ahead, dir);
            bendToward.Normalize();
            Vector3 Bent(float degrees) => (dir * Mathf.Cos(degrees * Mathf.Deg2Rad) + bendToward * Mathf.Sin(degrees * Mathf.Deg2Rad)).normalized;

            Quaternion turn = Quaternion.AngleAxis(twist, dir) * Quaternion.FromToRotation(fore.position - upper.position, dir);
            upper.rotation = Quaternion.Slerp(Quaternion.identity, turn, w) * upper.rotation;
            Vector3 foreDir = Bent(elbow);
            turn = Quaternion.AngleAxis(handTurn, foreDir) * Quaternion.FromToRotation(hand.position - fore.position, foreDir);
            fore.rotation = Quaternion.Slerp(Quaternion.identity, turn, w) * fore.rotation;
            // (the hand: straight on from the forearm as he was made, then bent up by the wrist)
            Quaternion straight = fore.rotation * handRest;
            Quaternion bent = Quaternion.FromToRotation(foreDir, Bent(elbow + wrist)) * straight;
            hand.rotation = Quaternion.Slerp(hand.rotation, bent, w);
        }
        private GameObject _tool;

        /// <summary>
        /// The game's own hammer in his right hand while he hammers (the hammer item's "attach" part, the one a player holds), placed by
        /// the Hammer: settings (testing, live: Hammer: preview shows it), hidden otherwise.
        /// </summary>
        private void ShowTool(bool show)
        {
            if (!show) { if (_tool != null && _tool.activeSelf) _tool.SetActive(false); return; }
            if (_tool == null)
            {
                Transform attach = ZNetScene.instance?.GetPrefab("Hammer")?.transform.Find("attach");
                if (attach == null || _rightHand == null) return;
                _tool = Instantiate(attach.gameObject, _rightHand, false);
                _tool.name = "cogling_hammer";
                foreach (Collider c in _tool.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (Transform t in _tool.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            }
            if (!_tool.activeSelf) _tool.SetActive(true);
            _tool.transform.localPosition = new Vector3(Plugin.HammerX.Value, Plugin.HammerY.Value, Plugin.HammerZ.Value) / 100f;
            _tool.transform.localRotation = Quaternion.Euler(Plugin.HammerTurnX.Value, Plugin.HammerTurnY.Value, Plugin.HammerTurnZ.Value);
            _tool.transform.localScale = Vector3.one * Plugin.HammerSize.Value;
        }

        /// <summary>The hammer's knock (the game's own building sound), played here on this game only.</summary>
        private void Knock()
        {
            if (_knockSound == null) _knockSound = ZNetScene.instance?.GetPrefab("sfx_build_hammer_wood");
            if (_knockSound == null) return;
            ZNetView.m_forceDisableInit = true;
            try { Destroy(Instantiate(_knockSound, transform.position + transform.forward * 0.4f + Vector3.up * 0.4f, Quaternion.identity), 3f); }
            finally { ZNetView.m_forceDisableInit = false; }
        }
        private int _act;
        private static int[] _muscles;   // the muscles the work poses move (Unity's humanoid muscle indices)
        private const int ArmUpL = 0, ArmFrontL = 1, ElbowL = 2, ArmUpR = 3, ArmFrontR = 4, ElbowR = 5, Spine = 6, Chest = 7;

        private static int[] Muscles()
        {
            if (_muscles != null) return _muscles;
            string[] names = HumanTrait.MuscleName;
            int Find(string n) => System.Array.IndexOf(names, n);
            return _muscles = new[]
            {
                Find("Left Arm Down-Up"), Find("Left Arm Front-Back"), Find("Left Forearm Stretch"),
                Find("Right Arm Down-Up"), Find("Right Arm Front-Back"), Find("Right Forearm Stretch"),
                Find("Spine Front-Back"), Find("Chest Front-Back"),
            };
        }

        private void Work()
        {
            if (Time.time > _nextRead)
            {
                _nextRead = Time.time + 0.2f;
                string name = "";
                if (_view != null && _view.IsValid())
                {
                    ZDO zdo = _view.GetZDO();
                    _act = zdo.GetInt(Carried.ActKey);
                    int slack = zdo.GetInt(CoglingSlack.SlackKey);
                    if (slack != _slack) SlackChanged(slack);
                    if (Chatting) _chatWith = zdo.GetVec3(CoglingSlack.LookKey, transform.position + transform.forward * 3f);
                    _watchId = _slack == (int)CoglingSlack.Activity.Watch ? zdo.GetZDOID(CoglingSlack.WatchKey) : ZDOID.None;
                    SetAsleep(!zdo.GetBool(Carried.AwakeKey));
                    int mood = zdo.GetInt(Carried.MoodKey);
                    if (mood != _mood)
                    {
                        _mood = mood;
                        _scared = mood == Carried.MoodScared;
                        // frightened: red; wary: yellow; calm: his own colour
                        SetEyes(mood == Carried.MoodScared ? new Color(1f, 0.12f, 0.08f) : mood == Carried.MoodWary ? new Color(1f, 0.82f, 0.1f) : (Color?)null);
                    }
                    name = zdo.GetString(Carried.PrefabKey);
                }
                if (Plugin.CarryPreview.Value && string.IsNullOrEmpty(name)) name = "Wood";   // (testing: the carry pose on every Cogling)
                if (name != _heldName) { _heldName = name; ShowHeld(name); }
            }
            float dt = Time.deltaTime;
            _carry = Mathf.MoveTowards(_carry, _held != null || _act == Carried.ActDeposit ? 1f : 0f, dt * 3f);
            if (_held != null && Plugin.CarryPreview.Value) _carry = 1f;
            _bend = Mathf.MoveTowards(_bend, _act == Carried.ActBend || Plugin.BendPreview.Value ? 1f : 0f, dt * 2.5f);
            bool hammering = _act == Carried.ActHammer || Plugin.HammerPreview.Value;
            _hammer = Mathf.MoveTowards(_hammer, hammering ? 1f : 0f, dt * 4f);
            ShowTool(hammering);
            if (_act == Carried.ActHammer && !Plugin.SwingFreeze.Value && Time.time >= _nextKnock) { _nextKnock = Time.time + Mathf.Max(0.1f, Plugin.SwingTime.Value); Knock(); }
            if (Plugin.BendPreview.Value) _bend = 1f;
            if ((_carry <= 0f && _bend <= 0f && _hammer <= 0f) || _pose.muscles == null) return;
            int[] m = Muscles();
            void Set(int which, float value, float weight)
            {
                int i = m[which];
                if (i >= 0 && i < _pose.muscles.Length) _pose.muscles[i] = Mathf.Lerp(_pose.muscles[i], value, weight);
            }
            // arms forward, elbows bent, holding something in front of his chest (the Carry settings: testing, to tune by eye)
            // (the arms themselves are aimed after the pose is set: Carrying)
            // bending down to the floor, arms reaching
            float spine = Plugin.BendSpine.Value, arms = Plugin.BendArms.Value;
            Set(Spine, spine, _bend); Set(Chest, spine * 0.8f, _bend);
            Set(ArmFrontL, arms, _bend); Set(ArmFrontR, arms, _bend); Set(ArmUpL, -0.6f, _bend); Set(ArmUpR, -0.6f, _bend);
            Set(ElbowL, 0.8f, _bend); Set(ElbowR, 0.8f, _bend);
            // hammering: his right arm up and down, a knock each time it comes down (on his rig -1 is forward)
            if (_hammer > 0f)
            {
                // (the arms themselves are aimed after the pose is set: Hammering)
            }
        }

        /// <summary>The carried item's own model between his hands (the game's prefab, every working part taken off), sized to fit.</summary>
        private void ShowHeld(string name)
        {
            if (_held != null) { Destroy(_held); _held = null; }
            if (string.IsNullOrEmpty(name)) return;
            GameObject prefab = ZNetScene.instance?.GetPrefab(name);
            if (prefab == null) return;
            ZNetView.m_forceDisableInit = true;
            try { _held = Instantiate(prefab, _root); }
            finally { ZNetView.m_forceDisableInit = false; }
            _held.name = "cogling_held";
            foreach (MonoBehaviour b in _held.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
            foreach (Joint j in _held.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (Rigidbody r in _held.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
            foreach (Collider c in _held.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            // the sparkle loose items have (on the item's own root for some: then only the effect goes, not the item)
            foreach (ParticleSystem p in _held.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (p.gameObject != _held) { DestroyImmediate(p.gameObject); continue; }
                if (p.GetComponent<ParticleSystemRenderer>() is ParticleSystemRenderer pr) DestroyImmediate(pr);
                DestroyImmediate(p);
            }
            foreach (Transform t in _held.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            // its own size measured once (at scale 1, facing his way); Hold sizes and turns it by the Carry: item settings each frame
            Renderer[] rs = _held.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { Destroy(_held); _held = null; return; }
            _held.transform.localScale = Vector3.one;
            _held.transform.rotation = transform.rotation;
            Bounds box = rs[0].bounds;
            foreach (Renderer r in rs) box.Encapsulate(r.bounds);
            _heldBiggest = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
            _heldCentre = _held.transform.InverseTransformPoint(box.center);
        }

        private void Hold()
        {
            if (_held == null || _leftHand == null || _rightHand == null) return;
            // in his hands (between them), nudged by the Carry: item settings
            Vector3 forward = transform.forward;
            forward.y = 0f;
            Vector3 at = (_leftHand.position + _rightHand.position) / 2f
                         + forward.normalized * Plugin.Height.Value * Plugin.CarryOut.Value + Vector3.up * Plugin.Height.Value * Plugin.CarryHeight.Value;
            // never bigger than its own size; its biggest side at most the Carry: item size share of his height
            float scale = _heldBiggest > 0.001f ? Mathf.Min(1f, Plugin.Height.Value * Plugin.CarryItemSize.Value / _heldBiggest) : 1f;
            _held.transform.localScale = Vector3.one * scale;
            _held.transform.rotation = transform.rotation * Quaternion.Euler(0f, Plugin.CarryItemTurn.Value, 0f);
            _held.transform.position = at - _held.transform.TransformVector(_heldCentre);
        }


        private void OnDestroy()
        {
            Live.Remove(this);
            _from?.Dispose();
            _idle?.Dispose();
            _happy?.Dispose();
            _sad?.Dispose();
            _idle = _happy = _sad = null;
            foreach (ClipPlayer c in _clips.Values) c?.Dispose();
            _clips.Clear();
            _to?.Dispose();
            _from = _to = null;
            // a hot reload swaps this component for the new copy's (PieceReload.Reattach), which builds its own robot: this one's goes
            if (_root != null) { _root.gameObject.SetActive(false); Destroy(_root.gameObject); }
        }

        /// <summary>Lets go of what every Cogling shares (hot reload). Not destroyed: an old copy's Coglings may still show it.</summary>
        internal static void Forget()
        {
            _avatar = null;
            _material = null;
            Live.Clear();
            _copper = null;
        }
    }
}
