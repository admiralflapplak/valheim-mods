"""The Wanted package icon (256 x 256), built from the game's render of the board: a red bounty notice on the board's planks with an
arrow shot through it, lit warm as if by a torch at dusk, the edges sinking into shadow.
Render first (Claude Tools, with Wanted loaded; the board's own look has the arrow-pinned notice in red):
    render nick_wanted_board yaw=32 pitch=16 focus=0.1,1.9,0.25 dist=1.35 fov=32 size=2048x2048 bg=clear
The render itself is the game's art, so it is not kept in the repository; the icon is.
Needs: pip install numpy Pillow
Run: python make_icon.py <render.png> [out.png]"""
import os, sys
import numpy as np
from PIL import Image, ImageEnhance, ImageFilter

S = 1024
src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.png")

im = Image.open(src).convert("RGB")
w, h = im.size
# the notice and the arrow: a square from just left of the notice to past the fletching, top of the paper's plank to its bottom edge
box = (int(w * 0.13), int(h * 0.12), int(w * 0.13) + int(w * 0.83), int(h * 0.12) + int(w * 0.83))
im = im.crop(box).resize((S, S), Image.LANCZOS)

a = np.asarray(im, dtype=np.float32) / 255
y, x = np.mgrid[0:S, 0:S].astype(np.float32) / S

# torchlight from the upper left: warm where it falls, falling off across the board
light = np.exp(-(((x - 0.22) / 0.62) ** 2 + ((y - 0.18) / 0.62) ** 2))
a = a * (0.55 + 0.6 * light[..., None]) * np.array([1.06, 0.97, 0.86])
# a soft warm glow on the paper (it catches the light more than the wood)
glow = np.exp(-(((x - 0.36) / 0.22) ** 2 + ((y - 0.52) / 0.30) ** 2))
a = 1 - (1 - a) * (1 - glow[..., None] * np.array([0.22, 0.14, 0.05]))
# the edges sink into shadow
d = np.sqrt((x - 0.48) ** 2 + (y - 0.5) ** 2)
a *= np.clip(1.15 - d * 1.05, 0.28, 1)[..., None]

img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), "RGB")
img = ImageEnhance.Contrast(img).enhance(1.12)
img = ImageEnhance.Color(img).enhance(1.12)
img.save(out.replace(".png", "_1024.png"))
small = img.resize((256, 256), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.2, percent=70, threshold=2))
small.save(out)
print("wrote", out)
