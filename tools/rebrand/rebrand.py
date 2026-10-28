# Paints the old team decals (the arrowhead, the green S) out of the colour maps and puts our own team emblems on in
# their place (in place, so run it once on the original maps). Everything per model lives in rebrand.json.
# usage: python3 rebrand.py rebrand.json <Resources dir> [preview dir]
#   For each model: its FBX is read straight from the file (fbxuv.py), then for each zone (a side of the helmet, the
#   cheerleader's chest) and each colour map:
#   1. the zone's surface: texels facing the zone ("n"), inside its box (metres: u across, v up, as seen from outside),
#      and on the outermost layer (a liner behind is left alone; "cut" boxes keep a facemask out);
#   2. the old decal: whatever there is not the map's base colour ("hue"), joined up and kept where it holds white
#      (a vent is dark only), grown a little to take its soft edge;
#   3. the decal is filled with the base colour around it (its shading carried across), then the team's emblem is
#      laid over it, centred on the old decal and as big as it (planar, straight on).
import sys, os, json, numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fbxuv

spec_path, root = sys.argv[1], sys.argv[2]
preview = sys.argv[3] if len(sys.argv) > 3 else None
spec = json.load(open(spec_path))
CELL = 0.004   # the decal mask's grid (m)


def hsv(rgb):
    hsv = np.asarray(Image.fromarray(rgb).convert('HSV')).astype(np.float32)
    return hsv[..., 0] * 360 / 256, hsv[..., 1] / 255, hsv[..., 2] / 255


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


def dilate(m, k):
    for _ in range(k):
        g = m.copy()
        g[1:] |= m[:-1]; g[:-1] |= m[1:]; g[:, 1:] |= m[:, :-1]; g[:, :-1] |= m[:, 1:]
        m = g
    return m


def components(m):
    # labels of a boolean grid's 4-connected pieces (flood fill, piece by piece)
    img = Image.fromarray((m * 255).astype(np.uint8)).copy()
    lab = np.zeros(m.shape, np.int32); n = 0
    while True:
        ys, xs = np.nonzero((np.asarray(img) == 255))
        if len(ys) == 0: return lab, n
        n += 1
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), 100)
        a = np.asarray(img)
        lab[(a == 100)] = n
        img = Image.fromarray(np.where(a == 100, 50, a).astype(np.uint8)).copy()


def fill_holes(m):
    pad = np.zeros((m.shape[0] + 2, m.shape[1] + 2), np.uint8); pad[1:-1, 1:-1] = m * 255
    img = Image.fromarray(pad).copy(); ImageDraw.floodfill(img, (0, 0), 100)
    return np.asarray(img)[1:-1, 1:-1] != 100


def in_poly(poly, x, y):
    inside = np.zeros(x.shape, bool)
    for i in range(len(poly)):
        a, b = poly[i], poly[i - 1]
        inside ^= ((a[1] > y) != (b[1] > y)) & (x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1] + 1e-12) + a[0])
    return inside


def sample(rgba, x, y):
    # bilinear lookup, x, y in 0..1 (y down)
    h, w = rgba.shape[:2]
    fx = np.clip(x * w - 0.5, 0, w - 1.001); fy = np.clip(y * h - 0.5, 0, h - 1.001)
    x0 = fx.astype(int); y0 = fy.astype(int); tx = (fx - x0)[:, None]; ty = (fy - y0)[:, None]
    a = rgba[y0, x0] * (1 - tx) + rgba[y0, x0 + 1] * tx
    b = rgba[y0 + 1, x0] * (1 - tx) + rgba[y0 + 1, x0 + 1] * tx
    return a * (1 - ty) + b * ty


emblems = {k: np.asarray(Image.open(os.path.join(root, v)).convert('RGBA')).astype(np.float32) / 255
           for k, v in spec['emblems'].items()}
