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
| Secrecy proof | `Assets/_Project/Tests/PlayMode/PlaySecrecyTests.cs` |

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
| **team only (SECRET)** | Only the phones of seats on that team, checked with `PartyHost.TeamPhones(team)` | `huddle`, `thumbs`, `job` |

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
| `juke`, `spin` | Juke (spin is a juke until WS-04) |
| `tackle`, `dive` | Tackle (dive is a tackle until WS-03) |
| `throw` | Throws on **release** to `tgt` |
| `scramble` | Accepted, does nothing (the QB runs with the stick) |

Actions the page already sends but the game ignores (reserved for the workstream named): `stiff` (WS-04), `call`, `catch` (WS-05), `swim`, `bull`, `swat`, `jump` (WS-03).

### `pick`
`{team: int}`, an index into `teams`. The person takes a bot's spot on that team; the spot left behind goes back to a bot. Team full of people: `err`.

### `suggest`, `lock`
`{id: string, aid: string}`. A play id from this team's `huddle.plays`, with the huddle screen's aid. `suggest` adds a thumb from anyone on the team. `lock` calls the play and only works for the team's caller. Both are checked in `Huddle.Suggest` / `Huddle.Lock` (wrong side, unknown id, not the caller: ignored). Afterwards the team gets `thumbs`; after a lock everyone gets `status`.

### `kick`
`{power: 0..1, aim: -1..1, aid}`. Sent by the `kick` layout in `layouts.js`. **No handler in the game yet**: WS-01 (kicking) adds it.

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
| `layout` | `pick`, `huddle`, `walk`, or one of the play layouts `qbpre qb carrier route block rush lb cover wait` (see `PhoneLayouts.cs`). `kick` exists on the page only. |
| `aid` | A new id every time (`<layout><counter>`). Presses must echo it. |
| `receivers` | `qb` only: `[{shape, label}]` in throw order. `shape` comes from `game.json` `play.shapes`, `label` like `WR #11`. |
| `line` | `wait` during the whistle only: what happened, for example `Tackled! Back to the huddle...`. |

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

### `job` (team only, SECRET)
`{side, play, spots, opp, me}`. The called play and this player's spot, for the job diagram. Sent when the huddle ends, only if the team has a call.

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
| WS-05 Receiving | CALL FOR IT pulse to the QB | team only |
| WS-07 Lobby | spot lists and swaps | public line-ups, own spot this phone |
| WS-12 Stats | post-game card | public totals, own line this phone |
| WS-13 Audibles | audible and hot route | team only |

## 7. Known gaps on main (found while writing this)

- `game.json` `play.shapes` holds `"???"` five times; the shape characters (△ ○ □ ◇ ☆) were lost in the M1-d commit, probably by a save in the wrong encoding. Every receiver button on the QB's phone then sends `tgt:"???"`, and `PhoneSource.OnButton` picks the last match, so a person's throw always goes to the fifth receiver.
- A team-full `err` after `pick` sends the person back to the join screen and closes the socket, because the page treats every `err` as a failed join. They have to tap JOIN again.
- The phone never sends `ping`, so a half-dead socket is only noticed when a send fails.
