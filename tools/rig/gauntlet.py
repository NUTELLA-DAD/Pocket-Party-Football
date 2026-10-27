# Rig gauntlet harness: poses a rigged FBX exactly the way the game's ModelSkin does (FootballFigure pose angles ->
# figure bone turns -> model bone turns, arms lowered 12 deg, raised arms splayed), renders each pose from the game's
# side-on camera and a 3/4 front view, writes one contact sheet, and prints deformation numbers.
# usage: python gauntlet.py <model.fbx> <texture.png> <outdir> [pose,pose,...]
import bpy, sys, math, json, os, numpy as np
from mathutils import Matrix, Vector, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
FBX, TEX, OUT = argv[0], argv[1], argv[2]
ONLY = argv[3].split(',') if len(argv) > 3 else None
os.makedirs(OUT, exist_ok=True)

# ---- FootballFigure.Get (degrees; drop/tilt as in the C#) ----
K = 'drop tilt lean head armL armLx foreL armR armRx foreR thighL thighLx shinL thighR thighRx shinR'.split()
def P(**kw): return {k: float(kw.get(k, 0)) for k in K}
RUN0 = dict(thighL=46, thighLx=4, shinL=-66, thighR=-26, thighRx=4, shinR=-42)
POSES = {
 'Stand':    P(armL=4, armLx=10, foreL=12, armR=4, armRx=10, foreR=12, thighLx=3, thighRx=3),
 'Ready':    P(drop=13, lean=24, head=-14, armL=25, armLx=18, foreL=45, armR=25, armRx=18, foreR=45, thighL=18, thighLx=9, shinL=-30, thighR=18, thighRx=9, shinR=-30),
 'Stance':   P(drop=23, lean=26, head=-12, armL=50, armLx=48, foreL=42, armR=50, armRx=48, foreR=42, thighL=62, thighLx=30, shinL=-80, thighR=62, thighRx=30, shinR=-80),
 'Block':    P(drop=18, lean=46, head=-32, armL=92, armLx=10, foreL=4, armR=92, armRx=10, foreR=4, thighL=-12, thighLx=10, shinL=-24, thighR=48, thighRx=10, shinR=-55),
 'Throw':    P(drop=4, lean=-6, armL=62, armLx=20, foreL=30, armR=-150, armRx=22, foreR=70, thighL=24, thighLx=6, shinL=-18, thighR=-18, thighRx=8, shinR=-12),
 'Run':      P(drop=5, lean=16, head=-8, armL=-38, armLx=12, foreL=75, armR=45, armRx=12, foreR=75, **RUN0),
 'Carry':    P(drop=6, lean=20, head=-10, armL=30, armLx=8, foreL=100, armR=-30, armRx=12, foreR=70, thighL=48, thighLx=4, shinL=-70, thighR=-28, thighRx=4, shinR=-40),
 'Catch':    P(drop=2, lean=-10, head=18, armL=165, armLx=12, foreL=20, armR=165, armRx=12, foreR=20, thighL=30, thighLx=6, shinL=-50, thighR=-10, thighRx=6, shinR=-20),
 'Release':  P(drop=6, lean=18, head=-8, armL=-30, armLx=20, foreL=40, armR=150, armRx=15, foreR=20, thighL=30, thighLx=6, shinL=-20, thighR=-25, thighRx=8, shinR=-15),
 'Secure':   P(drop=8, lean=20, head=-6, armL=60, armLx=6, foreL=120, armR=50, armRx=8, foreR=110, **RUN0),
 'Dive':     P(drop=-8, lean=70, head=-40, armL=160, armLx=14, foreL=10, armR=160, armRx=14, foreR=10, thighL=-30, thighLx=6, shinL=-30, thighR=-45, thighRx=6, shinR=-10),
 'Juke':     P(drop=18, lean=22, head=-14, armL=50, armLx=55, foreL=20, armR=-20, armRx=12, foreR=90, thighL=30, thighLx=30, shinL=-40, thighR=-10, thighRx=25, shinR=-30),
 'Kneel':    P(drop=30, tilt=15, head=-10, armL=60, armLx=10, foreL=40, armR=30, armRx=14, foreR=60, thighL=80, thighLx=6, shinL=-90, thighR=-10, thighRx=6, shinR=-100),
 'Supine':   P(drop=46, tilt=-88, head=20, armL=-25, armLx=75, foreL=15, armR=-25, armRx=75, foreR=15, thighL=40, thighLx=10, shinL=-90, thighR=30, thighRx=14, shinR=-70),
 'Celebrate':P(drop=0, head=-10, armL=40, armLx=40, foreL=80, armR=170, armRx=15, foreR=10, thighL=4, thighLx=4, shinL=-6, thighR=4, thighRx=4, shinR=-6),
}
BALL = {'Carry', 'Secure', 'Celebrate', 'Throw'}   # poses where the right hand holds the ball

