using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Grudges
{
    /// <summary>
    /// Brings recovered nemeses back. Runs on each player's own game: when a nemesis that cheated death has
    /// finished recovering, and the player who scarred it is in its biome, it may turn up a short way off and
    /// come for them. Your game spawns the new body and tells the server, which shows you the return.
    ///
    /// If the player who scarred it doesn't come by for a long while (two more days), anyone in its biome will do.
    /// </summary>
    internal class Returns : MonoBehaviour
    {
        private const float CheckEvery = 5f;
        private float _next;
        private readonly System.Random _rng = new System.Random();
        /// <summary>Ones we've spawned and are waiting for the server to confirm (so we don't spawn twice).</summary>
        private static readonly Dictionary<string, float> _waiting = new Dictionary<string, float>();
        private static Returns _instance;

        private void Awake() => _instance = this;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + CheckEvery;
            try { Check(); }
            catch (Exception e) { Plugin.DebugLog($"Returns: {e.Message}"); }
        }

        private void Check()
        {
            var p = Player.m_localPlayer;
            var roster = Net.Known;
            if (p == null || p.IsDead() || roster == null || ZNet.instance == null) return;
            double now = Server.WorldTime();
            string here = null;

            foreach (var n in roster.All.Where(x => x.State == NemesisState.Recovering).ToList())
            {
                if (now < n.ReturnAt || Waiting(n.Id)) continue;
                bool nobody = n.ScarredById == 0 && string.IsNullOrEmpty(n.ScarredByName); // it fell off a cliff or similar
                bool mine = nobody || n.ScarredById == p.GetPlayerID()
                            || (n.ScarredById == 0 && string.Equals(n.ScarredByName, p.GetPlayerName(), StringComparison.OrdinalIgnoreCase));
                bool overdue = now >= n.ReturnAt + 2 * Server.DayLength();
                if (!mine && !overdue) continue;
                if (here == null) here = GameInfo.Biome(p.transform.position);
                if (!string.Equals(here, n.Biome, StringComparison.OrdinalIgnoreCase)) continue;

                double perCheck = 1.0 - Math.Pow(1.0 - Plugin.ReturnChancePerMinute.Value, CheckEvery / 60.0);
                if (_rng.NextDouble() >= perCheck) continue;
                if (Spawn(n, p)) return; // one at a time
            }
        }

        private static bool Waiting(string id)
        {
            if (!_waiting.TryGetValue(id, out float until)) return false;
            if (Time.time < until) return true;
            _waiting.Remove(id);
            return false;
        }

        /// <summary>Test button: the first nemesis you scarred (or any recovering one) comes back now, wherever you are.</summary>
        internal static void ForceNow()
        {
            var p = Player.m_localPlayer;
            var roster = Net.Known;
            if (p == null || roster == null) { Plugin.ShowPlain("Grudges: get into a world first."); return; }
            var options = roster.All.Where(x => x.State == NemesisState.Recovering && !Waiting(x.Id)).ToList();
            var n = options.FirstOrDefault(x => x.ScarredById == p.GetPlayerID()) ?? options.FirstOrDefault();
            if (n == null) { Plugin.ShowPlain("Grudges: no nemesis is recovering right now. Use 'Nearest nemesis cheats death' first."); return; }
            if (!Spawn(n, p)) Plugin.ShowPlain("Grudges: couldn't find a spot for it to appear (water or a base all around?).");
        }

        private static bool Spawn(Nemesis n, Player p)
        {
            var c = SpawnBody(n, p, "return");
            if (c == null) return false;
            _waiting[n.Id] = Time.time + 30f;
            Plugin.Log.LogInfo($"{n.FullName} returns for {p.GetPlayerName()}.");
            return true;
        }

        /// <summary>
        /// Spawns a new body for a nemesis near a player, tagged as that nemesis (one generation newer than any old
        /// body, which stops being it), and sends it at them. A Champion brings an escort. Tells the server.
        /// </summary>
        internal static Character SpawnBody(Nemesis n, Player p, string reason, int levelBonus = 0)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(n.Species) : null;
            if (prefab == null) { Plugin.Log.LogWarning($"Grudges: no creature called '{n.Species}'."); return null; }
            if (!FindSpot(p, Plugin.ReturnDistance.Value, out Vector3 pos)) return null;

            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(Flat(p.transform.position - pos)));
            var c = go.GetComponent<Character>();
            var zdo = GameInfo.Zdo(c);
            if (c == null || zdo == null) { Plugin.Log.LogWarning("Grudges: the new body didn't wake up properly."); return null; }

            int gen = n.BodyGen + 1;
            zdo.Set(GameInfo.KeyId, n.Id);
            zdo.Set(GameInfo.KeyBaseLevel, n.BaseLevel.ToString());
            zdo.Set(GameInfo.KeyGen, gen.ToString());
            zdo.Set(GameInfo.KeyBonus, levelBonus.ToString());
            if (n.EffectiveLevel + levelBonus > 1) c.SetLevel(n.EffectiveLevel + levelBonus);
            if (reason == "hunt")
            {
                // Far from home: don't let the scenery kill it before it reaches you.
                c.m_tolerateFire = true;
                c.m_tolerateSmoke = true;
                c.m_tolerateWater = true;
            }
            c.SetHealth(c.GetMaxHealth());
            Hunt(c, p);
            if (n.Champion) Escort(prefab, pos, p);

            Net.ReportNewBody(n.Id, GameInfo.ZdoKey(c), gen, reason, p.GetPlayerName());
            return c;
        }

        /// <summary>A few ordinary ones of its kind come with a Champion.</summary>
        private static void Escort(GameObject prefab, Vector3 at, Player p)
        {
            int count = Plugin.ChampionEscort.Value;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 360f * i / Mathf.Max(1, count) + 45f, 0f) * Vector3.forward * 3f;
                Vector3 pos = at + offset;
                pos.y = GroundHeight(pos, at.y) + 0.3f;
                var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(Flat(p.transform.position - pos)));
                var c = go.GetComponent<Character>();
                if (c != null) Hunt(c, p);
            }
        }

        /// <summary>Points its AI at the player.</summary>
        private static void Hunt(Character c, Player p)
        {
            var ai = c.GetComponent<BaseAI>();
            if (ai == null) return;
            try { Traverse.Create(ai).Method("SetHuntPlayer", true).GetValue(); } catch { }
            try { Traverse.Create(ai).Method("SetAlerted", true).GetValue(); } catch { }
            try
            {
                var setTarget = AccessTools.Method(ai.GetType(), "SetTarget", new[] { typeof(Character) });
                setTarget?.Invoke(ai, new object[] { p });
            }
            catch { }
        }

        /// <summary>A dry spot on the ground at roughly this distance, behind or beside the player, outside any ward.</summary>
        private static bool FindSpot(Player p, float distance, out Vector3 spot)
        {
            float water = 30f;
            try { if (ZoneSystem.instance != null) water = Convert.ToSingle(Traverse.Create(ZoneSystem.instance).Field("m_waterLevel").GetValue()); } catch { }

            Vector3 back = -Flat(p.transform.forward).normalized;
            float[] angles = { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 180f };
            foreach (float a in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * back;
                Vector3 pos = p.transform.position + dir * distance;
                float ground = GroundHeight(pos, p.transform.position.y);
                if (ground < water + 0.5f) continue;
                pos.y = ground + 0.3f;
                if (InWard(pos)) continue;
                spot = pos;
                return true;
            }
            spot = Vector3.zero;
            return false;
        }

        private static float GroundHeight(Vector3 pos, float fallback)
        {
            try
            {
                if (ZoneSystem.instance != null)
                {
                    var m = AccessTools.Method(typeof(ZoneSystem), "GetGroundHeight", new[] { typeof(Vector3) });
                    if (m != null) return Convert.ToSingle(m.Invoke(ZoneSystem.instance, new object[] { pos }));
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Inside an active ward (a base)? Checks every ward in the loaded area against its radius, plus a margin so it
        /// doesn't pop up right at the edge. Wards that can't be read are treated as on.
        /// </summary>
        private static bool InWard(Vector3 pos)
        {
            try
            {
                foreach (var ward in UnityEngine.Object.FindObjectsByType<PrivateArea>(UnityEngine.FindObjectsSortMode.None))
                {
                    if (ward == null) continue;
                    var t = Traverse.Create(ward);
                    float radius = 32f;
                    try { radius = Convert.ToSingle(t.Field("m_radius").GetValue()); } catch { }
                    bool on = true;
                    try { var m = AccessTools.DeclaredMethod(typeof(PrivateArea), "IsEnabled"); if (m != null) on = (bool)m.Invoke(ward, null); } catch { }
                    if (on && Vector3.Distance(ward.transform.position, pos) < radius + 8f) return true;
                }
            }
            catch (Exception e) { Plugin.DebugLog($"Ward check failed ({e.Message})."); }
            return false;
        }

        internal static Vector3 FlatPublic(Vector3 v) => Flat(v);
        internal static bool InWardPublic(Vector3 pos) => InWard(pos);

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 0.0001f ? Vector3.forward : v; }
    }
}
