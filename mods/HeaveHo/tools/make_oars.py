"""
Writes the ships' own oars, assets/oar_<ship>.obj:

  drakkar   a turned oak loom with a brass-capped grip, a brass collar where it passes the oar port, and a broad leaf blade (oak, then
            the hull's dark green scales) with a brass root band and tip.
  longship  a plain weathered-oak oar with a rope-wrapped collar at the port and a narrow leaf blade.
  karve     a plain pale-oak oar with a rope-wrapped collar, and oarlock_karve.obj, the small wooden oarlock (a block on the gunwale
            with two pins) each oar rides in.

The oars have no texture of their own: their UVs point into the ship's hull atlas (the Drakkar's ashlandsship_d, the Longship's
ship_diffuse), so in the game each is drawn with its hull's own material and matches its wood exactly. The atlases belong to the game
and are not in this repo; the UV rectangles below say where in them the wood, brass and scales are.

Frame: +X outboard from the oarlock (the origin), Y up, the blade's face looks fore and aft (the stroke sweeps it through the
water). Metres. Written in OBJ's right-handed axes (HeaveHo's ObjLoader mirrors Z for Unity).

    python make_oars.py [drakkar] [longship] [karve] [oarlock_karve]       (none named: all)
"""
import math, os, sys

SEG = 12   # sides around the loom

# UV rectangles in the hull atlas (v up): u0, v0, u1, v1
# Drakkar atlas (ashlandsship_d): oak plank, brass, green scales
DRAKKAR_RECT = {'wood': (0.25, 0.24, 0.70, 0.335), 'brass': (0.74, 0.61, 0.94, 0.66), 'scale': (0.05, 0.03, 0.45, 0.16)}
# Longship atlas (ship_diffuse): the deck's tan oak, a lighter plank, and darker weathered wood
LONGSHIP_RECT = {'wood': (0.26, 0.53, 0.78, 0.66), 'plank': (0.72, 0.855, 0.92, 0.875), 'dark': (0.30, 0.835, 0.60, 0.865)}
# Karve atlas (boat_d, 256 px, hand painted): the pale orange stripe, a lighter patch, and the darker brown below
KARVE_RECT = {'wood': (0.42, 0.186, 0.84, 0.200), 'plank': (0.40, 0.277, 0.55, 0.316), 'dark': (0.10, 0.04, 0.40, 0.10)}


def drakkar_stations():
    """(x, width along Y, thickness along Z, zone). Rings that share an x make a step (a band's edge)."""
    s = []

    def add(x, w, t, zone):
        s.append((x, w, t, zone))

    # grip: a brass end cap, the turned grip, a brass band
    add(-1.55, 0.000, 0.000, 'brass')
    add(-1.54, 0.075, 0.075, 'brass')
    add(-1.48, 0.120, 0.120, 'brass')
    add(-1.38, 0.120, 0.120, 'brass')
    add(-1.38, 0.120, 0.120, 'wood')
    add(-1.10, 0.110, 0.110, 'wood')
    add(-1.06, 0.108, 0.108, 'wood')
    add(-1.06, 0.124, 0.124, 'brass')
    add(-1.00, 0.124, 0.124, 'brass')
    add(-1.00, 0.104, 0.104, 'wood')
    # the loom, slimmer toward the blade
    add(-0.40, 0.098, 0.098, 'wood')
    add(-0.16, 0.098, 0.098, 'wood')
    # collar where the oar passes the port
    add(-0.16, 0.150, 0.150, 'brass')
    add(0.16, 0.150, 0.150, 'brass')
    add(0.16, 0.098, 0.098, 'wood')
    add(1.40, 0.086, 0.086, 'wood')
    add(2.55, 0.076, 0.076, 'wood')
    # brass band where the loom meets the blade
    add(2.55, 0.106, 0.106, 'brass')
    add(2.66, 0.106, 0.106, 'brass')
    # the blade: a leaf, thickest along its middle. Oak up to the widest point, the hull's dark scales beyond it.
    add(2.66, 0.090, 0.082, 'wood')
    for x, w, t in [(2.82, 0.200, 0.090), (3.00, 0.290, 0.100), (3.22, 0.340, 0.100)]:
        add(x, w, t, 'wood')
    add(3.22, 0.340, 0.100, 'scale')
    for x, w, t in [(3.44, 0.325, 0.086), (3.62, 0.275, 0.060)]:
        add(x, w, t, 'scale')
    # brass tip
    add(3.62, 0.285, 0.070, 'brass')
    add(3.72, 0.220, 0.056, 'brass')
    add(3.78, 0.120, 0.038, 'brass')
    add(3.80, 0.000, 0.000, 'brass')
    return s


