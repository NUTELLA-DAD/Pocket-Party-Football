# Jersey numbers and eyes the game draws itself (FEATURES.jerseyNumbers, FEATURES.eyesLook): for one model, paints the
# baked-in numbers and irises out of each of its colour maps (in place), and writes the model's marks map, which tells
# the PocketParty/ToonPlayer shader where a number or an eye goes on each texel.
#   marks map RGBA: R, G = the texel's spot in its zone (number: in number heights, -1.5..1.5; eye: in iris radii, -3..3;
#   both 0..1 in the PNG), B = 128 number / 255 eye, A = 255 inside the zone (an eye's: inside its opening only).
# usage: python3 marks.py <uvmap.npz> <zones.json> <Models dir> [preview.png]
#   uvmap.npz from uvmap.py (the rest-pose point and normal under every texel of the colour map).
#   zones.json: {"marks": "Chiefs24/Chiefs24_Marks.png", "textures": [{"file": ..., "hue": jersey hue in degrees}],
#     "zones": [{"kind": "number"|"eye", "c": centre [x,y,z] m, "n": facing, "up": up, "w","h": half box m,
#     "depth": m either side of the surface, "size": number height | iris radius m,
#     an eye's "iris": its centre on the plane [s, t] m, "poly": its opening on the plane [[s, t], ...] m}]}
#   Blender space throughout: +X the model's left, -Y forward, Z up (what uvmap.py writes).
# Prints, per colour map, the iris and lid colours to copy into game.json "modelMarks".
import sys, json, os, numpy as np
from PIL import Image

uvz, spec_path, root = sys.argv[1:4]
preview = sys.argv[4] if len(sys.argv) > 4 else None
spec = json.load(open(spec_path))
d = np.load(uvz); pos, nrm = d['pos'], d['nrm']
N = pos.shape[0]
ok = ~np.isnan(pos[..., 0]); P = np.nan_to_num(pos)

def unit(v): v = np.asarray(v, float); return v / np.linalg.norm(v)

# ---- zones -> the marks map ----
marks = np.zeros((N, N, 4), np.float32)
zone_of = np.full((N, N), -1, np.int32)
local = np.zeros((N, N, 2), np.float32)
zm = {}   # an eye zone with a drawn opening: every texel's spot on its plane (m)
for zi, z in enumerate(spec['zones']):
    n = unit(z['n']); up = unit(z['up']); r = unit(np.cross(up, n)); up = np.cross(n, r)
    c = np.asarray(z['c'], float)
    q = P - c
    s, t, dn = q @ r, q @ up, q @ n
    box = ok & (np.abs(s) <= z['w']) & (np.abs(t) <= z['h']) & ((nrm @ n) > z.get('facing', 0.2))
    box &= (np.abs(dn) <= z['depth']) & (zone_of < 0)   # (an earlier zone keeps its texels)
    zone_of[box] = zi
    if 'poly' in z: zm[zi] = (s, t)
    i0, i1 = z.get('iris', (0, 0))   # an eye's local spot is measured from its iris, in iris radii
    local[box, 0] = (s[box] - i0) / z['size']; local[box, 1] = (t[box] - i1) / z['size']
    print('zone', zi, z['kind'], 'texels', int(box.sum()))

def shift(a, dy, dx):
    out = np.zeros_like(a)
    ys, yd = (slice(dy, None), slice(0, -dy)) if dy > 0 else ((slice(0, dy), slice(-dy, None)) if dy < 0 else (slice(None), slice(None)))
    xs, xd = (slice(dx, None), slice(0, -dx)) if dx > 0 else ((slice(0, dx), slice(-dx, None)) if dx < 0 else (slice(None), slice(None)))
    out[ys, xs] = a[yd, xd]
    return out

def grow(mask, px, within=None):
    for _ in range(px):
        g = mask.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)): g |= shift(mask, dy, dx)
        mask = g if within is None else (g & within)
    return mask

