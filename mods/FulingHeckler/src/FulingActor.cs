using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>Which routine he performs.</summary>
    internal enum SceneKind { Heckle, Lurker, RedLight, Tombstone, Copycat, Campfire, CoinThief, Gifter }

    /// <summary>What the Fuling should do on this visit.</summary>
    internal class Performance
    {
        public SceneKind Scene = SceneKind.Heckle;
        public Vector3? SpawnAt;     // scenes pick their own spot; null = in front of the player
        public Vector3? FacePoint;   // something to face when he arrives (the campfire)
        public Quaternion? SpawnRotation;   // which way he faces where he appears (a seat: the way it faces)
        public string SeatPose;      // sitting on a seat by the fire: the player animation for it (instead of 'Campfire Poses')
        public string Line;
        public bool IsMilestone;
        public List<string> DeathChain;   // what killed the player (null for tests = "generic")
        public bool EmoteTest;       // play the player-emote experiment instead of a taunt
        public bool? ForceTorch;     // test buttons can force the torch on/off, the Campfire Squatter never brings one (he has a fire); null = night + chance
        public string ForcedEmote;   // "Test next emote" button: perform exactly this emote, no dice
        public string ForcedHelmet;  // wear exactly this, no dice
        public bool ForceOutfit;     // wear every outfit piece
        public bool Fitting;         // hat fitting mode: walk up, stand still, wait for "Done"
        public Action<string> FallbackToText;

        // Puppets: another player's goblin, drawn here and driven by their messages (see FulingNet.cs).
        public bool Puppet;
        public string PuppetHelmet;            // exact helmet ("" = none)
        public List<string> PuppetOutfit;      // exact outfit pieces
        public bool PuppetTorch;
    }

    /// <summary>
    /// A local-only goblin that runs up to the player, heckles them in a speech bubble, and runs off.
    ///
    /// SAFETY: this never spawns the real Fuling creature. It copies only the visual model (meshes +
    /// animator) from the game's Fuling prefab, while that copy is inactive, then strips every script,
    /// collider and rigidbody off it before switching it on. No ZNetView = nothing is sent to the
    /// server, nobody else sees it, and it has no AI and can't hurt anyone.
    /// </summary>
    internal partial class FulingActor : MonoBehaviour
    {
        private const float SpawnDistance = 18f;     // where he appears, metres from the player
        private const float RunSpeed = 6f;           // metres per second
        private const float LeaveDistance = 28f;     // how far he runs before vanishing
        private const float MaxLifetime = 60f;       // hard cap, in case anything goes wrong
        private const float BubbleHeight = 2.1f;     // metres above his feet
        private const float BubbleCullDistance = 30f;
        private const float TurnSpeed = 540f;        // degrees per second

        private static FulingActor _current;
        private static bool _loggedAnimatorParams;
        private static GameObject _staging;
        private static readonly System.Random Rng = new System.Random();

        private Animator _animator;
        private readonly HashSet<string> _animParams = new HashSet<string>();
        private Performance _show;
        private EmotePlayer _emotes;
        private bool _hasTorch;

        // Kept so gear can be swapped while he's standing there (hat fitting).
        private GameObject _prefab;
        private Transform _sourceVisual, _copyVisual;
        private SkinnedMeshRenderer _body;
        private GameObject _helmet;
        private HelmetFitter _fitter;
        private bool _endFitting;
        private SkinColor _skin;

        /// <summary>The Fuling currently on screen, if any.</summary>
        public static FulingActor Current => _current;
        public string HelmetName => _helmet != null ? _helmet.name : null;
        public bool IsFitting => _show != null && _show.Fitting && !_endFitting;

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        /// <summary>Spawns the heckler. Returns false (and logs why) if it can't, so the caller can use text.</summary>
        public static bool TrySpawn(Player player, Performance show)
        {
            try
            {
                if (player == null || ZNetScene.instance == null) return false;
                if (_current != null) Destroy(_current.gameObject); // one goblin at a time

                string prefabName = Look.Type;
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
                if (prefab == null)
                {
                    Plugin.Log.LogWarning($"No creature called '{prefabName}' in this game. Check 'Fuling Type' in the config.");
                    return false;
                }

                Vector3 spawn;
                if (show.SpawnAt.HasValue) spawn = show.SpawnAt.Value;
                else if (!FindSpawnPoint(player, out spawn))
                {
                    Plugin.DebugLog("No dry ground nearby for the Fuling; using text instead.");
                    return false;
                }

                Animator sourceAnim = prefab.GetComponentInChildren<Animator>(true);
                GameObject root = BuildVisualCopy(prefab, sourceAnim, spawn);
                if (root == null) return false;

                var actor = root.AddComponent<FulingActor>();
                actor._show = show;
                actor._prefab = prefab;
                actor._sourceVisual = sourceAnim.transform;
                actor._copyVisual = root.transform.Find("Visual");
                // Remember his own body parts before any gear goes on, so recolouring skips the gear.
                actor._skin = new SkinColor(actor._copyVisual.GetComponentsInChildren<Renderer>(true));
                actor._skin.Apply();
                actor.DressUp();
                actor.InitAnimator();
                _current = actor;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't summon the Fuling ({e.GetType().Name}: {e.Message}). Using text instead.");
                return false;
            }
        }

        private static GameObject BuildVisualCopy(GameObject prefab, Animator sourceAnim, Vector3 position)
        {
            if (sourceAnim == null)
            {
                Plugin.Log.LogWarning($"'{prefab.name}' has no animated model to borrow.");
                return null;
            }

            // Copy under an inactive parent: inactive objects never run their scripts' Awake/Start,
            // so the game's own code on the model never gets a chance to do anything.
            if (_staging == null)
            {
                _staging = new GameObject("FulingHeckler_Staging");
                _staging.SetActive(false);
                DontDestroyOnLoad(_staging);
            }

            GameObject visual = Instantiate(sourceAnim.gameObject, _staging.transform, false);
            visual.name = "Visual";
            Gear.Strip(visual);

            if (visual.GetComponentInChildren<ZNetView>(true) != null)
            {
                Plugin.Log.LogError("Safety check failed: the Fuling copy still had network components. Not spawning.");
                DestroyImmediate(visual);
                return null;
            }

            var root = new GameObject("FulingHeckler_Fuling");
            root.transform.position = position;
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            return root;
        }

        /// <summary>Helmet sometimes, outfit pieces sometimes, torch sometimes at night. Otherwise he stays naked.</summary>
        private void DressUp()
        {
            Gear.LogOwnGearOnce(_prefab);
            _body = Gear.FindBodyOnCopy(_prefab, _sourceVisual, _copyVisual);

            if (_show.Puppet) { DressAsTold(); return; }

            // Head gear
            string pick = _show.ForcedHelmet;
            if (pick == null && Rng.NextDouble() < Look.HelmetChance)
            {
                var options = NameList.Parse(Look.Helmets);
                if (options.Count > 0) pick = options[Rng.Next(options.Count)];
            }
            if (pick != null) WearHelmet(pick, _show.ForcedHelmet != null);

            // Goblin armour pieces, each with its own chance
            foreach (var piece in NameList.Parse(Look.OutfitItems))
            {
                if (!_show.ForceOutfit && Rng.NextDouble() >= Look.OutfitPieceChance) continue;
                if (Gear.TryAttach(piece, null, _body, _copyVisual, 1f, out string why)) { _outfitWorn.Add(piece); Plugin.DebugLog($"Fuling is wearing {piece}."); }
                else Plugin.Log.LogInfo($"Fuling couldn't wear {piece}: {why}");
            }

            // Torch at night
            bool torch = _show.ForceTorch ?? (IsNight() && Rng.NextDouble() < Look.TorchAtNightChance);
            if (torch)
            {
                Transform hand = Gear.FindSlotOnCopy(_prefab, _sourceVisual, _copyVisual, "m_rightHand");
                if (Gear.TryAttach(Look.TorchItem, hand, _body, _copyVisual, Look.HeldItemSize, out string why))
                {
                    Plugin.DebugLog("Fuling brought a torch.");
                    _hasTorch = true;
                }
                else Plugin.Log.LogInfo($"Fuling couldn't hold {Look.TorchItem}: {why}");
            }
        }

        /// <summary>Puts a helmet on (replacing any current one) and applies its saved fit.</summary>
        public bool WearHelmet(string name, bool logResult)
        {
            if (_helmet != null) { Destroy(_helmet); _helmet = null; _fitter = null; }
            _helmetItem = null;

            Transform head = Gear.FindSlotOnCopy(_prefab, _sourceVisual, _copyVisual, "m_helmet");
            bool ok = Gear.TryAttach(name, head, _body, _copyVisual, 1f, out string why, out GameObject copy, out Vector3 itemScale);
            if (ok)
            {
                _helmet = copy;
                _helmetItem = name;
                // Rigid helmets (attached to the head bone) can be fitted; bendy ones follow the body on their own.
                if (head != null && copy.transform.parent == head)
                {
                    _fitter = copy.AddComponent<HelmetFitter>();
                    _fitter.ItemName = name;
                    _fitter.Init(transform, head, itemScale);
                    _fitter.Apply((_show.Fitting ? Plugin.SavedFits() : Look.Fits()).For(name)); // fitting = your own fits
                }
            }

            if (logResult) Plugin.Log.LogInfo(ok ? $"Helmet: wearing {name}{(_fitter != null ? "" : " (bends with the body, no fitting needed)")}." : $"Helmet: couldn't wear {name}: {why}");
            else if (!ok) Plugin.Log.LogInfo($"Fuling couldn't wear {name}: {why}");
            return ok;
        }

        /// <summary>Ends hat fitting: he runs off.</summary>
        public void EndFitting() => _endFitting = true;

        private void Update()
        {
            _skin?.Apply(); // live: changes to the colour sliders show immediately

            // Hat fitting: the sliders move the helmet live.
            if (IsFitting && _fitter != null) _fitter.Apply(Plugin.SliderFit());
        }

        internal static bool IsNight()
        {
            try
            {
                return EnvMan.instance != null && EnvMan.IsNight();
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        // Where he comes from
        // ------------------------------------------------------------------

        private static bool FindSpawnPoint(Player player, out Vector3 spawn) => PickSpot(player, SpawnCandidates(player), out spawn);

        /// <summary>In front of the player first, widening out, so you see him coming.</summary>
        private static IEnumerable<Vector3> SpawnCandidates(Player player)
        {
            Vector3 origin = player.transform.position;
            Vector3 look = ViewForward(player);
            float[] angles = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 110f, -110f };
            float[] distances = { SpawnDistance, SpawnDistance * 0.6f };
            foreach (float dist in distances)
                foreach (float a in angles)
                    yield return origin + Quaternion.Euler(0f, a + UnityEngine.Random.Range(-10f, 10f), 0f) * look * dist;
        }

        private static Vector3 ViewForward(Player player)
        {
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 f = cam != null ? cam.forward : player.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
        }

        private static bool IsUnderwater(Vector3 p)
        {
            float waterLevel = 30f;
            try
            {
                if (ZoneSystem.instance != null)
                    waterLevel = ZoneSystem.instance.m_waterLevel;
            }
            catch { /* default sea level */ }
            return p.y < waterLevel - 0.3f;
        }

        // ------------------------------------------------------------------
        // The performance
        // ------------------------------------------------------------------

        private bool _animReady;

        /// <summary>Sets up his animator. Done right after he's built (not a frame later), so the first moves aren't lost.</summary>
        private void InitAnimator()
        {
            if (_animReady) return;
            _animReady = true;
            _animator = GetComponentInChildren<Animator>(true);
            if (_animator != null)
            {
                _animator.applyRootMotion = false;           // we move him, not his animations
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                try { _animator.fireEvents = false; } catch { } // his footstep/attack events have no listeners now
                foreach (var prm in _animator.parameters) _animParams.Add(prm.name);
                _emotes = new EmotePlayer(_animator); // can borrow player animations later
                if (_hasTorch && Plugin.TorchPose.Value)
                    _emotes.SetTorchPose(Plugin.TorchRunAnimation.Value, Plugin.TorchIdleAnimation.Value);

                if (!_loggedAnimatorParams)
                {
                    _loggedAnimatorParams = true;
                    var list = string.Join(", ", _animator.parameters.Select(x => $"{x.name} ({x.type})").ToArray());
                    Plugin.Log.LogInfo($"Fuling animator parameters: {list}");
                }
            }
        }

        private void Start()
        {
            InitAnimator();

            if (_show.Puppet) { StartPuppet(); return; }
            if (!_show.Fitting) Destroy(gameObject, _show.Scene == SceneKind.Heckle ? MaxLifetime : 240f);
            StartBroadcasting();
            switch (_show.Scene)
            {
                case SceneKind.Lurker: StartCoroutine(Lurk()); break;        // sneaky: no puff on arrival
                case SceneKind.RedLight: StartCoroutine(RedLight()); break;
                case SceneKind.Tombstone: StartCoroutine(TombstoneDance()); break;
                case SceneKind.Campfire: StartCoroutine(CampfireSquat()); break;      // already sitting there
                case SceneKind.Copycat:
                    PoofHere(Plugin.ArrivalEffects.Value);
                    StartCoroutine(Copycat());
                    break;
                case SceneKind.Gifter: StartCoroutine(Gifter()); break;   // sneaky: no puff, no yell
                case SceneKind.CoinThief:
                    PoofHere(Plugin.ArrivalEffects.Value);
                    Sfx(SoundMoment.Arrive);
                    StartCoroutine(CoinThief());
                    break;
                default:
                    PoofHere(Plugin.ArrivalEffects.Value); // he arrives in a puff of smoke
                    Sfx(SoundMoment.Arrive);
                    StartCoroutine(Perform());
                    break;
            }
        }

        private bool _talkDone;
        private bool _playingDead;

        private IEnumerator Perform()
        {
            var player = Player.m_localPlayer;
            if (player == null) { Destroy(gameObject); yield break; }

            // 1. Run in, stopping somewhere in the configured range from the player.
            float min = Mathf.Min(Plugin.StopDistanceMin.Value, Plugin.StopDistanceMax.Value);
            float max = Mathf.Max(Plugin.StopDistanceMin.Value, Plugin.StopDistanceMax.Value);
            float stopAt = _show.Fitting ? 3f : UnityEngine.Random.Range(min, max);

            float timeout = 10f;
            while (timeout > 0f)
            {
                player = Player.m_localPlayer;
                if (player == null) { Destroy(gameObject); yield break; }

                Vector3 toPlayer = Flat(player.transform.position - transform.position);
                float dist = toPlayer.magnitude;
                // A little tolerance: aiming for exactly stopAt left him "running" microscopic steps forever.
                if (dist <= stopAt + 0.15f) break;

                MoveToward(toPlayer.normalized, Mathf.Min(RunSpeed * Time.deltaTime, dist - stopAt), RunSpeed, player.transform.position);
                timeout -= Time.deltaTime;
                yield return null;
            }

            // Hat fitting: stand still facing the player until "Done".
            if (_show.Fitting)
            {
                SetSpeed(0f);
                TrySayLine("{me} stand still. Make {me} pretty!", 4f);
                while (!_endFitting)
                {
                    var p = Player.m_localPlayer;
                    if (p == null) { Destroy(gameObject); yield break; }
                    FaceDirection(Flat(p.transform.position - transform.position));
                    yield return null;
                }
                TrySayLine("{me} look GOOD.", 2.5f);
                _talkDone = true;
            }

            // 2. Talk, unless the player gets too close, in which case he bolts.
            if (!_show.Fitting) _talkDone = false;
            Coroutine talk = _show.Fitting ? null : StartCoroutine(Talk());
            bool fled = false;
            while (!_talkDone)
            {
                player = Player.m_localPlayer;
                float personal = Plugin.PersonalSpace.Value;
                bool someoneClose = player != null && personal > 0f &&
                    Flat(player.transform.position - transform.position).magnitude < personal;
                if (!_show.Fitting && RemoteSaw(_pokeClose)) someoneClose = true; // another player walked up to him
                if (someoneClose)
                {
                    if (talk != null) StopCoroutine(talk);
                    fled = true;
                    break;
                }
                yield return null;
            }

            float speed = RunSpeed;
            if (fled)
            {
                // "Gotta go!" and he's off, faster than he came.
                if (_playingDead) { SetBool("dead", false); _playingDead = false; }
                RestoreEmote();
                string line = FleeLine();
                TrySayLine(line, 2.5f);
                Sfx(SoundMoment.Flee);
                Plugin.Log.LogInfo($"Fuling fled: player got too close. Said: {line}");
                speed = RunSpeed * 1.5f;
            }

            // 3. Run off, roughly away from the player, then vanish.
            player = Player.m_localPlayer;
            Vector3 away = player != null ? Flat(transform.position - player.transform.position) : transform.forward;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away = Quaternion.Euler(0f, UnityEngine.Random.Range(-35f, 35f), 0f) * away.normalized;

            // With the poof on, he only runs a short way (so you see him vanish); otherwise he runs out of sight.
            float runFor = Plugin.PoofEnabled.Value ? Plugin.ExitRunDistance.Value : LeaveDistance;
            float run = 0f;
            while (run < runFor)
            {
                float step = speed * Time.deltaTime;
                MoveToward(away, step, speed);
                run += step;
                yield return null;
            }
            SfxAt(SoundMoment.Poof);
            PoofHere(Plugin.ExitEffects.Value);
            Destroy(gameObject);
        }

        private IEnumerator Talk()
        {
            SetSpeed(0f);
            if (Plugin.HopOnArrival.Value) SetTrigger("jump");
            if (!TrySayLine(_show.Line, Plugin.BubbleSeconds.Value)) _show.FallbackToText?.Invoke(_show.Line); // ShowLine fills {me} itself

            float bubble = Plugin.BubbleSeconds.Value;
            float elapsed = 0f;

            // Let the line land before doing anything else.
            yield return FaceThePlayerFor(1.5f);
            elapsed += 1.5f;

            // The bit: a test, play dead, an emote, one of his own moves, or nothing.
            string bit;
            if (_show.EmoteTest)
            {
                bit = "emote test";
                EmoteExperiment.Run(_animator, _emotes, Rng);
                yield return FaceThePlayerFor(3f);
                elapsed += 3f;
            }
            else if (_show.ForcedEmote != null)
            {
                var clip = PlayEmote(new[] { _show.ForcedEmote });
                Sfx(SoundMoment.Taunt);
                bit = clip != null ? $"emote '{clip.name}' (test)" : $"emote test: nothing matched '{_show.ForcedEmote}'";
                float hold = clip != null ? Mathf.Clamp(clip.length, 1.5f, 5f) : 1.5f;
                yield return FaceThePlayerFor(hold);
                elapsed += hold;
            }
            else if ((_show.IsMilestone && Plugin.PlayDeadOnMilestones.Value) || Rng.NextDouble() < Plugin.PlayDeadChance.Value)
            {
                // He acts out your death: an impression of how you died if there's one for it, otherwise he flops over.
                var moves = ImpressionBook.Parse(Plugin.DeathImpressions.Value).For(_show.DeathChain);
                var impression = moves.Count > 0 ? PlayEmote(moves) : null;
                Sfx(SoundMoment.Taunt);
                if (impression != null)
                {
                    bit = $"impression '{impression.name}'";
                    float hold = Mathf.Clamp(impression.length, 2f, 5f);
                    yield return new WaitForSeconds(hold);
                    RestoreEmote();
                    elapsed += hold;
                }
                else
                {
                    bit = "play dead";
                    _playingDead = true;
                    SetBool("dead", true);
                    yield return new WaitForSeconds(2.5f);
                    SetBool("dead", false);
                    _playingDead = false;
                    SetTrigger("wakeup");
                    yield return new WaitForSeconds(1.5f);
                    elapsed += 4f;
                }
            }
            else if (Rng.NextDouble() < Plugin.TauntChance.Value)
            {
                // A player emote (laugh, point...) if we can, otherwise one of his own moves.
                AnimationClip emote = null;
                bool wantEmote = Plugin.UsePlayerEmotes.Value && _emotes != null && Rng.NextDouble() < Plugin.EmoteShare.Value;
                if (wantEmote) emote = PlayEmote(NameList.Parse(Plugin.Emotes.Value));
                Sfx(SoundMoment.Taunt);

                if (emote != null) bit = $"emote '{emote.name}'";
                else
                {
                    RestoreEmote();
                    var taunts = new[] { "swing_longsword", "stagger", "spear_throw" }.Where(_animParams.Contains).ToArray();
                    string move = taunts.Length > 0 ? taunts[Rng.Next(taunts.Length)] : null;
                    if (move != null) SetTrigger(move);
                    bit = wantEmote ? $"own move '{move}' (no emote matched)" : $"own move '{move}'";
                }

                float hold = emote != null ? Mathf.Clamp(emote.length, 1.5f, 5f) : 1.5f;
                yield return FaceThePlayerFor(hold);
                elapsed += hold;
            }
            else bit = "nothing (taunt roll failed)";

            Plugin.Log.LogInfo($"Fuling's bit this visit: {bit}.");

            // Keep facing the player until the bubble fades.
            if (elapsed < bubble) yield return FaceThePlayerFor(bubble - elapsed);
            _talkDone = true;
        }

        private static readonly string[] BuiltInFleeLines =
            { "Gotta go!", "Too close! {me} go!", "Personal space! Bye!", "NOPE!", "{me} not touch you! Bye bye!" };

        private static string FleeLine()
        {
            var lines = Plugin.Quips != null ? Plugin.Quips.Lines.Where(q => q.Event == "flee").Select(q => q.Text).ToList() : null;
            if (lines == null || lines.Count == 0) lines = BuiltInFleeLines.ToList();
            return lines[Rng.Next(lines.Count)];
        }

        private IEnumerator FaceThePlayerFor(float seconds)
        {
            while (seconds > 0f)
            {
                var p = Player.m_localPlayer;
                if (p != null) FaceDirection(Flat(p.transform.position - transform.position));
                seconds -= Time.deltaTime;
                yield return null;
            }
        }

        private void FaceDirection(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * Time.deltaTime);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // Only touch animator parameters that actually exist on this model.
        private void SetSpeed(float metresPerSecond)
        {
            _speedNow = metresPerSecond;
            if (_animator != null && _animParams.Contains("forward_speed")) _animator.SetFloat("forward_speed", metresPerSecond);
        }

        private void SetBool(string name, bool value)
        {
            if (_animator != null && _animParams.Contains(name)) _animator.SetBool(name, value);
            if (name == "dead") _deadNow = value;
            _net?.Bool(name, value);
        }

        private void SetTrigger(string name)
        {
            if (_animator != null && _animParams.Contains(name)) _animator.SetTrigger(name);
            _net?.Trigger(name);
        }

        private bool TrySayLine(string text, float seconds)
        {
            text = Plugin.FillMe(text);
            _net?.Say(text, seconds);
            try
            {
                SayLine(text, seconds);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Fuling couldn't show a speech bubble ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SayLine(string text, float seconds)
        {
            if (Chat.instance == null) throw new InvalidOperationException("Chat not ready");
            // The same call the merchant uses for his passive lines. A new line replaces his current bubble.
            Chat.instance.SetNpcText(gameObject, Vector3.up * BubbleHeight, BubbleCullDistance, seconds,
                Look.Name, text, false);
        }

        private void OnDestroy()
        {
            OnDestroyNet();
            if (_show != null && _show.Puppet) return;
            // Only the latest Fuling gets to end fitting; an older one finishing its exit run must not.
            bool latest = _current == this || _current == null;
            if (_current == this) _current = null;
            if (latest && _show != null && _show.Fitting && Plugin.FittingMode != null && Plugin.FittingMode.Value)
                Plugin.FittingMode.Value = false; // he's gone, so fitting is over
        }
    }
}
