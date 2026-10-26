# Workstreams: who owns which files

Several agents work on this repo at once. This file says which files each workstream may edit, so their pull requests merge without stepping on each other. It is the repo copy of the parallel workstream plan Kenny approved on 2026-10-26.

## Rules

1. **One branch and one PR per workstream.** The PR title starts with the workstream id (`WS-01 Kicking: ...`). Keep PRs small and rebase on `main` before asking for a merge.
2. **New feature, new file.** A sim rule goes in a new `Sim/<Feature>.cs` static class with `Step`-style entry points. A new TV element goes in a new `View/<Thing>.cs`. New phone screens go in new `phone/<thing>.js` and `<thing>.css`. The "Owns" column below is the whole list a workstream may edit.
3. **Shared files take one hook line each.** `Sim/World.cs` (`World.Step`), `Boot/PartyHost.cs`, `Boot/Messages.cs`, `phone/app.js`, `Sim/Bots.cs` and `Config/game.json` are shared. A workstream may add one call, one message case or one config block to each, at the end of the relevant list, with a comment carrying the workstream id (`// WS-01`). Nothing else in those files changes.
4. **Namespaced config.** Each workstream adds its own top-level block in `game.json` (`"kicking": {...}`) and its own `FEATURES.<name>` switch, which `NO_<NAME>=1` turns off. Nobody edits another block's numbers. Tuning shared numbers is its own small PR.
5. **Feature switches ship OFF.** A new sim rule lands with `FEATURES.<name>: false`, so the seeded bot-game baselines do not change and the PR needs no re-pin. Pure view and phone-only changes never touch the baseline.
6. **The laptop is the merge captain.** Only Kenny's laptop has Unity. Its loop: merge a PR, run `tools/gates.ps1`, turn the new switches on, re-pin the baseline once per batch, build, report. Cloud threads mark their PR "ready for laptop" and list the tests they added. **No cloud thread re-pins the baseline.**
7. **Protocol additions are additive.** A new phone message gets a new `t` value and an entry in [`PROTOCOL.md`](PROTOCOL.md). Existing messages do not change shape. Nothing team-private goes to the other team or into a public message.
8. **Plays and formations are data.** New plays are new `Config/plays/*.json` files. The format in [`PLAYS_FORMAT.md`](PLAYS_FORMAT.md) changes only through the Playbook workstream.
9. **Tests live next to the feature.** Each workstream adds its own `Tests/EditMode/<Feature>Tests.cs` or PlayMode test file. Existing test files are edited only to fix a break the workstream caused (and to extend the secrecy test for a new team-only message).
10. **The text checks stay green.** Every PR runs `.github/workflows/text-checks.yml`: `tools/hygiene.ps1` and the phone syntax check. Cloud threads cannot run Unity, so this is the check they can see.

Merge order inside a batch: phone-only and view-only PRs first, then sim PRs with switches off, then the captain's one re-pin commit with switches on.

Paths below are under `Assets/_Project/` (`Sim/`, `View/`, `Boot/`, `Net/`, `Input/`, `Config/`, `Tests/`, `Art/`) or `Assets/StreamingAssets/` (`phone/`), unless they start with `docs/`, `tools/` or `.github/`.

## Already in flight before the plan

| Work | Who | Files |
| --- | --- | --- |
| Phone join fix and the phone syntax check | Laptop (main thread) | `phone/app.js`, `tools/hygiene.ps1`, `tools/phone/` |
| Landscape phone layout | Laptop | `phone/*.css`, `phone/index.html` |
| QB throw stick, game side (`FEATURES.throwStick`) | Laptop | `Sim/World.cs`, `Sim/Ball.cs`, `Boot/PartyHost.cs`, the view's arc |
| QB throw stick, phone side, and the cleaner playbook screen | Main thread, cloud | `phone/app.js`, `phone/huddle.js`, `phone/huddle.css`, `phone/layouts.js` |
| Gauntlet loop 2: real character models | Laptop | `View/FigureKit.cs`, `View/FootballFigure.cs`, `Art/` |
| Practice lab scene | Thread "Build a practice lab scene" | new `Boot/Lab*.cs`, `Lab.bat`, `tools/build.ps1` |
| Tackle, catch, throw, juke animations | Thread "Add tackle and catch animations" | `View/FootballFigure.cs`, `View/PlayersView.cs` |
| Bigger hitboxes and the triangle marker | Thread "Enlarge hitboxes and swap name tag" | `game.json` play keys, `View/TeamMarks.cs` |

## Wave 0: seams

| Id | What | Owns | Depends on |
| --- | --- | --- | --- |
| WS-00 Sim events | `Sim/Events.cs`: a per-tick list of what happened (hike, throw, catch, incomplete, interception, tackle, juke, sack, touchdown, first down, turnover, whistle, out of bounds) with slot, team and spot. Not hashed. | new `Sim/Events.cs`, `Tests/EditMode/EventsTests.cs`; one push line per event in `Sim/LivePlay.cs` and `Sim/Downs.cs` | nothing |
| WS-0B Protocol doc and text CI | This file, `PROTOCOL.md`, and the GitHub text checks | `docs/PROTOCOL.md`, `docs/WORKSTREAMS.md`, `.github/workflows/text-checks.yml` | nothing |

