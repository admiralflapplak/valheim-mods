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
        private const string Usage = "keel status | keel next | keel play <eye [classic] [width] [depth] | eye [classic] at <seconds> | eye [classic] show [at <seconds>]|pacer [side] [depth] [glow x]|halfboat [near [metres]]|ransnet [side <metres>]> | keel probe [off] | keel pose | keel hands [up] [out] [marks] | keel plate <name> <x> <y> <z> <yaw> <pitch> [fov] [size] [cut [omens]] | keel depths | keel shaders [text] | keel material <prefab>";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        internal static void Register()
        {
            new Terminal.ConsoleCommand(Command, "Below the Keel: " + Usage, args => Run(args.Context, args.Args));
        }

        internal static void Unregister() => TerminalCommands().Remove(Command);

        private static string Spread(List<float> depths)
        {
            if (depths.Count == 0) return "none";
            depths.Sort();
            float At(float q) => depths[Mathf.Clamp((int)(q * (depths.Count - 1)), 0, depths.Count - 1)];
            return $"{depths.Count} points, deepest {depths[depths.Count - 1]:0} m; 10% {At(0.1f):0}, half {At(0.5f):0}, 90% {At(0.9f):0} m";
        }

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
                    foreach (string w in Visuals.Where()) t.AddString("  " + w);
                    t.AddString($"Open sea since its last omen: {Director.OpenSeaSeconds(ship) / 60f:0.0} min (fail-safe at {Plugin.FailSafeMinutes.Value:0})");
                    t.AddString($"Weather {EnvMan.instance.GetCurrentEnvironment()?.m_name}, fog {RenderSettings.fogMode} density {RenderSettings.fogDensity:0.0000} (sight {Sea.SightDistance():0} m), night {EnvMan.IsNight()}");
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
                    EyeOmen.Classic = omen is EyeOmen && System.Array.IndexOf(a, "classic") > 2;
                    bool show = omen is EyeOmen && System.Array.IndexOf(a, "show") > 2;
                    var rest = new List<string>(a);
                    rest.Remove("fire");
                    rest.Remove("classic");
                    rest.Remove("show");
                    a = rest.ToArray();
                    if (show)
                    {
                        // The whole model, 12 m across, its eye's top 9 m above the water (the hide's dome round it), held open for a minute.
                        EyeOmen.Show = true;
                        int at = System.Array.IndexOf(a, "at");
                        EyeOmen.FreezeAt = at > 0 && a.Length > at + 1 ? float.Parse(a[at + 1], System.Globalization.CultureInfo.InvariantCulture) : 10f;
                        EyeOmen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue), 12f, -9f);
                        t.AddString("The eye, lifted above the water beside the ship for a minute");
                        return;
                    }
                    if (omen is EyeOmen && a.Length > 4 && a[3] == "at")
                    {
                        float at = float.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture);
                        EyeOmen.FreezeAt = at;
                        omen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue));
                        t.AddString($"Eye held at {at:0.##} s for a minute");
                        return;
                    }
                    if (omen is EyeOmen && a.Length > 3)
                    {
                        float width = float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w) ? w : EyeOmen.Width;
                        float depth = a.Length > 4 && float.TryParse(a[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : float.NaN;
                        EyeOmen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue), Mathf.Clamp(width, 2f, 120f), depth);
                        t.AddString($"Playing eye, {width:0} m across, top {(!float.IsNaN(depth) ? depth.ToString("0.#") + " m" : "6-7 m")} down");
                        return;
                    }
                    if (omen is PacerOmen && a.Length > 3)
                    {
                        float side = float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture);
                        float depth = a.Length > 4 && a[4] != "glow" ? float.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
                        int g = System.Array.IndexOf(a, "glow");
                        PacerOmen.Glow = g > 0 && a.Length > g + 1 ? float.Parse(a[g + 1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                        try { PacerOmen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue), side, depth); }
                        finally { PacerOmen.Glow = 1f; }
                        t.AddString($"Playing pacer {side:0} m to the side" + (float.IsNaN(depth) ? "" : $", its middle {depth:0.#} m down"));
                        return;
                    }
                    if (omen is RansNetOmen && a.Length > 4 && a[3] == "side")
                    {
                        RansNetOmen.Side = float.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture);
                        omen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue));
                        t.AddString($"Playing ransnet {a[4]} m to the side");
                        return;
                    }
                    if (omen is HalfBoatOmen && a.Length > 3 && a[3] == "near")
                    {
                        float off = a.Length > 4 && float.TryParse(a[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float o) ? o : 12f;
                        HalfBoatOmen.Play(ship, (uint)Random.Range(int.MinValue, int.MaxValue), lateral: Mathf.Sign(off) * Mathf.Clamp(Mathf.Abs(off), 4f, 200f));   // (negative: to port)
                        t.AddString($"Half-boat holding {Mathf.Abs(off):0} m to {(off < 0f ? "port" : "starboard")} for a minute; ship at {ship.transform.position.x:0.0} {ship.transform.position.z:0.0}, heading {ship.transform.eulerAngles.y:0}");
                        return;
                    }
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
                case "hands":
                {
                    if (Player.m_localPlayer == null || ship == null) { t.AddString("Stand aboard a ship, at its side, facing over it."); return; }
                    Hands.Markers = System.Array.IndexOf(a, "marks") > 1;
                    var nums = new List<float>();
                    foreach (string arg in a)
                        if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r)) nums.Add(r);
                    t.AddString(Hands.Show(Player.m_localPlayer, ship, nums.Count > 0 ? nums[0] : 0f, nums.Count > 1 ? nums[1] : 0f));
                    return;
                }
                case "plate":
                {
                    // keel plate <name> <x> <y> <z> <yaw> <pitch> [fov] [size] [cut]: a still from a camera placed from the player.
                    if (Player.m_localPlayer == null || ship == null || a.Length < 8) { t.AddString("keel plate <name> <x> <y> <z> <yaw> <pitch> [fov=60] [size=2048] [cut]"); return; }
                    float F(int i, float d) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
                    bool cut = System.Array.IndexOf(a, "cut") > 0;
                    t.AddString(Plate.Render(Player.m_localPlayer, ship, a[2], new Vector3(F(3, 0f), F(4, 1.6f), F(5, 0f)), F(6, 0f), F(7, 0f),
                        F(8, 60f), (int)F(9, 2048f), cut, System.Array.IndexOf(a, "omens") > 0));
                    return;
                }
                case "pose":
                {
                    if (ship == null) { t.AddString("Not aboard a ship."); return; }
                    string where = RansNetOmen.Pose(ship);
                    t.AddString($"One of Ran's drowned, above the water 7 m to starboard, for a minute (eyes open 11 s in): {where}");
                    return;
                }
                case "depths":
                {
                    // The whole world from the generator: how deep its ocean is, and how deep where omens may come (no land near).
                    var all = new List<float>();
                    var open = new List<float>();
                    float saved = Plugin.MinDepth.Value;
                    Plugin.MinDepth.Value = -1000f;   // (for the moment: so IsDeep checks only the biome and the land round about)
                    try
                    {
                        for (float x = -10000f; x <= 10000f; x += 200f)
                            for (float z = -10000f; z <= 10000f; z += 200f)
                            {
                                if (x * x + z * z > 10000f * 10000f) continue;
                                var p = new Vector3(x, 0f, z);
                                if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Ocean) continue;
                                float d = Sea.Depth(p);
                                all.Add(d);
                                if (Sea.IsDeep(p, out _)) open.Add(d);
                            }
                    }
                    finally { Plugin.MinDepth.Value = saved; }
                    t.AddString("Ocean depth (every 200 m over the world): " + Spread(all));
                    t.AddString($"Away from land ({Plugin.LandDistance.Value:0} m): " + Spread(open));
                    foreach (float min in new[] { 20f, 25f, 30f, 35f, 40f, 50f, 60f })
                        t.AddString($"  at least {min:0} m deep: {open.FindAll(d => d >= min).Count * 100f / Mathf.Max(1, open.Count):0}% of it");
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
