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

## The numbers (laptop, Unity 6000.3.26f1)

- `BotsV3Tests` (7): after a throw the defenders' reactions land on 5-8 different ticks (v2: all on one); the offense is
  moving one tick after the snap and all 11 defenders are still reading it; on a run at least four defenders have a
  blocker each and no two blockers share a man; kick coverage is spread 44 yards across 1.5 s after the kick; kick returns
  hold coverage men up 268 ticks a return against 174 with v2, 233 of 240 blockers out in front of the returner a second
  after the catch; the same seed replays the same game.
- The 24-game gate (`BotGameTests`, every switch on): round 1 was 7.5 yards a play and 1% picks; round 3 is 6.7 a play,
  35% incomplete, 3.1% picks, 4.2 a carry, 6% of dropbacks sacked. Round 4's numbers are in the PR.

## The film

`tools\botreel.ps1 -Round N [-Seed S]` films the lab's bots-only drills (watchpunt, watchrun, watchtoss, watch) every 8
ticks from the snap to the whistle into `.shots\botreel\round-N\<drill>.png`. The sheets here are round 3 (seeds 1 and 2).
