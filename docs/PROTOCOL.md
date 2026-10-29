# Phone and TV protocol

Every message between the phones and the game, what is in it, and who is allowed to receive it.
This file describes the code as it is on `main`. When you add or change a message, change this file in the same PR.

Code that speaks it:

| Side | File |
| --- | --- |
| Server (HTTP + WebSocket, shared between Pocket Party games) | `Assets/_Project/Scripts/Net/PhoneServer.cs` |
| Shared messages and field readers: `join`, `err`, `ping`/`pong`, `stick`, `btn` | `Assets/_Project/Scripts/Net/PhoneProtocol.cs` |
| Who is in the game, tokens, rejoin | `Assets/_Project/Scripts/Net/Roster.cs` |
| Football messages (built) | `Assets/_Project/Scripts/Boot/Messages.cs` |
| Football messages (routed: who gets what) | `Assets/_Project/Scripts/Boot/PartyHost.cs` (`PumpNet`, `OnJoin`, `OnPick`, `OnCall`, `SendPhaseTo`, `SendUi`) |
| Button presses into sim input | `Assets/_Project/Scripts/Input/PhoneSource.cs` |
| Which layout a player sees | `Assets/_Project/Scripts/Input/PhoneLayouts.cs` |
| Phone page | `Assets/StreamingAssets/phone/app.js`, `huddle.js`, `layouts.js` |
| Final card (WS-12): built and sent | `Assets/_Project/Scripts/Boot/PostGameHost.cs`, shown by `phone/postgame.js` |
| Secrecy proof | `Assets/_Project/Tests/PlayMode/PlaySecrecyTests.cs`, `ReceivingSecrecyTests.cs` (WS-05) |

## 1. Transport

- The game hosts the server itself, on the LAN, no internet. Port from `game.json` `net.port`, trying the next `net.portTries` ports if it is taken.
- HTTP `GET` only. Anything else gets `405`.
  - `/` and `/join/<CODE>` serve `index.html`.
  - `/<name>.<ext>` serves one flat file from `StreamingAssets/phone/` for the known types `.js .css .png .svg .json`. No folders, no `..`, everything else `404`. Every response is `Cache-Control: no-store`.
  - `/ws` with `Upgrade: websocket` opens the socket.
- WebSocket text frames, UTF-8, one JSON object per message. Field `t` is the message type. A message over 16 KB (`MaxFrameBytes`) from a phone closes that socket. Unparseable JSON is dropped silently.
- Network threads only queue events. The game reads them on the main thread once per frame (`PartyHost.PumpNet`), so a message is acted on at the next frame, and sim input at the next 30 Hz tick.

## 2. Life of a phone

1. The phone opens `/join/ABCD` (from the QR code), the person types a name, taps JOIN. The page opens `/ws` and sends `join`.
2. The game answers `joined` (with a token), then `status`, then either `ui {layout:"pick"}` (no team yet) or `picked` plus the current screen (a rejoin).
3. The person taps a team: `pick`. The game answers `picked`, and every phone on that team gets its current screen again (the caller may have changed).
4. Each huddle: the team's phones get `huddle`, then `ui {layout:"huddle"}`. Suggestions and the lock go out as `suggest` / `lock`; the team gets `thumbs`; everyone gets `status`.
5. Leaving the huddle: the team's phones get `job` (their called play) and `ui` with their role's buttons. During the play `ui` is re-sent whenever a player's moment changes (caught the ball, the whistle).
6. Screen lock or Wi-Fi blip: the socket closes, the seat stays (`Connected = false`) and a bot plays the spot. The page reconnects with backoff (0.5 s doubling to 4 s) and sends `join` again with the saved token. The same seat, team and spot come back.

`joined` after a `join` with a known token is a rejoin. A token is 12 random bytes, base64url, and is kept in the phone's `localStorage` under `tok:<CODE>`.

## 3. Who may receive what

Every game-to-phone message has one of these audiences. A new message must say which one it is, here and in a comment next to its builder in `Messages.cs`.

