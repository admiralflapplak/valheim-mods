"""
Builds thunderstore/icon.png (256 x 256): Rocko laughing, head and shoulders, on a painted background.

The render is made in the game with Fuling Heckler's own console command (through Claude Tools' `console`): he's summoned laughing,
held still mid-laugh, and photographed alone on a transparent background, hat and recolour included:

    heckler summon laugh      (then, a few seconds later)
    heckler freeze
    heckler portrait 2.0 12 0 1024 1.1   (from 1.1 m above: his head is thrown back laughing, so this shows his face and the hat)
                                         -> BepInEx/config/FulingHeckler_portraits/<time>.png, copied to sources/rocko_laugh_a/b.png

The render is the game's art, so it is not kept in the repository; the icon is.

Needs:  pip install numpy Pillow

    python make_icon.py                 icon.png from the chosen background
    python make_icon.py drafts          every background with both sources in drafts/, and drafts/drafts.png side by side (also at 64 px)
"""
import os, sys
import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SIZE = 1024   # drawn at 4x, saved at 256
CHOSEN = "fire"


def grid():
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    return x, y


def to_image(rgb):
    return Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")


def background(kind):
    x, y = grid()
    if kind == "fire":
        # night, lit by a campfire below him: a warm glow rising behind his shoulders into a dark blue-violet sky
        glow = np.exp(-(((x - 0.5) / 0.55) ** 2 + ((y - 1.0) / 0.55) ** 2))
        sky = np.stack([0.05 + 0.04 * (1 - y), 0.05 + 0.03 * (1 - y), 0.12 + 0.10 * (1 - y)], -1)
        warm = np.stack([1.0, 0.45, 0.12], -1)
        return sky * (1 - glow[..., None]) + warm * glow[..., None] * 0.95
    if kind == "dusk":
        # a dusk sky over the meadows: amber at the horizon to purple above
        t = y[..., None]
        top, mid, low = np.array([0.18, 0.10, 0.30]), np.array([0.62, 0.30, 0.38]), np.array([0.98, 0.66, 0.30])
        return np.where(t < 0.6, top + (mid - top) * (t / 0.6), mid + (low - mid) * ((t - 0.6) / 0.4))
    if kind == "grave":
        # the fire background with a grave marker's silhouette over his left shoulder
        return background("fire")
    raise ValueError(kind)


def vignette(img, strength=0.55):
    x, y = grid()
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2) / 0.71
    a = np.asarray(img).astype(np.float32) / 255 * (1 - strength * d[..., None] ** 2)
    return to_image(a)


SOURCE = "rocko_laugh_b.png"


def bust(source=None):
    """The render, cropped to head and shoulders and scaled to fill the icon."""
    im = Image.open(os.path.join(HERE, "sources", source or SOURCE)).convert("RGBA")
    box = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    l, t, r, b = box
    w = r - l
    # head and the top of the shoulders, centred on the head: the face must read at 64 px
    head = im.crop((l, t, r, t + int(w * 0.35))).getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    cx = l + (head[0] + head[2]) // 2 if head else (l + r) // 2
    half = int(w * 0.36)
    crop = im.crop((cx - half, t - int(w * 0.02), cx + half, t - int(w * 0.02) + 2 * half))
    scale = SIZE * 1.0 / crop.width
    crop = crop.resize((int(crop.width * scale), int(crop.height * scale)), Image.LANCZOS)
    return crop


def full_body(source):
    """A whole-body render, cropped to him and scaled so he stands most of the icon's height."""
    im = Image.open(os.path.join(HERE, "sources", source)).convert("RGBA")
    l, t, r, b = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    fig = im.crop((l, t, r, b))
    scale = SIZE * 0.9 / max(fig.height, fig.width * 0.9)
    return fig.resize((int(fig.width * scale), int(fig.height * scale)), Image.LANCZOS)


def compose(kind, source=None):
    bg = background(kind)
    img = to_image(bg)
    if kind == "grave":
        d = ImageDraw.Draw(img)
        d.rounded_rectangle((30, 520, 230, 1000), radius=100, fill=(14, 12, 22))
        d.rectangle((30, 620, 230, 1024), fill=(14, 12, 22))
    img = vignette(img).convert("RGBA")
    full = (source or SOURCE).startswith("rocko_full")
    fig = full_body(source) if full else bust(source)
    # a warm rim from the fire behind: the figure's outline, blurred, tinted, under the figure
    alpha = fig.getchannel("A")
    rim = Image.new("RGBA", fig.size, (255, 150, 60, 0))
    rim.putalpha(alpha.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(14)).point(lambda a: int(a * 0.85)))
    ox, oy = (SIZE - fig.width) // 2, (SIZE - fig.height) // 2 + int(SIZE * 0.03) if full else SIZE - fig.height + int(SIZE * 0.05)
    img.alpha_composite(rim, (ox, oy))
    img.alpha_composite(fig, (ox, oy))
    return img.convert("RGB")


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "drafts":
        out = os.path.join(HERE, "drafts")
        os.makedirs(out, exist_ok=True)
        kinds = [("fire", "rocko_laugh_b.png"), ("fire", "rocko_full_3.png"), ("fire", "rocko_full_5.png"), ("fire", "rocko_full_7.png"),
                 ("dusk", "rocko_full_3.png"), ("dusk", "rocko_full_5.png")]
        sheet = Image.new("RGB", (3 * 276 + 20, 2 * (276 + 84)), (30, 30, 34))
        for i, (k, src) in enumerate(kinds):
            icon = compose(k, src).resize((256, 256), Image.LANCZOS)
            name = f"{k}_{src[6:-4]}"
            icon.save(os.path.join(out, f"icon_{name}.png"))
            x, y = 20 + (i % 3) * 276, (i // 3) * 360
            sheet.paste(icon, (x, y + 10))
            sheet.paste(icon.resize((64, 64), Image.LANCZOS), (x, y + 276))
            ImageDraw.Draw(sheet).text((x + 80, y + 300), name, fill=(230, 230, 230))
        sheet.save(os.path.join(out, "drafts.png"))
        print(os.path.join(out, "drafts.png"))
        return
    compose(CHOSEN).resize((256, 256), Image.LANCZOS).save(os.path.join(HERE, "icon.png"))
    print(os.path.join(HERE, "icon.png"))


if __name__ == "__main__":
    main()
