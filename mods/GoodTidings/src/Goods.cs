using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The trader's goods on the Lox, from the game's own props. Where each sits is measured from the seat (metres right, up, back along
    /// the Lox), with its turn (degrees: yaw, pitch, roll) and size; mirrored ones are also put on the left side. Placed by the user with
    /// F1 sliders (2026-10-28): the values below.
    /// </summary>
    internal static class Goods
    {
        private sealed class Def
        {
            public string Key, Prefab, Note;
            public float Right, Up, Back, Yaw, Pitch, Roll, Size;
            public bool Mirrored;
            /// <summary>Made facing +x (outward on the right): on the left it is turned round to face out there too, then turned as set.</summary>
            public bool FacesOut;
        }

        // (placed by the user in F1, 2026-10-28)
        private static readonly Def[] Defs =
        {
            new Def { Key = "Rune Crate", Prefab = "dvergrprops_crate_long", Note = "the long rune crate across the rump", Right = -0.035f, Up = -0.385f, Back = 1.244f, Size = 1f },
            new Def { Key = "Small Crate", Prefab = "dvergrprops_crate", Note = "a small crate on it", Right = 0.114f, Up = 0.132f, Back = 1.263f, Yaw = 15f, Size = 0.42f },
            new Def { Key = "Keg", Prefab = "dvergrprops_barrel", Note = "a keg beside it", Right = -0.36f, Up = 0.147f, Back = 1.294f, Yaw = -8.636f, Size = 0.3f },
            new Def { Key = "Pelt", Prefab = "LoxPelt", Note = "a rolled pelt behind", Right = 0f, Up = -0.561f, Back = 1.721f, Yaw = 82.43f, Roll = 7.864f, Size = 0.491f },
            new Def { Key = "Lantern Pole", Prefab = "piece_dvergr_lantern_pole", Note = "a lantern on a pole over the load", Right = -0.728f, Up = 1.267f, Back = 1.188f, Yaw = 75.146f, Pitch = -8.832f, Roll = -6.308f, Size = 0.528f },
            new Def { Key = "Pots", Prefab = "piece_pot1", Note = "clay pots tied on, both sides", Right = 0.942f, Up = -0.48f, Back = 0.825f, Yaw = -5.047f, Pitch = 6.505f, Roll = 12.523f, Size = 0.441f, Mirrored = true },
            new Def { Key = "Saddle Bags", Prefab = SaddleBagPrefab, Note = "our own leather bags, both sides; Roll leans their tops in against the Lox", Right = 1.267f, Up = -0.525f, Back = 0.79f, Roll = 31.879f, Size = 1.804f, Mirrored = true, FacesOut = true },
        };

        /// <summary>Not a game prefab: the bags are our own model (SaddleBag.cs).</summary>
        private const string SaddleBagPrefab = "goodtidings_saddlebag";

        private sealed class Placed { public Def Def; public int Side; public Follow Follow; public Transform Model; }
        private static readonly List<Placed> Shown = new List<Placed>();
        internal static bool Enabled = true;

        /// <summary>Puts every good on a Lox (its look) at its seat; adds what it makes to <paramref name="made"/>.</summary>
        internal static void Load(Transform lox, Transform seat, List<GameObject> made, Transform under = null)
        {
            if (!Enabled) return;
            foreach (Def d in Defs)
                for (int side = 1; side >= (d.Mirrored ? -1 : 1); side -= 2)
                {
                    if (d.Prefab != SaddleBagPrefab && ZNetScene.instance.GetPrefab(d.Prefab) == null) { Plugin.Log.LogWarning($"No prop {d.Prefab}"); continue; }
                    var holder = new GameObject("goodtidings_goods");
                    if (under) holder.transform.SetParent(under, false);
                    var f = holder.AddComponent<Follow>();
                    f.Target = seat; f.Frame = lox; f.FrameRotation = true;
                    StrapToHip(f, lox);
                    holder.transform.SetPositionAndRotation(seat.position, lox.rotation);
                    GameObject m = d.Prefab == SaddleBagPrefab ? SaddleBag.Build(holder.transform) : DvergerLook.Model(d.Prefab, holder.transform);
                    var p = new Placed { Def = d, Side = side, Follow = f, Model = m.transform };
                    Shown.Add(p);
                    Place(p);
                    made.Add(holder);
                }
        }

        /// <summary>
        /// Straps a good to the Lox's hip bone (the one the saddle hangs from), so it rocks and tilts with his back. Its place is set
        /// with the Lox at rest: the seat, and the hip's place and turn, measured from the Lox in the game's own Lox prefab.
        /// </summary>
        private static void StrapToHip(Follow f, Transform lox)
        {
            Transform hip = Utils.FindChild(lox, "Hip");
            Transform restLox = ZNetScene.instance.GetPrefab("Lox")?.transform.Find("Visual");
            Transform restHip = restLox ? Utils.FindChild(restLox, "Hip") : null, restSeat = restLox ? Utils.FindChild(restLox, "attachpoint") : null;
            if (hip == null || restHip == null || restSeat == null) return;   // (then it follows the seat, as before)
            f.Bone = hip;
            f.RestSeat = restLox.InverseTransformPoint(restSeat.position);
            f.RestBone = restLox.InverseTransformPoint(restHip.position);
            f.RestBoneTurn = Quaternion.Inverse(restLox.rotation) * restHip.rotation;
        }

        private static void Place(Placed p)
        {
            Def d = p.Def;
            p.Follow.Right = p.Side * d.Right;
            p.Follow.Up = d.Up;
            p.Follow.Back = d.Back;
            p.Model.localScale = Vector3.one * d.Size;
            p.Model.localRotation = d.FacesOut
                ? Quaternion.Euler(0f, p.Side < 0 ? 180f : 0f, 0f) * Quaternion.Euler(d.Pitch, d.Yaw, d.Roll)
                : Quaternion.Euler(d.Pitch, p.Side * d.Yaw, p.Side * d.Roll);
        }

    }
}
