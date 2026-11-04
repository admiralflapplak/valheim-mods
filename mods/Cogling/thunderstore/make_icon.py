"""
Builds thunderstore/icon.png (256 x 256): a Cogling on a painted background, in the style of the other mods' icons (Good Tidings,
Heave Ho, Weapon Oils): the figure on a soft glow with a vignette, a rim of light round its outline so it reads at 64 px, and a few
sparks.

The renders (sources/, each alone on a transparent background, from Claude Tools: render cogling ... size=1024x1024 bg=clear) are
the mod's own model.

Needs:  pip install numpy Pillow

    python make_icon.py                 icon.png from the chosen draft
    python make_icon.py drafts          every draft in drafts/, and drafts/drafts.png side by side (also at 64 px)
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SIZE = 1024   # drawn at 4x, saved at 256

# name: (Cogling render, crop box in it, background, antenna behind him?)
DRAFTS = {
    "cyan": ("cogling_front.png", (84, 130, 954, 1000), "cyan", False),
    "cyan_close": ("cogling_close.png", (152, 0, 952, 800), "cyan", False),
    "forge": ("cogling_front.png", (84, 130, 954, 1000), "forge", False),
    "night": ("cogling_front.png", (84, 130, 954, 1000), "night", False),
    "cyan_antenna": ("cogling_front.png", (84, 130, 954, 1000), "cyan", True),
    "forge_antenna": ("cogling_front.png", (84, 130, 954, 1000), "forge", True),
}
CHOSEN = "cyan_antenna"

RIMS = {"cyan": (120, 230, 255), "forge": (255, 170, 80), "night": (140, 190, 255)}


def grid():
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    return x, y


def to_image(rgb):
    return Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")


def background(kind):
    x, y = grid()
    glow = np.exp(-(((x - 0.5) / 0.48) ** 2 + ((y - 0.5) / 0.48) ** 2))[..., None]
    if kind == "cyan":
        # his eyes' glow
        dark = np.stack([0.02 + 0.02 * y, 0.05 + 0.03 * y, 0.08 + 0.04 * y], -1)
        return dark * (1 - glow) + np.array([0.10, 0.72, 0.95]) * glow * 0.8
    if kind == "forge":
        # a workshop lit by its forge: copper and fire
        dark = np.stack([0.07 + 0.03 * y, 0.04 + 0.02 * y, 0.04 + 0.02 * y], -1)
        return dark * (1 - glow) + np.array([0.95, 0.52, 0.18]) * glow * 0.85
    if kind == "night":
        dark = np.stack([0.02 + 0.02 * y, 0.03 + 0.03 * y, 0.09 + 0.05 * y], -1)
        return dark * (1 - glow) + np.array([0.20, 0.42, 0.95]) * glow * 0.75
    raise ValueError(kind)


def vignette(img, strength=0.6):
    x, y = grid()
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2) / 0.71
    return to_image(np.asarray(img).astype(np.float32) / 255 * (1 - strength * d[..., None] ** 2))


def sparks(img, tint, seed=7, count=38):
    # small soft points of light drifting about (as Weapon Oils' and Good Tidings' have)
    rng = np.random.default_rng(seed)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    px = layer.load()
    for _ in range(count):
        cx, cy = rng.uniform(0.04, 0.96) * SIZE, rng.uniform(0.04, 0.85) * SIZE
        r = rng.uniform(2.0, 6.5)
        a = rng.uniform(0.35, 0.95)
        for yy in range(int(cy - r * 3), int(cy + r * 3)):
            for xx in range(int(cx - r * 3), int(cx + r * 3)):
                if 0 <= xx < SIZE and 0 <= yy < SIZE:
                    f = np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / (2 * r * r)) * a
                    if f > 0.01:
                        old = px[xx, yy][3] / 255
                        px[xx, yy] = tint + (int(min(1, old + f) * 255),)
    img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(1.2)))


def figure(name, box):
    return Image.open(os.path.join(HERE, "sources", name)).convert("RGBA").crop(box).resize((SIZE, SIZE), Image.LANCZOS)


def rimmed(img, fig, tint, grow=9, blur=14, strength=0.85):
    rim = Image.new("RGBA", fig.size, tint + (0,))
    rim.putalpha(fig.getchannel("A").filter(ImageFilter.MaxFilter(grow)).filter(ImageFilter.GaussianBlur(blur)).point(lambda a: int(a * strength)))
    img.alpha_composite(rim)
    img.alpha_composite(fig)


def compose(name):
    source, box, kind, antenna = DRAFTS[name]
    tint = RIMS[kind]
    img = vignette(to_image(background(kind))).convert("RGBA")
    sparks(img, tint)
    if antenna:
        # his antenna behind him, to one side, smaller and dimmer (the lamp still glowing)
        a = Image.open(os.path.join(HERE, "sources", "antenna.png")).convert("RGBA").crop((200, 200, 840, 840)).resize((520, 520), Image.LANCZOS)
        dim = Image.new("RGBA", a.size, (0, 0, 0, 0))
        dim = Image.blend(dim, a, 0.75)
        dim.putalpha(a.getchannel("A").point(lambda v: int(v * 0.85)))
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        layer.alpha_composite(dim, (SIZE - 520, 300))
        rimmed(img, layer, tint, grow=5, blur=10, strength=0.5)
    rimmed(img, figure(source, box), tint)
    return img.convert("RGB")


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "drafts":
        out = os.path.join(HERE, "drafts")
        os.makedirs(out, exist_ok=True)
        names = list(DRAFTS)
        sheet = Image.new("RGB", (3 * 276 + 20, 2 * (276 + 84)), (30, 30, 34))
        for i, name in enumerate(names):
            icon = compose(name).resize((256, 256), Image.LANCZOS)
            icon.save(os.path.join(out, name + ".png"))
            x, y = 20 + (i % 3) * 276, 20 + (i // 3) * 360
            sheet.paste(icon, (x, y))
            sheet.paste(icon.resize((64, 64), Image.LANCZOS), (x, y + 266))
        sheet.save(os.path.join(out, "drafts.png"))
        print(os.path.join(out, "drafts.png"))
        return
    out = os.path.join(HERE, "icon.png")
    compose(CHOSEN).resize((256, 256), Image.LANCZOS).save(out)
    print(out)


if __name__ == "__main__":
    main()
