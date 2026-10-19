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
    /// Config helpers: ordered/advanced entries, and migration of values saved under an older section and key
    /// name, so reorganizing the menu never resets anyone's tuning (it carried the testers' settings across while the layout
    /// was being settled before the first release).
    /// </summary>
    internal static class Cfg
    {
        private static ConfigFile file;
        internal static HeaveHo.Shared.ServerSettings Synced;
        private static Dictionary<ConfigDefinition, string> orphans;
        private static int nextOrder = 100000;

        // Sections the pre-release builds used. Leftover values in them are removed once everything has been migrated.
        private static readonly HashSet<string> OldSections = new HashSet<string>
        {
            "0. Seats", "0. Controls", "1. Rowing", "2. Stamina", "3. Oars", "4. HUD", "5. Splashes", "9. Debug",
            "5. Oar rendering (experimental)",
            // the last pre-release layout: the splash settings joined the oars', and the HUD and the ships moved up a number
            "3. Oars (all ships)", "4. Splashes", "5. Rower HUD", "9. Advanced"
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

        /// <summary>Carries a value over from where it used to live: the first of the old "section/key" names (separated by ;) that has one.</summary>
        private static void Migrate(ConfigEntryBase entry, string from)
        {
            if (orphans == null) return;
            foreach (string name in from.Split(';'))
            {
                int slash = name.IndexOf('/');
                var old = new ConfigDefinition(name.Substring(0, slash), name.Substring(slash + 1));
                if (!orphans.TryGetValue(old, out string value)) continue;

                orphans.Remove(old);
                entry.SetSerializedValue(value);
                Plugin.Log.LogInfo($"Config: moved '{old.Section} / {old.Key}' to '{entry.Definition.Section} / {entry.Definition.Key}' ({value}).");
                return;
            }
        }

        /// <summary>Drops every value still left in a section that no setting lives in any more (a ship's old section, once it has been moved).</summary>
        internal static void PurgeSection(string section)
        {
            if (orphans == null) return;
            var stale = new List<ConfigDefinition>();
            foreach (ConfigDefinition d in orphans.Keys)
                if (d.Section == section) stale.Add(d);
            foreach (ConfigDefinition d in stale) orphans.Remove(d);
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
