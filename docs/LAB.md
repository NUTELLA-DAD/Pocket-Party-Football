# The practice lab

A test bench for the game's mechanics without a whole game: one drill on the field, the same rules as a real play
(LivePlay, Bots), started again after every whistle. Double-click **Lab.bat** (or run the game with `-lab`, or set
`PP_LAB=1` in the editor).

## Drills (`Assets/_Project/Config/lab.json`)

| Drill | You are | What it tests |
| --- | --- | --- |
| QB throwing | QB | Hike, throw to 4 open receivers, bullet vs lob, leading a receiver |
| Throwing into coverage | QB | The same against corners, safeties and a linebacker (no rush) |
| Catching | WR | A bullet, then a lob, thrown to you over and over; the catch circle |
| Tackling | LB | A runner comes at you; tackle reach and odds |
| Running and juking | RB | Three defenders chase you; juke timing |
| Cutting and reversing | WR | Just you: run, cut back and forth, turn right around; the momentum lean (FEATURES.momentumLean) |
| ... with the ball | RB | The same carrying the ball |
| Pursuit: all eleven chase you | RB | You start in the open, already running; the whole defense chases you in lanes (FEATURES.pursuitLanes) |
| Watch a pursuit | nobody | The same with a bot runner: angles, lanes, and the man who breaks down for the tackle |
| Breakaway: you are past them all | RB | You start past the safeties; all eleven chase you from behind, each in his own lane |
| Jump ball | WR | A lob up the sideline with a corner on your hip: go up together and come down tangled, or lay out for one getting away (FEATURES.catchPlay) |
| Watch a jump ball | nobody | The same with a bot receiver: the leap, one-handed grabs, dives and tangles |
| Full scrimmage | QB | All 22, Slants vs Cover 3 |
| Watch the bots | nobody | Bots only, for watching hitboxes in slow motion |

Each drill lists the formation spots on the field, the plays both sides "called", who has the ball, whether the play
starts live or waits for your hike, whether a catch ends it, and its time limit. Add a drill by adding an entry.

## Keys

WASD or arrows move, SHIFT sprint, SPACE hike, 1-5 throw to that receiver (hold CTRL for a lob), F tackle, E juke,
G block (hold). TAB next drill (SHIFT+TAB back), R run it again, V hitboxes on/off, P pause, N one tick while paused,
`[` slower and `]` faster (normal, 0.5x, 0.25x, 0.1x).

## The camera (FEATURES.labFreeCam, NO_LAB_FREE_CAM=1 turns it off)

The lab starts on the TV camera. The mouse takes over: drag to turn around the field, right-drag (or middle-drag) to
slide, the wheel to come closer or go further. F1-F8 jump to set angles (`camera.free.presets` in game.json): F1 the
TV camera before the snap, F2 the TV camera in a live play, F3 low on the sideline, F4 behind the offense, F5 straight
down, F6 the old 7° long lens, F7 high over the whole stadium, F8 low on the grass looking into the sunset.
`,` and `.` change the lens (longer = flatter, wider = deeper) while keeping the same framing, so you can see what
the lens alone does. C goes back to the TV camera, which follows the ball again. The panel shows the angle and lens.
`-lab-cam <n>` starts on preset n (screenshots). Code: `Scripts/Boot/LabCamera.cs`, `Scripts/View/OrbitCamera.cs`
(tests: `OrbitCameraTests`).

A phone works too: scan the QR, and the first phone that joins plays the drill (it gets the right buttons for each
moment; the keyboard keeps working).

## On screen

- The panel (top left): the drill, its help, the keys, your speed and stamina, the nearest tackler with his reach
  and odds ("IN REACH"), and a log: every throw (bullet or lob, how far), every catch, drop and incompletion (how far
  the nearest player was from the ball, against the catch circle), every tackle try with its odds, every juke.
