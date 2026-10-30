# numpy port of tools/rig/gauntlet.py's posing (which mirrors ModelSkin.cs), Blender space: Z up, model faces -Y, its left +X
import math, numpy as np
from model import load
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
def rot(axis, deg):
    a = np.asarray(axis, float); n = np.linalg.norm(a)
    if n < 1e-12: return np.eye(3)
    a = a / n; t = math.radians(deg); c, s = math.cos(t), math.sin(t)
    x, y, z = a
    return np.array([[c + x*x*(1-c), x*y*(1-c) - z*s, x*z*(1-c) + y*s],
                     [y*x*(1-c) + z*s, c + y*y*(1-c), y*z*(1-c) - x*s],
                     [z*x*(1-c) - y*s, z*y*(1-c) + x*s, c + z*z*(1-c)]])
def Rz(d): return rot((0, 0, 1), d)
def Rx(d): return rot((1, 0, 0), d)
def E(x, z): return Rx(x) @ Rz(z)
def unit(v): n = np.linalg.norm(v); return v / n if n > 1e-12 else v
def asin_d(x): return math.degrees(math.asin(max(-1, min(1, x))))
def ang(a, b): return math.degrees(math.acos(max(-1, min(1, float(unit(a) @ unit(b))))))
def from_to(a, b):
    a, b = unit(a), unit(b); ax = np.cross(a, b)
    if np.linalg.norm(ax) < 1e-9: return np.eye(3)
    return rot(ax, ang(a, b))
C = np.array([[0, 0, 1], [-1, 0, 0], [0, 1, 0]], float); Ci = np.linalg.inv(C)
A = np.array([[1, 0, 0, 0], [0, 0, -1, 0], [0, 1, 0, 0], [0, 0, 0, 1]], float)   # FBX Y-up -> Blender Z-up
MAPPED = ['Hips', 'Spine', 'Head', 'LeftUpperArm', 'LeftLowerArm', 'RightUpperArm', 'RightLowerArm',
          'LeftUpperLeg', 'LeftLowerLeg', 'RightUpperLeg', 'RightLowerLeg']
SH, ELB = 108.0, 86.0
ARM_OUT, SPLAY = 12.0, 35.0
SPREAD_FREE, SPREAD_KEEP, FOLD_CAP, CLAV_FROM, CLAV_SHARE = 18.0, 0.45, 15.0, -20.0, 0.22
FOOT_LEVEL, FOOT_NEAR = 0.7, 0.22
import os
ARM_HANG = float(os.environ.get('ARM_HANG', 0))
def hip_half(rig):
    if getattr(rig, '_hip', None) is None:
        keep = [rig.names.index(n) for n in ('Hips', 'Spine', 'LeftUpperLeg', 'RightUpperLeg')]
        X = rig.Vw; z = X[:, 2] / rig.height; m = np.isin(rig.W.argmax(1), keep) & (z >= 0.25) & (z <= 0.36)
        rig._hip = float(np.abs(X[m, 0]).max())
    return rig._hip
SPINE_BEND, SPINE_TWIST, NECK_SHARE = [0.4, 0.35, 0.25], [0.2, 0.35, 0.45], 0.4

def fig_world(p):
    hips = Rz(-p['tilt']); torso = hips @ Rz(-p['lean'])
    return {'Hips': hips, 'Spine': torso, 'Head': torso @ Rz(-p['head']),
        'LeftUpperArm': torso @ E(-p['armLx'], p['armL']), 'RightUpperArm': torso @ E(p['armRx'], p['armR']),
        'LeftUpperLeg': hips @ E(-p['thighLx'], p['thighL']), 'RightUpperLeg': hips @ E(p['thighRx'], p['thighR']),
        'LeftLowerLeg': hips @ E(-p['thighLx'], p['thighL']) @ Rz(p['shinL']),
        'RightLowerLeg': hips @ E(p['thighRx'], p['thighR']) @ Rz(p['shinR']),
        'LeftLowerArm': torso @ E(-p['armLx'], p['armL']) @ Rz(p['foreL']),
        'RightLowerArm': torso @ E(p['armRx'], p['armR']) @ Rz(p['foreR'])}

