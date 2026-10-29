using TMPro;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The riddle window (GameUi, in the game's own look): Muninn's name, his riddle, and three answers to click. A click only notes the
    /// answer; Tick sends it (never acted on inside a click). Closes on E, Escape, Tab, walking away, or when another has answered.
    /// </summary>
    internal static class RiddleWindow
    {
        private static RectTransform _window;
        private static MuninnVisitor _raven;
        private static Riddle _riddle;
        private static float _openedAt, _closedAt = -1f;
        private static int _chosen = -1;

        internal static bool IsOpen => _window != null;
        internal static bool JustClosed => Time.unscaledTime < _closedAt + 0.25f;

        internal static void Open(MuninnVisitor raven, Riddle riddle)
        {
            if (!GameUi.Ready()) return;
            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = Close;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _raven = raven;
            _riddle = riddle;
            _chosen = -1;
            _openedAt = Time.unscaledTime;
            Build();
        }

        public static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null;
            _raven = null;
            _closedAt = Time.unscaledTime;
        }

        internal static void Tick()
        {
            if (_window == null) return;
            Player me = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");
            if (me == null || me.IsDead() || global::Menu.IsVisible() || useAgain || _raven == null
                || Vector3.Distance(me.transform.position, _raven.transform.position) > 10f) { Close(); return; }
            if (_raven.Answered)
            {
                Close();
                me.Message(MessageHud.MessageType.Center, "$goodtidings_muninn_taken");
                return;
            }
            if (_chosen >= 0)
            {
                MuninnVisitor raven = _raven;
                bool right = _chosen == _riddle.Correct;
                Close();
                raven.Answer(right);
            }
        }

        private static void Build()
        {
            const float W = 640f;
            _window = GameUi.Window("GoodTidingsRiddle", new Vector2(W, 470f));
            GameUi.Label(_window, Localization.instance.Localize("$goodtidings_muninn"), new Vector2(0f, -18f), new Vector2(W, 50f), 34f,
                GameUi.Warm, GameUi.Norse, TextAlignmentOptions.Top, new Vector2(0.5f, 1f));
            GameUi.Braid(_window, new Vector2(0f, -72f), W - 80f);
            GameUi.Label(_window, _riddle.Text, new Vector2(40f, -100f), new Vector2(W - 80f, 140f), 22f, GameUi.Text, GameUi.Serif,
                TextAlignmentOptions.Center);
            for (int i = 0; i < _riddle.Answers.Length; i++)
            {
                int k = i;
                GameUi.Button(_window, _riddle.Answers[i], new Vector2(70f, -260f - i * 62f), new Vector2(W - 140f, 50f), () => _chosen = k, true, 20f);
            }
        }
    }
}