# figure space (Unity: x forward, y up, z = the figure's left) -> Blender (model faces -Y, left is +X, Z up)
C = Matrix(((0, 0, 1), (-1, 0, 0), (0, 1, 0)))
Ci = C.inverted()
def Rz(d): return Matrix.Rotation(math.radians(d), 3, 'Z')
def Rx(d): return Matrix.Rotation(math.radians(d), 3, 'X')
def E(x, z): return Rx(x) @ Rz(z)   # Unity Quaternion.Euler(x, 0, z): z first, then x
def fig_world(p):
    hips = Rz(-p['tilt']); torso = hips @ Rz(-p['lean'])
    return {
        'Hips': hips, 'Spine': torso, 'Head': torso @ Rz(-p['head']),
        'LeftUpperArm': torso @ E(-p['armLx'], p['armL']), 'RightUpperArm': torso @ E(p['armRx'], p['armR']),
        'LeftUpperLeg': hips @ E(-p['thighLx'], p['thighL']), 'RightUpperLeg': hips @ E(p['thighRx'], p['thighR']),
        'LeftLowerLeg': hips @ E(-p['thighLx'], p['thighL']) @ Rz(p['shinL']),
        'RightLowerLeg': hips @ E(p['thighRx'], p['thighR']) @ Rz(p['shinR']),
        'LeftLowerArm': torso @ E(-p['armLx'], p['armL']) @ Rz(p['foreL']),
        'RightLowerArm': torso @ E(p['armRx'], p['armR']) @ Rz(p['foreR']),
    }
# the figure's own joints (cm) at rest, for the splay's arm direction
SH, ELB, ARMZ = 108.0, 86.0, 25.5 * 1.12
MAPPED = ['Hips', 'Spine', 'Head', 'LeftUpperArm', 'LeftLowerArm', 'RightUpperArm', 'RightLowerArm',
          'LeftUpperLeg', 'LeftLowerLeg', 'RightUpperLeg', 'RightLowerLeg']
ARM_OUT, SPLAY = 12.0, 35.0
ADAPT = os.environ.get('ADAPT', '') == '1'
SPREAD_FREE, SPREAD_KEEP = float(os.environ.get('SPREAD_FREE', 18)), float(os.environ.get('SPREAD_KEEP', 0.45))
FOLD_CAP = float(os.environ.get('FOLD_CAP', 15))
CLAV_FROM, CLAV_SHARE = float(os.environ.get('CLAV_FROM', -20)), float(os.environ.get('CLAV_SHARE', 0.22))

