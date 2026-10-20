using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WisplightArrows
{
    /// <summary>
    /// A glow that went into a creature rides along on it. The shooter's game (the projectile's owner, which runs its hits) spawns the glow
    /// and writes the creature's ZDOID on it; every game then keeps the glow over that creature until it times out, and leaves it where
    /// the creature was last seen if it dies or unloads. A glow with no creature written stays where it landed. The ZDOID is only kept for
    /// the glow's short, unsaved life (the glow isn't persistent), so world renumbering never meets it. It rides at the spot on the
    /// creature where the arrow's shaft came out of it, not inside it: held by the creature's bone nearest that spot (BoneKey, found by
    /// name on every game, so it moves with a rearing head or a swinging arm), at OffsetKey in that bone's own space. A creature with no
    /// skeleton to find holds it by its own position and facing.
    /// </summary>
    internal class GlowFollow : MonoBehaviour
    {
        public const string TargetKey = "nick_wisparrow_target";
        public const string OffsetKey = "nick_wisparrow_offset";
        public const string BoneKey = "nick_wisparrow_bone";
        private const float SendEvery = 0.5f;   // how often the owner moves the glow's saved position (it decides which area it belongs to)

        private ZNetView _nview;
        private Character _target;
        private Transform _holder;   // the bone (or the creature itself) the glow is held by
        private Vector3 _offset;
        private bool _looked;
        private float _sendTimer;

        private void Awake() => _nview = GetComponent<ZNetView>();

        private void LateUpdate()
        {
            if (_nview == null || !_nview.IsValid()) return;
            if (!_looked)
            {
                ZDOID id = _nview.GetZDO().GetZDOID(TargetKey);
                if (id == ZDOID.None) { enabled = false; return; }   // (one on the ground)
                GameObject found = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
                if (found == null) return;   // (not loaded here yet: try again next frame)
                _target = found.GetComponent<Character>();
                _offset = _nview.GetZDO().GetVec3(OffsetKey, Vector3.zero);
                if (_target != null) _holder = FindBone(_target, _nview.GetZDO().GetString(BoneKey, "")) ?? _target.transform;
                _looked = true;
            }
            if (_target == null || _holder == null) { enabled = false; return; }   // (dead or gone: stays where it was last)

            transform.position = _holder.TransformPoint(_offset);
            if (_nview.IsOwner() && (_sendTimer -= Time.deltaTime) <= 0f)
            {
                _sendTimer = SendEvery;
                _nview.GetZDO().SetPosition(transform.position);
            }
        }

        /// <summary>The creature's skeleton: every bone its skinned meshes move with.</summary>
        private static HashSet<Transform> Bones(Character c)
        {
            var bones = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer r in c.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (r.bones != null) foreach (Transform b in r.bones) if (b != null) bones.Add(b);
            return bones;
        }

        private static Transform NearestBone(Character c, Vector3 at)
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (Transform b in Bones(c))
            {
                float d = (b.position - at).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = b; }
            }
            return best;
        }

        /// <summary>A bone by name in the creature's skeleton (null if there is none of that name: held by the creature then).</summary>
        private static Transform FindBone(Character c, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform b in Bones(c)) if (b.name == name) return b;
            return null;
        }

        private static readonly AccessTools.FieldRef<Projectile, bool> DidHit = AccessTools.FieldRefAccess<Projectile, bool>("m_didHit");

        /// <summary>A wisplight arrow that hits a creature puts a glow on it (on the shooter's game: Projectile.FixedUpdate runs hits only
        /// for the projectile's owner). The projectile itself spawns nothing on creatures (m_spawnOnCharacters is off). Whatever it hit,
        /// every game puts out the arrow's own wisp (MistTrail.HitRpc): the glow takes over, and two would show otherwise.</summary>
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class Projectile_OnHit
        {
            private static void Postfix(Projectile __instance, Collider collider)
            {
                if (!__instance.name.StartsWith(ArrowItem.ProjectileName) || !DidHit(__instance)) return;
                ZNetView own = __instance.GetComponent<ZNetView>();
                __instance.m_spawnOnTtl = false;   // (stuck, its time runs on to m_stayTTL, and the game would leave a second glow when it ends)
                __instance.GetComponent<MistTrail>()?.PutOut();   // (here at once; the RPC is for the other games)
                if (own != null && own.IsValid()) own.InvokeRPC(ZNetView.Everybody, MistTrail.HitRpc);
                if (collider == null) return;
                Character hit = collider.GetComponentInParent<Character>();
                GameObject glow = ArrowItem.Glow;
                if (hit == null || glow == null) return;
                ZNetView nview = hit.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;
                Vector3 at = Shaft.OpenPoint(__instance);
                GameObject made = Instantiate(glow, at, Quaternion.identity);
                ZNetView madeView = made.GetComponent<ZNetView>();
                if (madeView == null || !madeView.IsValid()) return;
                madeView.GetZDO().Set(TargetKey, nview.GetZDO().m_uid);
                Transform bone = NearestBone(hit, at);
                madeView.GetZDO().Set(BoneKey, bone != null ? bone.name : "");
                madeView.GetZDO().Set(OffsetKey, (bone != null ? bone : hit.transform).InverseTransformPoint(at));
            }
        }
    }

    /// <summary>
    /// Where along a stuck arrow the glow goes: the tip, or, when the tip is buried (an arrow bores into the ground, a tree, a creature),
    /// just outside the spot where it went in, so the wisp is never swallowed. Found by a ray from a little way back along the arrow's
    /// flight (open air a moment ago) towards the tip: an overlap test can't tell, since the ground and most rocks are only a surface to
    /// physics, and a point deep under it touches nothing.
    /// </summary>
    internal static class Shaft
    {
        private const float Back = 2f;          // how far back along the flight the ray starts
        private const float Clearance = 0.1f;   // how far outside the surface the wisp sits

        private static int _mask;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly AccessTools.FieldRef<Projectile, Character> Owner = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");

        public static Vector3 OpenPoint(Projectile arrow)
        {
            if (_mask == 0)
                _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle",
                                          "character", "character_net", "character_ghost", "character_noenv", "hitbox");
            Transform t = arrow.transform;
            Vector3 tip = t.position, forward = t.forward, start = tip - forward * Back;
            int n = Physics.RaycastNonAlloc(start, forward, Hits, Back, _mask, QueryTriggerInteraction.Ignore);
            Character owner = Owner(arrow);
            float nearest = Back;
            for (int i = 0; i < n; i++)
            {
                Transform hit = Hits[i].collider.transform;
                if (Hits[i].distance >= nearest || hit.IsChildOf(t)) continue;   // (not the arrow's own parts)
                if (owner != null && hit.IsChildOf(owner.transform)) continue;   // (nor the archer, shooting from closer than the ray starts)
                nearest = Hits[i].distance;
            }
            if (nearest >= Back) return tip;   // (nothing between: the tip is in the open)
            return start + forward * Mathf.Max(0f, nearest - Clearance);
        }

        /// <summary>The game spawns the landing glow at the projectile plus m_spawnOffset: point the offset at the open bit of the shaft.</summary>
        [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
        private static class Projectile_SpawnOnHit
        {
            private static void Prefix(Projectile __instance)
            {
                if (!__instance.name.StartsWith(ArrowItem.ProjectileName)) return;
                __instance.m_spawnOffset = __instance.transform.InverseTransformDirection(OpenPoint(__instance) - __instance.transform.position);
            }
        }
    }
}
