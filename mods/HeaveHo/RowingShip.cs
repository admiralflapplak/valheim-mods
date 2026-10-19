using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Nick.Shared;   // (Shared/Translations.cs)

namespace HeaveHo
{
    /// <summary>
    /// Added to every Ship. Handles:
    ///  - local player: seated in a rowing seat, W/S pick a gear, drain stamina, ping the ship owner
    ///  - ship owner:   collect pings into seat/gear bitmasks in the ZDO, push the ship from each oarlock
    ///  - everyone:     read the bitmasks and animate oars
    /// </summary>
    public class RowingShip : MonoBehaviour
    {
        /// <summary>The ship the local player is currently seated in as a rower (null if none).</summary>
        internal static RowingShip LocalRowingShip;

        // Gears. None is the default when you sit down.
        private const int GearBack = -1;
        private const int GearNone = 0;
        private const int GearEasy = 1;
        private const int GearHard = 2;

        private const string RpcRow = "HeaveHo_Row";
        private const float PingInterval = 1f;   // rowers re-ping this often
        private const float PingTimeout = 2.5f;  // owner drops a seat after this long without a ping
        private const int MaxSeats = 32;         // bitmask limit
        private const float OarInboard = 0.6f;   // handle length inside the hull

        private static readonly int ZdoRowMask = "HeaveHo_RowerMask".GetStableHashCode();  // any gear
        private static readonly int ZdoHardMask = "HeaveHo_HardMask".GetStableHashCode(); // Pull hard
        private static readonly int ZdoBackMask = "HeaveHo_BackMask".GetStableHashCode(); // Back water

        // Private Valheim field. If a game update renames it, this throws when the first ship loads.
        private static readonly AccessTools.FieldRef<Player, Transform> PlayerAttachPoint =
            AccessTools.FieldRefAccess<Player, Transform>("m_attachPoint");

        private static readonly List<RowingShip> All = new List<RowingShip>();

        /// <summary>No graphics (a dedicated server): rowing is still decided here, but nothing is drawn.</summary>
        private static bool Headless => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        private static readonly HashSet<string> loggedShips = new HashSet<string>();
        private static readonly HashSet<string> loggedHulls = new HashSet<string>();

        // The game builds the ghost shown while a ship is placed with the hammer (and tools that draw a ship on its own) with this switched
        // on: no network object, nobody aboard. Such a copy shows its oars out, as a parked ship does.
        private static readonly System.Reflection.FieldInfo ForceDisableInit = AccessTools.Field(typeof(ZNetView), "m_forceDisableInit");
        private bool preview;

        private ZNetView nview;
        private Rigidbody body;
        private Ship shipComponent;
        private bool sailing;                 // sail up and the ship moving: unused oars are shipped
        private Vector3 lastPosition;
        private float speedSmoothed;
        private Chair[] seats = Array.Empty<Chair>();
        private Vector3[] seatLocal = Array.Empty<Vector3>(); // seat position in ship space
        private float[] seatSide = Array.Empty<float>();      // +1 starboard, -1 port

        // Owner only
        private struct SeatPing { public float Time; public int Gear; }
        private readonly Dictionary<int, SeatPing> seatPings = new Dictionary<int, SeatPing>();
        private readonly List<int> staleSeats = new List<int>();

        // Local player
        private int localSeat = -1;
        private int localGear = GearNone;
        private bool localRowing;
        private int lastSentCode = int.MinValue;
        private bool exhausted;
        private float pingTimer;
        private bool upHeld, downHeld;

        // Oars
        private Oar[] oars = Array.Empty<Oar>();
        private readonly float[] gearPhase = new float[3]; // back, easy, hard

        private class Oar
        {
            public Transform Pivot;
            public Transform Shaft;
            public Transform Blade;
            public Transform Model; // custom bundle oar (Shaft/Blade are null then)
            public int Seat;
            public float Blend; // 0 = stowed, 1 = rowing
            public Transform Tip;            // blade tip: where it meets the water
            public bool InWater;             // the tip is below the water surface (for the splash on entry and exit)
            public float Out;                // 0 = slid in (not shown), 1 = out; moves smoothly toward what the ship wants
            public bool OutKnown;            // Out has been set once (a new oar starts where it should be, not sliding)
            public bool WaterKnown;          // Water / WaterSmooth hold this frame's reading (only while the oar pulls)
            public float Water;              // water surface height under the blade tip (world)
            public float WaterSmooth;        // the same, eased, for the depth of the stroke
        }

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            preview = ForceDisableInit != null && (bool)ForceDisableInit.GetValue(null);
            body = GetComponent<Rigidbody>();
            shipComponent = GetComponent<Ship>();
            lastPosition = transform.position;
            FindRowingSeats();
            All.Add(this);

            if (nview != null && nview.IsValid())
                nview.Register<int>(RpcRow, RPC_Row);
        }

        /// <summary>Plugin unload (hot reload, update): take this copy off every ship.</summary>
        internal static void DestroyAll()
        {
            foreach (RowingShip s in All.ToArray())
                if (s != null) Destroy(s);
            All.Clear();
            LocalRowingShip = null;
        }

        private void Start()
        {
            if (seats.Length > 0) ResolveOarlock();
            if (seats.Length > 0 && !Headless && (preview || (nview != null && nview.IsValid())))
                BuildOars();
        }

        // ───────────────────────── per-ship oarlock ─────────────────────────

