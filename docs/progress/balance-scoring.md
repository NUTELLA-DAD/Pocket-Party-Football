# Scoring balance pass, 2026-10-26: two variants, waiting on Kenny

Measured on the laptop with `Tests/EditMode/BalanceScoringTests.cs` (12 seeded full bot games: four 4-minute quarters
to the final whistle, every switch on) and the 24-game gate in `BotGameTests`.

- **A, `balance/scoring` (quick calls):** FEATURES.botQuickCall: a team with no people on it calls its play after
  bots.coachCallS = 3 s instead of sitting through the 15 s huddle (people still get 15 s). Bot games roughly double their
  snaps. The 24-game gate is restated as rates so it doesn't depend on how many snaps fit in a game.
- **B, `balance/picks-only` (wait for the playtest):** no quick calls. Only the interception and sack fixes, and the old gates
  are unchanged.

## A: quick calls (balance/scoring)

| seed | score | TDs | picks | incomplete | yd/play | sacks | FG tries | snaps |
|---|---|---|---|---|---|---|---|---|
| 1 | 34-17 | 6 | 1 | 32% | 8.8 | 3 | 3 | 86 |
| 2 | 10-3 | 1 | 0 | 31% | 5.1 | 10 | 2 | 83 |
| 3 | 21-17 | 5 | 2 | 39% | 6.4 | 3 | 1 | 89 |
| 4 | 10-16 | 2 | 1 | 39% | 6.2 | 5 | 4 | 85 |
| 5 | 24-10 | 4 | 1 | 30% | 7.6 | 3 | 2 | 85 |
| 6 | 13-17 | 3 | 2 | 31% | 7.0 | 5 | 3 | 87 |
| 7 | 10-10 | 2 | 1 | 32% | 6.5 | 3 | 2 | 82 |
| 8 | 14-26 | 4 | 1 | 30% | 6.8 | 3 | 4 | 79 |
| 9 | 27-17 | 5 | 2 | 34% | 7.4 | 5 | 3 | 84 |
| 10 | 7-17 | 3 | 1 | 33% | 4.8 | 6 | 1 | 81 |
| 11 | 15-3 | 1 | 3 | 41% | 4.8 | 4 | 3 | 85 |
| 12 | 10-9 | 2 | 2 | 32% | 6.5 | 5 | 1 | 79 |
| **all 12** | 29.8 pts/game | 3.2/game | 1.4/game (2.5% of passes) | 34% | 6.5 | 4.6/game (8% of dropbacks) | 12 of 12 games | 84/game |

0-0 games: 0. Before the pass (b5b9bbd), the same 12 games averaged 11.9 points, 1.3 TDs, 3.2 picks and 3.3 sacks, and
one ended 0-0.

The checks in A:

| | Target | Result |
| --- | --- | --- |
| Touchdowns a game | 2.5-6 | 3.2 ✓ |
| Interceptions a game | 1-2.5 | 1.4 ✓ |
| 0-0 games | none | none ✓ |
| A field goal try in most games | > 6 of 12 | 12 of 12 ✓ |
| Interceptions a pass (gate) | 2-5% | 2.5% (full games); 14 in 668 = 2.1% (24-game gate) ✓ |
| Sacks a dropback (gate) | 4-9% | 8% ✓ |
| Incomplete (gate) | 28-42% | 34% / 35% ✓ |
| Yards a play (gate) | 4.5-6.5 | 6.5 / 6.47 ✓ (at the edge) |

The gate's old per-game and per-pass numbers became rates on 2026-10-26: yards 4-6 → 4.5-6.5; incompletions 25-40% →
28-42%; picks 1 in 20 to 1 in 7 → 2-5% of passes; sacks 1-3 a 10-minute game → 4-9% of dropbacks.

Tuning in A (game.json): play.catchChanceDefense 0.15 → 0.06, defense.jumpPickChance 0.1 → 0.03 and jumpPickPerfectChance
0.25 → 0.1; defense.bullChance 0.18 → 0.09, swimLateChance 0.18 → 0.09, swimEarlyChance 0.3 → 0.2 and botLineChance
0.75 → 0.6; bots.escapeAfterS 1.2 → 0.9, pressureYd 1.2 → 1.6, openYd 3.5 → 4 and missPerYd 0.05 → 0.04;
play.tackleBase 0.6 → 0.65; coach.fieldGoalRangeYd 36 → 40 and offense.fourth punt weight 60 → 40. The bot coach goes
for it on 4th and short inside the 5 (kicking.botGoForItGoalYd). Plus FEATURES.botQuickCall with bots.coachCallS 3.

The tests that timed a full all-bot huddle switch botQuickCall off for themselves (AudioTests ×2, AudioPlayModeTests,
ClockTests.TheClockWaitsForTheFirstSnap, LivePlayTests.HbDiveHandsOffToTheBack). M3's play count range follows the switch.
The bot baseline is re-pinned.

## B: picks and sacks only (balance/picks-only)

See the end of this file on that branch.
