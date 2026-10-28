# Every colour map of the new teams (Cacti, Whales), from the Chiefs-red maps as they were before the rebrand (they
# still carry the old helmet decal, which tools/rebrand finds and paints out):
#   1. rebrand.py paints the old decal out and lays the team's emblem on (on a red copy of the map)
#   2. the same with the emblem in solid magenta, to learn exactly which texels the emblem covers
#   3. kit.py repaints the uniform in the team's colours, leaving the emblem's texels alone
# usage: python3 tools/teams/build.py <work dir> [team ...]      (from the repo root; needs git for the source maps)
# REUSE=1 keeps the work dir's emblem maps from a run before (rebrand.py takes minutes) and only repaints the kits.
import sys, os, json, shutil, subprocess, numpy as np
from PIL import Image

SRC_REV = 'e60697c'   # PR #77's head: numbers and irises painted out, the old Chiefs decals still on
ROOT = 'Assets/_Project/Resources'
HERE = os.path.dirname(os.path.abspath(__file__))
work = os.path.abspath(sys.argv[1])
kits = json.load(open(os.path.join(HERE, 'kits.json')))
teams = sys.argv[2:] or list(kits['teams'])
spec = json.load(open('tools/rebrand/rebrand.json'))
# (model folder, the red map as it was at SRC_REV, the new map's name with {T} for the team, cheer?)
MAPS = [('Player24', 'Chiefs24_BaseColor', 'Player24{T}_BaseColor', False),
        ('Linebacker', 'LinebackerKC_BaseColor', 'Linebacker{T}_BaseColor', False),
        ('QB', 'QB_BaseColor', 'QB{T}_BaseColor', False),
        ('WR', 'WR_BaseColor', 'WR{T}_BaseColor', False),
        ('TE', 'TE_BaseColor', 'TE{T}_BaseColor', False),
        ('Kicker', 'Kicker_BaseColor', 'Kicker{T}_BaseColor', False),
        ('Cheerleader', 'Cheerleader_BaseColor', 'Cheerleader{T}_BaseColor', True)]

SRC_FOLDER = {'Player24': 'Chiefs24'}   # (the folder's name at SRC_REV, before the rename)

def run(*a):
    print('>', ' '.join(a)); subprocess.run(a, check=True)

os.makedirs(work, exist_ok=True)
for T in teams:
    for kind in ('emblem', 'mask'):
        r = os.path.join(work, T, kind)
        if os.environ.get('REUSE') == '1' and os.path.exists(os.path.join(r, 'spec.json')): continue   # (only kit.py again)
        os.makedirs(os.path.join(r, 'Logos'), exist_ok=True)
        logo = Image.open(os.path.join(ROOT, 'Logos', T + '.png')).convert('RGBA')
        if kind == 'mask':
            a = logo.getchannel('A'); logo = Image.new('RGBA', logo.size, (255, 0, 255, 255)); logo.putalpha(a)
        logo.save(os.path.join(r, 'Logos', T + '.png'))
        models = []
        for folder, chiefs, new, cheer in MAPS:
            m = next(m for m in spec['models'] if m['fbx'] == 'Models/%s/%s_Rigged.fbx' % (folder, folder))
            os.makedirs(os.path.join(r, 'Models', folder), exist_ok=True)
            fbx = os.path.join(r, m['fbx'])
            if not os.path.exists(fbx): shutil.copy(os.path.join(ROOT, m['fbx']), fbx)
            png = os.path.join(r, 'Models', folder, chiefs + '.png')
            with open(png, 'wb') as f:
                f.write(subprocess.run(['git', 'show', '%s:%s/Models/%s/%s.png' % (SRC_REV, ROOT, SRC_FOLDER.get(folder, folder), chiefs)], check=True, capture_output=True).stdout)
            hue = next(t['hue'] for t in m['textures'] if t['emblem'] == 'ARR')
            models.append({'fbx': m['fbx'], 'zones': m['zones'],
                           'textures': [{'file': 'Models/%s/%s.png' % (folder, chiefs), 'hue': hue, 'emblem': T}]})
        json.dump({'emblems': {T: 'Logos/%s.png' % T}, 'models': models}, open(os.path.join(r, 'spec.json'), 'w'))
        run(sys.executable, 'tools/rebrand/rebrand.py', os.path.join(r, 'spec.json'), r)
    for folder, chiefs, new, cheer in MAPS:
        npz = os.path.join(work, folder + '.npz')
        if not os.path.exists(npz): run(sys.executable, 'tools/rebrand/fbxuv.py', os.path.join(ROOT, 'Models', folder, folder + '_Rigged.fbx'), npz)
        a = np.asarray(Image.open(os.path.join(work, T, 'emblem', 'Models', folder, chiefs + '.png')).convert('RGB')).astype(np.float32)
        b = np.asarray(Image.open(os.path.join(work, T, 'mask', 'Models', folder, chiefs + '.png')).convert('RGB')).astype(np.float32)
        keep = np.clip(np.abs(a - b).max(-1) / 40, 0, 1)
        kp = os.path.join(work, T, folder + '_keep.png')
        Image.fromarray((keep * 255).astype(np.uint8)).save(kp)
        out = os.path.join(ROOT, 'Models', folder, new.replace('{T}', T) + '.png')
        args = [sys.executable, os.path.join(HERE, 'kit.py'), os.path.join(work, T, 'emblem', 'Models', folder, chiefs + '.png'), npz,
                os.path.join(HERE, 'kits.json'), T, out, 'keep=' + kp]
        if cheer: args.append('cheer')
        run(*args)