        private Plugin.ShipOarSettings shipCfg;
        private ShipProfile profile;                            // ships with oar ports (the Drakkar); null for the rest
        private float[] portYaw = Array.Empty<float>();          // per seat: degrees the port's wall faces, in the oar's own turn
        private Plugin.OarModel oarModel;
        private Transform[] fittings = Array.Empty<Transform>();   // the wooden oarlocks on the gunwale (the Karve), one per seat

        // Per-seat placement (ships like the Drakkar whose benches span the beam).
        private Vector3[] railPoint = Array.Empty<Vector3>(); // measured rail beside each seat, ship space
        private bool[] railFound = Array.Empty<bool>();
        private int[] railGroupIndex = Array.Empty<int>();    // seats sharing a stretch of rail get spread out
        private int[] railGroupCount = Array.Empty<int>();

        private float Outboard => shipCfg != null && shipCfg.Outboard != null ? shipCfg.Outboard.Value : Plugin.DefaultOutboard;
        private float OarlockHeight => shipCfg != null && shipCfg.Height != null ? shipCfg.Height.Value : Plugin.DefaultHeight;
        /// <summary>A ship with oar ports always places each oar at its port; the others as their F1 setting says.</summary>
        private bool PerSeat => profile != null || (shipCfg != null && shipCfg.Placement.Value == Plugin.PlacementPerSeat);
        /// <summary>Oars, splashes and rower HUD on this ship type (F1, live).</summary>
        private bool VisualsOn => shipCfg == null || shipCfg.Visuals.Value;
        /// <summary>Rowing thrust, turning and stamina cost on this ship type (F1, live).</summary>
        private bool PhysicsOn => shipCfg == null || shipCfg.Physics.Value;

        private float OarScale => (shipCfg != null ? shipCfg.Scale.Value : 1f) * Plugin.OarModelScale.Value;

        private void ResolveOarlock()
        {
            string shipName = Utils.GetPrefabName(gameObject);
            shipCfg = Plugin.GetShipSettings(shipName, MeasureOarlock);
            profile = ShipProfile.For(shipName);
            if (PerSeat) MeasurePerSeat();
        }

        /// <summary>Oar pivot for a seat in ship space (live: follows the F1 sliders).</summary>
        private Vector3 OarlockLocal(int s)
        {
            if (PerSeat && s < railPoint.Length)
            {
                Vector3 p = railFound[s]
                    ? railPoint[s]
                    : new Vector3(seatSide[s] * Outboard, seatLocal[s].y + OarlockHeight, seatLocal[s].z);
                p.x += seatSide[s] * shipCfg.PerSeatOutboard.Value; // + = further outboard, - = further in
                p.y += shipCfg.PerSeatHeight.Value;
                if (shipCfg.Spacing != null) p.z += (railGroupIndex[s] - (railGroupCount[s] - 1) * 0.5f) * shipCfg.Spacing.Value;
                return p;
            }
            return new Vector3(seatSide[s] * Outboard, seatLocal[s].y + OarlockHeight, seatLocal[s].z);
        }

        /// <summary>Default for "Fixed" ships: the rail beside the middle bench.</summary>
        private bool MeasureOarlock(out float distance, out float height)
        {
            int mid = seats.Length / 2;
            if (MeasureRail(seatLocal[mid], seatSide[mid], out distance, out float railY))
            {
                height = railY - seatLocal[mid].y;
                return true;
            }
            height = 0f;
            return false;
        }

