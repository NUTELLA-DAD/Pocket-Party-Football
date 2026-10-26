# Scoring balance pass (balance/scoring), 2026-10-26: NOT merged, needs a decision

12 seeded full bot games (four 4-minute quarters to the final whistle, every switch on), measured on the laptop by
`Tests/EditMode/BalanceScoringTests.cs`.

## Targets vs results

| | Target | Before (b5b9bbd) | After (this branch) |
| --- | --- | --- | --- |
| Touchdowns a game (both teams) | 3-6 | 1.3 | **1.3** ✗ |
| Interceptions a game | 1-2 | 3.2 ✗ | 1.3 ✓ |
| Incomplete passes | 30-40% | 30% ✓ | 34% ✓ |
| Yards a play | 4.5-6.5 | 5.6 ✓ | 6.0 ✓ |
| Sacks a game | 1-3 | 3.3 ✗ | 2.3 ✓ |
| A field goal try in most games | > 6 of 12 | 10 of 12 ✓ | 10 of 12 ✓ |
| No 0-0 games | 0 | 1 ✗ | 0 ✓ |
| Points a game (both teams) | — | 11.9 | 12.4 |

### Before (b5b9bbd)

| seed | score | TDs | picks | incomplete | yd/play | sacks | FG tries |
|---|---|---|---|---|---|---|---|
| 1 | 7-7 | 2 | 7 | 32% | 7.5 | 4 | 0 |
| 2 | 0-0 | 0 | 5 | 28% | 5.0 | 3 | 0 |
| 3 | 0-10 | 1 | 4 | 17% | 7.4 | 3 | 1 |
| 4 | 0-10 | 1 | 5 | 41% | 6.7 | 0 | 1 |
| 5 | 3-3 | 0 | 3 | 31% | 3.7 | 2 | 2 |
| 6 | 0-17 | 2 | 3 | 25% | 5.5 | 3 | 1 |
| 7 | 7-10 | 2 | 1 | 45% | 4.2 | 5 | 1 |
| 8 | 0-6 | 0 | 2 | 36% | 5.5 | 5 | 2 |
| 9 | 3-7 | 1 | 2 | 30% | 3.2 | 10 | 1 |
| 10 | 10-7 | 2 | 0 | 30% | 5.9 | 4 | 1 |
| 11 | 19-0 | 2 | 1 | 25% | 5.8 | 1 | 2 |
| 12 | 7-10 | 2 | 5 | 12% | 6.5 | 0 | 1 |

### After (this branch)

| seed | score | TDs | picks | incomplete | yd/play | sacks | FG tries |
|---|---|---|---|---|---|---|---|
| 1 | 3-0 | 0 | 2 | 31% | 3.7 | 5 | 1 |
| 2 | 3-7 | 1 | 0 | 30% | 6.4 | 1 | 1 |
| 3 | 7-3 | 1 | 1 | 50% | 5.7 | 0 | 1 |
| 4 | 3-10 | 1 | 1 | 43% | 6.6 | 5 | 2 |
| 5 | 7-14 | 3 | 3 | 39% | 7.6 | 1 | 0 |
| 6 | 3-14 | 2 | 1 | 26% | 6.2 | 1 | 1 |
| 7 | 14-7 | 3 | 2 | 22% | 6.5 | 1 | 0 |
| 8 | 3-0 | 0 | 1 | 45% | 3.9 | 4 | 1 |
| 9 | 13-7 | 2 | 2 | 26% | 8.1 | 0 | 2 |
| 10 | 0-10 | 1 | 1 | 22% | 5.9 | 4 | 1 |
| 11 | 8-0 | 0 | 1 | 42% | 4.2 | 4 | 2 |
| 12 | 13-0 | 1 | 1 | 26% | 7.1 | 2 | 2 |

## What changed (game.json numbers, plus one coach choice)

- Interceptions: play.catchChanceDefense 0.15 → 0.09; defense.jumpPickChance 0.1 → 0.05 and jumpPickPerfectChance 0.25 → 0.15.
- Sacks: defense.bullChance 0.18 → 0.12, swimLateChance 0.18 → 0.12 and swimEarlyChance 0.3 → 0.25; botLineChance
  0.75 → 0.6. Also the QB escapes sooner: bots.escapeAfterS 1.2 → 0.9 and pressureYd 1.2 → 1.6.
- The bot QB's read: bots.openYd 3.5 → 4 (waits for a man more open) and missPerYd 0.05 → 0.04.
- Coach: coach.fieldGoalRangeYd 36 → 40 and offense.fourth punt weight 60 → 40. New: on 4th and short (≤ botGoForItYd)
  inside kicking.botGoForItGoalYd (5) of the goal line the coach goes for the touchdown instead of the field goal
  (Kicking.BotCall).
- Tackles a little less sure: play.tackleBase 0.6 → 0.55.

## What blocks the touchdown target

- **Too few plays.** A bot game is ~42 snaps. The clock runs through each 15 s huddle (PLAN's number), and all-bot teams
  always use the whole huddle.
- **Yards are capped.** At the 6.5 yd/play ceiling that's ~270 yards a game for both teams, while a touchdown drive
  from a touchback is ~75 yards. That leaves room for about 3.5 touchdowns only if every yard went into scoring drives.
- **The one round that came closer broke PLAN.** Round B used a 12 s huddle and a 2 s bot hike: 2.3 TDs a game (and
  20.5 points), but it cuts PLAN's 15 s huddle, so it was put back.
- **The older gate is already over its cap.** `BotGameTests.BotGamesHitTheBalanceTargets` and
  `CoachedBotGamesLookLikeFootball` (24 ten-minute games, max 6 yd/play) read 6.5 yd/play on this branch. So more
  yardage for touchdowns conflicts with that gate too.

Options (Kenny's call): a shorter huddle for teams with no people on them; 5-minute quarters; or loosen the yards cap
for the touchdown target. With any of these, re-run `BalanceScoringTests` and tune again.
