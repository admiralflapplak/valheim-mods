using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// The trader's goods on the Lox, from the game's own props. Where each sits is measured from the seat (metres right, up, back along
    /// the Lox), with its turn (degrees: yaw, pitch, roll) and size; mirrored ones are also put on the left side. For now each is a
    /// setting in "90 - Goods Placement" (this player's own, not the server's), so the placement can be tuned live in F1: a change moves the
    /// goods already shown, without building them again. The values chosen become the defaults below.
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
            public ConfigEntry<bool> On;
            public ConfigEntry<float> CRight, CUp, CBack, CYaw, CPitch, CRoll, CSize;
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
            new Def { Key = "Tankard", Prefab = "Tankard_dvergr", Note = "a tankard hung from the saddle", Right = 0f, Up = 0.055f, Back = 0.122f, Yaw = -95.455f, Size = 1f },
            new Def { Key = "Saddle Bags", Prefab = SaddleBagPrefab, Note = "our own leather bags, both sides; Roll leans their tops in against the Lox", Right = 1.267f, Up = -0.525f, Back = 0.79f, Roll = 31.879f, Size = 1.804f, Mirrored = true, FacesOut = true },
        };

        /// <summary>Not a game prefab: the bags are our own model (SaddleBag.cs).</summary>
        private const string SaddleBagPrefab = "goodtidings_saddlebag";

        private sealed class Placed { public Def Def; public int Side; public Follow Follow; public Transform Model; }
        private static readonly List<Placed> Shown = new List<Placed>();
        internal static bool Enabled = true;

        internal static void Bind(ConfigFile config)
        {
            const string section = "90 - Goods Placement";
            int order = 1000;
            ConfigEntry<float> F(Def d, string name, float value, float min, float max, string what) =>
                config.Bind(section, $"{d.Key} {name}", value, new ConfigDescription($"{d.Key} ({d.Note}): {what}",
                    new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { Order = order-- }));
            foreach (Def d in Defs)
            {
                d.On = config.Bind(section, $"{d.Key} Shown", true, new ConfigDescription($"{d.Key} ({d.Note}) on the Lox.", null,
                    new ConfigurationManagerAttributes { Order = order-- }));
                d.CRight = F(d, "Right", d.Right, -2.5f, 2.5f, "metres to the right of the seat (mirrored ones: out to each side).");
                d.CUp = F(d, "Up", d.Up, -2f, 3f, "metres above the seat.");
                d.CBack = F(d, "Back", d.Back, -3f, 4f, "metres behind the seat, along the Lox.");
                d.CYaw = F(d, "Yaw", d.Yaw, -180f, 180f, "turn round, degrees.");
                d.CPitch = F(d, "Pitch", d.Pitch, -180f, 180f, "tip forward or back, degrees.");
                d.CRoll = F(d, "Roll", d.Roll, -180f, 180f, "lean to the side, degrees.");
                d.CSize = F(d, "Size", d.Size, 0.05f, 3f, "size (1: the game's own).");
            }
            config.SettingChanged += (_, e) => { if (e.ChangedSetting.Definition.Section == section) Apply(); };
        }

        /// <summary>Puts every good on a Lox (its look) at its seat; adds what it makes to <paramref name="made"/>.</summary>
        internal static void Load(Transform lox, Transform seat, List<GameObject> made)
        {
            if (!Enabled) return;
            foreach (Def d in Defs)
                for (int side = 1; side >= (d.Mirrored ? -1 : 1); side -= 2)
                {
                    if (d.Prefab != SaddleBagPrefab && ZNetScene.instance.GetPrefab(d.Prefab) == null) { Plugin.Log.LogWarning($"No prop {d.Prefab}"); continue; }
                    var holder = new GameObject("goodtidings_goods");
                    var f = holder.AddComponent<Follow>();
                    f.Target = seat; f.Frame = lox; f.FrameRotation = true;
                    holder.transform.SetPositionAndRotation(seat.position, lox.rotation);
                    GameObject m = d.Prefab == SaddleBagPrefab ? SaddleBag.Build(holder.transform) : DvergerLook.Model(d.Prefab, holder.transform);
                    var p = new Placed { Def = d, Side = side, Follow = f, Model = m.transform };
                    Shown.Add(p);
                    Place(p);
                    made.Add(holder);
                }
        }

        private static void Place(Placed p)
        {
            Def d = p.Def;
            p.Follow.Right = p.Side * d.CRight.Value;
            p.Follow.Up = d.CUp.Value;
            p.Follow.Back = d.CBack.Value;
            p.Model.localScale = Vector3.one * d.CSize.Value;
            p.Model.localRotation = d.FacesOut
                ? Quaternion.Euler(0f, p.Side < 0 ? 180f : 0f, 0f) * Quaternion.Euler(d.CPitch.Value, d.CYaw.Value, d.CRoll.Value)
                : Quaternion.Euler(d.CPitch.Value, p.Side * d.CYaw.Value, p.Side * d.CRoll.Value);
            p.Follow.gameObject.SetActive(d.On.Value);
        }

        private static void Apply()
        {
            Shown.RemoveAll(p => p.Follow == null || p.Model == null);
            foreach (Placed p in Shown) Place(p);
        }

        internal static void Forget() => Shown.Clear();
    }
}
