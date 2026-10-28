# Pocket Party Football: the plan

The live, editable version is the Claude Doc "Pocket Party Football: Game Plan" in the project. This file is the copy builders read.

## The game in plain words

An 11-on-11 couch football game, Arrows vs Grains, on one TV with everyone's phone as the controller. The TV shows a chunky Wind Waker-style stadium from the sideline, so the field runs left to right like a broadcast. Every spot nobody claims is a bot, so 2 people or 22 get the same full game.

- **Join:** double-click the game, a QR code and 4-letter code appear. Scan, type a name, pick Arrows or Grains, pick a position.
- **Huddle:** each team's phones show the playbook. Only your team sees it; the TV only shows "CALLED ✓". Then your phone shows YOUR job on a mini field diagram.
- **Play:** the QB taps HIKE and each phone flips to the buttons that position needs.
- **Not in v1:** penalties, injuries, season mode, official logos. Short quarters, about 25 minutes a game.

## Reused from Airship Crew (QR-PHONE-GAME, UNITY.md)

| From Airship Crew | Football |
| --- | --- |
| QR + 4-letter room code; C# phone server (UNITY.md 4e) | Same; shared as `PocketParty.Net` |
| Saved token + rejoin | Same; a bot covers your spot while you reconnect |
| Stick 25 Hz latest-wins, reliable buttons, `aid` guard | Same |
| `CrewInput` / `IInputSource` (Phone, Gamepad, Bot, Replay) | `PlayerInput`; bots and humans drive the same players |
| Pure C# 30 Hz seeded sim, Unity draws | Same; flat field, 2D players, 3D ball |
| One `game.json`, every rule behind a switch | Same |
| Name plates, FIND ME, one banner voice, no buzzing | Same, plus a ring under your feet |
| Gates: deterministic bot games, screenshots, CLI builds | Same |

Lessons (LESSONS.md): a rough playable in days then real people; the core fantasy (secret calls + your job on your phone) in the first slice; readability before content; test what players actually get; concept sketches before art; bots for regressions, people for fun; keep the disk clean.

## Teams, humans, bots

- 11 on the field per team; up to 22 phones. Arrows red/gold/white, Grains green/white/black.
- Offense: QB 1, RB 1, WR 2, TE 1, OL 5. Defense: DL 4, LB 3, CB 2, S 2. Kicker/punter are bots; the play caller gets a kicking meter.
- Two-way players when fewer than 22 humans: one offense spot + one defense spot each (confirmed).
- A human idle 5 s during a live play gets bot help until they move (robot icon on the plate).
- Own helmet marks, never official logos.

## Secret play calling

