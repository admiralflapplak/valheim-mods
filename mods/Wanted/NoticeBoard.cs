using System;
using System.Linq;
using UnityEngine;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// A board's notices. They are saved on the board itself (its ZDO: ten places, each a contract or empty, and the day notices were last
    /// posted), so they live in the world save and every player sees the same. The board's owner (the game running its area) posts them
    /// at dawn: a new board starts bare (so tearing one down and building it again never brings new notices sooner than waiting would),
    /// and each new day, while it has room, it gets at least one, more likely two or three. Taking one asks the owner,
    /// which empties the slot and answers with the contract; only then does the paper go into the taker's bag (two players taking the same
    /// notice at once: one gets it, the other is told it's gone).
    /// </summary>
    internal sealed class NoticeBoard : MonoBehaviour
    {
        public const int Slots = Board.Slots;
        private const string KeyNotices = "Wanted.notices";   // the slots, ';'-separated, each Contract.Write or empty (boards from before 0.4 have five)
        private const string KeyDay = "Wanted.day";           // the day notices were last posted (-1: never, a new board)
        private const string KeyFirst = "Wanted.first";       // 1: a new board, still to have its first dawn
        private const string RpcTake = "Wanted_Take", RpcTaken = "Wanted_Taken";

        private ZNetView _view;
        private GameObject[] _slots;
        private MeshRenderer[] _papers;
        private GameObject[][] _pins;   // per slot, by Pin: the nail, the arrow, the knife
        private GameObject _pouch;
        private Container _box;
        private string _shown;
        private float _nextShow, _nextPost;
        private int _pendingSlot = -1;

        /// <summary>Every placed board this game has loaded (not build ghosts): for the spacing rule.</summary>
        internal static readonly System.Collections.Generic.List<NoticeBoard> Loaded = new System.Collections.Generic.List<NoticeBoard>();
        private float _pendingSince;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _slots = new GameObject[Slots];
            _papers = new MeshRenderer[Slots];
            _pins = new GameObject[Slots][];
            for (int i = 0; i < Slots; i++)
            {
                Transform slot = transform.Find("wanted_look/notice_" + i);
                _slots[i] = slot != null ? slot.gameObject : null;
                _papers[i] = slot != null ? slot.Find("paper")?.GetComponent<MeshRenderer>() : null;
                _pins[i] = new[] { slot?.Find("pin_nail")?.gameObject, slot?.Find("pin_arrow")?.gameObject, slot?.Find("pin_knife")?.gameObject };
            }
            _pouch = transform.Find("wanted_look/wanted_pouch")?.gameObject;
            _box = GetComponent<Container>();
            if (_view == null || !_view.IsValid()) return;   // (the build ghost: no saved data, nothing to do)
            Loaded.Add(this);
            _view.Register<int, string>(RpcTake, RPC_Take);
            _view.Register<int, string>(RpcTaken, RPC_Taken);
            Show(force: true);
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
            // (a hot reload swaps this component for the new copy's, which registers its own: registering a name twice throws)
            if (_view == null) return;
            try { _view.Unregister(RpcTake); _view.Unregister(RpcTaken); }
            catch (Exception) { }
        }

        private void Update()
        {
            if (_view == null || !_view.IsValid()) return;
            float now = Time.time;
            if (now >= _nextPost && _view.IsOwner())
            {
                _nextPost = now + 2f;
                try { Post(); ClearPouch(); }
                catch (Exception e) { Plugin.Log.LogWarning("Posting notices: " + e.Message); }
            }
            if (now >= _nextShow)
            {
                _nextShow = now + 0.5f;
                Show(force: false);
            }
            if (_pendingSlot >= 0 && now - _pendingSince > 5f) _pendingSlot = -1;   // the owner never answered (left, or the area unloaded)
        }

        // ---- the slots --------------------------------------------------------------------------------------------------

        private string[] Read()
        {
            var slots = new string[Slots];
            string[] saved = (_view.GetZDO().GetString(KeyNotices, "") ?? "").Split(';');
            for (int i = 0; i < Slots; i++) slots[i] = i < saved.Length ? saved[i] : "";
            return slots;
        }

        private void Write(string[] slots) => _view.GetZDO().Set(KeyNotices, string.Join(";", slots));

        /// <summary>How many notices hang on the board.</summary>
        public int Count() => _view != null && _view.IsValid() ? Read().Count(s => Contract.Read(s) != null) : 0;

        /// <summary>The contract hanging in this slot, or null.</summary>
        public Contract ContractAt(int slot) =>
            _view != null && _view.IsValid() && slot >= 0 && slot < Slots ? Contract.Read(Read()[slot]) : null;

        /// <summary>Papers shown for the slots that hold a notice, each in its kind's paper; the empty ones hidden (their hover too).</summary>
        private void Show(bool force)
        {
            if (_pouch != null) _pouch.SetActive(!string.IsNullOrEmpty(_view.GetZDO().GetString(HandIn.KeyPouch, "")));
            string saved = _view.GetZDO().GetString(KeyNotices, "");
            if (!force && saved == _shown) return;
            _shown = saved;
            string[] slots = Read();
            for (int i = 0; i < Slots; i++)
            {
                Contract c = Contract.Read(slots[i]);
                if (_slots[i] != null) _slots[i].SetActive(c != null);
                if (c != null)
                    for (int p = 0; p < _pins[i].Length; p++)
                        if (_pins[i][p] != null) _pins[i][p].SetActive(p == (int)c.Pin || (p == 0 && (int)c.Pin >= _pins[i].Length));
                if (c != null && _papers[i] != null)
                {
                    Material m = Paper.For(c.Kind, ZNetScene.instance);
                    if (m != null) _papers[i].sharedMaterial = m;
                }
            }
        }

        /// <summary>On the owner: once none of the rewards paid into the box are left in it, the pouch comes down.</summary>
        private void ClearPouch()
        {
            string waiting = _view.GetZDO().GetString(HandIn.KeyPouch, "");
            if (string.IsNullOrEmpty(waiting) || _box == null) return;
            var names = waiting.Split(',');
            bool any = _box.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && names.Contains(i.m_dropPrefab.name));
            if (!any) _view.GetZDO().Set(HandIn.KeyPouch, "");
        }

        // ---- posting (the owner) ------------------------------------------------------------------------------------------

        private void Post()
        {
            if (EnvMan.instance == null || ZNetScene.instance == null) return;
            int today = EnvMan.instance.GetDay();
            TakeDownOrphans();
            int last = _view.GetZDO().GetInt(KeyDay, -1);
            if (last == today) return;

            if (last < 0 || today < last)
            {
                _view.GetZDO().Set(KeyDay, today);   // a new board (or the world's clock went back): its first notices come at the next dawn
                if (last < 0) _view.GetZDO().Set(KeyFirst, 1);
                return;
            }
            string[] slots = Read();
            var rnd = new System.Random();
            int added = 0;
            if (_view.GetZDO().GetInt(KeyFirst) == 1)
            {
                // a new board's first dawn fills it well (losing what a board had is the price of building it again)
                int lo = Plugin.FirstDawnLeast.Value, hi = Math.Max(lo, Plugin.FirstDawnMost.Value);
                added += Fill(slots, rnd.Next(lo, hi + 1), rnd);
                _view.GetZDO().Set(KeyFirst, 0);
            }
            else
                for (int day = 0; day < Math.Min(today - last, 5); day++)   // each dawn passed (sleeping through several counts each)
                    added += Fill(slots, DawnCount(rnd), rnd);

            if (added > 0) Write(slots);
            _view.GetZDO().Set(KeyDay, today);
        }

        /// <summary>
        /// Takes down notices for a creature or thing the game no longer has (the mod that added it was removed): nobody could ever do
        /// them, and they'd hold a place on the board for good.
        /// </summary>
        private void TakeDownOrphans()
        {
            string[] slots = Read();
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Contract c = Contract.Read(slots[i]);
                if (c == null || Contracts.Known(c.Target)) continue;
                slots[i] = "";
                changed = true;
            }
            if (changed) Write(slots);
        }

        /// <summary>
        /// How many notices a dawn brings: one, and each more with its chance in turn (the setting "75, 55, 40, 20": a second 75%, a third
        /// 55% of those, ...), so with the defaults one 25%, two 35%, three 25%, four 12%, five 3% of dawns.
        /// </summary>
        private static int DawnCount(System.Random rnd)
        {
            int count = 1;
            foreach (string part in (Plugin.MoreChances.Value ?? "").Split(','))
            {
                if (!float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float chance)) break;
                if (rnd.NextDouble() * 100.0 >= chance) break;
                count++;
            }
            return count;
        }

        /// <summary>Puts up to this many new notices in empty places, never more than the board may show at once; how many it put up.</summary>
        private static int Fill(string[] slots, int count, System.Random rnd)
        {
            int added = 0;
            for (int n = 0; n < count; n++)
            {
                if (slots.Count(x => !string.IsNullOrEmpty(x)) >= Plugin.MaxNotices.Value) break;
                // a random empty place, so notices don't always fill from the top left
                int[] empties = Enumerable.Range(0, Slots).Where(i => string.IsNullOrEmpty(slots[i])).ToArray();
                if (empties.Length == 0) break;
                Contract c = Contracts.Make(rnd);
                if (c == null) break;
                slots[empties[rnd.Next(empties.Length)]] = c.Write();
                added++;
            }
            return added;
        }

        // ---- taking one -----------------------------------------------------------------------------------------------------

        /// <summary>The local player takes the notice in this slot (asks the owner; the paper comes when it answers).</summary>
        public void RequestTake(int slot)
        {
            Player player = Player.m_localPlayer;
            Contract c = ContractAt(slot);
            if (player == null || c == null || _pendingSlot >= 0) return;
            int most = Plugin.MaxCarried.Value;
            if (NoticeItem.Carried(player) >= most) { player.Message(MessageHud.MessageType.Center, T("wanted_too_many", most)); return; }
            if (!player.GetInventory().HaveEmptySlot()) { player.Message(MessageHud.MessageType.Center, T("wanted_bag_full")); return; }
            _pendingSlot = slot;
            _pendingSince = Time.time;
            _view.InvokeRPC(RpcTake, slot, c.Id);
        }

        /// <summary>On the owner: hand the notice over if it's still there (the slot empties at once), or say no.</summary>
        private void RPC_Take(long sender, int slot, string id)
        {
            try
            {
                string answer = "";
                if (_view.IsOwner() && slot >= 0 && slot < Slots)
                {
                    string[] slots = Read();
                    Contract c = Contract.Read(slots[slot]);
                    if (c != null && c.Id == id)
                    {
                        answer = slots[slot];
                        slots[slot] = "";
                        Write(slots);
                        Show(force: true);
                    }
                }
                _view.InvokeRPC(sender, RpcTaken, slot, answer);
            }
            catch (Exception e) { Plugin.Log.LogError("Taking a notice (owner): " + e); }
        }

        /// <summary>On the taker: the owner's answer. Only for a notice this game asked for.</summary>
        private void RPC_Taken(long sender, int slot, string answer)
        {
            try
            {
                if (slot != _pendingSlot) return;
                _pendingSlot = -1;
                Player player = Player.m_localPlayer;
                if (player == null) return;
                Contract c = Contract.Read(answer);
                if (c == null) { player.Message(MessageHud.MessageType.Center, T("wanted_gone")); return; }
                NoticeItem.Give(player, c);
                player.Message(MessageHud.MessageType.TopLeft, T("wanted_took", c.Line()));
            }
            catch (Exception e) { Plugin.Log.LogError("Taking a notice: " + e); }
        }
    }

    /// <summary>One paper on a board: its hover text is the notice, E takes it.</summary>
    internal sealed class NoticePaper : MonoBehaviour, Hoverable, Interactable
    {
        private int _slot = -1;
        private NoticeBoard _board;

        private void Awake()
        {
            int.TryParse(name.Substring(name.LastIndexOf('_') + 1), out _slot);   // "notice_3"
            _board = GetComponentInParent<NoticeBoard>();
        }

        public string GetHoverText()
        {
            Contract c = _board != null ? _board.ContractAt(_slot) : null;
            if (c == null) return "";
            return Localization.instance.Localize(Contracts.Describe(c) + "\n[<color=yellow><b>$KEY_Use</b></color>] " + T("wanted_take"));
        }

        public string GetHoverName() => T("wanted_notice_name");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _board == null || user != Player.m_localPlayer) return false;
            _board.RequestTake(_slot);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    /// <summary>The board's wood: says what it is, so looking at the planks doesn't offer to open the box.</summary>
    internal sealed class BoardFace : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverText()
        {
            NoticeBoard board = GetComponentInParent<NoticeBoard>();
            string hint = board != null && board.Count() == 0 ? T("wanted_board_empty") : T("wanted_board_hint");
            return Localization.instance.Localize("$wanted_board") + "\n<size=80%><color=#bdb7a9>" + hint + "</color></size>";
        }
        public string GetHoverName() => Localization.instance.Localize("$wanted_board");
        public float GetHoverOffset() => 0f;
        public bool Interact(Humanoid user, bool hold, bool alt) => false;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