def cross2(a, b): return a[..., 0] * b[..., 1] - a[..., 1] * b[..., 0]

def hull_mask(pts, qs):
    # points qs (k,2) inside the convex hull of pts (m,2)
    pts = np.unique(np.round(pts, 4), axis=0)
    pts = pts[np.lexsort((pts[:, 1], pts[:, 0]))]
    def half(ps):
        h = []
        for p in ps:
            while len(h) >= 2 and cross2(h[-1] - h[-2], p - h[-2]) <= 0: h.pop()
            h.append(p)
        return h
    hull = np.array(half(pts)[:-1] + half(pts[::-1])[:-1])
    inside = np.ones(len(qs), bool)
    for i in range(len(hull)):
        a, b = hull[i], hull[(i + 1) % len(hull)]
        inside &= cross2(b - a, qs - a) >= 0
    return inside

def in_poly(poly, q):
    inside = np.zeros(len(q), bool)
    for i in range(len(poly)):
        a, b = poly[i], poly[i - 1]
        hit = ((a[1] > q[:, 1]) != (b[1] > q[:, 1])) & (q[:, 0] < (b[0] - a[0]) * (q[:, 1] - a[1]) / (b[1] - a[1] + 1e-12) + a[0])
        inside ^= hit
    return inside

def load(f):
    img = Image.open(os.path.join(root, f)).convert('RGB')
    if img.size != (N, N): raise SystemExit(f + ' is not ' + str(N))
    return img

def hsv_of(img):
    hsv = np.asarray(img.convert('HSV')).astype(np.float32)
    return hsv[..., 0] * 360 / 256, hsv[..., 1] / 255, hsv[..., 2] / 255

# the eyes' openings, from the first colour map (the eyes are painted the same on every team's map)
first = load(spec['textures'][0]['file'])
h0, s0, v0 = hsv_of(first)
eye_open = np.zeros((N, N), bool)
for zi, z in enumerate(spec['zones']):
    if z['kind'] != 'eye': continue
    inz = zone_of == zi
    if 'poly' in z:   # the opening drawn by hand, metres on the zone's plane
        m = np.zeros((N, N), bool); m[inz] = in_poly(np.asarray(z['poly'], float), np.stack([zm[zi][0][inz], zm[zi][1][inz]], 1))
        eye_open |= m
        print('zone', zi, 'eye opening texels', int(m.sum()))
        continue
    sclera = inz & (v0 > z.get('scleraV', 0.78)) & (s0 < z.get('scleraS', 0.22))
    if sclera.sum() < 20: print('zone', zi, 'eye: no white found'); continue
    ins = hull_mask(local[sclera], local[inz])
    m = np.zeros((N, N), bool); m[inz] = ins
    eye_open |= m
    print('zone', zi, 'eye opening texels', int(m.sum()))

for zi, z in enumerate(spec['zones']):
    inz = zone_of == zi
    if z['kind'] == 'number':
        marks[inz, 0] = np.clip(local[inz, 0] / 3 + 0.5, 0, 1); marks[inz, 1] = np.clip(local[inz, 1] / 3 + 0.5, 0, 1)
        marks[inz, 2] = 128 / 255; marks[inz, 3] = 1
    else:
        e = inz & eye_open
        marks[inz, 0] = np.clip(local[inz, 0] / 6 + 0.5, 0, 1); marks[inz, 1] = np.clip(local[inz, 1] / 6 + 0.5, 0, 1)
        marks[inz, 2] = 1; marks[e, 3] = 1
# pad 3 texels into the empty texture around each zone (bilinear filtering reads a texel past the edge)
filled = marks[..., 3] > 0
for _ in range(3):
    acc = np.zeros_like(marks); cnt = np.zeros((N, N), np.float32)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        f = shift(filled, dy, dx); acc += shift(marks, dy, dx) * f[..., None]; cnt += f
    new = (~filled) & (~ok) & (cnt > 0)
    marks[new] = acc[new] / cnt[new][:, None]; marks[new, 3] = 1
    filled |= new
