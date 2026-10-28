# Gauntlet loop 3, round 6 (PR #36, head 924c4b7)

Checked on the laptop in the check folder (PocketFootball-ws00): no merge, no fixes.

## Gates (tools\gates.ps1, verbatim)

```
gates 2026-10-26T07:43:12
env       PASS  (1s)
hygiene   PASS  (1s)
editmode  PASS  (124s)
build     PASS  (15s)
shots     PASS  (95s)
playmode  PASS  (171s)
```

EditMode: total 327, passed 327, failed 0. PlayMode: total 17, passed 17, failed 0.

## Pairs

- `round-6-concept01-pair.png`: concept 01 left, ours right
- `round-6-concept04-pair.png`: concept 04 left, ours right

## Merge captain's observations

concept01:
- Footballs are in hand now: QB 7 is cocked behind the helmet, and RBs 24 and 21 tuck it at the chest. ✓ QB 9's ball is hidden behind his raised arm, so his pose still reads as standing.
- The lineup is evenly spaced, with much less overlap than in round 5. ✓
- Helmets are smaller and the linemen bulkier, so the balance is closer to the concept. ✓
- Faces: the brows are heavier and some mouths show teeth, but at this size the faces still read small and alike.
- Ink outline: a thin dark edge shows on the helmets and some limbs, but it's faint. Overall the look is still glossy 3D, not the concept's bold cel lines.
- Still missing: helmet logos are outline only (no solid white arrowhead or S decal), no helmet centre stripe, no sleeve or pant stripes, no ARROWS / GRAINS titles, and the backdrop is still a yellow-orange gradient, not pale cream. The numbers on 68 and 64 are partly hidden by their arms.

concept04: no visible change from round 5 at this distance. Every player is still in frame.

Score: concept01 5.5/10, concept04 6/10.

## Fresh reviewer (a new subagent with no context, given only the two pair images), verbatim

## Concept 01 (six-player lineup)

Differences, most important first:
1. **Proportions** — Concept players are chibi with big heads/bulky bodies but crisp, chunky, well-defined limbs and hugely broad linemen. Game bodies are soft rounded blobs; limbs stubby and lumpy, hands/feet read as mitten shapes. Linemen look padded/doughy rather than powerful.
2. **Faces** — Concept faces are expressive anime-cartoon style with determined brows, grin on QB, defined jaw. Game faces are small, flat, simple dot eyes, neutral expressions, sunk deep in helmet.
3. **Helmets/logos** — Concept has full arrowhead "ARR" logo and clean "S" logo, white center stripe, thick bold facemasks. Game has crudely painted "ARR" without arrowhead, thin facemasks, no green helmet stripe; helmet shells look oversized and squashed.
4. **Rendering style** — Concept is painted cel-art with ink outlines, rim light, crisp shading. Game is plastic-looking 3D, soft shading, no outlines, glossy highlights, heavy blurry ground shadows.
5. **Uniforms/numbers** — Concept has sleeve stripes, pant stripes, white trim on Grains. Game numbers are blurry/distorted, most trim missing.
6. **Poses** — Concept poses are dynamic (QB throwing, runners mid-stride, linemen deep crouch). Game poses are stiff, upright; 9 just stands.
7. **Background/framing** — Concept: cream sky, faint stadium silhouette, team wordmarks, flat side-on view. Game: orange-yellow gradient, no wordmarks, no stadium, low perspective with converging yard lines.

Score: 4/10

## Concept 04 (broadcast gameplay view)

Differences, most important first:
1. **Players** — Concept players are small, crisp chibi figures with readable numbers and clear spacing. Game players are larger, blobby, no readable numbers; the line is a dense overlapping clump (~7v7) that looks crowded and messy.
2. **Camera** — Game camera is lower, so field fills less and is more foreshortened; concept shows more field with both sets of 30/40/50 numbers well spaced.
3. **Midfield logo** — Concept is a crisp "S" with wheat sheaves; game logo is a blocky, distorted "S" with heavy outline that reads poorly.
4. **Sidelines** — Concept has detailed sidelines (coaches, refs, pylons, Gatorade table, bench with readable numbers). Game has chunky placeholder figures and crude red/green block crowd.
5. **Scoreboard** — Layout matches closely; game version slightly wider, ARR logo cruder.
6. **Color/lighting** — Similar bright green turf, yellow first-down and blue scrimmage lines. Concept has richer texture and subtler line shading.
7. **Markers/detail** — A/B receiver markers and QB ring present in both; game lacks pylons and yard-marker poles, sideline down markers are oversized.

Score: 6/10
