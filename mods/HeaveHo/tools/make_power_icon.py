"""
Draws assets/rowingPowerIcon.png: the oar icon a rower sees under the minimap, where the helm shows its rudder or sail.

The game's own icons there (the rudder blade, the sails) are small lit illustrations with a soft shadow, so this one is the Karve's
oar model (assets/oar_karve.obj, in the Karve's own wood) drawn the same way: lit from the upper left, smooth shaded, tilted like the
rudder icon, 213 x 377 px. It is an ordinary PNG: replace it with your own picture (same size, transparent background) and the mod
shows that instead.

Needs:  pip install numpy Pillow
The Karve's texture atlas (boat_d) belongs to the game and is not in this repo: pass it as the second argument to colour the oar
from it (python make_power_icon.py out.png boat_d.png); without it the oar is a plain oak colour.

    python make_power_icon.py [out.png] [boat_d.png]
"""
import math, os, sys
import numpy as np
from PIL import Image, ImageFilter

W, H = 213, 377          # the game's rudder icon (ship_rudder_icon)
SS = 4                   # supersampling
HERE = os.path.dirname(os.path.abspath(__file__))
OBJ = os.path.join(HERE, '..', 'assets', 'oar_karve.obj')
TILT = -14.0             # degrees the oar leans, blade toward the lower left like the rudder
WIDEN = 2.1              # the oar is drawn this much wider than life, so it reads at the icon's small size
LIGHT = np.array([-0.45, 0.65, 0.62])
LIGHT = LIGHT / np.linalg.norm(LIGHT)


def load(path):
    V, VT, VN, F = [], [], [], []
    for line in open(path):
        p = line.split()
        if not p:
            continue
        if p[0] == 'v':
            V.append([float(x) for x in p[1:4]])
        elif p[0] == 'vt':
            VT.append([float(x) for x in p[1:3]])
        elif p[0] == 'vn':
            VN.append([float(x) for x in p[1:4]])
        elif p[0] == 'f':
            F.append([[int(i) - 1 for i in t.split('/')] for t in p[1:4]])
    return np.array(V), np.array(VT), np.array(VN), F


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'assets', 'rowingPowerIcon.png')
    atlas = np.asarray(Image.open(sys.argv[2]).convert('RGB')).astype(float) / 255.0 if len(sys.argv) > 2 else None

    V, VT, VN, F = load(OBJ)
    # the oar's x runs from the grip to the blade; stand it up with the blade at the bottom, face toward the viewer
    # (the blade's face looks along z), then lean it
    a = math.radians(TILT)
    rot = np.array([[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]])
    # model (x along the oar) -> screen: x down, y across
    base = np.array([[WIDEN, 0, 0], [0, 1, 0], [0, 0, 1]], float) @ np.array([[0, 1, 0], [-1, 0, 0], [0, 0, 1]], float)   # x -> screen down
    M = rot @ base
    P = (M @ V.T).T
    N = (rot @ np.array([[0, 1, 0], [-1, 0, 0], [0, 0, 1]], float) @ VN.T).T

    # fit the oar into the canvas with a margin
    w, h = W * SS, H * SS
    lo, hi = P[:, :2].min(0), P[:, :2].max(0)
    scale = min((w * 0.80) / max(hi[0] - lo[0], 1e-6), (h * 0.92) / max(hi[1] - lo[1], 1e-6))
    cx, cy = (lo + hi) / 2
    sx = (P[:, 0] - cx) * scale + w * 0.52
    sy = h * 0.5 - (P[:, 1] - cy) * scale
    z = P[:, 2]

    color = np.zeros((h, w, 3))
    alpha = np.zeros((h, w))
    zbuf = np.full((h, w), -1e9)

    for f in F:
        idx = [t[0] for t in f]
        uvi = [t[1] for t in f]
        nri = [t[2] for t in f]
        xs, ys = sx[idx], sy[idx]
        x0, x1 = int(max(0, math.floor(xs.min()))), int(min(w - 1, math.ceil(xs.max())))
        y0, y1 = int(max(0, math.floor(ys.min()))), int(min(h - 1, math.ceil(ys.max())))
        if x1 < x0 or y1 < y0:
            continue
        den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(den) < 1e-9:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        l0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
        l1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not inside.any():
            continue
        zz = l0 * z[idx[0]] + l1 * z[idx[1]] + l2 * z[idx[2]]
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        take = inside & (zz > sub)
        if not take.any():
            continue
        nn = (l0[..., None] * N[nri[0]] + l1[..., None] * N[nri[1]] + l2[..., None] * N[nri[2]])
        nn = nn / np.maximum(np.linalg.norm(nn, axis=-1, keepdims=True), 1e-9)
        nn = np.where((nn[..., 2:3] < 0), -nn, nn)                       # two-sided: always face the viewer
        lam = np.clip(nn @ LIGHT, 0, 1)
        spec = np.clip(nn @ ((LIGHT + np.array([0, 0, 1.0])) / np.linalg.norm(LIGHT + np.array([0, 0, 1.0]))), 0, 1) ** 24
        uv = l0[..., None] * VT[uvi[0]] + l1[..., None] * VT[uvi[1]] + l2[..., None] * VT[uvi[2]]
        if atlas is not None:
            ax = np.clip((uv[..., 0] * (atlas.shape[1] - 1)).astype(int), 0, atlas.shape[1] - 1)
            ay = np.clip(((1 - uv[..., 1]) * (atlas.shape[0] - 1)).astype(int), 0, atlas.shape[0] - 1)
            base_c = atlas[ay, ax]
        else:
            base_c = np.broadcast_to(np.array([0.78, 0.55, 0.33]), nn.shape)
        shade = 0.52 + 0.72 * lam
        c = np.clip(base_c * shade[..., None] + 0.18 * spec[..., None], 0, 1)
        col = color[y0:y1 + 1, x0:x1 + 1]
        al = alpha[y0:y1 + 1, x0:x1 + 1]
        col[take] = c[take]
        al[take] = 1.0
        sub[take] = zz[take]

    img = np.dstack([np.clip(color * 255, 0, 255), alpha * 255]).astype(np.uint8)
    oar = Image.fromarray(img, 'RGBA')

    # a thin dark edge, so the oar reads against the sea as the game's icons do
    mask = oar.split()[3]
    edge = mask.filter(ImageFilter.MaxFilter(5)).point(lambda v: v)
    outline = Image.new('RGBA', oar.size, (30, 18, 8, 0))
    outline.putalpha(edge)
    oar = Image.alpha_composite(outline, oar)

    # the soft black shadow the game's icons carry, offset down and right
    shadow = Image.new('RGBA', oar.size, (0, 0, 0, 0))
    sh = mask.filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.GaussianBlur(7 * SS))
    shadow.putalpha(sh.point(lambda v: min(255, int(v * 1.6))))
    canvas = Image.new('RGBA', oar.size, (0, 0, 0, 0))
    canvas.alpha_composite(shadow, (int(3 * SS), int(4 * SS)))
    canvas.alpha_composite(oar)

    canvas = canvas.resize((W, H), Image.LANCZOS)
    canvas.save(out)
    print('wrote', os.path.normpath(out), canvas.size)


if __name__ == '__main__':
    main()
