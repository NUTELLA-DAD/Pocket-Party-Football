# The Grains' kit, lighter and with gold: on each colour map listed, the team green turns light green (hue shifted
# toward yellow, brighter) and the white cloth below the head turns gold (pants, sleeve and pant stripes; the
# facemask, the helmet emblem, socks and shoes keep their colours). In place, so run it once on the green maps.
# usage: python3 recolour.py recolour.json <Resources dir>
import sys, os, json, numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rebrand_uv import uvmap

spec = json.load(open(sys.argv[1])); root = sys.argv[2]
g = spec['green']; gold = np.array([int(spec['gold'][i:i + 2], 16) for i in (1, 3, 5)], np.float32) / 255
for m in spec['models']:
    imgs = [(f, Image.open(os.path.join(root, f)).convert('RGB')) for f in m['textures']]
    pos, _ = uvmap(os.path.join(root, m['fbx']), imgs[0][1].size[0])
    z = np.nan_to_num(pos[..., 2], nan=-1)
    for f, img in imgs:
        hsv = np.asarray(img.convert('HSV')).astype(np.float32)
        h, s, v = hsv[..., 0] * 360 / 256, hsv[..., 1] / 255, hsv[..., 2] / 255
        green = (np.abs(((h - g['hue']) + 180) % 360 - 180) < g['tol']) & (s > 0.25) & (v > 0.1)
        h2 = np.where(green, (h + g['shift']) % 360, h)
        s2 = np.where(green, s * g['sat'], s)
        v2 = np.where(green, np.minimum(1, v * g['gain'] + g['lift']), v)
        out = np.asarray(Image.fromarray(np.stack([h2 / 360 * 256, s2 * 255, v2 * 255], -1).clip(0, 255).astype(np.uint8), 'HSV').convert('RGB')).astype(np.float32) / 255
        lo, hi = m['goldZ']
        white = (s < 0.22) & (v > 0.55) & (z >= lo) & (z <= hi)
        out[white] = np.clip(gold * (v[white] / 0.92)[:, None], 0, 1)
        Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(os.path.join(root, f), optimize=True)
        print(f, 'green', int(green.sum()), 'gold', int(white.sum()))
