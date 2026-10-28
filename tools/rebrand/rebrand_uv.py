# The rest-pose point and normal under every texel of a model's colour map (shared by rebrand.py and recolour.py).
import numpy as np
import fbxuv


def uvmap(fbx, N):
    co, verts, uvs, tris = fbxuv.mesh(fbx)
    pos = np.full((N, N, 3), np.nan, np.float32); nrm = np.zeros((N, N, 3), np.float32)
    for tri in tris:
        P = co[verts[tri]]; n = np.cross(P[1] - P[0], P[2] - P[0]); nn = np.linalg.norm(n)
        if nn == 0: continue
        n /= nn; uv = uvs[tri]
        x = uv[:, 0] * N; y = (1 - uv[:, 1]) * N
        x0, x1 = max(int(np.floor(x.min())) - 1, 0), min(int(np.ceil(x.max())) + 1, N - 1)
        y0, y1 = max(int(np.floor(y.min())) - 1, 0), min(int(np.ceil(y.max())) + 1, N - 1)
        if x1 < x0 or y1 < y0: continue
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
    return pos, nrm
