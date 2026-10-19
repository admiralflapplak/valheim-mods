using HarmonyLib;
using UnityEngine;

namespace WeaponOils
{
    /// <summary>
    /// The look of an oiled weapon: particles in the oil's colour shed along the weapon (they stay where they were as it swings, so a swing
    /// leaves a trail) and a soft light of the same colour. It is a component on the weapon's model wherever the game shows one: in the
    /// right or left hand, stowed on the back, or lying on the ground as a dropped item. It follows the model, so it goes when the weapon
    /// is put away or picked up, and the game's own effects on the model (an upgraded weapon's glow) are left as they are: ours are
    /// separate objects beside them.
    ///
    /// The local player's own weapons say what oil they have (their item data), a dropped item too; another player's game can't see item
    /// data, so each player's game writes the oil on each of its weapon slots to its own player object (a short name per slot, only when
    /// one changes) and the others read that. An oil this version doesn't know shows nothing.
    /// </summary>
    internal sealed class OilGlow : MonoBehaviour
    {
        /// <summary>Where a weapon model is shown.</summary>
        internal enum Slot { RightHand, LeftHand, RightBack, LeftBack, Ground, Hands }   // Hands: on a hand bone, for oil on bare hands (OilHands)

        internal static readonly Slot[] PlayerSlots = { Slot.RightHand, Slot.LeftHand, Slot.RightBack, Slot.LeftBack };

        /// <summary>The keys on the player object, one per slot a player carries a weapon in: the id of the oil on it ("" when none). Never removed, only set to "".</summary>
        internal static string KeyOf(Slot slot)
        {
            switch (slot)
            {
                case Slot.RightHand: return "WeaponOils.glow.rh";
                case Slot.LeftHand: return "WeaponOils.glow.lh";
                case Slot.RightBack: return "WeaponOils.glow.rb";
                default: return "WeaponOils.glow.lb";
            }
        }

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenRight = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenLeft = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");

        /// <summary>The item a player has in a slot (in hand, or put away on the back), or null.</summary>
        internal static ItemDrop.ItemData ItemIn(Player player, Slot slot)
        {
            switch (slot)
            {
                case Slot.RightHand: return RightItem(player);
                case Slot.LeftHand: return LeftItem(player);
                case Slot.RightBack: return HiddenRight(player);
                case Slot.LeftBack: return HiddenLeft(player);
                default: return null;
            }
        }

        private static Material _particleMaterial;   // the game's own sparkle material, found once

        internal Slot Where;
        private Player _player;
        private ItemDrop _drop;
        private ParticleSystem _particles;
        private Light _light;
        private Bounds _bounds;
        private string _shown = "";
        private float _next;
        private float _lightLevel;
        private bool _flicker;
        private float _seed;
        // A weapon bent to the player's skeleton (Skoll and Hati, the Berserkir axes, claws) has its origin at the player's feet, not in the
        // hand: its glow is a pair, one on each hand, moved along with the hands every frame.
        private ParticleSystem _twin;
        private Animator _animator;
        private Transform _rightHand, _leftHand;
        private bool _claws;
        private const float SkinnedBladeLength = 0.3f;

        private void Awake()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { enabled = false; return; }   // a dedicated server draws nothing
            _seed = Random.value * 100f;   // (the particles and the light are made when the first oil shows: most models never carry one)
        }

        private void Update()
        {
            if (Time.time >= _next)
            {
                _next = Time.time + 0.4f;
                string wanted = Plugin.ShowEffect.Value ? Read() : "";
                if (wanted != _shown) { _shown = wanted; Show(OilDefs.Get(wanted)); }
            }
            if (_light != null && _light.enabled)
            {
                float noise = Mathf.PerlinNoise(Time.time * (_flicker ? 14f : 2f), _seed);
                _light.intensity = _lightLevel * Plugin.GlowStrength.Value * (_flicker ? 0.55f + noise * 0.9f : 0.85f + noise * 0.3f);
            }
        }

