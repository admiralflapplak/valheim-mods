using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// The `cogling` console command, for testing his walking: `cogling goto` sends the nearest Cogling to where the player looks (then
    /// he comes back home), `cogling home` sends him home. It only moves a Cogling this game runs (in single player, all of them).
    /// </summary>
    internal static class Commands
    {
        private const string Command = "cogling";
        private const string Usage = "cogling goto [x y z] | cogling home | cogling items";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        internal static void Register() => new Terminal.ConsoleCommand(Command, "Cogling: " + Usage, args => Run(args.Context, args.Args));

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private static void Run(Terminal t, string[] a)
        {
            Player p = Player.m_localPlayer;
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (p == null || (sub != "goto" && sub != "home" && sub != "items")) { t.AddString(Usage); return; }
            if (sub == "items")
            {
                List<Antenna> network = Antenna.NetworkAt(p.transform.position);
                if (network.Count == 0) { t.AddString("You're not in a Cogling Antenna's area."); return; }
                foreach (string line in FloorItems.Explain(network, p.transform.position)) { t.AddString(line); Plugin.Log.LogInfo("Cogling items: " + line); }
                return;
            }
            CoglingAI cog = BaseAI.BaseAIInstances.OfType<CoglingAI>()
                .Where(c => c != null && c.GetComponent<ZNetView>()?.IsOwner() == true)
                .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
            if (cog == null) { t.AddString("No Cogling near that this game runs."); return; }
            if (sub == "home") { cog.GoHome(); t.AddString("Going home."); return; }
            if (a.Length >= 5 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
                && float.TryParse(a[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                var at = new Vector3(x, y, z);
                t.AddString(cog.SendTo(at) ? $"Going to {at:F1}." : $"He finds no way to {at:F1}.");
                return;
            }
            Transform cam = GameCamera.instance?.transform;
            if (cam == null || !Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 60f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain")))
            {
                t.AddString("Look at a spot on the ground or a floor.");
                return;
            }
            t.AddString(cog.SendTo(hit.point) ? $"Going to {hit.point:F1}." : $"He finds no way to {hit.point:F1}.");
        }
    }
}
