"""
Builds thunderstore/screenshots/00-visitors.png: Good Tidings' four visitors together, each photographed alone in the game and put on
one picture (as Fuling Heckler's icon was made).

The cut-outs come from a temporary portrait command (each visitor alone on a transparent background, its renderers on a layer nothing
else uses, as Fuling Heckler's `heckler portrait`), the background from Claude Tools' `view`; they are in sources/ (the game's art:
not kept in the repository).

Needs:  pip install Pillow
    python make_showcase.py
"""
import os
from PIL import Image, ImageEnhance, ImageFilter, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "sources")
W, H = 1920, 1080


def cut(name):
    im = Image.open(os.path.join(SRC, name)).convert("RGBA")
    return im.crop(im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())


def fit_height(im, h):
    return im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)


def ground_shadow(canvas, cx, base, width, strength=120):
    """A soft dark oval on the ground under a figure."""
    sh = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(sh)
    d.ellipse([cx - width // 2, base - width // 9, cx + width // 2, base + width // 9], fill=strength)
    sh = sh.filter(ImageFilter.GaussianBlur(width // 10))
    black = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    black.putalpha(sh)
    canvas.alpha_composite(black)


def place(canvas, im, cx, base, shadow_width):
    ground_shadow(canvas, cx, base, shadow_width)
    canvas.alpha_composite(im, (round(cx - im.width / 2), round(base - im.height)))


def main():
    bg = Image.open(os.path.join(SRC, "background.png")).convert("RGB").resize((W, H), Image.LANCZOS)
    bg = bg.filter(ImageFilter.GaussianBlur(9))
    bg = ImageEnhance.Brightness(bg).enhance(0.82)
    canvas = bg.convert("RGBA")
    # a darker edge all round, so the eye goes to the middle
    vignette = Image.new("L", (W, H), 0)
    ImageDraw.Draw(vignette).ellipse([-W * 0.15, -H * 0.25, W * 1.15, H * 1.25], fill=255)
    vignette = vignette.filter(ImageFilter.GaussianBlur(160))
    dark = Image.new("RGBA", (W, H), (10, 12, 20, 255))
    dark.putalpha(vignette.point(lambda v: int((255 - v) * 0.7)))
    canvas.alpha_composite(dark)

    # back to front: Dagny on Atlas behind, the skald playing, the Dvergr on one knee, Muninn in front
    place(canvas, fit_height(cut("trader.png"), 640), 560, 900, 700)
    place(canvas, fit_height(cut("skald.png"), 600), 1050, 975, 260)
    place(canvas, fit_height(cut("dverger.png"), 390), 1420, 1000, 260)
    place(canvas, fit_height(cut("muninn.png"), 380), 1700, 1035, 230)

    os.makedirs(os.path.join(HERE, "screenshots"), exist_ok=True)
    out = os.path.join(HERE, "screenshots", "00-visitors.png")
    canvas.convert("RGB").save(out)
    print(out)


if __name__ == "__main__":
    main()
