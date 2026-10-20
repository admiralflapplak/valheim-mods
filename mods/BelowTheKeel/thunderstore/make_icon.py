"""Below the Keel's package icon (256 x 256): `python make_icon.py` writes icon.png, the burning eye under the water (icon_eye_glow,
after Reference/pic2.png), drawn entirely here. `python make_icon.py <name> ...` builds the drafts tried on the way (icon_<name>.png and
drafts.png side by side), the first of them:
  eye      - a vast slit-pupil eye looking up out of black water, a tiny longship crossing over it (drawn, like the omen's own texture)
  below    - from under the water: the hull dark against the light, something long coiling in the deep below it
  halfboat - the sea draugr rowing his half-boat, in fog (a shot from the game, graded)
  mist     - the half-boat centred on grey water from above, half lost in mist laid over it here
  pov      - from the eyes, gripping the gunwale in a storm, the Pacer rearing out of the swell (after Reference/pic.png)
  rope     - one of Ran's drowned hanging on a rope into the dark (a shot from the game, graded)
Sources in sources/ (Claude Tools):
  render VikingShip yaw=0 pitch=90 size=1600x1600 bg=clear   -> ship_top.png
  render VikingShip yaw=0 pitch=-90 size=1600x1600 bg=clear  -> ship_bottom.png
  keel play halfboat near, then a "view world" camera side-on at night  -> halfboat.png
  keel pose, then a "view world" camera low against the night sky       -> rope.png
  env Misty, keel play halfboat near -40, a "view world" camera 6 m up looking down at it (water only) -> halfboat_mist.png
Run: python make_icon.py [which]   (writes icon_<which>.png and drafts.png; "eye" etc. for one, nothing for all)"""
import math, os, random, sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageEnhance

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "sources")
S = 1024   # drawn at 4x, saved at 256


def src(name):
    return Image.open(os.path.join(SRC, name)).convert("RGBA")


def crop_alpha(im):
    return im.crop(im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())


def radial(size, inner, outer, centre=(0.5, 0.5), reach=0.75):
    """A round gradient, inner colour at the centre to outer at reach (fraction of the size)."""
    w, h = size
    cx, cy = centre[0] * w, centre[1] * h
    img = Image.new("RGB", size)
    px = img.load()
    for y in range(h):
        for x in range(w):
            t = min(1.0, math.hypot(x - cx, y - cy) / (reach * w))
            t = t * t * (3 - 2 * t)
            px[x, y] = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(3))
    return img.convert("RGBA")


