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
        public const string Version = "0.6.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ServerSettings Synced;

        // 1 - Building (the server's: the client pays when it builds, so it must charge the server's cost)
        internal static ConfigEntry<string> BoardCost;
        internal static ConfigEntry<string> BoardStation;

        // 2 - Notices (the server's: they decide what the world hands out)
        internal static ConfigEntry<int> MaxNotices;
        internal static ConfigEntry<float> SecondChance;
        internal static ConfigEntry<float> ThirdChance;
        internal static ConfigEntry<float> BoardSpacing;
        internal static ConfigEntry<int> MaxCarried;

        // 3 - Rewards (the server's)
        internal static ConfigEntry<int> RewardPercent;
        internal static ConfigEntry<float> IdolChance;

        // 4 - Looks (the server's: decided when a notice is posted, so every player sees the same board)
        internal static ConfigEntry<float> ArrowChance;
        internal static ConfigEntry<float> KnifeChance;

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
            MaxNotices = Synced.Bind("2 - Notices", "Most notices on a board", 5,
                "The most notices a board shows at once (it has places for " + NoticeBoard.Slots + ").", new AcceptableValueRange<int>(1, NoticeBoard.Slots));
            SecondChance = Synced.Bind("2 - Notices", "Chance of a second notice", 75f,
                "Each dawn a board with room posts one new notice (a new board gets its first at the next dawn); this is the chance (percent) " +
                "it posts a second as well.", new AcceptableValueRange<float>(0f, 100f));
            ThirdChance = Synced.Bind("2 - Notices", "Chance of a third notice", 40f,
                "When it posted a second, the chance (percent) it posts a third as well. With the defaults: one 25%, two 45%, three 30% of dawns.",
                new AcceptableValueRange<float>(0f, 100f));
            BoardSpacing = Synced.Bind("1 - Building", "Least distance between boards", 50f,
                "A Wanted board can't be built closer than this (metres) to another one.", new AcceptableValueRange<float>(0f, 200f));
            MaxCarried = Synced.Bind("2 - Notices", "Notices a player can carry", 3,
                "How many notices one player can carry at a time.", new AcceptableValueRange<int>(1, 10));
            RewardPercent = Synced.Bind("3 - Rewards", "Reward percent", 100,
                "Pay as a percentage of the standard rate (50 = half, 200 = double). Notices already posted keep their pay.", new AcceptableValueRange<int>(10, 500));
            IdolChance = Synced.Bind("3 - Rewards", "Idol chance", 3f,
                "The chance (percent) that a notice also pays one of the Forge of Potential's idols, of its land's tier.", new AcceptableValueRange<float>(0f, 100f));
            ArrowChance = Synced.Bind("4 - Looks", "Arrow chance", 15f,
                "The chance (percent) that a new notice is pinned to the board with an arrow instead of a nail.", new AcceptableValueRange<float>(0f, 100f));
            KnifeChance = Synced.Bind("4 - Looks", "Knife chance", 10f,
                "The chance (percent) that a new notice is pinned to the board with a knife instead of a nail.", new AcceptableValueRange<float>(0f, 100f));
            Config.SaveOnConfigSet = true;
            Config.Save();

            BoardCost.SettingChanged += (s, e) => Board.ApplyCost();
            BoardStation.SettingChanged += (s, e) => Board.ApplyCost();
            Synced.AddText(ContractData.SyncKey, ContractData.OwnText, ContractData.Use);

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