- Hitboxes on the grass at their true size: white = a player's body (the bump circle, `players.radiusYd`), red =
  tackle reach (`play.tackleReachYd`; a person's TACKLE button reaches 1.5x), blue = block reach (`bots.blockReachYd`),
  orange = where the pass is aimed, yellow = where it can be caught right now (`play.catchRadiusYd`, once the ball is
  below `play.catchMaxHeightYd`), white dots = the ball's flight path.

## Code

- `Scripts/Sim/Lab.cs`: pure C# (sets a drill up on a World, the lab QB's throws, the log). Tests: `LabTests`.
- `Scripts/Boot/LabMode.cs`: keyboard, phone, pause and speed, the panel. `Scripts/View/LabOverlay.cs`: the hitboxes.
- `PartyHost` has two hooks for it (`BeforeTick`, `AfterTick`) and `DevKeys` (off in the lab).
- Screenshots: `tools/shots.ps1` fixtures `lab_catch` and `lab_tackle`.

## The momentum gauntlet (`tools/motion.ps1`)

`Scripts/Boot/MotionReel.cs` drives "you" in the cuts drill along a fixed run (start, small cuts back and forth, a
sprint, a full reverse at a sprint, a reverse at a jog, a hard 90 degree cut, a stop) and saves a picture every 2 ticks
around the runner, with a CSV of his speed, facing and lean. `tools/motion.ps1 -Round N` runs it for three cameras (the
TV camera, a close look from the TV side, from behind) and makes one contact sheet per part of the run in
`.shots\motion\round-N\`. The lean itself: `Scripts/View/Momentum.cs` (tests: `MomentumTests`), tuned by the `turn*`,
`lean*`, `plantFrom` and `cutSpreadDeg` numbers in game.json's `anim` block.

## The pursuit gauntlet (`tools/pursuit.ps1`)

`Scripts/Boot/PursuitReel.cs` drives the ball carrier in the "pursuit" drill along four set runs, one a rep: straight
down the field, a sweep to the far sideline, a cut back across the field, a zigzag. All eleven defenders chase him
from where cover 3 lines them up. It saves a picture every few ticks and a CSV of every chaser's spot each tick.
`tools/pursuit.ps1 -Round N` runs it for two cameras (`top`: straight down over the chase; `close`: from the TV side on
the carrier and the chaser nearest him, for the body: the stride, the breakdown, the tackle; `tv` too if asked) and
writes into `.shots\pursuit\round-N\`:

- `trails.png`: the chase from above, one row a run: the carrier white, each chaser in his own colour, a dot every half
  second, and how many tick-pairs of chasers were bunched up (two closer than about a yard and a half, both away from
  the ball). `-Compare` runs the game as it was before as well (NO_PURSUIT_LANES, NO_BREAKDOWN, NO_STRIDE_HIPS,
  NO_SPINE_CHAIN) and puts it on the left.
- `<now|before>-<cam>-<run>.png`: contact sheets of the pictures; an `.mp4` of each too if ffmpeg is already installed.
- `summary.txt`: one line a run: how it ended, how long, bunched pair-ticks, the closest two chasers ever got, the
  average gap to the nearest team-mate.

What it tunes:
- the lanes: game.json `pursuit` (`Sim/Pursuit.cs`, tests `PursuitTests`): how far apart the lanes are, how far out they
  go, where they close in, how a chaser behind the carrier trails on his inside hip;
- the body (tests `BodyMotionTests`): `anim.spineBendShare`/`spineTwistShare`/`neckShare` (FEATURES.spineChain: the model's
  spine bends through Spine, Chest and UpperChest instead of as one plank at the waist), `anim.strideHipYawDeg`/
  `strideHipRollDeg` (FEATURES.strideHips: the pelvis turns with the leg reaching forward and dips on the swinging side),
  `anim.gather*` (FEATURES.breakdown: a defender closing on the carrier sinks his hips, chops his feet and brings his
  arms up to wrap; one coming from behind runs on through).

`-Drill breakaway` films the long chase from behind instead (into `.shots\pursuit\round-N-breakaway\`).

## Catch plays (FEATURES.catchPlay, `NO_CATCH_PLAY=1`)

- A diving catch (`Sim/CatchPlay.cs`): a low ball (under `catchPlay.diveMaxHeightYd`) nobody has in the catch circle
  can still be caught by a man running at it at `diveMinSpeedYdS` or more from up to `diveReachYd` further out; his
  odds are `diveChanceMul` of a plain catch's and he is on his belly for `diveDownS`.
- A jump ball: a man of the other team within `contestYd` of the catcher cuts his odds to `contestChanceMul` of a
  plain catch's, and both come down tangled for `tangleS` (the one with the ball lands on a knee, the other goes over).
  On the TV (`View/Moves.cs`) the two leave the grass together (`anim.leapS`, `leapYd` high) just before it gets there.
- One hand: a man reaching for a ball more than `anim.oneHandYd` out to his side of his run reaches with the hand on
  that side, the other arm back for balance.
- Tests `CatchPlayTests`; film it with `tools/botreel.ps1 -Drills watchjumpball -Every 2 -Reps 6`.
