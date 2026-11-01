"""
Turns the Cogling (the user's Meshy model, rigged by Mixamo: reference/robotguy) into assets/cogling.bin and assets/cogling.png, which
Cogling embeds and builds as a skinned mesh on its own skeleton in the game (CoglingModel.cs, CoglingBody.cs).

Run with Blender in the background (nothing opens):
    blender -b --factory-startup -P tools/convert_robot.py -- <model.fbx> <texture.png> <out.bin> <out.png> [texture size=1024]

What it writes (little-endian):
    "COG1"
    int bones;    per bone:   name, int parent (-1 for none), position xyz, rotation xyzw     the rest pose (a T-pose), in the model's space
    int vertices; per vertex: position xyz, normal xyz, uv, 4 x (int bone, float weight)    weights sorted, summing to 1
    int indices;  per index:  int
    names are an int byte count and UTF-8
Positions are Unity's, in metres: x right, y up, z forward (the model faces +z), feet on y = 0. Blender's axes (z up, the model facing -y)
go to Unity's as x' = -x, y' = z, z' = -y (as GoodTidings' Mixamo data); that mirror turns the handedness round, so each triangle's corners
are written in the opposite order (Unity's winding). Rotations are the same change applied to each bone's whole rest matrix, so they stay
proper rotations. The animation in the FBX (a dance) is ignored: the game's own animations move the Cogling.
"""
import bpy, struct, sys
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
source, texture, out_bin, out_png = args[0], args[1], args[2], args[3]
size = int(args[4]) if len(args) > 4 else 1024

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source)
for o in bpy.data.objects:
    if o.animation_data:
        o.animation_data.action = None
    if o.type == 'ARMATURE':
        o.data.pose_position = 'REST'
bpy.context.view_layer.update()

C = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))   # Blender -> Unity axes (its own inverse's transpose)
CI = C.inverted()

armature = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh_obj = next(o for o in bpy.data.objects if o.type == 'MESH')

# the mesh in world space, evaluated (any modifiers applied), with its corner normals
deps = bpy.context.evaluated_depsgraph_get()
eval_obj = mesh_obj.evaluated_get(deps)
mesh = eval_obj.to_mesh()
mesh.transform(mesh_obj.matrix_world)
mesh.calc_loop_triangles()
uv = mesh.uv_layers.active.data
normals = mesh.corner_normals

lowest = min((v.co.z for v in mesh.vertices))   # feet on the ground


def unity_pos(p):
    return (-p.x, p.z - lowest, -p.y)


def name(s):
    b = s.encode("utf-8")
    return struct.pack("<i", len(b)) + b


data = bytearray(b"COG1")

# bones: world rest matrices, changed to Unity's axes, then made local to the parent
bones = list(armature.data.bones)
index = {b.name: i for i, b in enumerate(bones)}
world = []
for b in bones:
    m = C @ (armature.matrix_world @ b.matrix_local) @ CI
    q = m.to_3x3().normalized().to_quaternion()   # the armature's 0.01 scale out: positions are taken separately below
    p = armature.matrix_world @ b.head_local
    world.append((q, unity_pos(p)))
data += struct.pack("<i", len(bones))
for i, b in enumerate(bones):
    q, p = world[i]
    data += name(b.name) + struct.pack("<i", index[b.parent.name] if b.parent else -1)
    data += struct.pack("<3f", *p) + struct.pack("<4f", q.x, q.y, q.z, q.w)

# vertices: one per distinct (vertex, uv, normal), as Unity needs
groups = {g.index: g.name for g in mesh_obj.vertex_groups}


def weights(v):
    w = [(index[groups[g.group]], g.weight) for g in v.groups if g.weight > 0 and groups.get(g.group) in index]
    w.sort(key=lambda x: -x[1])
    w = w[:4]
    total = sum(x[1] for x in w) or 1.0
    w = [(b, x / total) for b, x in w]
    while len(w) < 4:
        w.append((0, 0.0))
    return w


verts, keys, tris = [], {}, []
for t in mesh.loop_triangles:
    corner = []
    for li in t.loops:
        vi = mesh.loops[li].vertex_index
        u = uv[li].uv
        n = normals[li].vector
        key = (vi, round(u.x, 5), round(u.y, 5), round(n.x, 3), round(n.y, 3), round(n.z, 3))
        if key not in keys:
            keys[key] = len(verts)
            v = mesh.vertices[vi]
            verts.append((unity_pos(v.co), (-n.x, n.z, -n.y), (u.x, u.y), weights(v)))
        corner.append(keys[key])
    tris.extend((corner[0], corner[2], corner[1]))   # mirrored: the other way round

data += struct.pack("<i", len(verts))
for p, n, u, w in verts:
    data += struct.pack("<8f", *p, *n, *u)
    for b, x in w:
        data += struct.pack("<if", b, x)
data += struct.pack("<i", len(tris))
data += struct.pack(f"<{len(tris)}i", *tris)

with open(out_bin, "wb") as f:
    f.write(data)

# the texture, smaller (the game's creatures use 1024 or less)
img = bpy.data.images.load(texture)
if img.size[0] > size:
    img.scale(size, size)
img.filepath_raw = out_png
img.file_format = 'PNG'
img.save()

height = max(p[1] for p, _, _, _ in verts)
print(f"COG: {len(bones)} bones, {len(verts)} vertices, {len(tris) // 3} triangles, height {height:.3f} m, {len(data)} bytes, texture {img.size[0]}px")
