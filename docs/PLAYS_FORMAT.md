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
