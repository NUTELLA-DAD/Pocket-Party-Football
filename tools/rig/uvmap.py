# For each texel of a rigged FBX's colour map: the rest-pose point it paints (Blender space: +X the model's left, -Y
# forward, Z up, metres) and that face's normal (NaN where no triangle). After the models' uvmap_bpy.py, but read
# straight from the game's FBX. usage: python uvmap.py -- <model.fbx> <out.npz> [size, default 2048]
import bpy, sys, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
FBX, OUT = argv[0], argv[1]
N = int(argv[2]) if len(argv) > 2 else 2048
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
ob = next(o for o in bpy.data.objects if o.type == 'MESH')
dg = bpy.context.evaluated_depsgraph_get()
me = ob.evaluated_get(dg).to_mesh(); me.calc_loop_triangles()
M = np.array(ob.matrix_world)
co = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
co = co @ M[:3, :3].T + M[:3, 3]
uvl = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get('uv', uvl); uvl = uvl.reshape(-1, 2)
lt = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get('loops', lt); lt = lt.reshape(-1, 3)
lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
pos = np.full((N, N, 3), np.nan, np.float32); nrm = np.zeros((N, N, 3), np.float32)
for tri in lt:
    P = co[lv[tri]]; n = np.cross(P[1] - P[0], P[2] - P[0]); nn = np.linalg.norm(n)
    if nn == 0: continue
    n /= nn; uv = uvl[tri]
    x = uv[:, 0] * N; y = (1 - uv[:, 1]) * N
    x0, x1 = max(int(np.floor(x.min())) - 1, 0), min(int(np.ceil(x.max())) + 1, N - 1)
    y0, y1 = max(int(np.floor(y.min())) - 1, 0), min(int(np.ceil(y.max())) + 1, N - 1)
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
    if abs(d) < 1e-12: continue
    w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
    w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
    w2 = 1 - w0 - w1; e = -1.0 / max(abs(d) ** 0.5, 1)
    m = (w0 >= e) & (w1 >= e) & (w2 >= e)
    if not m.any(): continue
    p = w0[..., None] * P[0] + w1[..., None] * P[1] + w2[..., None] * P[2]
    pos[y0:y1 + 1, x0:x1 + 1][m] = p[m]; nrm[y0:y1 + 1, x0:x1 + 1][m] = n
np.savez_compressed(OUT, pos=pos, nrm=nrm)
ok = ~np.isnan(pos[..., 0])
print('covered', ok.mean(), 'bounds', np.nanmin(co, 0), np.nanmax(co, 0))
