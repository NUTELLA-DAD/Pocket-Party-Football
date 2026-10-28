using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The card after a play (WS-08, with the replay under FEATURES.replay): what happened, the yards, who made the
    // play (the tackler, the sack, the pick, the scorer, the man it was thrown to) and the next down. Built once at
    // the whistle from that play's events (Sim/Events.cs) and the World's downs, which already describe the next play.
    public static class ResultCard
    {
        public sealed class Info
        {
            public string Result = "";      // the sim's word: TACKLED, SACKED, INCOMPLETE, ...
            public string Headline = "";    // what the TV says: "TACKLED", "TOUCHDOWN!"
            public int Yards;               // gained by the offense (a loss is negative)
            public bool ShowYards;          // (not for interceptions)
            public string YardsText = "";   // "+7 YARDS", "LOSS OF 3", "NO GAIN"
            public string ByVerb = "";      // "Tackle by", "Sack by", "Picked off by", "Scored by", "Thrown to"
            public int BySlot = -1, ByNumber, ByTeam = -1;
            public string ByPos = "";
            public string NextText = "";    // "2ND & 3", "FIRST DOWN · 1ST & 10", "TURNOVER ON DOWNS · GRAINS BALL, 1ST & 10"
        }

        // Call just after the whistle. play: the play's events, from the hike to the whistle; los: the line of
        // scrimmage at the hike.
        public static Info Build(World w, List<GameEvent> play, float los)
        {
            var c = new Info();
            GameEvent? whistle = null;
            foreach (var e in play) if (e.Kind == EventKind.Whistle) whistle = e;
            c.Result = whistle.HasValue ? whistle.Value.Text ?? "" : w.Result;
            c.Headline = Headline(c.Result);

            // the yards, as the sim counts them (LivePlay.Whistle): a touchdown counts to the goal line
            var f = w.Cfg.field;
            float end = c.Result == "TOUCHDOWN" ? f.GoalLineX * w.Dir : w.NextLos;
            c.Yards = (int)Math.Round((end - los) * w.Dir);
            c.ShowYards = c.Result != "INTERCEPTED" && c.Result != "PICK SIX";
            c.YardsText = YardsText(c.Yards);

            // who made the play: the last event that names him
            foreach (var e in play)
            {
                if (c.ByVerb == "Picked off by") break;   // (the pick is the play, whoever brings him down)
                string verb = e.Kind == EventKind.Tackle ? TackleVerb(e.Text) : e.Kind == EventKind.Sack ? "Sack by"
                    : e.Kind == EventKind.Interception ? "Picked off by" : e.Kind == EventKind.Touchdown ? "Scored by"
                    : e.Kind == EventKind.Catch ? "Caught by" : e.Kind == EventKind.Handoff ? "Run by" : null;
                if (verb != null) Name(w, c, verb, e.Slot);
                if (e.Kind == EventKind.Throw && c.Result == "INCOMPLETE") Name(w, c, "Thrown to", e.Other);
            }
            if (c.Result == "INCOMPLETE" && c.ByVerb != "Thrown to") { c.ByVerb = ""; c.BySlot = -1; }   // (a catch that came loose: not his catch)

            // the next play: the World's downs already describe it
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(w.Note)) parts.Add(w.Note);
            if (Downs.On(w))
            {
                int team = w.PendingTurnover ? w.Defense : w.Offense;
                string down = Downs.DownText(w);
                parts.Add(w.PendingTurnover && team >= 0 && team < w.Cfg.teams.Count ? w.Cfg.teams[team].name.ToUpperInvariant() + " BALL, " + down : down);
            }
            c.NextText = string.Join(" · ", parts);
            return c;
        }

        // FEATURES.hitAngle: the sim notes how a tackle landed.
        public static string TackleVerb(string how) =>
            how == "BIG HIT" ? "Big hit by" : how == "TRIPPED" ? "Tripped up by" : how == "LOST BALANCE" ? "Knocked off balance by" : "Tackle by";

        static void Name(World w, Info c, string verb, int slot)
        {
            var p = slot >= 0 ? w.Find(slot) : null;
            if (p == null) return;
            c.ByVerb = verb; c.BySlot = p.Slot; c.ByNumber = p.Number; c.ByPos = p.Pos; c.ByTeam = p.Team;
        }

        public static string Headline(string result)
        {
            switch (result)
            {
                case "TACKLED": return "TACKLED";
                case "SACKED": return "SACKED!";
                case "INCOMPLETE": return "INCOMPLETE";
                case "INTERCEPTED": return "INTERCEPTED!";
                case "TOUCHDOWN": return "TOUCHDOWN!";
                case "PICK SIX": return "PICK SIX!";
                case "OUT OF BOUNDS": return "OUT OF BOUNDS";
                default: return string.IsNullOrEmpty(result) ? "PLAY OVER" : result;
            }
        }

        public static string YardsText(int yards) =>
            yards > 0 ? "+" + yards + (yards == 1 ? " YARD" : " YARDS") : yards < 0 ? "LOSS OF " + (-yards) : "NO GAIN";

        // "Tackle by Kenny (#54 LB)", or "Tackle by #54 LB" for a bot. names: a person's name by slot (or null).
        public static string ByLine(Info c, Func<int, string> names)
        {
            if (c == null || c.BySlot < 0 || c.ByVerb.Length == 0) return "";
            string who = "#" + c.ByNumber + " " + c.ByPos;
            string name = names != null ? names(c.BySlot) : null;
            return c.ByVerb + " " + (string.IsNullOrEmpty(name) ? who : name + " (" + who + ")");
        }

        static GUIStyle big, line;

        // Bottom centre of the TV, over the replay.
        public static void Draw(Info c, GameConfig cfg, Func<int, string> names, float s)
        {
            if (c == null) return;
            var rc = cfg.replay;
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            line ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            big.fontSize = Mathf.RoundToInt(rc.cardTitlePx * s);
            line.fontSize = Mathf.RoundToInt(rc.cardLinePx * s);
            var rows = new List<string>();
            if (c.ShowYards) rows.Add(c.YardsText);
            string by = ByLine(c, names);
            if (by.Length > 0) rows.Add(by);
            if (c.NextText.Length > 0) rows.Add("Next: " + c.NextText);
            float titleH = big.lineHeight + 8 * s, rowH = line.lineHeight + 4 * s;
            float w = rc.cardWidthPx * s, h = titleH + rows.Count * rowH + 24 * s;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 40 * s, w, h);
            GUI.color = new Color(0.05f, 0.13f, 0.1f, 0.88f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            // a stripe in the colour of the team that made the play
            var team = c.ByTeam >= 0 && c.ByTeam < cfg.teams.Count ? cfg.teams[c.ByTeam] : null;
            GUI.color = team != null ? Colors.Hex(team.color) : new Color(1f, 0.82f, 0.25f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 8 * s), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float y = r.y + 12 * s;
            GUI.Label(new Rect(r.x, y, r.width, titleH), c.Headline, big);
            y += titleH;
            foreach (var row in rows) { GUI.Label(new Rect(r.x, y, r.width, rowH), row, line); y += rowH; }
        }
    }
}
