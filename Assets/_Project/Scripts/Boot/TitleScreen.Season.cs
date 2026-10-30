using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The SEASON page (FEATURES.season, NO_SEASON=1: no button): the front office (Sim/Season.cs, Boot/SeasonHost.cs).
    // With no season yet: pick your club, START SEASON. Then: the week and record up top; on the left YOUR ELEVEN,
    // every spot's recruit with his SPEED / SIZE / HANDS, a + to boost each one and CUT; on the right the FREE AGENTS
    // (SIGN one into the spot he fits), NEW FACES, and the three COACHING lines (UPGRADE); PLAY NEXT GAME, START OVER
    // (or NEXT SEASON once the season is over) and BACK along the bottom. The hint line says what the highlighted
    // control does and costs, or why a press didn't work.
    // Mouse: hover and click. Keyboard / pad: Up / Down move between rows, Left / Right along a row, Enter / (A)
    // presses, Esc / (B) goes back.
    public sealed partial class TitleScreen
    {
        Season season;             // the saved season (null: none yet)
        bool seasonLoaded;         // (read once when the page opens, again after every change)
        int seasonClub;            // the club chooser's pick (a league index), before a season starts
        bool confirmReset;         // START OVER pressed once: the second press wipes the season
        int seasonPick;            // the highlighted control (an index into seasonControls)
        string seasonNote;         // why the last press didn't work (shown until the highlight moves)
        readonly List<SeasonControl> seasonControls = new List<SeasonControl>();

        sealed class SeasonControl
        {
            public Rect Rect;
            public int Row, Col;        // where it sits for the arrow keys
            public string Note;         // what it does and costs (the hint line)
            public Action Press;
            public bool Off;            // can't be pressed right now (not enough coins, maxed): shows its Note instead
        }

        void OpenSeason()
        {
            Open(Page.Season);
            season = SeasonHost.Load(cfg);
            seasonLoaded = true;
            seasonClub = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > 0 ? cfg.teams[0].@short : null));
            if (season != null && TeamPick.Find(cfg, season.club) >= 0) seasonClub = TeamPick.Find(cfg, season.club);
            confirmReset = false;
            seasonPick = 0;
            seasonNote = null;
            Debug.Log("[title] SEASON");
        }

        void SeasonInput(bool left, bool right, bool up, bool down, bool ok, bool click, bool moved, Vector2 mp)
        {
            var list = seasonControls;
            if (list.Count == 0) return;
            if (seasonPick >= list.Count) seasonPick = 0;
            if (moved || click)
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Rect.Contains(mp)) { if (seasonPick != i) seasonNote = null; seasonPick = i; if (click) ok = true; break; }
            if (up) SeasonMove(0, -1);
            if (down) SeasonMove(0, +1);
            if (left) SeasonMove(-1, 0);
            if (right) SeasonMove(+1, 0);
            if (!ok) return;
            var c = list[seasonPick];
            if (c.Off) { seasonNote = c.Note; return; }
            c.Press?.Invoke();
        }

        // Up / Down: the nearest control (by column) in the row before / after; Left / Right: along the row, wrapping.
        void SeasonMove(int dCol, int dRow)
        {
            var list = seasonControls;
            var cur = list[seasonPick];
            seasonNote = null;
            if (dRow != 0)
            {
                var rows = new SortedSet<int>();
                foreach (var c in list) rows.Add(c.Row);
                var order = new List<int>(rows);
                int ri = order.IndexOf(cur.Row), n = order.Count;
                int target = order[((ri + dRow) % n + n) % n];
                int best = -1, bestGap = int.MaxValue;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Row == target) { int gap = Mathf.Abs(list[i].Col - cur.Col); if (gap < bestGap) { bestGap = gap; best = i; } }
                if (best >= 0) seasonPick = best;
                return;
            }
            int next = -1, nextCol = dCol > 0 ? int.MaxValue : int.MinValue, wrap = -1, wrapCol = dCol > 0 ? int.MaxValue : int.MinValue;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Row != cur.Row || i == seasonPick) continue;
                int col = list[i].Col;
                if (dCol > 0)
                {
                    if (col > cur.Col && col < nextCol) { next = i; nextCol = col; }
                    if (col < wrapCol) { wrap = i; wrapCol = col; }
                }
                else
                {
                    if (col < cur.Col && col > nextCol) { next = i; nextCol = col; }
                    if (col > wrapCol) { wrap = i; wrapCol = col; }
                }
            }
            if (next >= 0) seasonPick = next;
            else if (wrap >= 0) seasonPick = wrap;
        }

        // A control: drawn as a button (gold when highlighted, dim when it can't be pressed) and listed for the input.
        bool SeasonButton(Rect r, int gridRow, int gridCol, string label, string note, Action press, float s, float fontPx = 26f, bool off = false)
        {
            int i = seasonControls.Count;
            seasonControls.Add(new SeasonControl { Rect = r, Row = gridRow, Col = gridCol, Note = note, Press = press, Off = off });
            bool on = i == seasonPick;
            Fill(r, on ? CupColors.Gold : off ? new Color(1f, 1f, 1f, 0.04f) : new Color(1f, 1f, 1f, 0.1f));
            int was = row.fontSize;
            row.fontSize = Mathf.RoundToInt(fontPx * s);
            row.normal.textColor = on ? CupColors.NavyText : off ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            GUI.Label(r, label, row);
            row.fontSize = was;
            return on;
        }

        void SeasonChanged()
        {
            SeasonHost.Save(season);
            seasonLoaded = true;
        }

        TeamConfig SeasonClubTeam() => season != null ? TeamOf(season.club) : null;

        void DrawSeason(float W, float H, float s)
        {
            var r = Panel(W, H, s, "SEASON");
            seasonControls.Clear();
            if (!seasonLoaded) { season = SeasonHost.Load(cfg); seasonLoaded = true; }
            float pad = 48 * s;
            small.alignment = TextAnchor.MiddleLeft;
            if (season == null) DrawSeasonStart(r, s, pad);
            else DrawFrontOffice(r, s, pad);
            // the hint line: why the last press didn't work, else what the highlight does, else how to drive the page
            string note = seasonNote ?? (seasonPick < seasonControls.Count ? seasonControls[seasonPick].Note : null)
                ?? "Up / Down: rows   Left / Right: along a row   Enter or (A): press   Esc or (B): back";
            hint.normal.textColor = seasonNote != null ? CupColors.GoldText : Color.white;
            Shadowed(new Rect(r.x, r.yMax - 70 * s, r.width, 40 * s), note, hint);
            hint.normal.textColor = Color.white;
            small.alignment = TextAnchor.MiddleLeft;
        }

        // No season yet: the pitch, the club chooser, START SEASON.
        void DrawSeasonStart(Rect r, float s, float pad)
        {
            var c = cfg.season;
            hint.normal.textColor = Color.white;
            Shadowed(new Rect(r.x, r.y + 92 * s, r.width, 36 * s), "Run a club through the season. Sign free agents, boost their stats, pay the coaches. Their names go on their backs.", hint);

            // YOUR CLUB   ◄ colours, logo, name ►
            var league = TeamPick.League(cfg);
            seasonClub = Mathf.Clamp(seasonClub, 0, Mathf.Max(0, league.Count - 1));
            var team = league.Count > 0 ? league[seasonClub] : null;
            float ty = r.y + 170 * s, th = 56 * s, tw = Mathf.Min(620 * s, r.width - 2 * pad);
            var tr = new Rect(r.x + (r.width - tw) * 0.5f, ty, tw, th);
            small.normal.textColor = CupColors.GoldText;
            small.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(tr.x, tr.y - 36 * s, tr.width, 30 * s), "YOUR CLUB", small);
            small.alignment = TextAnchor.MiddleLeft;
            Fill(tr, new Color(1f, 1f, 1f, 0.08f));
            if (team != null)
            {
                float sw = 12 * s;
                Fill(new Rect(tr.x, tr.y, sw, th), Colors.Hex(team.color));
                Fill(new Rect(tr.x + sw, tr.y, sw * 0.6f, th), Colors.Hex(team.color2));
                var logo = LogoOf(team.@short);
                string name = team.name.ToUpperInvariant();
                int was = row.fontSize;
                row.fontSize = Mathf.RoundToInt(32 * s);
                float nameW = row.CalcSize(new GUIContent(name)).x, ls = th - 10 * s;
                float lx = tr.center.x - (nameW + (logo != null ? ls + 12 * s : 0f)) * 0.5f;
                if (logo != null) { GUI.DrawTexture(new Rect(lx, tr.y + 5 * s, ls, ls), logo, ScaleMode.ScaleToFit); lx += ls + 12 * s; }
                UiFont.Toon(new Rect(lx, tr.y, nameW + 4 * s, th), name, row, Color.white, 2 * s);
                row.fontSize = was;
            }
            int n = league.Count;
            SeasonButton(new Rect(tr.x + 24 * s, tr.y + 6 * s, 56 * s, th - 12 * s), 0, 0, "◄", "The club before this one",
                () => { seasonClub = (seasonClub + n - 1) % Mathf.Max(1, n); }, s, 30f);
            SeasonButton(new Rect(tr.xMax - 80 * s, tr.y + 6 * s, 56 * s, th - 12 * s), 0, 1, "►", "The next club",
                () => { seasonClub = (seasonClub + 1) % Mathf.Max(1, n); }, s, 30f);

            // how it works
            string[] lines =
            {
                "<b>Your eleven.</b> Every spot is a recruit with a name, a face and his own SPEED, SIZE and HANDS. They stay the same, game after game, and the name goes across his shoulders.",
                "<b>Coins.</b> A win pays " + c.coinsWin + ", a tie " + c.coinsTie + ", a loss " + c.coinsLoss + ". Every touchdown adds " + c.coinsPerTd + ", a pick " + c.coinsPerPick + ", a sack " + c.coinsPerSack + ". You start with " + c.startCoins + ".",
                "<b>Spend them.</b> SIGN a free agent (the better he is, the more he costs), BOOST a recruit's stat by one, or pay the coaches: PLAYBOOK (more plays to call), RECOVERY (stamina back faster), SPRINT (sprints last longer).",
                "<b>Cut the dead weight.</b> CUT a recruit and a walk-on takes his spot: no name, the position's plain numbers.",
                "<b>The schedule.</b> " + Mathf.Max(1, c.weeks > 0 ? c.weeks : TeamPick.League(cfg).Count - 1) + " games, home and away in turn. Every other game on the TV stays as it was; the season is its own thing.",
            };
            body.normal.textColor = Color.white;
            int keep = body.fontSize;
            body.fontSize = Mathf.RoundToInt(26 * s);
            float y = tr.yMax + 36 * s, bw0 = r.width - 2 * pad - 40 * s;
            foreach (var line in lines)
            {
                float h = body.CalcHeight(new GUIContent(line), bw0);
                GUI.Label(new Rect(r.x + pad + 20 * s, y, bw0, h), line, body);
                y += h + 10 * s;
            }
            body.fontSize = keep;

            // START SEASON / BACK
            float bw = 360 * s, bh = 70 * s, gap = 20 * s;
            float bx = r.x + (r.width - (2 * bw + gap)) * 0.5f, by = r.yMax - 160 * s;
            SeasonButton(new Rect(bx, by, bw, bh), 1, 0, "START SEASON", "Start a season as the " + (team != null ? team.name.ToUpperInvariant() : "club"),
                () => { if (team == null) return; season = SeasonHost.StartNew(cfg, team.@short); seasonLoaded = true; seasonPick = 0; }, s, 32f);
            SeasonButton(new Rect(bx + bw + gap, by, bw, bh), 1, 1, "BACK", "Back to the menu", () => Open(Page.Menu), s, 32f);
        }

        // A season under way (or over): the eleven, the free agents, the coaches, the buttons.
        void DrawFrontOffice(Rect r, float s, float pad)
        {
            var c = cfg.season;
            var club = SeasonClubTeam();
            var next = season.Next();
            string clubName = (club?.name ?? season.club).ToUpperInvariant();

            // the line under the title: where the season stands
            string head = (season.seasons > 0 ? "SEASON " + (season.seasons + 1) + "  ·  " : "");
            if (next != null)
                head += "WEEK " + (next.Week + 1) + " OF " + season.opponents.Count + "  ·  " + clubName + " " + season.RecordText
                    + "  ·  NEXT: " + (next.ClubHome ? "v " : "at ") + Name(next.Opponent);
            else head += "SEASON OVER  ·  " + clubName + " " + season.RecordText;
            head += "  ·  " + season.coins + " COINS";
            hint.normal.textColor = CupColors.GoldText;
            Shadowed(new Rect(r.x, r.y + 92 * s, r.width, 36 * s), head, hint);
            hint.normal.textColor = Color.white;

            float top = r.y + 138 * s, bottom = r.yMax - 180 * s;
            float inner = r.width - 2 * pad, lw = inner * 0.57f - 12 * s, rx = r.x + pad + lw + 24 * s, rw = r.x + r.width - pad - rx;
            DrawEleven(new Rect(r.x + pad, top, lw, bottom - top), s);
            DrawOffice(new Rect(rx, top, rw, bottom - top), s);

            // the buttons along the bottom
            var labels = new List<string>(); var notes = new List<string>(); var acts = new List<Action>(); var offs = new List<bool>();
            if (next != null)
            {
                labels.Add("PLAY NEXT GAME"); notes.Add("Week " + (next.Week + 1) + ": " + Name(next.Away) + " at " + Name(next.Home) + ". Phones join on the next screen."); offs.Add(false);
                acts.Add(() =>
                {
                    confirmReset = false;
                    CupHost.SetLive(false);
                    SeasonHost.SetLive(true);
                    fadeT = 0f;
                    Debug.Log("[title] SEASON: next game");
                });
            }
            else
            {
                labels.Add("NEXT SEASON"); notes.Add("A new schedule for the same eleven, coaches and coins. This season goes in the history."); offs.Add(false);
                acts.Add(() => { if (season.NextSeason(cfg)) { SeasonChanged(); seasonPick = 0; } });
            }
            labels.Add(confirmReset ? "SURE? PRESS AGAIN" : "START OVER"); notes.Add("Wipe this season and pick a club again (press twice)."); offs.Add(false);
            acts.Add(() =>
            {
                if (!confirmReset) { confirmReset = true; return; }
                confirmReset = false;
                SeasonHost.Delete();
                season = null;
                seasonPick = 0;
            });
            labels.Add("BACK"); notes.Add("Back to the menu"); offs.Add(false); acts.Add(() => Open(Page.Menu));
            int n = labels.Count;
            float bw = Mathf.Min(360 * s, (inner - (n - 1) * 20 * s) / n), bh = 66 * s;
            float bx = r.x + (r.width - (n * bw + (n - 1) * 20 * s)) * 0.5f, by = r.yMax - 158 * s;
            for (int i = 0; i < n; i++)
                SeasonButton(new Rect(bx + i * (bw + 20 * s), by, bw, bh), 50, i, labels[i], notes[i], acts[i], s, 30f, offs[i]);
        }

        // YOUR ELEVEN: a row a spot. Rows 1..perTeam; columns 0..2 the + buttons, 3 CUT.
        void DrawEleven(Rect area, float s)
        {
            var c = cfg.season;
            int max = Season.StatMax(cfg);
            small.fontSize = Mathf.RoundToInt(22 * s);
            small.normal.textColor = CupColors.GoldText;
            GUI.Label(new Rect(area.x, area.y, area.width * 0.5f, 28 * s), "YOUR ELEVEN", small);
            // the columns: chip, number, name, three stats (value and +), CUT
            float chipW = 72 * s, numW = 44 * s, cutW = 58 * s, statW = 70 * s, plusW = 30 * s, gap = 6 * s;
            float statsX = area.xMax - cutW - gap - 3 * (statW + gap);
            float nameX = area.x + chipW + gap + numW + gap, nameW = statsX - nameX - gap;
            string[] heads = { "SPD", "SIZ", "HND" };
            small.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < heads.Length; i++) GUI.Label(new Rect(statsX + i * (statW + gap), area.y, statW - plusW, 28 * s), heads[i], small);
            small.alignment = TextAnchor.MiddleLeft;
            float y = area.y + 32 * s;
            int rows = season.lineup.Count;
            float rowH = Mathf.Min(48 * s, (area.height - 32 * s) / Mathf.Max(1, rows)), inH = rowH - 6 * s;
            for (int spot = 0; spot < rows; spot++)
            {
                var rec = season.lineup[spot];
                var rr = new Rect(area.x, y, area.width, inH);
                Fill(rr, new Color(1f, 1f, 1f, spot % 2 == 0 ? 0.05f : 0.02f));
                // the spot: a gold chip with its positions, the offense's number
                var chip = new Rect(rr.x, rr.y, chipW, inH);
                Fill(chip, rec != null ? CupColors.Gold : new Color(1f, 0.78f, 0.12f, 0.35f));
                small.fontSize = Mathf.RoundToInt(18 * s);
                small.alignment = TextAnchor.MiddleCenter;
                small.normal.textColor = CupColors.NavyText;
                GUI.Label(chip, Season.SpotName(cfg, spot), small);
                small.alignment = TextAnchor.MiddleLeft;
                small.fontSize = Mathf.RoundToInt(22 * s);
                small.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
                GUI.Label(new Rect(rr.x + chipW + gap, rr.y, numW, inH), "#" + Season.SpotNumber(cfg, spot), small);
                if (rec == null)
                {
                    // a walk-on: no name, the position's own numbers (dim), nothing to press
                    small.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
                    GUI.Label(new Rect(nameX, rr.y, nameW, inH), c.walkOn ?? "WALK-ON", small);
                    string pos = spot < cfg.formation.offense.Count ? cfg.formation.offense[spot].pos : null;
                    PosRating pr = pos != null && cfg.ratings.positions.TryGetValue(pos, out var r0) ? r0 : null;
                    int[] pv = { pr?.speed ?? 0, pr?.size ?? 0, pr?.hands ?? 0 };
                    small.alignment = TextAnchor.MiddleCenter;
                    for (int i = 0; i < pv.Length; i++) GUI.Label(new Rect(statsX + i * (statW + gap), rr.y, statW - plusW, inH), pv[i] > 0 ? pv[i].ToString() : "–", small);
                    small.alignment = TextAnchor.MiddleLeft;
                    y += rowH;
                    continue;
                }
                small.normal.textColor = Color.white;
                GUI.Label(new Rect(nameX, rr.y, nameW, inH), rec.name, small);
                // the stats, a + each
                int[] v = { rec.speed, rec.size, rec.hands };
                string[] statNames = { "SPEED", "SIZE", "HANDS" };
                for (int i = 0; i < v.Length; i++)
                {
                    float sx = statsX + i * (statW + gap);
                    small.alignment = TextAnchor.MiddleCenter;
                    small.normal.textColor = v[i] >= max ? CupColors.GoldText : Color.white;
                    GUI.Label(new Rect(sx, rr.y, statW - plusW, inH), v[i].ToString(), small);
                    small.alignment = TextAnchor.MiddleLeft;
                    string stat = Season.StatNames[i];
                    int cost = season.BoostCost(cfg, rec, stat);
                    bool off = cost < 0 || cost > season.coins;
                    string note = cost < 0 ? rec.name + "'s " + statNames[i] + " is at the ceiling (" + max + ")"
                        : "BOOST " + statNames[i] + " for " + rec.name + ": " + v[i] + " → " + (v[i] + 1) + " for " + cost + " coins" + (cost > season.coins ? " (you have " + season.coins + ")" : "");
                    int spotNow = spot;
                    SeasonButton(new Rect(sx + statW - plusW, rr.y + 2 * s, plusW, inH - 4 * s), 1 + spot, i, "+", note,
                        () => { if (season.Boost(cfg, spotNow, stat)) SeasonChanged(); }, s, 24f, off);
                }
                int spotCut = spot;
                SeasonButton(new Rect(area.xMax - cutW, rr.y + 2 * s, cutW, inH - 4 * s), 1 + spot, v.Length, "CUT",
                    "CUT " + rec.name + " (" + rec.games + " games, " + rec.yards + " yds, " + rec.tds + " TD, " + rec.tackles + " tkl, " + rec.picks + " int). A walk-on takes #" + Season.SpotNumber(cfg, spot) + ".",
                    () => { if (season.Cut(spotCut)) SeasonChanged(); }, s, 20f);
                y += rowH;
            }
            small.fontSize = Mathf.RoundToInt(24 * s);
        }

        // The right column: FREE AGENTS (rows 20..), NEW FACES (30), COACHING (40..).
        void DrawOffice(Rect area, float s)
        {
            var c = cfg.season;
            float y = area.y;
            small.fontSize = Mathf.RoundToInt(22 * s);
            small.normal.textColor = CupColors.GoldText;
            GUI.Label(new Rect(area.x, y, area.width, 28 * s), "FREE AGENTS", small);
            y += 32 * s;
            float cardH = 84 * s, signW = 128 * s;
            for (int i = 0; i < season.offers.Count; i++)
            {
                var o = season.offers[i];
                if (o == null) continue;
                var card = new Rect(area.x, y, area.width, cardH);
                Fill(card, new Color(1f, 1f, 1f, 0.06f));
                int spot = season.BestSpot(cfg, o);
                var there = spot < season.lineup.Count ? season.lineup[spot] : null;
                int cost = season.SignCost(cfg, o);
                small.fontSize = Mathf.RoundToInt(24 * s);
                small.normal.textColor = Color.white;
                GUI.Label(new Rect(card.x + 12 * s, card.y + 4 * s, card.width - signW - 24 * s, 28 * s), o.name, small);
                small.fontSize = Mathf.RoundToInt(20 * s);
                small.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(card.x + 12 * s, card.y + 32 * s, card.width - signW - 24 * s, 24 * s), "SPD " + o.speed + "   SIZ " + o.size + "   HND " + o.hands, small);
                small.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(card.x + 12 * s, card.y + 55 * s, card.width - signW - 24 * s, 24 * s),
                    "FOR #" + Season.SpotNumber(cfg, spot) + " " + Season.SpotName(cfg, spot) + "  ·  replaces " + (there != null ? there.Surname : (c.walkOn ?? "WALK-ON")), small);
                bool off = cost > season.coins;
                int offer = i, at = spot;
                SeasonButton(new Rect(card.xMax - signW - 10 * s, card.y + 22 * s, signW, 40 * s), 20 + i, 0,
                    cost > 0 ? "SIGN · " + cost : "SIGN · FREE",
                    "SIGN " + o.name + " for " + (cost > 0 ? cost + " coins" : "nothing") + " as #" + Season.SpotNumber(cfg, spot) + " " + Season.SpotName(cfg, spot)
                        + (there != null ? "; " + there.name + " is cut" : "") + (off ? " (you have " + season.coins + ")" : ""),
                    () => { if (season.Sign(cfg, offer, at)) SeasonChanged(); }, s, 22f, off);
                y += cardH + 8 * s;
            }
            // NEW FACES
            bool scoutOff = c.scoutCost > season.coins;
            SeasonButton(new Rect(area.x, y, area.width, 40 * s), 30, 0, "NEW FACES  ·  " + c.scoutCost,
                "Three new free agents for " + c.scoutCost + " coins" + (scoutOff ? " (you have " + season.coins + ")" : ""),
                () => { if (season.Scout(cfg)) SeasonChanged(); }, s, 22f, scoutOff);
            y += 52 * s;

            // COACHING
            small.fontSize = Mathf.RoundToInt(22 * s);
            small.normal.textColor = CupColors.GoldText;
            GUI.Label(new Rect(area.x, y, area.width, 28 * s), "COACHING", small);
            y += 32 * s;
            int li = 0;
            float upW = 170 * s, lineH = 44 * s;
            foreach (var kv in c.coaching)
            {
                var line = kv.Value;
                int level = season.Level(kv.Key), maxLevel = Season.MaxLevel(cfg, kv.Key), cost = season.UpgradeCost(cfg, kv.Key);
                var rr = new Rect(area.x, y, area.width, lineH - 6 * s);
                Fill(rr, new Color(1f, 1f, 1f, 0.05f));
                small.fontSize = Mathf.RoundToInt(22 * s);
                small.normal.textColor = Color.white;
                GUI.Label(new Rect(rr.x + 12 * s, rr.y, 150 * s, rr.height), line.name ?? kv.Key.ToUpperInvariant(), small);
                // the level as pips
                float px = rr.x + 150 * s, pw = 18 * s;
                for (int p = 0; p < maxLevel; p++)
                    Fill(new Rect(px + p * (pw + 4 * s), rr.y + rr.height * 0.5f - 7 * s, pw, 14 * s), p < level ? CupColors.Gold : new Color(1f, 1f, 1f, 0.15f));
                small.fontSize = Mathf.RoundToInt(18 * s);
                small.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(px + maxLevel * (pw + 4 * s) + 8 * s, rr.y, rr.xMax - upW - 12 * s - (px + maxLevel * (pw + 4 * s) + 8 * s), rr.height), line.blurb ?? "", small);
                bool off = cost < 0 || cost > season.coins;
                string key = kv.Key;
                string what = key == Season.PlaybookLine ? "more plays in the huddle" : key == Season.RecoveryLine ? "stamina back " + Mathf.RoundToInt(line.perLevel * 100f) + "% faster a level" : key == Season.SprintLine ? "sprints drain " + Mathf.RoundToInt(line.perLevel * 100f) + "% slower a level" : line.blurb ?? "";
                SeasonButton(new Rect(rr.xMax - upW - 6 * s, rr.y + 3 * s, upW, rr.height - 6 * s), 40 + li, 0,
                    cost < 0 ? "MAXED" : "UPGRADE · " + cost,
                    cost < 0 ? (line.name ?? key) + " is at its top level (" + maxLevel + ")"
                        : (line.name ?? key) + " to level " + (level + 1) + " of " + maxLevel + " for " + cost + " coins: " + what + (cost > season.coins ? " (you have " + season.coins + ")" : ""),
                    () => { if (season.Upgrade(cfg, key)) SeasonChanged(); }, s, 20f, off);
                y += lineH;
                li++;
            }
            small.fontSize = Mathf.RoundToInt(24 * s);
        }
    }
}