def longship_stations():
    """A plain oar for a ship with a 0.13 m port: slimmer loom, rope-wrapped at the port, a narrow blade."""
    s = []

    def add(x, w, t, zone):
        s.append((x, w, t, zone))

    add(-1.10, 0.000, 0.000, 'wood')
    add(-1.09, 0.050, 0.050, 'wood')
    add(-1.03, 0.088, 0.088, 'wood')
    add(-0.78, 0.088, 0.088, 'wood')
    add(-0.50, 0.072, 0.072, 'wood')
    add(-0.12, 0.070, 0.070, 'wood')
    # rope wrap where the oar rides in the port
    add(-0.12, 0.098, 0.098, 'dark')
    add(0.12, 0.098, 0.098, 'dark')
    add(0.12, 0.070, 0.070, 'wood')
    add(1.00, 0.066, 0.066, 'wood')
    add(1.80, 0.060, 0.060, 'wood')
    # the blade: a narrow leaf, thickest along its middle
    add(1.80, 0.075, 0.050, 'plank')
    for x, w, t in [(1.95, 0.140, 0.056), (2.15, 0.210, 0.062), (2.35, 0.230, 0.062), (2.55, 0.200, 0.048), (2.68, 0.150, 0.036),
                    (2.74, 0.075, 0.022)]:
        add(x, w, t, 'plank')
    add(2.76, 0.000, 0.000, 'plank')
    return s


def karve_stations():
    """A plain oar for the Karve: a rope-wrapped collar where it rides in the oarlock, a short leaf blade."""
    s = []

    def add(x, w, t, zone):
        s.append((x, w, t, zone))

    add(-0.95, 0.000, 0.000, 'wood')
    add(-0.94, 0.056, 0.056, 'wood')
    add(-0.88, 0.096, 0.096, 'wood')
    add(-0.62, 0.096, 0.096, 'wood')
    add(-0.38, 0.078, 0.078, 'wood')
    add(-0.12, 0.076, 0.076, 'wood')
    # rope wrap where the oar rides between the pins
    add(-0.12, 0.104, 0.104, 'dark')
    add(0.12, 0.104, 0.104, 'dark')
    add(0.12, 0.076, 0.076, 'wood')
    add(1.20, 0.072, 0.072, 'wood')
    add(2.00, 0.066, 0.066, 'wood')
    # the blade: a short leaf, thickest along its middle
    add(2.00, 0.080, 0.054, 'plank')
    for x, w, t in [(2.12, 0.150, 0.060), (2.30, 0.220, 0.066), (2.48, 0.240, 0.066), (2.64, 0.210, 0.052), (2.76, 0.155, 0.038),
                    (2.82, 0.080, 0.024)]:
        add(x, w, t, 'plank')
    add(2.84, 0.000, 0.000, 'plank')
    return s


def karve_oarlock():
    """The wooden oarlock on the gunwale, centred on the oar's pivot: a slim round bar along the rim with two tapered pins set in it.
    The mod tilts it to follow the hull side (the wall leans in, the rim runs in toward the bow and climbs)."""
    parts = []   # ('cylz', centre, (radius, length)) a bar along the ship; ('cyl', centre, (bottom radius, height, top radius)) a pin
    parts.append(('cylz', (0.0, -0.140, 0.0), (0.030, 0.36)))
    for z in (-0.095, 0.095):
        parts.append(('cyl', (0.0, -0.010, z), (0.027, 0.26, 0.013)))
    return parts


STYLES = {
    'drakkar': (drakkar_stations, DRAKKAR_RECT, 'oar_drakkar.obj'),
    'longship': (longship_stations, LONGSHIP_RECT, 'oar_longship.obj'),
    'karve': (karve_stations, KARVE_RECT, 'oar_karve.obj'),
}