| Audience | Meaning | Messages |
| --- | --- | --- |
| **this phone** | Only the socket that asked, or the seat it is about | `joined`, `err`, `pong`, `picked`, `ui`, `me` |
| **public** | Every connected phone, and the TV may show it | `lobby`, `status` |
| **team only (SECRET)** | Only the phones of seats on that team, checked with `PartyHost.TeamPhones(team)` (WS-13: `AudibleHost.TeamSeats`) | `huddle`, `thumbs`, `job`, `audible`; `call` (narrower still: only the offense QB's phone, from `Boot/ReceivingHud.cs`) |
| **public** | Every connected phone, and the TV may show it | `lobby`, `status`, `postgame` (all but its `mine`, which is this phone's own) |
| **team only (SECRET)** | Only the phones of seats on that team, checked with `PartyHost.TeamPhones(team)` | `huddle`, `thumbs`, `job`; `call` (narrower still: only the offense QB's phone, from `Boot/ReceivingHud.cs`) |

The secret-play rule: a play id, play name, play contents, a suggestion or a call never goes to the other team's phones, never into a **public** message, and never onto the TV. The TV and `status` say only whether each team has called (`called[]`, "CALLED ✓"). `PlaySecrecyTests.TheOtherTeamNeverGetsThePlay` proves it with two fake phones; any new team-only message must be added to that test (or a new one like it).

Note that the TV line (`PartyHost.TvLine`) is copied into `status.tv`, so it goes to every phone. Anything put on the TV is public.

## 4. Phone to game

The game only acts on `join` and `ping` from a socket that has not joined yet. Everything else is ignored until the socket has a seat.

### `join`
First message after the socket opens.

| Field | Type | |
| --- | --- | --- |
| `code` | string | Room code from the TV, 4 letters. Trimmed, upper-cased. Wrong code: `err`. |
| `name` | string | Cleaned by `Roster.CleanName`: letters, digits, space, `-`, `'`, upper case, at most 12. Empty: `PLAYER <n>`. |
| `token` | string | Saved token for this room, or `""`. A match is a rejoin. |
| `pid` | string | A random id the page keeps forever. Stored, not used yet. |

### `ping`
`{c: int}`. Answered with `pong {c}`. The server accepts it from any socket. The current page does not send it.

### `stick`
`{x: -1..1, y: -1..1}`, clamped; NaN reads as 0. Screen axes: x right, **y down**. Sent 25 times a second while the stick is held, and one `{0,0}` on release or when the page is hidden. A stick that has not been heard for `net.stickStaleS` seconds counts as released.

### `btn`
A button going down or up. Every press carries the aid guard.

| Field | Type | |
| --- | --- | --- |
| `b` | string | The action, see below. |
| `d` | 1 or 0 | 1 = pressed, 0 = released. |
| `aid` | string | The `aid` of the last `ui` this phone got. A press with any other aid is ignored (a stale button from a screen that has changed). |
| `tgt` | string | `throw` only: the receiver's shape, from `ui.receivers[].shape`. |
| `lob` | bool | `throw` release only: held at least 350 ms = lob, else a bullet. |

Actions the game acts on today (`PhoneSource.OnButton`):

| `b` | Effect |
| --- | --- |
| `find` | FIND ME: the player's ring flashes on the TV |
| `sprint` | Held: sprint |
| `block` | Held: block |
| `hike` | QB starts the play |
| `juke` | Juke |
| `tackle`, `dive` | Tackle (dive is a tackle until WS-03) |
| `throw` | Throws on **release** to `tgt` |
| `scramble` | Accepted, does nothing (the QB runs with the stick) |
| `call` | WS-05, `FEATURES.receiving`: a receiver's CALL FOR IT (see `call` below). Ignored for non-receivers and after the throw |
| `catch` | WS-05, `FEATURES.receiving`: the CATCH timing tap (`Sim/Receiving.cs`) |
| `spin`, `stiff`, `divein` | WS-04, `FEATURES.carriermoves`: the ball carrier's SPIN, STIFF ARM and DIVE (`Sim/CarrierMoves.cs`; a DIVE only works near the goal line). Ignored without the ball |

Actions the page already sends but the game ignores (reserved for the workstream named): `swim`, `bull`, `swat`, `jump` (WS-03).

### `pick`
`{team: int}`, an index into `teams`. The person takes a bot's spot on that team; the spot left behind goes back to a bot. Team full of people: `err`.

### `suggest`, `lock`
`{id: string, aid: string}`. A play id from this team's `huddle.plays`, with the huddle screen's aid. `suggest` adds a thumb from anyone on the team. `lock` calls the play and only works for the team's caller. Both are checked in `Huddle.Suggest` / `Huddle.Lock` (wrong side, unknown id, not the caller: ignored). Afterwards the team gets `thumbs`; after a lock everyone gets `status`.

### `audible`, `hot` (WS-13, `FEATURES.audibles`)
`{id: string, aid: string}`, at the line only (after the huddle, before the hike), with the aid of the screen on show.
`audible` changes the team's call to another play from its own playbook; only the team's caller, `audibles.perDown` times a down (`Audibles.Call`). Afterwards the team gets `job` again (the new play) and `audible`.
`hot` swaps this player's route for one of `audibles.hotRoutes` (`""` = back to the called route); only an offense person on a receiver spot (`play.receivers`) (`Audibles.PickHot`). Afterwards the team gets `audible`. Nothing goes to the other team or into `status`, and the TV shows nothing.

### `kick`
`{power: 0..1, aim: -1..1, aid}`. Sent by the kick meter (`kick.js`) or the `kick` layout's pad in `layouts.js`; only the kicker's, with the aid of the screen on show (`Boot/KickPhones.cs`). On a field goal with `FEATURES.kickSweep` it is the second tap: the aim set by `kickaim` stands.

### `kickaim` (`FEATURES.kickSweep`)
`{aim: -1..1, aid}`. A field goal's first tap: the aim the phone's sweeping arrow showed (the kicker's left is -1). It snaps the ball to the holder. Only the kicker, only while lined up (`Kicking.SetAim`).
With `FEATURES.kickArc` (punts and field goals) it is the HIKE button: the aim he steered, and the snap.

### `kicksteer`, `kickpower` (`FEATURES.kickArc`)
`kicksteer {aim: -1..1, aid}`: lined up for a punt or a field goal, the kicker steers the arc on the field (`Kicking.Steer`; sent at most every 80 ms while he holds an arrow or drags). `kickpower {power: 0..1, st?, aid}`: after the snap, the first tap stops the POWER meter (`Kicking.SetPower`). The second tap is `kick {pitch: 0..1, st?, aid}`: the PITCH ANGLE needle's share from `arcMinPitchDeg` to `arcMaxPitchDeg`, and the kick (`Kicking.KickPitched`). With `FEATURES.kickSync` the taps carry `st`, the game seconds their meter showed (`Kicking.TapAt`).

## 5. Game to phone

### `joined` (this phone)
`{token, slot, name, code, team, teams}`. `team` is -1 until picked. `teams` is the same array as in `lobby`.

### `err` (this phone)
`{m: string}`, a sentence for the person. Sent for a wrong room code, a full game (`net.maxPlayers`), and a team full of people. The page shows it on the join screen and closes the socket.

### `pong` (this phone)
`{c}`, echoing `ping`.

### `lobby` (public)
`{teams: [{name, short, color, color2, people, players}]}`. `people` = phones on that team, `players` = spots per team. Sent to every connected phone after any join or pick.

### `picked` (this phone)
`{team, number, pos, name, teamName, color, color2}`. The spot this person now plays. Colours are `#rrggbb`.

### `ui` (this phone)
Which screen to show.

| Field | |
| --- | --- |
| `layout` | `pick`, `huddle`, `walk`, or one of the play layouts `qbpre qb carrier route block rush lb cover wait`, `kick` (the kicker) or `hold` (a field goal's holder, `FEATURES.fgHolder`) (see `PhoneLayouts.cs`). |
| `aid` | A new id every time (`<layout><counter>`). Presses must echo it. |
| `receivers` | `qb` only: `[{shape, label}]` in throw order. `shape` comes from `game.json` `play.shapes`, `label` like `WR #11`. |
| `line` | `wait` during the whistle only: what happened, for example `Tackled! Back to the huddle...`. |

### `kickmeter` (the kicker's phone only, WS-01)
`{on, kind, yards}`: `on` is true while this phone's player lines up a kick (`kind`: `KICKOFF`, `PUNT`, `FIELD GOAL`, `EXTRA POINT`; `yards` to the posts). A field goal with `FEATURES.kickSweep` adds `sweep {stage: "aim"|"power", aimS, powerS, need, shank, aim?}`: which arrow is sweeping, each sweep's period there and back (seconds), the power that just clears the bar, where the hooking red top starts, and in the power stage the aim already set. With `FEATURES.wind` it adds `wind {mph, right, along}` (-1..1: across the kick toward the kicker's right, and at his back). Sent again whenever any of it changes, so a phone that was too slow sees the game's own `stage: "power"`. A punt or a field goal with `FEATURES.kickArc` sends `sweep {arc: true, stage: "aim"|"power"|"pitch", powerS, pitchS, minDeg, maxDeg, shank, need?, aim, power?, clear?, good?, t?}` instead: `need` (field goal) the power that clears the bar at its usual climb, `power` the first tap's once set, `clear [lo, hi]` (field goal, pitch stage) the climbs in degrees that clear the bar at that power, `good [lo, hi]` (punt) a good high hang.

### `me` (this phone)
`{st: 0..100, tired: bool, oob: bool}`. Stamina, sprint locked out, out of bounds. At most `view.meSendHz` times a second and only when it changed.

### `status` (public)
| Field | |
| --- | --- |
| `phase` | `huddle` or `set` (`set` means anything that is not the huddle) |
| `secs` | Seconds left in this phase, rounded up |
| `called` | One bool per team: has that team locked a call. **Never which play.** |
| `score` | `[int, int]` |
| `down` | Like `2ND & 7`, or `""` when downs are off |
| `tv` | The TV's banner line (`PartyHost.TvLine`) |

Sent to every connected phone on each phase change and each lock, and to a phone when it joins.

### `postgame` (public, plus this phone's own line) (WS-12, `FEATURES.stats`)
The final card, once the game clock ends the game (or F8 on the TV previews it). Sent to every connected phone every `stats.phoneResendS` seconds while the post-game screen is up, so a phone back from a screen lock gets it too. The page shows it until HIDE, and again only when the numbers change; a `huddle` clears it. Nothing team-private: no play ids or calls.

| Field | |
| --- | --- |
| `teams` | `[{name, short, color, score, yds, tds, tackles, picks}]`, the totals of everyone who played for that team |
| `winner` | Team index, or -1 for a tie |
| `mvp` | `"Player of the game: KENNY (#87 WR) · 1 TD · 112 YDS · 3 REC"`, or `""` |
| `mine` | This phone's player only: `{team, number, pos, yds, rushYd, recYd, catches, tackles, sacks, picks, tds, points, line}` (`line` like `1 TD · 112 YDS · 3 REC`). Missing when the phone has no player. |

### `huddle` (team only, SECRET)
| Field | |
| --- | --- |
| `side` | `offense` or `defense` |
| `secs` | Seconds left in the huddle |
| `caller`, `callerNumber` | Is this phone the caller; the caller's jersey number |
| `locked`, `mine` | The team has called; this phone's current suggestion (`""` if none) |
| `plays` | This team's playbook: the full play objects from `Config/plays/*.json` (see `docs/PLAYS_FORMAT.md`) |
| `spots`, `opp` | Both formations, `[{pos, number, ahead, z}]` in yards from the ball, `ahead` = this team's way |
| `me` | This phone's spot index |
| `thumbs` | `{playId: count}` for this team |

### `thumbs` (team only, SECRET)
`{counts: {playId: count}, mine, locked}`. After any suggest or lock on that team.

### Switching position in the huddle (FEATURES.huddleSwitch, NO_HUDDLE_SWITCH=1)
After kickoff, each huddle screen also gets the team's `lineup` (from `Boot/Lobby.cs`, same shape as in the lobby, with `switch: true`). The huddle's POSITION button lists the spots on the side the team plays this down. Tapping one sends the lobby's `spot {side, spot, aid}`, and a swap question is answered with `swap {yes, aid}`, both with the huddle screen's aid and only while the phase is the huddle. A bot's spot is yours at once; a person's spot asks them. After a change the spots are relabelled and every phone of the team gets its `huddle` and `ui` again. Proved by `LobbyTests.SwitchPositionFromTheHuddle`.

### `job` (team only, SECRET)
`{side, play, spots, opp, me}`. The called play and this player's spot, for the job diagram. Sent when the huddle ends, only if the team has a call.

### `call` (the offense QB's phone only, SECRET; WS-05)
`{k, shape, number, pos, ms}`. A receiver tapped CALL FOR IT: `k` is his shape number (1-based, `play.receivers` order), `shape` the same character as `ui.receivers[].shape`, `number` and `pos` like `21` and `RB`, `ms` how long to pulse his button. Sent once per call by `Boot/ReceivingHud.cs`, only to the seat of the QB, only if that seat is on the receiver's team. Never in `status`, never to the receiver's own phone. The TV draws the same pulse over his head (the room can see the TV, as the design says), but it is not in `TvLine`, so not in `status.tv`. Proved by `ReceivingSecrecyTests`.

### `audible` (team only, SECRET; WS-13, `FEATURES.audibles`)
| Field | |
| --- | --- |
| `open` | At the line: audibles and hot routes can be used now |
| `side` | `offense` or `defense` |
| `caller`, `left` | This phone may audible; how many audibles are left this down |
| `plays` | `caller` only: `[{id, name, desc}]`, this team's other plays |
| `hot` | A receiver only: `[{id, name, text}]`, the hot routes it can pick |
| `mine`, `mineText` | This phone's hot route id (`""` if none) and its line for the job card |
| `routes` | This team's hot routes this down: `[{spot, number, pos, id, name}]` (so the QB knows) |

Sent by `AudibleHost.Drive` to each phone of a team when its phase, down, spot or that team's audibles change (a version per team, so the other team's phones never hear that something changed), and after a rejoin.

## 6. Adding a message

1. Use a new `t` value. Do not change the shape of an existing message; adding an optional field is fine if old pages ignore it.
2. Pick the audience from section 3 and route it that way. Team-only messages go out through `TeamPhones(team)` and get a secrecy test.
3. Anything a person presses carries `aid`. The game checks it before acting.
4. Build it in its own function (in `Messages.cs` or the workstream's own file) with a comment naming the audience, and add one dispatch line at the end of the list in `PartyHost.PumpNet`, tagged with the workstream id.
5. Add it to this file: the table in section 3 and its own entry in section 4 or 5.

Planned messages (not built yet; the owning workstream defines them here when it lands):

| Workstream | Likely messages | Audience |
| --- | --- | --- |
| WS-01 Kicking | `kick` handler (phone side exists), a kicker `ui` layout `kick` | this phone |
| WS-05 Receiving | landed: `call` above, `btn` `call` / `catch` | the QB's phone only |
| WS-07 Lobby | spot lists and swaps | public line-ups, own spot this phone |
| WS-12 Stats | post-game card | public totals, own line this phone |
| WS-13 Audibles | built: `audible`, `hot` (phone to game) and `audible` (game to phone), see above | team only |
| WS-12 Stats | `postgame` (built, see section 5) | public totals, own line this phone |
| WS-13 Audibles | audible and hot route | team only |

## 7. Known gaps on main (found while writing this)

- (Fixed in WS-05: the shapes are back, written as `\u` escapes, and `ReceivingTests.EveryReceiverHasItsOwnShape` guards them.) `game.json` `play.shapes` held `"???"` five times; the shape characters (△ ○ □ ◇ ☆) were lost in the M1-d commit, probably by a save in the wrong encoding. Every receiver button on the QB's phone then sends `tgt:"???"`, and `PhoneSource.OnButton` picks the last match, so a person's throw always goes to the fifth receiver.
- A team-full `err` after `pick` sends the person back to the join screen and closes the socket, because the page treats every `err` as a failed join. They have to tap JOIN again.
- The phone never sends `ping`, so a half-dead socket is only noticed when a send fails.
