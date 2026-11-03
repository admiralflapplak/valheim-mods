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

        private static readonly Dictionary<string, CoglingClip> Loaded = new Dictionary<string, CoglingClip>();

        internal static CoglingClip Get(string name)
        {
            if (Loaded.TryGetValue(name, out CoglingClip clip)) return clip;
            using (Stream s = typeof(CoglingClip).Assembly.GetManifestResourceStream("Cogling.assets." + name + ".anim"))
            {
                if (s == null) { Plugin.Log.LogWarning($"No animation {name} in the mod"); return Loaded[name] = null; }
                var r = new BinaryReader(s);
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "ANI1") { Plugin.Log.LogWarning($"The animation {name} isn't one this version reads"); return Loaded[name] = null; }
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
