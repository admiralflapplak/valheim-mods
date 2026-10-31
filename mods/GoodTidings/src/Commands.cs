using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace GoodTidings
{
    /// <summary>
    /// The `goodtidings` console command, for server owners: `visit [kind]` calls a visit now (the host, or with devcommands), telling
    /// plainly why one can't start; `found` lists Dagny's stock file and the other mods' items she found.
    /// </summary>
    internal static class Commands
    {
        private const string Command = "goodtidings";
        private const string Usage = "goodtidings visit [trader|bountiful|muninn|dverger|skald] | goodtidings found";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        internal static void Register()
        {
            new Terminal.ConsoleCommand(Command, "Good Tidings: " + Usage, args => Run(args.Context, args.Args));
        }

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private static void Run(Terminal t, string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            Player p = Player.m_localPlayer;
            switch (sub)
            {
                case "visit":
                {
                    if (ZNet.instance == null || !ZNet.instance.IsServer() && !global::Console.instance.IsCheatsEnabled())
                    {
                        t.AddString("Host or devcommands only.");
                        return;
                    }
                    string only = a.Length > 2 ? a[2].ToLowerInvariant() : null;
                    if (only != null && !Visits.All.Any(v => v.Id == only))
                    {
                        t.AddString($"No visit called {only}: {string.Join(", ", Visits.All.Select(v => v.Id))}.");
                        return;
                    }
                    if (Visits.TryStart(only)) { t.AddString("Visit started."); return; }
                    // why not, as plainly as can be told
                    if (p != null && p.GetBaseValue() < 3) t.AddString($"Not at a base here (base value {p.GetBaseValue()}, visits need 3): go home first.");
                    else if (TraderVisitor.Count > 0 && (only == null || only == "trader")) t.AddString("Dagny is still here: one at a time.");
                    else if (MuninnVisitor.Count > 0 && (only == null || only == "muninn")) t.AddString("Muninn is still here (he leaves a few seconds after an answer): try again in a moment.");
                    else if (SkaldVisitor.Count > 0 && (only == null || only == "skald")) t.AddString("The skald is still playing here.");
                    else if (DvergerVisitor.Count > 0 && (only == null || only == "dverger")) t.AddString("The wounded Dvergr is still here.");
                    else if (BountifulSeason.Running && (only == null || only == "bountiful")) t.AddString("A bountiful season is already going on.");
                    else if (only != null && !Visits.All.First(v => v.Id == only).Enabled) t.AddString($"{only} is switched off in the settings.");
                    else t.AddString("No visit could start here (no open ground near the base?).");
                    return;
                }
                case "found":
                {
                    // Dagny's stock as found again now: other mods' items, the stock file's adds and removes
                    Stock.Rebuild();
                    var found = Stock.FoundNow();
                    t.AddString($"Stock file: {StockFile.FilePath}{(Plugin.Synced.TextFromServer(StockFile.SyncKey) ? " (the server's applies)" : "")}; " +
                                $"{StockFile.Adds.Count} added, {StockFile.Removed.Count} removed, finding other mods' items {(StockFile.FindOtherMods ? "on" : "off")}.");
                    t.AddString("Always: " + string.Join(", ", Stock.AlwaysNames()));
                    t.AddString(found.Count == 0 ? "No other mods' items found." : $"{found.Count} other mods' items found:");
                    foreach (StockDiscovery.Found f in found)
                        t.AddString($"  {f.Item} ({Localization.instance.Localize(ObjectDB.instance.GetItemPrefab(f.Item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? "?")}): " +
                                    $"{f.Slot} of the {Stock.LandNames[f.Land]}, {f.Stack} for {f.Price}, {f.From}");
                    return;
                }
                default:
                    t.AddString(Usage);
                    return;
            }
        }
    }
}
