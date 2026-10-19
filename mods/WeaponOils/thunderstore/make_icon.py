"""Three icon ideas for Weapon Oils, built from the game's renders (transparent PNGs listed in a Claude Tools answer)."""
import json, sys, random, math
from PIL import Image, ImageDraw, ImageFilter

done, out = sys.argv[1], sys.argv[2]
renders = [o["render"] for o in json.load(open(done, encoding="utf-8"))["outputs"] if "render" in o]

def load(prefab):
    p = next(r for r in renders if r.endswith("render-" + prefab + ".png"))
    im = Image.open(p).convert("RGBA")
    return im.crop(im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())

def fit(im, h):
    s = h / im.height
    return im.resize((max(1, int(im.width * s)), int(h)), Image.LANCZOS)

S = 1024

def background(inner, outer):
    bg = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(bg)
    for r in range(int(S * 0.75), 0, -4):
        t = r / (S * 0.75)
        c = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(3)) + (255,)
        d.ellipse((S / 2 - r, S / 2 - r, S / 2 + r, S / 2 + r), fill=c)
    return bg

def glow(canvas, x, y, radius, colour, strength=200):
    g = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(g).ellipse((x - radius, y - radius, x + radius, y + radius), fill=colour + (strength,))
    canvas.alpha_composite(g.filter(ImageFilter.GaussianBlur(radius * 0.45)))

def sparks(canvas, cx, cy, spread, colour, n=40, seed=3):
    rnd = random.Random(seed)
    d = ImageDraw.Draw(canvas)
    for _ in range(n):
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(spread * 0.3, spread)
        x, y = cx + math.cos(a) * r, cy + math.sin(a) * r * 0.8 - rnd.uniform(0, spread * 0.3)
        s = rnd.uniform(3, 9)
        d.line((x - s, y, x + s, y), fill=colour + (230,), width=2)
        d.line((x, y - s, x, y + s), fill=colour + (230,), width=2)

def shadow(canvas, im, x, y):
    a = im.getchannel("A").point(lambda v: int(v * 0.6))
    sh = Image.new("RGBA", im.size, (0, 0, 0, 0)); sh.putalpha(a)
    pad = 40
    big = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0)); big.paste(sh, (pad, pad))
    canvas.alpha_composite(big.filter(ImageFilter.GaussianBlur(14)), (x - pad + 10, y - pad + 16))

def place(canvas, im, cx, bottom):
    x, y = int(cx - im.width / 2), int(bottom - im.height)
    shadow(canvas, im, x, y)
    canvas.alpha_composite(im, (x, y))

def finish(canvas, name, ring=(150, 105, 60)):
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((10, 10, S - 10, S - 10), radius=120, outline=ring + (255,), width=14)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=128, fill=255)
    canvas.putalpha(mask)
    canvas.resize((256, 256), Image.LANCZOS).save(f"{out}/icon_{name}.png")
    return canvas

# A: Ember bottle in front, Rime and Venom behind it, an ember glow
a = background((70, 40, 22), (14, 9, 6))
glow(a, S / 2, S * 0.55, 330, (255, 120, 30), 150)
place(a, fit(load("nick_oil_rime"), 560), S * 0.28, S * 0.84)
place(a, fit(load("nick_oil_venom"), 560), S * 0.72, S * 0.84)
place(a, fit(load("nick_oil_ember"), 760), S * 0.5, S * 0.93)
sparks(a, S / 2, S * 0.32, 300, (255, 170, 60))
finish(a, "a_three_bottles")

# B: the Ember bottle with its mortar, sparks rising
b = background((80, 44, 20), (14, 9, 6))
glow(b, S * 0.47, S * 0.55, 340, (255, 120, 30), 170)
place(b, fit(load("nick_oil_ember"), 800), S * 0.43, S * 0.9)
place(b, fit(load("nick_oilbase_ember"), 330), S * 0.73, S * 0.93)
sparks(b, S * 0.45, S * 0.3, 320, (255, 180, 70), n=50, seed=7)
finish(b, "b_bottle_and_mortar")

# C: a fan of five bottles, one per colour family
c = background((52, 36, 26), (12, 9, 7))
glow(c, S / 2, S * 0.6, 380, (255, 190, 90), 110)
for oid, cx, h, bottom in (("cinder", 0.16, 470, 0.80), ("rime", 0.32, 560, 0.85), ("venom", 0.68, 560, 0.85), ("storm", 0.84, 470, 0.80)):
    place(c, fit(load("nick_oil_" + oid), h), S * cx, S * bottom)
place(c, fit(load("nick_oil_ember"), 700), S * 0.5, S * 0.92)
sparks(c, S / 2, S * 0.3, 360, (255, 210, 120), n=30, seed=11)
finish(c, "c_five_bottles")

sheet = Image.new("RGBA", (3 * 276 + 20, 296), (40, 40, 40, 255))
for i, n in enumerate(("a_three_bottles", "b_bottle_and_mortar", "c_five_bottles")):
    sheet.alpha_composite(Image.open(f"{out}/icon_{n}.png"), (20 + i * 276, 20))
sheet.save(f"{out}/icons_compare.png")
print("ok")
