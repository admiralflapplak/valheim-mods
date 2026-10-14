using System.Collections;
using System.Linq;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// The goofy ambient scenes: the Lurker, Red Light Green Light, and the Tombstone Dancer.
    /// Same Fuling, same safety rules; these are just different routines for him.
    /// </summary>
    internal partial class FulingActor
    {
        // ------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Is the player looking at this spot? Compares where the camera points with the direction to the spot.
        /// maxAngle ~10 = looking right at it; ~45 = it's somewhere in the middle of the screen.
        /// </summary>
        public static bool IsLookedAt(Vector3 spot, float maxAngle, float maxDistance)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 to = spot - cam.transform.position;
            if (to.magnitude > maxDistance) return false;
            return Vector3.Angle(cam.transform.forward, to) <= maxAngle;
        }

        /// <summary>
        /// Finds dry ground around the player at an angle from where they're looking
        /// (0 = straight ahead, 90 = to the side, 180 = directly behind), left or right at random.
        /// </summary>
        public static bool FindPointAround(Player player, float minAngle, float maxAngle, float minDist, float maxDist, out Vector3 point)
        {
            Vector3 origin = player.transform.position;
            Vector3 look = ViewForward(player);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = Random.Range(minAngle, maxAngle) * (Random.value < 0.5f ? -1f : 1f);
                float dist = Random.Range(minDist, maxDist);
                Vector3 p = origin + (Quaternion.Euler(0f, angle, 0f) * look) * dist;
                if (TryGround(p, origin.y, out Vector3 ground) && !IsUnderwater(ground))
                {
                    point = ground;
                    return true;
                }
            }
            point = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Is the player's character facing this spot? Uses the direction the camera points, but measured from the
        /// character, not the camera. In third person the camera sits behind you, so something standing right behind
        /// your character is on screen even though you're facing away; this check treats that as "not looking".
        /// </summary>
        public static bool IsFacedByPlayer(Player p, Vector3 spot, float maxAngle)
        {
            var cam = Camera.main;
            Vector3 facing = cam != null ? cam.transform.forward : p.transform.forward;
            facing.y = 0f;
            Vector3 to = spot - p.transform.position;
            to.y = 0f;
            if (facing.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(facing, to) <= maxAngle;
        }

        /// <summary>
        /// Can the camera actually see this spot, or is a hill, tree, rock or building in the way?
        /// Traces a line from the camera to the spot and checks whether anything solid blocks it first.
        /// </summary>
        public static bool HasLineOfSight(Vector3 spot)
        {
            var cam = Camera.main;
            if (cam == null) return true;
            Vector3 from = cam.transform.position;
            Vector3 to = spot - from;
            float dist = to.magnitude;
            if (dist < 0.5f) return true;
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small", "vehicle");
            // Stop just short of him so his own spot doesn't count as a blocker.
            return !Physics.Raycast(from, to / dist, out RaycastHit _, dist - 0.6f, mask);
        }

        private Vector3 Head => transform.position + Vector3.up * 1.2f;

        private float DistanceToPlayer(Player p) => Flat(p.transform.position - transform.position).magnitude;

        /// <summary>A line from the quips file for this moment, or a built-in one if the file has none.</summary>
        private static string SceneLine(string evt, params string[] fallback)
        {
            var lines = Plugin.Quips != null ? Plugin.Quips.Lines.Where(q => q.Event == evt).Select(q => q.Text).ToList() : null;
            if (lines == null || lines.Count == 0) lines = fallback.ToList();
            return lines[Rng.Next(lines.Count)];
        }

        /// <summary>Runs a short way from the player, then vanishes with the exit puff and poof sound.</summary>
        private IEnumerator RunOffAndVanish(float speed, float distance)
        {
            if (_animator != null) _animator.speed = 1f;
            var player = Player.m_localPlayer;
            Vector3 away = player != null ? Flat(transform.position - player.transform.position) : transform.forward;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;

            float run = 0f;
            while (run < distance)
            {
                float step = speed * Time.deltaTime;
                MoveToward(away, step, speed);
                run += step;
                yield return null;
            }
            VanishHere();
        }

        private void VanishHere()
        {
            SfxAt(SoundMoment.Poof);
            PoofHere(Plugin.ExitEffects.Value);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------
        // The Lurker: stares at you from off to the side. Look at him and he bolts.
        // ------------------------------------------------------------------

        private IEnumerator Lurk()
        {
            SetSpeed(0f);
            float patience = Plugin.LurkerPatience.Value;
            float watched = 0f, t = 0f;
            bool spotted = false;

            while (t < patience)
            {
                var p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                float dist = DistanceToPlayer(p);
                if (dist > 80f) { Destroy(gameObject); yield break; } // you wandered off; he gives up quietly

                FaceDirection(Flat(p.transform.position - transform.position));

                // Spotted = close enough to notice, looking right at him, and nothing in the way, held for a moment.
                float notice = Plugin.LurkerNoticeDistance.Value;
                bool seen = dist <= notice && IsLookedAt(Head, 12f, notice + 10f) && HasLineOfSight(Head);
                watched = seen ? watched + Time.deltaTime : 0f;
                if (watched > 0.5f || dist < 7f || RemoteSaw(_pokeSpotted)) { spotted = true; break; } // anyone can spot him

                t += Time.deltaTime;
                yield return null;
            }

            if (spotted)
            {
                SetTrigger("stagger"); // flinch
                TrySayLine(SceneLine("spotted", "Eep!", "{me} not here!", "You see nothing!", "!!!"), 2f);
                Sfx(SoundMoment.Spotted);
                Plugin.Log.LogInfo("Lurker: spotted! He bolts.");
                yield return new WaitForSeconds(0.4f);
                yield return RunOffAndVanish(RunSpeed * 1.5f, Plugin.ExitRunDistance.Value);
            }
            else
            {
                TrySayLine(SceneLine("bored", "Boring.", "Tall-one never look. {me} go home.", "*yawn*"), 2.5f);
                Sfx(SoundMoment.Bored);
                Plugin.Log.LogInfo("Lurker: never spotted. He got bored and left.");
                yield return new WaitForSeconds(1.2f);
                yield return RunOffAndVanish(RunSpeed, Plugin.ExitRunDistance.Value);
            }
        }

        // ------------------------------------------------------------------
        // Red Light, Green Light: creeps up from behind, freezes whenever you look. BOO.
        // ------------------------------------------------------------------

        private IEnumerator RedLight()
        {
            float closeEnough = Plugin.BooDistance.Value;
            float timeout = 120f, t = 0f;
            float speed = Plugin.CreepSpeed.Value;

            while (t < timeout)
            {
                var p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                float dist = DistanceToPlayer(p);
                if (dist > 70f) { VanishHere(); yield break; } // you outran him

                if (IsFacedByPlayer(p, transform.position, Plugin.RedLightWatchAngle.Value) || RemoteSaw(_pokeWatched)) // anyone watching freezes him
                {
                    // Frozen solid, mid-pose, wherever the animation happened to be.
                    if (_animator != null) _animator.speed = 0f;
                }
                else
                {
                    if (_animator != null) _animator.speed = 1f;
                    // A little tolerance: aiming for exactly closeEnough left him "running" microscopic steps.
                    if (dist <= closeEnough + 0.25f) break;
                    Vector3 to = Flat(p.transform.position - transform.position);
                    MoveToward(to.normalized, Mathf.Min(speed * Time.deltaTime, dist - closeEnough), speed);
                }

                // Backup: if he's been frozen within arm's reach for a while, he gives up being sneaky.
                if (dist <= closeEnough + 1.5f && _animator != null && _animator.speed == 0f)
                {
                    _frozenClose += Time.deltaTime;
                    if (_frozenClose > 3f) break;
                }
                else _frozenClose = 0f;

                t += Time.deltaTime;
                yield return null;
            }

            // He's right behind you. Wait for you to turn around and face him...
            if (_animator != null) _animator.speed = 1f;
            SetSpeed(0f);
            float wait = 0f;
            while (wait < 12f)
            {
                var p = Player.m_localPlayer;
                if (p == null) { Destroy(gameObject); yield break; }
                FaceDirection(Flat(p.transform.position - transform.position));
                if (IsFacedByPlayer(p, transform.position, 60f)) break;
                wait += Time.deltaTime;
                yield return null;
            }

            // BOO!
            SetTrigger("jump");
            TrySayLine(SceneLine("boo", "BOO!", "BOO! Hehehe!", "Gotcha!"), 1.5f);
            Sfx(SoundMoment.Boo);
            Plugin.Log.LogInfo("Red Light Green Light: BOO!");
            yield return new WaitForSeconds(0.7f);
            VanishHere();
        }

        private float _frozenClose;

        // ------------------------------------------------------------------
        // Tombstone Dancer: dancing on your grave when you come back for your stuff.
        // ------------------------------------------------------------------

        private IEnumerator TombstoneDance()
        {
            float timeout = 180f, t = 0f, nextDance = 0f;
            bool caught = false;
            var dances = NameList.Parse(Plugin.TombstoneDances.Value);

            while (t < timeout)
            {
                var p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                if (DistanceToPlayer(p) < Plugin.TombstoneCatchDistance.Value || RemoteSaw(_pokeClose)) { caught = true; break; }

                SfxLoop(SoundMoment.Dance); // music / humming, clip after clip

                if (t >= nextDance)
                {
                    var clip = PlayEmote(dances);
                    nextDance = t + (clip != null ? Mathf.Max(1.5f, clip.length * 0.95f) : 3f);
                }

                t += Time.deltaTime;
                yield return null;
            }

            RestoreEmote();
            SfxStop();
            if (caught)
            {
                var p = Player.m_localPlayer;
                if (p != null) FaceDirection(Flat(p.transform.position - transform.position));
                TrySayLine(SceneLine("tombstone", "{me} not touch your things! {me} just dance!",
                    "Was like this when {me} got here!", "Nice grave. Good for dancing. Bye!"), 3f);
                Sfx(SoundMoment.Caught);
                Plugin.Log.LogInfo("Tombstone Dancer: caught dancing. He bolts.");
                yield return new WaitForSeconds(0.6f);
                yield return RunOffAndVanish(RunSpeed * 1.5f, Plugin.ExitRunDistance.Value);
            }
            else VanishHere();
        }
    }
}
