using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Props on nemeses: arrows sticking out of them, an axe in the back, ice on the shoulders,
    /// a helmet (a stolen copy, or a dented bronze one). Which props a nemesis carries comes from its history
    /// (see Rules.PropsFor), so every player's game shows the same ones.
    ///
    /// A prop is a copy of a game item's model with every script stripped (the same method as the Fuling
    /// Heckler's hats): purely visual, local to each game, pinned to a bone so it moves with the animation.
    /// Only items on the curated list are used, and each is placed by a saved fit (tuned with the fitting tool).
    /// </summary>
    internal static class Props
    {
        /// <summary>Where each prop comes from and which bone it sits on.</summary>
        internal class Part
        {
            public string Source = "";   // "item:AxeStone" or "projectile:ArrowWood"
            public string Bone = "";     // a humanoid bone name, e.g. "UpperChest", "Head"
        }

        internal static FitBook Fits = new FitBook();   // the built-in fits with yours on top
        private static FitBook _yourFits = new FitBook();  // just what's in Grudges.propfits.txt
        private static int _fitsVersion;
        private static int _partsVersion = -1;
        private static readonly Dictionary<string, Dictionary<string, Part>> _parts =
            new Dictionary<string, Dictionary<string, Part>>(StringComparer.OrdinalIgnoreCase);
        private static GameObject _staging;
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Items to audition in the persona slot: hats and headgear, every creature's trophy, then odds and ends.
        /// More can be added without a new version: 'try-items = ...' under [fitting] in Grudges.looks.txt.
        /// A name the game doesn't have just shows a one-time "no item called" warning in the log.
        /// </summary>
        private static readonly string[] BuiltInTryItems =
        {
            // Headgear. Copied as the item's rigid model (the one you see on the ground), so hoods hold their shape
            // instead of wrapping the head; caps, crowns and masks suit that best.
            "HelmetMidsummerCrown", "HelmetYule", "HelmetCelebration", "HelmetDverger", "HelmetPointyHat", "HelmetRoot",
            "HelmetHat1", "HelmetHat2", "HelmetHat3", "HelmetHat4", "HelmetHat5", "HelmetHat6", "HelmetHat7", "HelmetHat8", "HelmetHat9", "HelmetHat10",
            "HelmetLeather", "HelmetTrollLeather", "HelmetBronze", "HelmetIron", "HelmetDrake", "HelmetPadded", "HelmetCarapace", "HelmetFlametal",
            "HelmetFenring", "HelmetOdin", "HelmetMage",
            // Trophies
            "TrophyBoar", "TrophyDeer", "TrophyNeck", "TrophyEikthyr",
            "TrophyGreydwarf", "TrophyGreydwarfBrute", "TrophyGreydwarfShaman", "TrophySkeleton", "TrophySkeletonPoison", "TrophyFrostTroll", "TrophyTheElder",
            "TrophyDraugr", "TrophyDraugrFem", "TrophyDraugrElite", "TrophyBlob", "TrophyLeech", "TrophyWraith", "TrophyAbomination", "TrophyBonemass",
            "TrophyWolf", "TrophyFenring", "TrophyUlv", "TrophyCultist", "TrophyHatchling", "TrophySGolem", "TrophyBjorn", "TrophyDragonQueen",
            "TrophyLox", "TrophyDeathsquito", "TrophyGrowth", "TrophyGoblin", "TrophyGoblinBrute", "TrophyGoblinShaman", "TrophyGoblinKing",
            "TrophySeeker", "TrophySeekerBrute", "TrophyTick", "TrophyGjall", "TrophyDvergr", "TrophyHare", "TrophySeekerQueen",
            "TrophyAsksvin", "TrophyFader", "TrophySurtling", "TrophySerpent", "TrophySkeletonHildir",
            // Odds and ends
            "BoneFragments", "Mushroom", "Club", "Coins", "AncientSeed", "RawMeat", "AxeStone", "Torch", "Crystal"
        };

        /// <summary>The Try Item dropdown: the built-in list plus any extras from the looks file (read at startup).</summary>
        internal static string[] TryItems()
        {
            var list = new List<string> { Plugin.NoItem };
            list.AddRange(RuleSettings.ParseList(Plugin.Looks.Get("fitting", "try-items")));
            foreach (var i in BuiltInTryItems) if (!list.Contains(i, StringComparer.OrdinalIgnoreCase)) list.Add(i);
            return list.ToArray();
        }

        /// <summary>Bones a prop can hang from (humanoid names; other rigs match by part of the bone's name).</summary>
        internal static readonly string[] Bones =
        {
            "Head", "Neck", "UpperChest", "Chest", "Spine", "Hips", "LeftShoulder", "RightShoulder",
            "LeftUpperArm", "RightUpperArm", "LeftHand", "RightHand", "LeftUpperLeg", "RightUpperLeg"
        };

        /// <summary>Every prop the fitting tool can show.</summary>
        internal static readonly string[] FittableProps = { "arrow1", "arrow2", "arrow3", "axe", "ice1", "ice2", "helmet", "persona" };

        /// <summary>Where the built-in props sit, until the fitting tool saves something better.</summary>
        internal const string DefaultFits =
            "arrow1: 0.05, -0.15, 0.08, 15, 10, 0, 1 | arrow2: 0.05, -0.1, 0, 25, -20, 0, 1 | arrow3: 0, 0.15, -0.05, -10, 180, 0, 1 | " +
            "axe: 0.1, -0.2, 0, 0, 0, 45, 1 | helmet: 0.1, 0, 0, 0, 0, 0, 1 | ice1: 0.1, 0, 0, 0, 0, 0, 0.6 | ice2: 0.1, 0, 0, 0, 0, 0, 0.6";

        /// <summary>A number that changes whenever the looks or personalities file, the fits, the aura strength or the tried item do.</summary>
        internal static int Refresh()
        {
            if (_partsVersion != Plugin.Looks.Version) { _parts.Clear(); _partsVersion = Plugin.Looks.Version; }
            int h = ((Plugin.Looks.Version * 7919 + _fitsVersion) * 31 + Plugin.PersonasVersion) * 31 + Plugin.AuraStrength.Value.GetHashCode();
            if (Trying) h = h * 31 + (Plugin.TryItem.Value + "@" + Plugin.TryBone.Value + Plugin.FitPersonality.Value).GetHashCode();
            return h;
        }

        /// <summary>The fitting tool is trying another item in the persona slot.</summary>
        internal static bool Trying =>
            Plugin.FitMode.Value && Plugin.FitProp.Value == "persona" && Plugin.TryItem.Value != Plugin.NoItem;

        /// <summary>A stolen or dented helmet and a personality prop on the head would sit inside each other: the helmet wins.</summary>
        internal static List<string> WithoutClashes(List<string> keys, string species)
        {
            if (!keys.Any(k => k.StartsWith("helmet"))) return keys;
            return keys.Where(k => !(k.StartsWith("persona:") && string.Equals(PartFor(k, species)?.Bone, "Head", StringComparison.OrdinalIgnoreCase))).ToList();
        }

        /// <summary>Writes the tried item into the personalities file as that personality's prop.</summary>
        internal static void SaveTriedProp()
        {
            if (Plugin.TryItem.Value == Plugin.NoItem) { Plugin.ShowPlain("Grudges: pick a Try Item first."); return; }
            string persona = Plugin.FitPersonality.Value;
            string section = Plugin.TryOnlyThisCreature.Value ? persona + ":" + FittingSpecies() : persona;
            string spec = $"item:{Plugin.TryItem.Value}@{Plugin.TryBone.Value}";
            try
            {
                string text = File.ReadAllText(Plugin.PersonalitiesFilePath);
                File.WriteAllText(Plugin.PersonalitiesFilePath,
                    KeyValueBook.SetKeys(text, section, new Dictionary<string, string> { { "prop", spec } }));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Props: couldn't write the personalities file ({e.Message})."); return; }
            Plugin.ReloadPersonalities();
            _loading = true; Plugin.TryItem.Value = Plugin.NoItem; _loading = false;
            Plugin.ShowPlain($"Grudges: [{section}] prop = {spec}. Now fit it and press Save fit.");
        }

        // ---- The fits file ----

        internal static void LoadFitsFile()
        {
            try
            {
                if (!File.Exists(Plugin.FitsFilePath))
                {
                    // First run of this version: your saved fits come over from the old config setting.
                    string old = Plugin.TakeOld("8d - Props", "Prop Fits");
                    _yourFits = old != null ? FitBook.Parse(old) : new FitBook();
                    _yourFits.Fits.Remove("torch");
                    var builtIn = FitBook.Parse(DefaultFits);
                    foreach (var kv in builtIn.Fits)   // keep only what differs from the built-in ones
                        if (_yourFits.Fits.TryGetValue(kv.Key, out var mine) && mine.ToString() == kv.Value.ToString()) _yourFits.Fits.Remove(kv.Key);
                    File.WriteAllText(Plugin.FitsFilePath, _yourFits.ToFile());
                }
                else _yourFits = FitBook.ParseFile(File.ReadAllLines(Plugin.FitsFilePath));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Props: couldn't read {Plugin.FitsFilePath} ({e.Message}); using the built-in fits.");
                _yourFits = new FitBook();
            }
            Rebuild();
        }

        private static void Rebuild()
        {
            Fits = FitBook.Parse(DefaultFits);
            foreach (var kv in _yourFits.Fits) Fits.Fits[kv.Key] = kv.Value;
            _fitsVersion++;
        }

        private static void SaveFitsFile()
        {
            try { File.WriteAllText(Plugin.FitsFilePath, _yourFits.ToFile()); }
            catch (Exception e) { Plugin.Log.LogWarning($"Props: couldn't save {Plugin.FitsFilePath} ({e.Message})."); }
            Rebuild();
        }

        // ---- Parts ----

        /// <summary>The props section of the looks file for one creature type (its [props:Creature] on top).</summary>
        private static Dictionary<string, Part> PartsFor(string species)
        {
            Refresh();
            species = species ?? "";
            if (_parts.TryGetValue(species, out var d)) return d;
            d = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Plugin.Looks.Section("props", species)) d[kv.Key] = ParsePart(kv.Value);
            _parts[species] = d;
            return d;
        }

        private static Part ParsePart(string spec)
        {
            var at = spec.Split('@');
            return new Part { Source = at[0].Trim(), Bone = at.Length > 1 ? at[1].Trim() : "Chest" };
        }

        /// <summary>The part for a prop key. "helmet:HelmetIron" uses the "helmet" bone with that item.</summary>
        internal static Part PartFor(string key, string species = null)
        {
            if (key.StartsWith("persona:") && Trying && key == FittingKey)
                return new Part { Source = "item:" + Plugin.TryItem.Value, Bone = Plugin.TryBone.Value };
            if (key.StartsWith("persona:"))
            {
                // A personality's prop comes from the personalities file ("item:Club@UpperChest").
                string spec = Plugin.Personas.Get(key.Substring(8), species, "prop");
                return spec.Length == 0 ? null : ParsePart(spec);
            }
            var parts = PartsFor(species);
            int c = key.IndexOf(':');
            if (c > 0)
            {
                string family = key.Substring(0, c), item = key.Substring(c + 1);
                parts.TryGetValue(family, out var fam);
                return new Part { Source = "item:" + item, Bone = fam?.Bone ?? "Head" };
            }
            return parts.TryGetValue(key, out var p) ? p : null;
        }

        /// <summary>Copies a prop's model and pins it to the right bone. Null (with a one-time log line) if it can't.</summary>
        internal static GameObject Make(Character c, string key)
        {
            if (key.StartsWith("aura:")) return Auras.Make(c, key.Substring(5));
            try
            {
                var part = PartFor(key, GameInfo.Prefab(c));
                if (part == null) { WarnOnce(key, $"Props: no part defined for '{key}'."); return null; }
                var bone = FindBone(c, part.Bone);
                if (bone == null) { WarnOnce(GameInfo.Prefab(c) + part.Bone, $"Props: {GameInfo.Prefab(c)} has no '{part.Bone}' bone."); return null; }
                var model = SourceModel(part.Source, out Vector3 worldScale);
                if (model == null) return null;

                var copy = SafeCopy(model, key);
                if (copy == null) return null;
                copy.transform.SetParent(bone, false);
                var fitter = copy.AddComponent<PropFitter>();
                fitter.Init(c.transform, bone, worldScale, RestRelation(c, bone));
                return copy;
            }
            catch (Exception e)
            {
                string where = (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
                WarnOnce(key + "err", $"Props: couldn't make '{key}' ({e.GetType().Name}: {e.Message}) {where}");
                return null;
            }
        }

        /// <summary>The model to copy: an item's rigid "attach" model (or the item itself), or an ammo's flying projectile.</summary>
        private static GameObject SourceModel(string source, out Vector3 worldScale)
        {
            worldScale = Vector3.one;
            if (ObjectDB.instance == null) return null;
            int c = source.IndexOf(':');
            string kind = c > 0 ? source.Substring(0, c) : "item";
            string name = c > 0 ? source.Substring(c + 1) : source;
            var item = ObjectDB.instance.GetItemPrefab(name);
            if (item == null) { WarnOnce(name, $"Props: no item called '{name}'."); return null; }

            if (kind == "projectile")
            {
                GameObject proj = null;
                try
                {
                    var drop = item.GetComponent<ItemDrop>();
                    proj = Traverse.Create(drop).Field("m_itemData").Field("m_shared").Field("m_attack").Field("m_attackProjectile").GetValue<GameObject>();
                }
                catch { }
                if (proj == null) { WarnOnce(source, $"Props: '{name}' has no projectile; using the item itself."); }
                else { worldScale = proj.transform.lossyScale; return proj; }
            }

            var attach = FindDeep(item.transform, "attach");
            if (attach != null) { worldScale = attach.lossyScale; return attach.gameObject; }
            worldScale = item.transform.lossyScale;
            return item;
        }

        /// <summary>Copies a model while inactive and strips everything but looks: scripts, physics, trails, sparkles, lights.</summary>
        private static GameObject SafeCopy(GameObject source, string name)
        {
            if (_staging == null)
            {
                _staging = new GameObject("Grudges_PropStaging");
                _staging.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_staging);
            }
            var copy = UnityEngine.Object.Instantiate(source, _staging.transform, false);
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
            foreach (var col in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
            // Sparkles and flames off. Remove the components, not their objects: on some items (crystals, the torch)
            // the effect sits on the same object as the model, and deleting the object deleted the prop with it.
            foreach (var psr in copy.GetComponentsInChildren<ParticleSystemRenderer>(true)) UnityEngine.Object.DestroyImmediate(psr);
            foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true)) UnityEngine.Object.DestroyImmediate(ps);
            foreach (var tr in copy.GetComponentsInChildren<TrailRenderer>(true)) tr.enabled = false;
            // Armour carries a second, skinned version of itself for wearing ("attach_skin"); it can't follow a creature's bones.
            foreach (var t in copy.GetComponentsInChildren<Transform>(true).Where(t => t != copy.transform && t.name == "attach_skin").ToList())
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
            foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var a in copy.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
            if (copy.GetComponentInChildren<ZNetView>(true) != null) { UnityEngine.Object.DestroyImmediate(copy); return null; }
            copy.name = "Grudges_Prop_" + name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.SetActive(true);
            return copy;
        }

        private static readonly Dictionary<string, Quaternion> _restCache = new Dictionary<string, Quaternion>();

        /// <summary>
        /// How a bone sits relative to the body in the creature's neutral pose, taken from the game's un-animated
        /// template of that creature. Measuring from the live body instead meant a prop made mid-run or mid-swing
        /// kept that odd angle for good. Null if the template can't be read (then the live pose is used).
        /// </summary>
        private static Quaternion? RestRelation(Character c, Transform bone)
        {
            try
            {
                var names = new List<string>();
                for (Transform t = bone; t != null && t != c.transform; t = t.parent) names.Add(t.name);
                names.Reverse();
                string path = string.Join("/", names.ToArray());
                string key = GameInfo.Prefab(c) + ":" + path;
                if (_restCache.TryGetValue(key, out var cached)) return cached;

                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(GameInfo.Prefab(c)) : null;
                var restBone = prefab != null ? prefab.transform.Find(path) : null;
                if (restBone == null)
                {
                    Plugin.DebugLog($"Props: no neutral pose for {key}; using the live pose.");
                    return null;
                }
                var rel = Quaternion.Inverse(prefab.transform.rotation) * restBone.rotation;
                _restCache[key] = rel;
                return rel;
            }
            catch { return null; }
        }

        /// <summary>A bone by humanoid name (works on any humanoid rig), else by name in the model.</summary>
        internal static Transform FindBone(Character c, string bone)
        {
            var anim = c.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                try
                {
                    var hb = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), bone, true);
                    var t = anim.GetBoneTransform(hb);
                    if (t != null) return t;
                }
                catch { }
            }
            return FindDeepContains(c.transform, bone);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        private static Transform FindDeepContains(Transform t, string name)
        {
            if (t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeepContains(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        private static void WarnOnce(string key, string msg)
        {
            if (_warned.Add(key)) Plugin.Log.LogWarning(msg);
        }

        // ---- Fitting tool ------------------------------------------------------------------

        /// <summary>The prop being fitted, as a key ("arrow1", "helmet:HelmetIron").</summary>
        internal static string FittingKey =>
            Plugin.FitProp.Value == "helmet" ? "helmet:" + Plugin.FitHelmet.Value
            : Plugin.FitProp.Value == "persona" ? "persona:" + Plugin.FitPersonality.Value
            : Plugin.FitProp.Value;

        internal static Fit SliderFit() => new Fit
        {
            Up = Plugin.FitUp.Value, Forward = Plugin.FitForward.Value, Side = Plugin.FitSide.Value,
            Tilt = Plugin.FitTilt.Value, Turn = Plugin.FitTurn.Value, Roll = Plugin.FitRoll.Value, Size = Plugin.FitSize.Value
        };

        /// <summary>Puts the saved fit of the selected prop (on the nearest nemesis's species) into the sliders.</summary>
        internal static void LoadSliders()
        {
            Refresh();
            string species = FittingSpecies();
            var f = Fits.For(species, FittingKey);
            _loading = true;
            Plugin.FitUp.Value = f.Up; Plugin.FitForward.Value = f.Forward; Plugin.FitSide.Value = f.Side;
            Plugin.FitTilt.Value = f.Tilt; Plugin.FitTurn.Value = f.Turn; Plugin.FitRoll.Value = f.Roll; Plugin.FitSize.Value = f.Size;
            _loading = false;
        }

        internal static bool _loading;

        /// <summary>Saves the sliders as the fit for this prop on this species.</summary>
        internal static void SaveSliders()
        {
            string species = FittingSpecies(), family = Kinds.FamilyOf(species);
            bool forFamily = Plugin.FitSaveFor.Value == "Whole family" && family != species;
            string key = $"{(forFamily ? family : species)}/{FittingKey}";
            _yourFits.Fits[key] = SliderFit();
            // Saving for the family: drop this creature's own fit, or it would keep overriding what you just saved.
            if (forFamily) _yourFits.Fits.Remove($"{species}/{FittingKey}");
            SaveFitsFile();
            Plugin.ShowPlain($"Grudges: saved the fit for {key}{(forFamily ? $" (every {family} without its own)" : "")}.");
            Plugin.Log.LogInfo($"Props: saved fit {key}: {_yourFits.Fits[key]}");
        }

        /// <summary>Removes this creature's own fit; if it had none, its family's. One press = one step back toward the default.</summary>
        internal static void ForgetFit()
        {
            string species = FittingSpecies(), family = Kinds.FamilyOf(species);
            string own = $"{species}/{FittingKey}", fam = $"{family}/{FittingKey}";
            string removed = _yourFits.Fits.Remove(own) ? own : family != species && _yourFits.Fits.Remove(fam) ? fam : null;
            if (removed != null) { SaveFitsFile(); Plugin.ShowPlain($"Grudges: forgot the fit for {removed}."); }
            else Plugin.ShowPlain("Grudges: nothing saved to forget; this is the default fit.");
            LoadSliders();
        }

        internal static string FittingSpecies()
        {
            var p = Player.m_localPlayer;
            var c = p != null ? GameInfo.NearestNemesis(p.transform.position, 25f) : null;
            return c != null ? GameInfo.Prefab(c) : "Greydwarf";
        }

        /// <summary>Every prop the fitting tool shows at once (the selected helmet in the helmet slot).</summary>
        internal static List<string> FittingSet() =>
            FittableProps.Select(k => k == "helmet" ? "helmet:" + Plugin.FitHelmet.Value
                                    : k == "persona" ? "persona:" + Plugin.FitPersonality.Value : k).ToList();
    }

    /// <summary>
    /// Sits on a prop and places it by a Fit: offsets in the creature's own directions, then tilt, turn and roll,
    /// and a size relative to the item's real-world size. It remembers how the bone sat relative to the body when
    /// the prop went on, so "up" means the creature's up whatever way the bone itself points.
    /// </summary>
    internal class PropFitter : MonoBehaviour
    {
        private Quaternion _restRel = Quaternion.identity;
        private float _boneScale = 1f;
        private Vector3 _itemScale = Vector3.one;

        public void Init(Transform body, Transform joint, Vector3 itemWorldScale, Quaternion? restRelation = null)
        {
            _restRel = restRelation ?? Quaternion.Inverse(body.rotation) * joint.rotation;
            float s = joint.lossyScale.x;
            _boneScale = Mathf.Abs(s) > 0.0001f ? s : 1f;
            _itemScale = itemWorldScale;
        }

        public void Apply(Fit f)
        {
            if (f == null) f = Fit.Identity;
            var inv = Quaternion.Inverse(_restRel);
            Vector3 offset = new Vector3(f.Side, f.Up, f.Forward);
            transform.localPosition = (inv * offset) / _boneScale;
            Quaternion bodyRot = Quaternion.AngleAxis(f.Turn, Vector3.up) * Quaternion.AngleAxis(f.Tilt, Vector3.right) * Quaternion.AngleAxis(f.Roll, Vector3.forward);
            transform.localRotation = inv * bodyRot * _restRel;
            Vector3 bone = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            Vector3 want = _itemScale * (f.Size <= 0f ? 1f : f.Size);
            transform.localScale = new Vector3(Div(want.x, bone.x), Div(want.y, bone.y), Div(want.z, bone.z));
        }

        private static float Div(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;
    }
}

namespace Grudges
{
    /// <summary>
    /// Elemental auras: embers pouring off a burned nemesis, frost mist off a frozen one, and so on. Borrowed from the
    /// game's own status-effect visuals (the look a creature has while it's burning, frozen, poisoned...), copied with
    /// their scripts stripped, made to loop, and turned down so they read as a lingering mark rather than a live effect.
    /// Each one sits at the middle of the body and follows the creature.
    /// </summary>
    internal static class Auras
    {
        private static GameObject _staging;
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _listedEffects;

        internal static GameObject Make(Character c, string type)
        {
            try
            {
                string source = Plugin.Looks.Get("scar-auras", type, GameInfo.Prefab(c));
                if (string.IsNullOrEmpty(source)) return null;
                var prefab = Find(source);
                if (prefab == null)
                {
                    if (_warned.Add(source)) Plugin.Log.LogWarning($"Auras: couldn't find a visual for '{source}' ({type}).");
                    ListStatusEffectsOnce();
                    return null;
                }

                if (_staging == null)
                {
                    _staging = new GameObject("Grudges_AuraStaging");
                    _staging.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_staging);
                }
                var copy = UnityEngine.Object.Instantiate(prefab, _staging.transform, false);
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
                foreach (var col in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
                foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
                foreach (var a in copy.GetComponentsInChildren<AudioSource>(true)) a.enabled = false; // no constant crackling
                foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.intensity *= 0.5f;
                if (copy.GetComponentInChildren<ZNetView>(true) != null) { UnityEngine.Object.DestroyImmediate(copy); return null; }

                float strength = Plugin.AuraStrength.Value;
                foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = true;
                    var em = ps.emission;
                    em.rateOverTimeMultiplier *= strength;
                    em.rateOverDistanceMultiplier *= strength;
                }

                // Middle of the body, following the creature (not a bone: bone scales would shrink the effect).
                var chest = Props.FindBone(c, "Chest") ?? Props.FindBone(c, "Spine");
                Vector3 local = chest != null ? c.transform.InverseTransformPoint(chest.position) : Vector3.up * 1f;
                copy.name = "Grudges_Aura_" + type;
                copy.transform.SetParent(c.transform, false);
                copy.transform.localPosition = local;
                copy.transform.localRotation = Quaternion.identity;
                copy.SetActive(true);
                foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);
                return copy;
            }
            catch (Exception e)
            {
                if (_warned.Add(type + "err")) Plugin.Log.LogWarning($"Auras: couldn't make the {type} aura ({e.GetType().Name}: {e.Message}).");
                return null;
            }
        }

        /// <summary>
        /// "vfx:name" is a game effect by name. Anything else is a status effect ("Burning", "Frost"...): we take the
        /// first visual it shows on a character.
        /// </summary>
        private static GameObject Find(string source)
        {
            if (source.StartsWith("vfx:"))
                return ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(source.Substring(4)) : null;
            if (ObjectDB.instance == null) return null;
            try
            {
                var list = Traverse.Create(ObjectDB.instance).Field("m_StatusEffects").GetValue() as System.Collections.IEnumerable;
                if (list == null) return null;
                foreach (var se in list)
                {
                    var so = se as UnityEngine.Object;
                    if (so == null || !string.Equals(so.name, source, StringComparison.OrdinalIgnoreCase)) continue;
                    var effects = Traverse.Create(se).Field("m_startEffects").Field("m_effectPrefabs").GetValue() as System.Collections.IEnumerable;
                    if (effects == null) return null;
                    foreach (var data in effects)
                    {
                        var go = Traverse.Create(data).Field("m_prefab").GetValue<GameObject>();
                        if (go != null && go.GetComponentInChildren<ParticleSystem>(true) != null) return go;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Lists the game's status effects once, so better names can go in 'Scar Auras'.</summary>
        private static void ListStatusEffectsOnce()
        {
            if (_listedEffects || ObjectDB.instance == null) return;
            _listedEffects = true;
            try
            {
                var list = Traverse.Create(ObjectDB.instance).Field("m_StatusEffects").GetValue() as System.Collections.IEnumerable;
                var names = new List<string>();
                if (list != null) foreach (var se in list) if (se is UnityEngine.Object o && o != null) names.Add(o.name);
                Plugin.Log.LogInfo("Auras: status effects you could use in 'Scar Auras': " + string.Join(", ", names.OrderBy(n => n).ToArray()));
            }
            catch { }
        }
    }
}
