using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Puts items (a helmet, a torch) onto the goblin copy. Same safety rule as the goblin himself:
    /// only the item's visible model is copied, while inactive, with every script stripped off.
    /// </summary>
    internal static class Gear
    {
        private static GameObject _staging;
        private static bool _loggedFulingGear;
        private static bool _loggedHeldScale;
        private static bool _loggedEffects;

        /// <summary>Which bone on the real Fuling an item slot attaches to, e.g. "m_helmet", "m_rightHand".</summary>
        public static Transform FindSlotOnCopy(GameObject prefab, Transform sourceVisual, Transform copyVisual, string slotField)
        {
            var vis = prefab.GetComponent<VisEquipment>();
            if (vis == null) return null;

            Transform sourceJoint = null;
            try { sourceJoint = Traverse.Create(vis).Field(slotField).GetValue<Transform>(); } catch { }
            if (sourceJoint == null) return null;

            // Rebuild the bone's path inside the model, then find the same bone in our copy.
            var names = new List<string>();
            for (Transform t = sourceJoint; t != null && t != sourceVisual; t = t.parent) names.Add(t.name);
            if (names.Count == 0) return copyVisual;
            names.Reverse();
            return copyVisual.Find(string.Join("/", names.ToArray()));
        }

        /// <summary>
        /// Copies an item's worn/held model onto the goblin copy, the same two ways the game does it:
        ///  - "attach_skin" items (most Fuling armour) bend with the body: they're pointed at the body's bones.
        ///  - "attach" items (torches, weapons) are rigid: they're snapped onto a bone like the right hand.
        /// Returns false with a reason if it can't.
        /// </summary>
        public static bool TryAttach(string itemPrefabName, Transform rigidJoint, SkinnedMeshRenderer body, Transform skinnedParent, float size, out string reason)
            => TryAttach(itemPrefabName, rigidJoint, body, skinnedParent, size, out reason, out _, out _);

        public static bool TryAttach(string itemPrefabName, Transform rigidJoint, SkinnedMeshRenderer body, Transform skinnedParent, float size,
            out string reason, out GameObject attached, out Vector3 itemWorldScale)
        {
            reason = null;
            attached = null;
            itemWorldScale = Vector3.one;
            try
            {
                if (ObjectDB.instance == null) { reason = "item database not ready"; return false; }

                GameObject item = ObjectDB.instance.GetItemPrefab(itemPrefabName);
                if (item == null) { reason = $"no item called '{itemPrefabName}'"; return false; }

                Transform skin = FindDeep(item.transform, "attach_skin");
                Transform rigid = FindDeep(item.transform, "attach");

                if (skin != null)
                {
                    if (body == null) { reason = "couldn't find the goblin's body to fit it to"; return false; }
                    // Bendy gear only works on the skeleton it was made for. Player gear has a different bone
                    // list from the Fuling's, so it would come out as a twisted mess: refuse it cleanly instead.
                    var parts = skin.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    int bodyBones = body.bones != null ? body.bones.Length : 0;
                    var misfit = parts.FirstOrDefault(r => r.bones == null || r.bones.Length != bodyBones);
                    if (misfit != null)
                    {
                        reason = $"'{itemPrefabName}' bends with the body and was made for a different skeleton " +
                                 $"({(misfit.bones != null ? misfit.bones.Length : 0)} bones vs the Fuling's {bodyBones})";
                        return false;
                    }

                    GameObject copy = SafeCopy(skin.gameObject, itemPrefabName, out reason);
                    if (copy == null) return false;

                    copy.transform.SetParent(skinnedParent, false);
                    copy.transform.localPosition = Vector3.zero;
                    copy.transform.localRotation = Quaternion.identity;
                    // Same trick the game uses: the armour was made for this skeleton, so share its bones.
                    foreach (var smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.rootBone = body.rootBone;
                        smr.bones = body.bones;
                    }
                    attached = copy;
                    return true;
                }

                if (rigid != null)
                {
                    if (rigidJoint == null) { reason = "no bone to attach to"; return false; }
                    GameObject copy = SafeCopy(rigid.gameObject, itemPrefabName, out reason);
                    if (copy == null) return false;

                    copy.transform.SetParent(rigidJoint, false);
                    copy.transform.localPosition = Vector3.zero;
                    copy.transform.localRotation = Quaternion.identity;

                    // His hand bone is scaled differently from what items expect, which made held items huge.
                    // Force the item to its real-world size (the size it is lying on the ground), times the size setting.
                    Vector3 want = rigid.lossyScale * size;
                    Vector3 bone = rigidJoint.lossyScale;
                    copy.transform.localScale = new Vector3(SafeDiv(want.x, bone.x), SafeDiv(want.y, bone.y), SafeDiv(want.z, bone.z));
                    if (!_loggedHeldScale)
                    {
                        _loggedHeldScale = true;
                        Plugin.Log.LogInfo($"Item fit ({itemPrefabName}): bone scale {bone}, item scale {rigid.lossyScale}, applied local scale {copy.transform.localScale}.");
                    }

                    WakeEffects(copy);
                    attached = copy;
                    itemWorldScale = rigid.lossyScale;
                    return true;
                }

                reason = $"'{itemPrefabName}' has no wearable model";
                return false;
            }
            catch (Exception e)
            {
                reason = $"{e.GetType().Name}: {e.Message}";
                return false;
            }
        }

        /// <summary>Copies a model while inactive, strips its scripts, and refuses anything networked.</summary>
        private static GameObject SafeCopy(GameObject source, string name, out string reason)
        {
            reason = null;
            if (_staging == null)
            {
                _staging = new GameObject("FulingHeckler_GearStaging");
                _staging.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_staging);
            }

            GameObject copy = UnityEngine.Object.Instantiate(source, _staging.transform, false);
            Strip(copy);
            if (copy.GetComponentInChildren<ZNetView>(true) != null)
            {
                UnityEngine.Object.DestroyImmediate(copy);
                reason = "safety check failed (network parts on item copy)";
                return null;
            }
            copy.name = name;
            copy.SetActive(true);
            return copy;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindDeep(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>The goblin copy's main body mesh: the one armour gets fitted to.</summary>
        public static SkinnedMeshRenderer FindBodyOnCopy(GameObject prefab, Transform sourceVisual, Transform copyVisual)
        {
            // Prefer the exact body model the game uses, matched by its path in the model.
            try
            {
                var vis = prefab.GetComponent<VisEquipment>();
                var src = vis != null ? Traverse.Create(vis).Field("m_bodyModel").GetValue<SkinnedMeshRenderer>() : null;
                if (src != null)
                {
                    var names = new List<string>();
                    for (Transform t = src.transform; t != null && t != sourceVisual; t = t.parent) names.Add(t.name);
                    names.Reverse();
                    var match = names.Count == 0 ? copyVisual : copyVisual.Find(string.Join("/", names.ToArray()));
                    var smr = match != null ? match.GetComponent<SkinnedMeshRenderer>() : null;
                    if (smr != null) return smr;
                }
            }
            catch { /* fall back below */ }

            // Otherwise: the skinned mesh with the most bones is the body.
            return copyVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderByDescending(r => r.bones != null ? r.bones.Length : 0)
                .FirstOrDefault();
        }

        private static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

        /// <summary>
        /// Torch flames and lights are normally switched on by the game when the item is equipped: it activates a
        /// hidden child called "equiped" (the game's spelling) that holds the fire. We stripped the scripts that do
        /// that, so switch the fire on directly, including every hidden parent above it.
        /// </summary>
        private static void WakeEffects(GameObject go)
        {
            var equipped = FindDeep(go.transform, "equiped") ?? FindDeep(go.transform, "equipped");
            if (equipped != null) ActivateUpTo(equipped, go.transform);

            foreach (var light in go.GetComponentsInChildren<Light>(true))
            {
                ActivateUpTo(light.transform, go.transform);
                light.enabled = true;
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ActivateUpTo(ps.transform, go.transform);
                ps.Play(true);
            }

            if (!_loggedEffects)
            {
                _loggedEffects = true;
                Plugin.Log.LogInfo($"Held item effects: 'equiped' part {(equipped != null ? "found" : "not found")}, " +
                    $"{go.GetComponentsInChildren<Light>(true).Length} light(s), {go.GetComponentsInChildren<ParticleSystem>(true).Length} particle system(s).");
            }
        }

        private static void ActivateUpTo(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                if (t == root) break;
            }
        }

        public static void Strip(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
        }

        /// <summary>Logs everything the real Fuling can carry, once, so you can pick names for the config.</summary>
        public static void LogOwnGearOnce(GameObject prefab)
        {
            if (_loggedFulingGear) return;
            _loggedFulingGear = true;
            var items = AllOwnItems(prefab).Select(i => $"{i.name} ({ItemType(i)})").Distinct().ToArray();
            Plugin.Log.LogInfo($"{prefab.name} can carry: {(items.Length > 0 ? string.Join(", ", items) : "nothing found")}. Any of these names work in the Helmets and Torch Item settings.");
        }

        private static IEnumerable<GameObject> AllOwnItems(GameObject prefab)
        {
            var humanoid = prefab.GetComponent<Humanoid>();
            if (humanoid == null) yield break;
            var t = Traverse.Create(humanoid);

            foreach (var field in new[] { "m_defaultItems", "m_randomWeapon", "m_randomArmor", "m_randomShield" })
            {
                GameObject[] arr = null;
                try { arr = t.Field(field).GetValue<GameObject[]>(); } catch { }
                if (arr != null) foreach (var go in arr) if (go != null) yield return go;
            }

            System.Collections.IEnumerable sets = null;
            try { sets = t.Field("m_randomSets").GetValue() as System.Collections.IEnumerable; } catch { }
            if (sets == null) yield break;
            foreach (var set in sets)
            {
                GameObject[] arr = null;
                try { arr = Traverse.Create(set).Field("m_items").GetValue<GameObject[]>(); } catch { }
                if (arr != null) foreach (var go in arr) if (go != null) yield return go;
            }
        }

        private static string ItemType(GameObject item)
        {
            try
            {
                var drop = item.GetComponent<ItemDrop>();
                if (drop == null) return "?";
                return Traverse.Create(drop).Field("m_itemData").Field("m_shared").Field("m_itemType").GetValue()?.ToString() ?? "?";
            }
            catch { return "?"; }
        }
    }
}
