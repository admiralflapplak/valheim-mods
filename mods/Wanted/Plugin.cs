using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Nick.Shared;   // (Shared/Translations.cs)
using ServerSettings = HeaveHo.Shared.ServerSettings;   // (mods/Shared/ServerSettings.cs, still in the namespace of the mod it was first copied for)

// The board is registered through PieceReload and the notice through ItemReload, so the mod can be reloaded in a world
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Wanted
{
    // publish.ps1 reads Guid, Name and Version from these three constants, so keep them in this exact form.
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nick.valheim.wanted";
        public const string Name = "Wanted";
        public const string Version = "0.3.4";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Building (the server's: the client pays when it builds, so it must charge the server's cost)
        internal static ConfigEntry<string> BoardCost;
        internal static ConfigEntry<string> BoardStation;

        // 2 - Notices (the server's: they decide what the world hands out)
        internal static ConfigEntry<int> FirstNotices;
        internal static ConfigEntry<float> DailyChance;
        internal static ConfigEntry<int> MaxCarried;

        // 3 - Rewards (the server's)
        internal static ConfigEntry<int> RewardPercent;
        internal static ConfigEntry<float> IdolChance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Translations.Load(this);
            Synced = new ServerSettings(Guid, Config, Logger);

            Config.SaveOnConfigSet = false;
            BoardCost = Synced.Bind("1 - Building", "Cost", "Wood:20, Resin:4, LeatherScraps:2",
                "What a Wanted board costs to build. " + Costs.Help);
            BoardStation = Synced.Bind("1 - Building", "Station", "piece_workbench",
                "The crafting station that must be near to build it. " + Costs.StationHelp);
            FirstNotices = Synced.Bind("2 - Notices", "Notices on a new board", 3,
                "How many notices a board has when it is built.", new AcceptableValueRange<int>(0, NoticeBoard.Slots));
            DailyChance = Synced.Bind("2 - Notices", "Daily chance of new notices", 50f,
                "Each new day, the chance (percent) that a board with room posts one to three new notices.", new AcceptableValueRange<float>(0f, 100f));
            MaxCarried = Synced.Bind("2 - Notices", "Notices a player can carry", 3,
                "How many notices one player can carry at a time.", new AcceptableValueRange<int>(1, 10));
            RewardPercent = Synced.Bind("3 - Rewards", "Reward percent", 100,
                "Pay as a percentage of the standard rate (50 = half, 200 = double). Notices already posted keep their pay.", new AcceptableValueRange<int>(10, 500));
            IdolChance = Synced.Bind("3 - Rewards", "Idol chance", 3f,
                "The chance (percent) that a notice also pays one of the Forge of Potential's idols, of its land's tier.", new AcceptableValueRange<float>(0f, 100f));
            Config.SaveOnConfigSet = true;
            Config.Save();

            BoardCost.SettingChanged += (s, e) => Board.ApplyCost();
            BoardStation.SettingChanged += (s, e) => Board.ApplyCost();

            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);

            // Loaded into a running world (a hot reload): the game's registers have already run.
            Board.Register(ZNetScene.instance);
            NoticeItem.Register(ZNetScene.instance, ObjectDB.instance);
            if (ZNetScene.instance != null)
                ItemReload.Repoint(name => name == NoticeItem.PrefabName ? NoticeItem.Prefab : null);

            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Commands.Update();
            Kills.Update();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Commands.Unregister();
            Kills.Unregister();
            Board.Unload();
            Board.Forget();
            NoticeItem.Forget();
            Synced?.Dispose();
            Translations.Unload();
            Instance = null;
            Synced = null;
        }
    }
}