out = os.path.join(root, spec['marks'])
Image.fromarray((marks * 255 + 0.5).astype(np.uint8), 'RGBA').save(out)
print('wrote', out)

# ---- paint the old numbers and irises out of each colour map ----
def inpaint(rgb, hole, source, iters=400):
    # each hole texel takes the average of its known neighbours (5x5), ring by ring in from the edge; only the hole
    # texels are visited
    flat = rgb.reshape(-1, 3).copy()
    hy, hx = np.nonzero(hole); hi = hy * N + hx
    offs = [(dy, dx) for dy in (-2, -1, 0, 1, 2) for dx in (-2, -1, 0, 1, 2) if dy or dx]
    nb = np.stack([np.clip(hy + dy, 0, N - 1) * N + np.clip(hx + dx, 0, N - 1) for dy, dx in offs], 1)
    known = (source & ~hole).reshape(-1).copy()
    todo = np.ones(len(hi), bool)
    for _ in range(iters):
        if not todo.any(): break
        idx = np.nonzero(todo)[0]
        k = known[nb[idx]]; cnt = k.sum(1)
        got = cnt > 0
        if not got.any(): break
        acc = (flat[nb[idx]] * k[..., None]).sum(1)
        g = idx[got]
        flat[hi[g]] = acc[got] / cnt[got][:, None]
        known[hi[g]] = True; todo[g] = False
    left = np.zeros(N * N, bool); left[hi[todo]] = True
    return flat.reshape(N, N, 3), left.reshape(N, N)

for tx in spec['textures']:
    img = load(tx['file'])
    rgb = np.asarray(img).astype(np.float32) / 255
    h, s, v = hsv_of(img)
    dh = np.abs(((h - tx['hue']) + 180) % 360 - 180)
    jersey = ok & (s > 0.3) & (dh < 28) & (v > 0.15)
    numz = np.isin(zone_of, [i for i, z in enumerate(spec['zones']) if z['kind'] == 'number'])
    hole = grow(numz & ~jersey, 3, numz) & numz
    rgb2, left = inpaint(rgb, hole, jersey)
    if left.any():   # an island all number: the zone's middle jersey colour
        rgb2[left] = np.median(rgb[numz & jersey], 0)
    # soften the fill so it doesn't streak
    hy, hx = np.nonzero(hole)
    nb = [(np.clip(hy + dy, 0, N - 1), np.clip(hx + dx, 0, N - 1)) for dy, dx in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1))]
    for _ in range(4):
        acc = sum(rgb2[y, x] * ok[y, x][:, None] for y, x in nb); cnt = sum(ok[y, x].astype(np.float32) for y, x in nb)
        rgb2[hy, hx] = acc / np.maximum(cnt, 1)[:, None]
    # the eyes: the opening goes plain white (the shader draws the iris); the lid colour is the skin around it
    lid, iris = [], []
    for zi, z in enumerate(spec['zones']):
        if z['kind'] != 'eye': continue
        inz = zone_of == zi; e = inz & eye_open
        if not e.any(): continue
        white = e & (v > 0.78) & (s < 0.22)
        skin = inz & ~grow(e, 3) & (s > 0.25) & (v > 0.3)
        irs = e & ~white & (v > 0.25) & (v < 0.85) & (s > 0.3)
        if skin.any(): lid.append(np.median(rgb[skin], 0))
        if irs.any(): iris.append(np.median(rgb[irs], 0))
        rgb2[e] = np.percentile(rgb[white], 60, axis=0) if white.any() else (0.95, 0.95, 0.95)
    Image.fromarray((np.clip(rgb2, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(root, tx['file']))
    def hx(c): return '#%02X%02X%02X' % tuple(int(x * 255 + 0.5) for x in c)
    print(tx['file'], 'painted out', int(hole.sum()), 'number texels;',
          'lid', hx(np.mean(lid, 0)) if lid else '-', 'iris', hx(np.mean(iris, 0)) if iris else '-')
