# Build brief: M0 and M1

Kenny's part is about 1 to 2 hours on the first evening: the repo (done), install Unity, sign in once. After that the agents build M0 and M1 in small jobs, and each job ends with a test Kenny can do with his phone.

## Before we start (Kenny, at home)

1. ~~Create the GitHub repo~~ (done: NUTELLA-DAD/Pocket-Party-Football).
2. Install Unity Hub and Unity 6 LTS and sign in with a free Unity ID (Personal licence). 30 to 60 minutes of download; same steps as section 8 of Airship Crew's UNITY.md. Skip if Unity is already installed.
3. Clone this repo to a short path: `C:\Users\kenny\PocketFootball\` (Unity has trouble with long paths and OneDrive folders).
4. First run only: Windows asks about the firewall. Tick Private networks and click Allow; set the Wi-Fi to Private.
5. Keep a phone on the same Wi-Fi for the tests.

## Rules every job follows

- Every number (speeds, tackle odds, clocks) lives in `Config/game.json`; every play in `Config/plays/*.json`.
- The football rules (`Sim`) are plain C# with no Unity code, 30 ticks a second, seeded, sorted lists: a bot game always replays the same way.
- Every new rule has an on/off switch in config plus a `NO_<NAME>=1` setting, so old test games still match.
- The scene is built from code and data: no hand-placed prefabs.
- The phone page loads nothing from the internet, never buzzes, uses `touch-action: none`, names every button's action, and sends the `aid` guard.
- Before reporting, each job runs the hygiene check and the EditMode tests and saves screenshots to `.shots`.
- Commit after each working step. The lead runs the full gate before merging.
- Each report is in plain words: what landed, test results, how Kenny tests it.

## M0: Setup (2 to 3 days)

| Job | What gets built | Done when | Kenny's test |
| --- | --- | --- | --- |
| M0-a Install check | Unity version pinned in `tools/pinned-unity.txt` | `Unity.exe -version` matches the pin | None |
| M0-b Project skeleton | Unity project, packages (URP, Input System, Test Framework, Newtonsoft JSON), asmdefs (Sim, Input, Net, View, Boot, Editor, Tests), one Boot scene, a flat field 120 x 53.3 yards with yard lines, the sideline camera, `BuildScript`, `tools/*.ps1` gates (env, hygiene, test-editmode, test-playmode, build, shots, gates) | `gates.ps1` passes and the .exe opens | Double-click the .exe and see a grey-green field from the sideline |
| M0-c Phone server and join | `PocketParty.Net` (TinyHttp, WebSocketServer, PhoneProtocol, LanAddress, Qr, room code, tokens), the phone join page from the mockup, team pick, one capsule per phone | PlayMode `PhoneLoopbackJoin` passes | Scan the QR, pick a team, walk your capsule. Lock and unlock the phone: still you |
| M0-d Rules core and safety net (alongside M0-c) | `World.Step` at 30 Hz, `Rng`, `World.Hash()`, `RunHeadless`, bot v0 (wanders), baseline test, hygiene script | 3 seeded runs give identical hashes | None |

## M1: Ugly playable slice (about 1 week)

M1-a first (everyone builds on it). Then M1-b, M1-e and M1-f side by side. M1-c and M1-d once M1-b has landed.

| Job | What gets built | Done when | Kenny's test |
| --- | --- | --- | --- |
| M1-a Field, players, movement | 22 capsules with team colours and jersey numbers, a mover with speed, acceleration and sprint stamina, players bumping, out of bounds, a ball with 3D flight | Lineup and formation screenshots look right; baseline replays identically | Walk, sprint until tired, run out of bounds |
| M1-b Plays and the secret huddle | Play file format and 4 plays (HB Dive, Slants, Cover 3, Man Blitz), 15 s huddle, secret calling, suggestions, the job diagram on the phone; TV shows only check marks | `PlaySecrecyTests`: the other team's sockets never get the play id | Call a play while a friend watches the TV: they can't tell what it is |
| M1-c Snap, pass, run, tackle | Auto line-up, HIKE, receiver shape buttons (tap = bullet, hold = lob), catches, handoffs, JUKE and SPRINT, TACKLE odds by angle and speed, the whistle | Pass, catch and tackle tests pass | Hike, throw a slant to the triangle, get tackled. Juke a bot |
| M1-d Bots v1 | Bots follow their job: OL blocks, DL rushes, man and zone coverage, pursuit angles, bot QB reads 3 receivers within 3 s; bot takeover for a human idle 5 s | 22-bot games finish with sensible yards per play | Add 21 bots and watch them play |
| M1-e Downs, score, camera, finding yourself | Downs and distance, first downs, TDs, turnover on downs, scoreboard, ball-following camera that leads passes, 48 px floor, edge portraits, rings and name plates, FIND ME, one banner at a time | `CameraFraming`: every human on screen or on an edge portrait | Press FIND ME and spot yourself in under a second |
| M1-f Phone layouts v1 | The mockup's screens driven by `layout` messages: huddle, job, QB pre-snap, QB pocket, ball carrier, receiver, blocker, defender, waiting | Every layout appears at the right moment in a bot game | Catch a pass and watch the phone switch to ball-carrier buttons |
| M1 close (lead) | Merge, refresh snapshots once, build the .exe, a 10-minute 22-bot game with no errors | All gates pass on the merged tree | Playtest 1 |

## Playtest 1 (30 minutes, 4 to 8 people)

Phones on the same Wi-Fi, TV about 2.5 m away, nobody coached.

- [ ] Time from scanning the QR to the first snap (target under 2 minutes)
- [ ] Did anyone figure out the other team's call from the TV? (target: no)
- [ ] "Where am I?" moments (target under 2 per person)
- [ ] Could the QB throw to the receiver they meant to?
- [ ] Did tackles feel fair, did jukes ever work?
- [ ] Anyone unsure what a button does?
- [ ] Huddle clock too short or too long?
- [ ] Ask: play again next week? The one thing to change?

Everything goes into `docs/PLAYTEST1.md`, which becomes the M2 job list.
