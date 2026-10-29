# The title's ROSTER button, made from the SETTINGS button: the word painted out with the plate's own navy (each row
# copied across from a column clear of the letters), then ROSTER in a heavy upright slab serif (DejaVu Serif Bold,
# fattened), cream with a dark ink edge and a drop, close to the block letters of the other buttons.
# Needs Pillow and the DejaVu fonts:  python tools/title/roster_button.py
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
title = os.path.join(root, 'Assets', '_Project', 'Resources', 'Title')
ttf = '/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf'
fat = 4   # the letters fattened this many px all round, toward the varsity block

src = Image.open(os.path.join(title, 'ButtonSettings.png')).convert('RGBA')
W, H = src.size
img = src.copy()
# the letters sit between x 230 and 740, y 50 and 160; column 190 is plain plate
clear_x, x0, x1, y0, y1 = 190, 200, W - 200, 44, 168
px = img.load()
for y in range(y0, y1):
    c = px[clear_x, y]
    for x in range(x0, x1):
        px[x, y] = c

text = 'ROSTER'
size = 104
font = ImageFont.truetype(ttf, size)
box = font.getbbox(text)
tw, th = box[2] - box[0], box[3] - box[1]
tx, ty = (W - tw) // 2 - box[0], (H - th) // 2 - box[1] - 4
edge = 7 + fat
ink = (6, 13, 43, 255)
layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(layer)
d.text((tx, ty + edge + 5), text, font=font, fill=ink, stroke_width=edge, stroke_fill=ink)   # the drop
d.text((tx, ty), text, font=font, fill=ink, stroke_width=edge, stroke_fill=ink)              # the edge
fill = Image.new('RGBA', (W, H), (0, 0, 0, 0))
ImageDraw.Draw(fill).text((tx, ty), text, font=font, fill=(255, 255, 255, 255), stroke_width=fat, stroke_fill=(255, 255, 255, 255))
# cream, a touch warmer toward the bottom (like SETTINGS)
grad = Image.new('RGBA', (W, H))
gp = grad.load()
for y in range(H):
    k = max(0.0, min(1.0, (y - ty) / max(1, th + 10)))
    col = (int(255 - 8 * k), int(250 - 22 * k), int(238 - 50 * k), 255)
    for x in range(W):
        gp[x, y] = col
layer.alpha_composite(Image.composite(grad, Image.new('RGBA', (W, H), (0, 0, 0, 0)), fill.split()[3]))
img.alpha_composite(layer)
out = os.path.join(title, 'ButtonRoster.png')
img.save(out)
print('wrote', out)
