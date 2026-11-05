"""
Builds thunderstore/icon.png (256 x 256): the wardrobe with its doors open and the portal swirling inside, on a painted background.

The sources are the mod's own build-icon pose drawn at 1024 px on a transparent background (a temporary change to the mod, not kept:
Icon.cs's Size at 512, and the posed pictures written out with ImageConversion.EncodeToPNG):
    sources/plain.png   the wardrobe, doors open, nothing inside
    sources/mask.png    the same with the opening filled flat magenta: where the swirl goes
They are the game's art, so they are not kept in the repository; the icon is.

Needs:  pip install numpy Pillow

    python make_icon.py            icon.png from the chosen draft
    python make_icon.py drafts     every draft in drafts/, and drafts/drafts.png side by side (also at 64 px)
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SIZE = 1024   # drawn at 4x, saved at 256

# name: (background, how much of the picture the wardrobe fills)
DRAFTS = {
    "night_fire": ("night", 0.92),
    "night_fire_close": ("night", 1.02),
    "hearth": ("hearth", 0.92),
    "frost": ("frost", 0.92),
}
CHOSEN = "night_fire"


def grid(n=SIZE):
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    return x, y


def to_image(rgb):
    return Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")


def background(kind):
    x, y = grid()
    glow = np.exp(-(((x - 0.5) / 0.45) ** 2 + ((y - 0.52) / 0.5) ** 2))[..., None]
    if kind == "night":
        # a dark room, the portal's fire glowing out of the wardrobe
        dark = np.stack([0.05 + 0.02 * y, 0.04 + 0.02 * y, 0.08 + 0.03 * y], -1)
        return dark * (1 - glow) + np.array([0.85, 0.38, 0.12]) * glow * 0.75
    if kind == "hearth":
        dark = np.stack([0.10 + 0.03 * y, 0.06 + 0.02 * y, 0.05 + 0.02 * y], -1)
        return dark * (1 - glow) + np.array([1.0, 0.62, 0.25]) * glow * 0.8
    if kind == "frost":
        # the Deep North the wardrobe comes from: cold blue round the warm portal
        dark = np.stack([0.03 + 0.02 * y, 0.06 + 0.03 * y, 0.12 + 0.05 * y], -1)
        return dark * (1 - glow) + np.array([0.45, 0.70, 1.0]) * glow * 0.7
    raise ValueError(kind)


def vignette(img, strength=0.55):
    x, y = grid()
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2) / 0.71
    return to_image(np.asarray(img).astype(np.float32) / 255 * (1 - strength * d[..., None] ** 2))


def opening(mask):
    """Where the swirl goes: the magenta pixels of the mask picture (0..1)."""
    m = np.asarray(mask.convert("RGBA")).astype(np.float32) / 255
    r, g, b, a = m[..., 0], m[..., 1], m[..., 2], m[..., 3]
    return np.clip((np.minimum(r, b) - g - 0.25) * 3, 0, 1) * a


def swirl(shape, cx, cy, rx, ry):
    """The portal's fire: arms of flame turning in to a dark heart, brightest at the rim, with sparks."""
    h, w = shape
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    u, v = (x - cx) / rx, (y - cy) / ry
    r = np.sqrt(u * u + v * v)
    ang = np.arctan2(v, u)
    arms = 0.5 + 0.5 * np.sin(ang * 4 + r * 11)
    fine = 0.5 + 0.5 * np.sin(ang * 9 - r * 23)
    heat = np.clip(0.55 * arms + 0.25 * fine + 0.45 * np.clip(r, 0, 1.2), 0, 1.4)
    deep, mid, hot = np.array([0.45, 0.04, 0.02]), np.array([0.95, 0.35, 0.05]), np.array([1.0, 0.85, 0.45])
    t = heat[..., None]
    col = np.where(t < 0.7, deep + (mid - deep) * (t / 0.7), mid + (hot - mid) * np.clip((t - 0.7) / 0.5, 0, 1))
    core = np.clip((r - 0.08) / 0.45, 0, 1) ** 1.4          # the dark middle it pulls you into
    col = col * core[..., None]
    rng = np.random.default_rng(3)
    sparks = np.zeros((h, w), np.float32)
    for _ in range(90):
        sx, sy = rng.uniform(cx - rx, cx + rx), rng.uniform(cy - ry, cy + ry)
        sparks += np.exp(-(((x - sx) ** 2 + (y - sy) ** 2) / rng.uniform(4, 18)))
    return np.clip(col + np.clip(sparks, 0, 1)[..., None] * np.array([1.0, 0.8, 0.4]), 0, 1)


