using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// An animation made outside the game (a Mixamo one, turned into data by Blender: tools in DESIGN.md), played on a character of the
    /// game's. Mods can't load an FBX at run time, so the data holds the skeleton's rest pose (a T-pose) and each frame's bone turns, already
    /// in Unity's axes. An invisible copy of that skeleton is made and described to Unity as a humanoid (AvatarBuilder); each frame it's
    /// posed from the data, and Unity's own humanoid retargeting (HumanPoseHandler) copies the pose onto the character's body, as it does
    /// between any two human rigs.
    /// </summary>
    internal sealed class MixamoClip
    {
        internal string[] Names;
        internal int[] Parents;
        internal Vector3[] RestPos;
        internal Quaternion[] RestRot;
        internal Vector3[] Hips;
        internal Quaternion[][] Rot;   // [frame][bone]
        internal float Fps;
        internal float Length => Rot.Length / Fps;

        private static readonly Dictionary<string, MixamoClip> Loaded = new Dictionary<string, MixamoClip>();

        /// <summary>A clip shipped in the mod (assets/&lt;name&gt;.json, embedded in the DLL).</summary>
        internal static MixamoClip Get(string name)
        {
            if (Loaded.TryGetValue(name, out MixamoClip clip)) return clip;
            string resource = typeof(MixamoClip).Assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name + ".json"));
            if (resource == null) { Plugin.Log.LogWarning($"No animation {name} in the mod"); return Loaded[name] = null; }
            using (Stream s = typeof(MixamoClip).Assembly.GetManifestResourceStream(resource))
            using (var reader = new StreamReader(s))
                clip = Parse(JObject.Parse(reader.ReadToEnd()));
            return Loaded[name] = clip;
        }

        private static MixamoClip Parse(JObject root)
        {
            Vector3 V(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
            Quaternion Q(JToken t) => new Quaternion((float)t[0], (float)t[1], (float)t[2], (float)t[3]);
            var bones = (JArray)root["bones"];
            var frames = (JArray)root["frames"];
            return new MixamoClip
            {
                Fps = (float?)root["fps"] ?? 30f,
                Names = bones.Select(b => (string)b["name"]).ToArray(),
                Parents = bones.Select(b => (int)b["parent"]).ToArray(),
                RestPos = bones.Select(b => V(b["pos"])).ToArray(),
                RestRot = bones.Select(b => Q(b["rot"])).ToArray(),
                Hips = frames.Select(f => V(f["hips"])).ToArray(),
                Rot = frames.Select(f => ((JArray)f["rot"]).Select(Q).ToArray()).ToArray(),
            };
        }
    }

    /// <summary>Plays a MixamoClip on a character body (its Animator's humanoid avatar), looping, after the game's own animation each frame.</summary>
    internal sealed class MixamoPlayer : MonoBehaviour
    {
        // Mixamo's bone names to Unity's humanoid ones
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
                foreach (var (mixamo, unity) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Pinky", "Little") })
                {
                    map[$"{side}Hand{mixamo}1"] = $"{side} {unity} Proximal";
                    map[$"{side}Hand{mixamo}2"] = $"{side} {unity} Intermediate";
                    map[$"{side}Hand{mixamo}3"] = $"{side} {unity} Distal";
                }
            }
            return map;
        }

        private MixamoClip _clip;
        private GameObject _source;
        private Avatar _avatar;
        private Transform[] _bones;
        private HumanPoseHandler _from, _to;
        private HumanPose _pose;
        private float _time;
        internal float Speed = 1f;
        private bool _loop = true;
        /// <summary>Called each frame once the pose is set (to hold things in the posed hands).</summary>
        internal System.Action Posed;

        /// <summary>Starts <paramref name="clip"/> on the body that has <paramref name="animator"/> (a humanoid); null if it can't.</summary>
        internal static MixamoPlayer Play(Animator animator, string clip, bool loop = true)
        {
            MixamoClip c = MixamoClip.Get(clip);
            if (c == null || animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
            var player = animator.gameObject.AddComponent<MixamoPlayer>();
            if (!player.Setup(c, animator)) { Destroy(player); return null; }
            player._loop = loop;
            return player;
        }

        private bool Setup(MixamoClip clip, Animator animator)
        {
            _clip = clip;
            _source = new GameObject("goodtidings_mixamo_source");
            _source.hideFlags = HideFlags.HideAndDontSave;
            _bones = new Transform[clip.Names.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i] = new GameObject(clip.Names[i]).transform;
                _bones[i].SetParent(clip.Parents[i] >= 0 ? _bones[clip.Parents[i]] : _source.transform, false);
                _bones[i].localPosition = clip.RestPos[i];
                _bones[i].localRotation = clip.RestRot[i];
            }
            var description = new HumanDescription
            {
                human = clip.Names.Where(Human.ContainsKey).Select(n => new HumanBone { boneName = n, humanName = Human[n], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = new[] { new SkeletonBone { name = _source.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one } }
                    .Concat(_bones.Select(b => new SkeletonBone { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = Vector3.one })).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(_source, description);
            if (avatar == null || !avatar.isValid || !avatar.isHuman) { Plugin.Log.LogWarning("The animation's skeleton isn't a valid humanoid"); return false; }
            // kept, and never unloaded with the game's unused assets: only the pose handler (native) used it, so the game's clean-up of
            // unused assets freed it under the handler and the next pose read crashed the game
            avatar.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _avatar = avatar;
            _from = new HumanPoseHandler(avatar, _source.transform);
            _to = new HumanPoseHandler(animator.avatar, animator.transform);
            return true;
        }

        // ---- blending in from another pose, and out into whatever the body's animator plays next (smooth changes, not a snap)

        private HumanPose _blendFrom, _last, _under;
        private float _blendIn = -1f, _blendInTime, _fade = -1f, _fadeTime;

        /// <summary>Starts from <paramref name="from"/> (the pose the body is in now) and blends into the animation over <paramref name="seconds"/>.</summary>
        internal void BlendFrom(HumanPose from, float seconds)
        {
            _blendFrom = Copy(from);
            _blendIn = 0f;
            _blendInTime = Mathf.Max(0.01f, seconds);
        }

        /// <summary>Blends from the animation into what the body's animator plays now over <paramref name="seconds"/>, then goes.</summary>
        internal void FadeOut(float seconds)
        {
            _fade = 0f;
            _fadeTime = Mathf.Max(0.01f, seconds);
            Posed = null;
        }

        /// <summary>The pose a body's animator has it in now.</summary>
        internal static HumanPose PoseOf(Animator animator)
        {
            var pose = new HumanPose();
            using (var h = new HumanPoseHandler(animator.avatar, animator.transform)) h.GetHumanPose(ref pose);
            return pose;
        }

        private static HumanPose Copy(HumanPose p) =>
            new HumanPose { bodyPosition = p.bodyPosition, bodyRotation = p.bodyRotation, muscles = (float[])p.muscles?.Clone() };

        internal static HumanPose Blend(HumanPose a, HumanPose b, float w)
        {
            var r = new HumanPose
            {
                bodyPosition = Vector3.Lerp(a.bodyPosition, b.bodyPosition, w),
                bodyRotation = Quaternion.Slerp(a.bodyRotation, b.bodyRotation, w),
                muscles = (float[])b.muscles.Clone(),
            };
            if (a.muscles != null)
                for (int i = 0; i < r.muscles.Length && i < a.muscles.Length; i++) r.muscles[i] = Mathf.Lerp(a.muscles[i], b.muscles[i], w);
            return r;
        }

        private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        private void LateUpdate()
        {
            if (_clip == null || _from == null) return;
            if (_fade >= 0f)
            {
                _fade += Time.deltaTime;
                _to.GetHumanPose(ref _under);
                HumanPose mixed = Blend(_last, _under, Ease(_fade / _fadeTime));
                _to.SetHumanPose(ref mixed);
                if (_fade >= _fadeTime) Destroy(this);
                return;
            }
            _time = _loop ? (_time + Time.deltaTime * Speed) % _clip.Length : Mathf.Min(_time + Time.deltaTime * Speed, _clip.Length - 1f / _clip.Fps);
            float f = _time * _clip.Fps;
            int a = Mathf.FloorToInt(f) % _clip.Rot.Length, b = (a + 1) % _clip.Rot.Length;
            float t = f - Mathf.Floor(f);
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = Quaternion.Slerp(_clip.Rot[a][i], _clip.Rot[b][i], t);
            _bones[0].localPosition = Vector3.Lerp(_clip.Hips[a], _clip.Hips[b], t);
            _from.GetHumanPose(ref _pose);
            HumanPose shown = _pose;
            if (_blendIn >= 0f)
            {
                _blendIn += Time.deltaTime;
                shown = Blend(_blendFrom, _pose, Ease(_blendIn / _blendInTime));
                if (_blendIn >= _blendInTime) _blendIn = -1f;
            }
            _to.SetHumanPose(ref shown);
            _last = Copy(shown);
            Posed?.Invoke();
        }

        private void OnDestroy()
        {
            _from?.Dispose();
            _to?.Dispose();
            if (_source) Destroy(_source);
            if (_avatar) Destroy(_avatar);
        }
    }
}
