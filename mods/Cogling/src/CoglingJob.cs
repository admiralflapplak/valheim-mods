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
        private enum Phase { ToItem, Picking, ToChest, Opening, Closing }

        private readonly CoglingAI _ai;
        private readonly CoglingNavigator _nav;
        private Phase _phase;
        private ItemDrop _item;
        private ZDOID _itemId;
        private Container _chest;
        private float _until;
        private List<Antenna> _network;

        internal CoglingJob(CoglingAI ai, CoglingNavigator nav)
        {
            _ai = ai;
            _nav = nav;
        }

        private ZDO Zdo => _ai.GetComponent<ZNetView>()?.GetZDO();
        internal bool Carrying => Carried.Read(Zdo) != null;

        /// <summary>An item in the area worth taking (one that has a chest to go to), and off he goes. False: nothing to do.</summary>
        internal bool TryStart(List<Antenna> network)
        {
            _network = network;
            List<Container> chests = Chests.Usable(network);
            if (chests.Count == 0) return false;
            foreach (ItemDrop d in FloorItems.Candidates(network, _ai.transform.position).Take(6))
            {
                Container chest = Sorting.Choose(d.m_itemData, chests, d.transform.position, out _);
                if (chest == null) { FloorItems.NoHome(d); continue; }
                if (!_nav.SetTarget(d.transform.position)) continue;
                _item = d;
                _itemId = FloorItems.Id(d);
                FloorItems.Claimed.Add(_itemId);
                _phase = Phase.ToItem;
                _ai.Log($"going for {d.m_itemData.m_stack} {Sorting.Short(d.m_itemData)} at {d.transform.position:F1}");
                return true;
            }
            return false;
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
                    if (Time.time < _until - 1.5f) return State.Working;   // (bending down)
                    // his now: saved on him first, then gone from the floor (a crash between can't lose it)
                    ItemDrop.ItemData item = _item.m_itemData.Clone();
                    Carried.Write(Zdo, item);
                    ZNetScene.instance.Destroy(_item.gameObject);
                    Scoop(item, _item.transform.position);
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
                    inv.AddItem(item);       // (the chest is his now: the game saves it on change)
                    Carried.Write(Zdo, null);
                    _ai.Log($"put {item.m_stack} {Sorting.Short(item)} in the chest at {_chest.transform.position:F1}");
                    return Close();
                }
                case Phase.Closing:
                    _ai.Stop();
                    if (Time.time < _until) return State.Working;
                    if (_chest != null) _chest.SetInUse(false);
                    _chest = null;
                    Act(Carried.ActNone);
                    return State.Done;
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
            foreach (ItemDrop d in FloorItems.Near(at, 1.2f, carried))
            {
                if (carried.m_stack >= carried.m_shared.m_maxStackSize) break;
                ZNetView view = d.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                int take = Mathf.Min(d.m_itemData.m_stack, carried.m_shared.m_maxStackSize - carried.m_stack);
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
