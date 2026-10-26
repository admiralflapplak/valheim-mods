using System;
using System.Linq;
using UnityEngine;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// A board's notices. They are saved on the board itself (its ZDO: five slots, each a contract or empty, and the day notices were last
    /// posted), so they live in the world save and every player sees the same. The board's owner (the game running its area) posts them:
    /// a new board starts with a few, and each new day, if there's room, there's a chance of one to three more. Taking one asks the owner,
    /// which empties the slot and answers with the contract; only then does the paper go into the taker's bag (two players taking the same
    /// notice at once: one gets it, the other is told it's gone).
    /// </summary>
    internal sealed class NoticeBoard : MonoBehaviour
    {
        public const int Slots = Board.Slots;
        private const string KeyNotices = "Wanted.notices";   // the five slots, ';'-separated, each Contract.Write or empty
        private const string KeyDay = "Wanted.day";           // the day notices were last posted (-1: never, a new board)
        private const string RpcTake = "Wanted_Take", RpcTaken = "Wanted_Taken";

        private ZNetView _view;
        private GameObject[] _slots;
        private MeshRenderer[] _papers;
        private GameObject _pouch;
        private Container _box;
        private string _shown;
        private float _nextShow, _nextPost;
        private int _pendingSlot = -1;
        private float _pendingSince;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _slots = new GameObject[Slots];
            _papers = new MeshRenderer[Slots];
            for (int i = 0; i < Slots; i++)
            {
                Transform slot = transform.Find("wanted_look/notice_" + i);
                _slots[i] = slot != null ? slot.gameObject : null;
                _papers[i] = slot != null ? slot.Find("paper")?.GetComponent<MeshRenderer>() : null;
            }
            _pouch = transform.Find("wanted_look/wanted_pouch")?.gameObject;
            _box = GetComponent<Container>();
            if (_view == null || !_view.IsValid()) return;   // (the build ghost: no saved data, nothing to do)
            _view.Register<int, string>(RpcTake, RPC_Take);
            _view.Register<int, string>(RpcTaken, RPC_Taken);
            Show(force: true);
        }

        private void OnDestroy()
        {
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
            int last = _view.GetZDO().GetInt(KeyDay, -1);
            if (last == today) return;

            string[] slots = Read();
            var rnd = new System.Random();
            int added = 0;
            if (last < 0 || today < last)
                added += Fill(slots, Plugin.FirstNotices.Value, rnd);   // a new board (or the world's clock went back): a few to start with
            else
                for (int day = 0; day < Math.Min(today - last, 5); day++)   // each day passed (sleeping through several counts each)
                    if (rnd.NextDouble() * 100.0 < Plugin.DailyChance.Value) added += Fill(slots, rnd.Next(1, 4), rnd);

            if (added > 0) Write(slots);
            _view.GetZDO().Set(KeyDay, today);
        }

        /// <summary>Puts up to this many new notices in empty slots; how many it put up.</summary>
        private static int Fill(string[] slots, int count, System.Random rnd)
        {
            int added = 0;
            for (int n = 0; n < count; n++)
            {
                // a random empty slot, so notices don't always fill from the top left
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
        public string GetHoverText() => Localization.instance.Localize("$wanted_board") + "\n<size=80%><color=#bdb7a9>" + T("wanted_board_hint") + "</color></size>";
        public string GetHoverName() => Localization.instance.Localize("$wanted_board");
        public float GetHoverOffset() => 0f;
        public bool Interact(Humanoid user, bool hold, bool alt) => false;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
