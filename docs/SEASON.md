# Season mode

`FEATURES.season` (`NO_SEASON=1` hides the button). Run one club through a season: recruit players who keep their
names, faces and numbers from game to game, cut the bad ones, and spend what you win on their stats and on the
coaches. Their surnames go across the backs of their jerseys (`FEATURES.jerseyNames`, `NO_JERSEY_NAMES=1` for
plain backs).

## How it plays

1. **SEASON** on the title menu opens the front office. The first time: pick your club with ◄ ► and press
   **START SEASON**. The season draws a schedule (every other team in the league once, home and away in turn), drafts
   an eleven and puts three free agents on the table. You start with `season.startCoins` coins.
2. **YOUR ELEVEN** (left): a row a spot. The gold chip is the spot's two positions (offense·defense), then the jersey
   number, the recruit's name and his **SPD / SIZ / HND**. **+** boosts a stat by one (the price rises with every
   boost that recruit has had). **CUT** drops him; a walk-on takes the spot (no name, the position's plain numbers).
3. **FREE AGENTS** (right): three recruits, each with the spot he fits best and who he would replace. **SIGN · cost**
   puts him in. A recruit whose three stats add up to `signFree` or less is free; every point above costs
   `signCostPerPoint`. **NEW FACES** draws three new ones for `scoutCost`.
4. **COACHING**: three lines, each with levels to buy.
   **PLAYBOOK** - the plays your team can call in the huddle (level 0 is a starter book; each level adds plays; kicks
   are always there). **RECOVERY** - stamina comes back faster. **SPRINT** - sprints drain stamina slower.
5. **PLAY NEXT GAME** starts the week's game: the TV wears your club and the opponent, the lobby opens as usual, and
   your eleven wear their recruits. Phones that pick a spot see whom they play as ("QB #9 · BRICK OKAFOR").
6. When the clock ends the game, the score goes into the record and the coins into the till: a win `coinsWin`, a tie
   `coinsTie`, a loss `coinsLoss`, plus `coinsPerTd` for each touchdown, `coinsPerPick` for each interception and
   `coinsPerSack` for each sack your eleven made. The post-game screen shows a gold **SEASON** strip with the result;
   Enter, (A) or a click goes back to the front office.
7. After the last week: **NEXT SEASON** keeps the eleven, the coaches and the coins and draws a new schedule; the
   finished season goes into the history. **START OVER** (press twice) wipes everything.

The other teams wear names too (drawn from the season's seed, so a replayed week shows the same faces) but play on
their positions' own ratings and the whole playbook, with no coaching.

## What persists

`season.json` in Unity's persistent data folder (on Windows
`%USERPROFILE%\AppData\LocalLow\<company>\<product>\season.json`): the club, the schedule and scores, the coins, the
eleven (each recruit's id, name, skin and hair indexes, stats, boosts and record), the free agents, the coaching
levels and the history. `pp.season.live` (PlayerPrefs) is 1 only while the next game to boot is the season's.

## Where the code is

| Piece | File |
| --- | --- |
| The season, recruits, coins, costs, schedule, save / load | `Assets/_Project/Scripts/Sim/Season.cs` (pure, tested) |
| The sim's hooks: a recruit's ratings, SPRINT / RECOVERY coaching, the club's playbook | `SeasonRules` in `Season.cs`; one line each in `Ratings.cs`, `Mover.cs`, `Huddle.cs` |
| Who wears which recruit | `Player.Recruit`, `World.Season`, `World.SetRecruit` (`World.cs`); `PlayerLine.Recruit` (`Stats.cs`) |
| Keeping it between games, dressing the eleven, recording the game | `Assets/_Project/Scripts/Boot/SeasonHost.cs` |
| The front office page | `Boot/TitleScreen.Season.cs` (a partial of `TitleScreen`) |
| Surnames on jersey backs | `View/JerseyNames.cs`, the alphabet in `View/Glyphs.cs`, hooked in `PlayersView.Body` |
| A recruit's fixed skin and hair | `JerseyMarks.SetSkin(holder, cfg, model, skinIndex, hairIndex)` |
| The phone | `picked.as` (`docs/PROTOCOL.md`), shown under the player's number in `phone/app.js` |
| Every number | `game.json` `"season"` |

A season game hashes `Season.Club` and each player's `Recruit`; a game without a season hashes exactly as before, so
the baseline tests stand.

## Tunables (`game.json` "season")

| Key | What |
| --- | --- |
| `weeks` | Games a season (0: every other team once) |
| `startCoins`, `coinsWin`, `coinsTie`, `coinsLoss`, `coinsPerTd`, `coinsPerPick`, `coinsPerSack` | The economy |
| `offers`, `scoutCost` | Free agents on the table; NEW FACES |
| `recruitSpread` | A drafted recruit's stats: the spot's position ratings, give or take this |
| `signFree`, `signCostPerPoint` | Signing |
| `boostCostBase`, `boostCostStep`, `boostMax` | Boosting (0: `roster.ratingMax` is the ceiling) |
| `coaching.<line>.costs` | The levels and their prices; `perLevel` the effect a level; `playbook.plays` the plays each level adds |
| `firstNames`, `lastNames` | The name pools (A-Z only: those are the glyphs) |
| `jerseyName` | `letterCm`, `penFrac`, `gapFrac`, `maxWidthCm`, and where the name sits: `aboveTorsoCm` up from the Torso bone, `backCm` back from it (rig cm) |

## Not yet

- A trade market, injuries, ageing or contracts: a recruit stays until you cut him.
- Visitors' recruits never improve; the league table is only your record.
- The jersey name's place on the back (`aboveTorsoCm`, `backCm`) was set by the rig's numbers, not by eye: if the
  letters float off the shoulders or sink into them, nudge those two numbers.
