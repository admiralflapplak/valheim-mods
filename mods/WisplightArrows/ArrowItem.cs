using System;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WisplightArrows
{
    /// <summary>
    /// The wisplight arrow: a copy of the wooden arrow with little damage of its own, whose projectile carries a copy of the Wisplight's
    /// wisp ball. The game clears Mistlands mist round anything with a Demister (and its ParticleSystemForceField, whose endRange is the
    /// radius ParticleMist keeps clear), so the arrow parts the mist as it flies. Where it lands (or runs out of flight) it leaves a glow,
    /// riding along on a creature it hit (GlowFollow):
    /// another wisp ball on a networked object every player sees, gone after a while (TimedDestruction, on its owner's game).
    /// Three prefabs: the item (registered through ItemReload, safe to repeat), and the projectile and the glow (ZNetScene only, so other
    /// players' games know them by name).
    /// </summary>
    internal static class ArrowItem
    {
        public const string PrefabName = "nick_wisparrow";
        public const string ProjectileName = "nick_wisparrow_projectile";
        public const string GlowName = "nick_wisparrow_glow";

        internal static GameObject Prefab;
        private static GameObject _projectile, _glow;
        internal static GameObject Glow => _glow;
        internal static GameObject NockTemplate;   // the wisp at the bow hand while one is drawn (NockGlow)
        internal static GameObject TrailPoint;   // a bare clear zone the flying arrow leaves behind it (MistTrail)
        private static Recipe _recipe;

        /// <summary>Registers (and builds, the first time) the arrow, its projectile and glow, and its recipe in whatever of ZNetScene and
        /// ObjectDB exists.</summary>
        public static void Register(ZNetScene scene, ObjectDB db)
        {
            if (scene == null && db == null) return;
            scene = scene != null ? scene : ZNetScene.instance;
            if (Prefab == null && scene != null) Build(scene);
            if (Prefab == null) return;

            ItemReload.Replace(scene, db, new[] { Prefab });
            ItemReload.Replace(scene, null, new[] { _projectile, _glow });   // (not items: ZNetScene only)
            Content.ItemExtras(new[] { Prefab }, db);

            if (db == null || scene == null) return;
            GameObject bench = scene.GetPrefab("piece_workbench");
            CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
            _recipe = Content.Recipe(Prefab, station, amount: Plugin.PerCraft.Value, costText: Plugin.Cost.Value, log: Plugin.Log, db: db);
            ApplyRecipeSettings();
            Plugin.Synced.NeedsServer(_recipe);
        }

        /// <summary>The cost, station and amount from the settings, onto the recipe (a mistake in the cost or station keeps the last good one).</summary>
        public static void ApplyRecipeSettings()
        {
            if (_recipe == null) return;
            Costs.Apply(_recipe, Plugin.Cost.Value, Plugin.Log);
            Costs.ApplyStation(_recipe, Plugin.Station.Value, Plugin.Log);
            _recipe.m_amount = Plugin.PerCraft.Value;
        }

        /// <summary>The arrow's own damage from the setting (arrows already in bags share the prefab's data, so they follow).</summary>
        public static void ApplyDamage()
        {
            ItemDrop drop = Prefab != null ? Prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return;
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_damages = new HitData.DamageTypes { m_pierce = Plugin.Damage.Value };
            s.m_damagesPerLevel = new HitData.DamageTypes();
        }

        /// <summary>The clear radii and the glow's time from the settings. Glows and arrows already out keep theirs.</summary>
        public static void ApplyMist()
        {
            SetRadius(_projectile, Plugin.FlightRadius.Value);
            SetRadius(_glow, Plugin.LandingRadius.Value);
            TimedDestruction timer = _glow != null ? _glow.GetComponent<TimedDestruction>() : null;
            if (timer != null) timer.m_timeout = Plugin.LandingSeconds.Value;
        }

        public static void Forget()
        {
            Prefab = null;
            _projectile = _glow = TrailPoint = NockTemplate = null;   // the prefabs and their holder stay alive for arrows already out there; the next copy replaces them by name
            _recipe = null;
            ArrowModel.Forget();
        }

        // ---- when the game registers its own ---------------------------------------------------------------------
        // ZNetScene.Awake can run before ObjectDB.Awake, and the main menu has an ObjectDB copy of its own: register from all three
        // (Register is safe to repeat). The host deletes a dropped item or flying arrow it doesn't know, so none of them may be missed.

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
            catch (Exception e) { Plugin.Log.LogError("Registering the wisplight arrow failed: " + e); }
        }

        // ---- the prefabs -------------------------------------------------------------------------------------------

        private static void Build(ZNetScene scene)
        {
            GameObject wood = scene.GetPrefab("ArrowWood");
            GameObject ball = WispBall(scene);
            GameObject woodProjectile = wood != null ? wood.GetComponent<ItemDrop>().m_itemData.m_shared.m_attack.m_attackProjectile : null;
            if (wood == null || ball == null || woodProjectile == null)
            {
                Plugin.Log.LogWarning("The game has no wooden arrow or Wisplight to copy: no wisplight arrows this session");
                return;
            }

            // Under an inactive holder, so the copies never wake up as real objects in the world.
            var holder = new GameObject("nick_wisparrow_holder");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);

            // Where it lands: a wisp ball on an object of its own that every player's game has, gone after a while.
            _glow = new GameObject(GlowName);
            _glow.transform.SetParent(holder.transform, false);
            _glow.AddComponent<ZNetView>();   // (not persistent: it never outlives its owner's session)
            var timer = _glow.AddComponent<TimedDestruction>();
            timer.m_triggerOnAwake = true;
            _glow.AddComponent<GlowFollow>();   // (on a creature it was shot into: follows it; on the ground: switches itself off)
            AddWisp(ball, _glow.transform, Vector3.zero);   // (at the arrow's tip: the game spawns it where the arrow struck)

            // In flight: the wooden arrow's projectile with a wisp riding on it, leaving the glow where it stops.
            _projectile = Object.Instantiate(woodProjectile, holder.transform);
            _projectile.name = ProjectileName;
            AddWisp(ball, _projectile.transform, Vector3.zero);
            _projectile.AddComponent<MistTrail>();
            TrailPoint = BareField(ball, holder.transform);

            // On the bow while one is drawn: a wisp of the archer's own (local, never networked).
            NockTemplate = new GameObject("nick_wisparrow_nock");
            NockTemplate.transform.SetParent(holder.transform, false);
            AddWisp(ball, NockTemplate.transform, Vector3.zero);
            Projectile p = _projectile.GetComponent<Projectile>();
            p.m_spawnOnHit = _glow;
            p.m_spawnOnHitChance = 1f;
            p.m_spawnCount = 1;
            p.m_spawnOnTtl = true;              // (shot into the sky, it still leaves its glow where it comes to an end)
            p.m_spawnOnCharacters = false;      // (a creature gets one that rides along on it instead: GlowFollow)
            p.m_copyProjectileRotation = false;
            p.m_randomSpawnOnHit.Clear();
            p.m_spawnItem = null;

            Prefab = Object.Instantiate(wood, holder.transform);
            Prefab.name = PrefabName;
            ItemDrop drop = Prefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = "$nick_wisparrow";
            s.m_description = "$nick_wisparrow_desc";
            s.m_value = 0;   // a price at the trader would make a cheap-to-craft item a coin machine
            s.m_attack.m_attackProjectile = _projectile;
            drop.m_itemData.m_dropPrefab = Prefab;
            ApplyDamage();
            ApplyMist();

            try
            {
                ArrowModel.Dress(Prefab, ball, scene, glow: true);         // (lying on the ground: a faint glow at the orb)
                ArrowModel.Dress(_projectile, ball, scene, glow: false);   // (in flight the wisp riding on it lights it)
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not put the orb on the arrow: " + e); }
            // The icon: the game's silver arrow icon with the orb painted on (ArrowIcon). Failing that, drawn from the model flat, seen
            // from the side with the tip to the right, as the game's own arrow icons are (looking along +X, the picture's right is the
            // arrow's -Z end, its tip).
            Sprite icon = null;
            try { icon = ArrowIcon.FromGame(scene); }
            catch (Exception e) { Plugin.Log.LogInfo("Could not paint the icon from the game's: " + e.Message); }
            if (icon != null) SetIcon(drop, icon);
            else
            {
                try
                {
                    ArrowModel.IconLook(Prefab, true);
                    ArrowIcon.AddHaze(Icon.MakeItem(Prefab, setIcon: true, look: Quaternion.LookRotation(Vector3.right, Vector3.up)));
                }
                catch (Exception e) { Plugin.Log.LogWarning("Could not draw the wisplight arrow icon: " + e.Message); }
                finally { ArrowModel.IconLook(Prefab, false); }
            }
        }

        /// <summary>The item's first icon (a new array: never write into one shared with the wooden arrow it was copied from).</summary>
        private static void SetIcon(ItemDrop drop, Sprite icon)
        {
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            Sprite[] icons = s.m_icons != null && s.m_icons.Length > 1 ? (Sprite[])s.m_icons.Clone() : new Sprite[1];
            icons[0] = icon;
            s.m_icons = icons;
        }

        /// <summary>The ball the Wisplight's status effect floats over the player (null if the game changed it).</summary>
        private static GameObject WispBall(ZNetScene scene)
        {
            GameObject wisplight = scene.GetPrefab("Demister");
            ItemDrop drop = wisplight != null ? wisplight.GetComponent<ItemDrop>() : null;
            return (drop != null ? drop.m_itemData.m_shared.m_equipStatusEffect as SE_Demister : null)?.m_ballPrefab;
        }

        /// <summary>A copy of the wisp ball under parent, with its looks and its mist clearing only (nothing networked or physical of its own).</summary>
        private static void AddWisp(GameObject ball, Transform parent, Vector3 at)
        {
            GameObject wisp = Object.Instantiate(ball, parent, false);
            wisp.name = "wisp";
            wisp.transform.localPosition = at;
            wisp.transform.localRotation = Quaternion.identity;
            foreach (ZSyncTransform c in wisp.GetComponentsInChildren<ZSyncTransform>(true)) Object.DestroyImmediate(c);
            foreach (ZNetView c in wisp.GetComponentsInChildren<ZNetView>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody c in wisp.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
            foreach (Collider c in wisp.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Demister d in wisp.GetComponentsInChildren<Demister>(true))
            {
                if (d.m_disableForcefieldDelay > 0f) Plugin.Log.LogInfo($"The wisp ball's force field switched itself off after {d.m_disableForcefieldDelay} s: kept on");
                d.m_disableForcefieldDelay = 0f;
            }
        }

        /// <summary>Only the wisp ball's force field and Demister, nothing to see or hit, for the trail's points (null if the ball has none).</summary>
        private static GameObject BareField(GameObject ball, Transform holder)
        {
            Demister source = ball.GetComponentInChildren<Demister>(true);
            if (source == null) return null;
            GameObject field = Object.Instantiate(source.gameObject, holder, false);
            field.name = "nick_wisparrow_trail";
            for (int i = field.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(field.transform.GetChild(i).gameObject);
            foreach (Component c in field.GetComponents<Component>())
                if (!(c is Transform || c is Demister || c is ParticleSystemForceField)) Object.DestroyImmediate(c);
            field.GetComponent<Demister>().m_disableForcefieldDelay = 0f;
            field.AddComponent<TrailPoint>();
            return field;
        }

        private static void SetRadius(GameObject prefab, float radius)
        {
            if (prefab == null) return;
            foreach (ParticleSystemForceField f in prefab.GetComponentsInChildren<ParticleSystemForceField>(true)) f.endRange = radius;
        }
    }
}
