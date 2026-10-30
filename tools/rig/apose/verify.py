# python3 verify.py <orig.fbx> <new.fbx>: gauntlet + held-out on the new model, stretch measured against the original's shape
import sys, numpy as np
from skel import Rig, POSES, pose, blends
from gnp import score, tri_area, edges
o = Rig(sys.argv[1]); n = Rig(sys.argv[2])
assert len(o.Vw) == len(n.Vw) and np.array_equal(o.m['T'], n.m['T']), 'topology changed'
T = o.m['T']; a0 = tri_area(o.Vw, T); e0 = edges(o.Vw, T)
print('names same', o.names == n.names, ' max uv diff', float(np.abs(o.UV[o.TUV] - n.UV[n.TUV]).max()))
gaunt = list(POSES.items()); held = [('h%d' % i, p) for i, p in enumerate(blends(99, 20))]
for tag, r in (('original', o), ('new', n)):
    dom = r.W.argmax(1); g = h = 0; st = []; per = {}
    for nm, p in gaunt + held:
        POSES['_e'] = p; pose(r, '_e'); s, _ = score(r, r.skin(), T, a0, e0, dom)
        if nm.startswith('h') and nm[1:].isdigit(): h += s['bad_tris']
        else: g += s['bad_tris']; st.append(s['strain']); per[nm] = s['bad_tris']
    del POSES['_e']
    print(tag, 'gauntlet', g, 'held-out', h, 'strain %.1f' % np.mean(st), per, flush=True)
