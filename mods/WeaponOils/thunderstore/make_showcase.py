"""Builds the Weapon Oils showcase picture from the game's renders (transparent PNGs listed in a Claude Tools answer)."""
import json, sys, re
from PIL import Image, ImageDraw, ImageFont, ImageFilter

done, english, out = sys.argv[1], sys.argv[2], sys.argv[3]
renders = [o["render"] for o in json.load(open(done, encoding="utf-8"))["outputs"] if "render" in o]
names = json.load(open(english, encoding="utf-8"))
ids = ["ember", "keen", "trollfat", "barkhide", "venom", "leech", "rime", "storm", "loxfat", "wisp", "royal", "cinder"]

def find(prefab):
    return next(p for p in renders if p.endswith("render-" + prefab + ".png"))

def load(prefab):
    im = Image.open(find(prefab)).convert("RGBA")
    return im.crop(im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())

def fit(im, w, h):
    s = min(w / im.width, h / im.height)
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)

def shadow(canvas, im, x, y, blur=14, alpha=150):
    a = im.getchannel("A").point(lambda v: int(v * alpha / 255))
    sh = Image.new("RGBA", im.size, (0, 0, 0, 0)); sh.putalpha(a)
    pad = blur * 3
    big = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0)); big.paste(sh, (pad, pad))
    big = big.filter(ImageFilter.GaussianBlur(blur))
    canvas.alpha_composite(big, (x - pad + 6, y - pad + 10))

W, H = 1920, 1080
bg = Image.new("RGBA", (W, H))
d = ImageDraw.Draw(bg)
for r in range(0, 1300, 4):   # warm radial gradient, light behind the middle
    t = r / 1300
    c = (int(64 - 46 * t), int(48 - 35 * t), int(34 - 25 * t), 255)
    d.ellipse((W * 0.45 - r * 1.3, H * 0.5 - r, W * 0.45 + r * 1.3, H * 0.5 + r), outline=c, width=6)
canvas = bg

def font(size, bold=False):
    for f in (["C:/Windows/Fonts/georgiab.ttf"] if bold else []) + ["C:/Windows/Fonts/georgia.ttf", "C:/Windows/Fonts/arial.ttf"]:
        try: return ImageFont.truetype(f, size)
        except OSError: pass
    return ImageFont.load_default()

draw = ImageDraw.Draw(canvas)
title, sub = "Weapon Oils", "Twelve oils from every biome, brewed at the cauldron and matured in the oil press"
tf, sf, lf = font(72, True), font(28), font(21)
draw.text((W // 2 + 2, 62 + 3), title, font=tf, fill=(0, 0, 0, 160), anchor="mm")
draw.text((W // 2, 62), title, font=tf, fill=(238, 214, 160, 255), anchor="mm")
draw.text((W // 2, 118), sub, font=sf, fill=(205, 190, 160, 255), anchor="mm")

# the press, large on the left
press = fit(load("nick_oilpress"), 560, 820)
px, py = 70 + (560 - press.width) // 2, H - 40 - press.height
shadow(canvas, press, px, py)
canvas.alpha_composite(press, (px, py))
draw = ImageDraw.Draw(canvas)
draw.text((px + press.width // 2, H - 24), names.get("nick_oilpress", "Oil press"), font=lf, fill=(220, 205, 175, 255), anchor="mm")

# twelve cells: each oil with its base in front of it
left, top, cols, cw, ch = 690, 170, 6, 200, 430
for i, oid in enumerate(ids):
    cx = left + (i % cols) * cw + cw // 2
    cy = top + (i // cols) * ch
    bottle = fit(load("nick_oil_" + oid), 170, 270)
    mortar = fit(load("nick_oilbase_" + oid), 118, 90)
    bx, by = cx - bottle.width // 2 - 14, cy + 300 - bottle.height
    mx, my = cx + 18, cy + 300 - mortar.height + 22
    shadow(canvas, bottle, bx, by, 10, 130)
    canvas.alpha_composite(bottle, (bx, by))
    shadow(canvas, mortar, mx, my, 8, 130)
    canvas.alpha_composite(mortar, (mx, my))
    draw = ImageDraw.Draw(canvas)
    label = re.sub(r" Oil$", "", names.get("nick_oil_" + oid, oid))
    draw.text((cx + 1, cy + 352 + 2), label, font=lf, fill=(0, 0, 0, 170), anchor="mm")
    draw.text((cx, cy + 352), label, font=lf, fill=(232, 216, 180, 255), anchor="mm")

canvas.convert("RGB").save(out, quality=92)
print(out, canvas.size)
