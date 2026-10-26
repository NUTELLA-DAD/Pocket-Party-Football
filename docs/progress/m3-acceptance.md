# M3 "A full game": acceptance

PLAN.md: *kickoffs, punts, field goals, extra points, safeties; four 4-minute quarters, the clock stopping only for
out of bounds, incompletes and scores; halftime side switch; the end and the winner; the 2 s replay and the result
card; rejoin after a screen lock; bot cover for an idle person; a full game of about 50 plays.*
Checked on the laptop on 2026-10-26, with every switch as game.json ships it.

| Item | Result | Evidence |
| --- | --- | --- |
| A full seeded 22-bot game to the final whistle, about 50 plays, no stalls, a sane score | PASS | `M3AcceptanceTests.ABotGameIsPlayedToTheFinalWhistle` (seeds 1-3): 46 / 48 / 43 plays from scrimmage, the longest phase is the 15 s huddle, every play ends inside maxPlayS, scores 7-7, 0-0 and 0-10 |
| Kickoff to open the game and each half | PASS | same test: play 1 is a kickoff, and the first second-half play is a kickoff. Also `KickingTests.AKickoffOpensABotGame` and `TheSecondHalfOpensWithAKickoffByTheOtherTeam` |
| A try after every touchdown, and a kickoff after every score | PASS | same test checks every play in 3 games. Also `KickingTests.ATouchdownIsFollowedByATry`, `AFieldGoalInRangeScoresThreeThenTheyKickOff` and `AReturnTouchdownGivesTheReturnersATry` |
| Punts, field goals, extra points | PASS | `M3AcceptanceTests.BotGamesKickEveryKindOfKick`, 3 games: 10 kickoffs, 10 punts, 1 field goal try (good), 3 tries (3 extra points) |
| Safeties | PASS | `KickingTests.ASafetyIsTwoPointsAndAFreeKick` (scripted: a safety is rare in bot games, and none came up in these 3) |
| The clock: 4 quarters, stops only for its stop results | PASS | `M3AcceptanceTests` (every stop result stops it, and other plays run on). Also `ClockTests.ABotGameEndsAfterFourQuarters(1-3)` |
| Halftime with the sides switched | PASS | `ClockTests.SidesSwitchAtHalf`, plus the second half and its kickoff in `M3AcceptanceTests` |
| The end and the winner banner | PASS | `M3AcceptanceTests` (Over, quarter 4, the winner has more points, ties allowed). Also `ClockTests.TheWinnerHasMorePoints` |
| The 2 s replay and the result card at each whistle | PASS | `ReplayTests.ReplayRunsForTheConfiguredTime`, `ReplayShowsWhatHappened`, `ResultCardAfterATackle` and `HashesAreEqualWithReplayOnAndOff`. Also PlayMode `ReplayPlayModeTests.ReplayPlaysAtTheWhistleAndHandsBack` |
| Rejoin: drop a phone mid-game, rejoin with its token into the same spots | PASS | `NetTests.TokenRejoinKeepsTheSlotAndTeam`, plus PlayMode `LobbyTests.TwoPhonesPickSwapDropAndRejoinIntoTheSameSpots` and `PhoneLoopbackJoinTests.PhoneJoinsPicksWalksAndRejoins` |
| Bot cover after 5 s idle | PASS | `BotGameTests.ABotPlaysForAnIdlePerson` (bots.idleTakeoverS = 5) |

No rule gaps were found, so no fix commits were needed and there's no re-pin.

Note for the balance thread (not a failure): seeded bot-only games score low (7-7, 0-0, 0-10 over a full game) with
4-7 interceptions each. PLAN asks for no particular score, and people play very differently from bots.