def compose(name):
    kind, fill = DRAFTS[name]
    plain = Image.open(os.path.join(HERE, "sources", "plain.png")).convert("RGBA")
    mask = Image.open(os.path.join(HERE, "sources", "mask.png")).convert("RGBA")

    # the swirl painted into the opening, over the plain wardrobe
    hole = opening(mask)
    ys, xs = np.nonzero(hole > 0.5)
    cx, cy, rx, ry = xs.mean(), ys.mean(), (xs.max() - xs.min()) / 2, (ys.max() - ys.min()) / 2
    fire = swirl(hole.shape, cx, cy, rx, ry)
    base = np.asarray(plain).astype(np.float32) / 255
    rgb = base[..., :3] * (1 - hole[..., None]) + fire * hole[..., None]
    alpha = np.maximum(base[..., 3], hole)
    fig = Image.fromarray((np.dstack([rgb, alpha]) * 255).astype(np.uint8), "RGBA")

    # firelight spilling out of the opening onto the wardrobe's own edges
    spill = Image.fromarray((hole * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(60))
    s = np.asarray(spill).astype(np.float32)[..., None] / 255 * 0.55
    f = np.asarray(fig).astype(np.float32) / 255
    f[..., :3] = np.clip(f[..., :3] + s * np.array([1.0, 0.45, 0.12]) * f[..., 3:4], 0, 1)
    fig = Image.fromarray((f * 255).astype(np.uint8), "RGBA")

    # fit it to the square, standing a little low (its top reads first)
    box = fig.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    fig = fig.crop(box)
    scale = SIZE * fill / max(fig.size)
    fig = fig.resize((int(fig.width * scale), int(fig.height * scale)), Image.LANCZOS)
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    layer.paste(fig, ((SIZE - fig.width) // 2, (SIZE - fig.height) // 2 + int(SIZE * 0.02)))

    img = vignette(to_image(background(kind))).convert("RGBA")
    # a warm rim from the glow behind, so the outline reads at 64 px
    tint = {"night": (255, 150, 70), "hearth": (255, 190, 110), "frost": (150, 205, 255)}[kind]
    rim = Image.new("RGBA", layer.size, tint + (0,))
    rim.putalpha(layer.getchannel("A").filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(14)).point(lambda a: int(a * 0.8)))
    img.alpha_composite(rim)
    img.alpha_composite(layer)
    return img.convert("RGB")


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "drafts":
        out = os.path.join(HERE, "drafts")
        os.makedirs(out, exist_ok=True)
        names = list(DRAFTS)
        sheet = Image.new("RGB", (len(names) * 276 + 20, 276 + 84), (30, 30, 34))
        for i, name in enumerate(names):
            icon = compose(name).resize((256, 256), Image.LANCZOS)
            icon.save(os.path.join(out, name + ".png"))
            x = 20 + i * 276
            sheet.paste(icon, (x, 20))
            sheet.paste(icon.resize((64, 64), Image.LANCZOS), (x, 286))
        sheet.save(os.path.join(out, "drafts.png"))
        print(os.path.join(out, "drafts.png"))
        return
    out = os.path.join(HERE, "icon.png")
    compose(CHOSEN).resize((256, 256), Image.LANCZOS).save(out)
    print(out)


if __name__ == "__main__":
    main()
