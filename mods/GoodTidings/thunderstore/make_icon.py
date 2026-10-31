"""
Builds thunderstore/icon.png (256 x 256): one of the visitors on a painted background.

The renders are the ones make_showcase.py uses (sources/, each visitor alone on a transparent background); they are the game's art,
so they are not kept in the repository; the icon is.

Needs:  pip install numpy Pillow

    python make_icon.py                 icon.png from the chosen draft
    python make_icon.py drafts          every draft in drafts/, and drafts/drafts.png side by side (also at 64 px)
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SIZE = 1024   # drawn at 4x, saved at 256

# name: (source, crop box in the 1024 render, background)
DRAFTS = {
    "skald_fire": ("skald.png", (250, 30, 950, 730), "fire"),
    "skald_blue": ("skald.png", (250, 30, 950, 730), "blue"),
    "skald_wide": ("skald.png", (130, 0, 970, 840), "fire"),
    "muninn_blue": ("muninn.png", (380, 0, 960, 580), "blue"),
    "muninn_gold": ("muninn.png", (380, 0, 960, 580), "gold"),
    "dagny_blue": ("trader.png", (60, 120, 870, 930), "blue"),
}
CHOSEN = "skald_blue"


def grid():
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    return x, y


def to_image(rgb):
    return Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")


def background(kind):
    x, y = grid()
    glow = np.exp(-(((x - 0.5) / 0.5) ** 2 + ((y - 0.55) / 0.5) ** 2))[..., None]
    if kind == "fire":
        # a dark hall lit by the hearth he plays beside
        dark = np.stack([0.06 + 0.03 * y, 0.05 + 0.02 * y, 0.10 + 0.02 * y], -1)
        return dark * (1 - glow) + np.array([0.95, 0.50, 0.16]) * glow * 0.9
    if kind == "blue":
        # the good visit's blue circle on the map, as a soft glow
        dark = np.stack([0.03 + 0.02 * y, 0.05 + 0.03 * y, 0.12 + 0.04 * y], -1)
        return dark * (1 - glow) + np.array([0.30, 0.62, 1.0]) * glow * 0.85
    if kind == "gold":
        dark = np.stack([0.08 + 0.02 * y, 0.06 + 0.02 * y, 0.10 + 0.02 * y], -1)
        return dark * (1 - glow) + np.array([1.0, 0.80, 0.30]) * glow * 0.85
    raise ValueError(kind)


def vignette(img, strength=0.55):
    x, y = grid()
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2) / 0.71
    return to_image(np.asarray(img).astype(np.float32) / 255 * (1 - strength * d[..., None] ** 2))


def compose(name):
    source, box, kind = DRAFTS[name]
    img = vignette(to_image(background(kind))).convert("RGBA")
    fig = Image.open(os.path.join(HERE, "sources", source)).convert("RGBA").crop(box).resize((SIZE, SIZE), Image.LANCZOS)
    # a light rim from the glow behind, so the outline reads at 64 px
    tint = {"fire": (255, 160, 70), "blue": (140, 200, 255), "gold": (255, 220, 120)}[kind]
    rim = Image.new("RGBA", fig.size, tint + (0,))
    rim.putalpha(fig.getchannel("A").filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(14)).point(lambda a: int(a * 0.8)))
    img.alpha_composite(rim)
    img.alpha_composite(fig)
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
