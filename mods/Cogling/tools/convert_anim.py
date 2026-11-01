"""
Turns a Mixamo animation of the Cogling's skeleton (an FBX "without skin", e.g. reference/Idle.fbx) into assets/<name>.anim, which
Cogling embeds and plays on his robot skeleton (CoglingClip.cs), blended with the game's own animations.

Run with Blender in the background (nothing opens):
    blender -b --factory-startup -P tools/convert_anim.py -- <animation.fbx> <abs path>/assets/<name>.anim

What it writes (little-endian):
    "ANI1"; float fps; int frames; int bones; per bone: name (int byte count, UTF-8)
    per frame: hips position xyz, then per bone: local rotation xyzw
Axes and units as tools/convert_robot.py writes the model (Unity's: x' = -x, y' = z, z' = -y; metres; feet on y = 0 in the rest pose),
and each local rotation is relative to the parent in the same frame (the same change of axes applied to each bone's whole matrix), so
a rotation fits the robot's own bones when the rigs match (both came from Mixamo for the same model).
"""
import bpy, struct, sys
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
source, out = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source)
armature = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
action = armature.animation_data.action if armature.animation_data else None
if action is None:
    raise SystemExit("no animation in " + source)
start, end = int(action.frame_range[0]), int(action.frame_range[1])
fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base

C = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
CI = C.inverted()

# the ground: the lowest bone of the rest pose (as the model's feet on y = 0)
armature.data.pose_position = 'REST'
bpy.context.view_layer.update()
lowest = min((armature.matrix_world @ b.head_local).z for b in armature.data.bones)
armature.data.pose_position = 'POSE'

bones = list(armature.pose.bones)
data = bytearray(b"ANI1")
data += struct.pack("<fii", fps, end - start + 1, len(bones))
for b in bones:
    n = b.name.encode("utf-8")
    data += struct.pack("<i", len(n)) + n

for f in range(start, end + 1):
    bpy.context.scene.frame_set(f)
    world = {}
    for b in bones:
        m = C @ (armature.matrix_world @ b.matrix) @ CI
        world[b.name] = m.to_3x3().normalized().to_quaternion()
    hips = armature.matrix_world @ bones[0].head
    data += struct.pack("<3f", -hips.x, hips.z - lowest, -hips.y)
    for b in bones:
        q = world[b.name]
        if b.parent:
            q = world[b.parent.name].inverted() @ q
        data += struct.pack("<4f", q.x, q.y, q.z, q.w)

with open(out, "wb") as fh:
    fh.write(data)
print(f"ANI: {len(bones)} bones, {end - start + 1} frames at {fps:.0f} fps, {len(data)} bytes; first bone {bones[0].name}")
