# rigfix.py: re-place the joints and rebuild the skin weights of a chibi player rigged with the 22-bone Humanoid
# skeleton (Hips ... RightToes, Unity names), from a T-pose FBX. The weights are built from the rest shape (arms out
# along X, legs down Z, model faces -Y, its left is +X), so they are smooth, symmetric and the same on every model;
# only the helmet keeps its old rigid Head weights. Every mesh-specific number is in a JSON file of parameters.
#   blender-python rigfix.py -- <in.fbx> <params.json> <out.fbx>
import bpy, sys, json, numpy as np
from mathutils import Vector

a = sys.argv[sys.argv.index('--') + 1:]
FBX_IN, PARAMS, FBX_OUT = a[0], a[1], a[2]
P = json.load(open(PARAMS))
J = P['joints']   # metres, the model's left side (+X); the right side mirrors

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX_IN)
ob = next(o for o in bpy.data.objects if o.type == 'MESH')
ao = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = ob.data
co = np.array([ob.matrix_world @ v.co for v in me.vertices])
nv = len(co)

# ---- 1. joints ----
# "jointsFromRig": leave the skeleton where it is (a model already tuned and given finger bones) and read the joints
# the weights blend at from its bones; the JSON then gives only what bones don't say (armpit, torso edge, pad top)
FROM_RIG = P.get('jointsFromRig', False)
if FROM_RIG:
    hl = {b.name: ao.matrix_world @ b.head_local for b in ao.data.bones}
    for k, (bn, ax_) in {'hipZ': ('Hips', 2), 'spineZ': ('Spine', 2), 'chestZ': ('Chest', 2),
                         'upperChestZ': ('UpperChest', 2), 'neckZ': ('Neck', 2), 'clavicleX': ('LeftShoulder', 0),
                         'shoulderX': ('LeftUpperArm', 0), 'elbowX': ('LeftLowerArm', 0), 'wristX': ('LeftHand', 0),
                         'kneeZ': ('LeftLowerLeg', 2), 'ankleZ': ('LeftFoot', 2), 'toeY': ('LeftToes', 1)}.items():
        J[k] = float(hl[bn][ax_])
    print('RIGFIX joints from the rig', {k: round(v, 3) for k, v in J.items()})
def V(*p): return Vector(p)
ay, sy = J.get('armY', 0.02), J['shoulderX']
B = {
    'Hips': (V(0, J['spineY'], J['hipZ']), V(0, J['spineY'], J['spineZ'])),
    'Spine': (V(0, J['spineY'], J['spineZ']), V(0, J['spineY'], J['chestZ'])),
    'Chest': (V(0, J['spineY'], J['chestZ']), V(0, J['spineY'], J['upperChestZ'])),
    'UpperChest': (V(0, J['spineY'], J['upperChestZ']), V(0, J['spineY'], J['neckZ'])),
    'Neck': (V(0, J['spineY'], J['neckZ']), V(0, 0, J['headZ'])),
    'Head': (V(0, 0, J['headZ']), V(0, 0, J['headTopZ'])),
}
for side, s in (('Left', 1), ('Right', -1)):
    B.update({
        side + 'Shoulder': (V(s * J['clavicleX'], ay, J['armZ']), V(s * J['shoulderX'], ay, J['armZ'])),
        side + 'UpperArm': (V(s * J['shoulderX'], ay, J['armZ']), V(s * J['elbowX'], ay, J['armZ'])),
        side + 'LowerArm': (V(s * J['elbowX'], ay, J['armZ']), V(s * J['wristX'], ay, J['armZ'])),
        side + 'Hand': (V(s * J['wristX'], ay, J['armZ']), V(s * J['handTipX'], ay, J['armZ'])),
        side + 'UpperLeg': (V(s * J['hipX'], J['legY'], J['hipZ'] - J['hipDrop']), V(s * J['kneeX'], J['legY'], J['kneeZ'])),
        side + 'LowerLeg': (V(s * J['kneeX'], J['legY'], J['kneeZ']), V(s * J['ankleX'], J['ankleY'], J['ankleZ'])),
        side + 'Foot': (V(s * J['ankleX'], J['ankleY'], J['ankleZ']), V(s * J['ankleX'], J['toeY'], J['toeZ'])),
        side + 'Toes': (V(s * J['ankleX'], J['toeY'], J['toeZ']), V(s * J['ankleX'], J['toeTipY'], J['toeZ'])),
    })