# ---- scene ----
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
ob = next(o for o in bpy.data.objects if o.type == 'MESH')
ao = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
img = bpy.data.images.load(TEX)
if img.size[0] > 1024: img.scale(1024, 1024)
m = bpy.data.materials.new('kit'); m.use_nodes = True
bs = m.node_tree.nodes['Principled BSDF']; tx = m.node_tree.nodes.new('ShaderNodeTexImage'); tx.image = img
m.node_tree.links.new(tx.outputs['Color'], bs.inputs['Base Color']); bs.inputs['Roughness'].default_value = 0.7
if os.environ.get('CLAY'):
    bs.inputs['Base Color'].default_value = (0.75, 0.72, 0.68, 1); m.node_tree.links.remove(bs.inputs['Base Color'].links[0])
if os.environ.get('WCOL'):
    import colorsys
    me_ = ob.data; ca = me_.color_attributes.new('W', 'FLOAT_COLOR', 'POINT')
    pal = {vg.index: colorsys.hsv_to_rgb((vg.index * 0.618) % 1, 0.75, 0.95) for vg in ob.vertex_groups}
    for v in me_.vertices:
        c = np.zeros(3)
        for g_ in v.groups: c += g_.weight * np.array(pal[g_.group])
        ca.data[v.index].color = (*c, 1)
    at = m.node_tree.nodes.new('ShaderNodeVertexColor'); at.layer_name = 'W'
    m.node_tree.links.new(at.outputs['Color'], bs.inputs['Base Color'])
ob.data.materials.clear(); ob.data.materials.append(m)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.samples = 10; sc.cycles.use_denoising = False; sc.cycles.device = 'CPU'
sc.render.resolution_x = 360; sc.render.resolution_y = 400; sc.render.film_transparent = False
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.6, 0.66, 1)
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 3.2; sun.rotation_euler = (math.radians(40), 0, math.radians(30))
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.3
# grass disc so falls read
bpy.ops.mesh.primitive_circle_add(radius=1.6, fill_type='NGON', location=(0, 0, 0))
g = bpy.context.active_object; gm = bpy.data.materials.new('g'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.18, 0.42, 0.16, 1); g.data.materials.append(gm)
# ball
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1)
ball = bpy.context.active_object; ball.name = 'Ball'
bm_ = bpy.data.materials.new('b'); bm_.use_nodes = True
bm_.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.35, 0.12, 0.04, 1); ball.data.materials.append(bm_)
BL = 0.5 / 2.85 * 1.8   # the game's ball length for a 2.85 m player, at this model's 1.8 m
ball.scale = (BL / 2 * 8.5 / 14, BL / 2, BL / 2 * 8.5 / 14)   # long axis along Y = the figure's forward at rest

pb = ao.pose.bones
def reset():
    for b in pb: b.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
reset()
REST = {b.name: b.matrix.copy() for b in pb}
# ModelSkin.Wear: the arms lowered to hang ARM_OUT degrees out from the side
for side, s in (('Left', 1), ('Right', -1)):
    up, lo = pb[side + 'UpperArm'], pb[side + 'LowerArm']
    want = (Rx(-s * ARM_OUT) @ Vector((0, -1, 0)))
    want = C @ want
    q = (lo.head - up.head).normalized().rotation_difference(want.normalized())
    M = up.matrix.copy(); h = M.translation.copy()
    R = q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ M
    up.matrix = Matrix.Translation(h) @ R
    bpy.context.view_layer.update()
LOWERED = {b.name: b.matrix.copy() for b in pb}
PALM = (LOWERED['RightHand'].to_3x3() @ REST['RightHand'].to_3x3().inverted()) @ Vector((0, 0, -1))
PALM_ALONG, PALM_IN = float(os.environ.get('PALM_ALONG', 0.45)), float(os.environ.get('PALM_IN', 0.0))
FOOT_LEVEL, FOOT_NEAR = float(os.environ.get('FOOT_LEVEL', 0.0)), 0.22
SCALE = 1.8 / 182.0   # model metres per figure cm
ob_eval_rest = None

