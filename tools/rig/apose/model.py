# The parts of a rigged FBX the rig tools need, read without Blender: the mesh (Blender space: Z up, faces -Y), its
# triangles and UVs, every bone's skin cluster (bind matrices, vertex weights) and the bones' parents.
import numpy as np
from fbxread import read, find, one
def p70(n):
    out = {}
    pp = one(n, 'Properties70')
    if pp:
        for P in pp[2]:
            out[P[1][0]] = P[1][4:]
    return out
def load(path):
    top = read(path)
    objs = next(t for t in top if t[0] == 'Objects')
    conns = next(t for t in top if t[0] == 'Connections')
    byid = {c[1][0]: c for c in objs[2]}
    geo = next(c for c in objs[2] if c[0] == 'Geometry')
    V = one(geo, 'Vertices')[1][0].reshape(-1, 3).astype(np.float64)
    PVI = one(geo, 'PolygonVertexIndex')[1][0].astype(np.int64)
    uvl = one(geo, 'LayerElementUV')
    UV = one(uvl, 'UV')[1][0].reshape(-1, 2).astype(np.float64)
    UVI = one(uvl, 'UVIndex')[1][0].astype(np.int64)
    # triangulate (fan) with corner indices
    tris = []; start = 0
    for i, v in enumerate(PVI):
        if v < 0:
            for k in range(start + 1, i): tris.append((start, k, k + 1))
            start = i + 1
    C = np.array(tris)
    vi = PVI.copy(); vi[vi < 0] = -vi[vi < 0] - 1
    T = vi[C]; TUV = UVI[C]
    # bones
    par = {}
    for c in conns[2]:
        if c[1][0] == 'OO': par.setdefault(c[1][1], []).append(c[1][2])
    bones = {}
    for c in objs[2]:
        if c[0] == 'Model' and c[1][2] == 'LimbNode':
            bones[c[1][0]] = c[1][1].split('\x00')[0]
    clusters = {}
    for c in objs[2]:
        if c[0] == 'Deformer' and c[1][2] == 'Cluster':
            bid = None   # the cluster's bone: connected bone -> cluster
            for k, vs in par.items():
                if k in bones and c[1][0] in vs: bid = k
            idx = one(c, 'Indexes'); w = one(c, 'Weights')
            TL = np.array(one(c, 'TransformLink')[1][0]).reshape(4, 4).T
            Tm = np.array(one(c, 'Transform')[1][0]).reshape(4, 4).T
            name = bones[bid]
            clusters[name] = dict(TL=TL, T=Tm, idx=idx[1][0].astype(np.int64) if idx else np.zeros(0, int), w=w[1][0].astype(np.float64) if w else np.zeros(0))
    # parent of each bone
    bparent = {}
    for k, vs in par.items():
        if k in bones:
            for v in vs:
                if v in bones: bparent[bones[k]] = bones[v]
    return dict(V=V, T=T, UV=UV, TUV=TUV, clusters=clusters, bparent=bparent, objs=objs, top=top)
