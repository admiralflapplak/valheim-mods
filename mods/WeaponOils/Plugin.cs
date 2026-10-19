using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

namespace WeaponOils
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.weaponoils";
        public const string Name = "WeaponOils";
        public const string Version = "0.9.42";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Oils (the server's: they are gameplay)
        internal static ConfigEntry<float> DurationMinutes;
        internal static ConfigEntry<float> StrengthScale;
        internal static ConfigEntry<bool> NeedBossKill;
        internal static ConfigEntry<bool> AllowReplace;
        internal static ConfigEntry<float> ChillSeconds;
        internal static ConfigEntry<float> ChillSlow;

        // 2 - Oil press (the server's)
        internal static ConfigEntry<string> PressCost;
        internal static ConfigEntry<string> PressStation;
        internal static ConfigEntry<float> PressMinutes;
        internal static ConfigEntry<int> OilsPerBatch;

        // 3 - Oil recipes (the server's: what each oil base costs at the cauldron), by oil id
        internal static readonly Dictionary<string, ConfigEntry<string>> OilCosts = new Dictionary<string, ConfigEntry<string>>();

        // 4 - Looks (each player's own)
        internal static ConfigEntry<bool> ShowEffect;
        internal static ConfigEntry<float> GlowStrength;
        internal static ConfigEntry<bool> ApplyAnimation;
        internal static ConfigEntry<float> AnimationSpeed;
        internal static ConfigEntry<string> OilSound;
        internal static ConfigEntry<float> OilSoundVolume, OilSoundPitch;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            DurationMinutes = Synced.Bind("1 - Oils", "Duration (minutes)", 20f,
                "How long an oil lasts on a weapon, in world time.", new AcceptableValueRange<float>(1f, 120f));
            StrengthScale = Synced.Bind("1 - Oils", "Strength scale", 1f,
                "Scales every oil's added damage (1 = as designed, 0.5 = half).", new AcceptableValueRange<float>(0.1f, 3f));
            NeedBossKill = Synced.Bind("1 - Oils", "Boss kill needed", true,
                "An oil's recipe stays hidden until the boss before its biome has fallen. Off: only its ingredients limit it.");
            AllowReplace = Synced.Bind("1 - Oils", "Replace an oil already on the weapon", false,
                "Off: a weapon that already has an oil refuses a second one until the first wears off. On: using another oil replaces it (the old oil is lost, the new one's time starts afresh).");
            ChillSeconds = Synced.Bind("1 - Oils", "Rime chill (seconds)", 4f,
                "How long what a Rime-oiled weapon hits stays chilled, with the frost effect round it and slowed.", new AcceptableValueRange<float>(0.5f, 20f));
            ChillSlow = Synced.Bind("1 - Oils", "Rime slow", 0.35f,
                "How much a chilled creature is slowed at the start (0.35 = 35% slower), fading away over the chill's time. Bosses and creatures that resist frost are not slowed.",
                new AcceptableValueRange<float>(0.05f, 0.9f));

            PressCost = Synced.Bind("2 - Oil press", "Cost", "Wood:25, Stone:10, Resin:8, LeatherScraps:4",
                "What an oil press costs to build. " + Costs.Help);
            PressStation = Synced.Bind("2 - Oil press", "Station", "piece_workbench",
                "The crafting station that must be near to build it. " + Costs.StationHelp);
            PressMinutes = Synced.Bind("2 - Oil press", "Maturing time (minutes)", 20f,
                "How long an oil base matures in the press before it can be tapped. A press already standing keeps its time until its area reloads.",
                new AcceptableValueRange<float>(1f, 240f));

            OilsPerBatch = Synced.Bind("2 - Oil press", "Oils per base", 4,
                "How many finished oils one oil base makes in the press.", new AcceptableValueRange<int>(1, 12));

            foreach (OilDef def in OilDefs.All)
                OilCosts[def.Id] = Synced.Bind("3 - Oil recipes", Title(def.Id) + " base cost", def.BaseCost,
                    $"What one {def.Id} oil base costs at the cauldron (level {def.CauldronLevel}; {def.Biome}). " + Costs.Help);

            ShowEffect = Config.Bind("4 - Looks", "Show effect on the weapon", true, "Particles and a soft light in the oil's colour on an oiled weapon (yours, and other players' that have the mod).");
            GlowStrength = Config.Bind("4 - Looks", "Effect strength", 1f, new ConfigDescription("How strong the weapon effect is: the number and size of the particles and the brightness of the light (1 = as designed).", new AcceptableValueRange<float>(0.2f, 2f)));
            ApplyAnimation = Config.Bind("4 - Looks", "Application animation", true, "Your hands coat the weapon when you apply an oil.");
            AnimationSpeed = Config.Bind("4 - Looks", "Application animation speed", 1f,
                new ConfigDescription("How fast the oiling animation plays (1 = as designed, lower = slower and gentler, higher = quicker).", new AcceptableValueRange<float>(0.4f, 2f)));
            OilSound = Config.Bind("5 - Sounds (for tuning)", "Oiling sound", "sfx_torch_swing",
                new ConfigDescription("The game sound effect played as the left hand starts to slide along the blade (Claude Tools: `prefabs sfx_` lists them). Empty: no sound.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            OilSoundVolume = Config.Bind("5 - Sounds (for tuning)", "Oiling sound volume", 0.5f,
                new ConfigDescription("How loud it is, as a part of the sound's own volume (the game's volume settings apply as well).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            OilSoundPitch = Config.Bind("5 - Sounds (for tuning)", "Oiling sound pitch", 1.3f,
                new ConfigDescription("Higher or lower than the sound's own pitch (1 = as it is).", new AcceptableValueRange<float>(0.5f, 2f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            Config.SaveOnConfigSet = true;
            Config.Save();

            PressCost.SettingChanged += (s, e) => OilPress.ApplyCost();
            PressStation.SettingChanged += (s, e) => OilPress.ApplyCost();
            PressMinutes.SettingChanged += (s, e) => OilPress.ApplyDuration();
            OilsPerBatch.SettingChanged += (s, e) => OilPress.AddConversions();
            foreach (ConfigEntry<string> cost in OilCosts.Values) cost.SettingChanged += (s, e) => OilItems.ApplyCosts();

            ChillSeconds.SettingChanged += (s, e) => OilChill.ApplySettings();
            ChillSlow.SettingChanged += (s, e) => OilChill.ApplySettings();
            OilChill.Make();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            // Loaded into a running world (a hot reload): the game's registers have already run.
            OilItems.Register(ZNetScene.instance, ObjectDB.instance);
            if (ZNetScene.instance != null)
                ItemReload.Repoint(name => OilItems.Bases.Values.Concat(OilItems.Oils.Values).FirstOrDefault(o => o.name == name));
            OilPress.Register(ZNetScene.instance);
            StatusEffects.Register();

            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private static string Title(string id) => char.ToUpperInvariant(id[0]) + id.Substring(1);

        private void Update()
        {
            Synced?.Update();
            OilApply.Update();
            OilHands.Update();
            OilAnimation.Update();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            OilPress.Unload();
            OilPress.Forget();
            Content.RemoveAll(keepItems: ItemReload.KeepOnUnload);
            OilItems.Forget();
            StatusEffects.Unload();
            OilChill.Forget();
            OilApply.Reset();
            OilAnimation.Reset();
            Sound.Reset();
            OilGlow.Forget();
            OilModels.Forget();
            OilMark.Forget();
            OilHands.Forget();
            Synced?.Dispose();
            Translations.Unload();
            Instance = null;
            Synced = null;
        }
    }
}
