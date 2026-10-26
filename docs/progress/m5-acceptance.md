# M5 "Party polish": acceptance

PLAN.md: *sound, celebrations, gamepads, audibles and hot routes, post-game stats.* Every switch is on in game.json
(audio, stats, gamepad, audibles). Checked on the laptop on 2026-10-26; nothing was downloaded.

| Item | Result | Evidence |
| --- | --- | --- |
| Sound: every game event has a cue, made in code, and bot football plays all of them | PASS | `AudioTests.EveryEventKindHasASound`, `EverySoundIsMadeInCode`, `BotGamesPlayEverySound`, `BigTackles` and `HuddleTicksOnlyAtTheEnd`; PlayMode `AudioPlayModeTests.BotGamePlaysEverySound` (a real Boot scene, every sound started) |
| Sound never changes the game | PASS | `AudioTests.ListeningDoesNotChangeTheGame` |
| Celebrations on a score | PASS | `StatsTests.AScoreSetsOffTheCelebration`, and `MovesTests.ATouchdownIsCelebrated` |
| Post-game stats add up | PASS | `StatsTests.ABotGamesStatsAddUpToTheScore`, `WithKickingTheScorersAndKicksStillMakeTheScore`, `TheColumnsAddUpToTheRow` and `ATouchdownIsTheScorersSixPointsAndHisYards` |
| The post-game screen fits 22 players | PASS | `StatsTests.ThePostGameScreenFits22PlayersWithoutOverlap` and `TheScreenListsEveryPlayerOfBothTeams` |
| **The game ends with the post-game on the TV and every phone** (new) | PASS | PlayMode `M5AcceptancePlayModeTests.TheGameEndsWithThePostGameOnTheTvAndEveryPhone`: the real Boot scene with a phone on each team, run out to the end of the 4th quarter. The TV shows the screen within stats.showAfterS, and both phones get `postgame` with the right score, the winner, their own line, and no play ids |
| Gamepads join and play every layout | PASS | `GamepadTests` (16 tests): `EveryPhoneLayoutIsMapped`, `APadJoinsAsAPersonLikeAPhone`, `ShouldersPickATeamAndTakeABotsSpot`, `TheKickerPadKicks`, `ATapIsABulletAndAHoldIsALob`, `APadPlaysAFullDown`, `AnUnpluggedPadStops`, … |
| Audibles: only the caller, once a down, never in the huddle or after the snap | PASS | `AudiblesTests.OnlyTheCallerAudiblesOncePerDown`, `NoAudibleInTheHuddleOrAfterTheSnap` and `TheNextDownGetsAFreshAudible` |
| Hot routes: a receiver swaps routes, and bots run them | PASS | `AudiblesTests.AReceiverSwapsToAHotRoute`, `ASlantBreaksTowardTheMiddle` and `TheBotRunsTheHotRoute` |
| Audibles and hot routes stay secret from the other team | PASS | PlayMode `PlaySecrecyTests.TheOtherTeamNeverGetsAnAudibleOrAHotRoute` |
| The game log for playtests | PASS | PlayMode `GameLogTests.TheGameWritesALogNextToItself` and `TenMinuteBotGameLogsEveryPlayAndTheReportListsThem` |

No gaps were found, so no fix commits were needed and there's no re-pin. The one "failure" on the first run was the new test
itself: the post-game screen waits stats.showAfterS after the final whistle on purpose, so the final play can be seen,
and the test now waits for it.
