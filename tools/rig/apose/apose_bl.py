# apose_bl.py: write the A-pose rest (apose.py's npz) into the FBX with Blender: the bones' new rest, the relaxed skin,
# its normals turned with the arms, weights if the npz has them; exported with the same settings as rigfix.py.
#   python3.11 apose_bl.py -- <in.fbx> <apose.npz> <out.fbx>
import bpy, sys, numpy as np
from mathutils import Matrix
a = sys.argv[sys.argv.index('--') + 1:]
FBX_IN, NPZ, FBX_OUT = a
Z = np.load(NPZ)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX_IN)
ob = next(o for o in bpy.data.objects if o.type == 'MESH'); ao = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = ob.data
# every bone's world turn from its old rest to its new one (D = new @ old^-1), old rest as the FBX had it
old = {n[2:]: None for n in Z.files if n.startswith('M_')}
OLD = {k: np.array(Z['O_' + k]) for k in old}; NEW = {k: np.array(Z['M_' + k]) for k in old}
D = {k: NEW[k] @ np.linalg.inv(OLD[k]) for k in old}
AW = ao.matrix_world; AWi = AW.inverted()
# loop normals, turned per vertex by its weighted bone turns
me.calc_normals_split() if hasattr(me, 'calc_normals_split') else None
ln = np.array([l.normal[:] for l in me.loops])
lv = np.array([l.vertex_index for l in me.loops])
gi = {vg.index: vg.name for vg in ob.vertex_groups}
nv = len(me.vertices); R = np.zeros((nv, 3, 3))
for v in me.vertices:
    for g in v.groups:
        n = gi[g.group]
        if n in D: R[v.index] += g.weight * D[n][:3, :3]
        else: R[v.index] += g.weight * np.eye(3)
ws = np.array([sum(g.weight for g in v.groups) for v in me.vertices]); R[ws == 0] = np.eye(3)
nl = np.einsum('lij,lj->li', R[lv], ln); nl /= np.maximum(np.linalg.norm(nl, axis=1, keepdims=True), 1e-9)
# pose the bones to the new rest and apply it
bpy.context.view_layer.objects.active = ao
bpy.ops.object.mode_set(mode='POSE')
order = []
def visit(b):
    order.append(b.name)
    for c in b.children: visit(c)
for b in ao.data.bones:
    if b.parent is None: visit(b)
for n in order:
    if n not in D: continue
    pb = ao.pose.bones[n]
    Mw = Matrix(D[n].tolist()) @ AW @ ao.data.bones[n].matrix_local
    pb.matrix = AWi @ Mw
    bpy.context.view_layer.update()
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
# the relaxed skin (world -> mesh space), its normals, and the weights if given
Mi = np.array(ob.matrix_world.inverted())
V = np.array(Z['V']); Vl = (Mi[:3, :3] @ V.T).T + Mi[:3, 3]
me.vertices.foreach_set('co', Vl.ravel())
me.update()
if hasattr(me, 'use_auto_smooth'): me.use_auto_smooth = True
me.normals_split_custom_set([tuple((Mi[:3, :3] @ n) / np.linalg.norm(Mi[:3, :3] @ n)) for n in nl])
if 'W' in Z.files:
    W = np.array(Z['W']); names = [str(s) for s in Z['names']]
    for vg in list(ob.vertex_groups): ob.vertex_groups.remove(vg)
    for j, n in enumerate(names):
        vg = ob.vertex_groups.new(name=n)
        for i in np.where(W[:, j] > 0)[0]: vg.add([int(i)], float(W[i, j]), 'REPLACE')
bpy.ops.export_scene.fbx(filepath=FBX_OUT, use_selection=False, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    use_armature_deform_only=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False,
    mesh_smooth_type='FACE', primary_bone_axis='Y', secondary_bone_axis='X')
print('APOSE wrote', FBX_OUT)
