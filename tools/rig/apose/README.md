# Rest the arms down (`apose`) and the Blender-free rig gauntlet

The players were modelled and rigged in a T-pose (arms straight out), but in the game their arms always hang near
their sides (ModelSkin lowers them about 78 degrees before any pose). Every pose therefore started from a big bend at
the shoulder, and the skin there (armpits, shoulder pads, the top of the sleeve) was crushed and stretched in every
frame: the "stretched textures" on the shoulders, even standing still.

`apose` moves each model's rest to arms hanging `phi` degrees out from straight down (25 by default), and relaxes the
skin around the shoulders as-rigid-as-possible against the modelled shape, so the painted texture keeps its proportions
there. The bones' rest moves with the arms; the weights, UVs and colour maps stay the same. ModelSkin finds any rest
pose (it measures where each arm points), so the game needs no change for it beyond the finger curl axes and the palm
direction, which now follow the rest arm (ModelSkin.Wear, palmRest).

Scripts (python3 with numpy and Pillow; only `apose_bl.py` needs Blender, `python3.11` with `pip install bpy==4.2.0`):

- `make_apose.py <in.fbx> <phi> <out.npz>`: the new rest (relaxed skin + bone matrices).
- `apose_bl.py -- <in.fbx> <apose.npz> <out.fbx>` (Blender): writes it into the FBX (bones' rest applied, skin and its
  normals moved), exported with the same settings as `../rigfix.py`.
- `verify.py <original.fbx> <new.fbx>`: the gauntlet on both, the new one's stretch measured against the original
  (T-pose) shape, which is what the texture was painted on. Prints the stretched-triangle count over the 15 gauntlet
  poses and over 20 held-out in-between poses (seeded), and the mean strain.
- `gnp.py <model.fbx> <texture.png|-> <outdir> [Pose,...]`: the gauntlet without Blender (`skel.py` poses the model
  the way ModelSkin does: the figure's pose angles, arms lowered, Adapt, Splay, feet levelled, the spine chain).
  NORENDER=1 only scores; HI=1 paints the stretched triangles magenta; ARM_HANG=0.06 mirrors FEATURES.armHang.
- `look.py <model.fbx> <apose.npz|-> <texture.png> <out.png> Pose,...`: a contact sheet with the stretched triangles
  in magenta, for a model and (optionally) its new rest before it is written.
- `fbxread.py`, `model.py`: a small binary FBX reader (mesh, UVs, skin clusters, bone parents).

Run order for a model: rigfix / handfix / soften (they expect the T-pose) -> marks (uvmap.py reads the rest pose:
run it on the T-pose FBX) -> `make_apose.py` -> `apose_bl.py` -> `verify.py`. The T-pose FBX of every model is in git
history before the apose commit; re-run the T-pose tools on that copy, then apose again.

## Results (phi 25, stretched triangles: 15 gauntlet poses + 20 held-out in-between poses)

| Model | T-pose rest | Arms-down rest |
|---|---|---|
| CB | 4463 + 2709 | 4390 + 2300 |
| Kicker | 4525 + 4491 | 3790 + 3422 |
| LeanLB | 6531 + 7405 | 4670 + 4291 |
| Linebacker | 11823 + 12749 | 10950 + 11310 |
| Player24 | 6927 + 7732 | 6577 + 6926 |
| QB | 5977 + 6763 | 5053 + 5112 |
| Referee | 8411 + 8425 | 7642 + 7085 |
| TE | 4465 + 4679 | 3736 + 3606 |
| WR | 4677 + 5069 | 4225 + 4149 |

All nine together: 117,821 -> 99,134 (-16%). Standing still, the shoulders went from hundreds of stretched triangles to
almost none (QB 182 -> 3). The Cheerleader stayed in her T-pose rest (3151 -> 3155: no gain).

Tried and dropped (numbers on the QB): re-fitting the weights to as-rigid-as-possible relaxed poses (5003 -> 4853, not
worth the change), and moving the shoulder pads' weight onto the clavicle (Linebacker 10949 -> 11382, worse). What is
left is mostly the hips and thighs in the deep crouches (Stance, Kneel, Dive) and the pads when an arm swings far
forward or back; a bent-leg rest, or extra helper bones at the hips and shoulders, are the next steps.