def vignette(img, strength=0.75):
    w, h = img.size
    mask = radial((w // 4, h // 4), (255, 255, 255), (int(255 * (1 - strength)),) * 3, reach=0.72).convert("L").resize((w, h))
    black = Image.new("RGBA", (w, h), (0, 0, 0, 255))
    return Image.composite(img, black, mask)


def glow(layer, radius, gain=1.0):
    """The layer blurred and added on top of itself: light that bleeds."""
    blur = layer.filter(ImageFilter.GaussianBlur(radius))
    if gain != 1.0:
        blur = ImageEnhance.Brightness(blur).enhance(gain)
    return ImageChops.add(layer, blur)


def silhouette(im, colour=(4, 6, 10)):
    a = im.getchannel("A")
    out = Image.new("RGBA", im.size, colour + (255,))
    out.putalpha(a)
    return out


def caustics(size, seed, alpha=26):
    """Faint wavering light on the water: thin bright loops, blurred."""
    rnd = random.Random(seed)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for _ in range(140):
        x, y = rnd.uniform(0, size[0]), rnd.uniform(0, size[1])
        r = rnd.uniform(20, 70)
        d.arc((x - r, y - r * 0.6, x + r, y + r * 0.6), rnd.uniform(0, 360), rnd.uniform(0, 360) + 120,
              fill=(140, 200, 210, alpha), width=3)
    return layer.filter(ImageFilter.GaussianBlur(4))


# ---------------------------------------------------------------------------------------------------------------------------- eye

def draw_eye(size, open_frac=0.62, ring_r=0.45, white=(40, 26, 20), vein=(80, 10, 8)):
    """The omen's eye seen from above: the same pattern as EyeMeshes.Draw (streaked amber iris, slit pupil, dark ring, bloodshot dark
    white), inside an almond of lids."""
    w = h = size
    eye = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    glowl = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    pe, pg = eye.load(), glowl.load()
    iris_r = 0.40
    for y in range(h):
        for x in range(w):
            u, v = x / (w - 1) * 2 - 1, y / (h - 1) * 2 - 1
            r = math.hypot(u, v)
            if r > 1:
                continue
            ang = math.atan2(v, u)
            slit_len, slit_w = 0.36, 0.055
            pupil = abs(v) < slit_len and abs(u) < slit_w * (1 - (v / slit_len) ** 2)
            if pupil:
                c, g = (0, 0, 0), (0, 0, 0)
            elif r < iris_r:
                streak = 0.6 + 0.25 * math.sin(ang * 37) + 0.15 * math.sin(ang * 11 + r * 20)
                toward = 1.2 - 0.6 * (r / iris_r)
                g = tuple(min(255, int(c0 * streak * toward)) for c0 in (255, 158, 30))
                c = g
            elif r < ring_r:
                c, g = (8, 5, 2), (0, 0, 0)
            else:
                veins = abs(math.sin(ang * 9 + math.sin(r * 14) * 1.5)) ** 30
                t = veins * min(1, (r - ring_r) * 3)
                c = tuple(int(a + (b - a) * t) for a, b in zip(white, vein))
                g = (0, 0, 0)
            pe[x, y] = c + (255,)
            pg[x, y] = g + (255,)
    # The almond: two arcs meeting at the corners, opened to open_frac of the eye's height.
    mask = Image.new("L", (w * 2, h * 2), 0)
    md = ImageDraw.Draw(mask)
    half = open_frac * h            # the opening's half-height, doubled canvas
    k = (w ** 2 / 4) / half         # circle through the corners (0, h) and (2w, h) with its top at h - half
    R = (k + half) / 2 + (w * w) / (2 * (k + half)) if False else None
    # (simpler: an ellipse-like almond from two big circles)
    rad = ((w) ** 2 + half ** 2) / (2 * half)
    md.ellipse((w - rad, h - half, w + rad, h - half + 2 * rad), fill=255)
    top = mask.copy()
    md2 = ImageDraw.Draw(mask2 := Image.new("L", (w * 2, h * 2), 0))
    md2.ellipse((w - rad, h + half - 2 * rad, w + rad, h + half), fill=255)
    almond = ImageChops.multiply(top, mask2).resize((w, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(2))
    eye.putalpha(almond)
    glowl.putalpha(almond)
    return eye, glowl, almond


def icon_eye():
    bg = radial((S, S), (22, 44, 52), (2, 4, 8), centre=(0.5, 0.58), reach=0.8)
    bg.alpha_composite(caustics((S, S), 3, alpha=18))
    size = int(S * 1.05)
    eye, gl, almond = draw_eye(size)
    off = ((S - size) // 2, int(S * 0.08))
    # A soft amber light in the water round the eye, then the eye, its iris glowing.
    halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    halo.alpha_composite(gl, off)
    halo = halo.filter(ImageFilter.GaussianBlur(70))
    halo = ImageEnhance.Brightness(halo).enhance(1.6)
    bg = ImageChops.add(bg, halo)
    bg.alpha_composite(eye, off)
    lit = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    lit.alpha_composite(gl, off)
    bg = ImageChops.add(bg, lit.filter(ImageFilter.GaussianBlur(10)))
    # The lids' rims: a dark band round the almond, a faint wet highlight on the upper one.
    rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    a_big = Image.new("L", (S, S), 0)
    a_big.paste(almond, off)
    edge = ImageChops.subtract(a_big.filter(ImageFilter.MaxFilter(31)), a_big)
    rim.paste((6, 8, 10, 255), (0, 0), edge.filter(ImageFilter.GaussianBlur(6)))
    bg.alpha_composite(rim)
    # Water over it all: murk, so it sits below the surface.
    murk = Image.new("RGBA", (S, S), (10, 30, 38, 70))
    bg.alpha_composite(murk)
    bg.alpha_composite(caustics((S, S), 9, alpha=22))
    # The ship: tiny, dark, crossing above the eye's upper lid.
    ship = crop_alpha(src("ship_top.png")).rotate(-62, expand=True, resample=Image.BICUBIC)
    ship = crop_alpha(ship)
    sh = S * 0.30 / max(ship.size)
    ship = ship.resize((int(ship.width * sh), int(ship.height * sh)), Image.LANCZOS)
    shadow = silhouette(ship, (0, 0, 0)).filter(ImageFilter.GaussianBlur(8))
    shadow.putalpha(shadow.getchannel("A").point(lambda a: a * 0.6))
    pos = (int(S * 0.56), int(S * 0.05))
    bg.alpha_composite(shadow, (pos[0] + 14, pos[1] + 18))
    bg.alpha_composite(silhouette(ship, (14, 12, 10)), pos)
    return vignette(bg, 0.8)


# -------------------------------------------------------------------------------------------------------------------------- below

def icon_below():
    # Under the water looking up: light at the surface, black below.
    bg = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(bg)
    for y in range(S):
        t = (y / S) ** 0.7
        c = tuple(int(a + (b - a) * t) for a, b in zip((120, 190, 190), (1, 3, 6)))
        d.line((0, y, S, y), fill=c + (255,))
    # Rays down from the surface, round the hull.
    rays = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rays)
    rnd = random.Random(5)
    for _ in range(14):
        x = rnd.uniform(-S * 0.2, S * 1.2)
        wdt = rnd.uniform(20, 70)
        rd.polygon([(x, 0), (x + wdt, 0), (x + wdt * 3 + S * 0.25, S), (x + S * 0.2, S)], fill=(170, 230, 220, 40))
    bg.alpha_composite(rays.filter(ImageFilter.GaussianBlur(18)))
    # The hull, from below, dark against the light.
    ship = crop_alpha(src("ship_bottom.png")).rotate(90, expand=True, resample=Image.BICUBIC)
    ship = crop_alpha(ship)
    sh = S * 0.62 / ship.width
    ship = ship.resize((int(ship.width * sh), int(ship.height * sh)), Image.LANCZOS)
    bg.alpha_composite(silhouette(ship, (6, 12, 16)), ((S - ship.width) // 2, int(S * 0.07)))
    # Something long below: a thick dark S, its fins faintly aglow, fading into the black.
    body = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    pts = []
    for i in range(200):
        t = i / 199
        x = -S * 0.15 + t * S * 1.3
        y = S * 0.66 + math.sin(t * math.pi * 2.2 + 0.4) * S * 0.12
        pts.append((x, y))
    for i, (x, y) in enumerate(pts):
        r = S * 0.11 * (0.85 + 0.15 * math.sin(i * 0.3))
        bd.ellipse((x - r, y - r * 0.8, x + r, y + r * 0.8), fill=(3, 6, 8, 255))
    bg.alpha_composite(body.filter(ImageFilter.GaussianBlur(3)))
    fins = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fins)
    for i in range(10, 190, 9):
        x, y = pts[i]
        fd.ellipse((x - 9, y - S * 0.085 - 9, x + 9, y - S * 0.085 + 9), fill=(60, 230, 200, 255))
    bg = ImageChops.add(bg, glow(fins, 14, 1.4).filter(ImageFilter.GaussianBlur(2)))
    # The deep: darker toward the bottom.
    deep = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deep)
    for y in range(S):
        a = int(max(0, (y / S - 0.55) / 0.45) ** 1.5 * 200)
        dd.line((0, y, S, y), fill=(0, 2, 4, a))
    bg.alpha_composite(deep)
    return vignette(bg, 0.55)


# ----------------------------------------------------------------------------------------------------------------------- halfboat

def grade(img, sat=0.45, bright=0.7, tint=(40, 60, 70), tint_amount=0.25):
    img = ImageEnhance.Color(img).enhance(sat)
    img = ImageEnhance.Brightness(img).enhance(bright)
    over = Image.new("RGBA", img.size, tint + (255,))
    return Image.blend(img, over, tint_amount)


def eyes(img, at, colour, radius, gain=2.2):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for dx in (-radius * 1.6, radius * 1.6):
        d.ellipse((at[0] + dx - radius, at[1] - radius * 0.6, at[0] + dx + radius, at[1] + radius * 0.6), fill=colour + (255,))
    return ImageChops.add(img, glow(layer, radius * 2.5, gain))


def icon_halfboat():
    shot = src("halfboat.png")
    box = (260, 520, 260 + 1180, 520 + 1180)   # the boat and rower, square
    img = shot.crop(box).resize((S, S), Image.LANCZOS)
    img = grade(img, sat=0.35, bright=0.62, tint=(70, 85, 90), tint_amount=0.3)
    # Fog: pale banks drifting across, thickest low on the water.
    fog = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fog)
    rnd = random.Random(11)
    for _ in range(40):
        x, y = rnd.uniform(-200, S + 200), rnd.uniform(S * 0.25, S * 0.9)
        w, h = rnd.uniform(200, 520), rnd.uniform(40, 110)
        fd.ellipse((x - w, y - h, x + w, y + h), fill=(170, 185, 190, rnd.randint(20, 55)))
    img.alpha_composite(fog.filter(ImageFilter.GaussianBlur(40)))
    # His eyes, green, in the dark of the hood (where the game's glow is in the shot).
    ex, ey = (884 - box[0]) * S / 1180, (822 - box[1]) * S / 1180
    img = eyes(img, (ex - 6, ey + 2), (90, 255, 170), 7, 2.6)
    return vignette(img, 0.7)


# --------------------------------------------------------------------------------------------------------------------------- mist

def fog_banks(size, seed, count, y_from, y_to, colour, alpha, blur):
    rnd = random.Random(seed)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for _ in range(count):
        x, y = rnd.uniform(-0.2, 1.2) * size[0], rnd.uniform(y_from, y_to) * size[1]
        w, h = rnd.uniform(0.15, 0.45) * size[0], rnd.uniform(0.03, 0.09) * size[1]
        d.ellipse((x - w, y - h, x + w, y + h), fill=colour + (rnd.randint(alpha // 2, alpha),))
    return layer.filter(ImageFilter.GaussianBlur(blur))


def icon_mist():
    return mist_variant(dark=False)


def icon_mistdark():
    return mist_variant(dark=True)


def mist_variant(dark):
    """The half-boat centred on grey water, half lost in fog: the shot from above (no land, no ship), with the mist laid over it here,
    thickest toward the top and drifting across the boat's ends."""
    shot = src("halfboat_mist.png")
    box = (9, 78, 9 + 1150, 78 + 1150)          # the boat (185-983 x 413-863 in the shot) centred, about two thirds of the width
    img = shot.crop(box).resize((S, S), Image.LANCZOS)
    img = grade(img, sat=0.4, bright=0.62 if dark else 0.82, tint=(150, 165, 172), tint_amount=0.12)
    if dark:   # dusk: the boat darker against it, so it holds when small
        img = ImageEnhance.Contrast(img).enhance(1.35)
    mist = (128, 140, 146) if dark else (196, 204, 208)
    # A veil over everything, then the far water thick with it.
    img.alpha_composite(Image.new("RGBA", (S, S), mist + (30 if dark else 55,)))
    top = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    td = ImageDraw.Draw(top)
    for y in range(S):
        a = int(max(0.0, 1 - y / (S * 0.42)) ** 1.4 * 235)
        td.line((0, y, S, y), fill=mist + (a,))
    img.alpha_composite(top)
    # Banks drifting across: over the far water, and low ones across the boat's ends and the water before it.
    img.alpha_composite(fog_banks((S, S), 21, 26, 0.05, 0.45, mist, 120, 45))
    img.alpha_composite(fog_banks((S, S), 22, 14, 0.42, 0.72, mist, 70 if dark else 95, 38))
    img.alpha_composite(fog_banks((S, S), 23, 18, 0.7, 1.0, mist, 80, 50))
    # His eyes, faint green in the hood (he faces the sternpost, left).
    hx, hy = (575 - box[0]) * S / 1150, (585 - box[1]) * S / 1150
    img = eyes(img, (hx - 10, hy - 2), (80, 255, 160), 5 if dark else 4, 3.2 if dark else 2.4)
    # The edges sink into deeper grey, so the middle holds the eye.
    edge = radial((S // 4, S // 4), (255, 255, 255), (150, 150, 150), reach=0.75).convert("L").resize((S, S))
    img = Image.composite(img, Image.new("RGBA", (S, S), (24, 28, 32, 255) if dark else (70, 78, 84, 255)), edge)
    return img


# ---------------------------------------------------------------------------------------------------------------------------- pov

def calm_fins(img, keep=0.32):
    """The Pacer's fins glow neon cyan in open air (under the water the murk dims them): pull them down to a cold, dim sheen."""
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            cyan = min(g, b) - r
            if cyan > 60:
                t = min(1.0, (cyan - 60) / 120)
                grey = (r + g + b) // 3
                nr, ng, nb = (int(c * (1 - t) + (c * keep * 0.6 + grey * keep * 0.4) * t) for c in (r, g, b))
                px[x, y] = (nr, ng, nb, a)
    return img


def rain(size, seed, count=500, alpha=40):
    rnd = random.Random(seed)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for _ in range(count):
        x, y = rnd.uniform(-100, size[0]), rnd.uniform(-100, size[1])
        ln = rnd.uniform(40, 120)
        d.line((x, y, x + ln * 0.25, y + ln), fill=(210, 220, 225, rnd.randint(alpha // 3, alpha)), width=2)
    return layer.filter(ImageFilter.GaussianBlur(0.8))


def pov(source):
    """From the eyes of someone gripping the gunwale in a storm, the Pacer rearing out of the swell in front of the ship (after the
    reference in ../Reference). The shot: keel hands 0.25 0.2, keel play pacer +/-15 1.8, env ThunderStorm, a view camera at the eyes."""
    img = src(source).resize((S, S), Image.LANCZOS)
    img = calm_fins(img, keep=0.6)
    img = ImageEnhance.Color(img).enhance(0.7)
    img = ImageEnhance.Contrast(img).enhance(1.25)
    img = ImageEnhance.Brightness(img).enhance(1.05)
    img = Image.blend(img, Image.new("RGBA", (S, S), (30, 45, 55, 255)), 0.08)
    img.alpha_composite(fog_banks((S, S), 31, 10, 0.0, 0.3, (150, 160, 168), 45, 40))
    img.alpha_composite(rain((S, S), 7, alpha=30))
    return vignette(img, 0.45)


def icon_pov():
    return pov("pov_rail_b.png")


def icon_pov2():
    return pov("pov_rail_a.png")


# -------------------------------------------------------------------------------------------------------------------------- karve

def deep_sea(size, seed=7):
    """Heave Ho's painted sea (HeaveHo/thunderstore/make_icon.py), gone cold: a dim teal pool of light round the boat falling off to
    black-blue at the edges, long ripples catching what light there is. Returns the water and its ripple layer apart (the ripples go
    back on over whatever lies beneath)."""
    import numpy as np
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.42) ** 2)
    t = np.clip(1 - d / 0.75, 0, 1) ** 1.1
    base = np.stack([0.02 + 0.07 * t, 0.07 + 0.38 * t, 0.11 + 0.42 * t], axis=-1)
    rng = np.random.default_rng(seed)
    noise = rng.random((size // 8, size // 8)).astype(np.float32)
    noise = np.asarray(Image.fromarray((noise * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
                       .filter(ImageFilter.GaussianBlur(8)), dtype=np.float32) / 255
    stripes = 0.5 + 0.5 * np.sin((y * 110 + x * 18) + noise * 10)
    ripple = np.clip((stripes - 0.45) * 1.8, 0, 1) * np.clip((noise - 0.35) * 2.2, 0, 1)
    ripples = ripple[..., None] * np.array([0.05, 0.16, 0.19]) * (0.35 + 0.65 * t[..., None])
    return base, ripples


def coil(size, centre, scale):
    """The silhouette: a thick, tapering S of something long under the water, much bigger than the boat. A soft mask, 0 to 1."""
    import numpy as np
    layer = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(layer)
    cx, cy = centre
    n = 260
    for i in range(n):
        t = i / (n - 1)
        # Comes up from the deep at the lower left, curves under the hull, and goes back down at the upper right.
        x = cx + (t - 0.5) * size * 1.25 * scale
        y = cy + math.sin(t * math.pi * 1.6 - 0.9) * size * 0.2 * scale + (0.5 - t) * size * 0.22 * scale
        r = size * 0.15 * scale * (0.35 + 0.65 * math.sin(math.pi * min(1.0, t * 1.15)))
        d.ellipse((x - r, y - r * 0.85, x + r, y + r * 0.85), fill=255)
    layer = layer.filter(ImageFilter.GaussianBlur(size / 45))
    return np.asarray(layer).astype(np.float32) / 255


def slit_eye(w, h):
    """An eye seen from above, the lids almost shut: a long narrow almond (two arcs meeting at sharp corners), the amber iris cut off by
    the lids above and below, a thin upright slit, dark edges where the lids close over it."""
    import numpy as np
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    u = (x - w / 2) / (w / 2)            # -1..1 along the eye
    v = (y - h / 2) / (h / 2)            # -1..1 across it
    lid = 1 - u ** 2                      # the almond: how far each lid has opened at this point along the eye
    inside = np.clip((lid - np.abs(v)) * 6, 0, 1)
    r = np.sqrt((u * w / h) ** 2 + v ** 2) * (h / w) * 2.2   # distance from the middle, in iris radii
    ang = np.arctan2(v, u * w / h)
    streak = 0.65 + 0.25 * np.sin(ang * 31) + 0.1 * np.sin(ang * 9 + r * 12)
    iris = np.clip(1.15 - r * 0.7, 0, 1) * streak * (r < 1.05)
    ring = (r >= 1.05) & (r < 1.18)
    pupil = np.abs(u * w / h) < 0.07 * (1 - np.clip(np.abs(v), 0, 1) ** 2) * 1.0
    col = np.stack([0.95 * iris, 0.58 * iris, 0.12 * iris], axis=-1)
    col[ring] = (0.04, 0.02, 0.01)
    col[(r >= 1.18)] = (0.10, 0.05, 0.04)
    col[pupil] = 0
    shade = np.clip(1 - (np.abs(v) / np.maximum(lid, 1e-3)) ** 3, 0.25, 1)   # darker toward the lids
    col = col * shade[..., None]
    rgba = np.dstack([col, inside]).clip(0, 1)
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").filter(ImageFilter.GaussianBlur(1.2))


def icon_karve_iris(eye_scale=1.12, under=0.5, open_frac=0.55):
    """The whole sea is the eye: a vast one, its edges off the picture, looking up from just under the surface, and the karve sailing
    across it, its hull over the slit. The eye is the omen's own (draw_eye), sunk in the water: tinted toward the deep, softened, the
    surface's ripples laid over it. under: how much the water veils it (0 none)."""
    import numpy as np
    size = S
    base, ripples = deep_sea(size, seed=11)
    es = int(size * eye_scale)
    # A thin dark ring round the iris, and a pale, bloodshot white (the water's teal veils it).
    e, g, almond = draw_eye(es, open_frac=open_frac, ring_r=0.415, white=(176, 166, 138), vein=(150, 52, 40))
    off = ((size - es) // 2, (size - es) // 2 + int(size * 0.04))
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    layer.alpha_composite(e, off)
    lit = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    lit.alpha_composite(g, off)
    layer = layer.filter(ImageFilter.GaussianBlur(size / 400))
    E = np.asarray(layer).astype(np.float32) / 255
    L = np.asarray(lit.filter(ImageFilter.GaussianBlur(size / 300))).astype(np.float32)[..., :3] / 255
    a = E[..., 3:4]
    water = base * (1 - a) + E[..., :3] * a
    water = water + L * 0.35                                         # the iris glows a little through the water
    deep = base * 0.8 + np.array([0.0, 0.05, 0.07])                 # the sea over it: its own colour, a little greener
    water = water * (1 - under) + deep * under
    halo = np.asarray(lit.filter(ImageFilter.GaussianBlur(size / 25))).astype(np.float32)[..., :3] / 255
    water = water + halo * 0.2 + ripples * 2.2                       # the surface's ripples, plain over the eye
    img = Image.fromarray((np.clip(water, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
    img = place_karve(img, size, 0.42, (0.57, 0.47))                 # a little off the middle: sailing across the slit
    return vignette(img, 0.72)


def glow_eye(w, h, seed=3, block=None):
    """The eye after Reference/pic2.png: a burning lantern, no whites. Its opening a tall, rounded almond with a thin dark rim; inside,
    hot yellow at the middle through orange to deep red at the edge, blotchy (blocky, like the game's own pixelated textures); a tall,
    thin, ragged slit. Returns the eye (RGBA) and its light (RGB, for the glow it throws)."""
    import numpy as np
    rng = np.random.default_rng(seed)
    block = block or max(4, w // 48)
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    u = (x - w / 2) / (w / 2)
    v = (y - h / 2) / (h / 2)
    # the opening: |v| below a rounded almond profile, so the corners are sharp but the sides full
    profile = (1 - np.abs(u) ** 1.8) ** 0.55
    inside = np.clip((profile - np.abs(v)) * 14, 0, 1)
    rim = np.clip((profile - np.abs(v)) * 14, 0, 1) - np.clip((profile * 0.93 - np.abs(v)) * 14, 0, 1)
    r = np.sqrt(u ** 2 + (v * 0.85) ** 2)
    # blotches: coarse random blocks, softened a little, warping the heat outward
    gh, gw = h // block + 2, w // block + 2
    n1 = rng.random((gh, gw)).astype(np.float32)
    n1 = np.asarray(Image.fromarray((n1 * 255).astype(np.uint8)).resize((gw * block, gh * block), Image.NEAREST)).astype(np.float32)[:h, :w] / 255
    n2 = rng.random((gh // 3 + 2, gw // 3 + 2)).astype(np.float32)
    n2 = np.asarray(Image.fromarray((n2 * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
    heat = np.clip(1.05 - r * 0.95 + (n2 - 0.5) * 0.55 + (n1 - 0.5) * 0.22, 0, 1)
    stops = [(0.0, (0.45, 0.04, 0.02)), (0.35, (0.85, 0.20, 0.04)), (0.62, (1.0, 0.55, 0.08)), (0.85, (1.0, 0.86, 0.22)), (1.0, (1.0, 0.97, 0.55))]
    col = np.zeros((h, w, 3), np.float32)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        m = (heat >= t0) & (heat <= t1)
        k = ((heat - t0) / (t1 - t0))[m][:, None]
        col[m] = np.array(c0) * (1 - k) + np.array(c1) * k
    # the slit: tall and thin, its edges ragged with the same blocks, a slight lean
    lean = u - v * 0.06
    width = 0.065 * (1 - np.clip(np.abs(v) / 0.92, 0, 1) ** 2) + (n1 - 0.5) * 0.02
    pupil = (np.abs(lean) < np.maximum(width, 0)) & (np.abs(v) < 0.92)
    col[pupil] = (0.02, 0.01, 0.01)
    col = col * (1 - rim[..., None] * 0.9)
    light = col * inside[..., None]
    rgba = np.dstack([col, np.clip(inside + rim, 0, 1)])
    return (Image.fromarray((rgba.clip(0, 1) * 255).astype(np.uint8), "RGBA"),
            Image.fromarray((light.clip(0, 1) * 255).astype(np.uint8), "RGB"))


def hide(size, seed=5):
    """The creature's face round the eye: dark navy scales, barely there (rows of overlapping arcs, a little uneven)."""
    import numpy as np
    rnd = random.Random(seed)
    img = Image.new("RGB", (size, size), (6, 12, 22))
    d = ImageDraw.Draw(img)
    sc = size // 18
    for row in range(-1, size // (sc // 2) + 2):
        for col in range(-1, size // sc + 2):
            x = col * sc + (sc // 2 if row % 2 else 0) + rnd.randint(-3, 3)
            y = row * sc // 2 + rnd.randint(-3, 3)
            shade = rnd.randint(10, 26)
            d.ellipse((x - sc * 0.62, y - sc * 0.55, x + sc * 0.62, y + sc * 0.55), fill=(shade // 2, shade, shade + 14),
                      outline=(2, 5, 10), width=max(2, sc // 14))
    return img.filter(ImageFilter.GaussianBlur(size / 700))


def icon_eye_glow():
    """The burning eye under the water alone, no boat."""
    return icon_karve_glow(boat=False)


def icon_karve_glow(boat=True):
    """The karve crossing a burning eye in the dark (after Reference/pic2.png): the eye fills the picture, set in a dark scaled hide
    under the water; the karve, small, dark against its glow, the light catching its edges."""
    import numpy as np
    size = S
    face = hide(size)
    ew, eh = int(size * 0.84), int(size * 0.74)
    eye, light = glow_eye(ew, eh)
    ox, oy = (size - ew) // 2, int(size * 0.14)
    img = face.convert("RGBA")
    # The glow it throws on the hide round it, then the eye.
    lit = Image.new("RGB", (size, size), (0, 0, 0))
    lit.paste(light, (ox, oy))
    img = ImageChops.add(img, ImageEnhance.Brightness(lit.filter(ImageFilter.GaussianBlur(size / 14))).enhance(0.55).convert("RGBA"))
    img.alpha_composite(eye, (ox, oy))
    img = ImageChops.add(img, ImageEnhance.Brightness(lit.filter(ImageFilter.GaussianBlur(size / 90))).enhance(0.25).convert("RGBA"))
    # Under the water: softened a little, a cold teal veil over it all (the glow comes up through it), then the surface's ripples
    # and the light patterns they throw, plain on top.
    base, ripples = deep_sea(size, seed=13)
    img = img.filter(ImageFilter.GaussianBlur(size / 500))
    W = np.asarray(img).astype(np.float32)[..., :3] / 255
    veil = np.array([0.03, 0.16, 0.20])
    W = W * 0.74 + veil * 0.26
    # Over the glow, ripples show as light and dark bands: the waves bend its light, darker in the troughs, brighter on the crests.
    band = ripples.mean(axis=2)
    band = band / max(band.max(), 1e-6)
    W = W * (0.72 + 0.5 * band[..., None])
    W = W + ripples * 2.6
    caust = np.asarray(caustics((size, size), 17, alpha=34).convert("RGBA")).astype(np.float32) / 255
    W = W + caust[..., :3] * caust[..., 3:4] * 0.9
    rnd = random.Random(21)
    img = Image.fromarray((np.clip(W, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
    d = ImageDraw.Draw(img)
    for _ in range(140):
        x, y, r = rnd.uniform(0, size), rnd.uniform(0, size), rnd.uniform(1, 3.2)
        d.ellipse((x - r, y - r, x + r, y + r), fill=(150, 175, 185, rnd.randint(40, 120)))
    # The karve: dark against the light, its edges catching the glow.
    if boat:
        img = sail_on(img, size, 0.34, (0.7, 0.55))   # beside the slit, as the figure in the reference
    return vignette(img, 0.6)


def sail_on(img, size, width, centre):
    """The karve as part of the picture, not pasted on it: lit from below by the eye (dark, warm light on its underside and edges, the
    sail dimmed), its shadow falling across the glow beneath it, a wake behind it and rings where the hull meets the water, and
    softened like the rest."""
    import numpy as np
    boat = crop_alpha(src("karve_3q.png"))
    sc = size * width / max(boat.size)
    boat = boat.resize((int(boat.width * sc), int(boat.height * sc)), Image.LANCZOS)
    bx, by = int(size * centre[0] - boat.width / 2), int(size * centre[1] - boat.height * 0.62)
    A = Image.new("L", (size, size), 0)
    A.paste(boat.getchannel("A"), (bx, by))

    # Its shadow on the glow below (the light comes up through the water: the shadow lies under it, a little offset and soft).
    sh = A.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.GaussianBlur(size / 90))
    shade = Image.new("RGBA", (size, size), (2, 4, 8, 0))
    shade.putalpha(sh.point(lambda v: int(v * 0.7)))
    img.alpha_composite(shade, (int(size * 0.012), int(size * 0.02)))

    # The wake: a V of pale lines trailing back from the bow, and rings round the hull, all thin and faint.
    wake = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    wd = ImageDraw.Draw(wake)
    hull = np.asarray(boat.getchannel("A")).astype(np.float32) / 255
    ys, xs = np.nonzero(hull[int(boat.height * 0.5):] > 0.5)
    ys = ys + int(boat.height * 0.5)
    bow = (bx + xs[np.argmin(xs)], by + ys[np.argmin(xs)])          # the bow points down and to the left in this render
    stern = (bx + xs[np.argmax(xs - ys * 0.2)], by + ys[np.argmax(xs - ys * 0.2)])
    back = np.array(stern, float) - np.array(bow, float)
    back /= max(1e-6, np.linalg.norm(back))
    side = np.array([-back[1], back[0]])
    for k, spread in enumerate((0.42, 0.55)):
        for sign in (-1, 1):
            pts = []
            for i in range(40):
                t = i / 39
                along = t * size * 0.42
                p = np.array(bow, float) + back * along + side * sign * along * spread * (0.6 + 0.4 * t)
                pts.append(tuple(p))
            for i in range(len(pts) - 1):
                a = int(150 * (1 - i / len(pts)) * (0.8 - 0.3 * k))
                wd.line((pts[i], pts[i + 1]), fill=(200, 225, 230, a), width=max(2, size // 300))
    ring = A.filter(ImageFilter.MaxFilter(17))
    ring = ImageChops.subtract(ring, A.filter(ImageFilter.MaxFilter(9)))
    rl = Image.new("RGBA", (size, size), (210, 230, 235, 0))
    rl.putalpha(ring.point(lambda v: int(v * 0.5)).filter(ImageFilter.GaussianBlur(2)))
    wake.alpha_composite(rl)
    img.alpha_composite(wake.filter(ImageFilter.GaussianBlur(size / 700)))

    # The boat lit from below: dark, warm light coming up its lower side and edges, the sail dimmed toward the light's colour.
    b = np.asarray(boat).astype(np.float32) / 255
    a = b[..., 3]
    rows = np.linspace(0, 1, boat.height)[:, None]
    lum = b[..., :3].mean(axis=2)
    dark = b[..., :3] * 0.32 + np.array([0.02, 0.02, 0.03])
    warm = np.array([1.0, 0.5, 0.15])
    under = np.clip((rows - 0.35) / 0.65, 0, 1)                      # more of the eye's light toward the bottom (the hull)
    col = dark + warm * (0.10 + 0.25 * under)[..., None] * (0.3 + 0.7 * lum)[..., None]
    edge = np.asarray(boat.getchannel("A").filter(ImageFilter.MinFilter(5))).astype(np.float32) / 255
    rim = np.clip(a - edge, 0, 1)
    col = col + warm * rim[..., None] * 0.55
    out = np.dstack([np.clip(col, 0, 1), a])
    lit = Image.fromarray((out * 255).astype(np.uint8), "RGBA").filter(ImageFilter.GaussianBlur(size / 1400))
    img.alpha_composite(lit, (bx, by))
    return img


def place_karve(img, size, width, centre, backlit=False):
    """The karve render on the picture, its middle at centre (fractions), width of the picture's: shadow on the water, the hull's
    lowest edge sunk in it."""
    import numpy as np
    boat = crop_alpha(src("karve_3q.png"))
    sc = size * width / max(boat.size)
    boat = boat.resize((int(boat.width * sc), int(boat.height * sc)), Image.LANCZOS)
    bx, by = int(size * centre[0] - boat.width / 2), int(size * centre[1] - boat.height * 0.62)
    alpha = Image.new("L", (size, size), 0)
    alpha.paste(boat.getchannel("A"), (bx, by))
    shadow = alpha.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(14))
    shade = Image.new("RGBA", (size, size), (0, 4, 8, 0))
    shade.putalpha(shadow.point(lambda v: int(v * 0.55)))
    img.alpha_composite(shade, (8, 14))
    b = np.asarray(boat).astype(np.float32)
    a = b[..., 3] / 255
    sink = np.zeros_like(a)
    span = max(6, int(boat.height * 0.05))
    for col in range(b.shape[1]):
        rows = np.nonzero(a[:, col] > 0.5)[0]
        if len(rows) == 0:
            continue
        bottom = rows.max()
        for k in range(span):
            if bottom - k >= 0:
                sink[bottom - k, col] = max(sink[bottom - k, col], (1 - k / span) * 0.75)
    b[..., :3] = b[..., :3] * (1 - sink[..., None]) + np.array([18, 52, 62], np.float32) * sink[..., None]
    boat = ImageEnhance.Brightness(Image.fromarray(b.clip(0, 255).astype(np.uint8), "RGBA")).enhance(0.88)
    if backlit:
        # Dark against a light below it, an amber rim where the glow catches its edges.
        boat = ImageEnhance.Brightness(boat).enhance(0.45)
        a_img = boat.getchannel("A")
        edge = ImageChops.subtract(a_img, a_img.filter(ImageFilter.MinFilter(5))).filter(ImageFilter.GaussianBlur(1.5))
        rim = Image.new("RGBA", boat.size, (255, 150, 50, 0))
        rim.putalpha(edge.point(lambda v: int(v * 0.8)))
        boat.alpha_composite(rim)
    img.alpha_composite(boat, (bx, by))
    return img


def icon_karve_eye():
    return icon_karve(eye=True)


def icon_karve(depth=0.8, eye=False):
    """A karve alone on a dark sea, seen three-quarters from above, and under it something vast and dark: the Heave Ho icon's recipe
    (one clean render on a painted sea). The render: render Karve yaw=150 pitch=55 size=1600x1600 bg=clear -> sources/karve_3q.png.
    depth: how dark the shape below is (0 none, 1 black)."""
    import numpy as np
    size = S
    base, ripples = deep_sea(size)
    shape = coil(size, (size * 0.5, size * 0.5), 1.0)
    # The shape: the deep's own darkest colour, so it looks like an absence of light under the water, not a thing on top of it.
    water = base * (1 - shape[..., None] * depth) + np.array([0.0, 0.01, 0.02]) * shape[..., None] * depth
    if eye:
        # One eye in the dark of it, beside the boat, half-lidded and dim: the shadow is looking up. Its glow carries through the
        # water; the ripples still go over it.
        ew = int(size * 0.24)
        glow_eye = slit_eye(ew, int(ew * 0.34)).rotate(-14, expand=True, resample=Image.BICUBIC)
        layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        layer.alpha_composite(glow_eye, (int(size * 0.07), int(size * 0.5)))
        lit = np.asarray(layer).astype(np.float32) / 255
        a = lit[..., 3:4]
        halo = np.asarray(layer.filter(ImageFilter.GaussianBlur(size / 35))).astype(np.float32)[..., :3] / 255
        water = water * (1 - a * 0.85) + lit[..., :3] * a * 0.85 + halo * 0.6
    water = water + ripples   # the surface over it
    img = Image.fromarray((np.clip(water, 0, 1) * 255).astype(np.uint8)).convert("RGBA")

    boat = crop_alpha(src("karve_3q.png"))
    sc = size * 0.5 / max(boat.size)
    boat = boat.resize((int(boat.width * sc), int(boat.height * sc)), Image.LANCZOS)
    bx, by = (size - boat.width) // 2, int(size * 0.18)
    alpha = Image.new("L", (size, size), 0)
    alpha.paste(boat.getchannel("A"), (bx, by))
    # Its shadow on the water, and the hull's lowest edge sunk in it: tinted toward the sea along the bottom of each column.
    shadow = alpha.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(14))
    shade = Image.new("RGBA", (size, size), (0, 4, 8, 0))
    shade.putalpha(shadow.point(lambda v: int(v * 0.55)))
    img.alpha_composite(shade, (8, 14))
    b = np.asarray(boat).astype(np.float32)
    a = b[..., 3] / 255
    sink = np.zeros_like(a)
    for col in range(b.shape[1]):
        rows = np.nonzero(a[:, col] > 0.5)[0]
        if len(rows) == 0:
            continue
        bottom = rows.max()
        span = max(6, int(boat.height * 0.05))
        for k in range(span):
            r = bottom - k
            if r >= 0:
                sink[r, col] = max(sink[r, col], (1 - k / span) * 0.75)
    tint = np.array([18, 52, 62], np.float32)
    b[..., :3] = b[..., :3] * (1 - sink[..., None]) + tint * sink[..., None]
    boat = Image.fromarray(b.clip(0, 255).astype(np.uint8), "RGBA")
    boat = ImageEnhance.Brightness(boat).enhance(0.85)
    img.alpha_composite(boat, (bx, by))
    return vignette(img, 0.6)


# --------------------------------------------------------------------------------------------------------------------------- comp

def comp(murk=0.55, fin=0.35, size=S):
    """The first-person picture built from three plates shot from one camera (keel plate, in rain): the empty sea, the Pacer cut out
    alone, the gunwale and hands cut out alone. The Pacer goes under the water here: darkened to the sea's own deep colour, softened,
    only partly there (murk: how much the water hides it), its fins a faint glow, the surface's glints laid back over it."""
    import numpy as np
    sea = src("plate_sea.png").convert("RGB").resize((size, size), Image.LANCZOS)
    pacer = src("plate_pacer_cut.png").resize((size, size), Image.LANCZOS)
    hands = src("plate_hands_cut.png").resize((size, size), Image.LANCZOS)

    sea = ImageEnhance.Color(sea).enhance(0.6)
    sea = ImageEnhance.Brightness(sea).enhance(0.62)
    sea = Image.blend(sea, Image.new("RGB", (size, size), (20, 38, 46)), 0.25)
    S_ = np.asarray(sea).astype(float) / 255

    P = np.asarray(pacer).astype(float) / 255
    alpha = P[..., 3]
    rgb = P[..., :3]
    # Its fins: the bright cyan, kept apart to glow faintly; the rest of it, the deep sea's darkest colour.
    cyan = np.clip((np.minimum(rgb[..., 1], rgb[..., 2]) - rgb[..., 0] - 0.25) / 0.4, 0, 1) * alpha
    deep = np.array([0.02, 0.06, 0.07])
    a_img = Image.fromarray((alpha * 255).astype("uint8")).filter(ImageFilter.GaussianBlur(size / 300))
    soft = np.asarray(a_img).astype(float) / 255 * (1 - murk)
    out = S_ * (1 - soft[..., None]) + deep * soft[..., None]
    f_img = Image.fromarray((cyan * 255).astype("uint8")).filter(ImageFilter.GaussianBlur(size / 140))
    glowf = np.asarray(f_img).astype(float) / 255 * fin
    out = out + glowf[..., None] * np.array([0.25, 0.85, 0.75])
    # The surface over it: the sea's own light detail (glints, foam) back on top, so it sits under the water, not on it.
    lum = S_.mean(axis=2)
    blur = np.asarray(Image.fromarray((lum * 255).astype("uint8")).filter(ImageFilter.GaussianBlur(size / 60))).astype(float) / 255
    glints = np.clip(lum - blur, 0, 1)
    out = np.clip(out + glints[..., None] * 0.9 * soft[..., None], 0, 1)

    img = Image.fromarray((out * 255).astype("uint8")).convert("RGBA")
    # The hands and gunwale, a touch darker and colder to sit in the rain light.
    h = ImageEnhance.Brightness(hands).enhance(0.72)
    h = ImageEnhance.Color(h).enhance(0.75)
    img.alpha_composite(h)
    img.alpha_composite(fog_banks((size, size), 41, 10, 0.0, 0.25, (120, 130, 136), 50, size / 25))
    img.alpha_composite(rain((size, size), 9, count=int(500 * size / 1024), alpha=34))
    return vignette(img, 0.55)


def icon_comp():
    return comp()


# --------------------------------------------------------------------------------------------------------------------------- rope

def icon_rope():
    shot = src("rope.png")
    box = (300, 300, 300 + 1100, 300 + 1100)
    img = shot.crop(box).resize((S, S), Image.LANCZOS)
    img = grade(img, sat=0.3, bright=0.55, tint=(20, 40, 50), tint_amount=0.35)
    # Under the surface the rope goes on, down into the black: carry it to the bottom, fading.
    d = ImageDraw.Draw(img)
    ex, ey = (813 - box[0]) * S / 1100, (496 - box[1]) * S / 1100
    deep = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deep)
    for y in range(S):
        a = int(max(0, (y / S - 0.6) / 0.4) ** 1.2 * 230)
        dd.line((0, y, S, y), fill=(0, 3, 6, a))
    img.alpha_composite(deep)
    img = eyes(img, (ex - 4, ey), (200, 255, 230), 5, 2.8)
    return vignette(img, 0.75)


# ----------------------------------------------------------------------------------------------------------------------------- all

MAKERS = {"eye": icon_eye, "below": icon_below, "halfboat": icon_halfboat, "mist": icon_mist, "mistdark": icon_mistdark, "pov": icon_pov, "pov2": icon_pov2, "comp": icon_comp, "karve": icon_karve, "karve_eye": icon_karve_eye, "karve_iris": icon_karve_iris, "karve_glow": icon_karve_glow, "eye_glow": icon_eye_glow, "rope": icon_rope}

if __name__ == "__main__":
    if not sys.argv[1:]:
        # The icon chosen: the burning eye under the water, no boat (drawn entirely here, no game art in it).
        icon_eye_glow().convert("RGB").resize((256, 256), Image.LANCZOS).save(os.path.join(HERE, "icon.png"))
        print("wrote icon.png")
        sys.exit(0)
    which = sys.argv[1:]
    icons = {}
    for name in which:
        icons[name] = MAKERS[name]().convert("RGB").resize((256, 256), Image.LANCZOS)
        icons[name].save(os.path.join(HERE, f"icon_{name}.png"))
    # The drafts side by side: full size on top, as small as in the mod list below.
    sheet = Image.new("RGB", (len(icons) * 276 + 20, 276 + 100), (32, 32, 36))
    for i, (name, im) in enumerate(icons.items()):
        sheet.paste(im, (20 + i * 276, 20))
        sheet.paste(im.resize((64, 64), Image.LANCZOS), (20 + i * 276, 296))
    sheet.save(os.path.join(HERE, "drafts.png"))
    print("wrote", ", ".join(f"icon_{n}.png" for n in icons), "and drafts.png")
