# Gauntlet loop 3, round 9 (PR #36, head 8f5aa58)

Checked on the laptop in the check folder (PocketFootball-ws00): no merge, no fixes.

## Gates (tools\gates.ps1, verbatim)

```
gates 2026-10-26T08:26:22
env       PASS  (1s)
hygiene   PASS  (1s)
editmode  PASS  (121s)
build     PASS  (15s)
shots     PASS  (96s)
playmode  PASS  (169s)
```

EditMode: total 327, passed 327, failed 0. PlayMode: total 17, passed 17, failed 0.

## Pairs

- `round-9-concept01-pair.png`: concept 01 left, ours right
- `round-9-concept04-pair.png`: concept 04 left, ours right

## Merge captain's observations

concept01:
- The wordmarks are now bold, heavy italic capitals. ✓ That's much closer to the concept's weight. They're still a clean italic, not the concept's brush lettering.
- The card is back to a paler, warm cream. ✓ It's still yellower than the concept, with a bright backlight at the top and big soft ground shadows.
- Each team's three now stand as a group, with an even gap between the teams in the middle. ✓ QB 9 now holds the ball out in front, a reaching pose rather than a throw.
- Unchanged: the Chiefs helmet is "KC" on a disc (no solid white arrowhead), the faces are small and alike, the facemasks read as thick white bars, the outlines are thin, and the bodies are soft and rounded.

concept04: unchanged at this distance.

Score: concept01 6.5/10, concept04 6/10.

## Fresh reviewer (a new subagent with no context, given only the two pair images), verbatim

## Pair 1: concept01 (team lineup)

Differences (most important first):
1. **Faces/style:** Concept uses crisp 2D cel-shading with ink outlines, expressive determined faces, big stylized eyes. Game has smooth plasticky 3D, no outlines, flat doll-like faces with tiny features and blank look.
2. **Body proportions:** Concept linemen (68, 64) are huge, wide, muscular vs slim QB. Game bodies are all similar rounded blobs; linemen read as squat balls, not powerful; less size differentiation.
3. **Helmets/logos:** Concept has KC arrowhead and Riders "S" logos, facemasks with clear bars and stripes. Game shows a plain "KC" letter disc and white "S" circle; facemasks are thick white blobs; helmets over-glossy.
4. **Poses:** Concept has dynamic action poses (QB throwing, RB cutting, linemen in stance). Game poses are stiffer/more upright, arms mostly forward; Riders QB not throwing.
5. **Uniforms:** Concept has sleeve stripes, pants stripes, dark gloves, two-tone socks. Game uniforms simpler, blurry numbers, fewer trim details.
6. **Lighting/color:** Concept is evenly lit on soft cream background. Game has warm yellowish gradient sky, strong backlight, heavy cast shadows.
7. **Background/framing:** Concept shows faint stadium silhouettes, figures evenly spaced with feet visible. Game has no stadium, perspective field with yard lines, figures cropped at edges, camera lower.
8. **Titles:** Same team names and colors, but game font is generic italic rather than brushy hand-lettering.

Score: 4/10

## Pair 2: concept04 (broadcast gameplay view)

Differences (most important first):
1. **Player cluster:** Game's line of scrimmage is a dense, overlapping clump of ~12 players; concept has a clean, readable split between offensive and defensive lines.
2. **Player scale/style:** Game players are larger, plainer, less detailed; concept's chibi players have visible numbers and crisp outlines.
3. **Midfield logo:** Concept has the stylized Riders "S" with wheat-sheaf details; game's "S" is a blobby, simplified version.
4. **Sidelines:** Concept has a busy, detailed sideline (benches, tents, coaches, orange markers); game's sideline is sparser and repeated-looking.
5. **Bottom sideline:** Game's near-sideline players are bigger and more crowded.
6. **Camera:** Game camera slightly higher/zoomed in.
7. **Scorebug:** Close, but KC logo differs and spacing tighter.
8. **Matching:** Field colors, yard numbers, blue/yellow lines and A/B markers match well.

Score: 6/10
