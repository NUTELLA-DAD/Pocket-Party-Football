# numpy gauntlet: python3 gnp.py <model.fbx> <tex.png|-> <outdir> [Pose,..]  -> metrics.json, sheet.png (unless NORENDER=1)
import sys, os, json, math, numpy as np
from PIL import Image
from skel import Rig, pose, POSES, unit
def tri_area(co, T): return 0.5 * np.linalg.norm(np.cross(co[T[:, 1]] - co[T[:, 0]], co[T[:, 2]] - co[T[:, 0]]), axis=1)
def edges(co, T): return np.stack([np.linalg.norm(co[T[:, i]] - co[T[:, (i + 1) % 3]], axis=1) for i in range(3)], 1)
def score(rig, co, T, a0, e0, dom):
    a = tri_area(co, T); r = a / np.maximum(a0, 1e-12)
    er = edges(co, T) / np.maximum(e0, 1e-9)
    bad = ((r < 0.45) | (r > 2.0) | (er.max(1) > 1.6) | (er.min(1) < 0.55)) & (a0 > 1e-7)
    where = {}
    for i in np.where(bad)[0]:
        n = rig.names[dom[T[i, 0]]]; where[n] = where.get(n, 0) + 1
    # area-weighted texture distortion: how far each triangle's shape left its rest shape (0 = rigid)
    strain = np.abs(np.log(np.maximum(er, 1e-3))).max(1)
    return dict(bad_tris=int(bad.sum()), squash=int((r < 0.45).sum()), stretch=int((r > 2.0).sum()),
                strain=float((strain * a0).sum() / a0.sum() * 1000),
                where=dict(sorted(where.items(), key=lambda kv: -kv[1])[:5])), bad

def render(co, T, uv, tuv, tex, cam, size=(300, 340), scale=2.3, target=(0, 0, 0.8), hi=None):
    W, H = size; f = unit(np.array(target) - np.array(cam)); up = np.array([0, 0, 1.])
    r = unit(np.cross(f, up)); u = np.cross(r, f)
    rel = co - np.array(target)
    px = (rel @ r / scale + 0.5) * W; py = (0.5 - rel @ u / scale * W / H) * H; pz = rel @ f
    img = np.zeros((H, W, 3)) + np.array([0.55, 0.6, 0.66]); zb = np.full((H, W), 1e9)
    n = np.cross(co[T[:, 1]] - co[T[:, 0]], co[T[:, 2]] - co[T[:, 0]]); n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    light = unit(np.array([0.4, -0.6, 0.7])); shade = 0.45 + 0.55 * np.clip(n @ light, 0, 1)
    th, tw = tex.shape[:2]
    for i in np.argsort(-pz[T].mean(1)):
        t = T[i]; x = px[t]; y = py[t]
        x0, x1 = int(max(0, math.floor(x.min()))), int(min(W - 1, math.ceil(x.max())))
        y0, y1 = int(max(0, math.floor(y.min()))), int(min(H - 1, math.ceil(y.max())))
        if x1 < x0 or y1 < y0: continue
        d = (x[1] - x[0]) * (y[2] - y[0]) - (x[2] - x[0]) * (y[1] - y[0])
        if abs(d) < 1e-9: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        l1 = ((gx - x[0]) * (y[2] - y[0]) - (x[2] - x[0]) * (gy - y[0])) / d
        l2 = ((x[1] - x[0]) * (gy - y[0]) - (gx - x[0]) * (y[1] - y[0])) / d
        l0 = 1 - l1 - l2; m = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        if not m.any(): continue
        z = l0 * pz[t[0]] + l1 * pz[t[1]] + l2 * pz[t[2]]
        sub = zb[y0:y1 + 1, x0:x1 + 1]; m &= z < sub
        if not m.any(): continue
        sub[m] = z[m]
        if hi is not None and hi[i]: col = np.array([1.0, 0.0, 1.0])[None]
        else:
            uvv = uv[tuv[i]]; U = l0[m] * uvv[0, 0] + l1[m] * uvv[1, 0] + l2[m] * uvv[2, 0]; Vv = l0[m] * uvv[0, 1] + l1[m] * uvv[1, 1] + l2[m] * uvv[2, 1]
            col = tex[np.clip(((1 - Vv % 1) * th).astype(int), 0, th - 1), np.clip(((U % 1) * tw).astype(int), 0, tw - 1)]
        img[y0:y1 + 1, x0:x1 + 1][m] = col * shade[i]
    return (np.clip(img, 0, 1) * 255).astype(np.uint8)

VIEWS = {'side': (4.0, 0.0, 1.9), 'front34': (2.6, -3.6, 1.6)}
if __name__ == '__main__':
    FBX, TEX, OUT = sys.argv[1:4]; ONLY = sys.argv[4].split(',') if len(sys.argv) > 4 else None
    os.makedirs(OUT, exist_ok=True)
    rig = Rig(FBX); T = rig.m['T']
    dom = rig.W.argmax(1)
    a0 = tri_area(rig.Vw, T); e0 = edges(rig.Vw, T)
    tex = np.asarray(Image.open(TEX).convert('RGB').resize((1024, 1024)), dtype=float) / 255 if TEX != '-' else np.ones((4, 4, 3)) * 0.8
    poses = ONLY or list(POSES); report = {}; tiles = []
    for name in poses:
        pose(rig, name, spine_chain=os.environ.get('NOCHAIN') != '1')
        co = rig.skin()
        report[name], bad = score(rig, co, T, a0, e0, dom)
        if os.environ.get('NORENDER') != '1':
            hi = bad if os.environ.get('HI') == '1' else None
            tiles.append([render(co, T, rig.UV, rig.TUV, tex, v, hi=hi) for v in VIEWS.values()])
    json.dump(report, open(os.path.join(OUT, 'metrics.json'), 'w'), indent=1)
    tot = sum(v['bad_tris'] for v in report.values()); strain = sum(v['strain'] for v in report.values()) / len(report)
    print('TOTAL', tot, 'STRAIN %.2f' % strain)
    if tiles:
        rows = [np.concatenate([t[k] for t in tiles], 1) for k in range(len(VIEWS))]
        Image.fromarray(np.concatenate(rows, 0)).save(os.path.join(OUT, 'sheet.png'))
