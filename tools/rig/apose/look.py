import sys, numpy as np, collections
from PIL import Image
from skel import Rig, POSES, pose
from gnp import score, tri_area, edges, render
fbx, npz, tex, out = sys.argv[1:5]; poses = sys.argv[5].split(',')
rig = Rig(fbx); X0 = rig.Vw.copy(); T = rig.m['T']; a0 = tri_area(X0, T); e0 = edges(X0, T); dom = rig.W.argmax(1)
if npz != '-':
    z = np.load(npz); rig.Vw = z['V']; rig.REST = {k[2:]: z[k] for k in z.files if k.startswith('M_')}; rig.reset()
tx = np.asarray(Image.open(tex).convert('RGB').resize((1024, 1024)), float) / 255
c = collections.Counter(); rows = [[], []]
for p in poses:
    pose(rig, p); co = rig.skin(); s, bad = score(rig, co, T, a0, e0, dom)
    for k, v in s['where'].items(): c[k] += v
    for i, v in enumerate([(4.0, 0.0, 1.9), (2.6, -3.6, 1.6)]): rows[i].append(render(co, T, rig.UV, rig.TUV, tx, v, hi=bad))
print(c.most_common(10))
Image.fromarray(np.concatenate([np.concatenate(r, 1) for r in rows], 0)).save(out)
