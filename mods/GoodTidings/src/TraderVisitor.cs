using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace GoodTidings
{
    /// <summary>
    /// On the trader's object: dresses it on each game (golden Lox, the rider, the goods; nothing of it is saved or sent), and runs the
    /// shop: E opens the game's own store window on a Trader kept switched off (no Haldor chatter or head turning), and while it's open
    /// this game tells her owner, who keeps her standing.
    /// </summary>
    internal sealed class TraderVisitor : MonoBehaviour, Interactable
    {
        private static readonly HashSet<Character> All = new HashSet<Character>();
        internal static int Count => All.Count;
        internal static bool Is(Character c) => c != null && All.Contains(c);

        private ZNetView _nview;
        private Character _character;
        private readonly List<GameObject> _made = new List<GameObject>();
        private GameObject _rider;
        private Trader _shop;
        private float _tradingSent;
        private TraderAnim _anim;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
            All.Add(_character);
            if (_nview.GetZDO() == null) return;
            _nview.Register("GoodTidings_Trading", RPC_Trading);
        }

        private void Start()
        {
            if (_nview.GetZDO() == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;   // (a dedicated server draws nothing)
            try
            {
                Transform visual = transform.Find("Visual");
                if (visual) TraderLook.Tint(visual.gameObject, TraderLook.Golden);
                bool hooded = _nview.GetZDO().GetBool(TraderAI.HoodedKey);
                _rider = TraderLook.Mount(transform, hooded, _made, under: transform);
                if (_rider) { MakeShop(_rider.transform); _anim = _rider.GetComponent<TraderAnim>(); }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Dressing the wandering trader failed: {e}");
            }
        }

        private void MakeShop(Transform at)
        {
            var go = new GameObject("goodtidings_shop");
            go.transform.SetParent(at, false);
            _shop = go.AddComponent<Trader>();
            _shop.enabled = false;   // (switched off before its Start: no greeting, head turning or random talk)
            _shop.m_name = "$goodtidings_dagny";
            _shop.m_items = Stock.Items();
        }

        private void OnDestroy()
        {
            All.Remove(_character);
            _pins.Remove();
        }

        private readonly MapPins _pins = new MapPins();

        private void Update()
        {
            if (_rider && _nview.GetZDO() != null)   // (only where she's dressed: a dedicated server has no map)
                _pins.Update(_nview.GetZDO().GetVec3(TraderAI.HomeKey, transform.position), transform.position,
                    Localization.instance.Localize("$goodtidings_dagny"));
            Greeting();
            // while this player has her shop open, tell her owner every couple of seconds (she stands still for a few seconds after each)
            if (_shop == null || !StoreGuiShowing(_shop)) return;
            if (Time.time - _tradingSent < 2f) return;
            _tradingSent = Time.time;
            _nview.InvokeRPC("GoodTidings_Trading");
        }

        private void RPC_Trading(long sender)
        {
            if (!_nview.IsOwner()) return;
            _nview.GetZDO().Set(TraderAI.TradeUntilKey, ZNet.instance.GetTime().Ticks + TimeSpan.FromSeconds(5).Ticks);
        }

        /// <summary>Atlas's pet effect (the Lox's own, from its Tameable: set on the prefab).</summary>
        public EffectList m_petEffect = new EffectList();
        private float _petted;

        /// <summary>Whether the player is looking at Atlas's head (pet him) rather than at Dagny or the load (trade).</summary>
        internal static bool AtHead(Player p)
        {
            // the view hits his head (a copy of its shape the game's looking test sees: TraderPrefab), within reach
            GameObject hover = p ? p.GetHoverObject() : null;
            TraderVisitor v = hover ? hover.GetComponentInParent<TraderVisitor>() : null;
            if (v == null || GameCamera.instance == null) return false;
            if (v._headShape == null) v._headShape = Utils.FindChild(v.transform, TraderPrefab.HeadReach)?.GetComponent<Collider>();
            Transform cam = GameCamera.instance.transform;
            return v._headShape && v._headShape.Raycast(new Ray(cam.position, cam.forward), out _, 60f);
        }

        private Collider _headShape;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            if (AtHead(Player.m_localPlayer))
            {
                if (Time.time - _petted < 1f) return true;
                _petted = Time.time;
                m_petEffect.Create(transform.position, transform.rotation);
                user.Message(MessageHud.MessageType.Center, "$goodtidings_atlas_pet");
                return true;
            }
            if (_shop == null) return false;
            if (_nview.GetZDO()?.GetBool(TraderAI.LeavingKey) == true) { user.Message(MessageHud.MessageType.Center, "$goodtidings_trader_closed"); return true; }
            _shop.m_items = Stock.Items(_nview.GetZDO()?.GetString(TraderAI.DiscountsKey, "") ?? "");   // (the world's progress, and Atlas's meals)
            StoreGui.instance.Show(_shop);
            _tradingSent = 0f;
            Say(Lines.Pick("goodtidings_trader_hello"), "Talk", HildirFx(t => t.m_randomStartTradeFX));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>A line over her head, with one of Hildir's gestures and her voice (the sounds of her own shop).</summary>
        internal void Say(string key, string gesture = null, EffectList voice = null)
        {
            if (_rider && Chat.instance)
                Chat.instance.SetNpcText(_rider, Vector3.up * 1.6f, 20f, 5f, "", Localization.instance.Localize(key), false);
            if (gesture != null && _anim) _anim.Gesture(gesture);
            voice?.Create(_rider ? _rider.transform.position + Vector3.up * 1.2f : transform.position, Quaternion.identity);
        }

        internal void Bought() => Say(Lines.Pick("goodtidings_trader_bought"), "Buy", HildirFx(t => t.m_randomBuyFX));
        internal void Sold() => Say(Lines.Pick("goodtidings_trader_sold"), "Sell", HildirFx(t => t.m_randomSellFX));

        /// <summary>Hildir's own voice for a moment of trading (her Trader's effect lists), if the game still has her.</summary>
        private static EffectList HildirFx(System.Func<Trader, EffectList> pick)
        {
            Trader hildir = ZNetScene.instance ? ZNetScene.instance.GetPrefab("Hildir")?.GetComponent<Trader>() : null;
            return hildir ? pick(hildir) : null;
        }

        // greets this player when they come close, says goodbye when they walk off (as the game's traders do), on this game only
        private const float GreetRange = 6f, ByeRange = 9f;
        private bool _greeted;
        private float _nextTalk;

        private void Greeting()
        {
            Player me = Player.m_localPlayer;
            if (me == null || _rider == null || _nview.GetZDO() == null || _nview.GetZDO().GetBool(TraderAI.LeavingKey))
            {
                if (_anim) _anim.LookTarget = null;
                return;
            }
            float d = Vector3.Distance(me.transform.position, transform.position);
            if (_anim) _anim.LookTarget = d < 10f ? me.GetHeadPoint() : (Vector3?)null;   // (she looks at this player while they're near)
            if (!_greeted && d < GreetRange)
            {
                _greeted = true;
                Say(Lines.Pick("goodtidings_trader_greet"), "Greet", HildirFx(t => t.m_randomGreetFX));
            }
            else if (_greeted && d < GreetRange + 2f && Time.time > _nextTalk && !StoreGui.IsVisible())
            {
                // a word now and then while this player stays near her (as the game's traders chat)
                if (_nextTalk > 0f) Say(Lines.Pick("goodtidings_trader_talk"), "Talk", HildirFx(t => t.m_randomTalkFX));
                _nextTalk = Time.time + UnityEngine.Random.Range(25f, 40f);
            }
            else if (_greeted && d > ByeRange)
            {
                _greeted = false;
                _nextTalk = 0f;
                Say(Lines.Pick("goodtidings_trader_goodbye"), null, HildirFx(t => t.m_randomGoodbyeFX));
            }
        }

        internal static TraderVisitor Of(Trader shop) => shop ? shop.GetComponentInParent<TraderVisitor>() : null;

        private static readonly AccessTools.FieldRef<StoreGui, Trader> StoreTrader = AccessTools.FieldRefAccess<StoreGui, Trader>("m_trader");

        private static bool StoreGuiShowing(Trader shop) => StoreGui.IsVisible() && StoreGui.instance && StoreTrader(StoreGui.instance) == shop;
    }

    /// <summary>
    /// Her shop is a Trader kept switched off, so the game's own lines would fail (it never set up its animator, and has no lines):
    /// she says her own.
    /// </summary>
    [HarmonyPatch(typeof(Trader), nameof(Trader.OnBought))]
    internal static class Trader_OnBought
    {
        private static bool Prefix(Trader __instance)
        {
            TraderVisitor v = TraderVisitor.Of(__instance);
            if (v == null) return true;
            v.Bought();
            return false;
        }
    }

    [HarmonyPatch(typeof(Trader), nameof(Trader.OnSold))]
    internal static class Trader_OnSold
    {
        private static bool Prefix(Trader __instance)
        {
            TraderVisitor v = TraderVisitor.Of(__instance);
            if (v == null) return true;
            v.Sold();
            return false;
        }
    }

    /// <summary>
    /// The store window closes when the player is more than 5 m from the trader: from beside a Lox that's about where its rider sits.
    /// While her shop is open it allows 9 m, and the game's own figure again for other traders.
    /// </summary>
    [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
    internal static class StoreGui_Show
    {
        private static float _gameDistance = -1f;

        private static void Prefix(StoreGui __instance, Trader trader)
        {
            if (_gameDistance < 0f) _gameDistance = __instance.m_hideDistance;
            __instance.m_hideDistance = TraderVisitor.Of(trader) ? 9f : _gameDistance;
        }
    }
}
