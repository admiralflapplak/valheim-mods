using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WisplightArrows
{
    /// <summary>
    /// The game never shows the arrow on the string, so a drawn wisplight arrow shows as a small wisp at the bow hand, clearing the mist
    /// a little way round the archer. Only the archer's game knows what arrow is drawn (Humanoid's draw time and ammo aren't sent), so it
    /// writes "drawing one" on its player's ZDO when that changes (never every frame, and an explicit 0 when it stops: a removed key would
    /// stay on other games), and every game shows the wisp on each player who has it set.
    /// </summary>
    internal static class NockGlow
    {
        private const string DrawingKey = "nick_wisparrow_drawing";
        private const float Linger = 0.5f;   // seconds the wisp stays after the draw ends: the arrow takes a moment to leave the bow

        private static readonly Dictionary<Player, GameObject> Shown = new Dictionary<Player, GameObject>();
        private static readonly List<Player> Gone = new List<Player>();
        private static readonly Dictionary<Player, float> LastDrawn = new Dictionary<Player, float>();   // when each was last seen drawing one
        private static bool _sent;
        private static Player _sentFor;   // a new character (or world) always gets the value once: a saved 1 from a crash mid-draw is cleared

        public static void Update()
        {
            Player local = Player.m_localPlayer;
            if (local != null) Send(local, IsDrawingOne(local));

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null) continue;
                bool drawing = player == local ? _sent : Flag(player);
                if (drawing) LastDrawn[player] = Time.time;
                else if (LastDrawn.TryGetValue(player, out float last) && Time.time - last < Linger) drawing = true;
                Shown.TryGetValue(player, out GameObject glow);
                if (drawing && glow == null) Shown[player] = Show(player);
                else if (!drawing && glow != null) { Object.Destroy(glow); Shown.Remove(player); }
            }

            foreach (KeyValuePair<Player, GameObject> kv in Shown)
                if (kv.Key == null || kv.Value == null) Gone.Add(kv.Key);
            foreach (Player p in Gone)
            {
                if (Shown.TryGetValue(p, out GameObject glow) && glow != null) Object.Destroy(glow);
                Shown.Remove(p);
                LastDrawn.Remove(p);
            }
            Gone.Clear();
        }

        /// <summary>Clear radius changed: the wisps already shown follow it.</summary>
        public static void ApplyRadius()
        {
            foreach (GameObject glow in Shown.Values)
                if (glow != null)
                    foreach (ParticleSystemForceField f in glow.GetComponentsInChildren<ParticleSystemForceField>(true)) f.endRange = Plugin.DrawRadius.Value;
        }

        /// <summary>On unload: every wisp gone, and the local player no longer marked as drawing.</summary>
        public static void Reset()
        {
            if (Player.m_localPlayer != null) Send(Player.m_localPlayer, false);
            foreach (GameObject glow in Shown.Values) if (glow != null) Object.Destroy(glow);
            Shown.Clear();
            LastDrawn.Clear();
            _sent = false;
            _sentFor = null;
        }

        private static bool IsDrawingOne(Player player)
        {
            if (player.GetAttackDrawPercentage() <= 0f) return false;
            ItemDrop.ItemData ammo = player.GetAmmoItem();
            return ammo != null && ammo.m_dropPrefab != null && ammo.m_dropPrefab.name == ArrowItem.PrefabName;
        }

        private static bool Flag(Player player)
        {
            ZNetView nview = player.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() && nview.GetZDO().GetInt(DrawingKey) == 1;
        }

        private static void Send(Player player, bool drawing)
        {
            if (drawing == _sent && player == _sentFor) return;
            ZNetView nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            nview.GetZDO().Set(DrawingKey, drawing ? 1 : 0);
            _sent = drawing;
            _sentFor = player;
        }

        private static GameObject Show(Player player)
        {
            GameObject template = ArrowItem.NockTemplate;
            VisEquipment vis = player.GetComponent<VisEquipment>();
            Transform hand = vis != null && vis.m_leftHand != null ? vis.m_leftHand : player.transform;
            if (template == null) return null;
            GameObject glow = Object.Instantiate(template, hand.position, Quaternion.identity);
            glow.transform.SetParent(hand, true);   // (keeping its own size in the world: the skeleton's bones are scaled)
            foreach (ParticleSystemForceField f in glow.GetComponentsInChildren<ParticleSystemForceField>(true)) f.endRange = Plugin.DrawRadius.Value;
            return glow;
        }
    }
}
