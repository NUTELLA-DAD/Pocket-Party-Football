# Rig fixes (rig gauntlet thread)

Blender as a Python module (`pip install bpy`), run with `python script.py -- args`.

- `rigfix.py -- in.fbx params.json out.fbx`: re-places the 22 Humanoid joints and rebuilds every skin weight from
  the T-pose shape (arms along X, legs down Z, model faces -Y, its left +X). Only the helmet keeps its old rigid
  Head weights (`headKeep`: old Head weight above this stays rigid). All mesh-specific numbers live in the JSON:
  `chiefs24.json` is the tuned set for Chiefs #24 (joint heights/widths in metres, blend widths).
  Exports with the same FBX settings as the original (Unity axes, no leaf bones, texture not embedded).
- `gauntlet.py model.fbx texture.png outdir [Pose,Pose]`: poses the model exactly the way the game's ModelSkin does
  (FootballFigure pose angles, arms lowered 12 deg, raised arms splayed) and renders each pose; prints a
  squash/stretch triangle count. Env: ADAPT=1 FOOT_LEVEL=0.7 PALM_ALONG=0.55 PALM_IN=<0.45*forearm m> mirror the
  ModelSkin.cs arm/feet/ball fixes; CLOSE=1, LEGS=1, HAND=1 are close-up cameras; NORENDER=1 only scores.
- `compare.py -- out.png dirA dirB view,view Pose,Pose`: side-by-side sheet for a reviewer.

For another model (e.g. the linebacker): copy chiefs24.json, set the joints from slices of that mesh (shoulder at the
sleeve/pad centre, elbow mid bare arm, wrist at the wristband, knee at the pants hem, ankle inside the shoe top), and
check `headKeep` keeps the helmet and facemask rigid.

`referee.json`: the referee (Resources/Models/Referee): no helmet, so `headKeep` keeps the head and cap rigid (rigged
rigid above 1.245 m before rigfix) and `padShift` is 0. Its import/prep/rig scripts are in the project files under
models/referee/scripts.

`te.json`: the tight end (Resources/Models/TE, Kenny's Chiefs #88). Its import/prep/rig scripts are in the project
files under models/te/scripts; after rigfix it goes through handfix.py (`TE` in hands.json, with a `Right` override:
its right hand sits further back than the left).

## Hands (`handfix.py`, `fcore.py`, `hands.json`)
`python handfix.py -- in.fbx hands.json <Model> out.fbx` (QB, WR, TE, Kicker, Chiefs24, Referee, Linebacker):
- pulls a sixth finger (`extra`: its outline seen from above, metres on the model's left side; `base`: where it
  meets the palm) back into the palm. Only the QB, WR, TE and Kicker have one. A `Left`/`Right` entry inside a model overrides its numbers for that hand.
- adds four bones per hand, children of `<Side>Hand`: `<Side>Thumb` > `<Side>ThumbTip` and `<Side>Fingers` >
  `<Side>FingersTip` (the four fingers bend together). `kline`: the knuckle line seen from above, `midOff`: how far
  past it the fingers' middle joint is, `thumb`/`thumbX`/`thumbXmax`: the line the thumb lies below and its reach
  along the arm. The hand's old weight is split between the hand and the new bones; nothing else changes (the
  gauntlet's numbers are the same before and after with the fingers straight).

`gauntlet.py` env: `FINGERS=fingers,tips,thumb,thumbTip` bends those bones toward the palm (degrees, e.g.
`45,40,25,15` for a hand closing on the ball); `HANDCAM=Left|Right` renders close-ups of that hand.
The Cheerleader (pom-poms) has no finger bones.
