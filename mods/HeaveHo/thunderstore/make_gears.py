"""
Builds screenshots/07-gears.png: the rower's HUD in each of the four gears, from four screenshots taken while rowing
(Claude Tools' `shot`, HUD on, 2560 x 1440).

For each gear it crops the crossed-oars icon and its arrows by the oarlock (found by their orange colour) and the oar icon under the
minimap, and sets them side by side under a label and a line of what the gear does.

Needs:  pip install numpy Pillow

    python make_gears.py <oars-up.png> <easy.png> <hard.png> <back.png> [out.png]
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

GEARS = [
    ('Oars up', 'No thrust, no stamina'),
    ('Easy stroke', 'Half thrust, low stamina'),
    ('Pull hard', 'Full thrust, full stamina'),
    ('Back water', 'Reverse, low stamina'),
]


def find_icon(im):
    """The orange crossed-oars icon by the oarlock, in the lower middle of the picture."""
    a = np.asarray(im.convert('RGB')).astype(int)
    sub = a[800:1300, 1000:2100]
    m = (sub[..., 0] > 225) & (sub[..., 1] > 130) & (sub[..., 1] < 215) & (sub[..., 2] < 80)
    ys, xs = np.nonzero(m)
    if len(xs) < 30:
        raise SystemExit('no oarlock icon found in a picture: is the HUD on and the player rowing?')
    return int(np.median(xs)) + 1000, int(np.median(ys)) + 800


def font(size, bold=False):
    for name in (('segoeuib.ttf' if bold else 'segoeui.ttf'), 'arial.ttf'):
        try:
            return ImageFont.truetype('C:/Windows/Fonts/' + name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def main():
    shots = sys.argv[1:5]
    out = sys.argv[5] if len(sys.argv) > 5 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'screenshots', '07-gears.png')
    cw, ch, icon_h = 400, 290, 300
    pad = 12
    W = pad + 4 * (cw + pad)
    H = 56 + ch + 12 + icon_h + 56
    sheet = Image.new('RGB', (W, H), (22, 24, 28))
    d = ImageDraw.Draw(sheet)
    f_name, f_note = font(28, True), font(20)
    for k, (path, (name, note)) in enumerate(zip(shots, GEARS)):
        im = Image.open(path).convert('RGB')
        cx, cy = find_icon(im)
        arrows = im.crop((cx - 190, cy - 190, cx + 190, cy + 90))
        arrows = arrows.resize((cw, int(cw * arrows.height / arrows.width)), Image.LANCZOS)
        icon = im.crop((2230, 250, 2500, 640))
        icon = icon.resize((int(icon_h * icon.width / icon.height), icon_h), Image.LANCZOS)
        x = pad + k * (cw + pad)
        d.text((x + 4, 10), name, fill=(255, 214, 120), font=f_name)
        sheet.paste(arrows, (x, 56))
        sheet.paste(icon, (x + (cw - icon.width) // 2, 56 + ch + 12))
        d.text((x + 4, H - 40), note, fill=(205, 205, 205), font=f_note)
    sheet.save(out, optimize=True)
    print('wrote', out, sheet.size)


if __name__ == '__main__':
    main()
