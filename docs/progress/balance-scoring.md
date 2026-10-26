# Scoring balance pass, variant B: picks and sacks only (balance/picks-only), 2026-10-26

For "wait for the playtest". No quick calls, the 15 s huddle for everyone, and the old 24-game gates unchanged. Only one
thing is tuned: fewer interceptions. Variant A (quick calls, the gate restated as rates) is on `balance/scoring`.

12 seeded full bot games (four 4-minute quarters to the final whistle, every switch on), printed by the explicit test
`BalanceScoringTests.FullBotGamesScoringTable`:

| seed | score | TDs | picks | incomplete | yd/play | sacks | FG tries | snaps |
|---|---|---|---|---|---|---|---|---|
| 1 | 0-14 | 2 | 3 | 29% | 7.8 | 5 | 0 | 40 |
| 2 | 3-0 | 0 | 5 | 30% | 4.1 | 3 | 1 | 44 |
| 3 | 6-0 | 0 | 1 | 32% | 5.4 | 0 | 2 | 42 |
| 4 | 3-0 | 0 | 2 | 35% | 5.7 | 0 | 1 | 44 |
| 5 | 7-3 | 1 | 2 | 33% | 4.6 | 2 | 1 | 44 |
| 6 | 14-0 | 2 | 3 | 23% | 7.3 | 0 | 0 | 43 |
| 7 | 7-10 | 2 | 1 | 45% | 4.2 | 5 | 1 | 42 |
| 8 | 0-13 | 1 | 1 | 43% | 6.4 | 2 | 2 | 45 |
| 9 | 3-7 | 1 | 2 | 30% | 3.2 | 10 | 1 | 42 |
| 10 | 10-7 | 2 | 0 | 30% | 5.9 | 4 | 1 | 43 |
| 11 | 19-0 | 2 | 1 | 25% | 5.8 | 1 | 2 | 40 |
| 12 | 7-13 | 2 | 5 | 14% | 5.8 | 2 | 2 | 38 |
| **all 12** | 12.2 pts/game | 1.3/game | 2.2/game (8.0% of passes) | 32% | 5.5 | 2.8/game | 10 of 12 games | 42/game |

0-0 games: 0.

| | Before (b5b9bbd) | After (B) |
| --- | --- | --- |
| Points a game | 11.9 | 12.2 |
| Touchdowns a game | 1.3 | 1.3 |
| Interceptions a game | 3.2 (11.5% of passes) | 2.2 (8.0%) |
| Sacks a game | 3.3 | 2.8 |
| 0-0 games | 1 | 0 |

The old gate (24 ten-minute games), unchanged: 5.2 yd/play (4-6) ✓, 33% incomplete (25-40%) ✓, 22 picks in 326 passes
= 6.7% (5-14%) ✓, sacks and runs ✓. `CoachedBotGamesLookLikeFootball` and `CoachAndBotsV2OnTheirOwn` pass too.

Tuning (game.json only): play.catchChanceDefense 0.15 → 0.10; defense.jumpPickChance 0.1 → 0.06 and jumpPickPerfectChance
0.25 → 0.15. The sack rate wasn't touched: the old gate wants at least 1 sack per 10-minute game (about 2.5 a full game),
and fewer picks alone brought full games from 3.3 to 2.8. The bot baseline is re-pinned.

What B doesn't fix: touchdowns stay at about 1.3 a game. A bot game is ~42 snaps with the 15 s huddle, which is too few
for more scoring (see variant A).