        /// <summary>
        /// Casts rays in from outside the hull, beside a seat, at decreasing heights; the first height that hits
        /// this ship is the top of the rail. Returns the rail's distance from the centerline and its height.
        /// </summary>
        private bool MeasureRail(Vector3 seat, float side, out float distance, out float railY)
        {
            distance = 0f;
            railY = 0f;
            Ship self = GetComponent<Ship>();
            Vector3 dir = transform.TransformDirection(new Vector3(-side, 0f, 0f));

            for (float dy = 2.5f; dy >= -0.3f; dy -= 0.05f)
            {
                Vector3 origin = transform.TransformPoint(new Vector3(side * 20f, seat.y + dy, seat.z));
                float outer = -1f;
                foreach (RaycastHit hit in Physics.RaycastAll(origin, dir, 20f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || hit.collider.GetComponentInParent<Ship>() != self) continue;
                    float x = Mathf.Abs(transform.InverseTransformPoint(hit.point).x);
                    if (x > outer) outer = x;
                }
                if (outer > 0.2f)
                {
                    distance = outer;
                    railY = seat.y + dy + 0.05f; // just on top of the rail
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rail beside every seat, for hulls whose benches run across the beam. Seats that land on the same
        /// stretch of rail (one thwart seats several rowers) are spread fore and aft so their oars don't stack.
        /// </summary>
        private void MeasurePerSeat()
        {
            int n = seats.Length;
            railPoint = new Vector3[n];
            railFound = new bool[n];
            railGroupIndex = new int[n];
            railGroupCount = new int[n];

            int found = 0;
            portYaw = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (profile != null && profile.NearestPort(seatLocal[i].z, out ShipProfile.Port port))
                {
                    // the oar goes through the port nearest to the seat, square to the wall
                    railPoint[i] = new Vector3(seatSide[i] * port.X, profile.PortY, port.Z);
                    railFound[i] = true;
                    portYaw[i] = -seatSide[i] * port.Yaw;
                    found++;
                }
                else if (MeasureRail(seatLocal[i], seatSide[i], out float d, out float y))
                {
                    railPoint[i] = new Vector3(seatSide[i] * d, y, seatLocal[i].z);
                    railFound[i] = true;
                    found++;
                }
            }

            // Group by side + rail position (0.5 m buckets); order within a group by distance from the centerline.
            var groups = new Dictionary<string, List<int>>();
            for (int i = 0; i < n; i++)
            {
                string key = $"{seatSide[i]}:{Mathf.Round(seatLocal[i].z * 2f)}";
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
                list.Add(i);
            }
            foreach (var list in groups.Values)
            {
                list.Sort((a, b) => Mathf.Abs(seatLocal[a].x).CompareTo(Mathf.Abs(seatLocal[b].x)));
                for (int k = 0; k < list.Count; k++)
                {
                    railGroupIndex[list[k]] = k;
                    railGroupCount[list[k]] = list.Count;
                }
            }

            Plugin.Log.LogInfo($"[{Utils.GetPrefabName(gameObject)}] per-seat oarlocks: {(profile != null ? "oar port" : "rail")} found for {found}/{n} seats, " +
                               $"{groups.Count} rail groups");
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (LocalRowingShip == this) LocalRowingShip = null;
            if (nview != null && nview.IsValid()) nview.Unregister(RpcRow);
            foreach (Oar oar in oars)
                if (oar != null && oar.Pivot != null) Destroy(oar.Pivot.gameObject);
            foreach (Transform f in fittings)
                if (f != null) Destroy(f.gameObject);
            if (objMaterial != null) Destroy(objMaterial);
            if (fittingMaterial != null) Destroy(fittingMaterial);
        }

        private bool Valid => nview != null && nview.IsValid() && nview.GetZDO() != null;

        /// <summary>
        /// Only benches row. The helm seat and mast hold are also Chair components but use different
        /// attach animations, so they're filtered out. All clients must agree on this list (indices are synced).
        /// </summary>
        private static Transform SeatPoint(Chair c) => c.m_attachPoint != null ? c.m_attachPoint : c.transform;

        private void FindRowingSeats()
        {
            var allowed = new HashSet<string>();
            foreach (string a in Plugin.RowingSeatAnimations.Value.Split(','))
                if (a.Trim().Length > 0) allowed.Add(a.Trim());

            // Optional per-ship name filter (e.g. the Drakkar rows only from 'sit_bench', not its crates or helm).
            var names = new List<string>();
            foreach (string n in Plugin.GetSeatNameFilter(Utils.GetPrefabName(gameObject)).Value.Split(','))
                if (n.Trim().Length > 0) names.Add(n.Trim().ToLowerInvariant());

            // The helm never rows, whatever its seat is called: skip any seat right at a steering position.
            var helms = new List<Vector3>();
            foreach (ShipControlls sc in GetComponentsInChildren<ShipControlls>(true))
                helms.Add(transform.InverseTransformPoint(sc.m_attachPoint != null ? sc.m_attachPoint.position : sc.transform.position));

            Chair[] all = GetComponentsInChildren<Chair>(true);
            var rowing = new List<Chair>();
            var helmSeats = new HashSet<Chair>();
            foreach (Chair c in all)
            {
                if (!allowed.Contains(c.m_attachAnimation) || rowing.Count >= MaxSeats) continue;
                string cn = c.name.ToLowerInvariant();
                if (names.Count > 0 && !names.Exists(n => cn.StartsWith(n))) continue;

                Vector3 at = transform.InverseTransformPoint(SeatPoint(c).position);
                if (helms.Exists(h => Vector3.Distance(h, at) < 2f)) { helmSeats.Add(c); continue; }

                rowing.Add(c);
            }

            seats = rowing.ToArray();
            seatLocal = new Vector3[seats.Length];
            seatSide = new float[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                seatLocal[i] = transform.InverseTransformPoint(SeatPoint(seats[i]).position);
                seatSide[i] = Mathf.Abs(seatLocal[i].x) > 0.05f ? Mathf.Sign(seatLocal[i].x) : (i % 2 == 0 ? 1f : -1f);
            }

            string shipName = Utils.GetPrefabName(gameObject);
            if (loggedShips.Add(shipName))
            {
                foreach (Chair c in all)
                {
                    // Where the player actually sits (the attach point), not the seat object's pivot.
                    Vector3 local = transform.InverseTransformPoint(SeatPoint(c).position);
                    string status = rowing.Contains(c) ? "ROWS" : helmSeats.Contains(c) ? "excluded (helm)" : "excluded";
                    Plugin.Log.LogInfo($"[{shipName}] seat '{c.name}' anim='{c.m_attachAnimation}' " +
                                       $"sit=({local.x:F2},{local.y:F2},{local.z:F2}) {status}");
                }
            }
        }

        // ───────────────────────── every client ─────────────────────────

        private void Update()
        {
            if (!Valid)
            {
                if (preview) UpdateOars(0, 0, 0, Mathf.Max(Time.deltaTime, 0.016f));
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateLocalRower(dt);

            ZDO zdo = nview.GetZDO();
            UpdateOars(zdo.GetInt(ZdoRowMask, 0), zdo.GetInt(ZdoHardMask, 0), zdo.GetInt(ZdoBackMask, 0), dt);
        }

        /// <summary>The local rower's gear: -1 back, 0 oars up, 1 easy, 2 hard.</summary>
        internal int LocalGear => localGear;

        /// <summary>0 to 1 through the local rower's stroke: 1 with the blade deepest in the water (the HUD icon swells with it).</summary>
        internal float LocalStrokeLift =>
            localGear == GearNone ? 0f : 0.5f + 0.5f * Mathf.Cos(gearPhase[localGear == GearBack ? 0 : localGear == GearEasy ? 1 : 2]);

        /// <summary>World point the rower HUD floats at: just above the local rower's seat.</summary>
        /// <summary>
        /// World point the rower HUD floats at: just outboard of and above the local rower's oarlock,
        /// mirroring how the helm HUD sits by the rudder.
        /// </summary>
        internal Vector3 LocalHudAnchor
        {
            get
            {
                if (localSeat < 0 || localSeat >= seats.Length)
                    return transform.position + Vector3.up * 2f;

                Vector3 local = OarlockLocal(localSeat);
                // a ship whose oarlock is far from the bench (the Drakkar's ports): float the arrows by the rower, not out at the wall
                float reach = Mathf.Min(Mathf.Abs(local.x - seatLocal[localSeat].x), 1.2f);
                local.x = seatLocal[localSeat].x + seatSide[localSeat] * reach;
                local.x += seatSide[localSeat] * Plugin.HudOutboard.Value;
                return transform.TransformPoint(local) + Vector3.up * Plugin.HudHeight.Value;
            }
        }

        /// <summary>Called from the SetControls patch with the forward/back input while seated here.</summary>
        internal void HandleGearInput(float forward)
        {
            bool up = forward > 0.5f;
            bool down = forward < -0.5f;

            if (up && !upHeld) ShiftGear(+1);
            if (down && !downHeld) ShiftGear(-1);

            upHeld = up;
            downHeld = down;
        }

        private void ShiftGear(int delta)
        {
            int next = Mathf.Clamp(localGear + delta, GearBack, GearHard);
            if (next == localGear) return;
            localGear = next;
            if (!RowerHud.Working) // the HUD arrows replace the text once hooked
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, GearName(localGear));
        }

        /// <summary>A key as the game writes it in its own hints: coloured, and following the player's bindings (a controller's stick when one is in use).</summary>
        private static string KeyToken(string keyboard, string gamepad) =>
            "<color=yellow><b>$KEY_" + (ZInput.IsGamepadActive() ? gamepad : keyboard) + "</b></color>";

        private static string GearName(int gear)
        {
            switch (gear)
            {
                case GearBack: return "< " + Translations.T("heaveho_gear_back");
                case GearEasy: return "> " + Translations.T("heaveho_gear_easy");
                case GearHard: return ">> " + Translations.T("heaveho_gear_hard");
                default: return Translations.T("heaveho_gear_none");
            }
        }

        private void UpdateLocalRower(float dt)
        {
            Player player = Player.m_localPlayer;
            int seat = FindLocalSeat(player);

            if (seat < 0)
            {
                if (LocalRowingShip == this) LocalRowingShip = null;
                localGear = GearNone; // standing up always resets
                upHeld = downHeld = false;
            }
            else if (LocalRowingShip != this)
            {
                LocalRowingShip = this;
                localGear = GearNone;
                upHeld = downHeld = false;
                player.Message(MessageHud.MessageType.TopLeft, Translations.T("heaveho_hint",
                    KeyToken("Forward", "LStickUp"), KeyToken("Backward", "LStickDown"), KeyToken("Jump", "Jump")));
            }

            if (seat >= 0 && localGear != GearNone)
            {
                float stamina = player.GetStamina();
                if (exhausted && stamina >= Plugin.RecoverStamina.Value)
                {
                    exhausted = false;
                }
                else if (!exhausted && stamina <= 0.5f)
                {
                    exhausted = true;
                    player.Message(MessageHud.MessageType.Center, Translations.T("heaveho_tired"));
                }
            }

            bool rowing = seat >= 0 && localGear != GearNone && !exhausted;

            if (rowing)
            {
                float mult = localGear == GearHard ? 1f : Plugin.EasyStaminaMultiplier.Value;
                if (PhysicsOn) player.UseStamina(Plugin.StaminaPerSecond.Value * mult * dt);
            }

            // Left a seat (or switched seats) while rowing: release the old one right away.
            if (localRowing && (!rowing || seat != localSeat))
            {
                SendRow(-localSeat - 1);
                lastSentCode = int.MinValue;
            }

            if (rowing)
            {
                int code = seat * 4 + (localGear + 1); // gear -1/1/2 -> 0/2/3
                pingTimer -= dt;
                if (code != lastSentCode || pingTimer <= 0f)
                {
                    SendRow(code);
                    lastSentCode = code;
                    pingTimer = PingInterval;
                }
            }

            localRowing = rowing;
            localSeat = seat;
        }

        private int FindLocalSeat(Player player)
        {
            if (player == null || seats.Length == 0 || !player.IsAttached()) return -1;
            if (!VisualsOn && !PhysicsOn) return -1; // rowing switched off on this ship type

            Transform attach = PlayerAttachPoint(player);
            if (attach == null) return -1;

            Chair chair = attach.GetComponentInParent<Chair>();
            if (chair == null) return -1;

            return Array.IndexOf(seats, chair); // -1 for helm, mast, other ships, house chairs
        }

        /// <summary>code >= 0: seat*4 + (gear+1). code < 0: seat (-code - 1) stopped.</summary>
        private void SendRow(int code)
        {
            if (Valid) nview.InvokeRPC(RpcRow, code); // routed to the ship's owner
        }

        // ───────────────────────── owner only ─────────────────────────

        private void RPC_Row(long sender, int code)
        {
            if (!nview.IsOwner()) return; // ownership moved mid-flight; rowers re-ping the new owner
            if (code >= MaxSeats * 4 || code < -MaxSeats) return; // not a message this mod sends

            if (code >= 0)
                seatPings[code / 4] = new SeatPing { Time = Time.time, Gear = code % 4 - 1 };
            else
                seatPings.Remove(-code - 1);
        }

        private void FixedUpdate()
        {
            if (!Valid || !nview.IsOwner()) return;

            float now = Time.time;
            int rowMask = 0, hardMask = 0, backMask = 0;
            staleSeats.Clear();

            foreach (var kv in seatPings)
            {
                int s = kv.Key;
                if (now - kv.Value.Time > PingTimeout || s < 0 || s >= seats.Length)
                {
                    staleSeats.Add(s); // disconnected / crashed without saying goodbye
                    continue;
                }
                rowMask |= 1 << s;
                if (kv.Value.Gear == GearHard) hardMask |= 1 << s;
                if (kv.Value.Gear == GearBack) backMask |= 1 << s;
            }
            foreach (int s in staleSeats) seatPings.Remove(s);

            ZDO zdo = nview.GetZDO();
            if (zdo.GetInt(ZdoRowMask, 0) != rowMask) zdo.Set(ZdoRowMask, rowMask);
            if (zdo.GetInt(ZdoHardMask, 0) != hardMask) zdo.Set(ZdoHardMask, hardMask);
            if (zdo.GetInt(ZdoBackMask, 0) != backMask) zdo.Set(ZdoBackMask, backMask);

            ApplyRowingForces(rowMask, hardMask, backMask);
        }

        /// <summary>
        /// Each rower pushes from their own oarlock. The straight-line part moves the ship; the off-center
        /// part becomes yaw torque (scaled by Turning strength), so one side rowing or backing water turns it.
        /// </summary>
        private void ApplyRowingForces(int rowMask, int hardMask, int backMask)
        {
            if (rowMask == 0 || body == null || !PhysicsOn) return;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float along = Vector3.Dot(body.linearVelocity, forward);
            Vector3 center = body.worldCenterOfMass;

            Vector3 totalForce = Vector3.zero;
            Vector3 totalTorque = Vector3.zero;
            int rowerIndex = 0;

            for (int s = 0; s < seats.Length; s++)
            {
                if ((rowMask & (1 << s)) == 0) continue;

                bool back = (backMask & (1 << s)) != 0;
                bool hard = (hardMask & (1 << s)) != 0;
                float dir = back ? -1f : 1f;
                float gearMult = hard ? 1f : back ? Plugin.BackMultiplier.Value : Plugin.EasyMultiplier.Value;
                float bonus = Plugin.RowerBonus(rowerIndex++);

                if (along * dir >= Plugin.MaxRowingSpeed.Value) continue;

                Vector3 force = forward * (dir * Plugin.RowerStrength.Value * bonus * gearMult * body.mass);
                Vector3 oarlock = transform.TransformPoint(OarlockLocal(s));

                totalForce += force;
                totalTorque += Vector3.Cross(oarlock - center, force);
            }

            body.AddForce(totalForce, ForceMode.Force);

            // Keep only yaw so rowing never rolls or pitches the hull.
            Vector3 yaw = Vector3.Project(totalTorque, transform.up) * Plugin.TurnStrength.Value;
            body.AddTorque(yaw, ForceMode.Force);
        }

        // ───────────────────────── oars ─────────────────────────

        private void BuildOars()
        {
            MeshRenderer hull = FindHullRenderer();
            Material wood = hull != null ? hull.sharedMaterial : null; // Valheim shaders; avoids pink Standard
            int layer = hull != null ? hull.gameObject.layer : gameObject.layer;

            oarModel = Plugin.OarFor(Utils.GetPrefabName(gameObject));
            BuildFittings(wood, layer);
            oars = new Oar[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                var pivot = new GameObject("HeaveHo_Oar_" + i).transform;
                pivot.SetParent(transform, false);

                if (oarModel != null)
                {
                    oars[i] = new Oar { Pivot = pivot, Model = SpawnObjModel(pivot, wood, layer), Seat = i };
                }
                else if (Plugin.OarPrefab != null)
                {
                    // Custom model: origin = oarlock, blade along +X (see README).
                    oars[i] = new Oar { Pivot = pivot, Model = SpawnModel(pivot, wood, layer), Seat = i };
                }
                else
                {
                    // Placeholder primitives when no bundle is installed.
                    oars[i] = new Oar
                    {
                        Pivot = pivot,
                        Shaft = MakePart(PrimitiveType.Cylinder, pivot, wood, layer),
                        Blade = MakePart(PrimitiveType.Cube, pivot, wood, layer),
                        Seat = i
                    };
                }

                var tip = new GameObject("HeaveHo_BladeTip").transform;
                tip.SetParent(pivot, false);
                oars[i].Tip = tip;
            }
        }

        /// <summary>The small wooden oarlock an oar rides in, on ships that have no port for it (the Karve): one at each seat's oarlock.</summary>
        private void BuildFittings(Material hullMaterial, int layer)
        {
            Plugin.OarModel model = profile != null && profile.Fitting ? Plugin.FittingFor(Utils.GetPrefabName(gameObject)) : null;
            if (model == null || hullMaterial == null) return;

            // the hull's material, its own texture kept, no normal map (the mesh has no tangents)
            var mat = new Material(hullMaterial) { name = "HeaveHo_Oarlock" };
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
            fittingMaterial = mat;

            fittings = new Transform[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                var go = new GameObject("HeaveHo_Oarlock_" + i) { layer = layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                fittings[i] = go.transform;
            }
        }

        private Material fittingMaterial;

        // ───────────────────────── splashes ─────────────────────────

        /// <summary>Water surface height at a point, waves included; the world's sea level where the game finds no water.</summary>
        internal static float WaterLevelAt(Vector3 p)
        {
            float level = Floating.GetLiquidLevel(p, 1f, LiquidType.All);
            if (level > -1000f) return level;
            return ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
        }

        /// <summary>
        /// A splash where the blade really meets the water: when its tip goes under the surface, and a smaller one when it comes out.
        /// A ship heeled over, or beached, or an oar that never reaches the water, makes none.
        /// </summary>
        private void UpdateSplash(Oar oar, bool rowing, bool hard)
        {
            if (oar.Tip == null) return;
            if (!rowing || oar.Blend < 0.95f || !Plugin.Splashes.Value)
            {
                oar.InWater = false;   // not pulling: nothing to splash, and no need to ask where the water is
                return;
            }

            Vector3 tip = oar.Tip.position;
            float water = oar.WaterKnown ? oar.Water : WaterLevelAt(tip);
            // a little hysteresis, so a blade skimming the surface doesn't splash on every wobble
            bool nowIn = oar.InWater ? tip.y < water + 0.08f : tip.y < water - 0.02f;
            if (nowIn == oar.InWater) return;
            oar.InWater = nowIn;

            // Fire on the water surface directly under the blade tip, aligned with the ship.
            SplashEffects.Spawn(new Vector3(tip.x, water + 0.02f, tip.z), transform.rotation, nowIn, hard);
        }

        private static bool IsWaterMask(Renderer r, Material m)
        {
            string n = r.name.ToLowerInvariant();
            string sh = m.shader != null ? m.shader.name.ToLowerInvariant() : "";
            return n.Contains("watermask") || n.Contains("water_mask") || sh.Contains("watermask") || sh.Contains("water mask");
        }

        private Material objMaterial; // one per ship, shared by its oars

        private Transform SpawnObjModel(Transform pivot, Material hullMaterial, int layer)
        {
            var go = new GameObject("HeaveHo_OarModel");
            go.layer = layer;
            go.transform.SetParent(pivot, false);
            go.AddComponent<MeshFilter>().sharedMesh = oarModel.Mesh;

            if (objMaterial == null && hullMaterial != null)
            {
                // Valheim's own ship shader, with the oar's texture swapped in.
                objMaterial = new Material(hullMaterial) { name = "HeaveHo_OarObj" };
                if (objMaterial.HasProperty("_MainTex") && !oarModel.HullTexture)
                {
                    objMaterial.SetTexture("_MainTex", oarModel.Texture);
                    objMaterial.SetTextureScale("_MainTex", Vector2.one);
                    objMaterial.SetTextureOffset("_MainTex", Vector2.zero);
                }
                if (objMaterial.HasProperty("_Color") && !oarModel.HullTexture) objMaterial.SetColor("_Color", Color.white);
                if (objMaterial.HasProperty("_BumpMap")) objMaterial.SetTexture("_BumpMap", null);
                objMaterial.DisableKeyword("_NORMALMAP"); // the hull's normal map doesn't fit the oar's UVs
            }

            var renderer = go.AddComponent<MeshRenderer>();
            if (objMaterial != null) renderer.sharedMaterial = objMaterial;
            return go.transform;
        }

        private static Transform SpawnModel(Transform pivot, Material hullMaterial, int layer)
        {
            GameObject model = Instantiate(Plugin.OarPrefab, pivot, false);
            model.name = "HeaveHo_OarModel";

            foreach (Collider col in model.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(col); // never let oars touch ship physics

            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            // Bundled Standard materials can render pink in Valheim. Rebuild each one on the hull's
            // Valheim shader and carry the oar's own texture/color across.
            if (hullMaterial != null)
            {
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material source = mats[m];
                        var swapped = new Material(hullMaterial) { name = "HeaveHo_Oar_" + (source != null ? source.name : "mat") };
                        if (source != null)
                        {
                            if (source.HasProperty("_MainTex") && swapped.HasProperty("_MainTex"))
                            {
                                swapped.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                                swapped.SetTextureScale("_MainTex", Vector2.one);
                                swapped.SetTextureOffset("_MainTex", Vector2.zero);
                            }
                            if (source.HasProperty("_Color") && swapped.HasProperty("_Color"))
                                swapped.SetColor("_Color", source.GetColor("_Color"));
                            if (swapped.HasProperty("_BumpMap")) swapped.SetTexture("_BumpMap", null);
                            swapped.DisableKeyword("_NORMALMAP");
                        }
                        mats[m] = swapped;
                    }
                    r.sharedMaterials = mats;
                }
            }

            return model.transform;
        }

        /// <summary>
        /// The ship's visible hull: an active, opaque Valheim 'Custom/Piece' renderer, preferring one named 'hull'.
        /// (Picking the biggest renderer grabbed a translucent splash/shadow effect — that was the see-through bug.)
        /// </summary>
        private MeshRenderer FindHullRenderer()
        {
            MeshRenderer best = null;
            float bestScore = float.MinValue;
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null || m.shader == null || IsWaterMask(r, m)) continue;
                if (!r.gameObject.activeInHierarchy || r.name.StartsWith("HeaveHo_")) continue;
                if (m.renderQueue >= 2500) continue; // anything transparent is never the hull

                float score = r.bounds.size.sqrMagnitude;
                if (m.shader.name == "Custom/Piece") score += 10000f;
                if (r.name.Equals("hull", System.StringComparison.OrdinalIgnoreCase)) score += 100000f;
                if (score > bestScore) { best = r; bestScore = score; }
            }

            if (best != null && loggedHulls.Add(Utils.GetPrefabName(gameObject)))
                Plugin.Log.LogInfo($"[{Utils.GetPrefabName(gameObject)}] oar material from '{best.name}' " +
                                   $"(shader '{best.sharedMaterial.shader.name}')");
            return best;
        }

