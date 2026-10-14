using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Spyglass
{
    /// <summary>
    /// The spyglass as an item: a copy of the Club (right layer, collider, drop behaviour) with its damage taken off and a model of its own
    /// built from cylinders in the game's bronze and wood materials. Registered by name through ItemReload (safe to repeat, and a hot reload
    /// replaces it without ever leaving the game without the prefab), with one recipe at a station the server's settings choose.
    /// </summary>
    internal static class SpyglassItem
    {
        public const string PrefabName = "nick_spyglass";
        public const string ItemName = "$nick_spyglass";
        private const float ModelScale = 1.25f;   // the model is built about half a metre long; the game's items are chunkier
        private const float ModelLength = 0.52f;   // from the eyepiece to the lens, before scaling
        private const float GripAt = 0.2f;   // where along the model (metres from the eyepiece) the hand holds it
        private const float TableCentre = 0.28f;   // on the horizontal stand: how far from the mount the spyglass's middle lies (the club's is 0.6, and 1.15 long)
        private const float WallRaise = 0.2f;   // on the wall stand: raised this much above where the club's middle would hang
        private const float TableRaise = 0.03f;   // the club's offset lays its axis on the table; the spyglass is thinner, so it sits this much higher
        private const float ClubCentre = 0.16f;   // where the club's model is centred along its attach point's Z (the item stand and the ground hang the item from it)

        internal static GameObject Prefab;
        private static Recipe _recipe;

        /// <summary>Registers (and builds, the first time) the item and its recipe in whatever of ZNetScene and ObjectDB exists.</summary>
        public static void Register(ZNetScene scene, ObjectDB db)
        {
            if (scene == null && db == null) return;
            scene = scene != null ? scene : ZNetScene.instance;
            if (Prefab == null && scene != null) Prefab = Build(scene);
            if (Prefab == null) return;

            ItemReload.Replace(scene, db, new[] { Prefab });
            Content.ItemExtras(new[] { Prefab }, db);
            if (db == null || scene == null) return;

            GameObject forge = scene.GetPrefab("forge");
            CraftingStation station = forge != null ? forge.GetComponent<CraftingStation>() : null;
            _recipe = Content.Recipe(Prefab, station, costText: Plugin.Cost.Value, log: Plugin.Log, db: db);
            ApplyRecipeSettings();
            Plugin.Synced.NeedsServer(_recipe);
        }

        /// <summary>The cost and station from the settings, onto the recipe (a mistake in either keeps the last good one).</summary>
        public static void ApplyRecipeSettings()
        {
            if (_recipe == null) return;
            var log = Plugin.Log;
            Costs.Apply(_recipe, Plugin.Cost.Value, log);
            Costs.ApplyStation(_recipe, Plugin.Station.Value, log);
        }

        /// <summary>The most durability a spyglass has, from the setting (items already made keep their own value until mended).</summary>
        public static void ApplyDurability()
        {
            ItemDrop drop = Prefab != null ? Prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return;
            drop.m_itemData.m_shared.m_maxDurability = Plugin.MaxDurability.Value;
        }

        public static void Forget()
        {
            Prefab = null;
            _recipe = null;
            _tableEntry = _wallEntry = null;
        }


        // ---- when the game registers its own ---------------------------------------------------------------------
        // ZNetScene.Awake can run before ObjectDB.Awake, and the main menu has an ObjectDB copy of its own: register from all three
        // (Register is safe to repeat). The host deletes a placed or dropped item it doesn't know, so none of them may be missed.

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Guarded(() => Register(__instance, ObjectDB.instance));
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake
        {
            private static void Postfix(ObjectDB __instance) => Guarded(() => Register(ZNetScene.instance, __instance));
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDB_CopyOtherDB
        {
            private static void Postfix(ObjectDB __instance) => Guarded(() => Register(ZNetScene.instance, __instance));
        }

        private static void Guarded(Action register)
        {
            try { register(); }
            catch (Exception e) { Plugin.Log.LogError("Registering the spyglass failed: " + e); }
        }

        // ---- the prefab -------------------------------------------------------------------------------------------

        private static GameObject Build(ZNetScene scene)
        {
            GameObject club = scene.GetPrefab("Club");
            if (club == null) { Plugin.Log.LogWarning("No Club prefab to copy: the spyglass can't be made"); return null; }

            // Under an inactive holder, so the copy never wakes up as a real object in the world.
            var holder = new GameObject("nick_spyglass_holder");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            GameObject go = Object.Instantiate(club, holder.transform);
            go.name = PrefabName;

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = ItemName;
            s.m_description = "$nick_spyglass_desc";
            s.m_maxStackSize = 1;
            s.m_maxQuality = 1;
            s.m_weight = 1f;
            s.m_value = 0;                    // a price at the trader would make a cheap-to-craft item a coin machine
            s.m_useDurability = true;         // it wears while looked through (Zoom.Wear), and is mended at the forge
            s.m_canBeReparied = true;
            s.m_maxDurability = Plugin.MaxDurability.Value;
            s.m_durabilityPerLevel = 0f;
            s.m_durabilityDrain = 0f;         // (that is what a swing costs a weapon: it has none)
            s.m_useDurabilityDrain = 0f;
            drop.m_itemData.m_durability = s.m_maxDurability;
            s.m_damages = new HitData.DamageTypes();
            s.m_damagesPerLevel = new HitData.DamageTypes();
            s.m_blockPower = 0f;
            s.m_blockPowerPerLevel = 0f;
            s.m_deflectionForce = 0f;
            s.m_attackForce = 0f;
            s.m_backstabBonus = 1f;
            s.m_movementModifier = 0f;
            s.m_equipStatusEffect = null;
            s.m_variants = 0;
            drop.m_itemData.m_dropPrefab = go;

            try { DressModel(go, scene); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not build the spyglass model: " + e); }
            try { Icon.MakeItem(go, setIcon: true); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not draw the spyglass icon: " + e.Message); }
            CentreOnStand(go);
            return go;
        }

        /// <summary>The item stand hangs an item from its attach point (the club's, which leans a little: the spyglass's is made upright),
        /// and the club's model is centred 0.16 along its length; the spyglass's centre is somewhere else. The club has an offset for the
        /// horizontal stand only (it lies turned round, so the difference runs the other way), and none for the wall stand, where the
        /// item's length is the stand's up: the spyglass gets its own entry there, raised by the difference and the setting.</summary>
        private static void CentreOnStand(GameObject go)
        {
            Transform attach = go.transform.Find("attach");
            if (attach != null) attach.localRotation = Quaternion.identity;
            _attachZ = attach != null ? attach.localPosition.z : 0f;
            ItemDrop drop = go.GetComponent<ItemDrop>();
            var offsets = drop != null ? drop.m_itemData.m_shared.m_itemStandOffsets : null;
            if (offsets == null) return;
            _tableEntry = null;
            foreach (ItemStand.OrientationSettings o in offsets)
            {
                Plugin.Log.LogInfo($"Item stand offset (from the club): orientations {o.m_orientations}, position {o.m_position}, rotation {o.m_rotation}, scale {o.m_scale}");
                o.m_orientations &= ~ItemStand.Orientation.Vertical;
                if (o.m_orientations == 0) continue;
                // lying flat (the club's slight tilt is for its length: the thick end would sink into the table), turned the club's way round
                o.m_rotation = new Vector3(0f, o.m_rotation.y, o.m_rotation.z);
                _tableEntry = o;
                _tableBase = o.m_position;
            }
            offsets.RemoveAll(o => o.m_orientations == 0);
            _wallEntry = new ItemStand.OrientationSettings { m_orientations = ItemStand.Orientation.Vertical };
            offsets.Add(_wallEntry);
            ApplyStandSettings();
        }

        private static ItemStand.OrientationSettings _tableEntry, _wallEntry;
        private static Vector3 _tableBase;
        private static float _attachZ;

        /// <summary>The stand offsets, from the constants.</summary>
        private static void ApplyStandSettings()
        {
            float centre = (GripAt - ModelLength * 0.5f) * ModelScale;
            if (_tableEntry != null)
                _tableEntry.m_position = new Vector3(_tableBase.x, _tableBase.y + TableRaise, TableCentre + centre - _attachZ);
            if (_wallEntry != null)
                _wallEntry.m_position = new Vector3(0f, 0f, ClubCentre - centre + WallRaise);
        }

        /// <summary>Takes the club's model off and puts the spyglass on the "attach" child: that is what the hand holds and the dropped item shows.</summary>
        private static void DressModel(GameObject go, ZNetScene scene)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
            foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
            foreach (LODGroup l in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(l);

            Transform attach = go.transform.Find("attach");
            if (attach == null)
            {
                attach = new GameObject("attach").transform;
                attach.SetParent(go.transform, false);
            }
            // The club's model, glow and trail go; its "collider" stays (the item on the ground, a dropped one, is solid through it: without it
            // the item falls through the world). The game takes it off the copy that is held in the hand.
            for (int i = attach.childCount - 1; i >= 0; i--)
                if (attach.GetChild(i).name != "collider") Object.DestroyImmediate(attach.GetChild(i).gameObject);

            Material bronze = MaterialOf(scene, "Bronze");
            Material wood = MaterialOf(scene, "FineWood") ?? MaterialOf(scene, "Wood");
            Material glass = bronze != null ? Tinted(bronze, new Color(0.08f, 0.1f, 0.14f)) : null;
            Material leather = wood != null ? Tinted(wood, new Color(0.28f, 0.17f, 0.1f)) : null;

            FitCollider(attach);
            var model = new GameObject("model").transform;
            model.SetParent(attach, false);
            // The game's weapons lie along their attach point's Z (the club is 1.15 m of Z), which the hand closes around: the model is
            // built along Y, so turn it about X to lie along Z, with the big end out of the back of the hand and the eyepiece forward,
            // and slide it so the fist wraps the narrow tube. Scaled up to sit with the game's chunky items.
            model.localScale = Vector3.one * ModelScale;
            model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            model.localPosition = new Vector3(0f, 0f, GripAt * ModelScale);

            // Eyepiece to objective, along +Y, in metres; the grip is at the origin.
            Segment(model, "eyepiece", 0.00f, 0.07f, 0.013f, leather ?? wood);
            Segment(model, "tube_small", 0.07f, 0.21f, 0.017f, bronze);
            Segment(model, "ring_a", 0.205f, 0.225f, 0.0235f, leather ?? wood);
            Segment(model, "tube_mid", 0.22f, 0.36f, 0.022f, bronze);
            Segment(model, "ring_b", 0.355f, 0.375f, 0.0285f, leather ?? wood);
            Segment(model, "tube_large", 0.37f, 0.50f, 0.027f, bronze);
            Segment(model, "lip", 0.49f, 0.52f, 0.033f, bronze);
            Segment(model, "lens", 0.519f, 0.522f, 0.028f, glass ?? bronze);
            AddSheathed(attach, model);
        }

        private const float SheathCentre = 0.16f;   // the club's model is centred this far along its length when it is sheathed on the back

        /// <summary>
        /// How it hangs on the back when it is put away. The game takes the item's "attach_back" part for that (and its "attach" part
        /// if there is none, which is how the club hangs): the club's length runs along +Z from the back joint, the spyglass's the other
        /// way round (eyepiece forward in the hand), so it stuck up above the shoulder. This is a turned copy with its length along +Z and
        /// its middle where the club's is. It comes first among the children (the game takes the first of the two it meets) and is
        /// switched off, as the game's own are: the item on the ground shows "attach" only.
        /// </summary>
        private static void AddSheathed(Transform attach, Transform model)
        {
            Transform back = Object.Instantiate(attach.gameObject, attach.parent).transform;
            back.name = "attach_back";
            back.SetSiblingIndex(0);
            for (int i = back.childCount - 1; i >= 0; i--)
                if (back.GetChild(i).name == "collider") Object.DestroyImmediate(back.GetChild(i).gameObject);
            Transform turned = back.Find("model");
            if (turned != null)
            {
                turned.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;   // (turned half way round: the length now runs along +Z from the eyepiece)
                turned.localPosition = new Vector3(0f, 0f, SheathCentre - ModelLength * 0.5f * ModelScale);   // so the middle of it is at SheathCentre
            }
            back.gameObject.SetActive(false);
        }

        /// <summary>The club's collision box, made the spyglass's size: as long as the model and a little thicker, centred on it.</summary>
        private static void FitCollider(Transform attach)
        {
            Transform collider = attach.Find("collider");
            BoxCollider box = collider != null ? collider.GetComponent<BoxCollider>() : null;
            if (box == null)
            {
                Plugin.Log.LogWarning("The club has no collision box to fit: the spyglass would fall through the ground");
                return;
            }
            collider.localPosition = Vector3.zero;
            collider.localRotation = Quaternion.identity;
            float from = GripAt * ModelScale;                           // the eyepiece end, along the attach's Z
            float to = (GripAt - ModelLength) * ModelScale;             // the objective end
            box.size = new Vector3(0.1f, 0.1f, (from - to) + 0.02f);
            box.center = new Vector3(0f, 0f, (from + to) * 0.5f);
        }

        private static void Segment(Transform parent, string name, float from, float to, float radius, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = name;
            Collider c = part.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);   // (the item prefab keeps its own collider)
            part.transform.SetParent(parent, false);
            part.transform.localPosition = new Vector3(0f, (from + to) * 0.5f, 0f);
            part.transform.localScale = new Vector3(radius * 2f, (to - from) * 0.5f, radius * 2f);   // (the cylinder is 2 high and 1 wide)
            if (material != null) part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>The material of a game item's model (the game's own look; Shader.Find would not have the shaders it strips).</summary>
        private static Material MaterialOf(ZNetScene scene, string itemPrefab)
        {
            GameObject item = scene.GetPrefab(itemPrefab);
            if (item == null) return null;
            // (the item's root carries a particle renderer with a see-through material: take the model's own)
            MeshRenderer r = item.GetComponentInChildren<MeshRenderer>(true);
            return r != null ? r.sharedMaterial : null;
        }

        /// <summary>A plain-coloured copy of a game material: its texture off so the colour shows.</summary>
        private static Material Tinted(Material source, Color color)
        {
            var m = new Material(source) { name = "nick_spyglass_" + color.GetHashCode() };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }
    }
}
