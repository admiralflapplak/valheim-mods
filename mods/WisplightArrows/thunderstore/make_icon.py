"""The Wisplight Arrows package icon (256 x 256), built from the game's render of the arrow (a transparent PNG listed in a Claude Tools
answer): the arrow corner to corner over Mistlands mist, the mist parted round its glowing orb.
Render first (Claude Tools): render nick_wisparrow yaw=90 pitch=0 focus=0,0,-0.55 dist=0.8 fov=30 size=2400x1200 bg=clear
Run: python make_icon.py <render answer .done.json> <out.png>"""
import json, sys, random, math
from PIL import Image, ImageDraw, ImageFilter

done, out = sys.argv[1], sys.argv[2]
renders = [o["render"] for o in json.load(open(done, encoding="utf-8"))["outputs"] if "render" in o]
S = 1024

def crop(im):
    return im.crop(im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())

arrow = crop(Image.open(renders[0]).convert("RGBA"))          # a close-up of the arrow's front, side on, tip to the right (cut off at the left)

# background: dark mist-blue, lighter towards the middle
bg = Image.new("RGBA", (S, S))
d = ImageDraw.Draw(bg)
for r in range(int(S * 0.8), 0, -4):
    t = r / (S * 0.8)
    inner, outer = (52, 66, 92), (14, 18, 30)
    d.ellipse((S / 2 - r, S / 2 - r, S / 2 + r, S / 2 + r), fill=tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(3)) + (255,))

# the arrow, close up: the orb large at the top right, the shaft running out of the bottom left corner
angle = 38
ORB_PX = 270                              # the orb's width on the 1024 picture
scale = ORB_PX / arrow.height             # (the orb is the tallest part of the close-up)
a = arrow.resize((int(arrow.width * scale), int(arrow.height * scale)), Image.LANCZOS)
orb = (a.width - a.height / 2, a.height / 2)        # the orb's middle in the unturned arrow
rot = a.rotate(angle, resample=Image.BICUBIC, expand=True)
dx, dy = orb[0] - a.width / 2, orb[1] - a.height / 2
c, s_ = math.cos(math.radians(angle)), math.sin(math.radians(angle))
ox, oy = rot.width / 2 + dx * c + dy * s_, rot.height / 2 - dx * s_ + dy * c   # where it lands after turning
tipx, tipy = S * 0.68, S * 0.32
ax, ay = int(tipx - ox), int(tipy - oy)

# mist: soft pale blobs everywhere but round the orb
rnd = random.Random(7)
mist = Image.new("RGBA", (S, S), (0, 0, 0, 0))
md = ImageDraw.Draw(mist)
for _ in range(140):
    x, y = rnd.uniform(-100, S + 100), rnd.uniform(-100, S + 100)
    r = rnd.uniform(60, 170)
    clear = math.hypot(x - tipx, y - tipy)
    if clear < 380: continue
    alpha = int(rnd.uniform(30, 60) * min(1.0, (clear - 380) / 200))
    md.ellipse((x - r, y - r * 0.7, x + r, y + r * 0.7), fill=(200, 215, 235, alpha))
bg.alpha_composite(mist.filter(ImageFilter.GaussianBlur(40)))

def glow(x, y, radius, colour, strength):
    g = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(g).ellipse((x - radius, y - radius, x + radius, y + radius), fill=colour + (strength,))
    bg.alpha_composite(g.filter(ImageFilter.GaussianBlur(radius * 0.5)))

glow(tipx, tipy, 420, (60, 140, 255), 110)
glow(tipx, tipy, 300, (110, 190, 255), 120)

# a soft shadow, then the arrow
sh = Image.new("RGBA", rot.size, (0, 0, 0, 0)); sh.putalpha(rot.getchannel("A").point(lambda v: int(v * 0.55)))
pad = 40
big = Image.new("RGBA", (rot.width + pad * 2, rot.height + pad * 2), (0, 0, 0, 0)); big.paste(sh, (pad, pad))
bg.alpha_composite(big.filter(ImageFilter.GaussianBlur(14)), (ax - pad + 10, ay - pad + 16))
bg.alpha_composite(rot, (ax, ay))

# the wisp's light shining out of the orb, and a few sparks drifting off it
glow(tipx, tipy, 120, (170, 230, 255), 110)
dd = ImageDraw.Draw(bg)
for _ in range(14):
    t = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(270, 420)
    x, y = tipx + math.cos(t) * r, tipy + math.sin(t) * r * 0.9
    sz = rnd.uniform(5, 11)
    spark = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(spark).ellipse((x - sz, y - sz, x + sz, y + sz), fill=(200, 240, 255, int(rnd.uniform(150, 240))))
    bg.alpha_composite(spark.filter(ImageFilter.GaussianBlur(2.5)))

bg.convert("RGB").resize((256, 256), Image.LANCZOS).save(out)
bg.convert("RGB").save(out.replace(".png", "_1024.png"))
print("wrote", out)
