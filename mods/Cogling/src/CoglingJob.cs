using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// What a Cogling is carrying, kept in his own saved data (his ZDO), so a carried item is never lost: if the game closes mid-trip
    /// he finishes the delivery when his area loads again, and if he's taken down or broken he drops it. Saved as a one-slot inventory
    /// (the game's own item saving: stack, quality, durability, custom data). Its prefab name is kept too, for every player's game to
    /// show it in his hands.
    /// </summary>
    internal static class Carried
    {
        internal const string ItemKey = "cogling_carry", PrefabKey = "cogling_carry_prefab", ActKey = "cogling_act";
        internal const string AwakeKey = "cogling_awake";   // powered up (else down, as a new Cogling starts: he boots up)
        internal const string MoodKey = "cogling_mood";     // frightened (step 8: red eyes, his cry, cowering) or calm
        internal const int MoodCalm = 0, MoodScared = 1, MoodWary = 2;
        internal const int ActNone = 0, ActBend = 1, ActDeposit = 2;

        internal static ItemDrop.ItemData Read(ZDO zdo)
        {
            byte[] bytes = zdo?.GetByteArray(ItemKey);
            if (bytes == null || bytes.Length == 0) return null;
            var inv = new Inventory("cogling", null, 1, 1);
            inv.Load(new ZPackage(bytes));
            return inv.GetAllItems().FirstOrDefault();
        }

        internal static void Write(ZDO zdo, ItemDrop.ItemData item)
        {
            if (zdo == null) return;
            if (item == null) { zdo.Set(ItemKey, new byte[0]); zdo.Set(PrefabKey, ""); return; }
            var inv = new Inventory("cogling", null, 1, 1);
            inv.AddItem(item.Clone());
            var pkg = new ZPackage();
            inv.Save(pkg);
            zdo.Set(ItemKey, pkg.GetArray());
            zdo.Set(PrefabKey, item.m_dropPrefab != null ? item.m_dropPrefab.name : "");
        }
    }

    /// <summary>
    /// His first job: an item from the floor to its chest (Sorting.Choose). Walk to the item, bend and pick it up (the game's own hand
    /// over: RequestOwn, taken only once it's his), carry it (saved: Carried), walk to the chest, open the lid, put it in, close the lid.
    /// The chest is taken over and reloaded before he touches it (ContainerAccess), so nothing is duplicated or lost; a chest that filled
    /// up meanwhile: another; none: he puts the item down where he stands.
    /// </summary>
    internal sealed class CoglingJob
    {
        internal enum State { Working, Done }
        private enum Phase { ToItem, Picking, ToChest, Opening, Closing, ToSource, Taking, TakenClosing }

        private readonly CoglingAI _ai;
        private readonly CoglingNavigator _nav;
        private Phase _phase;
        private ItemDrop _item;
        private ZDOID _itemId;
        private Container _chest;
        private Container _source;     // tidying: the chest he takes a stack from
        private Container _goal;       // tidying: the chest it's for (else Sorting chooses)
        private string _takeName;
        private int _takeQuality;
        private float _until;
        private List<Antenna> _network;
        private float _nextSearchLog;

        internal CoglingJob(CoglingAI ai, CoglingNavigator nav)
        {
            _ai = ai;
            _nav = nav;
        }

        private ZDO Zdo => _ai.GetComponent<ZNetView>()?.GetZDO();
        internal bool Carrying => Carried.Read(Zdo) != null;

        /// <summary>What the job is at, for the task line under his name: a key and its words (count, item name key).</summary>
        internal (string key, string a, string b) Describe()
        {
            ItemDrop.ItemData carried = Carried.Read(Zdo);
            switch (_phase)
            {
                case Phase.ToItem:
                case Phase.Picking:
                    return _item != null ? ("cogling_task_fetching", _item.m_itemData.m_stack.ToString(), _item.m_itemData.m_shared.m_name) : ("cogling_task_busy", "", "");
                case Phase.ToSource:
                case Phase.Taking:
                    return ("cogling_task_tidying", "", "$item_" + (_takeName ?? "").Replace("$item_", ""));
                default:
                    return carried != null ? ("cogling_task_carrying", carried.m_stack.ToString(), carried.m_shared.m_name) : ("cogling_task_busy", "", "");
            }
        }

        /// <summary>An item in the area worth taking (one that has a chest to go to), and off he goes. False: nothing to do.</summary>
        internal bool TryStart(List<Antenna> network)
        {
            _network = network;
            List<Container> chests = Chests.Usable(network);
            if (chests.Count == 0) return false;
            var candidates = FloorItems.Candidates(network, _ai.transform.position);
            if (Plugin.LogAI.Value && Time.time > _nextSearchLog) { _nextSearchLog = Time.time + 10f; _ai.Log($"floor search: {candidates.Count} item(s) he may take, {chests.Count} chest(s)"); }
            foreach (ItemDrop d in candidates.Take(6))
            {
                ItemDrop.ItemData load = d.m_itemData.Clone();
                load.m_stack = FloorItems.Load(d.m_itemData);   // (what he'll carry: a heavy stack in several trips)
                Container chest = Sorting.Choose(load, chests, d.transform.position, out _);
                if (chest == null) { FloorItems.NoHome(d); continue; }
                if (!_nav.SetTarget(d.transform.position))
                {
                    // (often for a moment only: the game maps where creatures can walk as areas are first visited) soon again; a long
                    // while only once it keeps failing
                    float wait = FloorItems.NoWay(d);
                    _ai.Log($"no way to {Sorting.Short(d.m_itemData)} at {d.transform.position:F1} (again in {wait:F0} s)");
                    continue;
                }
                _item = d;
                _itemId = FloorItems.Id(d);
                FloorItems.Claimed.Add(_itemId);
                _phase = Phase.ToItem;
                _ai.Log($"going for {d.m_itemData.m_stack} {Sorting.Short(d.m_itemData)} at {d.transform.position:F1}");
                return true;
            }
            return false;
        }

        /// <summary>A stack to move between chests of the area (Tidy), and off he goes to take it. False: the chests are tidy.</summary>
        internal bool TryStartTidy(List<Antenna> network)
        {
            _network = network;
            List<Container> chests = Chests.Usable(network);
            if (chests.Count < 2) return false;
            Tidy.Move move = Tidy.Next(chests, _ai.transform.position);
            if (move == null) return false;
            _source = move.From;
            _goal = move.To;
            _takeName = move.Name;
            _takeQuality = move.Quality;
            if (!_nav.SetTarget(ChestSpot(_source))) { _source = _goal = null; return false; }
            _phase = Phase.ToSource;
            _ai.Log($"tidying: {Sorting.Short(new ItemDrop.ItemData { m_shared = new ItemDrop.ItemData.SharedData { m_name = move.Name } })} from the chest at {_source.transform.position:F1} ({move.Why})");
            return true;
        }

        /// <summary>He's carrying something (a delivery cut short): on to a chest with it.</summary>
        internal bool Resume(List<Antenna> network)
        {
            _network = network;
            ItemDrop.ItemData item = Carried.Read(Zdo);
            if (item == null) return false;
            _ai.Log($"carrying {item.m_stack} {Sorting.Short(item)} from before: on to a chest");
            return ToAChest(item);
        }

        internal State Update(float dt)
        {
            State state = Step(dt);
            if (state == State.Done) _ai.Log($"job over ({_phase})");
            return state;
        }

        private State Step(float dt)
        {
            switch (_phase)
            {
                case Phase.ToItem:
                {
                    if (_item == null || !_item.GetComponent<ZNetView>().IsValid()) return Finish("the item is gone");
                    if (Utils.DistanceXZ(_ai.transform.position, _item.transform.position) < 0.9f) { StartPicking(); return State.Working; }
                    var r = _nav.Update(dt);
                    if (r == CoglingNavigator.Result.GaveUp)
                    {
                        // the closest he got: within reach he picks it up from there (items half sunk into the ground by a smelter are off
                        // the pathfinder's map); out of reach, he leaves it a good while
                        if (Utils.DistanceXZ(_ai.transform.position, _item.transform.position) < 1.6f) { StartPicking(); return State.Working; }
                        FloorItems.NoHome(_item, 300f);
                        return Finish("can't reach the item");
                    }
                    if (r == CoglingNavigator.Result.Arrived && Utils.DistanceXZ(_ai.transform.position, _item.transform.position) > 1.5f) _nav.SetTarget(_item.transform.position);   // (it rolled)
                    return State.Working;
                }
                case Phase.Picking:
                {
                    _ai.Stop();
                    if (_item == null || !_item.GetComponent<ZNetView>().IsValid()) return Finish("the item is gone");
                    ZNetView view = _item.GetComponent<ZNetView>();
                    if (!view.IsOwner()) { if (Time.time > _until) return Finish("it wasn't handed over"); _item.RequestOwn(); return State.Working; }
                    if (Time.time < _until - 1.9f) return State.Working;   // (bending down: a little over half a second)
                    // his now: saved on him first, then gone from the floor (a crash between can't lose it)
                    // as much as he can carry: the whole stack, or (too heavy for one trip) part of it, the rest left for the next
                    ItemDrop.ItemData item = _item.m_itemData.Clone();
                    int load = FloorItems.Load(_item.m_itemData);
                    item.m_stack = load;
                    Carried.Write(Zdo, item);
                    if (load < _item.m_itemData.m_stack) _item.SetStack(_item.m_itemData.m_stack - load);
                    else { ZNetScene.instance.Destroy(_item.gameObject); Scoop(item, _item.transform.position); }
                    FloorItems.Claimed.Remove(_itemId);
                    _item = null;
                    Act(Carried.ActNone);
                    _ai.Log($"picked up {item.m_stack} {Sorting.Short(item)}");
                    return ToAChest(item) ? State.Working : State.Done;
                }
                case Phase.ToChest:
                {
                    if (_chest == null || !ContainerAccess.IsStorage(_chest)) return Reroute("the chest is gone");
                    if (Near(_chest)) { StartOpening(); return State.Working; }
                    var r = _nav.Update(dt);
                    if (r == CoglingNavigator.Result.GaveUp) return Reroute("can't reach the chest", skip: _chest);
                    if (r == CoglingNavigator.Result.Arrived && !Near(_chest)) _nav.SetTarget(ChestSpot(_chest));
                    return State.Working;
                }
                case Phase.Opening:
                {
                    _ai.Stop();
                    _ai.Face(_chest != null ? _chest.transform.position : _ai.transform.position);
                    if (Time.time < _until) return State.Working;
                    ItemDrop.ItemData item = Carried.Read(Zdo);
                    if (item == null) return Close();
                    if (_chest == null || ContainerAccess.InUse(_chest) && !_chest.GetComponent<ZNetView>().IsOwner()) return Reroute("someone opened the chest");
                    Inventory inv = _chest.GetInventory();
                    if (!inv.CanAddItem(item)) return Reroute("the chest is full now", skip: _chest);
                    int count = item.m_stack;   // (AddItem merges into stacks there and counts the item down: what he brought, before)
                    inv.AddItem(item);          // (the chest is his now: the game saves it on change)
                    Carried.Write(Zdo, null);
                    _ai.Log($"put {count} {Sorting.Short(item)} in the chest at {_chest.transform.position:F1}");
                    return Close();
                }
                case Phase.Closing:
                    _ai.Stop();
                    if (Time.time < _until) return State.Working;
                    if (_chest != null) _chest.SetInUse(false);
                    _chest = null;
                    _goal = null;
                    Act(Carried.ActNone);
                    return State.Done;
                case Phase.ToSource:
                {
                    if (_source == null || !ContainerAccess.IsStorage(_source)) return Finish("the chest to tidy is gone");
                    if (Near(_source)) { StartTaking(); return State.Working; }
                    var r = _nav.Update(dt);
                    if (r == CoglingNavigator.Result.GaveUp) return Finish("can't reach the chest to tidy");
                    if (r == CoglingNavigator.Result.Arrived && !Near(_source)) _nav.SetTarget(ChestSpot(_source));
                    return State.Working;
                }
                case Phase.Taking:
                {
                    _ai.Stop();
                    _ai.Face(_source != null ? _source.transform.position : _ai.transform.position);
                    if (Time.time < _until) return State.Working;
                    if (_source == null) return Finish("the chest to tidy is gone");
                    // the stack (smallest of that item there, as planned), checked in the chest's latest contents: his first, then out
                    Inventory inv = _source.GetInventory();
                    ItemDrop.ItemData stack = inv.GetAllItems().Where(i => i.m_shared.m_name == _takeName && i.m_quality == _takeQuality)
                        .OrderBy(i => i.m_stack).FirstOrDefault();
                    if (stack == null) { _source.SetInUse(false); return Finish("it was taken meanwhile"); }
                    ItemDrop.ItemData carried = stack.Clone();
                    Carried.Write(Zdo, carried);
                    inv.RemoveItem(stack);
                    _ai.Log($"took {carried.m_stack} {Sorting.Short(carried)} from the chest at {_source.transform.position:F1}");
                    _phase = Phase.TakenClosing;
                    _until = Time.time + 0.4f;
                    return State.Working;
                }
                case Phase.TakenClosing:
                {
                    _ai.Stop();
                    if (Time.time < _until) return State.Working;
                    if (_source != null) _source.SetInUse(false);
                    Container from = _source;
                    _source = null;
                    Act(Carried.ActNone);
                    ItemDrop.ItemData item = Carried.Read(Zdo);
                    if (item == null) return Finish("nothing in his hands");
                    // to the planned chest, if it still has room; else wherever the rulebook says (never back where it came from)
                    if (_goal != null && ContainerAccess.IsStorage(_goal) && _goal.GetInventory().CanAddItem(item))
                    {
                        _chest = _goal;
                        _phase = Phase.ToChest;
                        _nav.SetTarget(ChestSpot(_chest));
                        _ai.Log($"to the chest at {_chest.transform.position:F1} (tidying)");
                        return State.Working;
                    }
                    return ToAChest(item, skip: from) ? State.Working : State.Done;
                }
            }
            return State.Done;
        }

        /// <summary>
        /// The same items lying right by it (a smelter's bars, a kiln's coal: one by one on the floor), scooped up with it, up to a full
        /// stack: those this game already owns only (the rest wait for another trip), each saved on him before it leaves the floor.
        /// </summary>
        private void Scoop(ItemDrop.ItemData carried, Vector3 at)
        {
            int added = 0;
            foreach (ItemDrop d in FloorItems.Near(at, 2f, carried))   // (a creature's drops scatter)
            {
                if (carried.m_stack >= Mathf.Min(carried.m_shared.m_maxStackSize, FloorItems.Load(new ItemDrop.ItemData { m_shared = carried.m_shared, m_stack = int.MaxValue / 2 }))) break;
                ZNetView view = d.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                int most = Mathf.Min(carried.m_shared.m_maxStackSize, Mathf.FloorToInt(Plugin.MaxCarryWeight.Value / Mathf.Max(0.0001f, carried.m_shared.m_weight)));
                int take = Mathf.Min(d.m_itemData.m_stack, most - carried.m_stack);
                if (take < d.m_itemData.m_stack) continue;   // (whole items only: never split one on the floor)
                carried.m_stack += take;
                Carried.Write(Zdo, carried);
                ZNetScene.instance.Destroy(d.gameObject);
                added++;
            }
            if (added > 0) _ai.Log($"scooped up {added} more lying with it");
        }

        private void StartPicking()
        {
            _phase = Phase.Picking;
            _until = Time.time + 2.5f;   // (bend for a second; up to 2.5 s for the item to be handed over)
            _ai.Stop();
            _ai.Face(_item.transform.position);
            if (!_item.GetComponent<ZNetView>().IsOwner()) _item.RequestOwn();
            Act(Carried.ActBend);
        }

        /// <summary>Choose a chest for what he carries and set off; none: put it down here.</summary>
        private bool ToAChest(ItemDrop.ItemData item, Container skip = null)
        {
            List<Container> chests = Chests.Usable(_network).Where(c => c != skip).ToList();
            _chest = Sorting.Choose(item, chests, _ai.transform.position, out string why);
            if (_chest == null) { PutDown(item); return false; }
            _phase = Phase.ToChest;
            _nav.SetTarget(ChestSpot(_chest));
            _ai.Log($"to the chest at {_chest.transform.position:F1} ({why})");
            return true;
        }

        private State Reroute(string why, Container skip = null)
        {
            _ai.Log(why);
            if (_chest != null && _chest.GetComponent<ZNetView>()?.IsOwner() == true) _chest.SetInUse(false);
            ItemDrop.ItemData item = Carried.Read(Zdo);
            if (item == null) return Finish(why);
            return ToAChest(item, skip) ? State.Working : State.Done;
        }

        private void StartTaking()
        {
            _ai.Stop();
            if (ContainerAccess.InUse(_source) || !ContainerAccess.MayUse(_source)) { Finish("the chest to tidy is in use"); _phase = Phase.Closing; _until = 0f; return; }
            ContainerAccess.TakeOver(_source);   // (his game owns it from here, with its latest contents)
            _source.SetInUse(true);
            _phase = Phase.Taking;
            _until = Time.time + 0.7f;
            Act(Carried.ActDeposit);
        }

        private void StartOpening()
        {
            _ai.Stop();
            if (ContainerAccess.InUse(_chest) || !ContainerAccess.MayUse(_chest)) { Reroute("the chest is in use", skip: _chest); return; }
            ContainerAccess.TakeOver(_chest);   // (his game owns it from here, with its latest contents)
            _chest.SetInUse(true);              // (the lid opens for everyone)
            _phase = Phase.Opening;
            _until = Time.time + 0.7f;
            Act(Carried.ActDeposit);
        }

        private State Close()
        {
            _phase = Phase.Closing;
            _until = Time.time + 0.5f;
            return State.Working;
        }

        /// <summary>He puts it down where he stands (no chest has room), and leaves it a while.</summary>
        private void PutDown(ItemDrop.ItemData item)
        {
            Vector3 at = _ai.transform.position + _ai.transform.forward * 0.5f + Vector3.up * 0.3f;
            ItemDrop dropped = ItemDrop.DropItem(item, item.m_stack, at, Quaternion.identity);
            Carried.Write(Zdo, null);
            Act(Carried.ActNone);
            if (dropped != null) FloorItems.NoHome(dropped);
            _ai.Log($"no chest has room: put {item.m_stack} {Sorting.Short(item)} down");
        }

        /// <summary>He's going (taken down, broken): what he carries falls where he stands, and a lid he opened closes.</summary>
        internal void Drop()
        {
            ItemDrop.ItemData item = Carried.Read(Zdo);
            if (item != null)
            {
                ItemDrop.DropItem(item, item.m_stack, _ai.transform.position + Vector3.up * 0.4f, Quaternion.identity);
                Carried.Write(Zdo, null);
            }
            Abandon();
        }

        /// <summary>The job ends here (no drop): a claimed item freed, an opened lid closed.</summary>
        internal void Abandon()
        {
            if (_source != null && _source.GetComponent<ZNetView>()?.IsOwner() == true) _source.SetInUse(false);
            _source = null;
            _goal = null;
            if (_itemId != ZDOID.None) FloorItems.Claimed.Remove(_itemId);
            if (_chest != null && _chest.GetComponent<ZNetView>()?.IsOwner() == true) _chest.SetInUse(false);
            _chest = null;
            _item = null;
        }

        private State Finish(string why)
        {
            _ai.Log(why);
            Abandon();
            Act(Carried.ActNone);
            return State.Done;
        }

        // (his reach: generous, so that wherever the navigator calls "arrived" by the chest spot, he can use the chest from there)
        private bool Near(Container c) => Utils.DistanceXZ(_ai.transform.position, c.transform.position) < 1.8f && Mathf.Abs(_ai.transform.position.y - c.transform.position.y) < 1.5f;

        /// <summary>Where he stands to use a chest: a metre from it, on his side.</summary>
        private Vector3 ChestSpot(Container c)
        {
            Vector3 away = _ai.transform.position - c.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = c.transform.forward;
            return c.transform.position + away.normalized * 0.9f;
        }

        private void Act(int act) => Zdo?.Set(Carried.ActKey, act);
    }
}
