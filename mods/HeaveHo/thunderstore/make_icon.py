"""
Builds thunderstore/icon.png (256 x 256) from a game render of the Drakkar with its oars out.

The render is made in the game with Claude Tools' `render` command, with Heave Ho loaded (a ship drawn on its own gets its oars out):

    render VikingShip_Ashlands yaw=180 pitch=30 views=1 bg=clear size=2000x2000 focus=0,7,0 dist=68 fov=26

That is the ship bow-on from above on a transparent background. This script puts it on a lit sea (a sunrise glow behind the sail, deep
water round the hull, a foam wake, a soft rim light, a vignette) and writes the icon. The render itself is the game's art, so it is
not kept in the repository; the icon is.

Needs:  pip install numpy Pillow

    python make_icon.py <render.png> [out.png]
"""
import math, os, sys
import numpy as np
from PIL import Image, ImageChops, ImageEnhance, ImageFilter

SIZE = 1024


def sea(size):
    """A turquoise sea fading to deep blue at the edges, lit by a low sun behind the sail, with long ripples that catch the light."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.45) ** 2)
    t = np.clip(1 - d / 0.75, 0, 1)
    base = np.stack([0.02 + 0.06 * t, 0.10 + 0.40 * t, 0.17 + 0.46 * t], axis=-1)
    # the sun: a warm glow high up (screen-blended, so it warms the water instead of greying it)
    sun = np.exp(-(((x - 0.50) / 0.46) ** 2 + ((y - 0.10) / 0.30) ** 2))
    glow = np.stack([1.00 * sun, 0.46 * sun, 0.12 * sun], axis=-1) * 0.78
    img = 1 - (1 - base) * (1 - glow)
    rng = np.random.default_rng(7)
    noise = rng.random((size // 8, size // 8)).astype(np.float32)
    noise = np.asarray(Image.fromarray((noise * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC).filter(ImageFilter.GaussianBlur(8)), dtype=np.float32) / 255
    stripes = 0.5 + 0.5 * np.sin((y * 110) + noise * 10)
    ripple = np.clip((stripes - 0.45) * 1.8, 0, 1) * np.clip((noise - 0.35) * 2.2, 0, 1)
    img += ripple[..., None] * np.array([0.06, 0.20, 0.26]) * (0.45 + 0.5 * sun[..., None])
    img += (ripple * sun)[..., None] * np.array([0.40, 0.22, 0.05])
    return Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), 'RGB').convert('RGBA')


def main():
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'icon.png')

    ship = Image.open(src).convert('RGBA')
    bbox = ship.getchannel('A').getbbox()
    ship = ship.crop(bbox)

    canvas = sea(SIZE)
    # fit the ship: tall, centred, a little low so the sail has air above it
    scale = min(SIZE * 0.86 / ship.width, SIZE * 0.93 / ship.height)
    ship = ship.resize((int(ship.width * scale), int(ship.height * scale)), Image.LANCZOS)
    px = (SIZE - ship.width) // 2
    py = int(SIZE * 0.045) + (SIZE - ship.height - int(SIZE * 0.07)) // 2

    alpha = Image.new('L', (SIZE, SIZE), 0)
    alpha.paste(ship.getchannel('A'), (px, py))

    # the ship's shadow on the water, and foam where the bow cuts it
    shadow = alpha.filter(ImageFilter.MaxFilter(11)).filter(ImageFilter.GaussianBlur(18))
    shade = Image.new('RGBA', (SIZE, SIZE), (1, 8, 16, 0))
    shade.putalpha(shadow.point(lambda v: int(v * 0.62)))
    canvas.alpha_composite(shade, (10, 18))
    foam = alpha.filter(ImageFilter.MaxFilter(13)).filter(ImageFilter.GaussianBlur(6))
    foam_ring = ImageChops.subtract(foam, alpha.filter(ImageFilter.MaxFilter(3)))
    low = Image.new('L', (SIZE, SIZE), 0)
    for yy in range(SIZE):
        low.paste(int(255 * min(1.0, max(0.0, (yy / SIZE - 0.55) / 0.25))), (0, yy, SIZE, yy + 1))
    foam_ring = ImageChops.multiply(foam_ring, low)
    foam_layer = Image.new('RGBA', (SIZE, SIZE), (226, 244, 246, 0))
    foam_layer.putalpha(foam_ring.point(lambda v: int(v * 0.55)))
    canvas.alpha_composite(foam_layer)

    # the ship, with a touch more life in it
    colour = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    colour.paste(ship, (px, py))
    rgb = colour.convert('RGB')
    rgb = ImageEnhance.Contrast(rgb).enhance(1.10)
    rgb = ImageEnhance.Color(rgb).enhance(1.14)
    rgb = ImageEnhance.Brightness(rgb).enhance(1.05)
    colour = Image.merge('RGBA', (*rgb.split(), colour.getchannel('A')))
    canvas.alpha_composite(colour)

    # vignette
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2)
    v = np.clip(1 - 0.85 * np.clip((d - 0.30) / 0.45, 0, 1) ** 1.6, 0, 1)
    arr = np.asarray(canvas.convert('RGB'), dtype=np.float32) * v[..., None]
    final = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGB')

    big = out.replace('.png', '_1024.png')
    final.save(big)
    icon = final.resize((256, 256), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.0, percent=60, threshold=2))
    icon.save(out)
    print('wrote', out, icon.size, 'and', big)


if __name__ == '__main__':
    main()
