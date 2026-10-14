using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Looks over the creatures your game has loaded twice a second and makes the nemeses look and act the part:
    /// name, size, colour, strengths and weaknesses, traits. Also strips creatures that stopped being nemeses,
    /// heals Regenerating ones, and makes nemeses taunt you.
    /// </summary>
    internal class Bodies : MonoBehaviour
    {
        internal static Bodies Instance;
        private void Awake() => Instance = this;

        /// <summary>Applies a creature's nemesis state immediately (used by the reveal at the moment it transforms).</summary>
        internal void TendNow(Character c)
        {
            try { if (c != null) Tend(c); }
            catch (Exception e) { Plugin.Log.LogWarning($"Body upkeep hiccup: {e.Message}"); }
        }

        private const float Interval = 0.5f;
        private float _next;
        private float _nextTauntAny;
        private readonly System.Random _rng = new System.Random();
        private readonly List<Character> _scratch = new List<Character>();

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Interval;
            if (ZNet.instance == null || ZNetScene.instance == null) return;

            try
            {
                UpdateFitTarget();
                var me = Player.m_localPlayer;
                PreviewTarget = Plugin.ColourPreview.Value && me != null ? GameInfo.NearestNemesis(me.transform.position, 40f) : null;
                _scratch.Clear();
                _scratch.AddRange(Character.GetAllCharacters());
                foreach (var c in _scratch)
                {
                    if (c == null || c is Player) continue;
                    Tend(c);
                }
                Taunts();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Body upkeep hiccup: {e.Message}"); }
        }

        /// <summary>The nemesis being fitted (the nearest one while fitting mode is on). Its AI is paused so it holds still.</summary>
        internal static Character FitTarget;

        /// <summary>The nemesis showing the colour-preview sliders (the nearest one while preview is on).</summary>
        internal static Character PreviewTarget;
        private readonly Dictionary<Character, BaseAI> _frozen = new Dictionary<Character, BaseAI>();
        private static System.Reflection.MethodInfo _setMoveDir;

        /// <summary>
        /// While fitting: the nearest nemesis is the one being fitted, and every creature within 40 m that this game
        /// controls holds still (AI paused, feet stopped), so nothing wanders off or interrupts. All back on when you stop.
        /// </summary>
        private void UpdateFitTarget()
        {
            var p = Player.m_localPlayer;
            bool on = Plugin.FitMode.Value && p != null;
            Character want = on ? GameInfo.NearestNemesis(p.transform.position, 25f) : null;
            if (want != FitTarget)
            {
                // The old target goes back to normal; the new one is held in its neutral pose.
                if (FitTarget != null) { var old = FitTarget.GetComponent<RestPoser>(); if (old != null) Destroy(old); }
                FitTarget = want;
                if (want != null)
                {
                    if (want.GetComponent<RestPoser>() == null) want.gameObject.AddComponent<RestPoser>().Init(want);
                    Props.LoadSliders();
                }
            }

            if (!on)
            {
                Unfreeze();
                return;
            }
            if (_setMoveDir == null) _setMoveDir = HarmonyLib.AccessTools.Method(typeof(Character), "SetMoveDir");
            foreach (var c in GameInfo.CreaturesNear(p.transform.position, 40f))
            {
                if (!GameInfo.IsOwner(c)) continue;
                if (!_frozen.ContainsKey(c))
                {
                    var ai = c.GetComponent<BaseAI>();
                    if (ai == null) continue;
                    ai.enabled = false;
                    _frozen[c] = ai;
                }
                try { _setMoveDir?.Invoke(c, Effects.FillArgs(_setMoveDir, Vector3.zero)); } catch { }
            }
        }

        private void Unfreeze()
        {
            if (_frozen.Count == 0) return;
            foreach (var kv in _frozen) if (kv.Key != null && kv.Value != null) kv.Value.enabled = true;
            _frozen.Clear();
        }

        private void Tend(Character c)
        {
            if (GameInfo.Zdo(c) == null) return;
            var roster = Net.Known;
            string tag = GameInfo.TagOf(c);
            var body = c.GetComponent<NemesisBody>();

            // Freshly promoted but not yet tagged: the owner writes the tag (the server only knows its network id).
            if (tag.Length == 0 && roster != null && GameInfo.IsOwner(c))
            {
                string key = GameInfo.ZdoKey(c);
                if (key.Length > 0)
                {
                    var match = roster.Active.FirstOrDefault(n => n.Zdo == key);
                    if (match != null && !c.IsDead())
                    {
                        var zdo = GameInfo.Zdo(c);
                        zdo.Set(GameInfo.KeyId, match.Id);
                        zdo.Set(GameInfo.KeyBaseLevel, match.BaseLevel.ToString());
                        zdo.Set(GameInfo.KeyGen, match.BodyGen.ToString());
                        tag = match.Id;
                        Plugin.DebugLog($"Tagged {c.name} as {match.FullName}.");
                    }
                }
            }

            if (tag.Length == 0)
            {
                if (body != null) body.Strip();
                return;
            }

            var living = roster?.FindLiving(tag);
            if (living != null && !living.IsActive) return; // recovering: a body coming back before the server has confirmed it
            var record = living;
            // An old body of a nemesis that has since moved into a new one (came back, joined a raid): let it go.
            if (record != null && GameInfo.GenOf(c) < record.BodyGen) record = null;
            if (record == null)
            {
                // Only strip once we actually know the roster; before that (still joining) leave it be.
                if (!Net.HaveRoster) return;
                if (body != null) body.Strip();
                if (GameInfo.IsOwner(c))
                {
                    var zdo = GameInfo.Zdo(c);
                    int.TryParse(zdo.GetString(GameInfo.KeyBaseLevel, "1"), out int baseLevel);
                    zdo.Set(GameInfo.KeyId, "");
                    if (baseLevel >= 1 && c.GetLevel() != baseLevel) c.SetLevel(baseLevel);
                    Plugin.DebugLog($"{c.name} is no longer a nemesis.");
                }
                return;
            }

            // Just promoted, and the reveal camera isn't on it yet: keep the old look a moment longer.
            if (body == null && Reveal.IsHeld(record.Id)) return;

            if (body == null) body = c.gameObject.AddComponent<NemesisBody>();
            body.Wear(c, record, Net.RosterVersion);
            body.TryRestoreQuietly();

            int propsVersion = Props.Refresh();
            bool fitting = Plugin.FitMode.Value && FitTarget == c;
            var keys = !Plugin.PropsEnabled.Value ? new List<string>() : fitting ? Props.FittingSet() : Props.WithoutClashes(Rules.PropsFor(record), record.Species);
            body.SyncProps(c, keys, propsVersion, fitting);

            if (GameInfo.IsOwner(c))
            {
                int level = record.EffectiveLevel + GameInfo.BonusOf(c);
                if (c.GetLevel() != level && !c.IsDead())
                {
                    bool up = level > c.GetLevel();
                    c.SetLevel(level);
                    if (up) c.SetHealth(c.GetMaxHealth()); // it rises renewed
                }
                if (record.Traits.Contains(Traits.Regenerating) && !c.IsDead())
                {
                    float rate = GameInfo.IsAlerted(c) ? Plugin.RegenInCombat.Value : Plugin.RegenPerSecond.Value;
                    if (rate > 0f && c.GetHealth() < c.GetMaxHealth()) c.Heal(c.GetMaxHealth() * rate * Interval, false);
                }
            }
        }

        private void Taunts()
        {
            if (!Plugin.TauntsEnabled.Value || Time.time < _nextTauntAny) return;
            var p = Player.m_localPlayer;
            if (p == null || p.IsDead() || Net.Known == null) return;

            foreach (var c in GameInfo.CreaturesNear(p.transform.position, Plugin.TauntDistance.Value))
            {
                var body = c.GetComponent<NemesisBody>();
                if (body == null || body.Record == null) continue;
                if (Time.time < body.NextTaunt) continue;
                float d = Vector3.Distance(c.transform.position, p.transform.position);
                if (d > 12f && !GameInfo.IsAlerted(c)) continue;
                Say(c, body, p);
                return;
            }
        }

        private static string OnSightEmotes(Nemesis n)
        {
            return Plugin.PersonaValue(n, "emotes-on-sight");
        }

        internal void Say(Character c, NemesisBody body, Player p)
        {
            string line = Server.Rules.Taunt(body.Record, p.GetPlayerID(), p.GetPlayerName(), _rng);
            body.NextTaunt = Time.time + Plugin.TauntCooldown.Value;
            _nextTauntAny = Time.time + 6f;
            body.SayLine(line);

            // Sometimes with a gesture. Not in the middle of a melee: it would stop to point at you while you hit it.
            float d = Vector3.Distance(c.transform.position, p.transform.position);
            if (Plugin.OnSightEmotes.Value && d > 5f && _rng.NextDouble() < Plugin.OnSightEmoteChance.Value
                && BorrowedEmote.TryPlay(c, OnSightEmotes(body.Record), out float length, out Action restore))
                body.DeferRestore(restore, length);
        }
    }

    /// <summary>
    /// Sits on a creature that is a nemesis. Remembers what it was like before, so it can be put back.
    /// Every game that has the creature loaded applies this itself; only names, numbers and colours change.
    /// </summary>
    internal class NemesisBody : MonoBehaviour
    {
        internal Nemesis Record;
        internal float NextTaunt;
        internal string OriginalName;

        private Character _c;
        private int _wornVersion = -1;
        private bool _captured;
        private HitData.DamageModifiers _origMods;
        private float _origSpeed, _origRun, _origWalk;
        private Vector3 _origScale;
        private float _origView, _origHear, _origChase, _origFlee;
        private bool _origAfraidOfFire;
        private Action _pendingRestore;
        private float _restoreNotBefore;
        private BaseAI _ai;
        private MonsterAI _monster;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly Dictionary<Renderer, Vector3> _origHsv = new Dictionary<Renderer, Vector3>();
        private MaterialPropertyBlock _block;
        private bool _hsv;

        internal bool Has(string trait) => Record != null && Record.Traits.Contains(trait);

        private void Capture(Character c)
        {
            if (_captured) return;
            _captured = true;
            _c = c;
            OriginalName = c.m_name;
            _origMods = c.m_damageModifiers;
            _origSpeed = c.m_speed; _origRun = c.m_runSpeed; _origWalk = c.m_walkSpeed;
            _origScale = c.transform.localScale;
            _ai = c.GetComponent<BaseAI>();
            _monster = c.GetComponent<MonsterAI>();
            if (_ai != null) { _origView = _ai.m_viewRange; _origHear = _ai.m_hearRange; _origAfraidOfFire = _ai.m_afraidOfFire; }
            if (_monster != null) { _origChase = _monster.m_maxChaseDistance; _origFlee = _monster.m_fleeIfLowHealth; }

            _block = new MaterialPropertyBlock();
            foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) _renderers.Add(r);
            _hsv = _renderers.Any(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Hue"));
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_block);
                _origHsv[r] = new Vector3(_block.GetFloat("_Hue"), _block.GetFloat("_Saturation"), _block.GetFloat("_Value"));
            }
        }

        /// <summary>Applies the record. Cheap to call often: only does work when something changed.</summary>
        internal void Wear(Character c, Nemesis record, int version)
        {
            Capture(c);
            Record = record;
            bool preview = Bodies.PreviewTarget == c;
            string lookKey = $"{version}|{Plugin.Looks.Version}|{preview}|{(preview ? Plugin.PreviewHue.Value + "/" + Plugin.PreviewSaturation.Value + "/" + Plugin.PreviewBrightness.Value : "")}|{Plugin.SizeMin.Value}|{Plugin.SizeMax.Value}|{Plugin.Scale.Value}|{Plugin.ChampionScale.Value}|{Plugin.Tint.Value}|{Plugin.TintHue.Value}|{Plugin.TintSaturation.Value}|{Plugin.TintBrightness.Value}" +
                             $"|{Plugin.SwiftSpeed.Value}|{Plugin.CowardFleeHealth.Value}|{Plugin.RelentlessRange.Value}";
            int key = lookKey.GetHashCode();
            if (key == _wornVersion) return;
            _wornVersion = key;

            c.m_name = record.Champion ? $"{record.FullName} (Champion)" : record.FullName;

            var mods = _origMods;
            SetMod(ref mods, record.Weakness, HitData.DamageModifier.Weak);
            SetMod(ref mods, record.Strength, HitData.DamageModifier.Resistant);
            // Scars: what nearly killed it, it now shrugs off (or, for fire, sometimes fears).
            foreach (var scar in record.Scars)
                if (scar.Effect == "Resist") SetMod(ref mods, scar.Type, HitData.DamageModifier.Resistant);
            c.m_damageModifiers = mods;

            float speed = Has(Traits.Swift) ? Plugin.SwiftSpeed.Value : 1f;
            c.m_speed = _origSpeed * speed; c.m_runSpeed = _origRun * speed; c.m_walkSpeed = _origWalk * speed;

            if (_ai != null)
            {
                float range = Has(Traits.Relentless) ? Plugin.RelentlessRange.Value : 1f;
                _ai.m_viewRange = _origView * range;
                _ai.m_hearRange = _origHear * range;
                _ai.m_afraidOfFire = _origAfraidOfFire || record.Scars.Any(x => x.Type == "Fire" && x.Effect == "Fear");
            }
            if (_monster != null)
            {
                _monster.m_maxChaseDistance = Has(Traits.Relentless) ? _origChase * Plugin.RelentlessRange.Value * 2f : _origChase;
                _monster.m_fleeIfLowHealth = Has(Traits.Coward) ? Mathf.Max(_origFlee, Plugin.CowardFleeHealth.Value) : _origFlee;
            }

            c.transform.localScale = _origScale * Plugin.SizeFor(record) * (record.Champion ? Plugin.ChampionScale.Value : 1f);
            ApplyTint(Plugin.Tint.Value);
            Plugin.DebugLog($"{OriginalName} now wears {record.FullName} (level {record.Level}).");
        }

        /// <summary>Back to an ordinary creature.</summary>
        // ---- Props ------------------------------------------------------------------------

        private readonly Dictionary<string, GameObject> _props = new Dictionary<string, GameObject>();
        private int _propsVersion = int.MinValue;
        private string _propsSet = "";
        private bool _fitting;

        /// <summary>Makes sure exactly these props are on it, placed by their current fits.</summary>
        internal void SyncProps(Character c, List<string> keys, int version, bool fitting)
        {
            _fitting = fitting;
            string set = string.Join(",", keys);
            if (set == _propsSet && version == _propsVersion && !fitting) return;
            if (version != _propsVersion) ClearProps(); // an item or bone may have changed: make them afresh
            if (set != _propsSet)
            {
                foreach (var kv in _props.Where(kv => !keys.Contains(kv.Key)).ToList())
                {
                    if (kv.Value != null) Destroy(kv.Value);
                    _props.Remove(kv.Key);
                }
                foreach (var k in keys.Where(k => !_props.ContainsKey(k)))
                {
                    var go = Props.Make(c, k);
                    if (go != null) _props[k] = go;
                }
                _propsSet = set;
            }
            _propsVersion = version;
            PlaceProps();
        }

        private void PlaceProps()
        {
            string species = GameInfo.Prefab(_c);
            foreach (var kv in _props)
            {
                if (kv.Value == null) continue;
                var fitter = kv.Value.GetComponent<PropFitter>();
                if (fitter == null) continue;
                Fit f = _fitting && kv.Key == Props.FittingKey ? Props.SliderFit() : Props.Fits.For(species, kv.Key);
                fitter.Apply(_fitting ? f : Jitter(f, kv.Key));
            }
        }

        /// <summary>Arrows don't all go in at the same angle: a small turn and tilt, the same every time for this nemesis.</summary>
        private Fit Jitter(Fit f, string key)
        {
            if (!key.StartsWith("arrow") || Record == null) return f;
            int h = (Record.Id + key).GetStableHashCode();
            float a = ((h & 0xFF) / 255f - 0.5f) * 16f, b = (((h >> 8) & 0xFF) / 255f - 0.5f) * 16f;
            return new Fit { Up = f.Up, Forward = f.Forward, Side = f.Side, Tilt = f.Tilt + a, Turn = f.Turn + b, Roll = f.Roll, Size = f.Size };
        }

        private void Update()
        {
            if (_fitting && _props.Count > 0) PlaceProps(); // sliders move live while fitting
        }

        private void ClearProps()
        {
            foreach (var go in _props.Values) if (go != null) Destroy(go);
            _props.Clear();
            _propsSet = "";
        }

        internal void Strip()
        {
            ClearProps();
            if (_captured && _c != null)
            {
                _c.m_name = OriginalName;
                _c.m_damageModifiers = _origMods;
                _c.m_speed = _origSpeed; _c.m_runSpeed = _origRun; _c.m_walkSpeed = _origWalk;
                _c.transform.localScale = _origScale;
                if (_ai != null) { _ai.m_viewRange = _origView; _ai.m_hearRange = _origHear; _ai.m_afraidOfFire = _origAfraidOfFire; }
                RestoreNow();
                if (_monster != null) { _monster.m_maxChaseDistance = _origChase; _monster.m_fleeIfLowHealth = _origFlee; }
                ApplyTint(false);
            }
            Destroy(this);
        }

        private void ApplyTint(bool on)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                var o = _origHsv.TryGetValue(r, out var v) ? v : Vector3.zero;
                if (_hsv)
                {
                    bool preview = Bodies.PreviewTarget == _c;
                    bool fromScar = false;
                    Vector3 t = preview ? new Vector3(Plugin.PreviewHue.Value, Plugin.PreviewSaturation.Value, Plugin.PreviewBrightness.Value)
                                        : SkinTint(out fromScar);
                    // The preview shows the numbers exactly as a fresh nemesis with that palette would look (no scar darkening, no Champion boost).
                    float dark = preview || fromScar ? 0f : ScarDarkening(); // a scar skin is its own darkness
                    float champ = !preview && Record != null && Record.Champion ? 0.2f : 0f;
                    _block.SetFloat("_Hue", on ? o.x + t.x : o.x);
                    _block.SetFloat("_Saturation", on ? o.y + t.y + champ : o.y);
                    _block.SetFloat("_Value", on ? o.z + t.z - dark : o.z);
                }
                else if (on)
                {
                    // Shader without hue controls: a plain reddish tint instead.
                    _block.SetColor("_Color", Color.Lerp(Color.white, new Color(0.75f, 0.45f, 0.4f), 0.6f));
                }
                else
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                r.SetPropertyBlock(_block);
            }
        }

        private static void SetMod(ref HitData.DamageModifiers m, string type, HitData.DamageModifier value)
        {
            switch (type)
            {
                case "Blunt": m.m_blunt = value; break;
                case "Slash": m.m_slash = value; break;
                case "Pierce": m.m_pierce = value; break;
                case "Fire": m.m_fire = value; break;
                case "Frost": m.m_frost = value; break;
                case "Lightning": m.m_lightning = value; break;
                case "Poison": m.m_poison = value; break;
                case "Spirit": m.m_spirit = value; break;
            }
        }

        /// <summary>
        /// The skin shift to use: its most recent scar's look ("Scar Skin"), or the normal nemesis tint when that
        /// scar type has none (or it has no scars).
        /// </summary>
        private Vector3 SkinTint(out bool fromScar)
        {
            fromScar = false;
            string type = Rules.SkinScar(Record);
            string species = Record != null ? Record.Species : null;
            if (type.Length > 0 && KeyValueBook.TryHsv(Plugin.Looks.Get("scar-skin", type, species), out float h, out float sat, out float v))
            {
                fromScar = true;
                return new Vector3(h, sat, v);
            }
            // Its own palette, or the fallback tint if it has none (or the palette was removed from the looks file).
            if (Record != null && Plugin.TryPalette(Record.Palette, species, out Vector3 pal)) return pal;
            return new Vector3(Plugin.TintHue.Value, Plugin.TintSaturation.Value, Plugin.TintBrightness.Value);
        }

        /// <summary>The colour shift it wears normally (its scar skin or palette), for loading into the preview sliders.</summary>
        internal Vector3 CurrentTint(out bool fromScar) => SkinTint(out fromScar);

        /// <summary>Each scar darkens it a little; fire scars char it properly.</summary>
        private float ScarDarkening()
        {
            if (Record == null) return 0f;
            float d = 0f;
            foreach (var s in Record.Scars) d += s.Type == "Fire" ? 0.15f : 0.06f;
            return Mathf.Min(d, 0.4f);
        }

        // ---- Borrowed emotes -----------------------------------------------------------

        /// <summary>
        /// Takes over putting its own animations back after an emote. Swapping them back resets its pose on the spot,
        /// so it waits until nobody can see it, or until it's hit (the flinch hides the snap), or a minute at most.
        /// </summary>
        internal void DeferRestore(Action restore, float emoteLength = 0f)
        {
            if (restore == null) return;
            RestoreNow();
            _pendingRestore = restore;
            _restoreNotBefore = Time.time + emoteLength + 0.5f;
            _restoreDeadline = Time.time + 60f;
        }

        private float _restoreDeadline;

        internal void TryRestoreQuietly()
        {
            if (_pendingRestore == null || Time.time < _restoreNotBefore) return;
            bool seen = _renderers.Any(r => r != null && r.isVisible);
            if (!seen || Time.time > _restoreDeadline) RestoreNow();
        }

        internal void RestoreNow()
        {
            var r = _pendingRestore;
            _pendingRestore = null;
            try { r?.Invoke(); } catch { /* creature gone */ }
        }

        internal void SayLine(string text)
        {
            if (Chat.instance == null || _c == null || string.IsNullOrEmpty(text)) return;
            float height = 2.2f * (Record != null ? Plugin.SizeFor(Record) : Plugin.Scale.Value) * Mathf.Max(0.5f, _origScale.y);
            Chat.instance.SetNpcText(gameObject, Vector3.up * height, 30f, Plugin.BubbleSeconds.Value, Record?.FullName ?? "", text, false);
        }
    }
}

