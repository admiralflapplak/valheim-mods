"""
Turns the skald's lyre (an OBJ with its texture, made by the user with Meshy) into assets/skald_lyre.bin and assets/skald_lyre.png,
which Good Tidings embeds and builds as a mesh in the game (SkaldLook.Lyre).

Run with Blender in the background (nothing opens):
    blender -b --factory-startup -P tools/convert_lyre.py -- <model.obj> <out.bin> <out.png>

What it adds: a fingerboard behind the strings, from the soundbox up to the crossbar (the model has open air there), its wood taken
from a spot on the soundbox's face in the texture.

What it writes (little-endian):
    "LYR1"; int vertices; per vertex: position xyz, normal xyz, uv; int indices; per index: int
Positions are Unity's, in metres: x right, y up the neck, z out of the strings (the model's front, Blender's -y, faces +z), the origin at
the soundbox's middle. Mirrored in x to go from Blender's right-handed axes to Unity's left-handed ones, so each triangle's corners are
written in the opposite order (Unity's winding).
"""
import bpy, bmesh, struct, sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
source, out_bin, out_png = args[0], args[1], args[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=source)
obj = [o for o in bpy.data.objects if o.type == "MESH"][0]
mesh = obj.data
mesh.transform(obj.matrix_world)
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
uv_layer = bm.loops.layers.uv.active

pts = [v.co for v in bm.verts]
zmin, zmax = min(p.z for p in pts), max(p.z for p in pts)

# the strings: the front-most thin geometry over the middle, between the soundbox and the crossbar (measured from the renders:
# strings within 4 cm of the middle; the soundbox's top at about z -0.165, the crossbar's underside at about z 0.248)
board_bottom, board_top = -0.17, 0.25
middle = [p for p in pts if abs(p.x) < 0.04 and board_bottom + 0.03 < p.z < board_top - 0.03]
strings_y = min(p.y for p in middle) if middle else -0.02
print("STRINGS at y", round(strings_y, 4), "from", len(middle), "points")

# a wood spot for the board: the uv of the soundbox face's corner nearest a point on its face, left of the sound hole
target = Vector((-0.1, -0.06, -0.3))
best = None
for f in bm.faces:
    for loop in f.loops:
        d = (loop.vert.co - target).length
        if best is None or d < best[0]:
            best = (d, loop[uv_layer].uv.copy())
wood_uv = best[1]

verts = []   # (position, normal, uv), in Blender's axes for now

def add_triangle(a, b, c, n, uvs):
    for p, uv in zip((a, b, c), uvs):
        verts.append((p.copy(), n.copy(), uv.copy()))

for f in bm.faces:
    corners = [(l.vert.co, l.vert.normal if f.smooth else f.normal, l[uv_layer].uv) for l in f.loops]
    for p, n, uv in corners:
        verts.append((p.copy(), n.copy(), uv.copy()))

# the fingerboard: a box behind the strings (a little further back than them, from the soundbox to the crossbar)
w, depth = 0.04, 0.014
y0 = strings_y + 0.006
y1 = y0 + depth
x0, x1, z0, z1 = -w, w, board_bottom, board_top
corners = {
    "000": Vector((x0, y0, z0)), "100": Vector((x1, y0, z0)), "010": Vector((x0, y1, z0)), "110": Vector((x1, y1, z0)),
    "001": Vector((x0, y0, z1)), "101": Vector((x1, y0, z1)), "011": Vector((x0, y1, z1)), "111": Vector((x1, y1, z1)),
}
quads = [
    (("000", "100", "101", "001"), Vector((0, -1, 0))),   # front (toward the strings)
    (("110", "010", "011", "111"), Vector((0, 1, 0))),    # back
    (("010", "000", "001", "011"), Vector((-1, 0, 0))),
    (("100", "110", "111", "101"), Vector((1, 0, 0))),
    (("001", "101", "111", "011"), Vector((0, 0, 1))),
    (("010", "110", "100", "000"), Vector((0, 0, -1))),
]
uv = [wood_uv, wood_uv + Vector((0.01, 0)), wood_uv + Vector((0.01, 0.03)), wood_uv + Vector((0, 0.03))]
for (a, b, c, d), n in quads:
    pa, pb, pc, pd = (corners[k] for k in (a, b, c, d))
    add_triangle(pa, pb, pc, n, (uv[0], uv[1], uv[2]))
    add_triangle(pa, pc, pd, n, (uv[0], uv[2], uv[3]))

# origin at the soundbox's middle (it spans the bottom of the model up to the board's start)
origin = Vector((0.0, 0.0, (zmin + board_bottom) / 2))

def to_unity(v):   # Blender x right, -y front, z up  ->  Unity x right (mirrored), z front, y up
    return (-v.x, v.z, -v.y)

with open(out_bin, "wb") as f:
    f.write(b"LYR1")
    f.write(struct.pack("<i", len(verts)))
    for p, n, t in verts:
        f.write(struct.pack("<3f", *to_unity(p - origin)))
        f.write(struct.pack("<3f", *to_unity(n)))
        f.write(struct.pack("<2f", t.x, t.y))
    f.write(struct.pack("<i", len(verts)))
    for i in range(0, len(verts), 3):   # each triangle's corners the other way round (the mirror)
        f.write(struct.pack("<3i", i, i + 2, i + 1))
print("WROTE", len(verts), "vertices; origin z", round(origin.z, 3))

img = bpy.data.images[0]
img.scale(1024, 1024)
img.filepath_raw = out_png
img.file_format = "PNG"
img.save()
print("TEXTURE", out_png)
