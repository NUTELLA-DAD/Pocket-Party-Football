# New teams (Cacti, Whales, Bats, Snowbirds, Comets, Cobras)

FEATURES.teamPick: the league is game.json's `teams` (the default match, away then home) and `moreTeams`; the title's
SETTINGS page picks any two (AWAY / HOME rows, saved), and `PP_TEAMS=CAC,WHL` picks them for one run.

Every colour map of a new team is made from the old red maps (as at PR #77, before the rebrand), numpy and Pillow
only: `python3 tools/teams/build.py <work dir> [CAC WHL]`.
- `emblems/<SHORT>.png`: Kenny's emblem pictures. `python3 tools/rebrand/emblems.py emblems/CAC.png
  Assets/_Project/Resources/Logos/CAC.png` makes the team mark (midfield, scoreboard, banners).
- `build.py` runs `tools/rebrand/rebrand.py` on a red copy of each map to paint the old helmet decal out and lay the
  team emblem on (and again with the emblem in solid magenta, which tells exactly which texels it covers), then
  `kit.py` repaints the uniform. `REUSE=1` skips the rebrand step (minutes) and only repaints.
- `kit.py`: red, gold and white parts of the uniform get the team's colours from `kits.json`, by region of the rest
  pose (`regions`: helmet above `helmet` of the model's height and within `helmetMaxX` of the middle, pants between
  `pantsLow` and `pantsHigh`, socks below; the rest is the jersey). The red's own shading carries over. Skin, shoes,
  gloves, the facemask and the emblem are left alone. The cheerleader takes the team's `cheer` main and accent.
- `splat.py <texels.npz> <map.png> <out.png>`: a quick front / side / back look at a map on its model (texel points
  from `tools/rebrand/fbxuv.py`).
- A model added after the source revision (`SRC_LATER` in build.py, e.g. LeanLB): put its red map as `marks.py` left it
  (numbers and irises painted out, the old helmet decal still on) in `<work dir>/src/<folder>/`. `MODELS=LeanLB` builds
  only that model's maps. A `regions` entry named after a model folder overrides `player` for it (LeanLB's shoulder pads
  reach above the usual helmet line; the Linebacker's and TE's too, or the helmet colour bands their shoulders).

The Bats (`BAT`) and Snowbirds (`SNB`): marks from Kenny's mascot-only pictures (`emblems/BAT.png` the crimson-winged
bat, `emblems/SNB.png` the owl helmet emblem); the pictures with the team name under them are the join screen's.
Black helmets, crimson jerseys, black pants, white numbers; all white with ice-blue stripes and navy numbers.

The Comets (`CMT`) and Cobras (`COB`): marks from Kenny's wordless emblems (`emblems/CMT.png` the pink-tailed comet,
`emblems/COB.png` the coiled cobra); the wordmark pictures are the join screen's (`wordmarks.py`). Purple helmets,
hot-pink jerseys with white trim, white pants with purple stripes, white numbers edged purple (pink, not purple, so
they stand apart from the Cobras); near-black helmets and pants, purple
jerseys, electric-lime trim and stripes, lime numbers edged near-black. LeanLB's red source map (`<work>/src/LeanLB/`) is
the 0.jpg colour map inside Hi3D_Untitled_allparts_20261028_084912.fbx at 2048 px, with LeanLB_BaseColor.png's texels
copied in wherever LeanLB_Marks.png has a zone (numbers and irises painted out), the old decal still on.
