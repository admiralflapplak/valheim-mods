using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Builds a Dverger's look out of the game's own parts: the body (the Visual of the "Dverger" prefab, or Hildir's, which is a Dverger
    /// skeleton too), outfits (the suits' skinned meshes) and hair. Outfits and hair are bound to the body's bones by name, so they fit a
    /// skeleton with fewer bones (Hildir's fingers have one joint each: the missing ones fall back to the nearest one above). Only models: no creature, no ZNetView, nothing
    /// saved. Parts of an outfit can be hidden by bone: the outfit gets its own bone list, with the hidden bones swapped for a
    /// zero-size stand-in under them, so the part weighted to them shrinks away (no readable mesh needed).
    /// </summary>
    internal static class DvergerLook
    {
        /// <summary>Which part of an outfit to keep.</summary>
        internal enum Keep { All, NoHead, HeadOnly }

        /// <summary>The bare body, under <paramref name="parent"/>, with nothing that needs a creature (animation events, level colours).</summary>
        internal static GameObject Body(Transform parent) => Body(parent, "Dverger", "Visual");

        /// <summary>Hildir's body without her dress and her long plait (its bones, Hair1-3, shrunk away), to dress in Dverger clothes.</summary>
        internal static GameObject HildirBody(Transform parent)
        {
            GameObject body = Body(parent, "Hildir", "Hildir");
            Transform skirt = body.transform.Find("Skirt");
            if (skirt) Object.Destroy(skirt.gameObject);
            SkinnedMeshRenderer mesh = BodyMesh(body);
            Transform head = mesh.bones.First(b => b.name == "head");
            Transform point = Point(head);
            mesh.bones = mesh.bones.Select(b => b.name.StartsWith("Hair") ? point : b).ToArray();
            return body;
        }

        /// <summary>
        /// A Lox's look with its saddle on (its Visual, without creature parts), under <paramref name="parent"/>. Its fur and skin can be
        /// recoloured with the creature shader's own controls (as the game tints creatures by level): <paramref name="hsv"/> is the shift
        /// in hue (-0.5 to 0.5, round the colour wheel), saturation (-1 grey to +1) and brightness (-1 to +1). The saddle keeps its colour.
        /// </summary>
        internal static GameObject Lox(Transform parent, Vector3? hsv = null)
        {
            GameObject lox = Body(parent, "Lox", "Visual");
            if (hsv is Vector3 shift) TraderLook.Tint(lox, shift);
            Transform saddle = Utils.FindChild(lox.transform, "Sadel");
            if (saddle)
            {
                foreach (var c in saddle.GetComponents<Component>())
                    if (c is Collider || c is MonoBehaviour) Object.Destroy(c);
                saddle.gameObject.SetActive(true);
            }
            foreach (Collider c in lox.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            return lox;
        }

        /// <summary>Just the look of a prefab (goods on the Lox): its meshes, lights and particles, nothing that works (no container, collider, ZNetView).</summary>
        internal static GameObject Model(string prefab, Transform parent)
        {
            var holder = new GameObject("goodtidings_build");
            holder.SetActive(false);
            GameObject m = Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), holder.transform);
            foreach (var c in m.GetComponentsInChildren<Component>(true))
                if (c is MonoBehaviour || c is Collider || c is Rigidbody) Object.DestroyImmediate(c);
            foreach (var c in m.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is Renderer) && !(c is MeshFilter) && !(c is LODGroup) && !(c is Light)
                    && c.GetType().Name != "ParticleSystem") Object.DestroyImmediate(c);
            m.name = "goodtidings_" + prefab;
            m.transform.SetParent(parent, false);
            m.transform.localPosition = Vector3.zero;   // (prefabs are stored where they were made: some 50 or 500 m up)
            m.transform.localRotation = Quaternion.identity;
            Object.Destroy(holder);
            return m;
        }

        /// <summary>Any character's look (its visual child, without creature parts), for looking into it.</summary>
        internal static GameObject BodyOf(string prefab, string visualName, Transform parent) => Body(parent, prefab, visualName);

        private static GameObject Body(Transform parent, string prefab, string visualName)
        {
            Transform visual = ZNetScene.instance.GetPrefab(prefab).transform.Find(visualName);
            // Built under an inactive holder, so nothing on it wakes up before the creature-only parts are gone.
            var holder = new GameObject("goodtidings_build");
            holder.SetActive(false);
            GameObject body = Object.Instantiate(visual.gameObject, holder.transform);
            body.name = "goodtidings_dverger";
            foreach (var c in body.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);
            body.transform.SetParent(parent, false);
            Object.Destroy(holder);
            return body;
        }

        /// <summary>Puts an outfit (a Dverger suit item) on the body, keeping only the part asked for.</summary>
        internal static GameObject Outfit(GameObject body, string suitPrefab, Keep keep, float headScale = 1f, Cuts.Tint tint = null)
        {
            SkinnedMeshRenderer bodyMesh = BodyMesh(body);
            Transform attach = ObjectDB.instance.GetItemPrefab(suitPrefab)?.transform.Find("attach_skin");
            if (attach == null) { Plugin.Log.LogWarning($"No outfit {suitPrefab}"); return null; }

            GameObject outfit = Object.Instantiate(attach.gameObject, bodyMesh.transform.parent);
            outfit.name = "goodtidings_" + suitPrefab;
            outfit.transform.localPosition = Vector3.zero;
            outfit.transform.localRotation = Quaternion.identity;
            outfit.SetActive(true);
            foreach (SkinnedMeshRenderer r in outfit.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.sharedMaterial = Cuts.MaterialFor(r.sharedMaterial, suitPrefab, tint);
                Transform[] bones = MapBones(r, bodyMesh);
                r.rootBone = bodyMesh.rootBone;
                bones = keep == Keep.All ? bones : HideBones(bones, keep);
                if (headScale != 1f) bones = ScaleHead(bones, headScale);
                r.bones = bones;
            }
            return outfit;
        }

        /// <summary>
        /// Swaps the body's face for another character's (Hildir's is more detailed): the head part of that character's body mesh, bound to
        /// their own head bones and this body's neck, while this body's own head shrinks away.
        /// </summary>
        internal static GameObject Face(GameObject body, string prefab, string visualName)
        {
            SkinnedMeshRenderer bodyMesh = BodyMesh(body);
            SkinnedMeshRenderer source = ZNetScene.instance.GetPrefab(prefab).transform.Find(visualName)
                .GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "dvergr");
            GameObject face = Object.Instantiate(source.gameObject, bodyMesh.transform.parent);
            face.name = "goodtidings_face_" + prefab;
            var r2 = face.GetComponent<SkinnedMeshRenderer>();
            foreach (var c in face.GetComponents<Component>())
                if (!(c is Transform) && !(c is SkinnedMeshRenderer)) Object.Destroy(c);
            // Their face is shaped for their own face bones (jaw, brows, cheeks, mouth sit elsewhere than this body's), so it brings a copy
            // of its own head bones, fixed to this body's head: on this body's face bones it came out pulled out of shape. Their neck goes on
            // this body's neck, so the two join (with everything below the head shrunk away there was a hole under it). A copy of their neck
            // as well was no good: it brought the necklace through the clothes. Their plait (Hair1-3) shrinks away: without their animation
            // it doesn't hang down but wraps up over the head.
            Transform bodyHead = bodyMesh.bones.First(b => b.name == "head");
            Transform sourceHead = source.bones.First(b => b.name == "head");
            Transform ownHead = Object.Instantiate(sourceHead.gameObject, bodyHead, false).transform;
            ownHead.name = "goodtidings_head_" + prefab;
            ownHead.localPosition = Vector3.zero;
            ownHead.localRotation = Quaternion.identity;
            ownHead.localScale = Vector3.one;
            foreach (var c in ownHead.GetComponentsInChildren<Component>(true))
                if (!(c is Transform)) Object.Destroy(c);
            Transform hidden = Point(bodyHead.parent);
            Transform plait = Point(ownHead);
            var mapped = new Transform[source.bones.Length];
            for (int i = 0; i < mapped.Length; i++)
            {
                Transform b = source.bones[i];
                if (b.name.StartsWith("Hair")) mapped[i] = plait;
                else if (b == sourceHead) mapped[i] = ownHead;
                else if (b.IsChildOf(sourceHead)) mapped[i] = Utils.FindChild(ownHead, b.name) ?? plait;
                else if (b.name == "neck") mapped[i] = bodyHead.parent;
                else mapped[i] = hidden;
            }
            r2.bones = mapped;
            r2.rootBone = bodyMesh.rootBone;
            bodyMesh.bones = HideBones(bodyMesh.bones, Keep.NoHead);
            return face;
        }

        /// <summary>
        /// Puts a hair item (DvergerHairFemale...) on the body's head. With <paramref name="parts"/>, only those of its meshes: the male
        /// hair is three ("hair13", "beardfluff", "mustachefluff"), so a beard can go with another hairstyle.
        /// </summary>
        internal static GameObject Hair(GameObject body, string hairPrefab, params string[] parts)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(hairPrefab);
            if (prefab == null) { Plugin.Log.LogWarning($"No hair {hairPrefab}"); return null; }
            Transform skin = prefab.transform.Find("attach_skin");
            if (skin != null)
            {
                SkinnedMeshRenderer bodyMesh = BodyMesh(body);
                GameObject s = Object.Instantiate(skin.gameObject, bodyMesh.transform.parent);
                s.transform.localPosition = Vector3.zero;
                s.transform.localRotation = Quaternion.identity;
                s.SetActive(true);
                foreach (SkinnedMeshRenderer r in s.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (parts.Length > 0 && !parts.Contains(r.name)) { Object.Destroy(r.gameObject); continue; }
                    r.bones = MapBones(r, bodyMesh);
                    r.rootBone = bodyMesh.rootBone;
                }
                return s;
            }
            Transform attach = prefab.transform.Find("attach");
            Transform head = Utils.FindChild(body.transform, "head");
            if (attach == null || head == null) { Plugin.Log.LogWarning($"Can't attach hair {hairPrefab}"); return null; }
            GameObject h = Object.Instantiate(attach.gameObject, head, false);
            h.transform.localPosition = Vector3.zero;
            h.transform.localRotation = Quaternion.identity;
            foreach (Collider c in h.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            h.SetActive(true);
            return h;
        }

        /// <summary>The bone names under the head (head included), to tell the hat or hood from the rest.</summary>
        internal static string BoneNames(GameObject body) => string.Join(", ", BodyMesh(body).bones.Select(b => b.name));

        private static SkinnedMeshRenderer BodyMesh(GameObject body) =>
            body.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "dvergr");

        /// <summary>The body's bone for each of <paramref name="r"/>'s own, by name; one the body lacks takes the nearest one above it.</summary>
        private static Transform[] MapBones(SkinnedMeshRenderer r, SkinnedMeshRenderer bodyMesh)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (Transform b in bodyMesh.rootBone.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(b.name)) byName[b.name] = b;
            Transform[] own = r.bones;
            if (own == null || own.Length == 0 || own.Any(b => b == null)) return bodyMesh.bones;   // nothing to go by: the game's way
            var result = new Transform[own.Length];
            for (int i = 0; i < own.Length; i++)
            {
                Transform b = own[i];
                while (b != null && !byName.ContainsKey(b.name)) b = b.parent;
                result[i] = b != null ? byName[b.name] : bodyMesh.rootBone;
            }
            return result;
        }

        /// <summary>
        /// The head part of an outfit (a hood or hat) made bigger by <paramref name="scale"/>, from the head joint, so it clears a face
        /// it wasn't made for. Each head bone gets a stand-in under it, placed where that bone would be if the whole head were scaled.
        /// </summary>
        private static Transform[] ScaleHead(Transform[] bones, float scale)
        {
            Transform head = bones.FirstOrDefault(b => b.name == "head");
            if (head == null) return bones;
            Matrix4x4 grow = head.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * scale) * head.worldToLocalMatrix;
            var made = new Dictionary<Transform, Transform>();
            var result = (Transform[])bones.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                Transform b = result[i];
                if (b == null || !(b == head || b.IsChildOf(head)) || b.name == "goodtidings_hidden") continue;
                if (!made.TryGetValue(b, out Transform proxy))
                {
                    Matrix4x4 local = b.worldToLocalMatrix * grow * b.localToWorldMatrix;
                    proxy = new GameObject("goodtidings_scaled").transform;
                    proxy.SetParent(b, false);
                    proxy.localPosition = local.GetColumn(3);
                    proxy.localRotation = local.rotation;
                    proxy.localScale = Vector3.one * scale;
                    made[b] = proxy;
                }
                result[i] = proxy;
            }
            return result;
        }

        private static Transform Point(Transform under)
        {
            var point = new GameObject("goodtidings_hidden").transform;
            point.SetParent(under, false);
            point.localScale = Vector3.zero;
            return point;
        }

        private static Transform[] HideBones(Transform[] bones, Keep keep)
        {
            Transform head = bones.FirstOrDefault(b => b.name.ToLowerInvariant() == "head");
            if (head == null) return bones;
            // Every hidden bone goes to the same single point (on the head, or the neck when only the head part is kept): one stand-in
            // per bone would squash the hidden part into lines between the bones (the pointy hat left a black spike up from the head).
            Transform point = Point(keep == Keep.NoHead ? head : head.parent);
            var result = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bool inHead = bones[i] == head || bones[i].IsChildOf(head);
                bool hide = keep == Keep.NoHead ? inHead : !inHead;
                result[i] = hide ? point : bones[i];
            }
            return result;
        }
    }
}
