# Team branding (Arrows and Grains)

The teams are our own: the **Arrows** (red, `ARR`) and the **Grains** (green, `GRN`). No real team names or logos.
Numpy and Pillow only, no Blender.

- `emblems.py <in.png> <out.png>`: turns an emblem picture into a team mark (crops it, drops stray specks, square
  512 px, clear background). The marks live at `Assets/_Project/Resources/Logos/<SHORT>.png`; `TeamMarks` loads them
  for the midfield logo, the scoreboard and the banners.
- `fbxuv.py <model.fbx> <out.npz>`: like `tools/rig/uvmap.py` but reads the FBX itself: the rest-pose point and normal
  under every texel of the colour map.
- `rebrand.py rebrand.json Assets/_Project/Resources [preview dir]`: for every colour map in `rebrand.json`, finds the
  old decal in each zone (both helmet sides; the cheerleader's chest), fills it with the colour around it and lays the
  team's emblem over it. It edits the maps in place, so run it once on maps that still carry the old decals (for
  example after a model is re-exported). Zone keys: `n` facing, `box` [u0, u1, v0, v1] metres (u across as seen from
  outside, v up), `maxH` emblem height cap, `cut` boxes left alone (a facemask lying on the shell), `keep` polygons the
  decal search skips (a stripe touching the decal), `hueTol`, `minWhite`, `join`, `edge`, `pad`, `scale`, `du`/`dv`.

A new team: put its emblem through `emblems.py`, then add its colour maps to `rebrand.json` with its own `emblem` key.
