using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

namespace BelowTheKeel
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.belowthekeel";
        public const string Name = "BelowTheKeel";
        public const string Version = "0.9.4";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - When (the server's: everyone aboard one ship must work out the same schedule, see Director)
        internal static ConfigEntry<float> MinutesBetween;
        internal static ConfigEntry<float> MinutesApart;
        internal static ConfigEntry<float> MinDepth;
        internal static ConfigEntry<float> LandDistance;
        internal static ConfigEntry<float> NightFactor;
        internal static ConfigEntry<float> FailSafeMinutes;

        // 2 - Omens (each player's own: one who turns an omen off just doesn't see it when the rest of the crew does)
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Eye;
        internal static ConfigEntry<bool> Pacer;
        internal static ConfigEntry<bool> HalfBoat;
        internal static ConfigEntry<bool> RansNet;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            MinutesBetween = Synced.Bind("1 - When", "Minutes between omens", 20f,
                "On average, how many minutes of sailing in deep water pass between two omens.", new AcceptableValueRange<float>(2f, 120f));
            MinutesApart = Synced.Bind("1 - When", "Least minutes apart", 6f,
                "Two omens never come closer together than this.", new AcceptableValueRange<float>(1f, 60f));
            MinDepth = Synced.Bind("1 - When", "Least water depth", 25f,
                "Omens only come where the sea is at least this deep under the ship (metres), and only in the Ocean biome. Valheim's open " +
                "sea is about 30 m deep almost everywhere (keel depths): the distance from land below is what keeps them out at sea.",
                new AcceptableValueRange<float>(10f, 60f));
            LandDistance = Synced.Bind("1 - When", "Least distance from land", 250f,
                "Omens only come when no land is this close to the ship (metres).", new AcceptableValueRange<float>(0f, 1000f));
            NightFactor = Synced.Bind("1 - When", "More often at night", 2f,
                "How many times more often omens come at night.", new AcceptableValueRange<float>(1f, 5f));
            FailSafeMinutes = Synced.Bind("1 - When", "Fail-safe minutes", 40f,
                "After this many minutes on the open sea with no omen (by bad luck, or because they came due near land), the next chance out " +
                "at sea brings one. Counted by the ship (its owner's game), so the whole crew shares it. 0 turns it off.",
                new AcceptableValueRange<float>(0f, 240f));

            Enabled = Config.Bind("2 - Omens", "Enabled", true, "See omens at all.");
            Eye = Config.Bind("2 - Omens", "The Eye", true, "A vast eye opens far below the ship.");
            Pacer = Config.Bind("2 - Omens", "The Pacer", true, "Something long and dark keeps pace beneath the ship, and never seems to end.");
            HalfBoat = Config.Bind("2 - Omens", "The Half-Boat", true, "A drowned man rows half a boat past you in the fog.");
            RansNet = Config.Bind("2 - Omens", "Rans Net", true, "The drowned hang still in the water under the ship.");
            Config.SaveOnConfigSet = true;
            Config.Save();

            // 0.3.0 copied whole draugr (Character included) and broke them apart, which left destroyed characters in the game's list
            // and health bars stuck on screen. Only things already destroyed go: harmless when there are none.
            Character.GetAllCharacters().RemoveAll(c => c == null);
            RemoveDeadHuds();

            Commands.Register();
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        /// <summary>EnemyHud can't drop the bar of a character destroyed while it had one (its own clean-up skips a destroyed key).</summary>
        private static void RemoveDeadHuds()
        {
            if (EnemyHud.instance == null) return;
            var huds = (System.Collections.IDictionary)AccessTools.Field(typeof(EnemyHud), "m_huds").GetValue(EnemyHud.instance);
            var dead = new System.Collections.Generic.List<object>();
            foreach (System.Collections.DictionaryEntry e in huds)
                if ((Character)e.Key == null) dead.Add(e.Key);
            foreach (object key in dead)
            {
                var gui = Traverse.Create(huds[key]).Field("m_gui").GetValue<GameObject>();
                if (gui != null) Object.Destroy(gui);
                huds.Remove(key);
            }

            // Bars no entry owns any more (left on screen, never updated): everything under the root but the live ones and the templates.
            var keep = new System.Collections.Generic.HashSet<GameObject>
                { EnemyHud.instance.m_baseHud, EnemyHud.instance.m_baseHudBoss, EnemyHud.instance.m_baseHudPlayer, EnemyHud.instance.m_baseHudMount };
            foreach (object hud in huds.Values) keep.Add(Traverse.Create(hud).Field("m_gui").GetValue<GameObject>());
            int orphans = 0;
            foreach (Transform child in EnemyHud.instance.m_hudRoot.transform)
                if (!keep.Contains(child.gameObject)) { Object.Destroy(child.gameObject); orphans++; }

            if (dead.Count + orphans > 0) Log.LogInfo($"Removed {dead.Count + orphans} stuck health bar(s)");
        }

        private void Update()
        {
            Synced?.Update();
            Director.Update();
        }

        private void OnDestroy()
        {
            Visuals.ClearAll();
            Look.Forget();
            Director.Reset();
            Commands.Unregister();
            Synced?.Dispose();
            Instance = null;
            Synced = null;
        }
    }
}