        private static Transform MakePart(PrimitiveType type, Transform parent, Material mat, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>()); // never let oars touch ship physics
            go.layer = layer;
            go.transform.SetParent(parent, false);
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private void UpdateOars(int rowMask, int hardMask, int backMask, float dt)
        {
            if (oars.Length == 0) return;

            // Each gear strokes at its own rate; oars in the same gear stay in unison.
            float baseRate = Plugin.StrokesPerSecond.Value * Mathf.PI * 2f;
            gearPhase[0] = (gearPhase[0] + dt * baseRate * 0.75f) % (Mathf.PI * 2f); // back
            gearPhase[1] = (gearPhase[1] + dt * baseRate * 0.75f) % (Mathf.PI * 2f); // easy
            gearPhase[2] = (gearPhase[2] + dt * baseRate * 1.2f) % (Mathf.PI * 2f);  // hard

            bool show = Plugin.ShowOars.Value && VisualsOn;
            float len = Plugin.PlaceholderOarLength * (shipCfg != null ? shipCfg.Scale.Value : 1f);

            // Under sail: the sail is up and the ship is really moving (from where it is, so it works on every player's game).
            float moved = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up).magnitude / dt;
            lastPosition = transform.position;
            speedSmoothed = Mathf.Lerp(speedSmoothed, moved, 1f - Mathf.Exp(-2f * dt));
            bool sailUp = shipComponent != null && shipComponent.IsSailUp();
            sailing = sailUp && speedSmoothed > (sailing ? 1.0f : 2.0f);   // a little hysteresis
            bool shipThem = sailing && Plugin.ShipUnderSail.Value;