FITTINGS = {
    'oarlock_karve': (karve_oarlock, KARVE_RECT['wood'], 'oarlock_karve.obj'),
}


def lerp(a, b, t):
    return a + (b - a) * t


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def norm(v):
    l = math.sqrt(dot(v, v)) or 1.0
    return (v[0] / l, v[1] / l, v[2] / l)


def build(style):
    stations, RECT, _ = STYLES[style]
    st = stations()
    span = {}
    for x, w, t, zone in st:
        lo, hi = span.get(zone, (x, x))
        span[zone] = (min(lo, x), max(hi, x))

    V, VT, VN, F = [], [], [], []
    ring_first = []   # index of each ring's first vertex (0-based)

    for i, (x, w, t, zone) in enumerate(st):
        # semi-axes slope toward the neighbouring rings of the same zone, for the normals
        a, b = 0.5 * w, 0.5 * t
        prev = st[i - 1] if i > 0 and st[i - 1][3] == zone and st[i - 1][0] != x else None
        nxt = st[i + 1] if i + 1 < len(st) and st[i + 1][3] == zone and st[i + 1][0] != x else None
        lo = prev or st[i]
        hi = nxt or st[i]
        dx = hi[0] - lo[0]
        da = 0.5 * (hi[1] - lo[1]) / dx if dx else 0.0
        db = 0.5 * (hi[2] - lo[2]) / dx if dx else 0.0
        ring_first.append(len(V))
        for j in range(SEG + 1):
            th = 2 * math.pi * j / SEG
            c, s = math.cos(th), math.sin(th)
            V.append((x, a * c, b * s))
            rect = RECT[zone]
            lo_x, hi_x = span[zone]
            fu = (x - lo_x) / (hi_x - lo_x) if hi_x > lo_x else 0.5
            fv = j / SEG
            if zone == 'scale':
                fv = 0.5 + 0.5 * s    # both faces of the blade show the same strip of scales
            VT.append((lerp(rect[0], rect[2], fu), lerp(rect[1], rect[3], fv)))
            if a > 1e-6 and b > 1e-6:
                n = norm((-2 * (c * c * da / a + s * s * db / b), 2 * c / a, 2 * s / b))
            else:
                n = (1.0 if i > 0 else -1.0, 0.0, 0.0)
            VN.append(n)

    def emit(i0, i1, i2, want):
        p0, p1, p2 = V[i0], V[i1], V[i2]
        n = cross(sub(p1, p0), sub(p2, p0))
        if dot(n, n) < 1e-12:
            return   # a pole: nothing to draw
        if dot(n, want) < 0:
            i1, i2 = i2, i1
        F.append((i0 + 1, i1 + 1, i2 + 1))

    for i in range(len(st) - 1):
        a0, b0 = ring_first[i], ring_first[i + 1]
        same_x = st[i][0] == st[i + 1][0]
        for j in range(SEG):
            q = (a0 + j, b0 + j, b0 + j + 1, a0 + j + 1)
            if same_x:
                # a band's edge: the face looks toward the smaller ring's side, away from the bigger one
                grows = (st[i + 1][1] + st[i + 1][2]) > (st[i][1] + st[i][2])
                want = (-1.0 if grows else 1.0, 0.0, 0.0)
                wants = (want, want)
            else:
                cy = sum(V[k][1] for k in q) / 4
                cz = sum(V[k][2] for k in q) / 4
                want = (0.0, cy, cz)
                wants = (want, want)
            emit(q[0], q[1], q[2], wants[0])
            emit(q[0], q[2], q[3], wants[1])
    return V, VT, VN, F


def write(style, path):
    V, VT, VN, F = build(style)
    with open(path, 'w', newline='\n') as o:
        o.write('# HeaveHo %s oar (tools/make_oars.py). No mtl: it is drawn with the hull material.\no Oar\n')
        for p in V:
            o.write('v %.5f %.5f %.5f\n' % p)
        for t in VT:
            o.write('vt %.5f %.5f\n' % t)
        for n in VN:
            o.write('vn %.5f %.5f %.5f\n' % n)
        for a, b, c in F:
            o.write('f %d/%d/%d %d/%d/%d %d/%d/%d\n' % (a, a, a, b, b, b, c, c, c))
    return V, F


