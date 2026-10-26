using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The post-game screen on the TV (WS-12, FEATURES.stats; NO_STATS=1 turns it off): when the game clock (WS-02)
    // ends the game, stats.showAfterS after the final whistle, FINAL and the score, the winner, the player of the
    // game, and both teams side by side, all 22 players with their yards, catches, tackles, picks and touchdowns.
    // It also keeps the stats (Sim/Stats.cs) and the score celebrations (Celebrations.cs) for the live game.
    // Boot/PostGameHost.cs drives it with one line at the end of PartyHost.Update and sends the phones their card.
    public sealed class PostGame : MonoBehaviour
    {
        World live;
        PlayersView liveView;
        Func<int, string> nameOf;
        float overFor;
        GUIStyle title, big, line, head, cell, cellBold;

        public Stats Stats { get; private set; }
        public Celebrations Cheers { get; private set; }
        public bool Showing { get; private set; }
        // the host's bookkeeping: the dev key's preview, when the phones get their card next (TV seconds)
        public bool Preview;
        public float NextPhoneAt;

        // From PartyHost.Update, after the live view drew. over: the game is over (the clock says so, or the preview
        // key); names: a person's name by slot (or null for a bot).
        public static PostGame Drive(GameObject on, World live, PlayersView liveView, bool over, Func<int, string> names)
        {
            if (!Stats.On(live.Cfg)) return null;
            var g = on.GetComponent<PostGame>();
            if (g == null)
            {
                g = on.AddComponent<PostGame>();
                g.live = live; g.liveView = liveView;
                g.Stats = new Stats();
                g.Cheers = new Celebrations();
            }
            g.nameOf = names;
            g.Frame(over);
            return g;
        }

        void Frame(bool over)
        {
            Stats.Observe(live);
            Cheers.Observe(live);
            if (liveView != null)
                foreach (var p in live.Players) Celebrations.Apply(liveView.Figure(p.Slot), Cheers.Of(live, p.Slot));
            overFor = over ? overFor + Time.unscaledDeltaTime : 0f;
            Showing = over && (Preview || overFor >= live.Cfg.stats.showAfterS);
        }

        // ---- the layout: pure, so a test can check 22 rows fit without overlapping on any TV ----

        public static readonly string[] Columns = { "#", "POS", "NAME", "YDS", "REC", "TKL", "INT", "TD" };
        public static readonly float[] ColumnFrac = { 0.08f, 0.09f, 0.35f, 0.12f, 0.09f, 0.09f, 0.09f, 0.09f };

        public sealed class Layout
        {
            public Rect Title, Score, Mvp;
            public Rect[] TeamHead, ColumnHead;   // by team
            public Rect[][] Rows;                 // by team, then row
            public float TitleFont, ScoreFont, LineFont, HeadFont, RowFont;
        }

        // A screen of width x height pixels, `teams` side by side with `rows` players each.
        public static Layout Lay(float width, float height, int teams, int rows, StatsConfig c)
        {
            float s = height / 1080f;
            var l = new Layout
            {
                TitleFont = c.titlePx * s, ScoreFont = c.scorePx * s, LineFont = c.linePx * s, HeadFont = c.headPx * s,
                TeamHead = new Rect[teams], ColumnHead = new Rect[teams], Rows = new Rect[teams][],
            };
            float m = c.marginPx * s, gap = c.gapPx * s;
            float y = m, w = width - 2f * m;
            l.Title = new Rect(m, y, w, l.TitleFont * 1.3f); y += l.Title.height;
            l.Score = new Rect(m, y, w, l.ScoreFont * 1.3f); y += l.Score.height;
            l.Mvp = new Rect(m, y, w, l.LineFont * 1.5f); y += l.Mvp.height + gap * 0.5f;
            float colW = (w - gap * (teams - 1)) / Mathf.Max(1, teams);
            float headH = l.HeadFont * 1.7f, colHeadH = l.HeadFont * 1.4f;
            float rowsTop = y + headH + colHeadH;
            float avail = height - m - rowsTop;
            float rowH = Mathf.Min(c.rowPx * s * 1.45f, avail / Mathf.Max(1, rows));
            l.RowFont = rowH / 1.45f;
            for (int t = 0; t < teams; t++)
            {
                float x = m + t * (colW + gap);
                l.TeamHead[t] = new Rect(x, y, colW, headH);
                l.ColumnHead[t] = new Rect(x, y + headH, colW, colHeadH);
                l.Rows[t] = new Rect[rows];
                for (int r = 0; r < rows; r++) l.Rows[t][r] = new Rect(x, rowsTop + r * rowH, colW, rowH);
            }
            return l;
        }

        // One player's row: number, position, name (or BOT, cut to stats.nameChars), YDS, REC, TKL, INT, TD.
        public static string[] Cells(PlayerLine l, string name, StatsConfig c)
        {
            string n = string.IsNullOrEmpty(name) ? "BOT" : name.ToUpperInvariant();
            if (n.Length > c.nameChars) n = n.Substring(0, c.nameChars);
            return new[] { l.Number.ToString(), l.Pos, n, l.Yards.ToString(), l.Catches.ToString(), l.Tackles.ToString(), l.Picks.ToString(), l.Tds.ToString() };
        }

        // A team's players for the screen: the biggest impact first, then by spot.
        public static List<PlayerLine> TeamRows(World w, Stats stats, int team)
        {
            var rows = new List<PlayerLine>();
            foreach (var p in w.Players) if (p.Team == team) rows.Add(stats.Of(p));
            var c = w.Cfg.stats;
            rows.Sort((a, b) =>
            {
                float ia = Stats.Impact(a, c), ib = Stats.Impact(b, c);
                return ia != ib ? ib.CompareTo(ia) : a.Spot != b.Spot ? a.Spot.CompareTo(b.Spot) : a.Slot.CompareTo(b.Slot);
            });
            return rows;
        }

        // "CHIEFS WIN!", "IT'S A TIE!"
        public static string WinnerLine(World w)
        {
            int best = -1, most = int.MinValue; bool tie = false;
            for (int t = 0; t < w.Score.Length; t++)
            {
                if (w.Score[t] > most) { best = t; most = w.Score[t]; tie = false; }
                else if (w.Score[t] == most) tie = true;
            }
            return tie || best < 0 || best >= w.Cfg.teams.Count ? "IT'S A TIE!" : w.Cfg.teams[best].name.ToUpperInvariant() + " WIN!";
        }

        // "Player of the game: KENNY (#87 WR) · 1 TD · 112 YDS · 3 REC", or "" when nobody did anything.
        public static string MvpLine(World w, Stats stats, Func<int, string> names)
        {
            var m = stats.Mvp(w.Cfg.stats);
            if (m == null) return "";
            var p = w.Find(m.Slot);
            if (p != null) m = stats.Of(p);
            string name = names != null ? names(m.Slot) : null;
            string who = "#" + m.Number + " " + m.Pos;
            return "Player of the game: " + (string.IsNullOrEmpty(name) ? who : name.ToUpperInvariant() + " (" + who + ")") + " · " + Stats.Summary(m);
        }

        // ---- drawing ----

        void OnGUI()
        {
            if (!Showing || live == null) return;
            GUI.depth = -10;   // over the game's HUD
            var cfg = live.Cfg;
            var c = cfg.stats;
            int teams = cfg.teams.Count;
            int rows = cfg.players.perTeam;
            for (int t = 0; t < teams; t++) rows = Mathf.Max(rows, live.TeamCount(t));
            var l = Lay(Screen.width, Screen.height, teams, rows, c);

            title ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            line ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            head ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            cell ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            cellBold ??= new GUIStyle(cell) { fontStyle = FontStyle.Bold };
            title.fontSize = Mathf.RoundToInt(l.TitleFont);
            big.fontSize = Mathf.RoundToInt(l.ScoreFont);
            line.fontSize = Mathf.RoundToInt(l.LineFont);
            head.fontSize = Mathf.RoundToInt(l.HeadFont);
            cell.fontSize = cellBold.fontSize = Mathf.RoundToInt(l.RowFont);

            // the whole TV: the game is over
            GUI.color = new Color(0.04f, 0.1f, 0.08f, 0.92f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(l.Title, "FINAL  ·  " + WinnerLine(live), title);
            var score = new List<string>();
            for (int t = 0; t < teams; t++) score.Add("<color=" + cfg.teams[t].color + ">" + cfg.teams[t].name.ToUpperInvariant() + "</color> " + live.Score[t]);
            GUI.Label(l.Score, string.Join("   —   ", score), big);
            string mvp = MvpLine(live, Stats, nameOf);
            if (mvp.Length > 0) { GUI.color = new Color(1f, 0.85f, 0.35f); GUI.Label(l.Mvp, mvp, line); GUI.color = Color.white; }

            for (int t = 0; t < teams; t++)
            {
                var tc = Colors.Hex(cfg.teams[t].color);
                // the team: its colour, its name and totals
                GUI.color = tc;
                GUI.DrawTexture(l.TeamHead[t], Texture2D.whiteTexture);
                GUI.color = Color.white;
                var tot = Stats.Team(t);
                string extra = Stats.Extra(t) != 0 ? " · " + Stats.Extra(t) + " PTS KICKING" : "";
                Label(Inset(l.TeamHead[t], l.HeadFont * 0.5f), cfg.teams[t].name.ToUpperInvariant() + "   " + tot.Yards + " YDS · " + tot.Tds + " TD · " + tot.Picks + " INT" + extra, head);
                Cols(l.ColumnHead[t], Columns, head, new Color(1f, 1f, 1f, 0.7f));
                var players = TeamRows(live, Stats, t);
                for (int r = 0; r < players.Count && r < l.Rows[t].Length; r++)
                {
                    var rr = l.Rows[t][r];
                    GUI.color = r % 2 == 0 ? new Color(1f, 1f, 1f, 0.06f) : new Color(1f, 1f, 1f, 0.02f);
                    GUI.DrawTexture(rr, Texture2D.whiteTexture);
                    var pl = players[r];
                    string name = pl.Human && nameOf != null ? nameOf(pl.Slot) : null;
                    if (pl.Human) { GUI.color = tc; GUI.DrawTexture(new Rect(rr.x, rr.y, rr.height * 0.18f, rr.height), Texture2D.whiteTexture); }
                    Cols(rr, Cells(pl, name, c), pl.Human ? cellBold : cell, Color.white);
                }
            }
            GUI.color = Color.white;
        }

        static Rect Inset(Rect r, float by) => new Rect(r.x + by, r.y, r.width - 2f * by, r.height);

        static void Label(Rect r, string text, GUIStyle style) => GUI.Label(r, text, style);

        // One row of cells across the columns (ColumnFrac of the row's width each).
        static void Cols(Rect r, string[] cells, GUIStyle style, Color color)
        {
            GUI.color = color;
            float x = r.x + r.height * 0.4f, w = r.width - r.height * 0.4f;
            for (int i = 0; i < cells.Length && i < ColumnFrac.Length; i++)
            {
                float cw = w * ColumnFrac[i];
                GUI.Label(new Rect(x, r.y, cw, r.height), cells[i], style);
                x += cw;
            }
            GUI.color = Color.white;
        }
    }
}