        /// <summary>The oil on this model's weapon: from the item data for the local player's own weapons and a dropped item, from the player object for others.</summary>
        private string Read()
        {
            if (Where == Slot.Ground)
            {
                if (_drop == null) _drop = GetComponentInParent<ItemDrop>();
                OilDef onGround = _drop != null ? OilState.Active(_drop.m_itemData, out _) : null;
                return onGround != null ? onGround.Id : "";
            }
            if (_player == null) _player = GetComponentInParent<Player>();
            if (_player == null) return _shown;
            if (Where == Slot.Hands)
            {
                if (_player == Player.m_localPlayer) return OilHands.Active(_player, out _)?.Id ?? "";
                ZDO other = _player.GetComponent<ZNetView>()?.GetZDO();
                return other != null ? other.GetString(OilHands.GlowKey, "") : "";
            }
            if (_player == Player.m_localPlayer)
            {
                ItemDrop.ItemData item = ItemIn(_player, Where);
                OilDef oil = item != null ? OilState.Active(item, out _) : null;
                return oil != null ? oil.Id : "";
            }
            ZDO zdo = _player.GetComponent<ZNetView>()?.GetZDO();
            return zdo != null ? zdo.GetString(KeyOf(Where), "") : "";
        }

        // ---- the particles and the light -------------------------------------------------------------------------

        private void Build()
        {
            Material material = ParticleMaterial();
            if (material == null) { Plugin.Log.LogInfo("No sparkle material to borrow: the oil effect is off"); enabled = false; return; }
            EnsureBounds();

            var go = new GameObject("nick_oilglow");
            go.transform.SetParent(transform, false);
            _particles = go.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // (a new one starts playing with the engine's defaults)
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            ParticleSystem.MainModule main = _particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // they stay behind as the weapon moves: a swing leaves a trail
            main.maxParticles = 80;

            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = _bounds.center;
            shape.scale = new Vector3(Mathf.Max(0.03f, _bounds.size.x), Mathf.Max(0.03f, _bounds.size.y), Mathf.Max(0.03f, _bounds.size.z));
            shape.randomDirectionAmount = 1f;

            ParticleSystem.ColorOverLifetimeModule colour = _particles.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            ParticleSystem.SizeOverLifetimeModule size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var lightObject = new GameObject("nick_oillight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = _bounds.center;
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.range = 1.6f;
            _light.enabled = false;

            if (OnBothHands())
            {
                shape.position = Vector3.zero;
                shape.scale = new Vector3(0.04f, 0.04f, SkinnedBladeLength);
                _twin = Instantiate(go, transform).GetComponent<ParticleSystem>();
                _twin.name = "nick_oilglow_left";
                PlaceOnHands();
            }
        }

        private static readonly AccessTools.FieldRef<Character, Animator> CharacterAnimator = AccessTools.FieldRefAccess<Character, Animator>("m_animator");

        /// <summary>Whether this model is a weapon in hand bent to the player's skeleton (no mesh of its own on the hand); finds the hands if so.</summary>
        private bool OnBothHands()
        {
            if (Where != Slot.RightHand || GetComponentInChildren<SkinnedMeshRenderer>() == null) return false;
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null && filter.GetComponent<MeshRenderer>() != null) return false;
            if (_player == null) _player = GetComponentInParent<Player>();
            _animator = _player != null ? CharacterAnimator(_player) : null;
            if (_animator == null) return false;
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _claws = OilHands.IsFist(ItemIn(_player, Slot.RightHand));
            return _rightHand != null && _leftHand != null;
        }

        private void LateUpdate()
        {
            if (_twin != null) PlaceOnHands();
        }

        /// <summary>Each glow along its hand's blade: out of the thumb side of the fist, or forward over the knuckles for claws.</summary>
        private void PlaceOnHands()
        {
            if (_rightHand == null || _leftHand == null || _animator == null) return;
            Vector3 right = Place(_particles.transform, _rightHand, true), left = Place(_twin.transform, _leftHand, false);
            _light.transform.position = (right + left) * 0.5f;
        }

