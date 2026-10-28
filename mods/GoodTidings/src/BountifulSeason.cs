using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Bountiful Season: for a few minutes (Duration) everything in the base works much faster (Speed): crops and saplings grow, beehives
    /// fill, fermenters ferment, smelters and kilns smelt, cooking stations cook, including anything started meanwhile. Over each piece it
    /// touches, the boss-summoning particles gather, slowed down (one per few metres: a crop field shares them).
    /// The server sends one routed RPC (where, how long, how fast); every game plays the show, and each piece is sped up only on the game
    /// that owns it, by moving its own "last updated" clock back each second by the extra time (the game's catch-up for time nobody was
    /// near): fuel is used as usual, and nothing is made that the station wouldn't make itself. Cooking only pushes raw food up to done:
    /// the boost never burns it.
    /// </summary>
    internal sealed class BountifulSeason : Visit
    {
        public override string Id => "bountiful";
        public override bool Enabled => Plugin.BountifulEnabled.Value;

        internal const float Radius = MapPins.Range;   // (a raid's range: the circle on the map shows where it reaches)
        private const float Rescan = 5f, EffectSpacing = 3f, ParticleSpeed = 0.35f;
        private static bool _running;
        /// <summary>A season is going on (raids wait: see Visits).</summary>
        internal static bool Running => _running;

        public override bool Start(Vector3 basePoint)
        {
            if (_running) return false;
            var pkg = new ZPackage();
            pkg.Write(basePoint);
            pkg.Write(Plugin.BountifulMinutes.Value * 60f);
            pkg.Write(Plugin.BountifulSpeed.Value);
            pkg.Write(Plugin.BountifulHeadStart.Value * 60f);
            pkg.Write(Plugin.BountifulGrowSpeed.Value);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Net.Bountiful, pkg);
            Plugin.Log.LogInfo($"Bountiful season at {basePoint} for {Plugin.BountifulMinutes.Value} minutes at {Plugin.BountifulSpeed.Value}x");
            return true;
        }

        internal static void OnRpc(long sender, ZPackage pkg)
        {
            Vector3 center = pkg.ReadVector3();
            float seconds = pkg.ReadSingle(), speed = pkg.ReadSingle();
            float headStart = pkg.GetPos() < pkg.Size() ? pkg.ReadSingle() : 0f;
            float growSpeed = pkg.GetPos() < pkg.Size() ? pkg.ReadSingle() : speed;
            Plugin.Instance.StartCoroutine(Run(center, seconds, speed, headStart, growSpeed));
        }

        internal static void Stop() => _stop = true;
        private static bool _stop;

        private static IEnumerator Run(Vector3 center, float seconds, float speed, float headStart, float growSpeed)
        {
            _running = true;
            _stop = false;
            var effects = new Dictionary<Component, GameObject>();
            var spots = new List<Vector3>();
            bool near = Player.m_localPlayer != null && Utils.DistanceXZ(Player.m_localPlayer.transform.position, center) < Radius + 80f;
            if (near)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Lines.Pick("goodtidings_bountiful"));
                Effect("sfx_staff_elder_grow", center);
            }

            var pins = new MapPins();   // (the blue circle on the map over the base, while it lasts)
            float end = Time.time + seconds, nextScan = 0f, last = Time.time;
            var targets = new List<Component>();
            var started = new HashSet<Component>();   // (growing things that have had their head start)
            while (Time.time < end && !_stop)
            {
                if (Time.time >= nextScan)
                {
                    nextScan = Time.time + Rescan;
                    targets = Find(center);   // (again now and then: new pieces, and food put on meanwhile)
                    if (near)
                        foreach (Component t in targets)
                            if (t && !effects.ContainsKey(t))
                            {
                                Vector3 at = t.transform.position;
                                bool crowded = spots.Any(s => Vector3.Distance(s, at) < EffectSpacing);
                                effects[t] = crowded ? null : Glow(at, end - Time.time);
                                if (!crowded) spots.Add(at);
                            }
                }
                float dt = Time.time - last;
                last = Time.time;
                float extra = dt * (speed - 1f), growExtra = dt * (growSpeed - 1f);
                foreach (Component t in targets)
                {
                    if (t == null) continue;
                    bool growing = t is Plant || t is Beehive;   // (they take far longer than the rest: their own, stronger speed)
                    // crops, saplings and beehives take far longer than the rest: a head start the first time they're seen (at the start,
                    // or planted meanwhile), then the same speed as everything else
                    float more = growing && started.Add(t) ? headStart : 0f;
                    try { Boost(t, (growing ? growExtra : extra) + more); }
                    catch (Exception e) { Plugin.Debug($"Bountiful season on {t.name}: {e.Message}"); }
                }
                pins.Update(center, center, "", withIcon: false, radius: Radius);
                yield return new WaitForSeconds(1f);
            }
            pins.Remove();

            foreach (GameObject g in effects.Values) if (g) Fade(g);
            if (near && !_stop) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "$goodtidings_bountiful_end");
            _running = false;
        }

        /// <summary>Everything the season can speed up near the base (a physics query, every few seconds: not every frame).</summary>
        private static List<Component> Find(Vector3 center)
        {
            var found = new HashSet<Component>();
            foreach (Collider c in Physics.OverlapSphere(center, Radius, ~0, QueryTriggerInteraction.Collide))
            {
                Component t = (Component)c.GetComponentInParent<Plant>() ?? (Component)c.GetComponentInParent<Beehive>() ?? (Component)c.GetComponentInParent<Fermenter>()
                              ?? (Component)c.GetComponentInParent<Smelter>() ?? c.GetComponentInParent<CookingStation>();
                if (t != null) found.Add(t);
            }
            return found.ToList();
        }

        // ---- the speed-up: only on the piece's owner, through its own clock

        private static void Boost(Component t, float extra)
        {
            ZNetView nview = t.GetComponentInParent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            ZDO zdo = nview.GetZDO();
            long back = TimeSpan.FromSeconds(extra).Ticks, now = ZNet.instance.GetTime().Ticks;
            switch (t)
            {
                case Plant _:
                    zdo.Set(ZDOVars.s_plantTime, zdo.GetLong(ZDOVars.s_plantTime, now) - back);
                    break;
                case Beehive _:
                    zdo.Set(ZDOVars.s_lastTime, zdo.GetLong(ZDOVars.s_lastTime, now) - back);
                    break;
                case Fermenter _:
                    long started = zdo.GetLong(ZDOVars.s_startTime, 0L);
                    if (started != 0L) zdo.Set(ZDOVars.s_startTime, started - back);
                    break;
                case Smelter _:
                    zdo.Set(ZDOVars.s_startTime, zdo.GetLong(ZDOVars.s_startTime, now) - back);
                    break;
                case CookingStation station:
                    for (int i = 0; i < station.m_slots.Length; i++)
                    {
                        string item = zdo.GetString("slot" + i, "");
                        CookingStation.ItemConversion c = item == "" ? null : station.m_conversion.FirstOrDefault(x => x.m_from && x.m_from.name == item);
                        if (c == null) continue;   // (empty, or done or burnt: only raw food has a conversion from it)
                        float cooked = zdo.GetFloat("slot" + i, 0f);
                        if (cooked < c.m_cookTime - 0.05f) zdo.Set("slot" + i, Mathf.Min(cooked + extra, c.m_cookTime - 0.05f));
                    }
                    break;
            }
        }

        // ---- the show (each game's own)

        /// <summary>The boss-summoning particles over a piece, slowed, kept going for the season.</summary>
        private static GameObject Glow(Vector3 at, float seconds)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab("vfx_prespawn");
            if (prefab == null) return null;
            GameObject g = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            foreach (TimedDestruction d in g.GetComponentsInChildren<TimedDestruction>()) UnityEngine.Object.Destroy(d);
            foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.simulationSpeed = ParticleSpeed;
                main.loop = true;
                // wispy green: every colour the particles take turned to blue, their own brightness and fade kept
                main.startColor = Blue(main.startColor);
                var life = ps.colorOverLifetime;
                if (life.enabled) life.color = Blue(life.color);
            }
            foreach (Light l in g.GetComponentsInChildren<Light>()) l.color = Wisp;
            foreach (AudioSource a in g.GetComponentsInChildren<AudioSource>()) a.enabled = false;   // (the summoning rumble: not for 5 minutes)
            UnityEngine.Object.Destroy(g, seconds + 10f);
            return g;
        }

        private static readonly Color Wisp = new Color(0.45f, 1f, 0.5f);   // (a wispy green)

        private static ParticleSystem.MinMaxGradient Blue(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Blue(g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Blue(g.colorMin), Blue(g.colorMax));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Blue(g.gradient));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Blue(g.gradientMin), Blue(g.gradientMax));
                default: return g;   // (random colour: left as it is)
            }
        }

        private static Gradient Blue(Gradient g)
        {
            if (g == null) return null;
            var keys = g.colorKeys.Select(k => new GradientColorKey(Blue(k.color), k.time)).ToArray();
            var n = new Gradient();
            n.SetKeys(keys, g.alphaKeys);
            return n;
        }

        /// <summary>A colour as the wispy green, as bright as it was (its alpha kept).</summary>
        private static Color Blue(Color c)
        {
            float v = Mathf.Max(c.r, c.g, c.b);
            return new Color(Wisp.r * v, Wisp.g * v, Wisp.b * v, c.a);
        }

        private static void Fade(GameObject g)
        {
            foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            UnityEngine.Object.Destroy(g, 6f);
        }

        private static void Effect(string prefab, Vector3 at)
        {
            GameObject p = ZNetScene.instance.GetPrefab(prefab);
            if (p) UnityEngine.Object.Instantiate(p, at, Quaternion.identity);
        }
    }
}
