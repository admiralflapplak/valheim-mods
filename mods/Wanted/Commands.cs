using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json;
using UnityEngine;

namespace Wanted
{
    /// <summary>
    /// The `wanted` command for Claude Tools (a request file, or `claude wanted ...` in the game's console): reads what the mod has made,
    /// for testing. Without Claude Tools nothing happens: it's found by its GUID and called by reflection, and registered again whenever a
    /// new copy of it loads. Reading only.
    /// </summary>
    internal static class Commands
    {
        private const string Owner = "Wanted";
        private static readonly string[] ToolGuids = { "com.quad.claudetools", "com.dhack.claudetools" };
        private const string Usage = "wanted boards [radius]: the Wanted boards near the player, which way they face, their notices and materials | wanted names: prefab names the contract lists use that this game doesn't have, and the world's stage | wanted found: what discovery adds beyond the built-in lists (other mods' creatures, things, food, metals) | wanted spawns <creature>: its entries in the world's spawn lists | wanted sample [count] [stage]: notices as a board would write them, nothing posted | wanted sketch <kind> <prefab> <count> [runes|wanted|none]: a notice's illustrated paper and bag icon as PNGs in claude/shots | wanted post [count] [nail|arrow|knife ...]: put notices up on the nearest board now (single player, host or devcommands)";

        private static BaseUnityPlugin _registeredWith;
        private static float _nextLook;

        /// <summary>From the plugin's Update: a dictionary lookup every 2 seconds, registering when a (new) Claude Tools is there.</summary>
        internal static void Update()
        {
            if (Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + 2f;
            BaseUnityPlugin tools = null;
            foreach (string guid in ToolGuids)
                if (Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) && info.Instance != null) { tools = info.Instance; break; }
            if (tools == null || ReferenceEquals(tools, _registeredWith)) return;
            _registeredWith = tools;   // (once per copy, even if it fails: no retrying every 2 seconds)
            try
            {
                var register = tools.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                if (register == null) return;
                // run is Func<string[], Action<JObject>, Action<string>, IEnumerator>; JObject comes from Claude Tools' own signature.
                Type json = register.GetParameters()[3].ParameterType.GetGenericArguments()[1].GetGenericArguments()[0];
                MethodInfo parse = json.GetMethod("Parse", new[] { typeof(string) });
                object run = typeof(Commands).GetMethod(nameof(Wrap), BindingFlags.NonPublic | BindingFlags.Static)
                                             .MakeGenericMethod(json).Invoke(null, new object[] { parse });
                register.Invoke(null, new object[] { Owner, "wanted", Usage, run });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't add the 'wanted' command to Claude Tools ({e.Message})."); }
        }

        private static Func<string[], Action<T>, Action<string>, IEnumerator> Wrap<T>(MethodInfo parse)
        {
            var toJson = (Func<string, T>)Delegate.CreateDelegate(typeof(Func<string, T>), parse);
            return (args, output, error) => Run(args, o => output(toJson(JsonConvert.SerializeObject(o))), error);
        }

        /// <summary>A hot reload: take the command away (it would call into this unloaded copy).</summary>
        internal static void Unregister()
        {
            try { _registeredWith?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Owner }); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't remove the 'wanted' command ({e.Message})."); }
            _registeredWith = null;
        }

