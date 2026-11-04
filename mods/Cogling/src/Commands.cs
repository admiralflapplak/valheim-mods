using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The `cogling` console command (a cheat: devcommands on): `cogling slack [what]` sends the nearest Cogling this game runs on a break,
    /// `cogling status` lists every Cogling's state and personality (hidden otherwise: it's only for testing).
    /// </summary>
    internal static class Commands
    {
        private const string Command = "cogling";
        private const string Usage = "cogling slack [stare|look|bored|dance|sit|chair|watch|chat|nap|exercise|tag|follow|animal|sneak|treasure|fiddle|shelter|inspect] | cogling status";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        internal static void Register() => new Terminal.ConsoleCommand(Command, "Cogling: " + Usage, args => Run(args.Context, args.Args), isCheat: true);

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private static void Run(Terminal t, string[] a)
        {
            Player p = Player.m_localPlayer;
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (p == null || (sub != "status" && sub != "slack")) { t.AddString(Usage); return; }
            if (sub == "status")
            {
                foreach (CoglingAI c in Object.FindObjectsByType<CoglingAI>(FindObjectsSortMode.None))   // (a command: once)
                {
                    string line = $"Cogling at {c.transform.position:F1}: {c.Status()}";
                    t.AddString(line);
                    Plugin.Log.LogInfo("Cogling status: " + line);
                }
                return;
            }
            CoglingAI cog = BaseAI.BaseAIInstances.OfType<CoglingAI>()
                .Where(c => c != null && c.GetComponent<ZNetView>()?.IsOwner() == true)
                .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
            if (cog == null) { t.AddString("No Cogling near that this game runs."); return; }
            if (sub == "slack")
            {
                string w = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                CoglingSlack.Activity? what = w == "stare" ? CoglingSlack.Activity.Stare : w == "look" ? CoglingSlack.Activity.LookAround
                    : w == "bored" ? CoglingSlack.Activity.Bored : w == "dance" ? CoglingSlack.Activity.Dance : w == "sit" ? CoglingSlack.Activity.Sit : w == "chair" ? CoglingSlack.Activity.Chair : w == "watch" ? CoglingSlack.Activity.Watch : w == "chat" ? CoglingSlack.Activity.Chat
                    : w == "nap" ? CoglingSlack.Activity.Nap : w == "exercise" ? CoglingSlack.Activity.Exercise
                    : w == "tag" ? CoglingSlack.Activity.Tag : w == "follow" ? CoglingSlack.Activity.Follow : w == "animal" ? CoglingSlack.Activity.Animal : w == "sneak" ? CoglingSlack.Activity.Sneak
                    : w == "treasure" ? CoglingSlack.Activity.Treasure : w == "fiddle" ? CoglingSlack.Activity.Fiddle : w == "shelter" ? CoglingSlack.Activity.Shelter : w == "inspect" ? CoglingSlack.Activity.Inspect : (CoglingSlack.Activity?)null;
                bool took = cog.SlackNow(what);
                if (!took && what == CoglingSlack.Activity.Chair) { t.AddString($"No chair for it: {CoglingSlack.ChairWhy}."); return; }
                t.AddString(took ? $"Taking a break{(what.HasValue ? $" ({what})" : "")}." : "It can't take that break here (a chat needs another Cogling near, free; a chair, a free chair in its area; watching, you near).");
                return;
            }
        }
    }
}
