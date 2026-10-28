# Bots v3: reactions and blocking (FEATURES.botsV3)

Kenny, 2026-10-28: "They all seem to turn at the same time; on a punt the offense is running behind the defense when
they should really try to block; their basic logic is chase ball."

What changed (`Sim/BotsV3.cs`, numbers in game.json `botsV3`, switch `FEATURES.botsV3`, `NO_BOTS_V3=1` turns it off):

1. **Reactions.** A bot takes in a change (a throw, a catch, a handoff, a kick coming down, the snap) only after a
   reaction time: 0.08 s next to the ball up to 0.25 s 25 yards away, shorter for a quick man (each bot draws a quickness
   once), plus up to 0.12 s of jitter. Until then he keeps doing what he was doing. The carrier, the QB, the man a pass
   is for, the returner and a defender within 7 yards of the man a pass is for react at once. The offense goes on the
   snap; the defense reacts to it (the line at once, the safeties last).
2. **Blocking.** The team with the ball shares one plan a tick: the threats (opponents closing on the ball, soonest first)
   are handed out one each to the blocker who can meet each soonest; he runs, sprinting when he has ground to make up, to
   the point on his man's line to the ball that he reaches first, so he gets in front instead of chasing from behind at
   a jog. Spare blockers form a convoy ahead of a carrier on the move; in pass protection, and with a pass in the air,
   they hold.
3. **Kicks.** Coverage runs lanes 5 yards apart and closes only within 8 yards of the ball; the return team sets up
   while the kick is in the air (blocks count then too) and the spare men build a wall 12 yards in front of the landing spot.

## The numbers (laptop, Unity 6000.3.26f1, final round)

- The whole EditMode suite: 468 of 468 pass; hygiene ok; the bot baseline re-pinned (bots v3 changes every bot game).
- `BotsV3Tests` (8): after a throw the defenders' reactions land on 5-8 different ticks (v2: all on one); the offense is
  moving one tick after the snap and all 11 defenders are still reading it; on a run at least four defenders have a
  blocker each and no two blockers share a man; kick coverage is spread 44 yards across 1.5 s after the kick; kick returns
  hold coverage men up about 270 ticks a return against 174 with v2, and 233 of 240 blockers are out in front of the
  returner a second after the catch; the same seed replays the same game.
- The 24-game gate (`BotGameTests`, every switch on): 6.7 yards a play, 4.8 a carry, 34% incomplete, 2.3% picked off,
  5% of dropbacks sacked. Twelve full games (`BalanceScoringTests`): 6.8 yards a play, 40 points and 4.7 touchdowns a
  game, 1.6 picks a game, no 0-0.
- Yards a play by the play called, v3 on / off (12 ten-minute games): boot 5.5 / 8.2, dive 4.9 / 3.1, screen 7.1 / 9.2,
  slants 9.2 / 6.5, sneak 1.9 / 2.9, toss 4.3 / 6.9, verts 7.6 / 6.5; all 6.6 / 6.6.
- Tuning rounds: 7.5 (v3 first cut) -> 7.1 -> 6.7 -> 7.1 -> 5.4 (routes finished before blocking, throw cap 0.12 s) ->
  5.4 -> 5.9 -> 6.7 (blockers inside 5 yd run straight at their man: the toss had fallen to 2.0 yd a play because the
  meeting point swung away as the back ran wide) -> 7.1 (cap 0.2 s) -> 6.7 (cap back to 0.25 s). Swings of about half
  a yard between settings are the seeded games diverging, not the setting.

## The film

`tools\botreel.ps1 -Round N [-Seed S]` films the lab's bots-only drills (watchpunt, watchrun, watchtoss, watch) every 8
ticks from the snap to the whistle into `.shots\botreel\round-N\<drill>.png`. The sheets here are the final round
(seeds 1 and 2): the punt shows the coverage fanned out in lanes, the return team turning to meet it and a wall forming in
front of the returner, who runs behind it with 8-11 coverage men held up at a time.