bpy.context.view_layer.objects.active = ao
bpy.ops.object.mode_set(mode='EDIT')
inv = ao.matrix_world.inverted()
eb = ao.data.edit_bones
for b in eb: b.use_connect = False
for n, (h, t) in ({} if FROM_RIG else B).items():
    e = eb[n]; e.head = inv @ h; e.tail = inv @ t
for n in ({} if FROM_RIG else B):
    e = eb[n]
    e.align_roll(inv.to_3x3() @ (Vector((0, 0, 1)) if ('Arm' in n or 'Hand' in n or 'Shoulder' in n) else Vector((0, -1, 0))))
bpy.ops.object.mode_set(mode='OBJECT')

# ---- 2. weights ----
def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def chain(x, cuts, widths):
    """weights for len(cuts)+1 segments along x, each cut blended over +-width"""
    w = []; prev = np.ones_like(x)
    for c, h in zip(cuts, widths):
        past = ss(c - h, c + h, x)
        w.append(prev - past); prev = past
    w.append(prev)
    return w

W = {n: np.zeros(nv) for n in B}
x, y, z = co[:, 0], co[:, 1], co[:, 2]
ax = np.abs(x)
old_head = np.zeros(nv)
hg = ob.vertex_groups['Head'].index
for v in me.vertices:
    for g in v.groups:
        if g.group == hg: old_head[v.index] = g.weight
rigid = old_head > P.get('headKeep', 0.5)
# "keep": bones whose vertices keep all their old weights (a re-run after handfix.py keeps the hands and fingers)
old_names = [g.name for g in ob.vertex_groups]
OLD = np.zeros((nv, len(old_names)))
for v in me.vertices:
    for g in v.groups: OLD[v.index, g.group] = g.weight
keep = np.zeros(nv, bool)
for j, n in enumerate(old_names):
    if any(n.endswith(k) for k in P.get('keep', [])): keep |= OLD[:, j] > 1e-4

bw = P['blend']
# legs: legness fades in below the hip line; the crotch splits between the legs
front = ss(0.02, -0.06, y)   # 1 on the front of the thigh (the pads), 0 on the seat
below = bw['hipBelow'] + (bw.get('hipFrontBelow', bw['hipBelow']) - bw['hipBelow']) * front
leg = 1 - ss(J['hipZ'] - below, J['hipZ'] + bw['hipAbove'], z)
# arms: armness grows out from the clavicle; under the armpit the torso side stays torso
gate = np.maximum(ss(J['armpitZ'] - bw['armpit'], J['armpitZ'] + bw['armpit'], z), ss(J['torsoEdgeX'] - 0.03, J['torsoEdgeX'] + 0.03, ax))
arm = ss(J['clavicleX'] + bw['clavIn'], J['clavicleX'] + bw['clavOut'], ax) * gate * (z > J['armpitZ'] - 0.25)
arm = arm * (1 - leg)
torso = 1 - leg - arm
# torso chain by height
tw = chain(z, [J['spineZ'], J['chestZ'], J['upperChestZ'], J['neckZ']], [bw['spine']] * 4)
for n, w in zip(['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck'], tw): W[n] += torso * w
for side, s in (('Left', 1), ('Right', -1)):
    mine = ss(-0.02, 0.02, s * x)
    # arm chain along |x|; the shoulder pad's top keeps more of the Shoulder (it rides the clavicle, not the arm)
    pad = ss(J['padZ'] - 0.05, J['padZ'] + 0.05, z) * bw['padShift']
    aw = chain(ax - pad, [J['shoulderX'], J['elbowX'], J['wristX']], [bw['shoulder'], bw['elbow'], bw['wrist']])
    for n, w in zip(['Shoulder', 'UpperArm', 'LowerArm', 'Hand'], aw): W[side + n] += arm * mine * w
    # leg chain by height, then the toes forward of the ball of the foot
    lw = chain(-z, [-J['kneeZ'], -J['ankleZ']], [bw['knee'], bw['ankle']])
    toes = ss(J['toeY'] + bw['toe'], J['toeY'] - bw['toe'], y) * (z < J['ankleZ'])
    W[side + 'UpperLeg'] += leg * mine * lw[0]
    W[side + 'LowerLeg'] += leg * mine * lw[1]
    W[side + 'Foot'] += leg * mine * lw[2] * (1 - toes)
    W[side + 'Toes'] += leg * mine * lw[2] * toes
    # the top of the thigh shares with the hips so the buttocks/crotch don't tear
