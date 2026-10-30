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
        private Transform[] _bones;
        private HumanPoseHandler _from, _to;
        private HumanPose _pose;
        private float _time;
        internal float Speed = 1f;
        /// <summary>Called each frame once the pose is set (to hold things in the posed hands).</summary>
        internal System.Action Posed;

        /// <summary>Starts <paramref name="clip"/> on the body that has <paramref name="animator"/> (a humanoid); null if it can't.</summary>
        internal static MixamoPlayer Play(Animator animator, string clip)
        {
            MixamoClip c = MixamoClip.Get(clip);
            if (c == null || animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
            var player = animator.gameObject.AddComponent<MixamoPlayer>();
            if (!player.Setup(c, animator)) { Destroy(player); return null; }
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
            _from = new HumanPoseHandler(avatar, _source.transform);
            _to = new HumanPoseHandler(animator.avatar, animator.transform);
            return true;
        }

        private void LateUpdate()
        {
            if (_clip == null || _from == null) return;
            _time = (_time + Time.deltaTime * Speed) % _clip.Length;
            float f = _time * _clip.Fps;
            int a = Mathf.FloorToInt(f) % _clip.Rot.Length, b = (a + 1) % _clip.Rot.Length;
            float t = f - Mathf.Floor(f);
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = Quaternion.Slerp(_clip.Rot[a][i], _clip.Rot[b][i], t);
            _bones[0].localPosition = Vector3.Lerp(_clip.Hips[a], _clip.Hips[b], t);
            _from.GetHumanPose(ref _pose);
            _to.SetHumanPose(ref _pose);
            Posed?.Invoke();
        }

        private void OnDestroy()
        {
            _from?.Dispose();
            _to?.Dispose();
            if (_source) Destroy(_source);
        }
    }
}
