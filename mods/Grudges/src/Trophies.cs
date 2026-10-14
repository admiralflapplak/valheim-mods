using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Named trophies. A nemesis that dies for good drops its species' trophy with its name and story written into
    /// the item's own saved data.
    ///
    /// Each named trophy gets its own copy of the item's shared settings (normally one copy is shared by every
    /// Greydwarf trophy in the world), so it can have its own name ("Murkspine's Head"), its own description (its
    /// story), and a stack size of 1 so it never merges with other trophies. That copy isn't saved by the game, so
    /// it's put back whenever the item is loaded: dropped, picked up, or read from an inventory.
    ///
    /// On an item stand, the story is copied onto the stand while it hangs there, and back onto the trophy when it
    /// comes down.
    /// </summary>
    internal static class Trophies
    {
        internal const string KeyName = "grudges_name";     // full name, e.g. "Murkspine, Who Planted a Viking"
        internal const string KeyShort = "grudges_short";   // e.g. "Murkspine"
        internal const string KeyStory = "grudges_story";
        private static readonly int StandName = "grudges_trophy_name".GetStableHashCode();
        private static readonly int StandShort = "grudges_trophy_short".GetStableHashCode();
        private static readonly int StandStory = "grudges_trophy_story".GetStableHashCode();
        private static bool _warnedNoCustomData;
        private static readonly HashSet<object> _ours = new HashSet<object>(); // shared-settings copies we made
        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        // ---- Dropping ------------------------------------------------------------------

        internal static void Drop(Character c, Nemesis n, string killerName)
        {
            if (!Plugin.TrophiesEnabled.Value || c == null || n == null) return;
            try
            {
                string itemName = Plugin.Looks.Get("trophies", GameInfo.Prefab(c));
                if (itemName.Length == 0) return;
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(itemName) : null;
                if (prefab == null) { Plugin.Log.LogWarning($"Trophies: no item called '{itemName}'."); return; }

                var go = UnityEngine.Object.Instantiate(prefab, c.transform.position + Vector3.up * 1.2f, Quaternion.identity);
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null) return;
                if (!Write(drop.m_itemData, n.FullName, n.Name, Rules.TrophyStory(n, killerName))) return;
                Save(drop);
                Plugin.Log.LogInfo($"Trophies: {n.FullName} dropped its trophy.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Trophies: drop failed ({e.Message})."); }
        }

        private static bool Write(object itemData, string name, string shortName, string story)
        {
            var data = CustomData(itemData, true);
            if (data == null) return false;
            data[KeyName] = name;
            data[KeyShort] = shortName;
            data[KeyStory] = story;
            Apply(itemData);
            return true;
        }

        private static void Save(ItemDrop drop)
        {
            try { Traverse.Create(drop).Method("Save").GetValue(); }
            catch (Exception e) { Plugin.DebugLog($"Trophies: couldn't save the name onto the item ({e.Message})."); }
        }

        /// <summary>The item's own key/value store (newer versions of the game have one). Null if this version doesn't.</summary>
        private static Dictionary<string, string> CustomData(object itemData, bool warn)
        {
            if (itemData == null) return null;
            Dictionary<string, string> d = null;
            try { d = Traverse.Create(itemData).Field("m_customData").GetValue<Dictionary<string, string>>(); } catch { }
            if (d == null && warn && !_warnedNoCustomData)
            {
                _warnedNoCustomData = true;
                Plugin.Log.LogWarning("Trophies: this version of the game can't store extra data on items, so trophies drop without their names.");
            }
            return d;
        }

        private static bool TryStory(object itemData, out string name, out string shortName, out string story)
        {
            name = shortName = story = null;
            var d = CustomData(itemData, false);
            if (d == null || !d.TryGetValue(KeyName, out name) || !d.TryGetValue(KeyStory, out story)) return false;
            if (!d.TryGetValue(KeyShort, out shortName) || string.IsNullOrEmpty(shortName)) shortName = name.Split(' ', ',')[0];
            return true;
        }

        /// <summary>
        /// Makes a named trophy its own item: own name, own description, never stacks. Safe to call any number of times.
        /// </summary>
        internal static void Apply(object itemData)
        {
            try
            {
                if (!TryStory(itemData, out var name, out var shortName, out var story)) return;
                var t = Traverse.Create(itemData).Field("m_shared");
                object shared = t.GetValue();
                if (shared == null || _ours.Contains(shared)) return;
                object copy = Clone.Invoke(shared, null);
                var c = Traverse.Create(copy);
                c.Field("m_name").SetValue($"{shortName}'s Head");
                c.Field("m_description").SetValue($"<color=#C8553D>{name}</color>\n{story}");
                c.Field("m_maxStackSize").SetValue(1);
                t.SetValue(copy);
                _ours.Add(copy);
            }
            catch (Exception e) { Plugin.DebugLog($"Trophies: couldn't make a trophy unique ({e.Message})."); }
        }

        // ---- Hooks -----------------------------------------------------------------------

        /// <summary>Hooks item loading and item stands. Done by hand: a mismatch only loses that part.</summary>
        internal static void Patch(Harmony harmony)
        {
            int hooked = 0;
            hooked += Hook(harmony, typeof(ItemDrop), "Awake", nameof(DropLoaded));
            hooked += Hook(harmony, typeof(ItemDrop), "Load", nameof(DropLoaded), optional: true);
            hooked += Hook(harmony, typeof(ItemDrop), "GetHoverText", nameof(DropHovered), prefix: true);
            hooked += Hook(harmony, typeof(Inventory), "Load", nameof(InventoryLoaded));
            hooked += Hook(harmony, typeof(ItemStand), "UseItem", nameof(StandUse));
            hooked += Hook(harmony, typeof(ItemStand), "GetHoverText", nameof(StandHover));
            hooked += Hook(harmony, typeof(ItemStand), "DropItem", nameof(StandDropBefore), prefix: true, optional: true);
            hooked += Hook(harmony, typeof(ItemStand), "DropItem", nameof(StandDropAfter), optional: true);
            hooked += Hook(harmony, typeof(ItemStand), "RPC_DropItem", nameof(StandDropBefore), prefix: true, optional: true);
            hooked += Hook(harmony, typeof(ItemStand), "RPC_DropItem", nameof(StandDropAfter), optional: true);
            Plugin.Log.LogInfo($"Trophies: {hooked} hooks in place.");
        }

        private static int Hook(Harmony harmony, Type t, string method, string patch, bool prefix = false, bool optional = false)
        {
            try
            {
                var m = AccessTools.Method(t, method);
                if (m == null)
                {
                    if (!optional) Plugin.Log.LogInfo($"Trophies: couldn't find {t.Name}.{method}; that part won't work.");
                    return 0;
                }
                var hm = new HarmonyMethod(typeof(Trophies), patch);
                if (prefix) harmony.Patch(m, prefix: hm); else harmony.Patch(m, postfix: hm);
                return 1;
            }
            catch (Exception e) { Plugin.Log.LogInfo($"Trophies: hook {t.Name}.{method} failed ({e.Message})."); return 0; }
        }

        private static void DropLoaded(ItemDrop __instance)
        {
            if (__instance != null) Apply(__instance.m_itemData);
        }

        private static void DropHovered(ItemDrop __instance)
        {
            if (__instance != null) Apply(__instance.m_itemData);
        }

        private static void InventoryLoaded(Inventory __instance)
        {
            try { foreach (var item in __instance.GetAllItems()) Apply(item); }
            catch (Exception e) { Plugin.DebugLog($"Trophies: inventory pass failed ({e.Message})."); }
        }

        // ---- Item stands ---------------------------------------------------------------

        private static ZDO StandZdo(ItemStand stand)
        {
            var nview = stand != null ? stand.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        private static bool StandHasItem(ItemStand stand)
        {
            try
            {
                var m = AccessTools.Method(typeof(ItemStand), "HaveAttachment");
                if (m != null) return (bool)m.Invoke(stand, null);
            }
            catch { }
            return true; // can't tell; assume it's still there
        }

        /// <summary>A trophy goes up on a stand: copy its story onto the stand (or clear an old one).</summary>
        private static void StandUse(ItemStand __instance, object[] __args, bool __result)
        {
            try
            {
                if (!__result) return;
                var item = __args?.OfType<ItemDrop.ItemData>().FirstOrDefault();
                var zdo = StandZdo(__instance);
                if (item == null || zdo == null) return;
                var nview = __instance.GetComponent<ZNetView>();
                if (TryStory(item, out var name, out var shortName, out var story))
                {
                    nview.ClaimOwnership();
                    zdo.Set(StandName, name);
                    zdo.Set(StandShort, shortName);
                    zdo.Set(StandStory, story);
                    Plugin.DebugLog($"Trophies: {name} hung on a stand.");
                }
                else if (zdo.GetString(StandName, "").Length > 0)
                {
                    nview.ClaimOwnership();
                    zdo.Set(StandName, "");
                }
            }
            catch (Exception e) { Plugin.DebugLog($"Trophies: stand update failed ({e.Message})."); }
        }

        private static void StandHover(ItemStand __instance, ref string __result)
        {
            try
            {
                var zdo = StandZdo(__instance);
                if (zdo == null) return;
                string name = zdo.GetString(StandName, "");
                if (name.Length == 0 || !StandHasItem(__instance)) return;
                __result += $"\n\n<color=#C8553D><b>{name}</b></color>\n<color=#BBBBBB>{Wrap(zdo.GetString(StandStory, ""), 48)}</color>";
            }
            catch { }
        }

        // Taking it down: remember the story, then write it onto the trophy that drops.
        private static string _downName, _downShort, _downStory;
        private static Vector3 _downAt;

        private static void StandDropBefore(ItemStand __instance)
        {
            _downName = null;
            var zdo = StandZdo(__instance);
            if (zdo == null) return;
            string name = zdo.GetString(StandName, "");
            if (name.Length == 0) return;
            _downName = name;
            _downShort = zdo.GetString(StandShort, "");
            _downStory = zdo.GetString(StandStory, "");
            _downAt = __instance.transform.position;
        }

        private static void StandDropAfter(ItemStand __instance)
        {
            if (_downName == null) return;
            string name = _downName, shortName = _downShort, story = _downStory;
            _downName = null;
            try
            {
                // The trophy that just fell: the nearest unnamed item near the stand.
                ItemDrop best = null;
                float bestDist = 3f;
                foreach (var d in UnityEngine.Object.FindObjectsByType<ItemDrop>(UnityEngine.FindObjectsSortMode.None))
                {
                    if (d == null || TryStory(d.m_itemData, out _, out _, out _)) continue;
                    float dist = Vector3.Distance(d.transform.position, _downAt);
                    if (dist < bestDist) { best = d; bestDist = dist; }
                }
                if (best != null && Write(best.m_itemData, name, shortName, story))
                {
                    Save(best);
                    var zdo = StandZdo(__instance);
                    if (zdo != null) zdo.Set(StandName, "");
                    Plugin.DebugLog($"Trophies: {name} taken down, story kept.");
                }
            }
            catch (Exception e) { Plugin.DebugLog($"Trophies: couldn't keep the story when taken down ({e.Message})."); }
        }

        private static string Wrap(string text, int width)
        {
            var sb = new StringBuilder();
            int col = 0;
            foreach (var word in (text ?? "").Split(' '))
            {
                if (col > 0 && col + word.Length + 1 > width) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word);
                col += word.Length;
            }
            return sb.ToString();
        }
    }
}
