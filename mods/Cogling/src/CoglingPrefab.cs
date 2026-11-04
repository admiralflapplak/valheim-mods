using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cogling
{
    /// <summary>
    /// The Cogling piece: a copy of the game's Dverger (a humanoid creature: its animator, movement, footsteps) made small, with no gear,
    /// no drops, no voice and no fight in it, wearing the robot (CoglingBody) instead of the Dverger's body. Like the game's own training
    /// dummy (TWIG, piece_TrainingDummy, also a creature) it carries a Piece: built with the hammer, and taken down with it, which the game
    /// does to a creature by killing it (Player.RemovePiece), and a creature's death gives its Piece's cost back (Character.OnDeath).
    /// Registered by name in ZNetScene and on the hammer's menu through PieceReload (safe to repeat, and to reload in a world).
    /// </summary>
    internal static class CoglingPrefab
    {
        internal const string Name = "cogling";
        private const float DvergerHeight = 1.45f;   // the Dverger's collider is 1.448 m tall (inspect Dverger)

        internal static GameObject Prefab;
        private static GameObject _holder;
        private static PieceTable _table;   // the build menu it was put on, kept so an unload can take it off again at the main menu

        internal static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab == null) Prefab = Build(scene);
            if (Prefab == null) return;

            GameObject hammer = scene.GetPrefab("Hammer");
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null) _table = table;
            PieceReload.ReplacePrefab(scene, Prefab, table != null ? table : _table);   // (replaces an older copy's by name: never missing)
            int swapped = PieceReload.Reattach(scene, Prefab);   // Coglings standing: this copy's CoglingBody
            if (swapped > 0) Plugin.Log.LogInfo($"Gave {swapped} standing Cogling(s) this copy's body");
            ApplyCost();
            if (Prefab.GetComponent<Piece>().m_resources.Length == 0 && _table != null)
            {
                _table.m_pieces.Remove(Prefab);   // never free to build: the Cost setting has a mistake and there was no good cost before it
                Plugin.Log.LogWarning("The Cogling has no cost (see the Cost setting): it isn't in the build menu");
            }
            Plugin.Synced.NeedsServer(Prefab);
        }

        /// <summary>The cost and station from the settings, onto the piece (a mistake in either keeps the last good one).</summary>
        internal static void ApplyCost()
        {
            Piece piece = Prefab != null ? Prefab.GetComponent<Piece>() : null;
            if (piece == null) return;
            Costs.Apply(piece, Plugin.Cost.Value, Plugin.Log);
            Costs.ApplyStation(piece, Plugin.Station.Value, Plugin.Log);
        }

        /// <summary>
        /// This copy unloads: the Cogling leaves the build menu but stays registered (PieceReload), so no area loading before the next copy
        /// registers loses its Coglings, and the prefab is never destroyed (the player's build menu and ghost may still hold it; a
        /// destroyed piece there threw every frame the hammer was out).
        /// </summary>
        internal static void Unregister()
        {
            PieceReload.UnloadPiece(Prefab, _table);
            _holder = Prefab = null;
            _table = null;
        }

        private static GameObject Build(ZNetScene scene)
        {
            GameObject dverger = scene.GetPrefab("Dverger");
            GameObject dummy = scene.GetPrefab("piece_TrainingDummy");
            if (dverger == null || dummy == null) { Plugin.Log.LogWarning("The game has no Dverger or training dummy to copy: no Cogling this session"); return null; }

            _holder = new GameObject("cogling_prefabs");
            _holder.SetActive(false);   // nothing under it wakes up: they are prefabs, not objects
            Object.DontDestroyOnLoad(_holder);

            GameObject cog = Object.Instantiate(dverger, _holder.transform);
            cog.name = Name;
            // the Dverger's body keeps its size (hidden: only its pose is used, and the game never scales a creature's root); the
            // capsule it walks and is hit with is made the robot's size
            float size = Plugin.Height.Value / DvergerHeight;
            if (cog.GetComponent<CapsuleCollider>() is CapsuleCollider capsule)
            {
                capsule.height *= size;
                capsule.radius = Mathf.Min(capsule.radius * size * 1.2f, capsule.height / 2f);
                capsule.center = new Vector3(0f, capsule.height / 2f, 0f);
            }
            if (cog.transform.Find("EyePos") is Transform eye) eye.localPosition *= size;

            // what the hammer sees: its removal ray only looks at piece layers (TWIG's whole body is on "piece"), not at "character",
            // where the Dverger's capsule is. A copy of the capsule on "piece_nonsolid" (it pushes nothing) lets the hammer find him.
            HammerTarget(cog);
            cog.AddComponent<CoglingVoice>();
            cog.AddComponent<CoglingTag>();

            // no loot, no talk
            foreach (Type t in new[] { typeof(CharacterDrop), typeof(NpcTalk) })
                if (cog.GetComponent(t) is Component c) Object.DestroyImmediate(c);

            var human = cog.GetComponent<Humanoid>();
            human.m_name = "$cogling_name";
            human.m_faction = Character.Faction.Players;   // (open question: monsters hunt this faction; see DESIGN.md)
            human.m_group = "";
            human.m_defaultItems = new GameObject[0];       // the Dverger's crossbow, suit and hair
            human.m_randomWeapon = new GameObject[0];
            human.m_randomArmor = new GameObject[0];
            human.m_randomShield = new GameObject[0];
            human.m_randomSets = new Humanoid.ItemSet[0];
            human.m_randomItems = new Humanoid.RandomItem[0];
            var dummyCharacter = dummy.GetComponent<Character>();
            if (dummyCharacter != null)
            {
                // wood, not blood: TWIG's hits, and TWIG's breaking sound when he's taken down or broken
                human.m_hitEffects = dummyCharacter.m_hitEffects;
                human.m_critHitEffects = dummyCharacter.m_critHitEffects;
                human.m_backstabHitEffects = dummyCharacter.m_backstabHitEffects;
                // (its sound only: TWIG's breaking dust, made for a full-size dummy, hid him and his falling apart in a cloud)
                human.m_deathEffects = new EffectList
                {
                    m_effectPrefabs = (dummyCharacter.m_deathEffects?.m_effectPrefabs ?? new EffectList.EffectData[0])
                        .Where(e => e?.m_prefab != null && e.m_prefab.GetComponentInChildren<ParticleSystem>(true) == null).ToArray(),
                };
            }
            else human.m_deathEffects = new EffectList();
            // ...and he falls apart (CoglingCollapse), instead of the Dverger's blood and ragdoll
            human.m_deathEffects = new EffectList
            {
                m_effectPrefabs = (human.m_deathEffects.m_effectPrefabs ?? new EffectList.EffectData[0])
                    .Concat(new[] { new EffectList.EffectData { m_prefab = CoglingCollapse.Prefab, m_enabled = true } }).ToArray(),
            };

            // his own brain (CoglingAI) in place of the Dverger's (MonsterAI: wandering and fighting)
            foreach (MonsterAI monster in cog.GetComponents<MonsterAI>()) Object.DestroyImmediate(monster);
            cog.AddComponent<CoglingAI>();

            // the piece: TWIG's settings (placement, effects), our name, cost and station
            Piece from = dummy.GetComponent<Piece>();
            Piece piece = cog.AddComponent<Piece>();
            foreach (FieldInfo f in typeof(Piece).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(piece, f.GetValue(from));
            piece.m_name = "$cogling_name";
            piece.m_description = "$cogling_desc";
            piece.m_resources = new Piece.Requirement[0];   // Costs.Apply fills it; a piece is never registered free (ApplyCost)
            piece.m_canBeRemoved = true;
            piece.m_usage = Piece.UsageTagFlags.Misc;   // (TWIG's are Misc and Defense)

            Transform visual = cog.transform.Find("Visual");

            // the Animation set setting's animations (default the mages', DvergrStaff_animator, not the rogues' crossbow ones)
            RuntimeAnimatorController staff = CoglingAnimations.Controller(scene);
            Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
            if (staff != null && animator != null) animator.runtimeAnimatorController = staff;
            else Plugin.Log.LogWarning("No animations to give the Cogling: he moves like a crossbow Dverger");
            if (visual != null) visual.gameObject.AddComponent<CoglingBody>();
            else Plugin.Log.LogWarning("The Dverger has no Visual: the Cogling shows as a Dverger");
            // the build-menu picture: the robot (put onto the picture's copy, which has no scripts to build him), in his idle
            try { piece.m_icon = Icon.Make(cog, new Vector3(0.35f, 0.15f, 1f), copy => CoglingBody.Picture(copy)) ?? piece.m_icon; }
            catch (Exception e) { Plugin.Log.LogWarning("Cogling icon: " + e.Message); }
            return cog;
        }

        /// <summary>The non-solid copy of the capsule the hammer finds him by (once; also for Coglings standing from before it existed).</summary>
        internal static void HammerTarget(GameObject cog)
        {
            if (cog.transform.Find("cogling_hammer_target") != null || !(cog.GetComponent<CapsuleCollider>() is CapsuleCollider body)) return;
            var target = new GameObject("cogling_hammer_target") { layer = LayerMask.NameToLayer("piece_nonsolid") };
            target.transform.SetParent(cog.transform, false);
            var c = target.AddComponent<CapsuleCollider>();
            c.center = body.center;
            c.radius = body.radius;
            c.height = body.height;
            c.direction = body.direction;
        }
    }
}
