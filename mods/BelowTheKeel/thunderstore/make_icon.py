"""Below the Keel package icon drafts (256 x 256 each), four ideas side by side:
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

def draw_eye(size, open_frac=0.62):
    """The omen's eye seen from above: the same pattern as EyeMeshes.Draw (streaked amber iris, slit pupil, dark ring, bloodshot dark
    white), inside an almond of lids."""
    w = h = size
    eye = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    glowl = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    pe, pg = eye.load(), glowl.load()
    iris_r, ring_r = 0.40, 0.45
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
                c = tuple(int(a + (b - a) * t) for a, b in zip((40, 26, 20), (80, 10, 8)))
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

MAKERS = {"eye": icon_eye, "below": icon_below, "halfboat": icon_halfboat, "mist": icon_mist, "mistdark": icon_mistdark, "pov": icon_pov, "pov2": icon_pov2, "rope": icon_rope}

if __name__ == "__main__":
    which = sys.argv[1:] or list(MAKERS)
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