        private Vector3 Place(Transform glow, Transform hand, bool isRight)
        {
            Vector3 along;
            if (_claws)
            {
                Transform middle = _animator.GetBoneTransform(isRight ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                along = middle != null ? (middle.position - hand.position).normalized : hand.forward;
            }
            else along = hand.rotation * OilAnimation.ThumbSide(_animator, hand, isRight);
            if (along.sqrMagnitude < 0.5f) along = hand.forward;
            glow.position = hand.position + along * (0.06f + SkinnedBladeLength * 0.5f);
            glow.rotation = Quaternion.LookRotation(along);
            return glow.position;
        }

        /// <summary>Starts the oil's look (null: nothing: stops it).</summary>
        private void Show(OilDef oil)
        {
            if (oil == null)
            {
                if (_particles != null) _particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // (what is in the air fades by itself)
                if (_twin != null) _twin.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (_light != null) _light.enabled = false;
                return;
            }
            if (_particles == null)
            {
                try { Build(); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not build the oil effect: " + e.Message); enabled = false; return; }
                if (_particles == null) return;
            }
            Style style = StyleOf(oil.Effect);
            Color colour = Bright(OilItems.ColourOf(oil));
            float strength = Plugin.GlowStrength.Value;
            Shed(_particles, style, colour, strength);
            if (_twin != null) Shed(_twin, style, colour, strength);

            _lightLevel = style.Light;
            _flicker = style.Flicker;
            _light.color = colour;
            _light.enabled = style.Light > 0f;
        }

        /// <summary>A dark oil colour (Barkhide's brown, Leech's blood) lifted to glow: the same hue, its brightest part raised to 0.9.</summary>
        private static Color Bright(Color c)
        {
            float top = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return top >= 0.8f || top <= 0f ? c : new Color(c.r * 0.9f / top, c.g * 0.9f / top, c.b * 0.9f / top, c.a);
        }

        private static void Shed(ParticleSystem particles, Style style, Color colour, float strength)
        {
            ParticleSystem.MainModule main = particles.main;
            main.startColor = colour;
            main.startLifetime = new ParticleSystem.MinMaxCurve(style.LifeMin, style.LifeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(style.SizeMin * strength, style.SizeMax * strength);
            main.startSpeed = style.Speed;
            main.gravityModifier = style.Gravity;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = style.Rate * strength;
            if (!particles.isPlaying) particles.Play(true);
        }

        private struct Style
        {
            public float Rate, SizeMin, SizeMax, LifeMin, LifeMax, Speed, Gravity, Light;
            public bool Flicker;
        }

        /// <summary>How each kind of oil sheds: fire rises as embers, frost and spirit drift, poison and blood drip, lightning spits sparks.</summary>
        private static Style StyleOf(OilEffect effect)
        {
            switch (effect)
            {
                case OilEffect.Fire: return new Style { Rate = 22, SizeMin = 0.05f, SizeMax = 0.1f, LifeMin = 0.4f, LifeMax = 0.9f, Speed = 0.12f, Gravity = -0.2f, Light = 0.9f, Flicker = true };
                case OilEffect.Frost: return new Style { Rate = 14, SizeMin = 0.04f, SizeMax = 0.08f, LifeMin = 0.8f, LifeMax = 1.5f, Speed = 0.05f, Gravity = -0.03f, Light = 0.4f };
                case OilEffect.Poison: return new Style { Rate = 11, SizeMin = 0.04f, SizeMax = 0.08f, LifeMin = 0.7f, LifeMax = 1.1f, Speed = 0f, Gravity = 0.35f, Light = 0.4f };
                case OilEffect.Lightning: return new Style { Rate = 30, SizeMin = 0.03f, SizeMax = 0.06f, LifeMin = 0.1f, LifeMax = 0.25f, Speed = 0.7f, Gravity = 0f, Light = 0.9f, Flicker = true };
                case OilEffect.Spirit:
                case OilEffect.Pure: return new Style { Rate = 14, SizeMin = 0.06f, SizeMax = 0.11f, LifeMin = 1f, LifeMax = 1.7f, Speed = 0.1f, Gravity = -0.06f, Light = 0.5f };
                // (the oils that don't burn, freeze or spark had no light and few, small particles: on a weapon in hand they looked unoiled)
                case OilEffect.Physical: return new Style { Rate = 16, SizeMin = 0.04f, SizeMax = 0.07f, LifeMin = 0.2f, LifeMax = 0.45f, Speed = 0.12f, Gravity = 0f, Light = 0.4f, Flicker = true };   // steel glints
                case OilEffect.StaminaSaving: return new Style { Rate = 13, SizeMin = 0.05f, SizeMax = 0.09f, LifeMin = 0.8f, LifeMax = 1.2f, Speed = 0f, Gravity = 0.25f, Light = 0.4f };   // fat drips
                case OilEffect.Durability: return new Style { Rate = 12, SizeMin = 0.05f, SizeMax = 0.09f, LifeMin = 1f, LifeMax = 1.5f, Speed = 0.04f, Gravity = -0.02f, Light = 0.35f };   // bark dust drifting
                case OilEffect.LifeSteal: return new Style { Rate = 13, SizeMin = 0.05f, SizeMax = 0.08f, LifeMin = 0.7f, LifeMax = 1.1f, Speed = 0f, Gravity = 0.4f, Light = 0.45f };   // blood drips
                case OilEffect.Stagger: return new Style { Rate = 14, SizeMin = 0.07f, SizeMax = 0.12f, LifeMin = 0.6f, LifeMax = 0.9f, Speed = 0.15f, Gravity = 0f, Light = 0.4f };
                case OilEffect.Guard: return new Style { Rate = 14, SizeMin = 0.05f, SizeMax = 0.08f, LifeMin = 1f, LifeMax = 1.4f, Speed = 0.08f, Gravity = 0f, Light = 0.5f };
                default: return new Style { Rate = 8, SizeMin = 0.05f, SizeMax = 0.08f, LifeMin = 0.6f, LifeMax = 1f, Speed = 0.05f, Gravity = 0f, Light = 0f };
            }
        }

        private bool _haveBounds;

        private void EnsureBounds()
        {
            if (_haveBounds) return;
            // a hand has no model of its own: a fist-sized box round the wrist bone (its holder is scaled so a unit is a metre)
            _bounds = Where == Slot.Hands ? new Bounds(Vector3.zero, Vector3.one * 0.11f) : ModelBounds();
            _haveBounds = true;
        }

        /// <summary>
        /// Which way this weapon's blade runs, in the model's own space (the axis it is longest along, pointing away from the hand that
        /// holds it, which is the model's origin), and how long it is in metres. For the application animation, which turns the weapon.
        /// </summary>
        internal void Blade(out Vector3 axis, out float length)
        {
            EnsureBounds();
            Vector3 size = _bounds.size, centre = _bounds.center;
            int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            axis = Vector3.zero;
            axis[longest] = centre[longest] < 0f ? -1f : 1f;
            length = size[longest] * Mathf.Abs(transform.lossyScale[longest]);
        }

        /// <summary>
        /// A hafted weapon's shaft: its true direction in the model's own space (pointing from the hand to the head), and how far (m) the
        /// weapon reaches ahead of the hand along it. From its longest mesh, tilt and all: an atgeir's spear is turned some 20 degrees
        /// inside its model, so the model's box (Blade) is not along the shaft, and slid along the box it left the hand. The hand, the
        /// model's origin, is often well up the haft (a battleaxe's and a sledge's in the middle, an atgeir's a metre from the butt).
        /// </summary>
        internal void Shaft(out Vector3 axis, out float ahead)
        {
            Blade(out axis, out _);
            ahead = 0f;
            MeshFilter best = null;
            Matrix4x4 bestToRoot = Matrix4x4.identity;
            int bestAxis = 0;
            float bestLength = 0f;
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                Matrix4x4 toRoot = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 size = filter.sharedMesh.bounds.size;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 side = Vector3.zero;
                    side[k] = size[k];
                    float length = toRoot.MultiplyVector(side).magnitude;
                    if (length > bestLength) { bestLength = length; best = filter; bestToRoot = toRoot; bestAxis = k; }
                }
            }
            if (best == null) { ahead = _bounds.size.magnitude * 0.5f; return; }
            Vector3 unit = Vector3.zero;
            unit[bestAxis] = 1f;
            Vector3 along = bestToRoot.MultiplyVector(unit).normalized;
            Bounds mesh = best.sharedMesh.bounds;
            float most = float.MinValue, least = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float at = Vector3.Dot(bestToRoot.MultiplyPoint3x4(corner), along);
                most = Mathf.Max(most, at);
                least = Mathf.Min(least, at);
            }
            if (-least > most) { along = -along; most = -least; }   // (the head is the end farther from the hand)
            axis = along;
            ahead = most * Mathf.Abs(transform.lossyScale.x);
        }