def build_fitting(name):
    parts_fn, rect, _ = FITTINGS[name]
    V, VT, VN, F = [], [], [], []
    u0, v0, u1, v1 = rect
    uv = ((u0 + u1) / 2, (v0 + v1) / 2)    # a plain colour: every vertex at the middle of the rectangle

    def face(corners, centre_of_part):
        n = cross(sub(corners[1], corners[0]), sub(corners[2], corners[0]))
        mid = tuple(sum(c[k] for c in corners) / len(corners) for k in range(3))
        if dot(n, sub(mid, centre_of_part)) < 0:
            corners = corners[::-1]
            n = cross(sub(corners[1], corners[0]), sub(corners[2], corners[0]))
        n = norm(n)
        base = len(V)
        for c in corners:
            V.append(c); VT.append(uv); VN.append(n)
        for k in range(1, len(corners) - 1):
            F.append((base + 1, base + k + 1, base + k + 2))

    for kind, c, size in parts_fn():
        if kind == 'box':
            hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
            p = [(c[0] + sx * hx, c[1] + sy * hy, c[2] + sz * hz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
            def P(sx, sy, sz): return (c[0] + sx * hx, c[1] + sy * hy, c[2] + sz * hz)
            for a, b in ((0, 1), (1, 0)):
                pass
            for sx in (-1, 1):
                face([P(sx, -1, -1), P(sx, 1, -1), P(sx, 1, 1), P(sx, -1, 1)], c)
            for sy in (-1, 1):
                face([P(-1, sy, -1), P(1, sy, -1), P(1, sy, 1), P(-1, sy, 1)], c)
            for sz in (-1, 1):
                face([P(-1, -1, sz), P(1, -1, sz), P(1, 1, sz), P(-1, 1, sz)], c)
        elif kind == 'cylz':
            r, ln = size
            n = 10
            def Z(a, z): return (c[0] + r * math.cos(a), c[1] + r * math.sin(a), c[2] + z)
            for i in range(n):
                a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
                face([Z(a0, -ln / 2), Z(a1, -ln / 2), Z(a1, ln / 2), Z(a0, ln / 2)], c)
                face([(c[0], c[1], c[2] + ln / 2), Z(a1, ln / 2), Z(a0, ln / 2)], c)
                face([(c[0], c[1], c[2] - ln / 2), Z(a0, -ln / 2), Z(a1, -ln / 2)], c)
        else:
            rb, h, rt = size
            n = 10
            def Q(a, y, r): return (c[0] + r * math.cos(a), c[1] + y, c[2] + r * math.sin(a))
            for i in range(n):
                a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
                face([Q(a0, -h / 2, rb), Q(a0, h / 2, rt), Q(a1, h / 2, rt), Q(a1, -h / 2, rb)], c)
                face([(c[0], c[1] + h / 2, c[2]), Q(a0, h / 2, rt), Q(a1, h / 2, rt)], c)
    return V, VT, VN, F


def write_fitting(name, path):
    V, VT, VN, F = build_fitting(name)
    with open(path, 'w', newline='\n') as o:
        o.write('# HeaveHo %s (tools/make_oars.py). No mtl: it is drawn with the hull material.\no Fitting\n' % name)
        for p in V:
            o.write('v %.5f %.5f %.5f\n' % p)
        for t in VT:
            o.write('vt %.5f %.5f\n' % t)
        for n in VN:
            o.write('vn %.5f %.5f %.5f\n' % n)
        for a, b, c in F:
            o.write('f %d/%d/%d %d/%d/%d %d/%d/%d\n' % (a, a, a, b, b, b, c, c, c))
    return V, F


if __name__ == '__main__':
    assets = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets')
    names = sys.argv[1:] or list(STYLES) + list(FITTINGS)
    for name in names:
        if name in STYLES:
            out = os.path.join(assets, STYLES[name][2])
            V, F = write(name, out)
        else:
            out = os.path.join(assets, FITTINGS[name][2])
            V, F = write_fitting(name, out)
        xs = [p[0] for p in V]
        print('wrote', os.path.normpath(out), len(V), 'verts', len(F), 'tris, x from', min(xs), 'to', max(xs))
