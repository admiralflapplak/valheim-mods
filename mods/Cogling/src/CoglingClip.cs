using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cogling
{
    /// <summary>
    /// An animation made for the Cogling's own skeleton (Mixamo, turned into data by tools/convert_anim.py: assets/&lt;name&gt;.anim, in
    /// the DLL): each frame's hips position and bone turns. Played on a hidden copy of his skeleton (ClipPlayer), whose humanoid pose is
    /// then blended with the game's animations on his body (CoglingBody): his idle, standing still.
    /// </summary>
    internal sealed class CoglingClip
    {
        internal string[] Bones;
        internal float Fps;
        internal Vector3[] Hips;          // per frame
        internal Quaternion[][] Rot;      // per frame, per bone (local)
        internal float Length => Rot.Length / Fps;

        // another rig's clip ("ANI2", tools/convert_rig_anim.py): its own skeleton, from its T-pose, and every bone's movement
        internal bool OwnRig;
        internal int[] Parents;
        internal Vector3[] RestPos;
        internal Quaternion[] RestRot;
        internal Vector3[][] Pos;         // per frame, per bone (local)

        /// <summary>Another rig's bone names to Unity's humanoid ones (Rig_Medium: the user's animation packs).</summary>
        internal static readonly Dictionary<string, string> RigHuman = new Dictionary<string, string>
        {
            { "hips", "Hips" }, { "spine", "Spine" }, { "chest", "Chest" }, { "head", "Head" },
            { "upperarm.l", "LeftUpperArm" }, { "lowerarm.l", "LeftLowerArm" }, { "wrist.l", "LeftHand" },
            { "upperarm.r", "RightUpperArm" }, { "lowerarm.r", "RightLowerArm" }, { "wrist.r", "RightHand" },
            { "upperleg.l", "LeftUpperLeg" }, { "lowerleg.l", "LeftLowerLeg" }, { "foot.l", "LeftFoot" }, { "toes.l", "LeftToes" },
            { "upperleg.r", "RightUpperLeg" }, { "lowerleg.r", "RightLowerLeg" }, { "foot.r", "RightFoot" }, { "toes.r", "RightToes" },
            // the Social pack's rig ("B-" bones; its fingers and jaw are left out: he has neither)
            { "B-hips", "Hips" }, { "B-spine", "Spine" }, { "B-chest", "Chest" }, { "B-neck", "Neck" }, { "B-head", "Head" },
            { "B-shoulder.L", "LeftShoulder" }, { "B-upperArm.L", "LeftUpperArm" }, { "B-forearm.L", "LeftLowerArm" }, { "B-hand.L", "LeftHand" },
            { "B-shoulder.R", "RightShoulder" }, { "B-upperArm.R", "RightUpperArm" }, { "B-forearm.R", "RightLowerArm" }, { "B-hand.R", "RightHand" },
            { "B-thigh.L", "LeftUpperLeg" }, { "B-shin.L", "LeftLowerLeg" }, { "B-foot.L", "LeftFoot" }, { "B-toe.L", "LeftToes" },
            { "B-thigh.R", "RightUpperLeg" }, { "B-shin.R", "RightLowerLeg" }, { "B-foot.R", "RightFoot" }, { "B-toe.R", "RightToes" },
        };

        private static readonly Dictionary<string, CoglingClip> Loaded = new Dictionary<string, CoglingClip>();

        internal static CoglingClip Get(string name)
        {
            if (Loaded.TryGetValue(name, out CoglingClip clip)) return clip;
            using (Stream s = typeof(CoglingClip).Assembly.GetManifestResourceStream("Cogling.assets." + name + ".anim"))
            {
                if (s == null) { Plugin.Log.LogWarning($"No animation {name} in the mod"); return Loaded[name] = null; }
                var r = new BinaryReader(s);
                string magic = Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic == "ANI2") return Loaded[name] = ReadOwnRig(r);
                if (magic != "ANI1") { Plugin.Log.LogWarning($"The animation {name} isn't one this version reads"); return Loaded[name] = null; }
                clip = new CoglingClip { Fps = r.ReadSingle() };
                int frames = r.ReadInt32(), bones = r.ReadInt32();
                clip.Bones = new string[bones];
                for (int i = 0; i < bones; i++) clip.Bones[i] = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                clip.Hips = new Vector3[frames];
                clip.Rot = new Quaternion[frames][];
                for (int f = 0; f < frames; f++)
                {
                    clip.Hips[f] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) * CoglingModel.Size;   // (the model's scale)
                    clip.Rot[f] = new Quaternion[bones];
                    for (int b = 0; b < bones; b++) clip.Rot[f][b] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                }
            }
            return Loaded[name] = clip;
        }

        private static CoglingClip ReadOwnRig(BinaryReader r)
        {
            var clip = new CoglingClip { OwnRig = true, Fps = r.ReadSingle() };
            int frames = r.ReadInt32(), bones = r.ReadInt32();
            clip.Bones = new string[bones];
            clip.Parents = new int[bones];
            clip.RestPos = new Vector3[bones];
            clip.RestRot = new Quaternion[bones];
            for (int i = 0; i < bones; i++)
            {
                clip.Bones[i] = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                clip.Parents[i] = r.ReadInt32();
                clip.RestPos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                clip.RestRot[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }
            clip.Pos = new Vector3[frames][];
            clip.Rot = new Quaternion[frames][];
            for (int f = 0; f < frames; f++)
            {
                clip.Pos[f] = new Vector3[bones];
                clip.Rot[f] = new Quaternion[bones];
                for (int b = 0; b < bones; b++)
                {
                    clip.Pos[f][b] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    clip.Rot[f][b] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                }
            }
            return clip;
        }

        // ---- another rig's skeleton, described to Unity as a humanoid (once per rig: by its bone names)

        private static readonly Dictionary<string, Avatar> RigAvatars = new Dictionary<string, Avatar>();

        /// <summary>A hidden copy of this clip's own skeleton in its T-pose, and its bones in the clip's order.</summary>
        internal GameObject BuildRig(out Transform[] bones)
        {
            var root = new GameObject("cogling_rig") { hideFlags = HideFlags.HideAndDontSave };
            bones = new Transform[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                var b = new GameObject(Bones[i]) { hideFlags = HideFlags.HideAndDontSave }.transform;
                b.SetParent(Parents[i] >= 0 ? bones[Parents[i]] : root.transform, false);
                b.localPosition = RestPos[i];
                b.localRotation = RestRot[i];
                bones[i] = b;
            }
            return root;
        }

        /// <summary>The humanoid avatar of this clip's rig (built from its T-pose), or null if Unity won't take it as a humanoid.</summary>
        internal Avatar RigAvatar()
        {
            string key = string.Join(",", Bones);
            if (RigAvatars.TryGetValue(key, out Avatar a)) return a;
            GameObject template = BuildRig(out Transform[] bones);
            try
            {
                var description = new HumanDescription
                {
                    human = bones.Where(b => RigHuman.ContainsKey(b.name))
                        .Select(b => new HumanBone { boneName = b.name, humanName = RigHuman[b.name], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = new[] { new SkeletonBone { name = template.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one } }
                        .Concat(bones.Select(b => new SkeletonBone { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = Vector3.one })).ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
                a = AvatarBuilder.BuildHumanAvatar(template, description);
                if (a == null || !a.isValid || !a.isHuman) { Plugin.Log.LogWarning("Another rig's animation: its skeleton isn't a valid humanoid"); a = null; }
                else { a.hideFlags = HideFlags.DontUnloadUnusedAsset; a.name = "cogling_rig"; }
            }
            finally { Object.Destroy(template); }
            return RigAvatars[key] = a;
        }

        internal static void Forget() => Loaded.Clear();
    }

    /// <summary>
    /// A clip playing (looping) on a hidden copy of the Cogling's skeleton; Pose gives its humanoid pose at a time. Either one of his own
    /// (CoglingClip) or one of the game's humanoid animations (the player's emotes: Unity plays a humanoid clip on any humanoid, through
    /// an animation graph on the hidden copy, evaluated by hand, as Good Tidings plays the player's riding for its trader).
    /// </summary>
    internal sealed class ClipPlayer
    {
        private readonly CoglingClip _clip;
        private readonly GameObject _rig;
        private readonly Transform[] _bones;   // the clip's bones in the rig (null where the rig has no such bone)
        private readonly HumanPoseHandler _handler;
        private PlayableGraph _graph;
        private AnimationClipPlayable _game;
        private readonly float _gameLength;

        /// <summary>One of the game's humanoid clips (by its name in the player's animations), or null if there's none such.</summary>
        internal static ClipPlayer Game(string name, Avatar avatar)
        {
            AnimationClip clip = GameClips().FirstOrDefault(c => c.name == name);
            if (clip == null) { Plugin.Log.LogWarning($"No player animation {name} to borrow (`cogling emotes` lists them)"); return null; }
            return new ClipPlayer(clip, avatar);
        }

        /// <summary>The player's animations (emotes among them).</summary>
        internal static AnimationClip[] GameClips()
        {
            Animator player = ZNetScene.instance?.GetPrefab("Player")?.GetComponentInChildren<Animator>(true);
            return player?.runtimeAnimatorController?.animationClips.Where(c => c != null).GroupBy(c => c.name).Select(g => g.First()).ToArray()
                   ?? new AnimationClip[0];
        }

        private ClipPlayer(AnimationClip clip, Avatar avatar)
        {
            _rig = CoglingBody.BuildSkeleton(null, out _).gameObject;
            _rig.hideFlags = HideFlags.HideAndDontSave;
            _bones = new Transform[0];
            var animator = _rig.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("cogling_emote");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            // (a copy without its events: the player's clips call footstep and effect events the hidden copy has no receiver for)
            AnimationClip quiet = Object.Instantiate(clip);
            quiet.name = clip.name;
            quiet.events = new AnimationEvent[0];
            _game = AnimationClipPlayable.Create(_graph, quiet);
            _game.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(_graph, "cogling_emote", animator).SetSourcePlayable(_game);
            _graph.Play();
            _gameLength = Mathf.Max(0.1f, clip.length);
            _handler = new HumanPoseHandler(avatar, _rig.transform);
        }

        internal ClipPlayer(CoglingClip clip, Avatar avatar)
        {
            _clip = clip;
            if (clip.OwnRig)
            {
                // another rig's clip: played on a copy of its own skeleton, read back as humanoid muscles (Unity retargets it)
                Avatar own = clip.RigAvatar();
                _rig = clip.BuildRig(out _bones);
                _handler = own != null ? new HumanPoseHandler(own, _rig.transform) : null;
                return;
            }
            _rig = CoglingBody.BuildSkeleton(null, out Transform[] rigBones).gameObject;
            _rig.hideFlags = HideFlags.HideAndDontSave;
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in rigBones) byName[t.name] = t;
            _bones = new Transform[clip.Bones.Length];
            for (int i = 0; i < _bones.Length; i++) byName.TryGetValue(clip.Bones[i], out _bones[i]);
            _handler = new HumanPoseHandler(avatar, _rig.transform);
        }

        internal void Pose(float time, ref HumanPose pose)
        {
            if (_graph.IsValid())
            {
                _game.SetTime(time % _gameLength);
                _graph.Evaluate(0f);
                _handler.GetHumanPose(ref pose);
                return;
            }
            float f = (time % _clip.Length) * _clip.Fps;
            int a = Mathf.FloorToInt(f) % _clip.Rot.Length, b = (a + 1) % _clip.Rot.Length;
            float t = f - Mathf.Floor(f);
            if (_clip.OwnRig)
            {
                if (_handler == null) return;
                for (int i = 0; i < _bones.Length; i++)
                {
                    _bones[i].localPosition = Vector3.Lerp(_clip.Pos[a][i], _clip.Pos[b][i], t);
                    _bones[i].localRotation = Quaternion.Slerp(_clip.Rot[a][i], _clip.Rot[b][i], t);
                }
                _handler.GetHumanPose(ref pose);
                return;
            }
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].localRotation = Quaternion.Slerp(_clip.Rot[a][i], _clip.Rot[b][i], t);
            if (_bones.Length > 0 && _bones[0] != null) _bones[0].localPosition = Vector3.Lerp(_clip.Hips[a], _clip.Hips[b], t);
            _handler.GetHumanPose(ref pose);
        }

        internal float Length => _graph.IsValid() ? _gameLength : _clip.Length;

        internal void Dispose()
        {
            _handler?.Dispose();
            if (_graph.IsValid()) _graph.Destroy();
            if (_rig != null) Object.Destroy(_rig);
        }
    }
}