1. Ball spotted; both teams' phones open the playbook. 15 s clock.
2. Caller: human QB on offense, human middle LB on defense, else the first human on that side. Teammates tap to suggest (thumbs-up count on the caller's cards).
3. LOCK IN; TV shows "ARROWS: CALLED ✓" only.
4. Clock out: the bot coach picks for the down and distance.
5. Every phone shows its job (route, block target, zone or man). Bots get the same jobs.

v1 offense: HB Dive, Toss Sweep, Slants, Four Verticals, PA Boot, HB Screen, QB Sneak, Punt/Field Goal.
v1 defense: Cover 2, Cover 3, Man Blitz, Zone Blitz, Goal Line, Prevent.
Later: audibles, hot routes, a decoy check mark.

## Phone controls by situation

Stick on the left, up to four big buttons on the right; layout changes with your role in the moment. See `docs/mockup/controller.html`.

| Situation | Buttons |
| --- | --- |
| Huddle | Play cards, LOCK IN (caller) / SUGGEST |
| QB before snap | HIKE, FIND ME |
| QB pocket | One button per receiver with a shape (△ ○ □ ◇ ☆, same shape over the receiver on the TV): tap = bullet, hold = lob; SCRAMBLE |
| Ball carrier | SPRINT (hold), JUKE (stick picks side), SPIN, STIFF ARM; dive near the goal line |
| Receiver / TE on a route | SPRINT, CALL FOR IT (shape pulses on TV and QB phone), CATCH |
| Blocker | BLOCK (hold), SPRINT |
| D-line | SWIM, BULL RUSH, TACKLE, SWAT |
| Linebacker | SPRINT, TACKLE, DIVE TACKLE, SWAT |
| CB / S | SPRINT, TACKLE, JUMP (pick/swat), DIVE TACKLE |
| Kick | Swipe-up meter: length = power, sideways = aim |
| Waiting | FIND ME, "you're on defense next" |

Sprint drains stamina (refills between plays). Tackle odds by angle and speed; a well-timed JUKE makes it miss. All numbers in `game.json`. Gamepads later.

## Game flow

Four 4-minute quarters; clock stops between plays only for out of bounds, incomplete passes and scores (about 25 minutes, ~50 plays). American rules: 100 yards, 4 downs for 10.

Play loop: huddle (15 s) → line up (auto, up to 10 s, QB hikes; bots hike after 3 s) → live → whistle with a 2 s slow-mo replay → result card → huddle.

In v1: kickoffs (bot kicker, humans return), punts, field goals, extra points, TDs, safeties, interceptions, fumbles (small, tunable), halftime side switch. Out of v1: penalties, 2-point tries, onside kicks, timeouts, challenges, injuries, overtime.

## Camera and readability

- One sideline camera, never tilts/rolls/spins, only slides left-right following the ball, leads long passes.
- About 60 yards visible so a player stays at least 48 px tall at 1080p. Since 2026-10-26 (Kenny's pick in the lab): a normal 35° lens, 46 yd across at 22° for the snap, easing back to 62 yd at 32° once the play runs (`camera.live`, FEATURES.snapZoom). It was a 7° long lens from far back, which flattened the 3D.
- Off-screen players: edge portrait in their colour with an arrow.
- Humans: coloured ring + a small team-colour triangle overhead (the name plate only shows during FIND ME, so it never covers the play); bots: jersey number only; ball carrier: brighter ring.
- FIND ME (button and automatic at each snap): ring flash, name plate, colour beam for 2 s.
- Pass shapes over eligible receivers on pass plays. One banner at a time.

## Toon look

URP with a hand-written toon shader: two flat light bands + soft rim, saturated team colours, thin outlines on players only. Players about 3 heads tall, big helmets and numbers, stepped toon animations. Stadium: striped painted grass, chunky posts, card crowd that waves, puffy clouds, gradient sky. Effects stepped at 8 fps. **Direction chosen:** Kenny's concept art in `docs/art/` (see `docs/art/ART_DIRECTION.md`) is the target look; it overrides the details above where they differ (for example the higher camera and the scoreboard bar). Grey capsules until M4.

## Architecture

- `Net` (`PocketParty.Net`): HTTP + WebSocket on `TcpListener`, serves the phone page, QR, room code.
- `StreamingAssets/phone`: plain HTML/JS controller.
- `Sim`: pure C# 30 Hz; 22 players, ball flight, blocks, tackles, catches, downs, clock, score.
- `Config/plays/*.json`: formation + one job per position (route points from the line of scrimmage, block target, zone, man). Bots and the phone job diagram read the same data.
- `BotSource`: per-position brain that follows the job then reacts.
- `View`: stadium, players, ball, camera, plates, replays; read-only.

New messages on top of UNITY.md's protocol:

| Direction | Message | Purpose |
| --- | --- | --- |
| phone → game | `pick` (team, offense spot, defense spot) | Lobby |
| game → phone | `huddle` (that team's plays, caller yes/no, seconds left) | Only to that team |
| phone → game | `call` / `suggest` (play id) | Secret call |
| game → phone | `job` (route / block / coverage as points) | Assignment diagram |
| game → phone | `layout` (qb, carrier, route, block, rush, cover, kick, wait) | Which buttons |
| phone → game | `btn` (hike, throw:△, juke, tackle...) + `aid` | Actions, never dropped |
| phone → game | `kick` (power, angle) | Kicking meter |

Safety net: three seeded 22-bot games must replay identically after every change unless a change is meant to alter them.

## Milestones

| Milestone | Length | Content |
| --- | --- | --- |
| M0 Setup | 2-3 days | Unity project, build script, shared phone server + QR, a phone moves a capsule on a grey field |
| M1 Ugly playable slice | ~1 week | 22 capsules, bots fill spots, secret call with 2 plays a side, hike/throw/run/tackle, downs + scoreboard → **Playtest 1** |
| M2 Playbook and controls | ~1 week | All 14 plays, every position's buttons, juke/spin/swat/pick, smarter bots |
| M3 A full game | ~1 week | Kicks, clock, quarters, halftime, instant replay, rejoin + bot cover → **Playtest 2** |
| M4 Toon art pass | 1-2 weeks | Run Kenny's gauntlet loop until the game matches the concept art (see ART_DIRECTION.md). Build the look: toon shader, players, teams, stadium, crowd, effects |
| M5 Party polish | ~1 week | Sound, celebrations, gamepads, audibles, hot routes, post-game stats |

## Decisions (confirmed by Kenny, 2026-10-25)

- **Graphics phase (M4) uses the gauntlet loop** (Kenny's standing request): keep iterating on the graphics until they match the concept art in `docs/art/`.

- American rules for both teams (11 players, 4 downs, 100 yards).
- Two-way players when fewer than 22 humans: one offense spot + one defense spot each.
- Play callers: the QB on offense and the middle LB on defense, with teammates suggesting.
- Downloads OK: the MIT QR encoder (M0) and CC0 Quaternius character art (M4).
- Art direction: Kenny's concept art in `docs/art/` (style only; the game uses our own Arrows and Grains marks).
