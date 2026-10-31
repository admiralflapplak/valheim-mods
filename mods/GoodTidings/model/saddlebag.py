"""Good Tidings' saddle bag: a leather pannier hung on each side of the wandering trader's Lox.

Origin at the top middle of the bag, where it hangs from the saddle; y up, +x outward (away from the Lox), z along the Lox.
Materials: Leather (the game's leather armour panel), Coal (dark strap leather, from the Lox saddle), Iron (buckles).
Draw it: python saddlebag.py <out folder>   Write the C#: python saddlebag.py <out folder> cs
"""
import os, sys
sys.path.insert(0, os.path.join(os.environ["APPDATA"], "com.kesomannen.gale", "valheim", "profiles", "Default", "BepInEx", "claude", "modelkit"))
from modelkit import Model, contact_sheet

L, H, T = 0.62, 0.44, 0.24   # length (along the Lox), height, thickness


def make():
    m = Model()
    # the bag: a leather box with a rounded bottom
    m.box("body", 0, -H / 2, 0, T, H, L, "Leather")
    m.cyl("bottom", 0, -H + 0.005, 0, T - 0.012, L / 2 - 0.008, 0.14, "Leather", rx=90)
    # a flap over the top, coming down the outer face
    m.box("flapTop", 0.005, 0.016, 0, T + 0.03, 0.03, L + 0.03, "Coal")
    m.box("flapFront", T / 2 + 0.016, -0.11, 0, 0.03, 0.24, L + 0.045, "Coal")
    m.box("flapEdge", T / 2 + 0.024, -0.235, 0, 0.03, 0.025, L + 0.06, "Coal")
    # two straps down the outer face, each with an iron buckle
    for z in (-0.18, 0.18):
        m.box("strap", T / 2 + 0.04, -0.24, z, 0.016, 0.36, 0.055, "Coal")
        m.box("buckle", T / 2 + 0.054, -0.2, z, 0.014, 0.065, 0.075, "Iron")
        m.box("buckleBar", T / 2 + 0.066, -0.2, z, 0.012, 0.014, 0.088, "Iron")
        # the strap it hangs from, up over the top towards the saddle
        m.box("hanger", -T / 2 + 0.03, 0.08, z, 0.035, 0.13, 0.06, "Coal")
    # a small pouch stitched on the outer face, below the flap
    m.box("pouch", T / 2 + 0.04, -0.34, 0, 0.07, 0.15, 0.21, "Leather")
    m.box("pouchFlap", T / 2 + 0.083, -0.285, 0, 0.018, 0.06, 0.225, "Coal")
    # iron rings at the bottom corners
    for z in (-L / 2 + 0.04, L / 2 - 0.04):
        m.cyl("ring", T / 2 + 0.015, -H + 0.03, z, 0.07, 0.008, 0.07, "Iron", rz=90)
    return m


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out, exist_ok=True)
    # seen from outside the Lox (+x), close up; views turned so the outer face shows first
    contact_sheet(make(), os.path.join(out, "saddlebag_sheet.png"), views=((-60, 15), (-120, 15), (-90, 5), (90, 15)),
                  target=(0, -0.2, 0), dist=2.0, ground=False)
    if len(sys.argv) > 2 and sys.argv[2] == "cs":
        open(os.path.join(os.path.dirname(__file__), "..", "src", "SaddleBagModel.cs"), "w").write(make().to_csharp("GoodTidings", "SaddleBagModel"))
