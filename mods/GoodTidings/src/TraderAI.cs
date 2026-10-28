using System;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The trader's Lox: walks in to the base and wanders round it, stands still while someone trades, and when her time is up walks
    /// away and is removed. Runs on the owner of her object only (the game's AI rule); everything it goes by is in her ZDO, so whoever
    /// owns her next carries on. The game's BaseAI does the walking (paths, slopes, water, fire).
    /// </summary>
    internal sealed class TraderAI : BaseAI
    {
        internal const string HomeKey = "GoodTidings.Home", LeaveAtKey = "GoodTidings.LeaveAt", LeavingKey = "GoodTidings.Leaving",
            TradeUntilKey = "GoodTidings.TradeUntil", HoodedKey = "GoodTidings.Hooded";
        private static readonly long LeaveGrace = TimeSpan.FromSeconds(150).Ticks;   // walking away, then gone even if she's stuck

        /// <summary>Where she wanders round (the base), saved so it survives a restart and a change of owner.</summary>
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
                    // head off away from the base: her wandering centre moves far out that way
                    Vector3 away = transform.position - home;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1f) away = transform.forward;
                    m_spawnPoint = home + away.normalized * 90f;
                    zdo.Set(ZDOVars.s_spawnPoint, m_spawnPoint);
                    zdo.Set(LeavingKey, true);
                    MessageHud.instance.MessageAll(MessageHud.MessageType.Center, "$goodtidings_trader_leaving");
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
                // someone has her shop open: stand and face them
                StopMoving();
                Player customer = Player.GetClosestPlayer(transform.position, 15f);
                if (customer) LookAt(customer.transform.position);
                return true;
            }

            IdleMovement(dt);
            return true;
        }
    }
}
