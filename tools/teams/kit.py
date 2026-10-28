# A new team's kit from a old red colour map: every red, gold and white part of the uniform repainted in the team's
# own colours, region by region (helmet, jersey, pants), keeping the map's baked shading. Skin, eyes, shoes, gloves
# and the facemask are left alone. The helmet's old decal is painted out and the team's emblem laid on afterwards by
# tools/rebrand (rebrand.py), so run this first.
# usage: python3 kit.py <in_BaseColor.png> <texels.npz> <kits.json> <team> <out.png> [cheer] [keep=<mask.png>]
#   keep: a grey mask of texels left exactly as they are (the team's emblem, already laid on by build.py)
#   texels.npz: the rest-pose point under every texel (tools/rebrand/fbxuv.py; NaN where no triangle)
#   kits.json:  per team the helmet, jersey, pants, stripe and trim colours (and the cheer squad's), see README.md
import sys, json, numpy as np
from PIL import Image, ImageFilter

src, texels, kits, team, out = sys.argv[1:6]
cheer = 'cheer' in sys.argv[6:]
keepPath = next((a[5:] for a in sys.argv[6:] if a.startswith('keep=')), None)
K = json.load(open(kits))
kit = K['teams'][team]
rgb = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255
H, W = rgb.shape[:2]
hsv = np.asarray(Image.open(src).convert('RGB').convert('HSV')).astype(np.float32)
h = hsv[..., 0] * 360 / 256; s = hsv[..., 1] / 255; v = hsv[..., 2] / 255
hr = np.where(h > 180, h - 360, h)

def band(x, a0, a1, b0, b1):
    return np.clip(np.minimum((x - a0) / (a1 - a0), (b1 - x) / (b1 - b0)), 0, 1)
def hexrgb(c):
    c = c.lstrip('#'); return np.array([int(c[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255

# ---- the colour classes (soft) ----
hot = np.clip((s - 0.72) / 0.08, 0, 1) * np.clip((v - 0.68) / 0.08, 0, 1)   # the orange fringe between red and gold
red = np.maximum(band(hr, -25, -15, 6, 12), hot * band(hr, 5, 8, 18, 22)) * np.clip((s - 0.30) / 0.15, 0, 1) * np.clip((v - 0.12) / 0.1, 0, 1)
gold = np.maximum(band(h, 30, 35, 55, 62) * np.clip((s - 0.35) / 0.15, 0, 1), hot * band(hr, 18, 22, 34, 36))
red = np.minimum(red, 1 - gold)
white = np.clip((0.16 - s) / 0.06, 0, 1) * np.clip((v - 0.62) / 0.08, 0, 1)
# skin is orange-ish with mid saturation: keep it out of red/gold
skin = band(h, 12, 18, 34, 40) * band(s, 0.12, 0.22, 0.62, 0.72) * (1 - hot)
gold = gold * (1 - skin); red = red * (1 - skin)

# ---- the regions, from the rest pose (Blender space: Z up) ----
d = np.load(texels); pos = d['pos']
if pos.shape[:2] != (H, W):   # (the texel map may be made at another size)
    yy = (np.arange(H) * pos.shape[0] / H).astype(int); xx = (np.arange(W) * pos.shape[1] / W).astype(int)
    pos = pos[yy][:, xx]
ok = ~np.isnan(pos[..., 0])
Z = np.nan_to_num(pos[..., 2], nan=-1.0)
zs = Z[ok]; z0, z1 = np.percentile(zs, 0.2), np.percentile(zs, 99.8)
hz = (Z - z0) / max(z1 - z0, 1e-6)   # 0 at the soles, 1 at the top of the helmet
R = K['regions'].get('cheer' if cheer else 'player')
def soft_above(x, edge, w=0.01): return np.clip((x - edge) / w + 0.5, 0, 1)
Xa = np.abs(np.nan_to_num(pos[..., 0]))
helmet = soft_above(hz, R['helmet']) * (Xa < R.get('helmetMaxX', 9)) * ok   # (not the T-posed arms beside it)
pants = soft_above(hz, R['pantsLow']) * (1 - soft_above(hz, R['pantsHigh'])) * ok
jersey = np.clip(ok - helmet - pants, 0, 1)
socks = (1 - soft_above(hz, R['pantsLow'])) * ok
# texels with no 3D point (the gutters between UV islands) take the region of the nearest island texel
def bleed(m):
    m = m.copy(); has = ok.copy()
    for _ in range(12):
        g = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))).astype(np.float32) / 255
        n = np.asarray(Image.fromarray(has.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(5))) > 0
        fill = n & ~has; m[fill] = g[fill]; has = n
    return m
helmet, pants, socks = bleed(helmet), bleed(pants), bleed(socks)
jersey = np.clip(1 - helmet - pants - socks, 0, 1)

# ---- repaint, keeping the shading: each class's brightness over its median brightness scales the new colour ----
def shade(mask, ref):   # (mask: which class's reference this is; only ref is used)
    return np.clip(v / ref, 0, 1.35)[..., None]
def paint(col, sh):
    c = hexrgb(col)[None, None, :] * np.minimum(sh, 1.0)
    over = np.clip(sh - 1.0, 0, 0.35) / 0.35   # (a highlight past the median lifts toward white, as on the red)
    return c + (1 - c) * over * 0.5

RED_V = float(np.median(v[red > 0.9])) if (red > 0.9).any() else 0.8
GOLD_V = float(np.median(v[gold > 0.9])) if (gold > 0.9).any() else 0.9
WHITE_V = float(np.median(v[white > 0.9])) if (white > 0.9).any() else 0.9
shR, shG, shW = shade(red, RED_V), shade(gold, GOLD_V), shade(white, WHITE_V)
# a red texel's share of red (the rest is the white it is mixed with at an edge)
frac = np.clip(s / 0.9, 0, 1)[..., None]

res = rgb.copy()
keep = np.asarray(Image.open(keepPath).convert('L').resize((W, H))).astype(np.float32) / 255 if keepPath else np.zeros((H, W), np.float32)
def lay(mask, px):
    global res
    m = (np.clip(mask, 0, 1) * (1 - keep))[..., None]
    res = res * (1 - m) + px * m

if cheer:
    C = kit['cheer']
    lay(red, paint(C['main'], shR) * frac + (1 - frac) * np.minimum(v, 1)[..., None])
    lay(gold, paint(C['accent'], shG))
else:
    redFor = helmet[..., None] * hexrgb(kit['helmet']) + jersey[..., None] * hexrgb(kit['jersey']) + pants[..., None] * hexrgb(kit['pantsStripe']) + socks[..., None] * hexrgb(kit['socks'])
    c = redFor * np.minimum(shR, 1.0); c = c + (1 - c) * np.clip(shR - 1.0, 0, 0.35) / 0.35 * 0.5
    lay(red, c * frac + (1 - frac) * np.minimum(v, 1)[..., None])
    goldFor = helmet[..., None] * hexrgb(kit.get('helmetTrim', kit['trim'])) + (1 - helmet[..., None]) * hexrgb(kit['trim'])
    c = goldFor * np.minimum(shG, 1.0); c = c + (1 - c) * np.clip(shG - 1.0, 0, 0.35) / 0.35 * 0.5
    lay(gold, c)
    # the white pants (and only the pants) take the pants colour
    lay(white * pants, paint(kit['pants'], shW))

Image.fromarray((np.clip(res, 0, 1) * 255 + 0.5).astype(np.uint8)).save(out, optimize=True)
print(team, src, '->', out, 'red %.2f gold %.2f white %.2f' % (RED_V, GOLD_V, WHITE_V))
