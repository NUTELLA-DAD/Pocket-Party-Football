# Art direction (from Kenny's concept art, 2026-10-25)

These four pictures are the target look. They replace the "three concept looks" step planned for M4: the direction is chosen.

| File | What it sets |
| --- | --- |
| `concepts/01-team-lineup.png` | Character style and body types for both teams |
| `concepts/02-action-shot.png` | Action, effects, stadium mood |
| `concepts/03-position-poses.png` | Key poses: receiver catch and dive, linebacker stance, kicker follow-through |
| `concepts/04-in-game-camera.png` | THE in-game view: camera, field markings, HUD |

## How we get there: the gauntlet loop (Kenny's standing request)
For the graphics phase, run the gauntlet loop until the game matches these pictures. If a gauntlet loop skill or workflow exists on Kenny's machine, use that. Otherwise each round is:
1. Take seeded screenshots of fixed scenes that match each concept: lineup (01), action (02), poses (03), and the in-game view (04).
2. Put each screenshot next to its concept picture and list every visible difference: proportions, colours, outlines, camera, HUD, field, crowd, effects.
3. Fix the biggest differences, then take new screenshots.
4. Keep going until a fresh reviewer, comparing the pairs with no other context, finds no meaningful difference. Then show Kenny the side-by-sides.

## Characters
- Chibi proportions: about 2.5 to 3 heads tall, oversized glossy helmets with a white face mask, big expressive anime-style eyes, big jersey numbers on chest and shoulders.
- Body type by position: linemen are wide and bulky (big arms, black elbow bands, black gloves), QBs and kickers slim, RBs and receivers compact and athletic. Same head size for all, so silhouettes read by width.
- Flat cel shading with soft highlights, clean dark outlines, saturated kit colours. Arrows: red jersey, white pants with red/gold stripes, red cleats. Grains: green jersey with white stripes, white pants with green stripes, green cleats.
- Poses to animate first: QB throw and set, RB carry and cut, receiver leaping catch and diving catch, lineman stance and engaged block, linebacker read stance, diving tackle, kicker follow-through.

## Field and camera (concept 04)
- Elevated sideline camera looking across the field, measured from the picture at about 16 degrees down and about 36 yards across (both numbers live in `game.json`), so all 22 players fit, with both sidelines visible. The field runs left to right; the camera only slides along it.
- Painted stripes of mown grass, big yard numbers with arrows, hash marks, centre-field team logo.
- Blue line of scrimmage, yellow first-down line, orange down markers on the sideline.
- Sideline crowd of small team-coloured figures, coaches, refs, benches and tents; players on the near bench in the foreground.
- Human-controlled player: a cyan ring under the feet and a small triangle marker above the head.
- Receiver markers float over eligible receivers (the concept uses A / B badges; we use the phone's shapes △ ○ □ ◇ ☆ in the same badge style).
- Scoreboard bar at top centre: team badge + abbreviation + score for each side, quarter, game clock, down and distance (`ARR 14 | GRN 21 | 3RD 2:18 | 2ND & 10`).

## Effects and mood (concept 02)
- Sunny day, blue sky, puffy clouds, stadium light towers, team flags, packed stands.
- Turf chunks and grass bursts on cuts and dives, speed lines on sprints. Keep effects stepped (the no-wobble rule).

## Logos
The game uses our own teams: the **Arrows** (red, `ARR`, three arrows) and the **Grains** (green, `GRN`, a wheat bundle). Each team's emblem is ONE texture, `Assets/_Project/Resources/Logos/<SHORT>.png`, used for the midfield logo, the scoreboard and the banners; the same emblem is painted on both sides of every helmet and on the cheerleaders' tops (`tools/rebrand/`, re-runnable on fresh colour maps). The concept pictures still show the old real-team logos; read them for style only, never copy their logos.
