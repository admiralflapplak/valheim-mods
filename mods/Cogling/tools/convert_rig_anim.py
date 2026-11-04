"""
Turns one animation of another humanoid rig (an FBX holding several, e.g. reference/RobotAnime/Rig_Medium/Rig_Medium_Simulation.fbx)
into assets/<name>.anim in the "ANI2" form: the clip WITH its own skeleton, so Cogling can retarget it at run time through Unity's
humanoid muscles (CoglingClip / ClipPlayer: the skeleton is described to Unity as a humanoid from its T-pose, the clip plays on a hidden
copy of it, and its humanoid pose goes onto his body). Bones are matched to Unity's humanoid by name in CoglingClip.RigHuman.

Run with Blender in the background (nothing opens):
    blender -b --factory-startup -P tools/convert_rig_anim.py -- <file.fbx> <action> <T-pose action, or "rest"> <abs path>/assets/<name>.anim

What it writes (little-endian):
    "ANI2"; float fps; int frames; int bones;
    per bone: name (int byte count, UTF-8), int parent (-1: none), rest local position xyz, rest local rotation xyzw (from the T-pose)
    per frame, per bone: local position xyz, local rotation xyzw
Axes and units as tools/convert_robot.py writes the model (Unity's: x' = -x, y' = z, z' = -y; metres), positions with the lowest bone
of the T-pose on y = 0.
"""
import bpy, struct, sys
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
source, action_name, tpose_name, out = args[0], args[1], args[2], args[3]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source)
armature = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base

# bones joined to their parent ignore their keyed movement in Blender (the importer joins the hips to the root, and the hips' sitting
# down, all of it movement, was lost): unjoined, as Unity plays it
bpy.context.view_layer.objects.active = armature
bpy.ops.object.mode_set(mode='EDIT')
for eb in armature.data.edit_bones:
    eb.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')


def find_action(name):
    for a in bpy.data.actions:
        if a.name == name or a.name.split("|")[-1] == name:
            return a
    raise SystemExit(f"no action {name} in {source}: " + ", ".join(a.name for a in bpy.data.actions))


def use(action):
    ad = armature.animation_data or armature.animation_data_create()
    ad.action = action
    if hasattr(ad, "action_slot") and getattr(action, "slots", None):   # (Blender 4.4+: an action's slot)
        ad.action_slot = action.slots[0]


C = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
CI = C.inverted()
bones = list(armature.pose.bones)
index = {b.name: i for i, b in enumerate(bones)}


def world():
    """Each bone's position and rotation in Unity's axes, this frame."""
    out = []
    for b in bones:
        m = C @ (armature.matrix_world @ b.matrix) @ CI
        out.append((m.to_translation(), m.to_3x3().normalized().to_quaternion()))
    return out


def local(w, i, lowest):
    pos, rot = w[i]
    p = bones[i].parent
    if p is None:
        return pos - type(pos)((0, lowest, 0)), rot
    ppos, prot = w[index[p.name]]
    inv = prot.inverted()
    return inv @ (pos - ppos), inv @ rot


# the rest: the T-pose action's first frame (Unity describes a humanoid from a T-pose)
if tpose_name == "rest":
    # no T-pose action (the Social pack: one clip a file): the skeleton's own rest pose, a T-pose there
    ad = armature.animation_data or armature.animation_data_create()
    ad.action = None
    for pb in armature.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
else:
    tpose = find_action(tpose_name)
    use(tpose)
    bpy.context.scene.frame_set(int(tpose.frame_range[0]))
bpy.context.view_layer.update()
rest = world()
lowest = min(p.y for p, _ in rest)

data = bytearray(b"ANI2")
action = find_action(action_name)
start, end = int(action.frame_range[0]), int(action.frame_range[1])
data += struct.pack("<fii", fps, end - start + 1, len(bones))
for i, b in enumerate(bones):
    n = b.name.encode("utf-8")
    data += struct.pack("<i", len(n)) + n
    data += struct.pack("<i", index[b.parent.name] if b.parent else -1)
    lp, lr = local(rest, i, lowest)
    data += struct.pack("<3f4f", lp.x, lp.y, lp.z, lr.x, lr.y, lr.z, lr.w)

use(action)
for f in range(start, end + 1):
    bpy.context.scene.frame_set(f)
    w = world()
    for i in range(len(bones)):
        lp, lr = local(w, i, lowest)
        data += struct.pack("<3f4f", lp.x, lp.y, lp.z, lr.x, lr.y, lr.z, lr.w)

with open(out, "wb") as fh:
    fh.write(data)
print(f"ANI2: {action.name}, {len(bones)} bones, {end - start + 1} frames at {fps:.0f} fps, {len(data)} bytes")
