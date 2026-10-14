using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace HeaveHo
{
    /// <summary>
    /// Read by the (Valheim) Configuration Manager by field name: Order sorts entries within a section (higher first),
    /// IsAdvanced hides an entry unless "Advanced settings" is ticked in the F1 menu.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
#pragma warning disable 0649
        public int? Order;
        public bool? IsAdvanced;
        public bool? ReadOnly; // set by ServerSettings while the server's value applies
#pragma warning restore 0649
    }

    /// <summary>
    /// Config helpers: ordered/advanced entries, and migration of values saved under the pre-1.7 section and key
    /// names, so reorganizing the menu never resets anyone's tuning.
    /// </summary>
    internal static class Cfg
    {
        private static ConfigFile file;
        internal static HeaveHo.Shared.ServerSettings Synced;
        private static Dictionary<ConfigDefinition, string> orphans;
        private static int nextOrder = 100000;

        // Sections used before 1.7. Leftover values in them are removed once everything has been migrated.
        private static readonly HashSet<string> OldSections = new HashSet<string>
        {
            "0. Seats", "0. Controls", "1. Rowing", "2. Stamina", "3. Oars", "4. HUD", "5. Splashes", "9. Debug",
            "5. Oar rendering (experimental)"
        };

        internal static void Init(ConfigFile config, HeaveHo.Shared.ServerSettings synced)
        {
            file = config;
            Synced = synced;
            orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config)
                as Dictionary<ConfigDefinition, string>;
        }

        /// <param name="order">Explicit sort order (higher = first). Omit to keep the order entries are bound in.</param>
        /// <param name="local">True for settings that stay each player's own (what they see); everything else is decided by the server in multiplayer.</param>
        /// <param name="from">Old "section/key" this setting used to live under, to carry its value over.</param>
        internal static ConfigEntry<T> Bind<T>(string section, string key, T def, string desc,
            AcceptableValueBase range = null, bool advanced = false, int? order = null, string from = null, bool local = false)
        {
            var attrs = new ConfigurationManagerAttributes
            {
                Order = order ?? nextOrder--,
                IsAdvanced = advanced ? true : (bool?)null
            };
            if (!local && !desc.Contains("[Synced with Server]")) desc += " [Synced with Server]";
            ConfigEntry<T> entry = file.Bind(section, key, def, new ConfigDescription(desc, range, attrs));
            if (!local) Synced?.Add(entry);
            if (from != null) Migrate(entry, from);
            return entry;
        }

        private static void Migrate(ConfigEntryBase entry, string from)
        {
            if (orphans == null) return;
            int slash = from.IndexOf('/');
            var old = new ConfigDefinition(from.Substring(0, slash), from.Substring(slash + 1));
            if (!orphans.TryGetValue(old, out string value)) return;

            orphans.Remove(old);
            entry.SetSerializedValue(value);
            Plugin.Log.LogInfo($"Config: moved '{old.Section} / {old.Key}' to '{entry.Definition.Section} / {entry.Definition.Key}' ({value}).");
        }

        /// <summary>Drops values left in the old global sections (per-ship ones migrate when each ship loads).</summary>
        internal static void PurgeOldSections()
        {
            if (orphans != null)
            {
                var stale = new List<ConfigDefinition>();
                foreach (ConfigDefinition d in orphans.Keys)
                    if (OldSections.Contains(d.Section)) stale.Add(d);
                foreach (ConfigDefinition d in stale) orphans.Remove(d);
            }
            file.Save();
        }

        internal static void Save() => file.Save();
    }
}
