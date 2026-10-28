using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace GoodTidings
{
    /// <summary>
    /// The wandering trader's look, as chosen with the 'goodtidings look' previews (DESIGN.md): the rider, her riding pose, the golden Lox
    /// and its load. Shared by the previews and the real trader. Only models: each game builds it for itself, nothing is saved or sent.
    /// </summary>
    internal static class TraderLook
    {
        internal const string Ashlands = "DvergerSuitArbalest_Ashlands", Brown = "DvergerSuitArbalest";
        /// <summary>The golden Lox: the creature shader's hue, saturation and brightness shifts.</summary>
        internal static readonly Vector3 Golden = new Vector3(0.05f, 0.3f, 0.12f);
        internal const float RiderUp = 0.1f, RiderBack = 0.2f, HoodSize = 1.1f;

        /// <summary>The rider: a Dverger with Hildir's face, white braids, ochre Ashlands clothes; hooded (black hood and cape) or bare.</summary>
        internal static GameObject Rider(Transform parent, bool hooded, float hood = HoodSize)
        {
            var buckle = new RectInt(108, 0, 20, 20);
            var cape = new RectInt(64, 96, 64, 32);
            var ochre = new Cuts.Tint { Hue = 0.11f, Saturation = 0.55f, Value = 1.2f, Keep = { buckle } };
            if (hooded) ochre.Black.Add(cape); else ochre.Cut.Add(cape);
            GameObject body = DvergerLook.Body(parent);
            DvergerLook.Face(body, "Hildir", "Hildir");
            DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead, 1f, ochre);
            if (hooded) DvergerLook.Outfit(body, Brown, DvergerLook.Keep.HeadOnly, hood, new Cuts.Tint { Grey = true, Value = 0.35f });
            DvergerLook.Hair(body, "DvergerHairFemale");
            return body;
        }

        /// <summary>
        /// Puts the rider and the goods on a Lox's look (<paramref name="lox"/>: its root, holding the seat point "attachpoint"); what it
        /// makes goes in <paramref name="made"/> (to destroy with the Lox). She follows the seat point, a little up and back from it (as the
        /// game moves a riding player: parented to it she'd take on its stretched scale), riding (TraderAnim).
        /// </summary>
        internal static GameObject Mount(Transform lox, bool hooded, List<GameObject> made, Transform under = null)
        {
            Transform seat = Utils.FindChild(lox, "attachpoint");
            if (seat == null) { Plugin.Log.LogWarning("The Lox has no seat (attachpoint)"); return null; }
            var rider = new GameObject("goodtidings_rider");
            if (under) rider.transform.SetParent(under, false);
            var follow = rider.AddComponent<Follow>();
            follow.Target = seat;
            follow.Frame = lox;
            follow.Up = RiderUp;
            follow.Back = RiderBack;
            rider.transform.SetPositionAndRotation(seat.position, seat.rotation);
            made.Add(rider);
            GameObject body = Rider(rider.transform, hooded);
            int first = made.Count - 1;   // (the rider; the goods follow it in the list)
            Goods.Load(lox, seat, made, under);
            TraderAnim.Begin(body);
            JoinLods(lox, made.GetRange(first, made.Count - first));
            return body;
        }

        /// <summary>
        /// Puts the rider and the goods on the Lox's own detail levels, so they all go at the same distance as the Lox: each of them kept
        /// only its own levels before, and they dropped out one by one, each at its own distance. Each keeps its most detailed model
        /// (its other levels' models are switched off, its level switching removed) and that model joins every level of the Lox.
        /// </summary>
        internal static void JoinLods(Transform lox, IEnumerable<GameObject> parts)
        {
            LODGroup loxLods = lox.GetComponentInChildren<LODGroup>();
            var renderers = new List<Renderer>();
            foreach (GameObject part in parts)
            {
                if (part == null) continue;
                foreach (LODGroup group in part.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] levels = group.GetLODs();
                    if (levels.Length > 1)
                    {
                        var keep = new HashSet<Renderer>(levels[0].renderers.Where(r => r));
                        for (int i = 1; i < levels.Length; i++)
                            foreach (Renderer r in levels[i].renderers)
                                if (r && !keep.Contains(r)) r.enabled = false;
                    }
                    Object.Destroy(group);
                }
                renderers.AddRange(part.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.GetType().Name != "ParticleSystemRenderer"));
            }
            if (loxLods == null) return;
            LOD[] lods = loxLods.GetLODs();
            for (int i = 0; i < lods.Length; i++)
                lods[i].renderers = lods[i].renderers.Concat(renderers).ToArray();
            loxLods.SetLODs(lods);
        }

        /// <summary>Recolours a Lox's fur and skin (not its saddle) on copies of its materials.</summary>
        internal static void Tint(GameObject lox, Vector3 hsv)
        {
            foreach (Renderer r in lox.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Sadel") continue;
                Material[] mats = r.materials;   // (copies: the game's own Lox keep their colour)
                foreach (Material m in mats)
                {
                    m.SetFloat("_Hue", hsv.x);
                    m.SetFloat("_Saturation", hsv.y);
                    m.SetFloat("_Value", hsv.z);
                }
                r.materials = mats;
            }
        }

        /// <summary>Plays another character's animation clip on a look, looping (human rigs share animations). Destroy the graph when done.</summary>
        internal static bool Play(GameObject body, string fromPrefab, string clipName, out PlayableGraph graph)
        {
            graph = default;
            Animator from = ZNetScene.instance.GetPrefab(fromPrefab)?.GetComponentInChildren<Animator>(true);
            AnimationClip clip = from?.runtimeAnimatorController?.animationClips.FirstOrDefault(c => c.name == clipName);
            Animator anim = body.GetComponent<Animator>();
            if (clip == null || anim == null) return false;
            AnimationPlayableUtilities.PlayClip(anim, clip, out graph);
            return true;
        }
    }
}