class Rig:
    def __init__(self, path):
        m = load(path); self.m = m
        self.names = list(m['clusters']); self.parent = m['bparent']
        self.order = []
        def visit(n):
            if n in self.order: return
            if self.parent.get(n): visit(self.parent[n])
            self.order.append(n)
        for n in self.names: visit(n)
        self.kids = {n: [k for k in self.names if self.parent.get(k) == n] for n in self.names}
        self.REST = {n: A @ m['clusters'][n]['TL'] for n in self.names}
        # rest world vertex positions (should equal the mesh's own coords)
        V = m['V']; Vh = np.c_[V, np.ones(len(V))]
        c0 = m['clusters']['Hips']; self.Vw = (A @ c0['TL'] @ c0['T'] @ Vh.T).T[:, :3]
        nb = len(self.names); self.W = np.zeros((len(V), nb))
        for i, n in enumerate(self.names):
            c = m['clusters'][n]; self.W[c['idx'], i] += c['w']
        s = self.W.sum(1, keepdims=True); self.W = self.W / np.maximum(s, 1e-12)
        self.height = float(self.Vw[:, 2].max()); self.T = m["T"]; self.UV = m['UV']; self.TUV = m['TUV']
        self.reset()
    def reset(self):
        self.M = {n: self.REST[n].copy() for n in self.names}
    def head(self, n): return self.M[n][:3, 3].copy()
    def tail(self, n):
        k = {'LeftShoulder': 'LeftUpperArm', 'RightShoulder': 'RightUpperArm'}.get(n)
        return self.head(k)
    def setw(self, n, Mnew):
        loc = {}
        def collect(b):
            for k in self.kids[b]:
                loc[k] = np.linalg.inv(self.M[b]) @ self.M[k]; collect(k)
        collect(n)
        self.M[n] = Mnew
        def apply(b):
            for k in self.kids[b]:
                self.M[k] = self.M[b] @ loc[k]; apply(k)
        apply(n)
    def turn(self, n, R, about=None):
        h = self.head(n) if about is None else about
        T4 = np.eye(4); T4[:3, :3] = R; T4[:3, 3] = h - R @ h
        self.setw(n, T4 @ self.M[n])
    def setrot(self, n, R3, head=None):
        M = np.eye(4); M[:3, :3] = R3; M[:3, 3] = self.head(n) if head is None else head
        self.setw(n, M)
    def skin(self):
        S = np.stack([self.M[n] @ np.linalg.inv(self.REST[n]) for n in self.names])   # nb,4,4
        Vh = np.c_[self.Vw, np.ones(len(self.Vw))]
        out = np.einsum('vb,bij,vj->vi', self.W, S, Vh, optimize=True)
        return out[:, :3]

