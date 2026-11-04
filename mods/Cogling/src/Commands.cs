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
        private const string Usage = "cogling goto [x y z] | cogling home | cogling items | cogling tidy | cogling status | cogling slack [stare|look|bored|dance|sit|chair|watch|chat] | cogling emotes | cogling break [seconds]";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        internal static void Register() => new Terminal.ConsoleCommand(Command, "Cogling: " + Usage, args => Run(args.Context, args.Args));

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private static void Run(Terminal t, string[] a)
        {
            Player p = Player.m_localPlayer;
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (p == null || (sub != "goto" && sub != "home" && sub != "items" && sub != "tidy" && sub != "status" && sub != "slack" && sub != "emotes" && sub != "antenna" && sub != "break")) { t.AddString(Usage); return; }
            if (sub == "antenna")
            {
                // (testing: the nearest antenna's parts, where each is, and the lamp seen from the dish)
                Antenna near = Antenna.Nearest(p.transform.position, 60f);
                if (near == null) { t.AddString("No antenna near."); return; }
                var lines = new List<string>();
                void Dump(Transform tr, string path)
                {
                    path = path.Length == 0 ? tr.name : path + "/" + tr.name;
                    if (!path.Contains("AreaMarker"))
                        lines.Add($"{path} active {tr.gameObject.activeSelf} local {tr.localPosition:F3} rot {tr.localEulerAngles:F1} scale {tr.localScale:F2} world {tr.position:F3}"
                                  + $" [{string.Join(",", tr.GetComponents<Component>().Select(c => c.GetType().Name))}]");
                    foreach (Transform child in tr) Dump(child, path);
                }
                Dump(near.transform, "");
                Transform dish = near.transform.Find("New/cogling_turn/cogling_dish"), lamp = dish != null ? dish.Find("cogling_lamp") : null;
                if (dish != null && lamp != null) lines.Add($"lamp from the dish: {dish.InverseTransformPoint(lamp.position):F3}; lamp's parent {lamp.parent.name}");
                foreach (string l in lines) Plugin.Log.LogInfo("Cogling antenna: " + l);
                t.AddString($"{lines.Count} parts written to the log.");
                return;
            }
            if (sub == "emotes")
            {
                string list = string.Join(", ", ClipPlayer.GameClips().Select(c => $"{c.name} ({c.length:F1} s{(c.isLooping ? ", loops" : "")})").OrderBy(n => n));
                t.AddString(list);
                Plugin.Log.LogInfo("Cogling: player animations: " + list);
                return;
            }
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
            if (sub == "tidy")
            {
                List<Antenna> net = Antenna.NetworkAt(p.transform.position);
                if (net.Count == 0) { t.AddString("You're not in a Cogling Antenna's area."); return; }
                List<Container> chests = Chests.Usable(net);
                Tidy.Move move = Tidy.Next(chests, p.transform.position);
                string line = move == null
                    ? $"Nothing to tidy among the {chests.Count} chest(s) it may use (re-sort by groups {(Plugin.ResortGroups.Value ? "on" : "off")}): no item split across chests that could be gathered (a full chest can't take more)."
                    : $"Next: {move.Name} from the chest at {move.From.transform.position:F1} to the chest at {move.To.transform.position:F1} ({move.Why}).";
                t.AddString(line);
                Plugin.Log.LogInfo("Cogling tidy: " + line);
                return;
            }
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
            if (sub == "break")
            {
                // (testing: he breaks down as if wrecked, and the antenna rebuilds him in a few seconds)
                float seconds = 10f;
                if (a.Length > 2) float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
                if (Antenna.NetworkAt(cog.transform.position).Count == 0) { t.AddString("It's outside any antenna's area: nothing would rebuild it."); return; }
                Character body = cog.GetComponent<Character>();
                Rebuilds.QuickSeconds = Mathf.Max(1f, seconds);
                var blow = new HitData { m_point = body.GetCenterPoint(), m_dir = -body.transform.forward };
                blow.m_damage.m_blunt = body.GetMaxHealth() * 10f;
                body.Damage(blow);
                t.AddString($"Breaking it down: the antenna rebuilds it in {Rebuilds.QuickSeconds:F0} s (it checks every 5 s).");
                return;
            }
            if (sub == "slack")
            {
                string w = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                CoglingSlack.Activity? what = w == "stare" ? CoglingSlack.Activity.Stare : w == "look" ? CoglingSlack.Activity.LookAround
                    : w == "bored" ? CoglingSlack.Activity.Bored : w == "dance" ? CoglingSlack.Activity.Dance : w == "sit" ? CoglingSlack.Activity.Sit : w == "chair" ? CoglingSlack.Activity.Chair : w == "watch" ? CoglingSlack.Activity.Watch : w == "chat" ? CoglingSlack.Activity.Chat : (CoglingSlack.Activity?)null;
                bool took = cog.SlackNow(what);
                if (!took && what == CoglingSlack.Activity.Chair) { t.AddString($"No chair for it: {CoglingSlack.ChairWhy}."); return; }
                t.AddString(took ? $"Taking a break{(what.HasValue ? $" ({what})" : "")}." : "It can't take that break here (a chat needs another Cogling near, free; a chair, a free chair in its area; watching, you near).");
                return;
            }
            if (a.Length >= 5 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
                && float.TryParse(a[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                var at = new Vector3(x, y, z);
                t.AddString(cog.SendTo(at) ? $"Going to {at:F1}." : $"It finds no way to {at:F1}.");
                return;
            }
            Transform cam = GameCamera.instance?.transform;
            if (cam == null || !Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 60f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain")))
            {
                t.AddString("Look at a spot on the ground or a floor.");
                return;
            }
            t.AddString(cog.SendTo(hit.point) ? $"Going to {hit.point:F1}." : $"It finds no way to {hit.point:F1}.");
        }
    }
}
