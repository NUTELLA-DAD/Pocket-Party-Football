# The join screen's team cards: Kenny's full emblem pictures (the mascot with the team name under it), trimmed to what
# is drawn and sized for the TV (Resources/Emblems/<SHORT>.png) and the phones (StreamingAssets/phone/emblems/).
# Pillow only. Run: python3 tools/teams/wordmarks.py <picture.png> <SHORT>
# (Logos/<SHORT>.png, the mascot-only mark for the field and helmets, is a different picture: tools/rebrand/emblems.py)
import os, sys
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
TV = (640, 480)      # largest size on the TV card (the card scales it down)
PHONE = (320, 240)   # the phone's team buttons
PAD = 0.03           # a little clear edge round the trimmed picture


def fit(im, box):
    w, h = im.size
    k = min(box[0] / w, box[1] / h, 1.0)
    return im.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)


def main(src, short):
    im = Image.open(src).convert('RGBA')
    bbox = im.getchannel('A').point(lambda a: 255 if a > 8 else 0).getbbox()
    if bbox:
        im = im.crop(bbox)
    p = round(max(im.size) * PAD)
    framed = Image.new('RGBA', (im.width + 2 * p, im.height + 2 * p), (0, 0, 0, 0))
    framed.paste(im, (p, p))
    for box, out in ((TV, os.path.join(ROOT, 'Assets/_Project/Resources/Emblems', short + '.png')),
                     (PHONE, os.path.join(ROOT, 'Assets/StreamingAssets/phone/emblems', short + '.png'))):
        os.makedirs(os.path.dirname(out), exist_ok=True)
        fit(framed, box).save(out, optimize=True)
        print('wrote', os.path.relpath(out, ROOT))


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit('usage: wordmarks.py <picture.png> <SHORT>')
    main(sys.argv[1], sys.argv[2].upper())
