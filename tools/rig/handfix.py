# handfix.py: tidy a chibi player's hands and give them finger bones (Blender as a Python module).
#   python handfix.py -- <in.fbx> <params.json> <Model> <out.fbx>
# The T-pose FBX (arms along X, model faces -Y, its left is +X, Z up; Unity's bone names) keeps its 22 bones and
# gains four per hand: <Side>Thumb > <Side>ThumbTip and <Side>Fingers > <Side>FingersTip (the four fingers move as
# one, children of <Side>Hand). A model with a sixth finger ("extra" in its params) has it pulled back into the palm.
# The hand's old weight is split between the hand and the new bones; nothing else changes.
import bpy, sys, json, numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, __import__('os').path.dirname(__file__))
from fcore import retract, rig

a = sys.argv[sys.argv.index('--') + 1:]
FBX_IN, PARAMS, MODEL, FBX_OUT = a
p = json.load(open(PARAMS))[MODEL]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX_IN)
ob = next(o for o in bpy.data.objects if o.type == 'MESH')
ao = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = ob.data
mw = ob.matrix_world.copy(); mwi = mw.inverted()
co = np.array([mw @ v.co for v in me.vertices])
print('HANDFIX', MODEL, 'verts', len(co), 'bbox', co.min(0).round(3), co.max(0).round(3), 'bones', len(ao.data.bones))

def weights(name):
    w = np.zeros(len(co)); vg = ob.vertex_groups[name]; gi = vg.index
    for v in me.vertices:
        for g in v.groups:
            if g.group == gi: w[v.index] = g.weight
    return w

new = {}; bones = {}
for s, side in ((1, 'Left'), (-1, 'Right')):
    h = weights(side + 'Hand')
    co, e = retract(co, s, p)
    W, nh, B = rig(co, h, s, p, side)
    print('HANDFIX', side, 'extra finger verts', int(e.sum()), 'hand verts', int((h > 0).sum()))
    new[side + 'Hand'] = nh; new.update(W); bones.update(B)

for v in me.vertices: v.co = mwi @ Vector(co[v.index])

# the bones, children of the hands
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = ao; ao.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
ai = ao.matrix_world.inverted(); eb = ao.data.edit_bones
for n in ['LeftThumb', 'LeftThumbTip', 'LeftFingers', 'LeftFingersTip', 'RightThumb', 'RightThumbTip', 'RightFingers', 'RightFingersTip']:
    hd, tl, par = bones[n]
    b = eb.new(n); b.head = ai @ Vector(hd); b.tail = ai @ Vector(tl)
    b.parent = eb[par]; b.use_connect = False
    b.roll = eb[par].roll   # (the hand's own roll, so a curl turns the same way on every finger bone)
bpy.ops.object.mode_set(mode='OBJECT')

for n, w in new.items():
    vg = ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)
    nz = np.where(w > 1e-4)[0]; zero = np.where((w <= 1e-4))[0]
    vg.remove([int(i) for i in zero])
    for i in nz: vg.add([int(i)], float(w[i]), 'REPLACE')
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
print('HANDFIX bones now', len(ao.data.bones))

# export as the original (Unity axes, no leaf bones, texture not embedded), like rigfix.py
bpy.ops.export_scene.fbx(filepath=FBX_OUT, use_selection=False, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    use_armature_deform_only=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False,
    mesh_smooth_type='FACE', primary_bone_axis='Y', secondary_bone_axis='X')