        private static IEnumerator Run(string[] args, Action<object> output, Action<string> error)
        {
            try
            {
                string what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                if (what == "spawns" && args.Length > 2)   // how a creature is listed in the world's spawn lists (why discovery gave it its stage)
                {
                    var lines = new List<string>();
                    var lists = new List<SpawnSystemList>();
                    var zc = ZNetScene.instance.GetPrefab("_ZoneCtrl")?.GetComponent<SpawnSystem>();
                    if (zc != null) lists.AddRange(zc.m_spawnLists);
                    foreach (var l in lists) foreach (var d in l.m_spawners)
                        if (d.m_prefab != null && d.m_prefab.name == args[2])
                            lines.Add($"{l.name}: biome={d.m_biome} ({(int)d.m_biome}) area={d.m_biomeArea} enabled={d.m_enabled} key='{d.m_requiredGlobalKey}' event='{d.m_requiredPersistentEvent}' night={d.m_spawnAtNight}/{d.m_spawnAtDay}");
                    output(new Dictionary<string, object> { { "spawns", lines } });
                    return null;
                }
                if (what == "post")   // put notices up on the nearest board now (changes the world: single player, the host, or devcommands)
                {
                    if (!(ZNet.instance != null && ZNet.instance.IsServer()) && !(global::Console.instance != null && global::Console.instance.IsCheatsEnabled()))
                    { error("wanted post changes the world: single player, the host, or with devcommands."); return null; }
                    Player me = Player.m_localPlayer;
                    if (me == null) { error("Not in a world."); return null; }
                    NoticeBoard board = NoticeBoard.Loaded.Where(b => b != null).OrderBy(b => Vector3.Distance(b.transform.position, me.transform.position)).FirstOrDefault();
                    if (board == null || Vector3.Distance(board.transform.position, me.transform.position) > 20f) { error("No Wanted board within 20 m."); return null; }
                    int n = args.Length > 2 && int.TryParse(args[2], out int k) ? k : 1;
                    var pins = new List<Pin>();
                    for (int i = 3; i < args.Length; i++) if (Enum.TryParse(args[i], true, out Pin pin)) pins.Add(pin);
                    output(new Dictionary<string, object> { { "posted", board.PostNow(n, pins) } });
                    return null;
                }
                if (what == "sketch" && args.Length > 4)   // a notice's illustrated paper (and its bag icon) as PNGs, for looking at: nothing in the world changes
                {
                    if (!Enum.TryParse(args[2], true, out Kind kind)) { error("Kind: Kill, Gather, Trophy or Provisions."); return null; }
                    var c = new Contract { Kind = kind, Target = args[3], Count = int.TryParse(args[4], out int n) ? n : 1, Id = "sketch" };
                    Heading heading = args.Length > 5 && Enum.TryParse(args[5], true, out Heading h) ? h : Plugin.NoticeHeading.Value;
                    Material m = Sketch.For(c, ZNetScene.instance, heading);
                    Sprite icon = Sketch.IconFor(c, ZNetScene.instance, heading, false);
                    if (m == null || icon == null) { error($"No picture of {args[3]}."); return null; }
                    string dir = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots");
                    string paper = System.IO.Path.Combine(dir, $"wanted_sketch_{kind}_{args[3]}_{c.Count}_{heading}.png");
                    string bag = System.IO.Path.Combine(dir, $"wanted_icon_{kind}_{args[3]}_{heading}.png");
                    System.IO.File.WriteAllBytes(paper, ImageConversion.EncodeToPNG((Texture2D)m.mainTexture));
                    System.IO.File.WriteAllBytes(bag, ImageConversion.EncodeToPNG(icon.texture));
                    output(new Dictionary<string, object> { { "paper", paper }, { "icon", bag }, { "picture", Sketch.PictureInfo(c, ZNetScene.instance) } });
                    return null;
                }
                if (what == "trophies")   // each trophy notice's worth beside what its kills are worth, at the trophy's real drop chance
                {
                    var lines = new List<string>();
                    var all = ContractData.AllEntries().ToList();
                    foreach (Entry t in all.Where(e => e.Kind == Kind.Trophy))
                    {
                        Entry kill = null; float chance = 0f;
                        foreach (Entry k in all.Where(e => e.Kind == Kind.Kill))
                            foreach (CharacterDrop.Drop d in ZNetScene.instance.GetPrefab(k.Prefab)?.GetComponent<CharacterDrop>()?.m_drops ?? new List<CharacterDrop.Drop>())
                                if (d?.m_prefab != null && d.m_prefab.name == t.Prefab && kill == null) { kill = k; chance = d.m_chance; }
                        if (kill == null) { lines.Add($"t{t.Tier} {t.Prefab} @{t.Value}: no creature found"); continue; }
                        float perTrophy = kill.Value / Mathf.Max(0.01f, chance);   // what the kills to get one are worth
                        lines.Add($"t{t.Tier} {t.Prefab} @{t.Value} | {kill.Prefab} @{kill.Value} drops it {chance:P0} ({1f / Mathf.Max(0.01f, chance):0.#} kills) | " +
                                  $"those kills worth {perTrophy:0} | trophy pays {t.Value * Contracts.Premium(Kind.Trophy):0} vs bounty for those kills {perTrophy * Contracts.Premium(Kind.Kill):0}");
                    }
                    output(new Dictionary<string, object> { { "trophies", lines } });
                    return null;
                }
                if (what == "sample")   // notices as a board would write them (nothing posted): for checking what they ask and pay
                {
                    int count = args.Length > 2 && int.TryParse(args[2], out int n) ? n : 10;
                    int tier = args.Length > 3 && int.TryParse(args[3], out int t) ? t : -1;
                    var rnd = new System.Random();
                    var lines = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        Contract c = Contracts.Make(rnd, tier);
                        if (c != null) lines.Add($"t{c.Tier} {c.Kind} {c.Target} x{c.Count} -> {c.Reward}");
                    }
                    output(new Dictionary<string, object> { { "sample", lines } });
                    return null;
                }
                if (what == "found")
                {
                    Discovery.Forget();
                    var entries = ContractData.AllEntries().ToList();   // (finds them afresh)
                    output(new Dictionary<string, object>
                    {
                        { "found", Discovery.Found.OrderBy(e => e.Tier).ThenBy(e => e.Kind)
                            .Select(e => $"{e.Kind} t{e.Tier} {e.Prefab} x{e.Count} @{e.Value} ({Contracts.DisplayName(e.Prefab)})").ToList() },
                        { "foundRewards", Discovery.FoundPay.Select(p => $"t{p.Tier} {p.Prefab} @{p.Value} ({Contracts.DisplayName(p.Prefab)})").ToList() },
                        { "arrows", Discovery.Ammo.OrderBy(a => a.Tier).Select(a => $"t{a.Tier} {a.Prefab} @{a.Value} ({Contracts.DisplayName(a.Prefab)})").ToList() },
                        { "valuables", Discovery.Valuables.Select(v => $"{v.Prefab} @{v.Value} ({Contracts.DisplayName(v.Prefab)})").ToList() },
                        { "creaturesSkipped", Discovery.Skipped() },
                        { "builtIn", ContractData.Entries.Count }, { "total", entries.Count },
                    });
                    return null;
                }
                if (what == "names") { output(new Dictionary<string, object> { { "unknown", Contracts.Unknown() }, { "worldTier", Contracts.WorldTier() } }); return null; }
                if (what != "boards") { output(new Dictionary<string, object> { { "usage", Usage } }); return null; }
                Player p = Player.m_localPlayer;
                if (p == null) { error("Not in a world."); return null; }
                float radius = args.Length > 2 && float.TryParse(args[2], out float r) ? r : 30f;
                output(new Dictionary<string, object> { { "boards", Boards(p.transform.position, radius) } });
            }
            catch (Exception e) { error(e.Message); }
            return null;
        }