for n in W: W[n][rigid] = 0
W['Head'][rigid] = 1
# the neck inside the helmet goes with the head
W['Head'] += W['Neck'] * P.get('neckToHead', 0.5); W['Neck'] *= 1 - P.get('neckToHead', 0.5)

# spatial smoothing (loose shells next to each other move together), not across the rigid helmet
k = P.get('smoothRadius', 0.0)
if k > 0:
    from mathutils.kdtree import KDTree
    kd = KDTree(nv)
    for i, p in enumerate(co): kd.insert(p, i)
    kd.balance()
    names = list(W); M = np.stack([W[n] for n in names], 1)
    for it in range(P.get('smoothIters', 1)):
        M2 = M.copy()
        for i in np.where(~rigid)[0]:
            nb = kd.find_range(co[i], k)
            idx = np.array([j for (_, j, d) in nb if not rigid[j]]); d = np.array([d for (_, j, d) in nb if not rigid[j]])
            if len(idx) == 0: continue
            wt = np.exp(-(d / (k * 0.5)) ** 2)
            M2[i] = (M[idx] * wt[:, None]).sum(0) / wt.sum()
        M = M2
    for j, n in enumerate(names): W[n] = M[:, j]

# write: top 4, normalised
for n in old_names: W.setdefault(n, np.zeros(nv))
names = list(W); M = np.stack([W[n] for n in names], 1)
# "newAbove": [z0, z1]: only the body above z1 takes the new weights, below z0 keeps its old ones, blended between
if 'newAbove' in P:
    t = ss(P['newAbove'][0], P['newAbove'][1], z)[:, None]
    O = np.zeros_like(M)
    for j, n in enumerate(old_names): O[:, names.index(n)] = OLD[:, j]
    M = t * M + (1 - t) * O
M[keep] = 0
for j, n in enumerate(old_names): M[keep, names.index(n)] = OLD[keep, j]
M[M < 1e-3] = 0
order = np.argsort(-M, 1)
for i in range(nv): M[i, order[i, 4:]] = 0
M /= np.maximum(M.sum(1, keepdims=True), 1e-9)
for vg in list(ob.vertex_groups): ob.vertex_groups.remove(vg)
for j, n in enumerate(names):
    vg = ob.vertex_groups.new(name=n)
    nz = np.where(M[:, j] > 0)[0]
    for i in nz: vg.add([int(i)], float(M[i, j]), 'REPLACE')
print('RIGFIX unweighted', int((M.sum(1) == 0).sum()), 'rigid head', int(rigid.sum()), 'kept', int(keep.sum()))

# ---- 3. export (as the original: Unity axes, Humanoid names, no leaf bones, texture not embedded) ----
bpy.ops.export_scene.fbx(filepath=FBX_OUT, use_selection=False, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    use_armature_deform_only=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False,
    mesh_smooth_type='FACE', primary_bone_axis='Y', secondary_bone_axis='X')