def pose(rig, name, adapt=True, spine_chain=True, extra=None):
    p = POSES[name]; rig.reset()
    F = fig_world(p)
    # Wear: arms lowered to hang ARM_OUT out from the side
    for side, s in (('Left', 1), ('Right', -1)):
        want = C @ (Rx(-s * ARM_OUT) @ np.array([0, -1, 0.]))
        q = from_to(rig.head(side + 'LowerArm') - rig.head(side + 'UpperArm'), want)
        rig.turn(side + 'UpperArm', q)
        if ARM_HANG > 0:   # ModelSkin FEATURES.armHang
            hip = hip_half(rig)
            up = rig.head(side + 'UpperArm'); d = rig.head(side + 'Hand') - up
            over = up[0] * s + d[0] * s - (hip + ARM_HANG * rig.height)
            yz = math.hypot(d[2], d[0])
            if over > 0:
                lat = max(0.0, d[0] * s - over)
                t = np.array([lat * s, d[1], -math.sqrt(max(0, yz * yz - lat * lat))])
                rig.turn(side + 'UpperArm', from_to(d, t))
    LOW = {n: rig.M[n].copy() for n in rig.names}
    SCALE = 1.8 / 182.0
    for bn in MAPPED:
        Rw = C @ F[bn] @ Ci; L = LOW[bn]
        if bn == 'Hips':
            h = L[:3, 3] + np.array([0, 0, -p['drop'] * SCALE * (rig.height / 1.8)])
            rig.setrot(bn, Rw @ L[:3, :3], h)
        else:
            rig.setrot(bn, Rw @ L[:3, :3])
    if spine_chain and all(n in rig.names for n in ('Chest', 'UpperChest', 'Neck')):
        bow(rig, LOW)
    if adapt:
        up_w = unit(C @ F['Spine'] @ np.array([0, 1, 0.]))
        for side, sg in (('Left', 1), ('Right', -1)):
            out_w = unit(C @ F['Spine'] @ np.array([0, 0, sg]))
            ua, la, sh, hd = side + 'UpperArm', side + 'LowerArm', side + 'Shoulder', side + 'Hand'
            d = unit(rig.head(la) - rig.head(ua))
            lat = asin_d(d @ out_w); eu0 = asin_d(d @ up_w)
            if lat > SPREAD_FREE and eu0 < 0 and abs(p['tilt']) < 30:
                want = SPREAD_FREE + (lat - SPREAD_FREE) * SPREAD_KEEP
                rig.turn(ua, rot(np.cross(d, out_w), -(lat - want)))
            du = unit(rig.head(la) - rig.head(ua)); df = unit(rig.head(hd) - rig.head(la))
            bend = ang(du, df); eu = asin_d(du @ up_w); ef = asin_d(df @ up_w)
            if eu < 0 and bend > 60 and ef > FOLD_CAP:
                rig.turn(la, rot(np.cross(df, up_w), -(ef - FOLD_CAP)))
            du = unit(rig.head(la) - rig.head(ua)); eu = asin_d(du @ up_w)
            lift = max(0.0, eu - CLAV_FROM) * CLAV_SHARE
            if lift > 0:
                keep = rig.M[ua][:3, :3].copy()
                lat_w = unit(rig.tail(sh) - rig.head(sh))
                ax = np.cross(lat_w, du)
                if np.linalg.norm(ax) > 1e-6:
                    rig.turn(sh, rot(ax, min(lift, ang(lat_w, du))))
                    rig.setrot(ua, keep)
    if abs(p['tilt']) < 30:
        for side in ('Left', 'Right'):
            ft, to_ = side + 'Foot', side + 'Toes'
            if rig.head(ft)[2] > FOOT_NEAR * rig.height / 1.8: continue
            d = unit(rig.head(to_) - rig.head(ft)); d0 = unit(LOW[to_][:3, 3] - LOW[ft][:3, 3]); up = np.array([0, 0, 1.])
            delta = asin_d(d @ up) - asin_d(d0 @ up)
            rig.turn(ft, rot(np.cross(d, up), -FOOT_LEVEL * delta))
    for side, s, arm in (('Left', 1, 'LeftUpperArm'), ('Right', -1, 'RightUpperArm')):
        dirv = unit(F[arm] @ np.array([2, ELB - SH, 3 * 1.12 * s]))
        raised = dirv @ (F['Spine'] @ np.array([0, 1, 0.]))
        if raised <= 0: continue
        outward = F['Spine'] @ np.array([0, 0, s])
        ax = np.cross(dirv, outward)
        if np.linalg.norm(ax) < 1e-6: continue
        tgt = rot(ax, min(SPLAY * raised, ang(dirv, outward))) @ dirv
        rig.turn(arm, from_to(C @ dirv, C @ tgt))
    if extra: extra(rig, p, LOW)

def bow(rig, LOW):
    # ModelSkin.Bow + Nod: the Spine's turn spread over Spine, Chest, UpperChest; the neck takes NECK_SHARE of the head's
    sp = ['Spine', 'Chest', 'UpperChest']
    # rest locals (the chain as modelled, before the figure's turns): Spine on Hips etc, from LOW
    restloc = {n: np.linalg.inv(LOW[rig.parent[n]][:3, :3]) @ LOW[n][:3, :3] for n in sp + ['Neck', 'Head']}
    head_w = rig.M['Head'][:3, :3].copy()
    straight = rig.M['Hips'][:3, :3] @ restloc['Spine']
    allr = rig.M['Spine'][:3, :3] @ straight.T
    from scipy_free import split_twist, slerp
    axis = rig.M['Hips'][:3, :3] @ np.array([0, 0, 1.])   # the hips' up (Blender Z)
    swing, twist = split_twist(allr, axis)
    b = t = 0.0; rest = straight
    for i, n in enumerate(sp):
        if i > 0: rest = rest @ restloc[n]
        b += SPINE_BEND[i]; t += SPINE_TWIST[i]
        R = allr @ rest if i == 2 else slerp(swing, b) @ slerp(twist, t) @ rest
        rig.setrot(n, R)
    neck = rig.M['UpperChest'][:3, :3] @ restloc['Neck']
    turnh = head_w @ (neck @ restloc['Head']).T
    rig.setrot('Neck', slerp(turnh, NECK_SHARE) @ neck)
    rig.setrot('Head', head_w)

def blends(seed, n):
    """n in-between poses: two of POSES mixed (seeded), like the game's moves passing from one key to the next."""
    rng = np.random.default_rng(seed); names = [k for k in POSES if not k.startswith('_')]; out = []
    for _ in range(n):
        a, b = rng.choice(len(names), 2, replace=False); t = rng.uniform(0.2, 0.8)
        out.append({k: POSES[names[a]][k] * (1 - t) + POSES[names[b]][k] * t for k in K})
    return out
