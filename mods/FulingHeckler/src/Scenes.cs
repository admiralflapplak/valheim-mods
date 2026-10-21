using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Decides when the Fuling turns up uninvited: random scenes every so often (Lurker, Red Light Green Light),
    /// and the Tombstone Dancer when you head back to where you died. Never during fights, on boats, in water,
    /// in dungeons, or while another Fuling is already out.
    /// </summary>
    internal class Scenes : MonoBehaviour
    {
        public static Scenes Instance;

        private readonly System.Random _rng = new System.Random();
        private float _nextSceneAt = -1f;
        private Vector3? _deathPos;
        private float _deathTime;
        private bool _tombstoneTried;
        private float _nextCampfireCheck, _campfireCooldownUntil;

        // Creature factions that don't count as "a fight is going on".
        private static readonly HashSet<string> CalmFactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Players", "AnimalsVeg", "PlayerSpawned", "Dverger", "TrainingDummy" };

        private void Awake() => Instance = this;

        private void ScheduleNext()
        {
            float min = Mathf.Min(Plugin.SceneMinutesMin.Value, Plugin.SceneMinutesMax.Value);
            float max = Mathf.Max(Plugin.SceneMinutesMin.Value, Plugin.SceneMinutesMax.Value);
            _nextSceneAt = Time.time + UnityEngine.Random.Range(min, max) * 60f;
            Plugin.DebugLog($"Next random scene in {(_nextSceneAt - Time.time) / 60f:0.0} minutes.");
        }

        /// <summary>Called when the local player dies, so the Tombstone Dancer knows where the grave is.</summary>
        public void RecordDeath(Vector3 position)
        {
            _deathPos = position;
            _deathTime = Time.time;
            _tombstoneTried = false;
        }

        /// <summary>You died recently and haven't been back to the grave: keep your pockets free for your stuff.</summary>
        public bool HeadingBackToGrave => _deathPos.HasValue && Time.time - _deathTime < 30f * 60f && !_tombstoneTried;

        private void Update()
        {
            if (!Plugin.Enabled.Value) return;
            var p = Player.m_localPlayer;
            if (p == null) return;
            if (_nextSceneAt < 0f) ScheduleNext();

            // Tombstone Dancer: you're heading back towards where you died.
            if (_deathPos.HasValue && !_tombstoneTried && !p.IsDead())
            {
                if (Time.time - _deathTime > 30f * 60f) _deathPos = null; // too long ago
                else
                {
                    float d = Vector3.Distance(p.transform.position, _deathPos.Value);
                    if (d < 45f && d > 22f) // far enough to see him dancing before you get there
                    {
                        _tombstoneTried = true;
                        if (Plugin.TombstoneDancer.Value && _rng.NextDouble() < Plugin.TombstoneChance.Value && CanStage(p, out string why))
                            StartTombstone(p, _deathPos.Value);
                        else Plugin.DebugLog("Tombstone Dancer skipped this time.");
                    }
                }
            }

            // Campfire Squatter: night, a lit fire nearby, and you're approaching it.
            if (Plugin.CampfireEnabled.Value && Time.time >= _nextCampfireCheck)
            {
                _nextCampfireCheck = Time.time + 5f;
                if (Time.time >= _campfireCooldownUntil && FulingActor.IsNight() && !p.IsDead())
                {
                    var fire = NearestLitFire(p, 50f);
                    if (fire.HasValue)
                    {
                        float d = Vector3.Distance(p.transform.position, fire.Value);
                        if (d > 25f) // far enough to see him sitting there before you arrive
                        {
                            _campfireCooldownUntil = Time.time + 20f * 60f;
                            if (_rng.NextDouble() < Plugin.CampfireChance.Value && CanStage(p, out string why)) StartCampfire(p, fire.Value);
                            else Plugin.DebugLog("Campfire Squatter skipped this time.");
                        }
                    }
                }
            }

            // Random scenes.
            if (!Plugin.RandomScenes.Value || Time.time < _nextSceneAt) return;
            if (!CanStage(p, out string reason))
            {
                Plugin.DebugLog($"Random scene postponed: {reason}.");
                _nextSceneAt = Time.time + 30f; // try again shortly
                return;
            }

            var options = new List<SceneKind>();
            if (Plugin.LurkerEnabled.Value) options.Add(SceneKind.Lurker);
            if (Plugin.RedLightEnabled.Value) options.Add(SceneKind.RedLight);
            if (Plugin.CopycatEnabled.Value) options.Add(SceneKind.Copycat);
            if (Plugin.ThiefEnabled.Value && CoinTheft.CoinsCarried(p) >= Plugin.ThiefMinCoins.Value) options.Add(SceneKind.CoinThief);
            if (Rules.GifterEnabled && Gifts.HasRoom(p, out _) && !HeadingBackToGrave) options.Add(SceneKind.Gifter);
            if (options.Count > 0) Stage(p, options[_rng.Next(options.Count)]);
            ScheduleNext();
        }

        // ------------------------------------------------------------------
        // Starting scenes
        // ------------------------------------------------------------------

        public bool Stage(Player p, SceneKind kind)
        {
            Vector3 spot;
            bool found;
            switch (kind)
            {
                case SceneKind.Lurker:   // off to the side, at the configured distance
                    float min = Mathf.Min(Plugin.LurkerDistanceMin.Value, Plugin.LurkerDistanceMax.Value);
                    float max = Mathf.Max(Plugin.LurkerDistanceMin.Value, Plugin.LurkerDistanceMax.Value);
                    found = FulingActor.FindPointAround(p, 70f, 140f, min, max, out spot); break;
                case SceneKind.RedLight: found = FulingActor.FindPointAround(p, 155f, 180f, 22f, 28f, out spot); break; // behind you
                case SceneKind.Copycat:  found = FulingActor.FindPointAround(p, 0f, 30f, 12f, 18f, out spot); break;   // in front, where you'll see him
                case SceneKind.Gifter:   found = FulingActor.FindPointAround(p, 150f, 180f, 18f, 24f, out spot); break; // behind you
                case SceneKind.CoinThief: found = FulingActor.FindPointAround(p, 60f, 120f, 14f, 20f, out spot); break; // from the side
                default: return false;
            }
            if (!found) { Plugin.DebugLog($"{kind}: no dry ground to stand on nearby."); return false; }

            Plugin.Log.LogInfo($"Scene: {kind}.");
            return FulingActor.TrySpawn(p, new Performance { Scene = kind, SpawnAt = spot, Line = "" });
        }

        public bool StartTombstone(Player p, Vector3 grave)
        {
            // Stand right beside the grave marker, on the ground.
            Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * 1.2f;
            Vector3 spot = grave + new Vector3(r.x, 0f, r.y);
            var hit = Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out RaycastHit h, 15f,
                LayerMask.GetMask("Default", "static_solid", "terrain", "piece"));
            if (hit) spot = h.point;

            Plugin.Log.LogInfo("Scene: Tombstone Dancer.");
            return FulingActor.TrySpawn(p, new Performance { Scene = SceneKind.Tombstone, SpawnAt = spot, Line = "" });
        }

        public bool StartCampfire(Player p, Vector3 fire)
        {
            // A seat by the fire (a chair, bench, log bench, throne): he sits on it, the way it faces, as a player would.
            if (FindSeat(fire, out Vector3 seat, out Quaternion facing, out string pose))
            {
                Plugin.Log.LogInfo($"Scene: Campfire Squatter, on a seat ({pose}).");
                return FulingActor.TrySpawn(p, new Performance { Scene = SceneKind.Campfire, SpawnAt = seat, SpawnRotation = facing, SeatPose = pose, ForceTorch = false, Line = "" });
            }

            // Sit beside the fire, on the side facing you so you can see him, a little off to one side.
            Vector3 toPlayer = p.transform.position - fire; toPlayer.y = 0f;
            Vector3 side = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.forward;
            side = Quaternion.Euler(0f, UnityEngine.Random.Range(-60f, 60f), 0f) * side;
            Vector3 spot = fire + side * 1.7f;
            if (Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out RaycastHit h, 15f,
                LayerMask.GetMask("Default", "static_solid", "terrain", "piece")))
                spot = h.point;

            Plugin.Log.LogInfo("Scene: Campfire Squatter.");
            return FulingActor.TrySpawn(p, new Performance { Scene = SceneKind.Campfire, SpawnAt = spot, FacePoint = fire, ForceTorch = false, Line = "" });
        }

        /// <summary>Every loaded fire, kept as they load (a search of the whole scene every few seconds stutters in a big base).</summary>
        internal static readonly HashSet<Fireplace> Fires = new HashSet<Fireplace>();

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
        private static class FireplaceAwake
        {
            private static void Postfix(Fireplace __instance) => Fires.Add(__instance);
        }

        /// <summary>Fires already loaded when the mod starts (after a reload in a world). Once, at load.</summary>
        internal static void FindLoadedFires()
        {
            foreach (var f in UnityEngine.Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None)) Fires.Add(f);
        }

        private const float SeatRange = 4.5f;   // how far from the fire a seat may be (a ring of log benches round a campfire)

        /// <summary>
        /// A free seat near the fire, the nearest first: the game's Chair part (chairs, stools, benches, log benches, thrones, other
        /// mods' seats) gives the spot and which way it faces, and its sitting style picks the player animation. Only the pieces near
        /// the fire are looked at, once, when the scene starts.
        /// </summary>
        private static bool FindSeat(Vector3 fire, out Vector3 seat, out Quaternion facing, out string pose)
        {
            seat = Vector3.zero; facing = Quaternion.identity; pose = null;
            float best = float.MaxValue;
            var seen = new HashSet<Chair>();
            try
            {
                foreach (var c in Physics.OverlapSphere(fire, SeatRange, LayerMask.GetMask("piece", "piece_nonsolid", "Default")))
                {
                    var chair = c != null ? c.GetComponentInParent<Chair>() : null;
                    if (chair == null || !seen.Add(chair) || chair.m_attachPoint == null || chair.m_inShip || chair.IsInUse()) continue;
                    string anim = SeatAnimation(chair.m_attachAnimation);
                    if (anim == null) continue;
                    Vector3 at = chair.m_attachPoint.position;
                    float d = Vector3.Distance(new Vector3(at.x, 0f, at.z), new Vector3(fire.x, 0f, fire.z));
                    if (d < 0.8f || d > SeatRange || Mathf.Abs(at.y - fire.y) > 2f || d >= best) continue;   // not on the fire, not upstairs
                    best = d;
                    seat = at;
                    facing = Quaternion.Euler(0f, chair.m_attachPoint.rotation.eulerAngles.y, 0f);
                    pose = anim;
                }
            }
            catch (Exception e) { Plugin.DebugLog($"Campfire Squatter: couldn't look for seats ({e.Message})."); }
            return pose != null;
        }

        /// <summary>The player animation for a seat's sitting style (Chair.m_attachAnimation), or null for one he can't do.</summary>
        private static string SeatAnimation(string attach)
        {
            switch (attach)
            {
                case "attach_chair": return "Player_Sit_Chair";
                case "attach_throne": return "Sit_Throne_Idle";
                default: return null;
            }
        }

        /// <summary>
        /// A fire to sit by: what the game counts as a fire for comfort (campfire, iron fire pit, hearth, bonfire, standing brazier, and
        /// other mods' fires in that group), not a sconce, a standing torch or a Jack-o-turnip, and not a brazier hanging from the ceiling.
        /// </summary>
        private static bool IsCampfire(Fireplace f)
        {
            var piece = f.GetComponentInParent<Piece>();
            if (piece == null || piece.m_comfortGroup != Piece.ComfortGroup.Fire) return false;
            return Utils.GetPrefabName(piece.gameObject).IndexOf("ceiling", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>The closest burning fire to sit by (IsCampfire) within range, if any.</summary>
        public static Vector3? NearestLitFire(Player p, float range)
        {
            try
            {
                Vector3? best = null;
                float bestDist = range;
                Fires.RemoveWhere(f => f == null);
                foreach (var f in Fires)
                {
                    float d = Vector3.Distance(f.transform.position, p.transform.position);
                    if (d > bestDist) continue;
                    if (!f.IsBurning() || !IsCampfire(f)) continue;
                    best = f.transform.position;
                    bestDist = d;
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>The safety rules: when he must NOT show up.</summary>
        public bool CanStage(Player p, out string reason)
        {
            reason = null;
            if (FulingActor.Current != null) { reason = "a Fuling is already out"; return false; }
            if (FulingActor.PuppetNear(p.transform.position, 100f)) { reason = "another player's goblin is out nearby"; return false; }
            if (p.IsDead() || p.IsTeleporting()) { reason = "dead or teleporting"; return false; }
            if (p.transform.position.y > 1000f) { reason = "in a dungeon"; return false; }
            if (p.IsSwimming()) { reason = "swimming"; return false; }
            if (p.InBed()) { reason = "in bed"; return false; }
            if (p.IsAttached()) { reason = "sitting, on a cart or at a ship's helm"; return false; }
            if (p.GetStandingOnShip() != null) { reason = "on a boat"; return false; }
            if (EnemiesNearby(p, 35f)) { reason = "enemies nearby"; return false; }
            return true;
        }

        /// <summary>
        /// A fight going on near the player: a creature in range that is alerted or after someone (its AI says so). Wildlife minding its
        /// own business (a Neck by the water, a greyling wandering) doesn't hold the scenes up.
        /// </summary>
        private static bool EnemiesNearby(Player p, float range)
        {
            try
            {
                foreach (var c in Character.GetAllCharacters())
                {
                    if (c == null || c == p || c is Player || c.IsDead()) continue;
                    if (Vector3.Distance(c.transform.position, p.transform.position) > range) continue;
                    if (c.IsTamed()) continue;
                    if (CalmFactions.Contains(c.GetFaction().ToString())) continue;
                    var ai = c.GetBaseAI();
                    if (ai == null || (!ai.IsAlerted() && !ai.HaveTarget())) continue;
                    return true;
                }
            }
            catch { /* if unsure, don't block */ }
            return false;
        }
    }
}
