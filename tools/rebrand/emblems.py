# Cleans Kenny's two helmet emblems into the game's team marks: crops to the emblem, drops the stray specks around it
# (keeps only the piece joined to the middle), and saves a square 512 px PNG with a clear background.
# usage: python3 emblems.py <in.png> <out.png>
import sys, numpy as np
from PIL import Image, ImageDraw

src, out = sys.argv[1], sys.argv[2]
im = Image.open(src).convert('RGBA')
a = np.asarray(im)[..., 3]
solid = Image.fromarray(((a > 128) * 255).astype(np.uint8)).copy()
w, h = solid.size
# the emblem is the piece under the most solid pixel nearest the middle
ys, xs = np.nonzero(np.asarray(solid) > 0)
k = np.argmin((xs - w / 2) ** 2 + (ys - h * 0.55) ** 2)
ImageDraw.floodfill(solid, (int(xs[k]), int(ys[k])), 77)
keep = np.asarray(solid) == 77
# fill its holes (pixels not reachable from the border through clear pixels)
outside = Image.fromarray(((~keep) * 255).astype(np.uint8)).copy()
pad = Image.new('L', (w + 2, h + 2), 255); pad.paste(outside, (1, 1))
ImageDraw.floodfill(pad, (0, 0), 77)
keep |= np.asarray(pad)[1:-1, 1:-1] != 77
# grow by 2 px so the soft edge stays
k2 = keep.copy()
for dy in (-2, -1, 0, 1, 2):
    for dx in (-2, -1, 0, 1, 2): k2 |= np.roll(np.roll(keep, dy, 0), dx, 1)
rgba = np.asarray(im).copy()
rgba[..., 3] = np.where(k2, rgba[..., 3], 0)
ys, xs = np.nonzero(rgba[..., 3] > 8)
rgba = rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
eh, ew = rgba.shape[:2]
side = max(eh, ew) + 8
sq = np.zeros((side, side, 4), np.uint8)
sq[(side - eh) // 2:(side - eh) // 2 + eh, (side - ew) // 2:(side - ew) // 2 + ew] = rgba
Image.fromarray(sq, 'RGBA').resize((512, 512), Image.LANCZOS).save(out, optimize=True)
print(out, 'emblem', ew, 'x', eh)
