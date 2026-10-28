using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Atlas, Dagny's Lox: walks in to the base and wanders round it, eats food left lying near him (each meal knocks 30% off one of her
    /// goods, up to three), turns side-on once to whoever trades so they're beside her saddle,
    /// rests a good while between short wanders, and when her time is up walks away and is
    /// removed. Runs on the owner of the object only (the game's AI rule); everything it goes by is in its ZDO, so whoever owns it next
    /// carries on. The game's BaseAI does the walking (paths, slopes, water, fire).
    /// </summary>
    internal sealed class TraderAI : BaseAI
    {
        internal const string HomeKey = "GoodTidings.Home", LeaveAtKey = "GoodTidings.LeaveAt", LeavingKey = "GoodTidings.Leaving",
            TradeUntilKey = "GoodTidings.TradeUntil", HoodedKey = "GoodTidings.Hooded", MealsKey = "GoodTidings.Meals",
            DiscountsKey = "GoodTidings.Discounts";
        internal const int MaxMeals = 3;
        private static readonly long LeaveGrace = TimeSpan.FromSeconds(150).Ticks;   // walking away, then gone even if he's stuck

        /// <summary>What he eats (set on the prefab): the Lox's own food and the earlier lands' berries and roots.</summary>
        public List<ItemDrop> m_consumeItems = new List<ItemDrop>();
        private ItemDrop _meal;
        private float _mealSearch;
        private int _itemMask;

        /// <summary>Where he wanders round (the base), saved so it survives a restart and a change of owner.</summary>
        internal void SetHome(Vector3 point)
        {
            m_spawnPoint = point;
            ZDO zdo = m_nview.GetZDO();
            zdo.Set(HomeKey, point);
            zdo.Set(ZDOVars.s_spawnPoint, point);
        }

        public override bool UpdateAI(float dt)
        {
            if (!base.UpdateAI(dt)) return false;
            ZDO zdo = m_nview.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            m_spawnPoint = zdo.GetVec3(ZDOVars.s_spawnPoint, m_spawnPoint);   // (another owner may have moved it)

            long leaveAt = zdo.GetLong(LeaveAtKey, 0L);
            if (leaveAt == 0L)
            {
                // not set up by a visit (made by hand): stay the usual time from now
                leaveAt = now + TimeSpan.FromMinutes(Plugin.TraderMinutes.Value).Ticks;
                zdo.Set(LeaveAtKey, leaveAt);
            }

            if (now >= leaveAt)
            {
                Vector3 home = zdo.GetVec3(HomeKey, m_spawnPoint);
                if (!zdo.GetBool(LeavingKey))
                {
                    // head off away from the base: his wandering centre moves far out that way
                    Vector3 away = transform.position - home;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1f) away = transform.forward;
                    m_spawnPoint = home + away.normalized * 90f;
                    zdo.Set(ZDOVars.s_spawnPoint, m_spawnPoint);
                    zdo.Set(LeavingKey, true);
                    Player.MessageAllInRange(transform.position, 60f, MessageHud.MessageType.Center, Lines.Pick("goodtidings_trader_leaving"), null);
                }
                if (Utils.DistanceXZ(transform.position, home) > 60f || now > leaveAt + LeaveGrace)
                {
                    ZNetScene.instance.Destroy(gameObject);
                    return true;
                }
                IdleMovement(dt);
                return true;
            }

            if (zdo.GetLong(TradeUntilKey, 0L) > now)
            {
                // turn side-on to the customer once when the trade starts, then stand: no shuffling round as they move
                StopMoving();
                if (!_trading)
                {
                    _trading = true;
                    Player customer = Player.GetClosestPlayer(transform.position, 15f);
                    _sideOn = customer ? SideOn(customer.transform.position) : (Vector3?)null;
                }
                if (_sideOn is Vector3 face)
                {
                    LookAt(transform.position + face * 5f);
                    if (IsLookingAt(transform.position + face * 5f, 8f)) _sideOn = null;
                }
                return true;
            }
            _trading = false;

            if (Eat(dt, zdo)) return true;

            // he wanders a little, then stands and rests a good while (not turning to anyone), and so on
            _phase -= dt;
            if (_phase <= 0f)
            {
                _resting = !_resting;
                _phase = _resting ? UnityEngine.Random.Range(20f, 45f) : UnityEngine.Random.Range(8f, 15f);
            }
            if (_resting && Utils.DistanceXZ(transform.position, m_spawnPoint) < m_randomMoveRange * 2f)
            {
                StopMoving();
                return true;
            }
            IdleMovement(dt);
            return true;
        }

        private bool _trading, _resting;
        private float _phase;
        private Vector3? _sideOn;

        /// <summary>
        /// The way to face for a customer to stand at his side (by the saddle) rather than at his head: of the two, the one nearer his
        /// facing now. Null if he's within 35 degrees of it already (then he stays as he is).
        /// </summary>
        private Vector3? SideOn(Vector3 customer)
        {
            Vector3 to = customer - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return null;
            Vector3 left = Vector3.Cross(to, Vector3.up).normalized, right = -left;
            Vector3 want = Vector3.Dot(left, transform.forward) >= Vector3.Dot(right, transform.forward) ? left : right;
            return Vector3.Angle(transform.forward, want) > 35f ? want : (Vector3?)null;
        }

        /// <summary>Goes to food lying near him and eats it (as a Lox does); each meal, up to three, knocks 30% off one of Dagny's goods.</summary>
        private bool Eat(float dt, ZDO zdo)
        {
            if (m_consumeItems.Count == 0 || zdo.GetInt(MealsKey) >= MaxMeals) return false;
            _mealSearch -= dt;
            if (_meal == null && _mealSearch <= 0f)
            {
                _mealSearch = 4f;
                _meal = FindMeal(10f);
            }
            if (_meal == null) return false;
            if (MoveTo(dt, _meal.transform.position, 3.5f, false))
            {
                LookAt(_meal.transform.position);
                if (IsLookingAt(_meal.transform.position, 25f) && _meal.RemoveOne())
                {
                    _meal = null;
                    ((Humanoid)m_character).m_consumeItemEffects.Create(transform.position, Quaternion.identity);
                    m_animator.SetTrigger("consume");
                    Fed(zdo);
                }
            }
            return true;
        }

        private ItemDrop FindMeal(float range)
        {
            if (_itemMask == 0) _itemMask = LayerMask.GetMask("item");
            ItemDrop best = null;
            float bestDistance = float.MaxValue;
            foreach (Collider c in Physics.OverlapSphere(transform.position, range, _itemMask))
            {
                ItemDrop item = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null;
                if (item == null || !item.GetComponent<ZNetView>().IsValid()) continue;
                if (!m_consumeItems.Exists(f => f && f.m_itemData.m_shared.m_name == item.m_itemData.m_shared.m_name)) continue;
                float d = Vector3.Distance(item.transform.position, transform.position);
                if (d < bestDistance) { best = item; bestDistance = d; }
            }
            return best;
        }

        private void Fed(ZDO zdo)
        {
            int meals = zdo.GetInt(MealsKey) + 1;
            zdo.Set(MealsKey, meals);
            string item = Stock.PickDiscount(zdo.GetString(DiscountsKey, ""));
            if (item != null) zdo.Set(DiscountsKey, Stock.AddDiscount(zdo.GetString(DiscountsKey, ""), item));
            string name = item != null ? ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name : null;
            string msg = name != null ? $"{Lines.Pick("goodtidings_atlas_fed")} {name}" : Lines.Pick("goodtidings_atlas_munch");
            if (meals >= MaxMeals) msg += "\n" + Lines.Pick("goodtidings_atlas_full");
            Player.MessageAllInRange(transform.position, 30f, MessageHud.MessageType.Center, msg, null);
        }
    }
}
