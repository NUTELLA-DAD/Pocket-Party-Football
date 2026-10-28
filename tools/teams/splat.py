# A quick look at a colour map on its model without Blender: every texel's rest-pose point splatted from the front,
# the side and the back (nearest point wins). usage: python3 splat.py <texels.npz> <map.png> <out.png> [px per metre]
import sys, numpy as np
from PIL import Image
npz, png, out = sys.argv[1:4]
ppm = float(sys.argv[4]) if len(sys.argv) > 4 else 300
pos = np.load(npz)['pos']
img = np.asarray(Image.open(png).convert('RGB'))
if img.shape[:2] != pos.shape[:2]:
    img = np.asarray(Image.fromarray(img).resize(pos.shape[1::-1]))
ok = ~np.isnan(pos[..., 0]); P = pos[ok]; C = img[ok]
views = [(0, 1, -1), (1, 0, 1), (0, 1, 1)]   # (screen-x axis, depth axis, facing): front (-Y forward), side, back
x0, x1 = P[:, :2].min(), P[:, :2].max()
Wv = int((x1 - x0) * ppm) + 4; Hv = int(P[:, 2].max() * ppm) + 4
panels = []
for ax, dep, face in views:
    sx = ((P[:, ax] * (-face if ax == 0 else 1) - x0) * ppm).astype(int) + 2
    sy = (Hv - 2 - P[:, 2] * ppm).astype(int)
    d = P[:, dep] * face   # nearer the viewer = smaller
    o = np.argsort(-d)
    can = np.full((Hv, Wv, 3), 40, np.uint8)
    can[sy[o], sx[o]] = C[o]
    panels.append(can)
Image.fromarray(np.concatenate(panels, 1)).save(out)
