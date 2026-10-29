using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The ROSTER page (FEATURES.rosterPage, NO_ROSTER_PAGE=1: no button): every player model a team puts on the field
    // (Sim/RosterCards.cs), one at a time, standing in the team's colours (View/RosterStage.cs), with the positions
    // that wear it and each position's SPEED, SIZE and HANDS (FEATURES.ratings, 1..10 as gold pips).
    // Left / Right (or the arrows by the picture): the next player model. Up / Down (or the arrows by the team's name):
    // the next team in the league, wearing its own kit. Esc, (B) or BACK: the menu.
    public sealed partial class TitleScreen
    {
        RosterStage stage;
        int rosterTeam, rosterCard;
        Rect rosterPrev, rosterNext, rosterTeamPrev, rosterTeamNext, rosterBack;
        bool rosterOnBack;   // the highlight is on BACK (keyboard / pad: Enter presses it)
        static readonly string[] StatNames = { "SPEED", "SIZE", "HANDS" };

        // startPage "roster", or "roster:<card>:<team short>" for a screenshot of one card in one team's kit.
        void OpenRoster(string startPage)
        {
            Open(Page.Roster);
            var league = TeamPick.League(cfg);
            rosterTeam = Mathf.Max(0, TeamPick.Find(cfg, cfg.teams.Count > 0 ? cfg.teams[0].@short : null));
            rosterCard = 0;
            rosterOnBack = false;
            var bits = startPage != null ? startPage.Split(':') : new string[0];
            if (bits.Length > 1 && int.TryParse(bits[1], out int c)) rosterCard = c;
            if (bits.Length > 2 && TeamPick.Find(cfg, bits[2]) >= 0) rosterTeam = TeamPick.Find(cfg, bits[2]);
            rosterTeam = Mathf.Clamp(rosterTeam, 0, Mathf.Max(0, league.Count - 1));
            if (stage == null) stage = RosterStage.Build(cfg);
            Debug.Log("[title] ROSTER");
        }

        List<RosterCard> RosterCardsNow(out TeamConfig team)
        {
            var league = TeamPick.League(cfg);
            team = league.Count > 0 ? league[Mathf.Clamp(rosterTeam, 0, league.Count - 1)] : null;
            var cards = RosterCards.Of(cfg, team);
            if (cards.Count > 0) rosterCard = (rosterCard % cards.Count + cards.Count) % cards.Count;
            return cards;
        }

        void RosterInput(bool left, bool right, bool up, bool down, bool ok, bool click, bool moved, Vector2 mp)
        {
            int teams = TeamPick.League(cfg).Count;
            if (click)
            {
                if (rosterPrev.Contains(mp)) left = true;
                else if (rosterNext.Contains(mp)) right = true;
                else if (rosterTeamPrev.Contains(mp)) up = true;
                else if (rosterTeamNext.Contains(mp)) down = true;
                else if (rosterBack.Contains(mp)) { Open(Page.Menu); return; }
            }
            if (moved) rosterOnBack = rosterBack.Contains(mp);
            if (left) { rosterCard--; rosterOnBack = false; }
            if (right) { rosterCard++; rosterOnBack = false; }
            // the team's kit: Up the last team in the league, Down the next
            if (teams > 0 && up) rosterTeam = (rosterTeam + teams - 1) % teams;
            if (teams > 0 && down) rosterTeam = (rosterTeam + 1) % teams;
            if (ok && !click) Open(Page.Menu);   // Enter / (A): back to the menu, like HOW TO PLAY
        }

        void DrawRoster(float W, float H, float s)
        {
            var r = Panel(W, H, s, "ROSTER");
            var cards = RosterCardsNow(out var team);
            var gold = new Color(1f, 0.78f, 0.12f, 1f);
            float pad = 48 * s;

            // the team, under the title: ◄ its colours, logo and name ►
            if (team != null)
            {
                float ty = r.y + 96 * s, th = 48 * s, tw = Mathf.Min(560 * s, r.width - 2 * pad);
                var tr = new Rect(r.x + (r.width - tw) * 0.5f, ty, tw, th);
                Fill(tr, new Color(1f, 1f, 1f, 0.08f));
                float sw = 12 * s;
                Fill(new Rect(tr.x, tr.y, sw, th), Colors.Hex(team.color));
                Fill(new Rect(tr.x + sw, tr.y, sw * 0.6f, th), Colors.Hex(team.color2));
                rosterTeamPrev = new Rect(tr.x + sw * 1.6f, tr.y, 60 * s, th);
                rosterTeamNext = new Rect(tr.xMax - 60 * s, tr.y, 60 * s, th);
                int was = row.fontSize;
                row.fontSize = Mathf.RoundToInt(30 * s);
                row.normal.textColor = Color.white;
                GUI.Label(rosterTeamPrev, "▲", row);
                GUI.Label(rosterTeamNext, "▼", row);
                var logo = LogoOf(team.@short);
                string name = team.name.ToUpperInvariant();
                float nameW = row.CalcSize(new GUIContent(name)).x, ls = th - 8 * s;
                float lx = tr.center.x - (nameW + (logo != null ? ls + 10 * s : 0f)) * 0.5f;
                if (logo != null) { GUI.DrawTexture(new Rect(lx, tr.y + 4 * s, ls, ls), logo, ScaleMode.ScaleToFit); lx += ls + 10 * s; }
                UiFont.Toon(new Rect(lx, tr.y, nameW + 4 * s, th), name, row, Color.white, 2 * s);
                row.fontSize = was;
            }

            if (cards.Count == 0 || team == null)
            {
                Shadowed(new Rect(r.x, r.center.y, r.width, 40 * s), "No player models in game.json", hint);
                return;
            }
            var card = cards[rosterCard];

            // the picture, on the left, with ◄ ► either side
            float top = r.y + 168 * s, bottom = r.yMax - 92 * s;
            float ph = bottom - top, pw = Mathf.Min(ph * 0.8f, r.width * 0.4f);
            var pic = new Rect(r.x + pad + 44 * s, top, pw, ph);
            Fill(new Rect(pic.x - 5 * s, pic.y - 5 * s, pic.width + 10 * s, pic.height + 10 * s), gold);
            if (stage != null) GUI.DrawTexture(pic, stage.Picture(team, card, Mathf.RoundToInt(pic.width), Mathf.RoundToInt(pic.height)), ScaleMode.StretchToFill, false);
            rosterPrev = new Rect(pic.x - 48 * s, pic.center.y - 40 * s, 40 * s, 80 * s);
            rosterNext = new Rect(pic.xMax + 8 * s, pic.center.y - 40 * s, 40 * s, 80 * s);
            int keep = row.fontSize;
            row.fontSize = Mathf.RoundToInt(44 * s);
            UiFont.Toon(rosterPrev, "◄", row, gold, 2 * s);
            UiFont.Toon(rosterNext, "►", row, gold, 2 * s);
            row.fontSize = keep;
            hint.normal.textColor = Color.white;
            Shadowed(new Rect(pic.x, pic.yMax + 8 * s, pic.width, 32 * s), (rosterCard + 1) + " / " + cards.Count, hint);

            // the card, on the right: the model's name and number, then each position and its ratings
            float cx = rosterNext.xMax + 36 * s, cw = r.xMax - pad - cx, y = top;
            head.fontSize = Mathf.RoundToInt(52 * s);
            head.alignment = TextAnchor.MiddleLeft;
            UiFont.Toon(new Rect(cx, y, cw, 64 * s), card.Name, head, gold, 3 * s);
            head.alignment = TextAnchor.MiddleCenter;
            head.fontSize = Mathf.RoundToInt(56 * s);
            y += 70 * s;
            small.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            small.alignment = TextAnchor.MiddleLeft;
            var codes = new List<string>();
            foreach (var sp in card.Spots) codes.Add(sp.Pos);
            GUI.Label(new Rect(cx, y, cw, 32 * s), "#" + card.Number + "   ·   PLAYS " + string.Join(" · ", codes), small);
            y += 50 * s;

            bool rated = cfg.Feature("ratings");
            // the column heads, once
            float chip = 76 * s, statX = cx + chip + 16 * s, statW = (cx + cw - statX) / StatNames.Length;
            if (rated && HasRatings(card))
            {
                small.normal.textColor = CupColors.GoldText;
                for (int i = 0; i < StatNames.Length; i++) GUI.Label(new Rect(statX + i * statW, y, statW, 28 * s), StatNames[i], small);
                y += 34 * s;
            }
            int max = Mathf.Max(1, cfg.roster.ratingMax);
            float rowH = Mathf.Min(92 * s, (r.yMax - 150 * s - y) / Mathf.Max(1, card.Spots.Count));
            foreach (var sp in card.Spots)
            {
                // the position: a gold chip with its letters, its full name beside it
                var chipR = new Rect(cx, y, chip, 40 * s);
                Fill(chipR, gold);
                small.alignment = TextAnchor.MiddleCenter;
                small.normal.textColor = CupColors.NavyText;
                GUI.Label(chipR, sp.Pos, small);
                small.alignment = TextAnchor.MiddleLeft;
                small.normal.textColor = Color.white;
                string label = sp.PosName + (sp.Numbers.Count > 0 ? "   (#" + string.Join(", #", sp.Numbers) + ")" : "");
                GUI.Label(new Rect(statX, y, cw - chip - 16 * s, 40 * s), label, small);
                float by = y + 46 * s;
                if (sp.Rating != null && rated)
                {
                    int[] v = { sp.Rating.speed, sp.Rating.size, sp.Rating.hands };
                    for (int i = 0; i < v.Length; i++) Pips(new Rect(statX + i * statW, by, statW - 18 * s, 24 * s), v[i], max, s);
                }
                else
                {
                    small.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                    string note = !rated ? "Ratings are off: every position plays the same." : sp.Note ?? "";
                    int was = small.fontSize;
                    small.fontSize = Mathf.RoundToInt(20 * s);
                    GUI.Label(new Rect(statX, by, cw - chip - 16 * s, 28 * s), note, small);
                    small.fontSize = was;
                }
                y += rowH;
            }

            // BACK, bottom right of the card
            rosterBack = new Rect(r.xMax - pad - 220 * s, r.yMax - 150 * s, 220 * s, 60 * s);
            Fill(rosterBack, rosterOnBack ? gold : new Color(1f, 1f, 1f, 0.08f));
            row.normal.textColor = rosterOnBack ? CupColors.NavyText : Color.white;
            int w0 = row.fontSize;
            row.fontSize = Mathf.RoundToInt(30 * s);
            GUI.Label(rosterBack, "BACK", row);
            row.fontSize = w0;

            Shadowed(new Rect(r.x, r.yMax - 70 * s, r.width, 40 * s), "Left / Right: next player   Up / Down: next team   Esc or (B): back", hint);
        }

        static bool HasRatings(RosterCard card)
        {
            foreach (var sp in card.Spots) if (sp.Rating != null) return true;
            return false;
        }

        // A rating as pips: `value` of `max` lit gold, the rest dim, and the number after them.
        void Pips(Rect at, int value, int max, float s)
        {
            float numW = 30 * s, gap = Mathf.Max(1f, 3 * s);
            float pw = (at.width - numW - gap * (max - 1)) / max;
            for (int i = 0; i < max; i++)
            {
                var pr = new Rect(at.x + i * (pw + gap), at.y, pw, at.height);
                Fill(pr, i < value ? new Color(1f, 0.78f, 0.12f, 1f) : new Color(1f, 1f, 1f, 0.12f));
            }
            small.alignment = TextAnchor.MiddleRight;
            small.normal.textColor = Color.white;
            GUI.Label(new Rect(at.xMax - numW, at.y - 4 * s, numW, at.height + 8 * s), value.ToString(), small);
            small.alignment = TextAnchor.MiddleLeft;
        }
    }
}
