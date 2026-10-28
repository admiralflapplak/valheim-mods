using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace GoodTidings
{
    /// <summary>
    /// The "goodtidings" console command, for testing. "look" only puts models on this player's screen (nothing networked or saved),
    /// so it is open to everyone; "visit" starts a visit and needs the host or devcommands.
    /// </summary>
    internal static class Commands
    {
        private const string Command = "goodtidings";
        private const string Usage = "goodtidings look [trader [hood size]|ride [up back [up2 back2]]|loxcolours|loxcolour h s v|ridecolours|beard|hildir|face|hood [sizes]|colours|clear] | goodtidings goods [off] | goodtidings bones | goodtidings mesh <item> | goodtidings tex grid <n>|cut <suit> x0 y0 x1 y1|clear | goodtidings anims | goodtidings pose [rest|<clip> [0-1]] | goodtidings visit [trader [here]]";

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> TerminalCommands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        private static readonly List<GameObject> Shown = new List<GameObject>();
        private static string[] _lastLook;

        internal static void Register()
        {
            new Terminal.ConsoleCommand(Command, "Good Tidings: " + Usage, args => Run(args.Context, args.Args));
        }

        internal static void Unregister()
        {
            TerminalCommands().Remove(Command);
            ClearLooks();
            Cuts.Clear();
            SaddleBag.Clear();
        }

        private static void Run(Terminal t, string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            Player p = Player.m_localPlayer;
            switch (sub)
            {
                case "look":
                    _lastLook = a;
                    ClearLooks();
                    if (a.Length > 2 && a[2] == "clear") { t.AddString("Cleared."); return; }
                    if (p == null) { t.AddString("Not in a world."); return; }
                    if (a.Length > 2 && a[2] == "loxcolours")
                    {
                        // Saddled Lox in a row, recoloured (hue, saturation, brightness shifts)
                        var looks = new (string name, Vector3 hsv)[]
                        {
                            ("as it is", Vector3.zero), ("dark", new Vector3(0f, -0.1f, -0.35f)), ("pale", new Vector3(0f, -0.85f, 0.3f)),
                            ("grey", new Vector3(0f, -1f, -0.05f)), ("red", new Vector3(-0.05f, 0.35f, -0.08f)), ("golden", new Vector3(0.05f, 0.3f, 0.12f)),
                        };
                        for (int k = 0; k < looks.Length; k++) DvergerLook.Lox(Place(p, k, looks.Length, 11f, 5.2f), looks[k].hsv);
                        t.AddString("Lox in front of you, left to right: " + string.Join(", ", looks.Select(x => $"{x.name} ({x.hsv.x}, {x.hsv.y}, {x.hsv.z})")) +
                                    ". 'goodtidings look loxcolour <hue> <saturation> <brightness>' for one of your own.");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "loxcolour")
                    {
                        float H(int i) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
                        DvergerLook.Lox(Place(p, 0, 1, 8f), new Vector3(H(3), H(4), H(5)));
                        t.AddString($"A Lox in front of you: hue {H(3)}, saturation {H(4)}, brightness {H(5)}.");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "ridecolours")
                    {
                        // both versions on a red and on a golden Lox
                        var red = new Vector3(-0.05f, 0.35f, -0.08f);
                        var gold = TraderLook.Golden;
                        Ride(Place(p, 0, 4, 10f, 5.2f), true, red, 0.1f, 0.2f);
                        Ride(Place(p, 1, 4, 10f, 5.2f), false, red, 0.1f, 0.2f);
                        Ride(Place(p, 2, 4, 10f, 5.2f), true, gold, 0.1f, 0.2f);
                        Ride(Place(p, 3, 4, 10f, 5.2f), false, gold, 0.1f, 0.2f);
                        t.AddString("4 Lox in front of you, left to right: red with the hooded trader, red with the bare one, golden with the hooded one, golden with the bare one.");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "ride")
                    {
                        // goodtidings look ride [up back [up2 back2]]: both versions, each on a saddled Lox, playing the player's Riding_Idle;
                        // the rider moved up and back from the seat by those metres (the second pair for the second Lox)
                        var nums = a.Skip(3).Select(x => float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f).ToList();
                        if (nums.Count == 0) { nums.Add(0.1f); nums.Add(0.2f); }
                        while (nums.Count < 2) nums.Add(0f);
                        if (nums.Count < 4) { nums.Add(nums[0]); nums.Add(nums[1]); }
                        for (int k = 0; k < 2; k++)
                            if (!Ride(Place(p, k, 2, 8f, 4.5f), k == 0, null, nums[k * 2], nums[k * 2 + 1])) { t.AddString("No riding clip on the player."); return; }
                        t.AddString($"2 Lox in front of you, ridden by the hooded (up {nums[0]}, back {nums[1]}) and the bare trader (up {nums[2]}, back {nums[3]}).");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "trader")
                    {
                        float hood = a.Length > 3 && float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hs) ? hs : 1.1f;
                        for (int k = 0; k < 2; k++) BuildTrader(Place(p, k, 2), k == 0, hood);
                        t.AddString($"2 looks in front of you: hooded (black hood at size {hood}, black cape), bare (no hood, no cape); ochre clothes.");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "colours")
                    {
                        // The bare version (no hood, no cape) in the clothes as they are, then recoloured (hue, strength, brightness)
                        var buckle = new RectInt(108, 0, 20, 20);
                        var tints = new (string name, Cuts.Tint tint)[]
                        {
                            ("as it is", null),
                            ("forest green", new Cuts.Tint { Hue = 0.33f, Saturation = 0.45f, Value = 1.1f, Keep = { buckle } }),
                            ("deep red", new Cuts.Tint { Hue = 0.0f, Saturation = 0.55f, Value = 1.05f, Keep = { buckle } }),
                            ("blue", new Cuts.Tint { Hue = 0.6f, Saturation = 0.5f, Value = 1.1f, Keep = { buckle } }),
                            ("ochre", new Cuts.Tint { Hue = 0.11f, Saturation = 0.55f, Value = 1.2f, Keep = { buckle } }),
                        };
                        for (int k = 0; k < tints.Length; k++)
                        {
                            GameObject body = DvergerLook.Body(Place(p, k, tints.Length));
                            DvergerLook.Face(body, "Hildir", "Hildir");
                            DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead, 1f, tints[k].tint);
                            DvergerLook.Hair(body, "DvergerHairFemale");
                        }
                        t.AddString("5 looks in front of you, left to right: " + string.Join(", ", tints.Select(x => x.name)) + ".");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "hood")
                    {
                        var sizes = new List<float>();
                        for (int k = 3; k < a.Length; k++) if (float.TryParse(a[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) sizes.Add(f);
                        if (sizes.Count == 0) sizes.AddRange(new[] { 1f, 1.08f, 1.15f, 1.25f });
                        for (int k = 0; k < sizes.Count; k++)
                        {
                            GameObject body = DvergerLook.Body(Place(p, k, sizes.Count));
                            DvergerLook.Face(body, "Hildir", "Hildir");
                            DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead);
                            DvergerLook.Outfit(body, Brown, DvergerLook.Keep.HeadOnly, sizes[k]);
                            DvergerLook.Hair(body, "DvergerHairFemale");
                        }
                        t.AddString($"{sizes.Count} looks in front of you, Hildir's face, white braids and the brown hood, left to right at sizes " +
                                    string.Join(", ", sizes) + ". 'goodtidings look hood 1.1 1.2' for others.");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "face")
                    {
                        ShowFace(p);
                        t.AddString("4 looks in front of you, left to right: Dverger with its own face and red braids (to compare), Dverger with Hildir's face " +
                                    "and white braids, the same with the brown hood, the same with the Support mage's hat; all in Ashlands clothes. 'goodtidings look clear' takes them away.");
                        return;
                    }
                    if (a.Length > 2 && a[2] == "hildir")
                    {
                        ShowHildir(p);
                        t.AddString("3 looks in front of you, left to right: Dverger in Ashlands clothes (to compare), Hildir in Ashlands clothes, " +
                                    "Hildir in Ashlands clothes + brown hood; all with red braids. 'goodtidings look clear' takes them away.");
                        return;
                    }
                    bool beard = a.Length > 2 && a[2] == "beard";
                    ShowLooks(p, beard);
                    t.AddString($"{Shown.Count} looks in front of you, left to right: A Ashlands clothes + brown hood, " +
                                "B Ashlands clothes (no hat), C Ashlands as it is, D brown cloak as it is; all with red braids" +
                                (beard ? " and a white beard" : "") + ". 'goodtidings look clear' takes them away.");
                    return;
                case "goods":
                    Goods.Enabled = !(a.Length > 2 && a[2] == "off");
                    if (_lastLook != null && p != null) Run(t, _lastLook);
                    t.AddString(Goods.Enabled ? "Goods on." : "Goods off.");
                    return;
                case "hildirface":
                {
                    // how Hildir's face moves: her face mesh's blend shapes, and which head bones each of her clips moves (sampled over time)
                    GameObject body = DvergerLook.BodyOf("Hildir", "Hildir", null);
                    var sb = new System.Text.StringBuilder();
                    foreach (SkinnedMeshRenderer r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Mesh m = r.sharedMesh;
                        sb.Append($"{r.name}: {m.blendShapeCount} blend shapes");
                        for (int i = 0; i < m.blendShapeCount; i++) sb.Append(" " + m.GetBlendShapeName(i));
                        sb.Append("; materials " + string.Join(",", r.sharedMaterials.Select(x => x ? x.name : "-")) + "\n");
                    }
                    Animator anim = body.GetComponent<Animator>();
                    Transform head = Utils.FindChild(body.transform, "head");
                    var bones = head.GetComponentsInChildren<Transform>(true);
                    foreach (AnimationClip clip in anim.runtimeAnimatorController.animationClips.Distinct())
                    {
                        var start = new Dictionary<Transform, (Vector3, Quaternion)>();
                        var moved = new Dictionary<string, float>();
                        for (int k = 0; k <= 20; k++)
                        {
                            clip.SampleAnimation(body, clip.length * k / 20f);
                            foreach (Transform b in bones)
                            {
                                if (!start.ContainsKey(b)) { start[b] = (b.localPosition, b.localRotation); continue; }
                                float deg = Quaternion.Angle(start[b].Item2, b.localRotation) + (b.localPosition - start[b].Item1).magnitude * 1000f;
                                if (deg > 0.5f) moved[b.name] = Mathf.Max(moved.TryGetValue(b.name, out float o) ? o : 0f, deg);
                            }
                        }
                        sb.Append($"{clip.name} ({clip.length:0.0}s, human {clip.humanMotion}): " + string.Join(", ", moved.Select(kv => $"{kv.Key} {kv.Value:0}")) + "\n");
                    }
                    Object.Destroy(body);
                    t.AddString(sb.ToString());
                    return;
                }
                case "aim":
                {
                    // what the player is looking at, for the Atlas / Dagny choice
                    GameObject hover = p ? p.GetHoverObject() : null;
                    TraderVisitor v = Object.FindAnyObjectByType<TraderVisitor>();
                    if (v == null) { t.AddString("No trader."); return; }
                    Transform cam = GameCamera.instance.transform;
                    Transform visual = v.transform.Find("Visual");
                    Transform head = visual ? Utils.FindChild(visual, "Head") : null;
                    Collider hc = Utils.FindChild(v.transform, "Capsule (1)")?.GetComponent<Collider>();
                    string Ang(Vector3 at) => $"{Vector3.Angle(cam.forward, at - cam.position):0.0} deg, {Vector3.Distance(cam.position, at):0.0} m";
                    t.AddString($"hover {(hover ? hover.name : "none")}, at head {TraderVisitor.AtHead(p)}; head bone {(head ? Ang(head.position) : "none")}; " +
                                $"head collider {(hc ? Ang(hc.bounds.center) : "none")}; rider {Ang(v.transform.position + Vector3.up * 3f)}; " +
                                $"atlas at {v.transform.position}, facing {v.transform.forward}, head bone at {(head ? head.position.ToString() : "-")}, collider at {(hc ? hc.bounds.center.ToString() : "-")}");
                    return;
                }
                case "itemdump":
                {
                    // every item the game knows: prefab, English name, type, value, stack size, to a file in Claude Tools' shots folder
                    var sb = new System.Text.StringBuilder();
                    foreach (GameObject item in ObjectDB.instance.m_items)
                    {
                        ItemDrop d = item ? item.GetComponent<ItemDrop>() : null;
                        if (d == null) continue;
                        var sh = d.m_itemData.m_shared;
                        sb.AppendLine($"{item.name}	{Localization.instance.Localize(sh.m_name)}	{sh.m_itemType}	{sh.m_value}	{sh.m_maxStackSize}");
                    }
                    string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", "items.tsv");
                    System.IO.File.WriteAllText(path, sb.ToString());
                    t.AddString(path);
                    return;
                }
                case "hurt":
                {
                    // tries the ways a creature is hurt on the trader: health before and after, and whether poison took hold
                    TraderVisitor v = Object.FindAnyObjectByType<TraderVisitor>();
                    Character c = v ? v.GetComponent<Character>() : null;
                    if (c == null) { t.AddString("No trader."); return; }
                    float before = c.GetHealth();
                    var hit = new HitData { m_point = c.transform.position };
                    hit.m_damage.m_blunt = 50f;
                    c.Damage(hit);
                    var raw = new HitData { m_point = c.transform.position };
                    raw.m_damage.m_poison = 50f;
                    c.ApplyDamage(raw, true, true, HitData.DamageModifier.Normal);
                    StatusEffect se = c.GetSEMan().AddStatusEffect("Poison".GetStableHashCode(), true);
                    t.AddString($"health {before:0} -> {c.GetHealth():0}; poison took hold: {se != null || c.GetSEMan().HaveStatusEffect("Poison".GetStableHashCode())}");
                    return;
                }
                case "colliders":
                {
                    TraderVisitor v = Object.FindAnyObjectByType<TraderVisitor>();
                    if (v == null || p == null) { t.AddString("No trader."); return; }
                    int mask = Traverse.Create(p).Field("m_interactMask").GetValue<int>();
                    foreach (Collider c in v.GetComponentsInChildren<Collider>(true))
                        t.AddString($"{c.name}: {c.GetType().Name}, layer {LayerMask.LayerToName(c.gameObject.layer)}, enabled {c.enabled && c.gameObject.activeInHierarchy}, " +
                                    $"trigger {c.isTrigger}, interact sees it {((mask >> c.gameObject.layer) & 1) == 1}, size {c.bounds.size}");
                    return;
                }
                case "anims":
                {
                    GameObject body = DvergerLook.Body(null);
                    var anim = body.GetComponent<Animator>();
                    t.AddString(anim && anim.runtimeAnimatorController
                        ? string.Join(", ", anim.runtimeAnimatorController.animationClips.Select(c => c.name).Distinct())
                        : "No animator.");
                    Object.Destroy(body);
                    return;
                }
                case "animinfo":
                {
                    // goodtidings animinfo <prefab> [text]: its animator's rig (human rigs can share animations) and clips with that text
                    GameObject pf = a.Length > 2 ? ZNetScene.instance.GetPrefab(a[2]) : null;
                    Animator anim = pf ? pf.GetComponentInChildren<Animator>(true) : null;
                    if (anim == null) { t.AddString("No animator."); return; }
                    string filter = a.Length > 3 ? a[3].ToLowerInvariant() : "";
                    var clips = anim.runtimeAnimatorController ? anim.runtimeAnimatorController.animationClips.Select(c => c.name).Distinct()
                        .Where(n => n.ToLowerInvariant().Contains(filter)).ToList() : new List<string>();
                    t.AddString($"{pf.name}: animator on {anim.name}, controller {anim.runtimeAnimatorController?.name}, avatar {anim.avatar?.name} " +
                                $"human {anim.avatar != null && anim.avatar.isHuman}, {clips.Count} clips: {string.Join(", ", clips)}");
                    return;
                }
                case "pose":
                {
                    // goodtidings pose rest: the animator off, the bones as the model stores them; pose <clip>: that clip, held still
                    string pose = a.Length > 2 ? a[2] : "rest";
                    int n = 0;
                    foreach (GameObject root in Shown)
                    {
                        if (!root) continue;
                        foreach (Animator anim in root.GetComponentsInChildren<Animator>())
                        {
                            if (pose == "rest") { anim.enabled = false; RestPose(anim.transform); n++; continue; }
                            AnimationClip clip = anim.runtimeAnimatorController?.animationClips.FirstOrDefault(c => c.name == pose);
                            if (clip == null) continue;
                            anim.enabled = false;
                            clip.SampleAnimation(anim.gameObject, a.Length > 3 && float.TryParse(a[3], out float f) ? f * clip.length : 0f);
                            n++;
                        }
                    }
                    t.AddString($"{n} looks posed: {pose}.");
                    return;
                }
                case "texdump":
                {
                    // goodtidings texdump <prefab>: saves every material's main texture of a prefab to shots/ (to choose parts of it)
                    GameObject pf = a.Length > 2 ? ZNetScene.instance.GetPrefab(a[2]) : null;
                    if (pf == null) { t.AddString("goodtidings texdump <prefab>"); return; }
                    foreach (Renderer r in pf.GetComponentsInChildren<Renderer>(true))
                        foreach (Material mat in r.sharedMaterials)
                            if (mat && mat.mainTexture) t.AddString($"{r.name}: {mat.name} {mat.shader.name} -> {Cuts.Save(mat.mainTexture, "texture_" + mat.name)}");
                    return;
                }
                case "mesh":
                {
                    // goodtidings mesh <item>: whether its meshes can be read (to cut parts out), and how they're made
                    GameObject item = a.Length > 2 ? ObjectDB.instance.GetItemPrefab(a[2]) : null;
                    if (item == null) { t.AddString("goodtidings mesh <item prefab>"); return; }
                    foreach (SkinnedMeshRenderer r in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Mesh m = r.sharedMesh;
                        t.AddString($"{r.name}: mesh {m.name}, readable {m.isReadable}, {m.vertexCount} vertices, {m.subMeshCount} parts, " +
                                    $"{r.sharedMaterials.Length} materials, {m.bindposes.Length} bones");
                        Material mat = r.sharedMaterial;
                        t.AddString($"  material {mat.name}, shader {mat.shader.name}, keywords [{string.Join(" ", mat.shaderKeywords)}], " +
                                    $"cutoff {(mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff").ToString() : "none")}, render queue {mat.renderQueue}");
                        if (mat.mainTexture is Texture2D tex)
                        {
                            // A copy of its picture, to see where on it each part is (the game's own is locked: drawn through the GPU)
                            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                            Graphics.Blit(tex, rt);
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                            RenderTexture.active = prev;
                            RenderTexture.ReleaseTemporary(rt);
                            string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", "texture_" + mat.name + ".png");
                            System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(copy));
                            Object.Destroy(copy);
                            t.AddString($"  texture {tex.name} {tex.width}x{tex.height} saved to {path}");
                        }
                        // Where the mesh's corners lie on the picture: a drawing of its triangles needs the mesh, which is locked; its
                        // bounds still say how big it is
                        t.AddString($"  bounds {m.bounds}");
                    }
                    return;
                }
                case "tex":
                {
                    // goodtidings tex grid <n|0> | cut <suit> <x0> <y0> <x1> <y1> | clear: then the last 'look' is shown again
                    string what = a.Length > 2 ? a[2] : "";
                    if (what == "grid" && a.Length > 3 && int.TryParse(a[3], out int n)) Cuts.Grid = n;
                    else if (what == "cut" && a.Length > 7 && int.TryParse(a[4], out int x0) && int.TryParse(a[5], out int y0)
                             && int.TryParse(a[6], out int x1) && int.TryParse(a[7], out int y1))
                    {
                        if (!Cuts.BySuit.TryGetValue(a[3], out var list)) Cuts.BySuit[a[3]] = list = new List<RectInt>();
                        list.Add(new RectInt(x0, y0, x1 - x0, y1 - y0));
                    }
                    else if (what == "clear") { Cuts.BySuit.Clear(); Cuts.Grid = 0; }
                    else { t.AddString("goodtidings tex grid <n|0> | cut <suit> <x0> <y0> <x1> <y1> | clear"); return; }
                    Cuts.Clear();
                    if (_lastLook != null && p != null) Run(t, _lastLook);
                    t.AddString($"Grid {Cuts.Grid}, cuts: " + string.Join("; ", Cuts.BySuit.Select(kv => kv.Key + " " + string.Join(" ", kv.Value))));
                    return;
                }
                case "bones":
                {
                    GameObject body = DvergerLook.Body(null);
                    t.AddString(DvergerLook.BoneNames(body));
                    Object.Destroy(body);
                    return;
                }
                case "visit":
                    if (!ZNet.instance.IsServer() && !global::Console.instance.IsCheatsEnabled()) { t.AddString("Host or devcommands only."); return; }
                    if (a.Length > 3 && a[2] == "trader" && a[3] == "here")
                    {
                        // the trader rides in to where this player stands, base or not
                        if (p == null) { t.AddString("Not in a world."); return; }
                        t.AddString(new WanderingTrader().Start(p.transform.position) ? "The wandering trader is on her way." : "She couldn't come (one already here, or no dry ground near).");
                        return;
                    }
                    t.AddString(Visits.TryStart(a.Length > 2 ? a[2] : null) ? "Visit started." : "No visit could start (is a player at a base?).");
                    if (p != null) t.AddString($"Your base value here: {p.GetBaseValue()} (raids and visits need 3).");
                    return;
                default:
                    t.AddString(Usage);
                    return;
            }
        }

        /// <summary>The trader as chosen: ochre clothes; hooded with the cape and hood black, or bare without either.</summary>
        private static GameObject BuildTrader(Transform parent, bool hooded, float hood) => TraderLook.Rider(parent, hooded, hood);

        /// <summary>A saddled, loaded Lox (recoloured by <paramref name="hsv"/> if given) with the trader riding it.</summary>
        private static bool Ride(Transform place, bool hooded, Vector3? hsv, float up, float back)
        {
            GameObject lox = DvergerLook.Lox(place, hsv);
            GameObject body = TraderLook.Mount(lox.transform, hooded, Shown);
            Follow f = body ? body.GetComponentInParent<Follow>() : null;
            if (f) { f.Up = up; f.Back = back; }
            return body != null;
        }

        private static bool Play(GameObject body, string fromPrefab, string clipName)
        {
            if (!TraderLook.Play(body, fromPrefab, clipName, out PlayableGraph graph)) return false;
            Graphs.Add(graph);
            return true;
        }

        private static readonly List<PlayableGraph> Graphs = new List<PlayableGraph>();

        private static void ShowHildir(Player p)
        {
            for (int i = 0; i < 3; i++)
            {
                GameObject body = i == 0 ? DvergerLook.Body(Place(p, i, 3)) : DvergerLook.HildirBody(Place(p, i, 3));
                DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead);
                if (i == 2) DvergerLook.Outfit(body, Brown, DvergerLook.Keep.HeadOnly);
                DvergerLook.Hair(body, Braids);
            }
        }

        private static void ShowFace(Player p)
        {
            for (int i = 0; i < 4; i++)
            {
                GameObject body = DvergerLook.Body(Place(p, i, 4));
                if (i > 0) DvergerLook.Face(body, "Hildir", "Hildir");
                DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead);
                DvergerLook.Hair(body, i == 0 ? Braids : "DvergerHairFemale");
                if (i == 2) DvergerLook.Outfit(body, Brown, DvergerLook.Keep.HeadOnly);
                if (i == 3) DvergerLook.Outfit(body, "DvergerSuitSupport", DvergerLook.Keep.HeadOnly);
            }
        }

        /// <summary>A spot for look <paramref name="i"/> of <paramref name="count"/>, in a row 4 m in front of the camera, facing it.</summary>
        private static Transform Place(Player p, int i, int count, float distance = 4f, float spacing = 1.6f)
        {
            Transform cam = GameCamera.instance ? GameCamera.instance.transform : p.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 at = p.transform.position + fwd * distance + right * ((i - (count - 1) / 2f) * spacing);
            if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
            var root = new GameObject("goodtidings_look_" + (char)('A' + i));
            root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-fwd));
            Shown.Add(root);
            return root.transform;
        }

        private const string Ashlands = TraderLook.Ashlands, Brown = TraderLook.Brown, Braids = "DvergerHairFemale_Redhair";

        private static void ShowLooks(Player p, bool beard)
        {
            for (int i = 0; i < 4; i++)
            {
                GameObject body = DvergerLook.Body(Place(p, i, 4));
                switch (i)
                {
                    case 0:
                        DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead);
                        DvergerLook.Outfit(body, Brown, DvergerLook.Keep.HeadOnly);
                        break;
                    case 1: DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.NoHead); break;
                    case 2: DvergerLook.Outfit(body, Ashlands, DvergerLook.Keep.All); break;
                    case 3: DvergerLook.Outfit(body, Brown, DvergerLook.Keep.All); break;
                }
                DvergerLook.Hair(body, Braids);
                if (beard) DvergerLook.Hair(body, "DvergerHairMale", "beardfluff", "mustachefluff");
            }
        }

        /// <summary>Puts every bone back as the Dverger prefab stores it (no animation).</summary>
        private static void RestPose(Transform visual)
        {
            Transform source = ZNetScene.instance.GetPrefab("Dverger").transform.Find("Visual");
            foreach (Transform b in visual.GetComponentsInChildren<Transform>(true))
            {
                Transform from = Utils.FindChild(source, b.name);
                if (from == null || b == visual) continue;
                b.localPosition = from.localPosition;
                b.localRotation = from.localRotation;
            }
        }

        private static void ClearLooks()
        {
            foreach (PlayableGraph g in Graphs) if (g.IsValid()) g.Destroy();
            Graphs.Clear();
            foreach (GameObject go in Shown) if (go) Object.Destroy(go);
            Shown.Clear();
            Goods.Forget();
        }
    }
}