namespace Grudges
{
    /// <summary>
    /// Holds a creature in its neutral pose (the same pose prop placements are measured from) while it's being
    /// fitted: its animation is switched off and every bone is set to match the game's un-animated template of that
    /// creature, every frame (so head-turning and the like can't move it). Removing this puts everything back.
    /// </summary>
    internal class RestPoser : MonoBehaviour
    {
        private Animator _anim;
        private bool _animWasOn;
        private readonly List<Transform> _bones = new List<Transform>();
        private readonly List<Vector3> _pos = new List<Vector3>();
        private readonly List<Quaternion> _rot = new List<Quaternion>();

        public void Init(Character c)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(GameInfo.Prefab(c)) : null;
            if (prefab == null) { Plugin.Log.LogInfo("Fitting: couldn't find the creature's template, so it stays in its current pose."); return; }

            _anim = c.GetComponentInChildren<Animator>();
            if (_anim != null) { _animWasOn = _anim.enabled; _anim.enabled = false; }

            // Every transform under the creature that also exists, at the same path, in the template.
            var stack = new Stack<KeyValuePair<Transform, string>>();
            for (int i = 0; i < c.transform.childCount; i++) stack.Push(new KeyValuePair<Transform, string>(c.transform.GetChild(i), c.transform.GetChild(i).name));
            while (stack.Count > 0)
            {
                var kv = stack.Pop();
                var live = kv.Key;
                if (live.name.StartsWith("Grudges_")) continue; // our own props and auras
                var rest = prefab.transform.Find(kv.Value);
                if (rest != null)
                {
                    _bones.Add(live);
                    _pos.Add(rest.localPosition);
                    _rot.Add(rest.localRotation);
                }
                for (int i = 0; i < live.childCount; i++)
                    stack.Push(new KeyValuePair<Transform, string>(live.GetChild(i), kv.Value + "/" + live.GetChild(i).name));
            }
            Plugin.DebugLog($"Fitting: holding {GameInfo.Prefab(c)} in its neutral pose ({_bones.Count} parts).");
            LateUpdate();
        }

        private void LateUpdate()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                var b = _bones[i];
                if (b == null) continue;
                b.localPosition = _pos[i];
                b.localRotation = _rot[i];
            }
        }

        private void OnDestroy()
        {
            if (_anim != null) _anim.enabled = true; // back to normal animation
        }
    }
}
