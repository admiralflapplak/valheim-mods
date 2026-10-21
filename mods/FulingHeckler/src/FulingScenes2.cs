using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>Round 2 scenes: the Copycat, the Campfire Squatter and the Coin Thief.</summary>
    internal partial class FulingActor
    {
        // ------------------------------------------------------------------
        // Copycat: copies your emotes from a distance. Always leaves on his own after a while.
        // ------------------------------------------------------------------

        private string _pendingEmote;
        private float _pendingEmoteAt;

        private void OnPlayerEmote(string emote)
        {
            _pendingEmote = emote;
            _pendingEmoteAt = Time.time + 0.6f; // a beat later, like he's watching and copying
        }

        private IEnumerator Copycat()
        {
            SetSpeed(0f);
            Plugin.PlayerEmoted += OnPlayerEmote;
            float limit = Plugin.CopycatDuration.Value, t = 0f;
            string ending = "time";
            try
            {
                while (t < limit)
                {
                    var p = Player.m_localPlayer;
                    if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                    float dist = DistanceToPlayer(p);
                    if (dist > 60f) { ending = "left behind"; break; }
                    if (dist < 4f || RemoteSaw(_pokeClose)) { ending = "too close"; break; }
                    FaceDirection(Flat(p.transform.position - transform.position));

                    if (_pendingEmote != null && Time.time >= _pendingEmoteAt)
                    {
                        string emote = _pendingEmote;
                        _pendingEmote = null;

                        // Pointing at him is rude.
                        if (emote.IndexOf("point", StringComparison.OrdinalIgnoreCase) >= 0 && IsFacedByPlayer(p, transform.position, 30f))
                        {
                            ending = "rude";
                            break;
                        }

                        var clip = PlayEmote(new[] { emote });
                        Plugin.DebugLog(clip != null ? $"Copycat copies '{emote}' ({clip.name})." : $"Copycat doesn't know '{emote}'.");
                        if (clip == null) TrySayLine("?", 1.5f);
                    }

                    t += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                Plugin.PlayerEmoted -= OnPlayerEmote;
            }

            RestoreEmote();
            Plugin.Log.LogInfo($"Copycat: leaving ({ending}).");
            switch (ending)
            {
                case "rude":
                    TrySayLine(SceneLine("copycat_rude", "Rude!", "Do not point at {me}!", "{me} not a thing to point at!"), 2.5f);
                    Sfx(SoundMoment.Flee);
                    SetTrigger("stagger");
                    yield return new WaitForSeconds(0.8f);
                    yield return RunOffAndVanish(RunSpeed * 1.3f, Plugin.ExitRunDistance.Value);
                    break;
                case "too close":
                    TrySayLine(FleeLine(), 2f);
                    Sfx(SoundMoment.Flee);
                    yield return RunOffAndVanish(RunSpeed * 1.5f, Plugin.ExitRunDistance.Value);
                    break;
                case "left behind":
                    VanishHere();
                    break;
                default:
                    // Time's up: a wave goodbye, then gone. He always leaves on his own.
                    var bye = PlayEmote(new[] { "wave" });
                    TrySayLine(SceneLine("copycat_bye", "Fun game. Bye!", "{me} tired now. Bye bye!", "Again tomorrow!"), 2.5f);
                    yield return new WaitForSeconds(bye != null ? Mathf.Clamp(bye.length, 1.5f, 3f) : 1.5f);
                    RestoreEmote();
                    VanishHere();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Campfire Squatter: sitting by your fire at night. Bolts when you get close.
        // ------------------------------------------------------------------

        private IEnumerator CampfireSquat()
        {
            SetSpeed(0f);
            if (_show.SpawnRotation.HasValue) transform.rotation = _show.SpawnRotation.Value;
            else if (_show.FacePoint.HasValue) transform.rotation = Quaternion.LookRotation(Flat(_show.FacePoint.Value - transform.position).normalized, Vector3.up);

            var poses = _show.SeatPose != null ? new List<string> { _show.SeatPose } : NameList.Parse(Plugin.CampfirePoses.Value);
            Vector3 seat = transform.position;   // on a seat: its own spot, which Seat Height / Seat Forward shift him from (live)
            float t = 0f, nextPose = 0f, timeout = 240f;
            bool caught = false;
            while (t < timeout)
            {
                var p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                if (DistanceToPlayer(p) < Plugin.CampfireCatchDistance.Value || RemoteSaw(_pokeClose)) { caught = true; break; }
                if (_show.SeatPose != null)
                    transform.position = seat + Vector3.up * Plugin.SeatUp.Value + transform.forward * Plugin.SeatForward.Value;

                if (t >= nextPose)
                {
                    var clip = PlayEmote(poses);
                    nextPose = t + (clip != null ? Mathf.Max(1.5f, clip.length * 0.95f) : 4f);
                }
                t += Time.deltaTime;
                yield return null;
            }

            RestoreEmote();
            if (!caught) { VanishHere(); yield break; }

            var player = Player.m_localPlayer;
            if (player != null) FaceDirection(Flat(player.transform.position - transform.position));
            SetTrigger("stagger");
            TrySayLine(SceneLine("campfire", "{me} just warming hands! Bye!", "Nice fire. {me} go now.",
                "Was cold! {me} not touch anything!"), 3f);
            Sfx(SoundMoment.Caught);
            Plugin.Log.LogInfo("Campfire Squatter: caught. He bolts.");
            yield return new WaitForSeconds(0.6f);
            yield return RunOffAndVanish(RunSpeed * 1.5f, Plugin.ExitRunDistance.Value);
        }

        // ------------------------------------------------------------------
        // Coin Thief: grabs coins and runs, dropping them one by one so you can follow the trail.
        // SAFETY: a coin is only taken from you AFTER a real coin has been dropped on the ground,
        // so if anything goes wrong mid-run, you never lose coins.
        // ------------------------------------------------------------------

        private IEnumerator CoinThief()
        {
            // 1. Run up to the player.
            float timeout = 8f;
            while (timeout > 0f)
            {
                var p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { Destroy(gameObject); yield break; }
                float dist = DistanceToPlayer(p);
                if (dist <= 2.2f) break;
                MoveToward(Flat(p.transform.position - transform.position).normalized,
                    Mathf.Min(RunSpeed * 1.3f * Time.deltaTime, dist - 2.0f), RunSpeed * 1.3f, p.transform.position);
                timeout -= Time.deltaTime;
                yield return null;
            }
            SetSpeed(0f);

            var player = Player.m_localPlayer;
            if (player == null) { Destroy(gameObject); yield break; }

            int toDrop = Mathf.Min(Plugin.ThiefAmount.Value, CoinTheft.CoinsCarried(player));
            int fee = Mathf.Min(Plugin.ThiefFee.Value, Mathf.Max(0, CoinTheft.CoinsCarried(player) - toDrop));
            if (toDrop <= 0 || !FindDryEscape(player, out Vector3 escapeDir))
            {
                TrySayLine(SceneLine("thief_nope", "{me} want shiny... next time.", "Hmm. No. Too risky."), 2.5f);
                yield return new WaitForSeconds(1.5f);
                yield return RunOffAndVanish(RunSpeed, Plugin.ExitRunDistance.Value);
                yield break;
            }

            // 2. The grab, with whatever animation the 'Thief Grab Animation' setting names.
            FaceDirection(Flat(player.transform.position - transform.position));
            float grabTime = PlayGrabAnimation(Plugin.ThiefGrabAnimation.Value);
            TrySayLine(SceneLine("thief_steal", "Shiny! {me} take!", "Ooh, coins! Mine now!", "{me} borrow these!"), 2.5f);
            Sfx(SoundMoment.Steal);
            Plugin.Log.LogInfo($"Coin Thief: grabbing {toDrop} coins (fee {fee}).");
            yield return new WaitForSeconds(grabTime);
            RestoreEmote();

            // 3. Run off, dropping a coin every few metres. Drop first, then take it from the player.
            float spacing = Plugin.ThiefCoinSpacing.Value;
            float speed = RunSpeed * 1.2f;
            float run = 0f, sinceDrop = spacing * 0.5f;
            int dropped = 0;
            while (dropped < toDrop && run < 80f)
            {
                float step = speed * Time.deltaTime;
                MoveToward(escapeDir, step, speed);
                run += step;
                sinceDrop += step;

                if (sinceDrop >= spacing && !IsUnderwater(transform.position))
                {
                    sinceDrop = 0f;
                    // Only while you still carry a coin he can take (you may have died, or put them in a chest, since he grabbed them):
                    // otherwise the dropped coin would be a free one.
                    var p = Player.m_localPlayer;
                    if (p == null || p.IsDead() || CoinTheft.CoinsCarried(p) < 1)
                    {
                        Plugin.Log.LogInfo("Coin Thief: you have no coins left for him to drop, so he stops.");
                        break;
                    }
                    if (CoinTheft.DropOneCoin(transform.position + Vector3.up * 0.6f - escapeDir * 0.3f))
                    {
                        SfxAt(SoundMoment.CoinDrop);
                        CoinTheft.TakeCoins(p, 1);
                        dropped++;
                    }
                    else
                    {
                        Plugin.Log.LogWarning("Coin Thief: couldn't drop a coin, so he stops stealing. You lost nothing.");
                        break;
                    }
                }
                yield return null;
            }

            // 4. Optional fee, then the getaway.
            if (fee > 0 && dropped == toDrop)
            {
                var p = Player.m_localPlayer;
                if (p != null) CoinTheft.TakeCoins(p, Mathf.Min(fee, CoinTheft.CoinsCarried(p)));
                TrySayLine(SceneLine("thief_fee", "{me} keep a little. Fee!", "Finder's fee! Hehehe!").Replace("{n}", fee.ToString()), 2.5f);
            }
            else TrySayLine(SceneLine("thief_oops", "Oops! Hole in pocket!", "Wait... where shiny go?", "{me} bad at stealing."), 2.5f);

            Plugin.Log.LogInfo($"Coin Thief: dropped {dropped} coin(s) along his path{(fee > 0 && dropped == toDrop ? $", kept {fee}" : "")}.");
            yield return RunOffAndVanish(speed, 6f);
        }

        /// <summary>
        /// Plays the grab move. "none" = no animation; his own moves (jump, stagger, swing_longsword, spear_throw)
        /// are triggered directly; anything else is looked up in the player's animations. Returns how long to wait.
        /// </summary>
        private float PlayGrabAnimation(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0.4f;

            if (_animParams.Contains(name))
            {
                SetTrigger(name);
                return 0.6f;
            }

            var clip = PlayEmote(new[] { name });
            if (clip == null)
            {
                Plugin.Log.LogInfo($"Coin Thief: no animation called '{name}'. Use 'none', one of his own (jump, stagger, " +
                                   "swing_longsword, spear_throw), or a name from the 'Player animations' log line.");
                return 0.4f;
            }
            Plugin.DebugLog($"Coin Thief grab: {clip.name}");
            return Mathf.Clamp(clip.length, 0.4f, 1.5f); // keep the grab snappy even if the animation is long
        }

        /// <summary>Picks a direction away from the player with dry, walkable ground for the whole coin trail.</summary>
        private bool FindDryEscape(Player p, out Vector3 dir)
        {
            Vector3 away = Flat(transform.position - p.transform.position);
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            float length = Plugin.ThiefCoinSpacing.Value * (Plugin.ThiefAmount.Value + 1) + 4f;

            foreach (float angle in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
            {
                Vector3 d = Quaternion.Euler(0f, angle, 0f) * away;
                bool ok = true;
                float lastY = transform.position.y;
                for (float x = 3f; x <= length && ok; x += 3f)
                {
                    Vector3 probe = transform.position + d * x;
                    if (!TryGround(probe, lastY, out Vector3 ground) || IsUnderwater(ground) || Mathf.Abs(ground.y - lastY) > 3f) ok = false;
                    else lastY = ground.y;
                }
                if (ok) { dir = d; return true; }
            }
            dir = away;
            return false;
        }
    }

    /// <summary>The real-item side of the Coin Thief, kept small and careful.</summary>
    internal static class CoinTheft
    {
        private const string CoinName = "$item_coins";
        private const string CoinPrefab = "Coins";

        public static int CoinsCarried(Player p)
        {
            try { return p.GetInventory().CountItems(CoinName); }
            catch { return 0; }
        }

        public static void TakeCoins(Player p, int amount)
        {
            try { p.GetInventory().RemoveItem(CoinName, amount); }
            catch (Exception e) { Plugin.Log.LogWarning($"Coin Thief: couldn't take a coin ({e.Message})."); }
        }

        /// <summary>Drops one real coin into the world. Returns false if it couldn't (then no coin is taken).</summary>
        public static bool DropOneCoin(Vector3 position)
        {
            try
            {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(CoinPrefab) : null;
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) return false;
                var item = drop.m_itemData.Clone();
                item.m_stack = 1;
                var spawned = ItemDrop.DropItem(item, 1, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                return spawned != null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Coin Thief: dropping a coin failed ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }
    }

    /// <summary>Tells the Copycat when the local player uses an emote. Patched by hand so a mismatch can't break the mod.</summary>
    internal static class EmoteHook
    {
        public static void Postfix(Player __instance, object[] __args)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                string emote = __args != null && __args.Length > 0 ? __args[0] as string : null;
                if (!string.IsNullOrEmpty(emote)) Plugin.RaiseEmote(emote);
            }
            catch { /* never interfere with emoting */ }
        }
    }
}
