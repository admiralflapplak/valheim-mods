using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// The "keel" console command, for testing. Nothing in it changes the game (it only shows things on this player's screen), so it is
    /// open to everyone.
    /// </summary>
    internal static class Commands
    {
        private const string Command = "keel";
        private const string Usage = "keel status | keel next | keel play <eye|pacer|halfboat|ransnet> | keel probe [off] | keel pose | keel shaders [text] | keel material <prefab>";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        internal static void Register()
        {
            new Terminal.ConsoleCommand(Command, "Below the Keel: " + Usage, args => Run(args.Context, args.Args));
        }

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private static void Run(Terminal t, string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "status";
            Ship ship = Ship.GetLocalShip();

            switch (sub)
            {
                case "status":
                {
                    if (ship == null) { t.AddString("Not aboard a ship."); return; }
                    Vector3 p = ship.transform.position;
                    bool deep = Sea.IsDeep(p, out string why);
                    t.AddString($"Depth {Sea.Depth(p):0} m, biome {WorldGenerator.instance.GetBiome(p)}: " + (deep ? "omens may come" : "no omens (" + why + ")"));
                    t.AddString($"Ship id {Director.ShipId(ship):X}, {Visuals.Count} omen object(s) showing");
                    return;
                }
                case "next":
                {
                    if (ship == null) { t.AddString("Not aboard a ship."); return; }
                    ulong id = Director.ShipId(ship);
                    double now = ZNet.instance.GetTimeSeconds();
                    long slot = (long)(now / Director.SlotSeconds);
                    int found = 0;
                    for (long s = slot + 1; s < slot + 2000 && found < 3; s++)
                    {
                        if (!Director.Fires(id, s, out Omen omen, out _)) continue;
                        float minutes = (float)((s * Director.SlotSeconds - now) / 60.0);
                        t.AddString($"In {minutes:0.0} min: {omen.Name} (if the ship is in deep water then; night now: {EnvMan.IsNight()})");
                        found++;
                    }
                    if (found == 0) t.AddString("None in the next 1000 minutes.");
                    return;
                }
                case "play":
                {
                    if (ship == null) { t.AddString("Not aboard a ship."); return; }
                    Omen omen = a.Length > 2 ? Omens.Find(a[2]) : null;
                    if (omen == null) { t.AddString(Usage); return; }
                    omen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue));   // only on this screen
                    t.AddString("Playing " + omen.Name);
                    return;
                }
                case "probe":
                {
                    if (a.Length > 2 && a[2] == "off") { Probe.Clear(); t.AddString("Probe removed."); return; }
                    if (ship == null) { t.AddString("Not aboard a ship."); return; }
                    t.AddString(Probe.Show(ship));
                    return;
                }
                case "pose":
                {
                    if (ship == null) { t.AddString("Not aboard a ship."); return; }
                    string where = RansNetOmen.Pose(ship);
                    t.AddString($"One of Ran's drowned, above the water 7 m to starboard, for a minute (eyes open 11 s in): {where}");
                    return;
                }
                case "shaders":
                {
                    string filter = a.Length > 2 ? a[2] : "";
                    var names = new SortedSet<string>();
                    foreach (Shader sh in Resources.FindObjectsOfTypeAll<Shader>())
                        if (sh.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) names.Add($"{sh.name} ({(sh.isSupported ? "ok" : "unsupported")}, queue {sh.renderQueue})");
                    foreach (string n in names) t.AddString(n);
                    return;
                }
                case "material":
                {
                    GameObject prefab = a.Length > 2 && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(a[2]) : null;
                    if (prefab == null) { t.AddString("No such prefab."); return; }
                    foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            t.AddString($"{r.name}: {m.name} ({m.shader.name}) keywords [{string.Join(" ", m.shaderKeywords)}]");
                            for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                            {
                                string n = m.shader.GetPropertyName(i);
                                var type = m.shader.GetPropertyType(i);
                                string v = type == UnityEngine.Rendering.ShaderPropertyType.Color ? m.GetColor(n).ToString()
                                         : type == UnityEngine.Rendering.ShaderPropertyType.Texture ? (m.GetTexture(n) != null ? m.GetTexture(n).name : "-")
                                         : type == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(n).ToString()
                                         : m.GetFloat(n).ToString("0.###");
                                t.AddString($"  {n} = {v}");
                            }
                        }
                    return;
                }
                default:
                    t.AddString(Usage);
                    return;
            }
        }
    }
}
