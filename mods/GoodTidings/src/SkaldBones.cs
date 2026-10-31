using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The skald's bones (copied from DummyMe's SkeletonBody, with the user's DummyMe settings built in: Plain, skull 0.81 at -2.982 /
    /// 5.527 / 0.405 cm, eyes 1 cm, shoulders 1.1): the game's own Skeleton creature's body, skinned to a player body's bones (both rigs
    /// name their bones the same), so player clothes fit it and animations for a human body play on it.
    /// Each skeleton bone is laid onto the player's bone of the same name as both were modelled (their bind poses) and stretched to its
    /// length, so the joints meet where the viking's do; the rest of it is made as much bigger as the viking is taller. The skull's parts
    /// (the jaw) go with the skull, so the jaw stays on it. Idea from Races of Valheim's skeleton race.
    /// </summary>
    internal static class SkaldBones
    {
        internal const string PartPrefix = "goodtidings_skald_";
        private const string Creature = "Skeleton";
        private const float SkullSize = 0.81f, SkullHeight = -2.982f, SkullForward = 5.527f, SkullSide = 0.405f, EyeSize = 1f, ShoulderWidth = 1.1f;

        // The bone each bone points at, where it isn't its name with the last digit counted on (fingers) or "_end" (finger and toe tips).
        private static readonly Dictionary<string, string> Next = new Dictionary<string, string>
        {
            { "Hips", "Spine1" }, { "Spine", "Spine1" }, { "Spine1", "Spine2" }, { "Spine2", "Neck" }, { "Neck", "Head" },
            { "LeftShoulder", "LeftArm" }, { "LeftArm", "LeftForeArm" }, { "LeftForeArm", "LeftHand" }, { "LeftHand", "LeftHandMiddle1" },
            { "RightShoulder", "RightArm" }, { "RightArm", "RightForeArm" }, { "RightForeArm", "RightHand" }, { "RightHand", "RightHandMiddle1" },
            { "LeftUpLeg", "LeftLeg" }, { "LeftLeg", "LeftFoot" }, { "LeftFoot", "LeftToeBase" }, { "LeftToeBase", "LeftToeBase_end" },
            { "RightUpLeg", "RightLeg" }, { "RightLeg", "RightFoot" }, { "RightFoot", "RightToeBase" }, { "RightToeBase", "RightToeBase_end" },
        };

        // Bones laid with the one above them rather than on their own joint: the two rigs split the lower back in other places (the
        // skeleton's Spine joint sits 23 cm over its hips, the viking's 11), the same 40 cm in all, so the pelvis and the lower spine
        // are fitted as one (fitted apart, the pelvis was squashed to half and the lower spine stretched into it).
        private static readonly Dictionary<string, string> RidesWith = new Dictionary<string, string> { { "Spine", "Hips" } };

        // What Shoulder width widens.
        private static readonly HashSet<string> Broad = new HashSet<string> { "Spine2", "LeftShoulder", "RightShoulder" };

        /// <summary>
        /// Builds the skeleton on a player body (<paramref name="body"/>: its body mesh; <paramref name="character"/>: the character it's
        /// part of, y up and facing +z; <paramref name="visual"/>: its Visual) and hides the body; returns what it made (empty if it couldn't).
        /// </summary>
        internal static List<Object> Build(Transform character, SkinnedMeshRenderer body, Transform visual)
        {
            var made = new List<Object>();
            string name = Creature;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (body == null || body.sharedMesh == null || prefab == null)
            {
                Plugin.Log.LogWarning(prefab == null ? $"No \"{name}\" creature in the game to borrow a body from" : "The player has no body to fit a skeleton to");
                return made;
            }
            var mine = new RestPose(character, body);
            var skins = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
            RestPose skull = null;
            foreach (var bones in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (bones.sharedMesh == null) continue;
                var theirs = new RestPose(prefab.transform, bones);
                var smr = AddRenderer(body, made);
                smr.sharedMaterials = bones.sharedMaterials;
                Fit(bones, theirs, mine, smr, made);
                skins[bones] = smr;
                if (skull == null && theirs.Has("Head")) skull = theirs;
            }
            if (made.Count == 0)
            {
                Plugin.Log.LogWarning($"The \"{name}\" creature has no body to borrow");
                return made;
            }
            AddEffects(prefab, visual, skins, made);
            if (skull != null) AddEyes(skull, mine, body.gameObject.layer, made);
            body.forceRenderingOff = true;   // (the viking's own body, under the bones)
            return made;
        }

        /// <summary>
        /// The creature's glowing eyes: small quads in its eye sockets, on its head bone (the game grows them in with its animations,
        /// so in the prefab they're there at size 0). Copied onto the player's head bone where the fitted skull puts the sockets, at
        /// Eye size.
        /// </summary>
        private static void AddEyes(RestPose theirs, RestPose mine, int layer, List<Object> made)
        {
            var theirHead = theirs.Bone("Head");
            var myHead = mine.Bone("Head");
            float bulk = Height(mine) / Mathf.Max(Height(theirs), 1e-4f);
            var lay = Lay("Head", theirs, mine, bulk);
            int eyes = 0;
            foreach (Transform eye in theirHead)
            {
                var filter = eye.GetComponent<MeshFilter>();
                var look = eye.GetComponent<MeshRenderer>();
                if (!eye.name.StartsWith("eye") || filter == null || look == null) continue;
                // Where the socket is as modelled, on the player's head (its own scale left out: it's 0 in the prefab).
                var at = mine.CharToBone(myHead) * lay * theirs.BoneToChar("Head") * Matrix4x4.TRS(eye.localPosition, eye.localRotation, Vector3.one);
                var go = new GameObject(PartPrefix + eye.name) { layer = layer };
                go.transform.SetParent(myHead, false);
                go.transform.localPosition = at.GetColumn(3);
                go.transform.localRotation = at.rotation;
                go.transform.localScale = Vector3.one * (EyeSize * 0.01f / Mathf.Max(myHead.lossyScale.x, 1e-4f));
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = look.sharedMaterials;
                made.Add(go);
                eyes++;
            }
            if (eyes == 0) Plugin.Log.LogWarning("This skeleton has no eyes to copy");
        }

        /// <summary>
        /// The creature's own effects that go everywhere it goes (the Rancid Remains' dripping water): its particle systems hung straight
        /// off its Visual, copied to the player's in the same place, those that drip from its body made to drip from the player's skeleton.
        /// </summary>
        private static void AddEffects(GameObject prefab, Transform myVisual, Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> skins, List<Object> made)
        {
            var theirVisual = prefab.transform.Find("Visual");
            if (theirVisual == null || myVisual == null) return;
            foreach (Transform child in theirVisual)
            {
                if (child.GetComponent<ParticleSystem>() == null) continue;
                var fx = Object.Instantiate(child.gameObject, myVisual, false);
                fx.name = PartPrefix + child.name;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = ps.shape;
                    if (shape.skinnedMeshRenderer != null && skins.TryGetValue(shape.skinnedMeshRenderer, out var mine))
                        shape.skinnedMeshRenderer = mine;
                }
                made.Add(fx);
            }
        }

        private static SkinnedMeshRenderer AddRenderer(SkinnedMeshRenderer body, List<Object> made)
        {
            var go = new GameObject(PartPrefix + "body") { layer = body.gameObject.layer };
            go.transform.SetParent(body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            go.transform.localScale = body.transform.localScale;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.rootBone = body.rootBone;
            smr.localBounds = body.localBounds;
            smr.updateWhenOffscreen = body.updateWhenOffscreen;
            smr.quality = body.quality;
            made.Add(go);
            return smr;
        }

        /// <summary>
        /// Skins the creature's mesh to the player's bones: each of its bones on the player's of the same name (or of the nearest bone
        /// above it the player has), its bind pose made so that, as modelled, its part lies along the player's bone (Lay). The mesh is
        /// copied for that; if the game's can't be read, it goes on the player's bones as it is, at the skeleton's own sizes.
        /// </summary>
        private static void Fit(SkinnedMeshRenderer source, RestPose theirs, RestPose mine, SkinnedMeshRenderer smr, List<Object> made)
        {
            var mesh = source.sharedMesh;
            float bulk = Height(mine) / Mathf.Max(Height(theirs), 1e-4f);
            var bones = new Transform[source.bones.Length];
            var bind = new Matrix4x4[bones.Length];
            var laid = new Dictionary<string, Matrix4x4>();
            for (int i = 0; i < bones.Length; i++)
            {
                var b = source.bones[i];
                Transform rider = b;
                while (rider != null && !mine.Has(rider.name)) rider = rider.parent;
                bones[i] = rider != null ? mine.Bone(rider.name) : mine.Bone("Hips");
                // The skull's own parts (the jaw, the eyes) move with the skull, wherever the viking's jaw bone pivots.
                string by = rider == null ? "Hips" : IsUnder(rider, "Head") ? "Head"
                    : RidesWith.TryGetValue(rider.name, out var with) ? with : rider.name;
                if (!laid.TryGetValue(by, out var lay)) laid[by] = lay = Lay(by, theirs, mine, bulk);
                bind[i] = mine.CharToBone(bones[i]) * lay * theirs.MeshToChar;
            }

            if (mesh.isReadable)
            {
                mesh = Object.Instantiate(mesh);
                mesh.name = PartPrefix + source.sharedMesh.name;
                mesh.bindposes = bind;
                made.Add(mesh);
            }
            else Plugin.Log.LogWarning($"The {source.sharedMesh.name} mesh can't be read; it goes on as it is, at the skeleton's own sizes");
            smr.sharedMesh = mesh;
            smr.bones = bones;
        }

        /// <summary>
        /// In the character's space as modelled: takes the skeleton's bone onto the player's (its start on theirs, turned as theirs),
        /// stretched along it to the player's bone length and across it by <paramref name="bulk"/>.
        /// </summary>
        private static Matrix4x4 Lay(string bone, RestPose theirs, RestPose mine, float bulk)
        {
            if (!theirs.Has(bone) || !mine.Has(bone)) return Matrix4x4.Scale(Vector3.one * bulk);
            var t = theirs.BoneToChar(bone);
            var m = mine.BoneToChar(bone);
            Vector3 tStart = t.MultiplyPoint3x4(Vector3.zero), mStart = m.MultiplyPoint3x4(Vector3.zero);

            var stretch = Matrix4x4.Scale(Vector3.one * bulk);
            string next = NextOf(bone);
            if (next != null && theirs.Has(next) && mine.Has(next))
            {
                var along = theirs.BoneToChar(next).MultiplyPoint3x4(Vector3.zero) - tStart;
                float length = along.magnitude;
                if (length > 1e-4f)
                {
                    float mineLength = Vector3.Distance(mine.BoneToChar(next).MultiplyPoint3x4(Vector3.zero), mStart);
                    float r = Mathf.Clamp(mineLength / length, 0.5f * bulk, 2f * bulk);
                    // bulk all round, and r instead along the bone: bulk·I + (r - bulk)·d·dᵀ
                    var d = along / length;
                    for (int row = 0; row < 3; row++)
                        for (int col = 0; col < 3; col++)
                            stretch[row, col] += (r - bulk) * d[row] * d[col];
                }
            }
            if (bone == "Head") stretch = Matrix4x4.Scale(Vector3.one * SkullSize) * stretch; // around the top of the neck
            var turn = Matrix4x4.Rotate(m.rotation * Quaternion.Inverse(t.rotation));
            var lift = bone == "Head" ? new Vector3(SkullSide, SkullHeight, SkullForward) * 0.01f : Vector3.zero;
            var laid = Matrix4x4.Translate(mStart + lift) * turn * stretch * Matrix4x4.Translate(-tStart);
            // Shoulder width: the top of the rib cage, the collarbones and shoulder blades spread out from the spine, and the upper arm
            // bones' tops with them, their elbows staying where the viking's are.
            float width = ShoulderWidth;
            if (Broad.Contains(bone)) return Matrix4x4.Scale(new Vector3(width, 1f, 1f)) * laid;
            if ((bone == "LeftArm" || bone == "RightArm") && next != null && mine.Has(next))
                return Lean(mStart, mine.BoneToChar(next).MultiplyPoint3x4(Vector3.zero), new Vector3((width - 1f) * mStart.x, 0f, 0f)) * laid;
            return laid;
        }

        /// <summary>
        /// Moves the start of a bone from <paramref name="start"/> to <paramref name="end"/> by <paramref name="shift"/> and its end
        /// not at all, everything between by its share of the way: v + shift·(1 - (v - start)·d / length).
        /// </summary>
        private static Matrix4x4 Lean(Vector3 start, Vector3 end, Vector3 shift)
        {
            var lean = Matrix4x4.identity;
            float length = Vector3.Distance(start, end);
            if (length < 1e-4f || shift == Vector3.zero) return lean;
            var d = (end - start) / length;
            float along = Vector3.Dot(d, start) / length;
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++) lean[row, col] -= shift[row] * d[col] / length;
                lean[row, 3] += shift[row] * (1f + along);
            }
            return lean;
        }

        /// <summary>
        /// Puts a player's item on the body (as VisEquipment does): its skinned meshes ("attach_skin") on the body's bones of the same
        /// names, or its model ("attach", a helmet or hat) on the head. Clothes painted into the body's texture have no mesh and show nothing.
        /// </summary>
        internal static GameObject Wear(SkinnedMeshRenderer body, string item, List<Object> made)
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(item);
            if (prefab == null) { Plugin.Log.LogWarning($"No item {item}"); return null; }
            var byName = new Dictionary<string, Transform>();
            foreach (Transform b in body.rootBone.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(b.name)) byName[b.name] = b;
            Transform skin = prefab.transform.Find("attach_skin");
            if (skin != null)
            {
                GameObject s = Object.Instantiate(skin.gameObject, body.transform.parent);
                s.name = PartPrefix + item;
                s.transform.localPosition = Vector3.zero;
                s.transform.localRotation = Quaternion.identity;
                s.SetActive(true);
                foreach (SkinnedMeshRenderer r in s.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = new Transform[r.bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform b = r.bones[i];
                        while (b != null && !byName.ContainsKey(b.name)) b = b.parent;
                        bones[i] = b != null ? byName[b.name] : body.rootBone;
                    }
                    r.bones = bones;
                    r.rootBone = body.rootBone;
                }
                made.Add(s);
                return s;
            }
            // a helmet or hat: on the player's helmet point inside the head (VisEquipment.m_helmet), as the game puts it (on the head bone
            // itself it floated far above, grown)
            Transform attach = prefab.transform.Find("attach");
            if (attach == null || !byName.TryGetValue("Helmet_attach", out Transform head) && !byName.TryGetValue("Head", out head))
            {
                Plugin.Log.LogWarning($"{item} has nothing to wear");
                return null;
            }
            GameObject h = Object.Instantiate(attach.gameObject, head, false);
            h.name = PartPrefix + item;
            h.transform.localPosition = Vector3.zero;
            h.transform.localRotation = Quaternion.identity;
            foreach (Collider c in h.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            h.SetActive(true);
            made.Add(h);
            return h;
        }

        /// <summary>From the hips to the head, as modelled.</summary>
        private static float Height(RestPose rig) => !rig.Has("Hips") || !rig.Has("Head") ? 1f
            : Vector3.Distance(rig.BoneToChar("Hips").MultiplyPoint3x4(Vector3.zero), rig.BoneToChar("Head").MultiplyPoint3x4(Vector3.zero));

        private static string NextOf(string bone)
        {
            if (Next.TryGetValue(bone, out var next)) return next;
            if (bone.EndsWith("_end")) return null;
            char last = bone[bone.Length - 1];
            return char.IsDigit(last) && last < '3' ? bone.Substring(0, bone.Length - 1) + (char)(last + 1) : bone + "_end";
        }

        private static bool IsUnder(Transform t, string name)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name == name) return true;
            return false;
        }

        /// <summary>A rig as it was modelled: each bone's own space in the character's (y up, facing +z), from the mesh's bind poses
        /// (bones it isn't skinned to, from their parent's).</summary>
        private class RestPose
        {
            private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
            private readonly Dictionary<Transform, int> _skinned = new Dictionary<Transform, int>();
            private readonly Matrix4x4[] _bind;

            public Matrix4x4 MeshToChar { get; }

            public RestPose(Transform character, SkinnedMeshRenderer smr)
            {
                // From the hips down only: gear on the character has bones of its own, some with the same names.
                var root = smr.rootBone != null ? smr.rootBone : smr.transform.parent;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
                for (int i = 0; i < smr.bones.Length; i++)
                    if (smr.bones[i] != null) _skinned[smr.bones[i]] = i;
                _bind = smr.sharedMesh.bindposes;
                MeshToChar = character.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            }

            public bool Has(string bone) => _bones.ContainsKey(bone);
            public Transform Bone(string bone) => _bones[bone];
            public Matrix4x4 BoneToChar(string bone) => MeshToChar * BoneToMesh(_bones[bone]);
            public Matrix4x4 CharToBone(Transform bone) => (MeshToChar * BoneToMesh(bone)).inverse;

            private Matrix4x4 BoneToMesh(Transform t)
            {
                if (_skinned.TryGetValue(t, out int i) && i < _bind.Length) return _bind[i].inverse;
                if (t.parent == null) return Matrix4x4.identity;
                return BoneToMesh(t.parent) * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            }
        }
    }
}