## Wave 1: M2 and M3 features

| Id | What | Owns | Switch / block | Depends on |
| --- | --- | --- | --- | --- |
| WS-01 Kicking | Kickoffs, punts, field goals, extra points, safeties, bot kicker, phone kick meter (`kick` message) | new `Sim/Kicking.cs`, `Config/plays/punt.json`, `fg.json`, `phone/kick.js`, `phone/kick.css`, `View/KickMeter.cs` | `FEATURES.kicking`, `kicking` | WS-0B (message doc) |
| WS-02 Clock and quarters | Game clock, four quarters, stop rules, halftime side switch, end of game, winner banner; scoreboard shows the clock | new `Sim/Clock.cs`, `View/Scoreboard.cs` (moves the HUD text out of `PartyHost.TvLine`) | `FEATURES.clock`, `clock` | nothing |
| WS-03 Defender moves | SWIM, BULL RUSH, SWAT, JUMP, DIVE TACKLE; bots use them | new `Sim/DefenseMoves.cs`, `Tests/EditMode/DefenseMovesTests.cs`; one button each at the end of `Input/PhoneLayouts.cs` and `phone/layouts.js` | `FEATURES.defmoves`, `defense` | landscape layout (for the buttons) |
| WS-04 Carrier moves and fumbles | SPIN, STIFF ARM, DIVE, fumbles with a recovery scramble | new `Sim/CarrierMoves.cs`, `Sim/Fumbles.cs`, tests; buttons at the end of the layouts | `FEATURES.carriermoves`, `FEATURES.fumbles`, `carrier` | landscape layout (for the buttons) |
| WS-05 Receiving | CALL FOR IT (team-private pulse), CATCH timing window, contested catches | new `Sim/Receiving.cs`, tests; one line in `Boot/Messages.cs` for the pulse | `FEATURES.receiving`, `receiving` | nothing |
| WS-06 Bot coach and bots v2 | Play calling by situation, QB scramble, incompletions near 35 percent, defenders react to new moves | `Sim/Bots.cs`, new `Sim/BotCoach.cs`, `Tests/EditMode/BotGameTests.cs` | `bots` | WS-01, WS-03, WS-04 (start on the coach now) |
| WS-07 Lobby and two-way players | Offense and defense spot per person, lobby screen, swaps, rejoin into the same spots | `Net/Roster.cs`, new `phone/lobby.js`, `phone/lobby.css`, `Sim/Formation.cs` two-way mapping, `Tests/PlayMode/LobbyTests.cs` | `lobby` | nothing |
| WS-08 Replay and result card | 2 s slow-motion replay at the whistle, result card | new `View/Replay.cs`, `View/ResultCard.cs` | `FEATURES.replay`, `replay` | WS-00 |

## Wave 2: look and party polish

| Id | What | Owns | Switch / block | Depends on |
| --- | --- | --- | --- | --- |
| WS-09 Sound | Whistle, hits, crowd, huddle tick, score sting from events (ask Kenny before any download) | new `View/Audio.cs`, `Audio/` | `FEATURES.audio`, `audio` | WS-00 |
| WS-10 Crowd and stadium | Instanced card crowd in stepped 8 fps keys, bench row, far-side tents and staff, chunkier posts | `View/Stadium.cs` only, textures under `Art/Stadium/` | | nothing |
| WS-11 Field paint and grass | Painterly grass, softer lines and numbers, the centre logo as one replaceable texture, warm grade | `View/FieldView.cs`, `View/Markings.cs`, `Art/Shaders/Grass*` | `field` | nothing |
| WS-12 Stats and celebrations | Per-player counts, post-game TV screen and phone card, score celebration | new `Sim/Stats.cs`, `View/PostGame.cs`, `phone/postgame.js` | `stats` | WS-00, WS-02 |
| WS-13 Audibles and hot routes | One audible per down, receiver route swap, both team-private | new `Sim/Audibles.cs`, `phone/audible.js` | `FEATURES.audibles`, `audibles` | WS-07, the playbook screen redo |
| WS-14 Gamepads | Xbox pads join as players through the input interface | new `Input/GamepadSource.cs`, `Tests/EditMode/GamepadTests.cs` | `gamepad` | nothing |
| WS-15 Playtest capture | A game log next to the build and a one-page report | new `Boot/GameLog.cs`, `tools/playtest-report.ps1` | | WS-00 |

## Merge batches (laptop)

1. The three threads already open (hitboxes PR #2, animations, practice lab), then WS-00 and WS-0B.
2. WS-02, WS-05, WS-07, WS-08.
3. WS-01, WS-03, WS-04, then WS-06 last.
4. Wave 2 as each lands; WS-12 and WS-13 last.

Each batch ends with one re-pin and one build on the laptop.

## Not in this plan

Penalties, overtime, season mode and anything that needs official logos (all out of v1 by the design). Tuning after Playtest 1 becomes its own wave.
