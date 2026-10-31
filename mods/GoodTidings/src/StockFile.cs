using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GoodTidings
{
    /// <summary>
    /// Dagny's stock file, BepInEx/config/nick.GoodTidings.stock.json (written with an example the first time): items added to a land's
    /// goods, meads or rares (or to the lines she always has), items taken out, and whether other mods' items are found by themselves
    /// (StockDiscovery). The built-in lists stay in Stock.cs; the file changes them. On a server the server's file applies to everyone
    /// (ServerSettings sends it), and an edit applies at once (watched).
    /// </summary>
    internal static class StockFile
    {
        internal const string SyncKey = "Stock";
        internal static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, Plugin.Guid + ".stock.json");

        internal sealed class Add
        {
            public string Item, Slot;
            public int Land, Stack, Price;
        }

        internal static List<Add> Adds = new List<Add>();
        internal static HashSet<string> Removed = new HashSet<string>();
        internal static bool FindOtherMods = true;

        internal static string OwnText()
        {
            try
            {
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Example);
                return File.ReadAllText(FilePath);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not read {FilePath}: {e.Message}"); return ""; }
        }

        /// <summary>The file that applies (the server's on a server that has the mod): read, and the stock built again from it.</summary>
        internal static void Use(string text)
        {
            var adds = new List<Add>();
            var removed = new HashSet<string>();
            bool find = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    JObject root = JObject.Parse(text);
                    find = (bool?)root["findOtherMods"] ?? true;
                    foreach (JToken t in (root["remove"] as JArray) ?? new JArray())
                        if ((string)t is string s && s.Length > 0) removed.Add(s);
                    foreach (JToken t in (root["add"] as JArray) ?? new JArray())
                    {
                        string item = (string)t["item"], slot = ((string)t["slot"] ?? "goods").ToLowerInvariant();
                        if (string.IsNullOrEmpty(item)) continue;
                        if (slot != "goods" && slot != "meads" && slot != "rares" && slot != "always")
                        {
                            Plugin.Log.LogWarning($"Stock file: \"{slot}\" isn't a slot (goods, meads, rares or always)");
                            continue;
                        }
                        int land = LandOf(t["land"]);
                        if (land < 0 && slot != "always") { Plugin.Log.LogWarning($"Stock file: {item} has no land she knows"); continue; }
                        adds.Add(new Add { Item = item, Slot = slot, Land = Math.Max(0, land),
                            Stack = Math.Max(1, (int?)t["stack"] ?? 1), Price = Math.Max(1, (int?)t["price"] ?? 100) });
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"The stock file has a mistake ({e.Message}): using the built-in stock only"); adds.Clear(); removed.Clear(); }
            Adds = adds;
            Removed = removed;
            FindOtherMods = find;
            Stock.Rebuild();
        }

        /// <summary>A land by its number (0 Meadows ... 7 Deep North) or its name; -1 if neither.</summary>
        private static int LandOf(JToken t)
        {
            if (t == null) return -1;
            if (t.Type == JTokenType.Integer) { int n = (int)t; return n >= 0 && n < Stock.LandNames.Length ? n : -1; }
            string s = ((string)t ?? "").Replace(" ", "").ToLowerInvariant();
            for (int i = 0; i < Stock.LandNames.Length; i++)
                if (Stock.LandNames[i].Replace(" ", "").ToLowerInvariant() == s) return i;
            return -1;
        }

        // ---- watching the file: an edit applies at once (on a server, everyone gets it again)

        private static FileSystemWatcher _watcher;
        private static volatile bool _changed;
        private static float _changedAt;

        internal static void Watch()
        {
            try
            {
                _watcher = new FileSystemWatcher(BepInEx.Paths.ConfigPath, Path.GetFileName(FilePath)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
                _watcher.Changed += (_, __) => _changed = true;
                _watcher.Created += (_, __) => _changed = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not watch the stock file: {e.Message}"); }
        }

        /// <summary>From the plugin's Update: a second after the last change, the file is read again (on the game's own thread).</summary>
        internal static void Tick()
        {
            if (!_changed) return;
            if (_changedAt <= 0f) { _changedAt = UnityEngine.Time.unscaledTime; return; }
            if (UnityEngine.Time.unscaledTime - _changedAt < 1f) return;
            _changed = false;
            _changedAt = 0f;
            Plugin.Synced.TextChanged(SyncKey);
        }

        internal static void Unwatch()
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        private const string Example =
@"{
  ""_help"": [
    ""Dagny's stock. Her built-in lists are in the mod; this file changes them. Other mods' items (things gathered or dropped in a land,"",
    ""meads fermented from known things) are found by themselves while findOtherMods is true: this file is for fine-tuning."",
    ""add: an item (its prefab name) into a land's goods, meads or rares, or into the lines she always has (slot always)."",
    ""  land: 0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountains, 4 Plains, 5 Mistlands, 6 Ashlands, 7 Deep North (number or name);"",
    ""  she sells a land's things once its boss is beaten (Meadows from the start). stack: how many in one buy; price: coins for it."",
    ""remove: prefab names she should never sell (built-in, added or found)."",
    ""On a server, the server's file applies to everyone. Changes apply as soon as the file is saved. Names the game doesn't have are skipped."",
    ""For example: \""add\"": [ { \""item\"": \""Resin\"", \""slot\"": \""goods\"", \""land\"": \""Meadows\"", \""stack\"": 10, \""price\"": 20 } ],"",
    ""\""remove\"": [ \""FishingRod\"", \""nick_oil_ember\"" ]. `goodtidings found` in the console lists the other mods' items she found.""
  ],
  ""findOtherMods"": true,
  ""add"": [
  ],
  ""remove"": [
  ]
}
";
    }
}