            // The oarlocks belong to the boat: they show with the oars' visuals, in or out of use, where the oar's pivot is.
            for (int i = 0; i < fittings.Length; i++)
            {
                if (fittings[i].gameObject.activeSelf != show) fittings[i].gameObject.SetActive(show);
                if (!show) continue;
                fittings[i].localPosition = OarlockLocal(i);
                // lies on the hull side: along the rim, leaning with the wall (mirrored for the port side)
                fittings[i].localRotation = Quaternion.Euler(profile.FittingPitch, profile.FittingYaw * seatSide[i], profile.FittingRoll * seatSide[i]);
            }

            foreach (Oar oar in oars)
            {
                if (oar.Pivot.gameObject.activeSelf != show) oar.Pivot.gameObject.SetActive(show);
                if (!show) continue;

                int bit = 1 << oar.Seat;
                bool active = (rowMask & bit) != 0;
                bool back = (backMask & bit) != 0;
                bool hard = (hardMask & bit) != 0;
                float phase = back ? gearPhase[0] : hard ? gearPhase[2] : gearPhase[1];
                float side = seatSide[oar.Seat];

                oar.Blend = Mathf.MoveTowards(oar.Blend, active ? 1f : 0f, dt * 1.5f);

                // Drive: blade low, sweeping aft (or forward when backing water). Recovery: raised.
                float s = Mathf.Sin(phase);
                float c = Mathf.Cos(phase);
                float sweep = hard ? 32f : 24f;
                float strokeYaw = sweep * side * s * (back ? -1f : 1f);
                float strokeDip = -18f - 10f * c;
                // Stowed: raised and swung aft along the hull.
                float stowYaw = 75f * side;
                float stowDip = 12f;
                float baseYaw = side > 0f ? 0f : 180f; // local +X always points outboard

                // The vanilla ships (see ShipProfile): the oar is square to the wall (the Karve's to its gunwale), the sweep is what the
                // hole allows, and the stroke is deep enough that the blade reaches the water from the oarlock's height. Its blade
                // turns flat while it is in the air (feathered) and upright in the water. At rest it lies level in its port, or,
                // on the Karve, swung aft along the hull as that boat always had it.
                bool ported = profile != null && oar.Seat < portYaw.Length;
                float strokeRoll = 0f, stowRoll = 0f;

                // Where the water really is under this blade (this frame's reading, used for the stroke's depth and for the splash).
                // Only while the oar pulls: a resting oar asks nothing.
                if (active && oar.Blend > 0.5f && oar.Tip != null)
                {
                    oar.Water = WaterLevelAt(oar.Tip.position);
                    oar.WaterSmooth = oar.WaterKnown ? Mathf.Lerp(oar.WaterSmooth, oar.Water, 1f - Mathf.Exp(-5f * dt)) : oar.Water;
                    oar.WaterKnown = true;
                }
                else oar.WaterKnown = false;

                if (ported)
                {
                    baseYaw += portYaw[oar.Seat];
                    float sweepAngle = hard ? profile.SweepHard : profile.SweepEasy;
                    strokeYaw = sweepAngle * side * s * (back ? -1f : 1f);
                    stowYaw = profile.StowAlongHull ? profile.StowYaw * side : 0f;   // the Karve's oars rest swung aft, along the gunwale
                    stowDip = profile.StowDip;
                    stowRoll = profile.StowAlongHull ? 0f : 90f;
                    strokeRoll = 90f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.15f, c)));

                    // The stroke's depth: how far down the oar points when the blade meets the water (the middle of its dip).
                    // Level, that is set by the oarlock's height above the water the ship floats in (profile.Waterline).
                    Vector3 lock_ = OarlockLocal(oar.Seat);
                    float reach = oarModel != null ? (oarModel.Mesh.bounds.max.x - profile.TipInset) * OarScale : len;
                    float nominal = -Mathf.Asin(Mathf.Clamp((Mathf.Max(0.1f, lock_.y - profile.Waterline) + 0.05f) / reach, 0f, 0.95f)) * Mathf.Rad2Deg;
                    float catchDip = nominal;
                    if (oar.WaterKnown)
                    {
                        // The ship heels, pitches in the waves and sits lower with a load, so follow the water: solve for the dip
                        // that puts the tip just under the real surface, from the oarlock's real height and the ship's real tilt.
                        // (The oar's direction in the world has height  A cos(dip) + B sin(dip),  with A, B from the ship's axes and
                        // the oar's yaw at the catch; where the sweep has swung it to.)
                        float yawCatch = (baseYaw - sweepAngle * side * (back ? -1f : 1f)) * Mathf.Deg2Rad;
                        float a = transform.right.y * Mathf.Cos(yawCatch) - transform.forward.y * Mathf.Sin(yawCatch), b = transform.up.y;
                        float m = Mathf.Sqrt(a * a + b * b);
                        float t = (oar.WaterSmooth - 0.05f - transform.TransformPoint(lock_).y) / reach;
                        float dynamic = (Mathf.Asin(Mathf.Clamp(t / m, -1f, 1f)) - Mathf.Atan2(a, b)) * Mathf.Rad2Deg;
                        // never absurd: a few degrees up, a good deal more down, and never past straight down or level
                        catchDip = Mathf.Clamp(dynamic, Mathf.Max(nominal - 14f, -60f), Mathf.Min(nominal + 8f, -1f));
                    }
                    strokeDip = catchDip - profile.DipAmplitude * c;
                }

                float yaw = Mathf.Lerp(stowYaw, strokeYaw, oar.Blend);
                float dip = Mathf.Lerp(stowDip, strokeDip, oar.Blend);

                oar.Pivot.localPosition = OarlockLocal(oar.Seat);
                oar.Pivot.localRotation = Quaternion.Euler(0f, baseYaw + yaw, dip);

                if (oar.Tip != null)
                {
                    float tipX = oar.Model != null && oarModel != null
                        ? (oarModel.Mesh.bounds.max.x - (profile != null ? profile.TipInset : 0.3f)) * OarScale
                        : len - 0.35f;
                    oar.Tip.localPosition = new Vector3(tipX, 0f, 0f);
                }
                UpdateSplash(oar, active, hard);

                if (oar.Model != null)
                {
                    // An oar nobody pulls is only there if "Oars at rest" says so (and not while the ship sails): otherwise it slides
                    // out of its port (or appears at the rail) when its rower starts and back in when they stop, so the ship keeps
                    // its own shape.
                    bool restingOut = (shipCfg == null || shipCfg.ShowAtRest.Value) && !shipThem;
                    float outTarget = active || restingOut ? 1f : 0f;
                    oar.Out = oar.OutKnown ? Mathf.MoveTowards(oar.Out, outTarget, dt * 1.5f) : outTarget;
                    oar.OutKnown = true;
                    float out_ = Mathf.SmoothStep(0f, 1f, oar.Out);
                    if (oar.Model.gameObject.activeSelf != (out_ > 0.001f)) oar.Model.gameObject.SetActive(out_ > 0.001f);
                    oar.Model.localScale = new Vector3(OarScale * Mathf.Max(out_, 0.001f), OarScale, OarScale);
                    oar.Model.localRotation = ported ? Quaternion.Euler(Mathf.Lerp(stowRoll, strokeRoll, oar.Blend), 0f, 0f) : Quaternion.identity;
                    continue;
                }

                // Placeholder geometry re-applied every frame so F1 tweaks show up live.
                float total = len + OarInboard;
                oar.Shaft.localRotation = Quaternion.Euler(0f, 0f, 90f); // cylinder axis Y -> X
                oar.Shaft.localScale = new Vector3(0.07f, total * 0.5f, 0.07f);
                oar.Shaft.localPosition = new Vector3((len - OarInboard) * 0.5f, 0f, 0f);
                oar.Blade.localScale = new Vector3(0.7f, 0.22f, 0.04f);
                oar.Blade.localPosition = new Vector3(len - 0.35f, 0f, 0f);
            }
        }
    }
}
