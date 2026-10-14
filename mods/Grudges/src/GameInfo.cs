using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>Small helpers for reading things out of the game, kept in one place.</summary>
    internal static class GameInfo
    {
        internal static readonly int KeyId = "grudges_id".GetStableHashCode();
        internal static readonly int KeyBaseLevel = "grudges_base".GetStableHashCode();
        internal static readonly int KeyGen = "grudges_gen".GetStableHashCode();
        internal static readonly int KeyBonus = "grudges_bonus".GetStableHashCode();

        /// <summary>Extra levels this body has because it's hunting in a tougher biome than its home.</summary>
        internal static int BonusOf(Character c)
        {
            var zdo = Zdo(c);
            return zdo != null && int.TryParse(zdo.GetString(KeyBonus, "0"), out int b) ? b : 0;
        }

        /// <summary>Which body generation this creature is (0 for bodies from before generations existed).</summary>
        internal static int GenOf(Character c)
        {
            var zdo = Zdo(c);
            return zdo != null && int.TryParse(zdo.GetString(KeyGen, "0"), out int g) ? g : 0;
        }

        internal static ZDO Zdo(Character c)
        {
            if (c == null) return null;
            var nview = c.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        internal static bool IsOwner(Character c)
        {
            var nview = c != null ? c.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() && nview.IsOwner();
        }

        /// <summary>The nemesis id written on this creature, or "".</summary>
        internal static string TagOf(Character c)
        {
            var zdo = Zdo(c);
            return zdo != null ? zdo.GetString(KeyId, "") : "";
        }

        /// <summary>The creature's network id as text, e.g. "123456:789". "" if it can't be read.</summary>
        internal static string ZdoKey(Character c)
        {
            try
            {
                var zdo = Zdo(c);
                if (zdo == null) return "";
                object uid = Traverse.Create(zdo).Field("m_uid").GetValue();
                string s = uid?.ToString() ?? "";
                return s.Contains(":") ? s : "";
            }
            catch { return ""; }
        }

        internal static string Prefab(Character c) => c != null ? Utils.GetPrefabName(c.gameObject) : "";

        /// <summary>The creature's normal display name, even if it's currently wearing a nemesis name.</summary>
        internal static string DisplayName(Character c)
        {
            if (c == null) return "";
            var body = c.GetComponent<NemesisBody>();
            string raw = body != null ? body.OriginalName : c.m_name;
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        private static bool _biomeWarned;

        internal static string Biome(Vector3 pos)
        {
            try
            {
                object b = Traverse.Create(typeof(Heightmap)).Method("FindBiome", new[] { typeof(Vector3) }, new object[] { pos }).GetValue();
                if (b != null) return b.ToString();
            }
            catch { /* try the other way */ }
            try
            {
                if (WorldGenerator.instance != null)
                {
                    object b = Traverse.Create(WorldGenerator.instance).Method("GetBiome", new[] { typeof(Vector3) }, new object[] { pos }).GetValue();
                    if (b != null) return b.ToString();
                }
            }
            catch { /* give up below */ }
            if (!_biomeWarned) { _biomeWarned = true; Plugin.Log.LogWarning("Couldn't work out biomes; every nemesis will count as 'Unknown'."); }
            return "Unknown";
        }

        internal static CreatureRef Describe(Character c, Vector3 from)
        {
            return new CreatureRef
            {
                Zdo = ZdoKey(c),
                Species = Prefab(c),
                SpeciesName = DisplayName(c),
                NemesisId = TagOf(c),
                Level = c.GetLevel(),
                Biome = Biome(c.transform.position),
                Distance = Vector3.Distance(from, c.transform.position)
            };
        }

        internal static bool IsAlerted(Character c)
        {
            var ai = c != null ? c.GetComponent<BaseAI>() : null;
            return ai != null && ai.IsAlerted();
        }

        /// <summary>Creatures (not players, not dead, not tamed) within a radius, closest first.</summary>
        internal static List<Character> CreaturesNear(Vector3 pos, float radius)
        {
            var found = new List<Character>();
            Character.GetCharactersInRange(pos, radius, found);
            found.RemoveAll(c => c == null || c is Player || c.IsDead() || c.IsTamed());
            found.Sort((a, b) => (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
            return found;
        }

        internal static Character NearestNemesis(Vector3 pos, float radius)
        {
            foreach (var c in CreaturesNear(pos, radius))
                if (c.GetComponent<NemesisBody>() != null) return c;
            return null;
        }

        internal static Character NearestEligible(Vector3 pos, float radius, bool includeNemeses)
        {
            foreach (var c in CreaturesNear(pos, radius))
            {
                if (!Plugin.IsEligible(Prefab(c)) || c.m_boss) continue;
                if (!includeNemeses && TagOf(c).Length > 0) continue;
                return c;
            }
            return null;
        }
    }
}
