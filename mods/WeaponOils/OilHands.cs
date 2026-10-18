using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponOils
{
    /// <summary>
    /// The oil on a player's bare hands (an oil used with no weapon in hand): their unarmed blows carry it as a weapon's would. The game's
    /// "weapon" for an empty hand is one item shared by every player, so the oil can't be kept on it: it is kept on the player instead, in
    /// Player.m_customData (saved with the character), running out at a point in world time like a weapon's. While it lasts the player has a
    /// status effect with the oil's icon and its time left (top right, as a mead's), and both hands carry the weapon glow (OilGlow, which
    /// other players see from the player object). OilState.Active answers for the empty-hand "weapon" from here, so every effect works.
    /// </summary>
    internal static class OilHands
    {
        private const string IdKey = "WeaponOils.hands";
        private const string UntilKey = "WeaponOils.hands.until";
        internal const string GlowKey = "WeaponOils.glow.hands";   // the oil id on the player object, for other players' games ("" when none)
        private const string HolderName = "nick_oilhands";

        private static readonly Dictionary<string, int> Hashes = new Dictionary<string, int>();   // the status effect of each oil, by oil id
        private static readonly List<GameObject> Holders = new List<GameObject>();
        private static float _nextGlow;

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;

        /// <summary>The game's "weapon" for an empty hand (the PlayerUnarmed prefab's item, named plain "Unarmed", not a $key).</summary>
        internal static bool IsHands(ItemDrop.ItemData item) =>
            item != null && (item.m_shared.m_name == "Unarmed" || item.m_shared.m_name == "$item_unarmed"
                             || (item.m_dropPrefab != null && item.m_dropPrefab.name == "PlayerUnarmed"));

        /// <summary>A weapon worn on the fists (claws: anything that trains the unarmed skill), which is oiled as a weapon but with the palms pose.</summary>
        internal static bool IsFist(ItemDrop.ItemData item) => item != null && !IsHands(item) && item.m_shared.m_skillType == Skills.SkillType.Unarmed;

        /// <summary>Oil on the hands is lost with the body, as food is (the game clears food in the same place).</summary>
        [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class Player_OnDeath
        {
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || Active(__instance, out _) == null) return;
                Clear(__instance);
                foreach (int hash in Hashes.Values) StatusEffects.Remove(__instance, hash);
            }
        }

        /// <summary>Makes the status effect of one oil (once its item, and so its icon, exists).</summary>
        internal static void MakeEffect(OilDef oil, Sprite icon)
        {
            string name = "nick_weaponoils_hands_" + oil.Id;
            var se = StatusEffects.Create<SE_OilHands>(name, "$" + OilItems.OilPrefix + oil.Id, icon, "$" + OilItems.OilPrefix + oil.Id + "_desc");
            se.Id = oil.Id;
            se.m_ttl = Plugin.DurationMinutes.Value * 60f;
            se.m_stopMessageType = MessageHud.MessageType.Center;
            se.m_stopMessage = "$nick_weaponoils_hands_worn_off";
            Hashes[oil.Id] = name.GetStableHashCode();
        }

        /// <summary>The oil on this player's hands, or null (an expired one is cleared).</summary>
        internal static OilDef Active(Player player, out float secondsLeft)
        {
            secondsLeft = 0f;
            if (player == null || !player.m_customData.TryGetValue(IdKey, out string id)) return null;
            if (!player.m_customData.TryGetValue(UntilKey, out string until)
                || !double.TryParse(until, NumberStyles.Float, CultureInfo.InvariantCulture, out double end) || end <= Now)
            {
                Clear(player);
                return null;
            }
            secondsLeft = (float)(end - Now);
            return OilDefs.Get(id);
        }

        internal static void Clear(Player player)
        {
            player.m_customData.Remove(IdKey);
            player.m_customData.Remove(UntilKey);
        }

        /// <summary>Puts the oil on the player's hands (replacing any) and shows its effect.</summary>
        internal static void Apply(Player player, OilDef oil, float seconds)
        {
            player.m_customData[IdKey] = oil.Id;
            player.m_customData[UntilKey] = (Now + seconds).ToString("R", CultureInfo.InvariantCulture);
            foreach (KeyValuePair<string, int> e in Hashes)
                if (e.Key != oil.Id) StatusEffects.Remove(player, e.Value);   // (quietly: an oil replaced hasn't worn off)
            if (Hashes.TryGetValue(oil.Id, out int hash)) StatusEffects.Apply(player, hash, resetTime: true);
        }

        /// <summary>
        /// Once a second: the local player's effect is given back when it is missing while the oil lasts (after a login, or a reload of
        /// the mod), and both hands of every player get the glow's holder (it shows only while that player's hands are oiled).
        /// </summary>
        internal static void Update()
        {
            if (Time.time < _nextGlow) return;
            _nextGlow = Time.time + 1f;
            Player local = Player.m_localPlayer;
            if (local != null && Active(local, out _) is OilDef oil && Hashes.TryGetValue(oil.Id, out int hash) && !StatusEffects.Has(local, hash))
                StatusEffects.Apply(local, hash, resetTime: true);   // (its time is the player's: SE_OilHands sets it)
            foreach (Player player in Player.GetAllPlayers()) Equip(player);
        }

        /// <summary>A glow holder on each of the player's hand bones, scaled so that below it a unit is a metre.</summary>
        private static void Equip(Player player)
        {
            Animator animator = player != null ? player.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman) return;
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand })
            {
                Transform hand = animator.GetBoneTransform(bone);
                if (hand == null || hand.Find(HolderName) != null) continue;
                var holder = new GameObject(HolderName);
                holder.transform.SetParent(hand, false);
                Vector3 s = hand.lossyScale;
                holder.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(s.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(s.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
                holder.AddComponent<OilGlow>().Where = OilGlow.Slot.Hands;
                Holders.Add(holder);
            }
        }

        internal static void Forget()
        {
            foreach (GameObject holder in Holders) if (holder != null) Object.Destroy(holder);   // (their glow is this copy's code)
            Holders.Clear();
            Hashes.Clear();
        }
    }

    /// <summary>The icon and timer of oil on the hands. Its time is the player's oil, kept in world time (OilHands), not its own count.</summary>
    internal sealed class SE_OilHands : StatusEffect
    {
        public string Id;

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            OilDef oil = OilHands.Active(m_character as Player, out float left);
            if (oil == null || oil.Id != Id) { m_time = m_ttl + 1f; return; }   // worn off (or replaced): done
            if (left > m_ttl) m_ttl = left;   // (the duration setting was lowered since it went on)
            m_time = m_ttl - left;
        }
    }
}
