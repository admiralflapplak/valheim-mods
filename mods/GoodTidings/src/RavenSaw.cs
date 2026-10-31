using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Muninn's reward for a right answer, "What the raven saw" (set with the user 2026-10-31): Huginn and Muninn fly over the world each
    /// day and tell Odin all they see, so he tells the players something worth finding. The server (the only game that knows every place in
    /// the world, and counts the world's right answers) picks: each right answer one place, the nearest not yet explored dungeon of this land
    /// or the next; every third right answer in the world a valuable one, the next boss's altar or a trader. Each player near him marks it
    /// on their own map (a saved pin) and sees the land round it; with nothing left to show, an idol of the land and its rare instead. An
    /// old riddle answered rightly brings "The raven's road" too.
    /// </summary>
    internal static class RavenSaw
    {
        internal const string AskRpc = "GoodTidings_RavenAsk", TellRpc = "GoodTidings_RavenSaw";
        private const float Reveal = 150f, NearMuninn = 40f;

        // what he can show, by kind (the prefab names of the world's places; a kind's places all share its name)
        private static readonly string[] BossAltars = { "Eikthyrnir", "GDKing", "Bonemass", "Dragonqueen", "GoblinKing", "Mistlands_DvergrBossEntrance1", "FaderLocation" };
        private static readonly string[][] Dungeons =   // (the next land's: Black Forest ... Ashlands)
        {
            new[] { "Crypt2", "Crypt3", "Crypt4" }, new[] { "SunkenCrypt4" }, new[] { "MountainCave02" }, new[] { "GoblinCamp2" },
            new[] { "Mistlands_DvergrTownEntrance1", "Mistlands_DvergrTownEntrance2" }, new[] { "CharredFortress" },
        };

        /// <summary>From Muninn's owner: someone answered rightly at <paramref name="home"/>; the server picks what he saw.</summary>
        internal static void Ask(Vector3 home, bool lore, bool test = false)
        {
            var pkg = new ZPackage();
            pkg.Write(home);
            pkg.Write(lore);
            pkg.Write(1);
            pkg.Write(test);
            ZRoutedRpc.instance?.InvokeRoutedRPC(AskRpc, pkg);   // (to the server)
        }

        /// <summary>On the server: the places, in the order they're worth (each kind's three nearest), sent to everyone.</summary>
        internal static void OnAsk(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            Vector3 home = pkg.ReadVector3();
            bool lore = pkg.ReadBool();
            int count = pkg.ReadInt();
            bool test = pkg.ReadBool();
            int land = Stock.NewestLand();
            // the world's right answers to him (testing doesn't count): every third, the valuable one first
            int answers = test ? 0 : Counted();
            bool valuable = test || answers % 3 == 0;
            var places = new List<(string prefab, Vector3 at)>();
            List<(string, Vector3)> Nearest(IEnumerable<string> kind, int take) =>
                ZoneSystem.instance.m_locationInstances.Values.Where(l => l.m_location != null && kind.Contains(l.m_location.m_prefabName))
                    .OrderBy(l => Utils.DistanceXZ(l.m_position, home)).Take(take).Select(l => (l.m_location.m_prefabName, l.m_position)).ToList();
            if (valuable)
            {
                // the valuable ones: the next boss's altar (the newest land's own boss), then the traders, nearest first
                if (land < BossAltars.Length) places.AddRange(Nearest(new[] { BossAltars[land] }, 3));
                var traders = new List<string> { "Vendor_BlackForest", "Hildir_camp" };
                if (land >= 2) traders.Add("BogWitch_Camp");
                places.AddRange(Nearest(traders, 6));
            }
            // the everyday ones, nearest first: this land's dungeon and the next one's
            var dungeons = new List<string>();
            if (land > 0 && land - 1 < Dungeons.Length) dungeons.AddRange(Dungeons[land - 1]);
            if (land < Dungeons.Length) dungeons.AddRange(Dungeons[land]);
            places.AddRange(Nearest(dungeons, 12));
            var tell = new ZPackage();
            tell.Write(home);
            tell.Write(lore);
            tell.Write(count);
            tell.Write(test);
            tell.Write(places.Count);
            foreach (var (prefab, at) in places) { tell.Write(prefab); tell.Write(at); }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, TellRpc, tell);
            Plugin.Log.LogInfo($"What the raven saw (right answer {answers}{(valuable ? ", the valuable one" : "")}): {places.Count} places told " +
                               $"({string.Join(", ", places.Select(p => p.prefab).Distinct())})");
        }

        private const string CountKey = "goodtidings_ravens";

        /// <summary>One more right answer to him in this world (a world key with the count), and how many now. Server.</summary>
        private static int Counted()
        {
            ZoneSystem zones = ZoneSystem.instance;
            int n = 0;
            if (zones.GetGlobalKey(CountKey, out string value)) int.TryParse(value, out n);
            n++;
            zones.RemoveGlobalKey(CountKey);
            zones.SetGlobalKey(CountKey + " " + n);
            return n;
        }

        private static readonly MethodInfo ExploreAt = AccessTools.Method(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) });
        private static readonly MethodInfo Explored = AccessTools.Method(typeof(Minimap), "IsExplored", new[] { typeof(Vector3) });
        private static readonly MethodInfo SimilarPin = AccessTools.Method(typeof(Minimap), "HaveSimilarPin");

        /// <summary>On every game: its player, if near him, marks the first places they haven't explored.</summary>
        internal static void OnTell(long sender, ZPackage pkg)
        {
            Vector3 home = pkg.ReadVector3();
            bool lore = pkg.ReadBool();
            int count = pkg.ReadInt();
            bool test = pkg.ReadBool();   // (testing: explored places count as news too)
            int n = pkg.ReadInt();
            var places = new List<(string prefab, Vector3 at)>();
            for (int i = 0; i < n; i++) places.Add((pkg.ReadString(), pkg.ReadVector3()));
            Player p = Player.m_localPlayer;
            Minimap map = Minimap.instance;
            if (p == null || map == null || Utils.DistanceXZ(p.transform.position, home) > NearMuninn) return;

            var shown = new List<string>();
            var kindsShown = new HashSet<string>();
            foreach (var (prefab, at) in places)
            {
                if (shown.Count >= count) break;
                string kind = Kind(prefab);
                if (kindsShown.Contains(kind)) continue;   // (one of each kind)
                if (!test && Explored != null && (bool)Explored.Invoke(map, new object[] { at })) continue;   // (already known: not news)
                ExploreAt?.Invoke(map, new object[] { at, Reveal });
                bool boss = BossAltars.Contains(prefab);
                string name = "$goodtidings_place_" + kind;
                Minimap.PinType type = boss ? Minimap.PinType.Boss : Minimap.PinType.Icon3;
                if (!(SimilarPin?.Invoke(map, new object[] { at, type, Localization.instance.Localize(name), true }) is bool similar && similar))
                    map.AddPin(at, type, Localization.instance.Localize(name), true, false);
                shown.Add(Localization.instance.Localize(name));
                kindsShown.Add(kind);
            }
            if (shown.Count > 0)
                p.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$goodtidings_raven_saw", string.Join(", ", shown)));
            else
                Gift(p);   // (nothing left to show this player: something he found instead)
            if (lore) RavensRoad.Give(p);
        }

        /// <summary>
        /// When there's nothing left on the map to show: a Battle or Protection Idol of the newest land (the Forge of Potential's) and the
        /// land's rare, into this player's own inventory (each player near him gets their own), or at their feet if it's full.
        /// </summary>
        internal static void Gift(Player p)
        {
            int land = Stock.NewestLand();
            var gifts = new List<(string prefab, int stack)> { ($"Upgrader{land}{(UnityEngine.Random.value < 0.5f ? "Weapon" : "Armor")}", 1) };
            if (Stock.Rare(new System.Random(UnityEngine.Random.Range(0, int.MaxValue))) is (string rare, int count)) gifts.Add((rare, count));
            var given = new List<string>();
            foreach (var (prefab, stack) in gifts)
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
                ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                if (p.GetInventory().CanAddItem(item, stack)) p.GetInventory().AddItem(item, stack);
                else ItemDrop.DropItem(drop.m_itemData, stack, p.transform.position + p.transform.forward + Vector3.up, Quaternion.identity);
                given.Add(Localization.instance.Localize(drop.m_itemData.m_shared.m_name));
            }
            p.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$goodtidings_raven_gift", string.Join(", ", given)));
            Plugin.Log.LogInfo($"Nothing left on the map to show: Muninn gives {string.Join(", ", given)}");
        }

        /// <summary>The name a place goes by on the map (a dungeon's variants share one).</summary>
        private static string Kind(string prefab)
        {
            foreach (string[] d in Dungeons) if (d.Contains(prefab)) return d[0];
            return prefab;
        }
    }

    /// <summary>
    /// "The raven's road": for a while (Raven's Road Minutes) portals take everything this player carries, metals and ores too, as if Odin
    /// had lifted his own rule for them. A status effect of our own (a feather's icon) and the game's one portal check.
    /// </summary>
    internal static class RavensRoad
    {
        internal const string Name = "GoodTidings_RavensRoad";
        private static SE_Stats _effect;

        internal static void Give(Player p)
        {
            if (_effect == null)
            {
                _effect = ScriptableObject.CreateInstance<SE_Stats>();
                _effect.name = Name;
                _effect.m_name = "$goodtidings_ravenroad";
                _effect.m_tooltip = "$goodtidings_ravenroad_tip";
                _effect.m_icon = ObjectDB.instance?.GetItemPrefab("Feathers")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            }
            _effect.m_ttl = Plugin.MuninnRoadMinutes.Value * 60f;
            p.GetSEMan().AddStatusEffect(_effect, resetTime: true);
            p.Message(MessageHud.MessageType.TopLeft, "$goodtidings_ravenroad_start");
        }

        internal static bool On(Player p) => p != null && p.GetSEMan().HaveStatusEffect(Name.GetStableHashCode());

        internal static void Unload()
        {
            if (_effect) UnityEngine.Object.Destroy(_effect);
            _effect = null;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsTeleportable))]
    internal static class Humanoid_IsTeleportable
    {
        private static void Postfix(Humanoid __instance, ref bool __result)
        {
            if (!__result && __instance is Player p && RavensRoad.On(p)) __result = true;
        }
    }
}
