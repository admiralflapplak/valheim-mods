using UnityEngine;

namespace HeaveHo
{
    /// <summary>
    /// What is known by hand about a ship whose planking has real oar ports (the Longship and the Drakkar; the Karve has none, its oars
    /// sit on the rail and are measured at run time). The oars go through the ports: the ports' positions and the way each one faces
    /// were read from the hull's mesh once, and are listed here.
    /// </summary>
    internal class ShipProfile
    {
        /// <summary>One oar port on the starboard side; the port side is its mirror.</summary>
        internal struct Port
        {
            public float X, Z;
            /// <summary>Degrees the wall faces fore (+) or aft (-) of straight out: the oar goes through the port square to the wall.</summary>
            public float Yaw;
            public Port(float x, float z, float yaw) { X = x; Z = z; Yaw = yaw; }
        }

        internal Port[] Ports;
        /// <summary>Port height and the water's height in ship space (metres): how far the blade has to reach down.</summary>
        internal float PortY, Waterline;
        /// <summary>Stroke through a port: a sweep the hole allows, the blade feathered while it is in the air.</summary>
        internal float SweepEasy, SweepHard, DipAmplitude, StowDip;
        /// <summary>For oars that rest along the hull (the Karve): how far aft of straight out they swing. The hull widens toward midships, so a little under 90 keeps them just outside the rim.</summary>
        internal float StowYaw;
        /// <summary>How far in from the end of the oar model the blade's tip is, for the splash.</summary>
        internal float TipInset;
        /// <summary>At rest the oars lie swung aft along the hull (the Karve), resting along the gunwale, instead of level through their ports.</summary>
        internal bool StowAlongHull;
        /// <summary>"Oars at rest" starts on: the oars stay out when nobody rows.</summary>
        internal bool OarsOutAtRest;
        /// <summary>The oar rides in a small wooden oarlock on the gunwale (oarlock_&lt;ship&gt;.obj), which this mod draws.</summary>
        internal bool Fitting;
        /// <summary>How the fitting is turned to lie on the hull side (degrees; yaw and roll are mirrored for the port side): pitch with the rim's climb, yaw with the rim's run, roll with the wall's lean.</summary>
        internal float FittingPitch, FittingYaw, FittingRoll;

        internal static ShipProfile For(string ship)
        {
            switch (ship)
            {
                case "VikingShip_Ashlands": return Drakkar;
                case "VikingShip": return Longship;
                case "Karve": return Karve;
                default: return null;
            }
        }

        /// <summary>The port nearest to a seat's fore-aft position, on the seat's side.</summary>
        internal bool NearestPort(float seatZ, out Port port)
        {
            port = default;
            float best = float.MaxValue;
            foreach (Port p in Ports)
            {
                float d = Mathf.Abs(p.Z - seatZ);
                if (d < best) { best = d; port = p; }
            }
            return best < float.MaxValue;
        }

        // The Karve has no oar ports: a clean gunwale edge at 1.0 m out and 0.9 m up (read from the hull), with a wooden oarlock on it
        // for each of the two oars. The pivot sits in the oarlock, 0.1 m above the rim; the water is about 0.15 m above the ship's
        // origin when it floats. The stroke and the oars' resting place along the hull are the ones this boat always had.
        private static readonly ShipProfile Karve = new ShipProfile
        {
            Ports = new[] { new Port(1.0f, 2.23f, 0f) },
            PortY = 0.98f,
            Waterline = 0.15f,
            SweepEasy = 24f,
            SweepHard = 32f,
            DipAmplitude = 9f,
            StowDip = 2f,
            StowYaw = 80f,
            TipInset = 0.1f,
            StowAlongHull = true,
            OarsOutAtRest = true,
            Fitting = true,
            // at the oarlock the rim runs in toward the bow at about 14 degrees, climbs about 6, and the wall leans in about 19
            FittingPitch = -6f,
            FittingYaw = -14f,
            FittingRoll = 18f
        };

        // Read from the Longship's hull: 8 a side, 0.9 m apart, only 0.13 m wide, 0.83 m above the keel line, in a plank 6 cm thick; the
        // water is 0.5 m above the ship's origin when it floats (from where moored Longships sit), so the ports are 0.3 m above it.
        // The holes are drilled straight across the ship, so every oar goes through square to it (yaw 0).
        private static readonly ShipProfile Longship = new ShipProfile
        {
            Ports = new[]
            {
                new Port(1.26f, -6.72f, 0f), new Port(1.60f, -5.82f, 0f), new Port(1.88f, -4.93f, 0f), new Port(2.11f, -4.03f, 0f),
                new Port(2.27f, -3.14f, 0f), new Port(2.38f, -2.24f, 0f), new Port(2.44f, -1.34f, 0f), new Port(2.46f, -0.45f, 0f),
                new Port(2.46f, 0.45f, 0f), new Port(2.44f, 1.34f, 0f), new Port(2.38f, 2.24f, 0f), new Port(2.27f, 3.14f, 0f),
                new Port(2.11f, 4.03f, 0f), new Port(1.88f, 4.93f, 0f), new Port(1.60f, 5.82f, 0f), new Port(1.26f, 6.72f, 0f)
            },
            PortY = 0.83f,
            Waterline = 0.5f,
            SweepEasy = 16f,
            SweepHard = 22f,
            DipAmplitude = 8f,
            StowDip = 2f,
            TipInset = 0.1f
        };

        // Read from the Drakkar's hull (the brass rings round each port): 16 a side, a metre apart, 2.94 m above the keel line,
        // with the water 1.1 m above the ship's origin when it floats.
        private static readonly ShipProfile Drakkar = new ShipProfile
        {
            Ports = new[]
            {
                new Port(4.01f, -7.86f, -26.6f), new Port(4.43f, -6.80f, -20.3f), new Port(4.84f, -5.68f, -20.2f),
                new Port(5.15f, -4.71f, -6.6f), new Port(5.29f, -3.71f, -6.6f), new Port(5.44f, -2.66f, -6.6f),
                new Port(5.53f, -1.60f, -0.1f), new Port(5.56f, -0.54f, -0.1f),
                new Port(5.56f, 0.54f, 0.1f), new Port(5.53f, 1.60f, 0.1f),
                new Port(5.44f, 2.66f, 6.6f), new Port(5.29f, 3.71f, 6.6f), new Port(5.15f, 4.71f, 6.6f),
                new Port(4.84f, 5.68f, 20.2f), new Port(4.43f, 6.80f, 20.3f), new Port(4.01f, 7.86f, 26.6f)
            },
            PortY = 2.94f,
            Waterline = 1.1f,
            SweepEasy = 11f,
            SweepHard = 15f,
            DipAmplitude = 7f,
            StowDip = 3f,
            TipInset = 0.1f
        };
    }
}