# each emblem's own box inside its square PNG (0..1), so it is sized by what is drawn, not by the clear margin
eboxes = {}
for k, e in emblems.items():
    ys, xs = np.nonzero(e[..., 3] > 0.1)
    eboxes[k] = (xs.min() / e.shape[1], xs.max() / e.shape[1], ys.min() / e.shape[0], ys.max() / e.shape[0])

for model in spec['models']:
    fbx = os.path.join(root, model['fbx'])
    maps = [(t, np.asarray(Image.open(os.path.join(root, t['file'])).convert('RGB')).copy()) for t in model['textures']]
    N = maps[0][1].shape[0]
    pos, nrm = uvmap(fbx, N)
    ok = ~np.isnan(pos[..., 0]); P = np.nan_to_num(pos)
    for zi, z in enumerate(model['zones']):
        n = np.asarray(z['n'], float); n /= np.linalg.norm(n)
        up = np.array([0, 0, 1.0]); r = np.cross(up, n); r /= np.linalg.norm(r); up = np.cross(n, r)
        u, v, dep = P @ r, P @ up, P @ n
        u0, u1, v0, v1 = z['box']
        inbox = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
        for poly in z.get('keep', []):   # polygons the decal search leaves alone (a stripe touching the decal)
            inbox &= ~in_poly(np.asarray(poly, float), u, v)
        # the surface is taken a little wider than the search box, so a bigger emblem isn't clipped at its edge
        pad = z.get('pad', 0.04)
        U0, U1, V0, V1 = u0 - pad, u1 + pad, v0 - pad, v1 + pad
        sel = ok & ((nrm @ n) > z.get('facing', 0.2)) & (u >= U0) & (u < U1) & (v >= V0) & (v < V1)
        for c0, c1, d0, d1 in z.get('cut', []):   # boxes left alone (a facemask that sits on the shell)
            sel &= ~((u >= c0) & (u < c1) & (v >= d0) & (v < d1))
        # the outermost layer: within 2 cm of the furthest-out texel in its 1 cm column
        gu = ((u - U0) / 0.01).astype(int).clip(0); gv = ((v - V0) / 0.01).astype(int).clip(0)
        GW, GH = int((U1 - U0) / 0.01) + 1, int((V1 - V0) / 0.01) + 1
        gu = gu.clip(0, GW - 1); gv = gv.clip(0, GH - 1)
        top = np.full((GH, GW), -9.0)
        np.maximum.at(top, (gv[sel], gu[sel]), dep[sel])
        shell = sel.copy(); shell[sel] = dep[sel] >= top[gv[sel], gu[sel]] - z.get('layer', 0.02)
        # the decal mask on a fine grid
        cu = ((u - u0) / CELL).astype(int); cv = ((v1 - v) / CELL).astype(int)
        MW, MH = int((u1 - u0) / CELL) + 1, int((v1 - v0) / CELL) + 1
        for t, rgb in maps:
            h, s, val = hsv(rgb)
            dh = np.abs(((h - t['hue']) + 180) % 360 - 180)
            base = (dh < z.get('hueTol', 28)) & (s > 0.3) & (val > 0.15)
            cand = shell & inbox & ~base
            white = cand & (val > 0.7) & (s < 0.3)
            grid = np.zeros((MH, MW), bool); grid[cv[cand], cu[cand]] = True
            wgrid = np.zeros((MH, MW), np.int32); np.add.at(wgrid, (cv[white], cu[white]), 1)
            joined = fill_holes(dilate(grid, z.get('join', 3)))
            lab, nl = components(joined)
            mask = np.zeros_like(joined)
            for i in range(1, nl + 1):
                piece = lab == i
                if wgrid[piece].sum() >= z.get('minWhite', 40): mask |= piece
            mask = dilate(mask, z.get('edge', 2))
            if not mask.any():
                print(t['file'], 'zone', zi, 'no decal found'); continue
            hole = shell & inbox & mask[cv.clip(0, MH - 1), cu.clip(0, MW - 1)]
            # 1. the base colour carried across the hole: per 1 cm column, the mean of the plain texels, spread inwards
            known = shell & base & ~hole
            acc = np.zeros((GH, GW, 3)); cnt = np.zeros((GH, GW))
            np.add.at(acc, (gv[known], gu[known]), rgb[known].astype(float)); np.add.at(cnt, (gv[known], gu[known]), 1)
            have = cnt > 0
            col = np.where(have[..., None], acc / np.maximum(cnt, 1)[..., None], 0)
            for _ in range(GW + GH):
                if have.all(): break
                a2 = np.zeros_like(col); c2 = np.zeros((GH, GW))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    sh = np.roll(np.roll(col * have[..., None], dy, 0), dx, 1); sc = np.roll(np.roll(have, dy, 0), dx, 1)
                    a2 += sh; c2 += sc
                new = ~have & (c2 > 0)
                col[new] = a2[new] / c2[new][:, None]; have |= new
            for _ in range(2):   # soften
                col = (col + np.roll(col, 1, 0) + np.roll(col, -1, 0) + np.roll(col, 1, 1) + np.roll(col, -1, 1)) / 5
            fill = col[gv[hole], gu[hole]]
            out = rgb.astype(np.float32)
            out[hole] = fill
            # 2. the emblem over the old decal's box
            ys, xs = np.nonzero(mask)
            bu0, bu1 = u0 + xs.min() * CELL, u0 + (xs.max() + 1) * CELL
            bv1, bv0 = v1 - ys.min() * CELL, v1 - (ys.max() + 1) * CELL
            ex0, ex1, ey0, ey1 = eboxes[t['emblem']]
            aspect = (ex1 - ex0) / (ey1 - ey0)
            eh = min((bv1 - bv0) * z.get('scale', 1.1), (bu1 - bu0) * z.get('scale', 1.1) / aspect, z.get('maxH', 1))
            ew = eh * aspect
            cu0 = (bu0 + bu1) / 2 + z.get('du', 0); cv0 = (bv0 + bv1) / 2 + z.get('dv', 0)
            on = shell & (np.abs(u - cu0) <= ew / 2) & (np.abs(v - cv0) <= eh / 2)
            ex = ex0 + (u[on] - (cu0 - ew / 2)) / ew * (ex1 - ex0)
            ey = ey0 + ((cv0 + eh / 2) - v[on]) / eh * (ey1 - ey0)
            e = sample(emblems[t['emblem']], ex, ey)
            # keep the surface's light and shade under the emblem
            under = out[on] / 255
            lum = under @ np.array([0.3, 0.59, 0.11]); ref = np.median(fill @ np.array([0.3, 0.59, 0.11])) / 255
            shade = np.clip(lum / max(ref, 1e-3), 0.8, 1.1)[:, None]
            a = e[:, 3:4]
            out[on] = (under * (1 - a) + e[:, :3] * shade * a) * 255
            rgb[...] = np.clip(out + 0.5, 0, 255).astype(np.uint8)
            print('%s zone %d: decal %d texels, emblem %.2f x %.2f m at (%.2f, %.2f)' % (t['file'], zi, hole.sum(), ew, eh, cu0, cv0))
            if preview:
                # what the zone looks like now, straight on
                res = 600; scl = res / max(U1 - U0, V1 - V0)
                can = np.zeros((int((V1 - V0) * scl) + 1, int((U1 - U0) * scl) + 1, 3), np.uint8) + 40
                o = np.argsort(dep[sel])
                vv = ((V1 - v[sel][o]) * scl).astype(int); uu = ((u[sel][o] - U0) * scl).astype(int)
                can[vv, uu] = rgb[sel][o]
                name = os.path.splitext(os.path.basename(t['file']))[0]
                Image.fromarray(can).save(os.path.join(preview, '%s_zone%d.png' % (name, zi)))
    for t, rgb in maps:
        Image.fromarray(rgb).save(os.path.join(root, t['file']), optimize=True)
        print('wrote', t['file'])
