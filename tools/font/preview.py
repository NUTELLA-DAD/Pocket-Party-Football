# A sheet of every Pocket Block glyph and a few HUD lines, drawn through FreeType (the same engine Unity uses), so a
# broken outline shows up here first. Needs Pillow:  python tools/font/preview.py [out.png]
import os
import sys

from PIL import Image, ImageDraw, ImageFont

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ttf = os.path.join(root, 'Assets', '_Project', 'Resources', 'Fonts', 'PocketBlock.ttf')
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, '.shots', 'font-preview.png')

lines = [
    (96, 'ABCDEFGHIJKLM'),
    (96, 'NOPQRSTUVWXYZ'),
    (96, '0123456789 :.,!?'),
    (72, 'abcdefghijklmnopqrstuvwxyz'),
    (64, "& + - / ( ) [ ] # % ' \" · ◄ ► ▲ ▼ ● ✓ — °"),
    (64, 'ARR 14  GRN 21  3RD 2:18  2ND & 10'),
    (48, 'HUDDLE 0:12     ARROWS CALLED ✓     GRAINS ...'),
    (40, 'TOUCHDOWN!   FIRST DOWN   INTERCEPTED!   Field goal is good'),
    (26, 'Scan to join, or open 192.168.1.20:8080  Code ABCD  ARR 3 · GRN 2 joined'),
    (18, 'Small text: the QB hikes, next huddle in 5. Tap HIKE when everyone is set.'),
]
W = 1800
H = sum(int(sz * 1.35) for sz, _ in lines) + 40
img = Image.new('RGB', (W, H), (16, 33, 90))
d = ImageDraw.Draw(img)
y = 20
for sz, text in lines:
    f = ImageFont.truetype(ttf, sz)
    d.text((24, y), text, font=f, fill=(255, 201, 40) if sz >= 64 else (251, 243, 220))
    y += int(sz * 1.35)
os.makedirs(os.path.dirname(out), exist_ok=True)
img.save(out)
print('wrote', out)
