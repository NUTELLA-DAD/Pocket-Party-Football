# Play file format (M1-b)

One play per file: `Assets/_Project/Config/plays/<id>.json`. The game loads every `*.json` in that folder and sorts the plays by `id`, so file order never matters. A build copies them to `StreamingAssets/config/plays/`, where they can be edited without rebuilding.

```json
{
  "id": "slants",
  "name": "Slants",
  "side": "offense",
  "desc": "Quick throw inside",
  "jobs": [
    { "kind": "route", "pts": [[-8, 0]], "text": "Three-step drop. Throw to a receiver as he cuts" },
    { "kind": "block", "pts": [[-4, -2]], "text": "Stay in and block a blitzer" },
    ...one job per formation spot, 11 in all...
  ]
}
```

| Field | Meaning |
| --- | --- |
| `id` | Unique, lowercase, no spaces. It is the only thing the caller's phone sends (`lock` / `suggest`). It is never shown or sent to the other team. |
| `name`, `desc` | Shown on the play card (own team only). |
| `side` | `offense` or `defense`. |
| `jobs` | Exactly 11, in **formation spot order** (`game.json` → `formation.offense` / `formation.defense`). |

Formation spot order (from `game.json`):
- Offense: 0 QB, 1 RB, 2 WR (z -20, near sideline), 3 WR (z +20, far sideline), 4 WR slot (z +13), 5 TE (z -4.5), 6-10 OL (center, then -1.5, +1.5, -3, +3).
- Defense: 0 LB (middle, z 0), 1 CB (z -19), 2 CB (z +19), 3 S (z -9), 4 S (z +9), 5 LB (z -4.5), 6 LB (z +4.5), 7-10 DL (z -1, +1, -3.5, +3.5).

Coordinates in `pts` are yards from the ball: `[ahead, z]`.
- `ahead`: yards past the line of scrimmage in the **offense's** attack direction (negative = the offense's backfield). The same frame for both sides, so a defensive zone 20 yards deep is `[20, z]`.
- `z`: yards across the field from the middle (negative = the near sideline, the camera's side; positive = the far sideline). The field is 53.3 yards wide, so `z` stays within ±26.

Job kinds:

| `kind` | Fields | Meaning |
| --- | --- | --- |
| `route` | `pts` (1+ points) | Run from your formation spot through the points in order. The QB's drop is a one-point route. |
| `block` | `pts` (1 point) | Block toward that point (the man there). |
| `zone` | `pts` (1 point), `radius` | Cover the circle around that point. |
| `man` | `target` (offense spot index) | Cover that offensive player wherever he goes. |
| `rush` | none | Go after the QB. |

Every job has a `text`: one short line the phone shows as "your job". The EditMode test `PlaysTests` checks every file: 11 jobs, known kinds, points present where needed, `man` targets 0-10, unique ids, and both sides have at least one play.

Optional, `"formation": "<set id>"` (FEATURES.formations, `NO_FORMATIONS=1` turns it off): where the team lines up for this play, one of `game.json` → `formation.sets`. A set is `{ "name", "side", "spots": [[back, z], ...] }` with one `[back, z]` per formation spot, in the same spot order as above (who is who never changes, only where they stand; `back` = yards behind the line on the team's own side). In the huddle everyone stands in the base line-up; when both calls are in, each team moves to its play's formation, and an audible shifts the team into the new play's one. Write route points from the formation's spots (e.g. in I-Form the QB is 2 back under center, so his drop starts at `[-2, 0]`). The phone cards draw each play's own line-up and show the formation's name under the play's.

| Offense set | Look |
| --- | --- |
| `shotgun` | QB 5 deep, RB beside him; 2 wide, slot, inline TE (HB Screen) |
| `iform` | QB under center, the slot WR as an offset fullback, RB 7 deep (HB Dive) |
| `singleback` | QB under center, RB 6 deep (PA Boot, Toss Sweep) |
| `trips` | gun; three receivers to the far side, one alone on the near side (Slants) |
| `spread` | gun, 2x2: the TE split out as a second slot (Four Verticals) |
| `jumbo` | under center, the WRs in tight as extra tight ends and a wing (QB Sneak) |
| `punt`, `fieldgoal` | the kicking line-ups (Kicking.cs still places the punter, holder and kicker) |

| Defense set | Look |
| --- | --- |
| `twohigh` | 4-3, two deep safeties (Cover 2) |
| `singlehigh` | 4-3, one safety deep, the other walked down (Cover 3) |
| `blitzlook` | outside LBs walked up a step showing blitz, a single high safety (Zone Blitz) |
| `press` | corners tight on the receivers, safeties down, no deep help (Man Blitz) |
| `goalline` | everyone close, corners pressed (Goal Line) |
| `prevent` | corners 10 off, safeties 20 deep (Prevent) |

Optional, M1-c: `"runner": <offense spot>` on a run play: the QB hands the ball to that spot when they meet just after the snap (HB Dive and Toss Sweep use 1, the RB). Without it the QB keeps the ball and throws.
