using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponOils
{
    /// <summary>
    /// The oils as items, two per oil as with mead: a base made at the cauldron (a copy of a mead base, as a mortar of paste) and the finished oil
    /// the press makes of it (a copy of a mead, as a bottle), in the oil's colour with its biome's charm (OilModels). Registered by name through ItemReload (safe to
    /// repeat, and a hot reload replaces them without the game ever missing a prefab); the base's recipe is at the cauldron, and the press's
    /// conversions (OilPress.AddConversions) turn each base into its oil.
    /// </summary>
    internal static class OilItems
    {
        public const string BasePrefix = "nick_oilbase_";
        public const string OilPrefix = "nick_oil_";

        internal static readonly Dictionary<string, GameObject> Bases = new Dictionary<string, GameObject>();   // by oil id
        internal static readonly Dictionary<string, GameObject> Oils = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        private static GameObject _holder;   // inactive parent of the prefabs: lives as long as the game does (an old copy's items stay valid)

        /// <summary>The oil an item is (by its name), or null.</summary>
        public static OilDef OilOf(ItemDrop.ItemData item)
        {
            string name = item != null ? item.m_shared.m_name : null;
            const string key = "$" + OilPrefix;
            return name != null && name.StartsWith(key, StringComparison.Ordinal) ? OilDefs.Get(name.Substring(key.Length)) : null;
        }

        /// <summary>Whether this recipe is the cauldron recipe of one of the oil bases (for the boss gate).</summary>
        public static OilDef OilOf(Recipe recipe)
        {
            string name = recipe != null ? recipe.name : null;
            const string key = "Recipe_" + BasePrefix;
            return name != null && name.StartsWith(key, StringComparison.Ordinal) ? OilDefs.Get(name.Substring(key.Length)) : null;
        }

        /// <summary>Registers (and builds, the first time) the items and their recipes in whatever of ZNetScene and ObjectDB exists. Safe to repeat.</summary>
        public static void Register(ZNetScene scene, ObjectDB db)
        {
            if (scene == null && db == null) return;
            scene = scene != null ? scene : ZNetScene.instance;
            if (Oils.Count == 0 && scene != null) BuildAll(scene);
            if (Oils.Count == 0) return;

            GameObject[] all = Bases.Values.Concat(Oils.Values).ToArray();
            ItemReload.Replace(scene, db, all);
            Content.ItemExtras(all, db);

            if (db != null && scene != null)
            {
                GameObject cauldron = scene.GetPrefab("piece_cauldron");
                CraftingStation station = cauldron != null ? cauldron.GetComponent<CraftingStation>() : null;
                foreach (OilDef def in OilDefs.All)
                {
                    if (!Bases.TryGetValue(def.Id, out GameObject baseItem)) continue;
                    Recipe recipe = Content.Recipe(baseItem, station, def.CauldronLevel, 1, costText: Plugin.OilCosts[def.Id].Value, log: Plugin.Log, db: db);
                    Recipes[def.Id] = recipe;
                    Plugin.Synced.NeedsServer(recipe);
                }
            }
            OilPress.AddConversions();
        }

        /// <summary>Each recipe's cost from its setting (a mistake keeps the last good one).</summary>
        public static void ApplyCosts()
        {
            foreach (OilDef def in OilDefs.All)
                if (Recipes.TryGetValue(def.Id, out Recipe r)) Costs.Apply(r, Plugin.OilCosts[def.Id].Value, Plugin.Log);
        }

        public static void Forget()
        {
            Bases.Clear();
            Oils.Clear();
            Recipes.Clear();
            _holder = null;   // the prefabs and their holder stay alive for items already out there; the next copy replaces them by name
        }

        // ---- the prefabs -----------------------------------------------------------------------------------------

        private static void BuildAll(ZNetScene scene)
        {
            GameObject baseSource = scene.GetPrefab("MeadBaseFrostResist"), oilSource = scene.GetPrefab("MeadFrostResist");
            if (baseSource == null || oilSource == null) { Plugin.Log.LogWarning("The game has no mead to copy: no oils this session"); return; }

            OilModels.GlassBasis = GlassOf(scene);
            _holder = new GameObject("WeaponOils_items");
            _holder.SetActive(false);   // nothing under it wakes up: they are prefabs, not objects
            Object.DontDestroyOnLoad(_holder);

            foreach (OilDef def in OilDefs.All)
            {
                try
                {
                    Color colour = ColourOf(def);
                    Bases[def.Id] = MakeItem(baseSource, def, BasePrefix + def.Id, "$" + BasePrefix + def.Id, "$nick_oilbase_desc", colour, isBase: true);
                    Oils[def.Id] = MakeItem(oilSource, def, OilPrefix + def.Id, "$" + OilPrefix + def.Id, "$" + OilPrefix + def.Id + "_desc", colour, isBase: false);
                    OilHands.MakeEffect(def, Oils[def.Id].GetComponent<ItemDrop>().m_itemData.GetIcon());   // (the icon on the HUD while it is on bare hands)
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Could not build the {def.Id} oil: {e}");
                    Bases.Remove(def.Id);
                    Oils.Remove(def.Id);
                }
            }
            StatusEffects.Register();   // (the hands' effects into whatever ObjectDB there is now; the chill's patches add them to later ones)
        }

        /// <summary>The crystal wall's see-through material (null if the game changed it: the bottles then have pale opaque glass).</summary>
        private static Material GlassOf(ZNetScene scene)
        {
            GameObject wall = scene.GetPrefab("crystal_wall_1x1");
            if (wall == null) return null;
            foreach (MeshRenderer r in wall.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("crystal_window", StringComparison.Ordinal)) return r.sharedMaterial;
            return null;
        }

        private static readonly Quaternion IconView = Quaternion.Euler(22f, -28f, 0f);   // a little from above and to the side, the charm in view

        /// <summary>
        /// The copied mead's own model (the stew bowl, or the pouch) gives way to ours (OilModels): a mortar for a base, a bottle for an oil,
        /// with a box to land on that fits it. Our parts are dressed in copies of the mead's own material, so they use the game's shader.
        /// </summary>
        private static void ReplaceModel(GameObject go, OilDef def, Color colour, bool isBase)
        {
            Material basis = null;
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (basis == null && r.sharedMaterial != null) basis = r.sharedMaterial;
            if (basis == null) return;   // (nothing to dress ours in: keep the mead's look)

            if (isBase)
            {
                Transform stew = go.transform.Find("stew");
                if (stew != null) Object.DestroyImmediate(stew.gameObject);
                GameObject model = OilModels.Mortar(go.transform, def, colour, basis, go.layer);
                var box = model.AddComponent<BoxCollider>();   // (in the model's own, unscaled, units)
                box.center = new Vector3(0f, 0.045f, 0f);
                box.size = new Vector3(0.18f, 0.09f, 0.18f);
            }
            else
            {
                Transform attach = go.transform.Find("attach");
                if (attach == null) return;
                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
                    Object.DestroyImmediate(r.gameObject);   // the pouch, held and lying (their LOD groups go with them)
                OilModels.Bottle(attach, def, colour, basis, go.layer);
                Transform collider = attach.Find("collider");
                if (collider != null)
                {
                    collider.localPosition = new Vector3(0f, 0.17f, 0f);
                    collider.localRotation = Quaternion.identity;
                    collider.localScale = new Vector3(0.2f, 0.34f, 0.2f);
                }
            }
        }

        private static GameObject MakeItem(GameObject source, OilDef def, string prefabName, string name, string description, Color colour, bool isBase)
        {
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = prefabName;

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = name;
            s.m_description = description;
            s.m_itemType = isBase ? ItemDrop.ItemData.ItemType.Material : ItemDrop.ItemData.ItemType.Consumable;
            s.m_maxStackSize = isBase ? 20 : 10;
            s.m_value = 0;                      // a price at the trader would make a cheap-to-brew item a coin machine
            s.m_consumeStatusEffect = null;     // an oil goes on a weapon, never into the player: Player.ConsumeItem is taken over for it (OilApply)
            s.m_food = 0f;
            s.m_foodStamina = 0f;
            s.m_foodEitr = 0f;
            s.m_foodBurnTime = 0f;
            s.m_foodRegen = 0f;

            ReplaceModel(go, def, colour, isBase);
            foreach (Light light in go.GetComponentsInChildren<Light>(true)) light.color = colour;

            drop.m_itemData.m_dropPrefab = go;
            OilModels.IconGlass(true);   // (the glass reads better a little opaque at icon size)
            try { Icon.MakeItem(go, setIcon: true, look: IconView); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not draw the icon of " + prefabName + ": " + e.Message); }
            finally { OilModels.IconGlass(false); }
            return go;
        }

        /// <summary>An oil's colour: its effect's, but fire that also burns the dead (Cinder) is a deeper red than plain fire (Ember).</summary>
        internal static Color ColourOf(OilDef oil) =>
            oil.Effect == OilEffect.Fire && oil.Extra == OilEffect.Spirit ? new Color(0.9f, 0.2f, 0.08f) : ColourOf(oil.Effect);

        internal static Color ColourOf(OilEffect effect)
        {
            switch (effect)
            {
                case OilEffect.Pure: return new Color(0.7f, 0.9f, 0.95f);
                case OilEffect.Fire: return new Color(0.95f, 0.42f, 0.1f);
                case OilEffect.Frost: return new Color(0.55f, 0.8f, 0.95f);
                case OilEffect.Poison: return new Color(0.4f, 0.75f, 0.2f);
                case OilEffect.Lightning: return new Color(0.95f, 0.9f, 0.45f);
                case OilEffect.Spirit: return new Color(0.7f, 0.9f, 0.95f);
                case OilEffect.Physical: return new Color(0.6f, 0.62f, 0.66f);
                case OilEffect.StaminaSaving: return new Color(0.9f, 0.82f, 0.55f);
                case OilEffect.Durability: return new Color(0.45f, 0.3f, 0.18f);
                case OilEffect.LifeSteal: return new Color(0.6f, 0.1f, 0.12f);
                case OilEffect.Stagger: return new Color(0.75f, 0.55f, 0.28f);
                case OilEffect.Guard: return new Color(0.85f, 0.7f, 0.25f);
                default: return Color.white;
            }
        }

        // ---- when the game registers its own ---------------------------------------------------------------------
        // ZNetScene.Awake can run before ObjectDB.Awake, and the main menu has an ObjectDB copy of its own: register from all three
        // (Register is safe to repeat). The host deletes a dropped item it doesn't know, so none of them may be missed.

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
            catch (Exception e) { Plugin.Log.LogError("Registering the oils failed: " + e); }
        }
    }
}
