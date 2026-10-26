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
| Full scrimmage | QB | All 22, Slants vs Cover 3 |
| Watch the bots | nobody | Bots only, for watching hitboxes in slow motion |

Each drill lists the formation spots on the field, the plays both sides "called", who has the ball, whether the play
starts live or waits for your hike, whether a catch ends it, and its time limit. Add a drill by adding an entry.

## Keys

WASD or arrows move, SHIFT sprint, SPACE hike, 1-5 throw to that receiver (hold CTRL for a lob), F tackle, E juke,
G block (hold). TAB next drill (SHIFT+TAB back), R run it again, V hitboxes on/off, P pause, N one tick while paused,
`[` slower and `]` faster (normal, 0.5x, 0.25x, 0.1x).

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
