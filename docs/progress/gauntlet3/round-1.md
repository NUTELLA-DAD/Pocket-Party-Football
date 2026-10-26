# Gauntlet loop 3 (characters), round 1

Goal: the players, refs, chain crew and coaches read like the concept art (01 for the models, 04 in game).
Built in the cloud without Unity, so this round is judged on the laptop's screenshots.

What changed, from the round-8 and loop-2 reviews:
1. Bench numbers were drawn mirror-image on every back (`12` showed as `ƖƧ`). Fixed: back numbers read left to right.
2. Proportions: the helmet is 10% smaller and sits on a taller neck; longer legs (hips 64 to 70 cm on the 182 cm rig),
   a taller, broader chest, and big shoulder pads that are wider than the helmet (the concept's silhouette), linemen a bit wider still.
3. Numbers: bigger chest and back numbers, plus "TV numbers" on top of both shoulder pads, which face the camera in game.
4. Sleeve stripes moved from the pads to rings round the sleeve cuff (Chiefs gold and white, Roughriders white).
5. The players' shade: their own material, a warmer same-hue shade (`view.playerShade*` in game.json), so a green jersey
   shades dark green instead of teal-blue. The stadium keeps the shared cool shade.
6. Refs, coaches and staff are chibi now (a big round head with eyes, a cap with a brim, shoes), and a chain-crew member
   in stripes and an orange cap holds each down marker (`stadium.crew*`).
7. New photo scene `concept01`: the six figures of concept 01 (68, 7, 24 v 9, 21, 64) close up, for judging the models.
   `tools\gauntlet.ps1 -Round 1 -Scenes concept01,concept04`.
8. Concept 04 scene: the lone Chiefs defender on the centre logo now stands over the line as a linebacker.
