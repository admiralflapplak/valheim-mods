using System;
using HarmonyLib;
using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Waits until the player has respawned, then sends the Fuling in person or shows a text message.
    /// </summary>
    internal class Delivery : MonoBehaviour
    {
        private Decision _pending;
        private Player _deadBody;
        private float _aliveSince = -1f;

        public void QueueAfterRespawn(Player deadBody, Decision d)
        {
            _pending = d;
            _deadBody = deadBody;
            _aliveSince = -1f;
        }

        private void Update()
        {
            if (_pending == null) return;

            var p = Player.m_localPlayer;
            if (p == null || p == _deadBody || p.IsDead() || p.IsTeleporting())
            {
                _aliveSince = -1f;
                return;
            }

            if (_aliveSince < 0f) _aliveSince = Time.time;
            if (Time.time - _aliveSince < Plugin.DeliveryDelaySeconds.Value) return;

            var d = _pending;
            _pending = null;
            Deliver(p, d.Line, d.IsSpecial, d.IsMilestone, deathChain: d.DeathChain);
        }

        public void Deliver(Player p, string line, bool special, bool milestone, bool emoteTest = false, bool? forceTorch = null, bool isTest = false, string forcedEmote = null, string forcedHelmet = null, bool forceOutfit = false, bool fitting = false, System.Collections.Generic.List<string> deathChain = null)
        {
            var mode = Plugin.DeliveryModeSetting.Value;
            bool inPerson = isTest || emoteTest || forceTorch.HasValue
                || mode == DeliveryMode.Always || (mode == DeliveryMode.SpecialOnly && special);

            if (inPerson)
            {
                var show = new Performance
                {
                    Line = line,
                    IsMilestone = milestone,
                    DeathChain = deathChain,
                    EmoteTest = emoteTest,
                    ForceTorch = forceTorch,
                    ForcedEmote = forcedEmote,
                    ForcedHelmet = forcedHelmet,
                    ForceOutfit = forceOutfit,
                    Fitting = fitting,
                    FallbackToText = ShowLine
                };
                if (FulingActor.TrySpawn(p, show)) return;
            }
            ShowLine(line);
        }

        /// <summary>Chat.m_hideTimer is private: zero makes the chat window show, as a new message from a player does.</summary>
        private static readonly AccessTools.FieldRef<Chat, float> ChatHideTimer = AccessTools.FieldRefAccess<Chat, float>("m_hideTimer");

        internal static void ShowLine(string text)
        {
            var where = Plugin.TextPos.Value;
            if (where == TextPosition.Off) { Plugin.Log.LogInfo("Fuling's line by text: none (Text Position is Off)."); return; }
            string line = Plugin.FillMe(text);
            Plugin.Log.LogInfo($"Fuling's line by text ({where}): {line}");
            if (where == TextPosition.Chat && Chat.instance != null)
            {
                Chat.instance.AddString(Look.Name, line, Talker.Type.Normal, false);
                try { ChatHideTimer(Chat.instance) = 0f; } catch { /* the line is there; the window just doesn't pop up */ }
                return;
            }
            if (MessageHud.instance == null) return;
            string msg = $"<color=#E07A3A><b>{Look.Name}:</b></color> {line}";
            var type = where == TextPosition.Center ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft;
            MessageHud.instance.ShowMessage(type, msg);
        }

        public static void ShowPlainText(string text)
        {
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}
