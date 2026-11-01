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
        }

        /// <summary>The robot's skeleton in its rest pose, under <paramref name="parent"/>; bones in the model's order.</summary>
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

        private void ShowEyes()
        {
            if (Skin == null) return;
            if (_eyes == null) _eyes = new MaterialPropertyBlock();
            Skin.GetPropertyBlock(_eyes, 0);
            _eyes.SetColor("_EmissionColor", (_eyeColor ?? Plugin.EyeColor.Value) * Plugin.EyeGlow.Value);
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
            _to.SetHumanPose(ref _pose);
        }

        private void OnDestroy()
        {
            Live.Remove(this);
            _from?.Dispose();
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