        /// <summary>How far (m) a hafted weapon reaches ahead of the hand holding it, along its shaft (see Shaft).</summary>
        internal float Ahead()
        {
            Shaft(out _, out float ahead);
            return ahead;
        }

        internal static GameObject RightHandModelOf(VisEquipment vis) => vis != null ? RightHandModel(vis) : null;

        /// <summary>The weapon model's size and middle in its own space, from its meshes (so a long sword and a short axe each get their own).</summary>
        private Bounds ModelBounds()
        {
            bool any = false;
            var result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                Matrix4x4 toRoot = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = toRoot.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(local, Vector3.zero); any = true; }
                    else result.Encapsulate(local);
                }
            }
            return any ? result : new Bounds(Vector3.zero, new Vector3(0.1f, 0.8f, 0.1f));
        }

        /// <summary>The game's own sparkle material (the item sparkles on a mead lying on the ground), so the effect looks like the game's.</summary>
        private static Material ParticleMaterial()
        {
            if (_particleMaterial != null) return _particleMaterial;
            ZNetScene scene = ZNetScene.instance;
            GameObject mead = scene != null ? scene.GetPrefab("MeadFrostResist") : null;
            Transform sparkles = mead != null ? mead.transform.Find("fx_ItemSparkles") : null;
            ParticleSystemRenderer renderer = sparkles != null ? sparkles.GetComponent<ParticleSystemRenderer>() : null;
            _particleMaterial = renderer != null ? renderer.sharedMaterial : null;
            return _particleMaterial;
        }

        internal static void Forget() => _particleMaterial = null;

        // ---- onto the models the game builds ---------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightHandModel = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> LeftHandModel = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightBackModel = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightBackItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> LeftBackModel = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftBackItemInstance");

        private static void Attach(GameObject model, Slot slot)
        {
            if (model == null || model.GetComponent<OilGlow>() != null) return;
            model.AddComponent<OilGlow>().Where = slot;
        }

        // (each of these builds a new model only when the item changes, and the model goes with the item)

        [HarmonyPatch(typeof(VisEquipment), "SetRightHandEquipped")]
        private static class VisEquipment_SetRightHandEquipped
        {
            private static void Postfix(VisEquipment __instance) => Attach(RightHandModel(__instance), Slot.RightHand);
        }

        [HarmonyPatch(typeof(VisEquipment), "SetLeftHandEquipped")]
        private static class VisEquipment_SetLeftHandEquipped
        {
            private static void Postfix(VisEquipment __instance) => Attach(LeftHandModel(__instance), Slot.LeftHand);
        }

        [HarmonyPatch(typeof(VisEquipment), "SetBackEquipped")]
        private static class VisEquipment_SetBackEquipped
        {
            private static void Postfix(VisEquipment __instance)
            {
                Attach(RightBackModel(__instance), Slot.RightBack);
                Attach(LeftBackModel(__instance), Slot.LeftBack);
            }
        }

        /// <summary>A dropped item that carries an oil (its data is in place by Start, whether it was dropped just now or loaded with the world).</summary>
        [HarmonyPatch(typeof(ItemDrop), "Start")]
        private static class ItemDrop_Start
        {
            private static void Postfix(ItemDrop __instance)
            {
                if (OilState.HasOil(__instance.m_itemData)) Attach(__instance.gameObject, Slot.Ground);
            }
        }
    }
}
