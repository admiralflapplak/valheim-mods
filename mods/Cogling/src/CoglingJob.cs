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
        internal const int ActNone = 0, ActBend = 1, ActDeposit = 2, ActHammer = 3;

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
        private enum Phase { ToItem, Picking, ToChest, Opening, Closing, ToSource, Taking, TakenClosing, ToPiece, Hammering, ToStation, FixingGear, ToFeed, Feeding }

        private readonly CoglingAI _ai;
        private readonly CoglingNavigator _nav;
        private Phase _phase;
        private ItemDrop _item;
        private ZDOID _itemId;
        private Container _chest;
        private Container _source;     // tidying: the chest he takes a stack from
        private Container _goal;       // tidying: the chest it's for (else Sorting chooses)
        private string _takeName, _tidyKey;
        private WearNTear _piece;            // repairing: the damaged building piece
        private CraftingStation _station;    // repairing gear: the station it's mended at
        private bool _gear;                  // (this trip from a chest is a repair, back to the same chest)
        private int _takeQuality;
        private Tending.Feed _feed;          // tending: the fire or smelter he feeds, and how many he took for it (0: a whole stack)
        private int _takeCount;
        private float _pendingSince = -1f, _before, _nextFeed;
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
                case Phase.ToPiece:
                case Phase.Hammering:
                    return ("cogling_task_repairing", "", _piece != null ? _piece.GetComponent<Piece>()?.m_name ?? "" : "");
                case Phase.ToStation:
                case Phase.FixingGear:
                    return ("cogling_task_mending", carried != null ? carried.m_shared.m_name : "", _station != null ? _station.m_name : "");
                case Phase.ToFeed:
                case Phase.Feeding:
                    return ("cogling_task_tending", "", _feed?.Name ?? "");
                case Phase.ToSource:
                case Phase.Taking:
                    if (_feed != null) return ("cogling_task_tending", "", _feed.Name);
                    if (_gear) return ("cogling_task_mending", _takeName ?? "", _station != null ? _station.m_name : "");
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
            _tidyKey = Tidy.Key(move.From, move.Name);
            Tidy.Claimed.Add(_tidyKey);
            _takeName = move.Name;
            _takeQuality = move.Quality;
            if (!_nav.SetTarget(ChestSpot(_source))) { _source = _goal = null; return false; }
            _phase = Phase.ToSource;
            _ai.Log($"tidying: {Sorting.Short(new ItemDrop.ItemData { m_shared = new ItemDrop.ItemData.SharedData { m_name = move.Name } })} from the chest at {_source.transform.position:F1} ({move.Why})");
            return true;
        }

        /// <summary>A damaged building piece in the area, and off he goes to mend it. False: nothing needs it.</summary>
        internal bool TryStartRepair(List<Antenna> network)
        {
            _network = network;
            WearNTear piece = Repairs.NextPiece(network, _ai.transform.position);
            if (piece == null) return false;
            if (!_nav.SetTarget(RepairSpot(piece))) { Repairs.SkipFor(piece, 300f); return false; }
            _piece = piece;
            Repairs.Claimed.Add(Repairs.Id(piece));
            _phase = Phase.ToPiece;
            _ai.Log($"repairing {Localization.instance.Localize(piece.GetComponent<Piece>().m_name)} at {piece.transform.position:F1} ({piece.GetHealthPercentage() * 100f:F0}%)");
            return true;
        }

        /// <summary>A worn item in a chest whose station stands in the area: off to take it there. False: none.</summary>
        internal bool TryStartGear(List<Antenna> network)
        {
            _network = network;
            List<Container> chests = Chests.Usable(network);
            var (chest, item, station) = Repairs.NextGear(network, chests);
            if (chest == null || !_nav.SetTarget(ChestSpot(chest))) return false;
            _source = chest;
            _goal = chest;   // (back where it came from)
            _station = station;
            _gear = true;
            _takeName = item.m_shared.m_name;
            _takeQuality = item.m_quality;
            _phase = Phase.ToSource;
            _ai.Log($"mending {Localization.instance.Localize(item.m_shared.m_name)} ({item.m_durability / item.GetMaxDurability() * 100f:F0}%) at the {Localization.instance.Localize(station.m_name)}");
            return true;
        }

        /// <summary>A fire burning low or a working smelter in the area, and its fuel or ore in a chest: off to fetch it. False: none.</summary>
        internal bool TryStartFeed(List<Antenna> network)
        {
            if (!Plugin.TendFires.Value && !Plugin.TendSmelters.Value) return false;
            _network = network;
            Tending.Feed feed = Tending.Next(network, _ai.transform.position, Chests.Usable(network));
            if (feed == null || !_nav.SetTarget(ChestSpot(feed.Chest))) return false;
            // as many as it can take, from that one stack, as much as he can carry
            int most = Mathf.Max(1, Mathf.FloorToInt(Plugin.MaxCarryWeight.Value / Mathf.Max(0.0001f, feed.Stack.m_shared.m_weight)));
            _takeCount = Mathf.Max(1, Mathf.Min(feed.Wants, feed.Stack.m_stack, most));
            _feed = feed;
            Tending.Claimed.Add(feed.Station.GetInstanceID());
            _source = feed.Chest;
            _goal = null;
            _takeName = feed.Stack.m_shared.m_name;
            _takeQuality = feed.Stack.m_quality;
            _phase = Phase.ToSource;
            _ai.Log($"tending: {_takeCount} {Localization.instance.Localize(_takeName)} for the {Localization.instance.Localize(feed.Name)} at {feed.Station.transform.position:F1}");
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
            if (state == State.Done) { _ai.Log($"job over ({_phase})"); Release(); }
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
                        _ai.Say(CoglingVoice.Line.Grumble);
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
                    _ai.Say(CoglingVoice.Line.Pickup);
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
                    _ai.Say(_tidyKey != null ? CoglingVoice.Line.Tidied : CoglingVoice.Line.Stored);
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
                case Phase.ToPiece:
                {
                    if (_piece == null || !_piece.GetComponent<ZNetView>().IsValid()) return Finish("the piece is gone");
                    if (_piece.GetHealthPercentage() >= 0.999f) return Finish("someone repaired it already");
                    // (within arm's reach of the piece: he walks right up to it, a small robot's reach)
                    Vector3 at = Repairs.NearestPoint(_piece, _ai.transform.position);
                    float reach = Vector3.Distance(_ai.transform.position + Vector3.up * 0.5f, at);
                    if (reach < 1.3f) { StartHammering(at); return State.Working; }
                    var r = _nav.Update(dt);
                    if (r != CoglingNavigator.Result.Moving && reach < 1.9f) { StartHammering(at); return State.Working; }   // (as close as he gets: a chest in the way)
                    if (r == CoglingNavigator.Result.GaveUp || r == CoglingNavigator.Result.Arrived)
                    {
                        Repairs.SkipFor(_piece, 300f);   // (out of his reach: up on a roof, behind a wall)
                        _ai.Say(CoglingVoice.Line.Grumble);
                        return Finish("can't reach the piece");
                    }
                    return State.Working;
                }
                case Phase.Hammering:
                {
                    _ai.Stop();
                    _ai.Face(_hammerAt);
                    if (Time.time < _until) return State.Working;
                    Act(Carried.ActNone);
                    if (_piece != null && _piece.Repair())
                    {
                        Piece p = _piece.GetComponent<Piece>();
                        p?.m_placeEffect.Create(_piece.transform.position, _piece.transform.rotation);
                        _ai.Log($"repaired {Localization.instance.Localize(p?.m_name ?? "")}");
                        _ai.Say(CoglingVoice.Line.Repaired);
                    }
                    Release();   // (before forgetting the piece: its claim goes with it)
                    _piece = null;
                    return State.Done;
                }
                case Phase.ToStation:
                {
                    if (_station == null) { _gear = false; return Reroute("the station is gone"); }
                    if (Utils.DistanceXZ(_ai.transform.position, _station.transform.position) < 1.5f) { StartHammering(_station.transform.position); _phase = Phase.FixingGear; return State.Working; }
                    var r = _nav.Update(dt);
                    if (r == CoglingNavigator.Result.GaveUp && Utils.DistanceXZ(_ai.transform.position, _station.transform.position) < 2f) { StartHammering(_station.transform.position); _phase = Phase.FixingGear; return State.Working; }
                    if (r == CoglingNavigator.Result.GaveUp) { _gear = false; return Reroute("can't reach the station"); }
                    if (r == CoglingNavigator.Result.Arrived && Utils.DistanceXZ(_ai.transform.position, _station.transform.position) < 2f) { StartHammering(_station.transform.position); _phase = Phase.FixingGear; return State.Working; }
                    if (r == CoglingNavigator.Result.Arrived) _nav.SetTarget(StationSpot(_station));
                    return State.Working;
                }
                case Phase.FixingGear:
                {
                    _ai.Stop();
                    _ai.Face(_hammerAt);
                    if (Time.time < _until) return State.Working;
                    Act(Carried.ActNone);
                    ItemDrop.ItemData item = Carried.Read(Zdo);
                    if (item != null)
                    {
                        item.m_durability = item.GetMaxDurability();
                        Carried.Write(Zdo, item);   // (mended in his hands: saved so)
                        _station?.m_repairItemDoneEffects.Create(_station.transform.position, Quaternion.identity);
                        _ai.Log($"mended {Localization.instance.Localize(item.m_shared.m_name)}");
                        _ai.Say(CoglingVoice.Line.Repaired);
                    }
                    _gear = false;
                    // back to the chest it came from (if it still has room), else wherever the rulebook says
                    if (item != null && _goal != null && ContainerAccess.IsStorage(_goal) && _goal.GetInventory().CanAddItem(item))
                    {
                        _chest = _goal;
                        _phase = Phase.ToChest;
                        _nav.SetTarget(ChestSpot(_chest));
                        return State.Working;
                    }
                    return item != null && ToAChest(item) ? State.Working : State.Done;
                }
                case Phase.ToFeed:
                {
                    if (_feed?.Station == null || !_feed.View.IsValid()) return Leftover("the station is gone");
                    float d = Utils.DistanceXZ(_ai.transform.position, Edge(_feed.Station, _ai.transform.position));
                    var r = _nav.Update(dt);
                    if (d < 0.9f || (r != CoglingNavigator.Result.Moving && d < 1.6f))
                    {
                        _ai.Stop();
                        _phase = Phase.Feeding;
                        _pendingSince = -1f;
                        _nextFeed = Time.time + 0.5f;
                        Act(Carried.ActDeposit);
                        return State.Working;
                    }
                    if (r == CoglingNavigator.Result.GaveUp) return Leftover("can't reach the station");
                    if (r == CoglingNavigator.Result.Arrived) _nav.SetTarget(FeedSpot());
                    return State.Working;
                }
                case Phase.Feeding:
                {
                    // one at a time, each counted as used only once the station's own count goes up (its owner may refuse or be gone)
                    _ai.Stop();
                    if (_feed?.Station == null || !_feed.View.IsValid()) return Leftover("the station is gone");
                    _ai.Face(_feed.Station.transform.position);
                    ItemDrop.ItemData item = Carried.Read(Zdo);
                    if (item == null) return Fed();
                    if (_pendingSince >= 0f)
                    {
                        if (Tending.Level(_feed) >= _before + 0.5f)
                        {
                            item.m_stack--;
                            Carried.Write(Zdo, item.m_stack > 0 ? item : null);
                            _pendingSince = -1f;
                            _nextFeed = Time.time + 0.45f;
                            return State.Working;
                        }
                        if (Time.time - _pendingSince > 2.5f) return Leftover("the station didn't take it");
                        return State.Working;
                    }
                    if (Time.time < _nextFeed) return State.Working;
                    if (!Tending.Room(_feed)) return Leftover("the station is full");
                    if (!Tending.StillWanted(_feed, _network)) return Leftover("the chests have its coal again: the kiln can wait");
                    _before = Tending.Level(_feed);
                    Tending.Add(_feed);
                    _pendingSince = Time.time;
                    return State.Working;
                }
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
                    var matching = inv.GetAllItems().Where(i => i.m_shared.m_name == _takeName && i.m_quality == _takeQuality);
                    ItemDrop.ItemData stack = _gear ? matching.OrderBy(i => i.m_durability / Mathf.Max(0.01f, i.GetMaxDurability())).FirstOrDefault()
                                                    : matching.OrderBy(i => i.m_stack).FirstOrDefault();
                    if (_feed != null) stack = matching.OrderByDescending(i => i.m_stack).FirstOrDefault();
                    if (stack == null) { _source.SetInUse(false); return Finish("it was taken meanwhile"); }
                    ItemDrop.ItemData carried = stack.Clone();
                    if (_feed != null && _takeCount > 0 && _takeCount < stack.m_stack)
                    {
                        // (tending: only what the station takes; the rest stays)
                        carried.m_stack = _takeCount;
                        Carried.Write(Zdo, carried);
                        inv.RemoveItem(stack, _takeCount);
                    }
                    else
                    {
                        Carried.Write(Zdo, carried);
                        inv.RemoveItem(stack);
                    }
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
                    if (_feed != null)
                    {
                        _phase = Phase.ToFeed;
                        _nav.SetTarget(FeedSpot());
                        return State.Working;
                    }
                    if (_gear && _station != null)
                    {
                        _phase = Phase.ToStation;
                        _nav.SetTarget(StationSpot(_station));
                        return State.Working;
                    }
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

        /// <summary>The piece or tidying move he had claimed, free again.</summary>
        private void Release()
        {
            if (_feed?.Station != null) Tending.Claimed.Remove(_feed.Station.GetInstanceID());
            if (_piece != null) Repairs.Claimed.Remove(Repairs.Id(_piece));
            if (_tidyKey != null) { Tidy.Claimed.Remove(_tidyKey); _tidyKey = null; }
        }

        /// <summary>Fed: all of it went in.</summary>
        private State Fed()
        {
            _ai.Log($"fed the {Localization.instance.Localize(_feed?.Name ?? "")}");
            _ai.Say(CoglingVoice.Line.Stored);
            Act(Carried.ActNone);
            Release();
            _feed = null;
            return State.Done;
        }

        /// <summary>The station took no more (full, gone, no answer): what's left goes back to a chest.</summary>
        private State Leftover(string why)
        {
            _ai.Log(why);
            Act(Carried.ActNone);
            Release();
            _feed = null;
            ItemDrop.ItemData item = Carried.Read(Zdo);
            return item != null && ToAChest(item) ? State.Working : State.Done;
        }

        /// <summary>Where he stands to feed a station: just outside its nearest edge, on his side (a smelter is big: its middle was out of reach).</summary>
        private Vector3 FeedSpot()
        {
            Vector3 edge = Edge(_feed.Station, _ai.transform.position), toward = _ai.transform.position - edge;
            toward.y = 0f;
            return edge + (toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward) * 0.5f;
        }

        /// <summary>The point of a station's solid body nearest a spot (its colliders'), at the spot's height; its middle if it has none.</summary>
        private static Vector3 Edge(MonoBehaviour station, Vector3 from)
        {
            Vector3 best = station.transform.position;
            float bestDistance = float.MaxValue;
            foreach (Collider c in station.GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Vector3 p = c is MeshCollider m && !m.convex ? c.bounds.ClosestPoint(from) : c.ClosestPoint(from);
                float d = Utils.DistanceXZ(p, from);
                if (d < bestDistance) { best = p; bestDistance = d; }
            }
            best.y = from.y;
            return best;
        }

        private Vector3 _hammerAt;

        /// <summary>A moment of hammering (his swing on every player's game, the hammer's sound with it).</summary>
        private void StartHammering(Vector3 at)
        {
            _ai.Stop();
            _hammerAt = at;
            _phase = Phase.Hammering;
            _until = Time.time + 1.8f;
            Act(Carried.ActHammer);
        }

        /// <summary>Where he stands to mend a piece: on the ground just in front of its nearest point, on his side.</summary>
        private Vector3 RepairSpot(WearNTear piece)
        {
            Vector3 at = Repairs.NearestPoint(piece, _ai.transform.position);
            Vector3 toward = _ai.transform.position - at;
            toward.y = 0f;
            Vector3 spot = at + (toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.zero) * 0.6f;
            // (on the floor or ground under it: not on top of a chest, a station or other furniture standing there)
            RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 1f, Vector3.down, 4f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain"));
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<WearNTear>() == piece || Furniture(hit.collider)) continue;
                return hit.point;
            }
            return spot;
        }

        private static bool Furniture(Collider c) =>
            c.GetComponentInParent<Container>() != null || c.GetComponentInParent<CraftingStation>() != null || c.GetComponentInParent<StationExtension>() != null
            || c.GetComponentInParent<Bed>() != null || c.GetComponentInParent<Fireplace>() != null || c.GetComponentInParent<Smelter>() != null
            || c.GetComponentInParent<ItemStand>() != null || c.GetComponentInParent<Chair>() != null;

        private Vector3 StationSpot(CraftingStation s)
        {
            Vector3 away = _ai.transform.position - s.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = s.transform.forward;
            return s.transform.position + away.normalized * 0.9f;
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
            _ai.Say(CoglingVoice.Line.Grumble);
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
            Release();
            Act(Carried.ActNone);   // (a break or the bell mid-chore: his arms down, the hammer away)
            if (_source != null && _source.GetComponent<ZNetView>()?.IsOwner() == true) _source.SetInUse(false);
            _source = null;
            _goal = null;
            _piece = null;
            _station = null;
            _gear = false;
            _feed = null;
            _takeCount = 0;
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
