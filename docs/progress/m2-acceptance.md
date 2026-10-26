# M2 "Playbook and controls": acceptance

PLAN.md: *All 14 plays, every position's buttons, juke/spin/swat/pick, smarter bots.* Checked by
`Tests/EditMode/M2AcceptanceTests.cs` with game.json's switches as they ship (2026-10-26, laptop).

| Item | Result | Evidence |
| --- | --- | --- |
| All plays exist with a job for all 11 spots | PASS | `ThereAreFourteenPlaysOfElevenJobs`: 7 offense (dive, toss, slants, verts, boot, screen, sneak), 6 defense (cover2, cover3, mblitz, zblitz, goalline, prevent), punt, fg |
| Every offense play runs against every defense (42 seeded 22-bot snaps) | PASS | `EveryOffensePlayRunsAgainstEveryDefense` ×7: each snaps, every route runner leaves his spot and every blocker stays at the line in the first second, the whistle comes within maxPlayS, and the next spot is on the field. Results range from TACKLED to TOUCHDOWN, INTERCEPTED, SACKED, INCOMPLETE, OUT OF BOUNDS and FUMBLE RECOVERED |
| Punt and field goal on 4th down | PASS | `FourthDownKicksAreKicked`: punt from the 30 → TOUCHBACK; fg from the 20 → FIELD GOAL |
| Bots use every bot move | PASS | `BotGamesUseEveryBotMove`, six 10-minute bot games: spin 10, stiff arm 54, dive 5, swim 201, bull rush 141, swat 15, jump 6, dive tackle 46, interceptions 8 |
| Juke and call for it | PASS | `APersonJukesAndCallsForIt`: a person's JUKE fires the Juke event, and CALL FOR IT makes the receiver call. Bots never juke or call; that's by design (Receiving.cs: "Bots never tap"), and their juke is the spin and stiff arm |
| Every position's buttons, as in PLAN "Phone controls by situation" | PASS | `EveryLayoutShowsThePlansButtons`: layouts.js qbpre, qb, carrier, route, block, rush, lb, cover, wait and kick, plus huddle.js LOCK IN / SUGGEST |
| Every button reaches the sim | PASS | `EveryButtonReachesTheSim`: each layouts.js action is accepted by PhoneSource and lands in PlayerInput (or FIND ME for the host). Each receiver shape throws to its receiver, tap = bullet, hold = lob |
| Smarter bots | PASS | Already covered by `BotGameTests.BotGamesHitTheBalanceTargets` (24 games: 5.5 yd/play, 31% incomplete, ~1 pick in 12 passes) and the coach tests |

No gaps were found, so no fix commits were needed. Bot games are unchanged, so no re-pin.