        private static List<object> Boards(Vector3 from, float radius)
        {
            var list = new List<object>();
            int hash = Board.PrefabName.GetStableHashCode();
            foreach (ZNetView view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))   // a test command, run by hand
            {
                if (view == null || view.GetZDO() == null || view.GetZDO().GetPrefab() != hash) continue;
                Transform t = view.transform;
                if (Vector3.Distance(t.position, from) > radius) continue;
                Transform look = t.Find("wanted_look");
                MeshRenderer plank = look != null ? look.Find("plank")?.GetComponent<MeshRenderer>() : null;
                MeshRenderer paper = look != null ? look.Find("notice_0/paper")?.GetComponent<MeshRenderer>() : null;
                list.Add(new Dictionary<string, object>
                {
                    { "at", new[] { Round(t.position.x), Round(t.position.y), Round(t.position.z) } },
                    { "yaw", Round(t.eulerAngles.y) },
                    { "chest", t.Find("wanted_chest") != null ? "moved (0.1.3 or later)" : "in the middle (older)" },
                    { "plank", Describe(plank) },
                    { "paper", Describe(paper) },
                    { "notices", view.GetZDO().GetString("Wanted.notices", "") },
                    { "day", view.GetZDO().GetInt("Wanted.day", -1) },
                });
            }
            return list;
        }

        private static object Describe(Renderer r)
        {
            if (r == null) return null;
            Material m = r.sharedMaterial;
            if (m == null) return "no material";
            return new Dictionary<string, object>
            {
                { "material", m.name },
                { "wobble", m.HasProperty("_ValueNoiseVertex") ? m.GetFloat("_ValueNoiseVertex") : -1f },
                { "wobbleKeyword", m.IsKeywordEnabled("_VALUENOISEVERTEX_ON") },
                { "ripple", m.HasProperty("_RippleDistance") ? m.GetFloat("_RippleDistance") : -1f },
            };
        }

        private static float Round(float v) => (float)Math.Round(v, 3);
    }
}