def mesh_tris(depsgraph):
    me = ob.evaluated_get(depsgraph).to_mesh()
    me.calc_loop_triangles()
    n = len(me.vertices); co = np.empty(n * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    t = np.empty(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get('vertices', t); t = t.reshape(-1, 3)
    ob.evaluated_get(depsgraph).to_mesh_clear()
    return co, t

def tri_area(co, t):
    return 0.5 * np.linalg.norm(np.cross(co[t[:, 1]] - co[t[:, 0]], co[t[:, 2]] - co[t[:, 0]]), axis=1)

# the dominant bone per triangle, to say where a problem is
me0 = ob.data
dom = np.zeros(len(me0.vertices), dtype=np.int64)
for v in me0.vertices:
    if v.groups: dom[v.index] = max(v.groups, key=lambda g: g.weight).group
names = [vg.name for vg in ob.vertex_groups]

def pose(name):
    p = POSES[name]
    reset()
    F = fig_world(p)
    # hips: drop in figure cm
    for bn in MAPPED:   # parents come before children in this list
        b = pb[bn]
        Rw = (C @ F[bn] @ Ci).to_4x4()
        L = LOWERED[bn]; h = L.translation.copy()
        if bn == 'Hips': h = h + Vector((0, 0, -p['drop'] * SCALE))
        b.matrix = Matrix.Translation(h) @ Rw @ Matrix.Translation(-L.translation) @ L if bn == 'Hips' else \
                   Matrix.Translation(b.matrix.translation) @ Rw @ L.to_3x3().to_4x4()
        bpy.context.view_layer.update()
    if ADAPT:
        up_w = (C @ F['Spine'] @ Vector((0, 1, 0))).normalized()
        for side, sg in (('Left', 1), ('Right', -1)):
            out_w = (C @ F['Spine'] @ Vector((0, 0, sg))).normalized()
            ua, la, sh = pb[side + 'UpperArm'], pb[side + 'LowerArm'], pb[side + 'Shoulder']
            def turn(b, q):
                h = b.matrix.translation.copy()
                b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
                bpy.context.view_layer.update()
            # 1. narrow: an arm spread more than SPREAD_FREE out to the side comes in by SPREAD_KEEP of the extra
            d = (la.head - ua.head).normalized()
            lat = math.degrees(math.asin(max(-1, min(1, d.dot(out_w)))))
            eu0 = math.degrees(math.asin(max(-1, min(1, d.dot(up_w)))))
            if lat > SPREAD_FREE and eu0 < 0 and abs(p['tilt']) < 30:
                want = SPREAD_FREE + (lat - SPREAD_FREE) * SPREAD_KEEP
                axis = d.cross(out_w)
                if axis.length > 1e-6: turn(ua, Quaternion(axis.normalized(), -math.radians(lat - want)))
            # 2. fold cap: a bent forearm under a lowered upper arm stops FOLD_CAP deg above level (hands stay off the facemask)
            du = (la.head - ua.head).normalized(); df = (pb[side + 'Hand'].head - la.head).normalized()
            bend = math.degrees(du.angle(df)); eu = math.degrees(math.asin(max(-1, min(1, du.dot(up_w)))))
            ef = math.degrees(math.asin(max(-1, min(1, df.dot(up_w)))))
            if eu < 0 and bend > 60 and ef > FOLD_CAP:
                axis = df.cross(up_w)
                if axis.length > 1e-6: turn(la, Quaternion(axis.normalized(), -math.radians(ef - FOLD_CAP)))
            # 3. clavicle: the shoulder (and its pad) lifts with a raised arm; the arm keeps its direction
            du = (la.head - ua.head).normalized(); eu = math.degrees(math.asin(max(-1, min(1, du.dot(up_w)))))
            lift = max(0.0, eu - CLAV_FROM) * CLAV_SHARE
            if lift > 0:
                keep = ua.matrix.to_3x3().copy()
                lat_w = (sh.tail - sh.head).normalized()
                axis = lat_w.cross(du)
                if axis.length > 1e-6:
                    turn(sh, Quaternion(axis.normalized(), math.radians(min(lift, math.degrees(lat_w.angle(du))))))
                    h = ua.matrix.translation.copy(); ua.matrix = Matrix.Translation(h) @ keep.to_4x4()
                    bpy.context.view_layer.update()
    if FOOT_LEVEL > 0 and abs(p['tilt']) < 30:
        for side in ('Left', 'Right'):
            ft, to_ = pb[side + 'Foot'], pb[side + 'Toes']
            if ft.head.z - (-p['drop'] * SCALE) * 0 > FOOT_NEAR + 0.0 and ft.head.z > FOOT_NEAR: continue
            d = (to_.head - ft.head).normalized(); d0 = (LOWERED[side + 'Toes'].translation - LOWERED[side + 'Foot'].translation).normalized()
            up = Vector((0, 0, 1))
            delta = math.asin(max(-1, min(1, d.dot(up)))) - math.asin(max(-1, min(1, d0.dot(up))))
            axis = d.cross(up)
            if axis.length < 1e-6: continue
            h = ft.matrix.translation.copy()
            ft.matrix = Matrix.Translation(h) @ Quaternion(axis.normalized(), -FOOT_LEVEL * delta).to_matrix().to_4x4() @ Matrix.Translation(-h) @ ft.matrix
            bpy.context.view_layer.update()
    # ModelSkin.Splay: a raised arm swings out, clear of the helmet
    for side, s, arm, fore in (('Left', 1, 'LeftUpperArm', 'LeftLowerArm'), ('Right', -1, 'RightUpperArm', 'RightLowerArm')):
        armp = F[arm]; elbow_dir = armp @ Vector((2, ELB - SH, 3 * 1.12 * s))
        dirv = elbow_dir.normalized()
        raised = dirv.dot(F['Spine'] @ Vector((0, 1, 0)))
        if raised <= 0: continue
        outward = F['Spine'] @ Vector((0, 0, s))
        ang = math.radians(SPLAY) * raised
        axis = dirv.cross(outward)
        if axis.length < 1e-6: continue
        tgt = (Matrix.Rotation(min(ang, dirv.angle(outward)), 3, axis.normalized()) @ dirv)
        q = (C @ dirv).rotation_difference(C @ tgt)
        b = pb[arm]; h = b.matrix.translation.copy()
        b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
        bpy.context.view_layer.update()
    # the ball in the right glove
    hand, lower = pb['RightHand'], pb['RightLowerArm']
    if name in BALL:
        glove_rest = LOWERED['RightHand'].translation + (LOWERED['RightHand'].translation - LOWERED['RightLowerArm'].translation) * PALM_ALONG + PALM * PALM_IN
        Mh = hand.matrix @ LOWERED['RightHand'].inverted()
        ball.location = Mh @ glove_rest
        rot = Mh.to_3x3()   # the glove keeps the figure hand's rest turn; the ball's long axis is the figure's +x
        ball.rotation_euler = rot.to_euler()
        ball.hide_render = False
    else:
        ball.hide_render = True
    bpy.context.view_layer.update()

VIEWS = {  # the game: side-on, from a little above; plus a 3/4 front
    'side': (Vector((4.0, -0.0, 1.9)), 'side'),
    'front34': (Vector((2.6, -3.6, 1.6)), '34'),
}
if os.environ.get('CLOSE'):
    VIEWS = {'side': (Vector((4.0, -1.2, 2.2)), 'side'), 'front34': (Vector((2.6, -3.6, 1.9)), '34'), 'back34': (Vector((-2.6, 3.6, 2.2)), 'b')}
    cam.data.ortho_scale = 2.0
if os.environ.get('HAND'):
    VIEWS = {'h34': (Vector((2.6, -3.6, 2.0)), '34'), 'hside': (Vector((4.0, -1.0, 1.9)), 's'), 'hfront': (Vector((0.6, -4.0, 1.6)), 'f')}
    cam.data.ortho_scale = 0.9
if os.environ.get('LEGS'):
    VIEWS = {'legside': (Vector((4.0, -1.0, 1.2)), 's'), 'leg34': (Vector((2.6, -3.6, 1.2)), '34'), 'legback': (Vector((-2.0, 3.6, 1.3)), 'b')}
    cam.data.ortho_scale = 1.0
def look(loc, target=None):
    if target is None: target = Vector((-0.05, -0.1, 0.8)) if os.environ.get("HAND") else Vector((0, 0, 0.32 if os.environ.get("LEGS") else 0.75 if not os.environ.get("CLOSE") else 0.8))
    cam.location = loc
    cam.rotation_euler = (target - loc).to_track_quat('-Z', 'Y').to_euler()

dg = bpy.context.evaluated_depsgraph_get()
co0, T = mesh_tris(dg); a0 = tri_area(co0, T)
e0 = np.stack([np.linalg.norm(co0[T[:, i]] - co0[T[:, (i + 1) % 3]], axis=1) for i in range(3)], 1)
report = {}
poses = ONLY or list(POSES)
for name in poses:
    pose(name)
    dg = bpy.context.evaluated_depsgraph_get()
    co, _ = mesh_tris(dg)
    a = tri_area(co, T); r = a / np.maximum(a0, 1e-12)
    e = np.stack([np.linalg.norm(co[T[:, i]] - co[T[:, (i + 1) % 3]], axis=1) for i in range(3)], 1)
    er = e / np.maximum(e0, 1e-9)
    bad = (r < 0.45) | (r > 2.0) | (er.max(1) > 1.6) | (er.min(1) < 0.55)
    where = {}
    for i in np.where(bad & (a0 > 1e-7))[0]:
        n = names[dom[T[i, 0]]]; where[n] = where.get(n, 0) + 1
    report[name] = {'bad_tris': int(bad.sum()), 'squash': int((r < 0.45).sum()), 'stretch': int((r > 2.0).sum()),
                    'where': dict(sorted(where.items(), key=lambda kv: -kv[1])[:5])}
    for vn, (loc, tag) in ({} if os.environ.get('NORENDER') else VIEWS).items():
        look(loc)
        sc.render.filepath = os.path.join(OUT, f'{name}_{vn}.png')
        bpy.ops.render.render(write_still=True)
json.dump(report, open(os.path.join(OUT, 'metrics.json'), 'w'), indent=1)
print('TOTAL', sum(v['bad_tris'] for v in report.values()))
if os.environ.get('NORENDER'): sys.exit(0)

# contact sheet: one row per view, one column per pose, using Blender's own image buffers
W, H = sc.render.resolution_x, sc.render.resolution_y
cols = len(poses); rows = len(VIEWS)
per = 8 if cols > 8 else cols
nrow_blocks = math.ceil(cols / per)
sheet = np.ones(((H * rows) * nrow_blocks, W * per, 4), dtype=np.float32)
for ci, name in enumerate(poses):
    bi, cj = divmod(ci, per)
    for ri, vn in enumerate(VIEWS):
        im = bpy.data.images.load(os.path.join(OUT, f'{name}_{vn}.png'))
        px = np.array(im.pixels[:], dtype=np.float32).reshape(H, W, 4)
        y0 = (nrow_blocks - 1 - bi) * H * rows + (rows - 1 - ri) * H
        sheet[y0:y0 + H, cj * W:(cj + 1) * W] = px
        bpy.data.images.remove(im)
out = bpy.data.images.new('sheet', W * per, H * rows * nrow_blocks)
out.pixels = sheet.ravel(); out.filepath_raw = os.path.join(OUT, 'sheet.png'); out.file_format = 'PNG'; out.save()
print('ORDER', poses)
print('METRICS', json.dumps({k: (v['bad_tris'], v['where']) for k, v in report.items()}))
