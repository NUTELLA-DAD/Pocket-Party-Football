# A-pose rest: lower the model's arms to `phi` degrees out from straight down (90 = the T-pose it was modelled in),
# then relax the skin around the shoulders as-rigid-as-possible against the modelled (textured) shape, so the
# armpits and shoulder pads keep their painted proportions; the bones' new rest is that pose.
import math, numpy as np
from skel import Rig, rot, from_to, unit, C, Rx
def edges_of(T):
    e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]]); e.sort(1); return np.unique(e, axis=0)
def kabsch(Hm):
    U, S, Vt = np.linalg.svd(Hm)
    R = Vt.transpose(0, 2, 1) @ U.transpose(0, 2, 1)
    d = np.linalg.det(R); Vt2 = Vt.copy(); Vt2[d < 0, 2] *= -1
    return Vt2.transpose(0, 2, 1) @ U.transpose(0, 2, 1)
def lower_arms(rig, phi):
    rig.reset()
    for side, s in (('Left', 1), ('Right', -1)):
        want = C @ (Rx(-s * phi) @ np.array([0, -1, 0.]))
        rig.turn(side + 'UpperArm', from_to(rig.head(side + 'LowerArm') - rig.head(side + 'UpperArm'), want))
def relax(X, T, V1, free, iters=30, cg=200, log=None):
    """ARAP: positions of the free vertices that keep every edge's rest shape best (the others held at V1)."""
    E = edges_of(T); i0, j0 = E[:, 0], E[:, 1]; N = len(X)
    er = X[i0] - X[j0]; w = 1 / np.maximum((er ** 2).sum(1), 1e-10)   # (relative: every edge's strain counts alike)
    deg = np.zeros(N); np.add.at(deg, i0, w); np.add.at(deg, j0, w)
    def Lap(P):
        out = deg[:, None] * P
        np.subtract.at(out, i0, w[:, None] * P[j0]); np.subtract.at(out, j0, w[:, None] * P[i0]); return out
    V = V1.copy(); F = free
    for it in range(iters):
        e = V[i0] - V[j0]
        H = np.zeros((N, 3, 3)); c = (w[:, None] * er)[:, :, None] * e[:, None, :]
        np.add.at(H, i0, c); np.add.at(H, j0, c)
        R = kabsch(H)
        b = np.zeros((N, 3)); t = 0.5 * w[:, None] * np.einsum('ecd,ed->ec', R[i0] + R[j0], er)
        np.add.at(b, i0, t); np.subtract.at(b, j0, t)
        # CG on L V = b over the free rows (fixed rows held)
        x = V.copy(); r = (b - Lap(x)) * F[:, None]; d = r.copy(); rr = (r * r).sum()
        for k in range(cg):
            Ad = Lap(d) * F[:, None]; a = rr / max((d * Ad).sum(), 1e-30)
            x += a * d; r -= a * Ad; rn = (r * r).sum()
            if rn < 1e-14: break
            d = r + rn / rr * d; rr = rn
        V = x
        if log and it % 10 == 9:
            el = np.linalg.norm(V[i0] - V[j0], axis=1) / np.sqrt(1 / w); log('arap %d  edge strain mean %.4f' % (it, np.abs(np.log(el)).mean()))
    return V
def apose(rig, phi, grow=0.0, iters=30, log=print, free_bones=None):
    X = rig.Vw.copy(); T = rig.m['T']
    lower_arms(rig, phi)
    V1 = rig.skin()
    nid = {n: i for i, n in enumerate(rig.names)}
    fb = free_bones or ['LeftShoulder', 'RightShoulder', 'LeftUpperArm', 'RightUpperArm', 'UpperChest', 'Chest', 'Neck']
    W = rig.W
    blend = 1 - W.max(1)
    near = W[:, [nid[n] for n in fb]].sum(1) > 0.02
    free = near & (W[:, nid['Head']] < 0.5)
    # the elbows down and the waist down hold the relaxed region in place
    hold = ['LeftLowerArm', 'RightLowerArm', 'LeftHand', 'RightHand', 'Spine', 'Hips', 'Head']
    free &= W[:, [nid[n] for n in hold if n in nid]].sum(1) < 0.5
    log('apose phi %.0f: %d of %d vertices free' % (phi, int(free.sum()), len(X)))
    V = relax(X, T, V1, free.astype(float), iters=iters, log=log)
    REST = {n: rig.M[n].copy() for n in rig.names}
    return V, REST, free
