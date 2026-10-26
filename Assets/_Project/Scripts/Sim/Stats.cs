using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Each player's numbers for the game (WS-12, FEATURES.stats; NO_STATS=1 turns it off): yards, catches, tackles,
    // sacks, picks, touchdowns and points. Counted only from the shared event list (Sim/Events.cs), so it never
    // changes a game and is not in World.Hash. The TV's post-game screen (View/PostGame.cs) and the phones' final
    // card (phone/postgame.js) read it.
    // Yards are counted the way the sim counts them (LivePlay.Whistle): from the line of scrimmage at the hike to the
    // spot of the whistle, a touchdown to the goal line, so a team's yards add up to the World's YardsGained over its
    // plays that were not intercepted. A completion's yards go to the receiver (and the passer's passing yards);
    // anything else the offense carried (a handoff, a QB run, a sack) is a rush by the man who had it at the whistle.
    // A touchdown's points go to the scorer. Points scored without a scorer in the events (kicks and safeties, until
    // their workstreams push an event for them) go to the team's Extra, so the team totals always match the score.
    public sealed class PlayerLine
    {
        public int Slot, Team, Spot, Number;
        public string Pos = "";
        public bool Human;
        public int Rushes, RushYd, Catches, RecYd;
        public int PassAtt, PassComp, PassYd;
        public int Tackles, Sacks, Picks, Jukes;
        public int Tds, Points;

        public int Yards => RushYd + RecYd;
    }

    // The numbers behind the post-game screen (game.json "stats").
    public sealed class StatsConfig
    {
        // player of the game: the most of these, added up (a yard is worth mvpPerYd, a catch mvpPerCatch, ...)
        public float mvpPerYd, mvpPerCatch, mvpPerTackle, mvpPerSack, mvpPerPick, mvpPerTd;
        // the score celebration (View/Celebrations.cs): the scorer spikes it for spikeS; teammates within cheerYd
        // jump and the other team's men within cheerYd slump for celebrateS (at most maxCheer of each)
        public bool celebrate;
        public float celebrateS, spikeS, cheerYd;
        public int maxCheer;
        // the TV's post-game screen, sizes on a 1080p TV: it shows showAfterS after the final whistle
        public float showAfterS, titlePx, scorePx, linePx, headPx, rowPx, minRowPx, marginPx, gapPx;
        public int nameChars;
        // the phones' final card is sent again every phoneResendS (a phone back from a screen lock gets it too)
        public float phoneResendS;
    }

    public sealed class Stats
    {
        readonly SortedDictionary<int, PlayerLine> lines = new SortedDictionary<int, PlayerLine>();
        readonly List<GameEvent> fresh = new List<GameEvent>();
        int nextSeq;
        int[] lastScore, credited;
        int[] extra = new int[0];
        // the play under way
        bool live, picked;
        float los, dir;
        int offense, passer = -1, receiver = -1;

        public static bool On(GameConfig cfg) => cfg.Feature("stats");

        public IEnumerable<PlayerLine> Lines => lines.Values;

        // Points a team scored that no player's touchdown explains (kicks, safeties).
        public int Extra(int team) => team >= 0 && team < extra.Length ? extra[team] : 0;

        // A player's line (a zero line for someone who has done nothing yet; it is not stored).
        public PlayerLine Of(Player p)
        {
            if (p == null) return new PlayerLine { Slot = -1, Team = -1 };
            if (!lines.TryGetValue(p.Slot, out var l)) l = new PlayerLine { Slot = p.Slot };
            Fill(l, p);
            return l;
        }

        static void Fill(PlayerLine l, Player p)
        {
            l.Team = p.Team; l.Spot = p.Spot; l.Number = p.Number; l.Pos = p.Pos; l.Human = p.Human;
        }

        PlayerLine Line(World w, int slot)
        {
            if (slot < 0) return null;
            if (!lines.TryGetValue(slot, out var l)) { l = new PlayerLine { Slot = slot, Team = -1 }; lines[slot] = l; }
            var p = w.Find(slot);
            if (p != null) Fill(l, p);
            return l;
        }

        // Call after the World stepped: once a tick, or once a frame (it reads every event since the last call).
        public void Observe(World w)
        {
            if (!On(w.Cfg)) return;
            int teams = w.Score.Length;
            if (lastScore == null) { lastScore = new int[teams]; credited = new int[teams]; extra = new int[teams]; }
            Array.Clear(credited, 0, teams);
            fresh.Clear();
            w.Events.Since(nextSeq, fresh);
            nextSeq = w.Events.Next;
            int tdPoints = w.Cfg.downs.touchdownPoints;
            foreach (var e in fresh)
            {
                PlayerLine l;
                switch (e.Kind)
                {
                    case EventKind.Hike:
                        // (the line and the way stay put until the next huddle, so reading them now is the hike's)
                        live = true; picked = false; passer = -1; receiver = -1;
                        los = w.LineOfScrimmageX; dir = w.Dir; offense = w.Offense;
                        break;
                    case EventKind.Throw:
                        passer = e.Slot;
                        if ((l = Line(w, e.Slot)) != null) l.PassAtt++;
                        break;
                    case EventKind.Catch:
                        receiver = e.Slot;
                        if ((l = Line(w, e.Slot)) != null) l.Catches++;
                        if ((l = Line(w, passer)) != null) l.PassComp++;
                        break;
                    case EventKind.Incomplete:
                        // a catch that came loose is not a catch
                        if (receiver >= 0)
                        {
                            if ((l = Line(w, receiver)) != null) l.Catches--;
                            if ((l = Line(w, passer)) != null) l.PassComp--;
                            receiver = -1;
                        }
                        break;
                    case EventKind.Interception:
                        picked = true;
                        if ((l = Line(w, e.Slot)) != null) l.Picks++;
                        break;
                    case EventKind.Juke:
                        if ((l = Line(w, e.Slot)) != null) l.Jukes++;
                        break;
                    case EventKind.Tackle:
                        if ((l = Line(w, e.Slot)) != null) l.Tackles++;
                        break;
                    case EventKind.Sack:
                        if ((l = Line(w, e.Slot)) != null) { l.Tackles++; l.Sacks++; }
                        break;
                    case EventKind.Touchdown:
                        if ((l = Line(w, e.Slot)) != null) { l.Tds++; l.Points += tdPoints; }
                        if (e.Team >= 0 && e.Team < teams) credited[e.Team] += tdPoints;
                        break;
                    case EventKind.Whistle:
                        if (live && !picked) Yards(w, e);
                        live = false;
                        break;
                }
            }
            // whatever the score did that no touchdown explains
            for (int t = 0; t < teams; t++)
            {
                extra[t] += w.Score[t] - lastScore[t] - credited[t];
                lastScore[t] = w.Score[t];
            }
        }

        // The play's yards, as LivePlay.Whistle counts them, to the receiver (and the passer) or the man who carried it.
        void Yards(World w, GameEvent e)
        {
            string result = e.Text ?? "";
            if (result == "INCOMPLETE") return;
            var f = w.Cfg.field;
            float lim = f.GoalLineX - f.lineEveryYd * 0.5f;
            float spot = e.X < -lim ? -lim : e.X > lim ? lim : e.X;
            float end = result == "TOUCHDOWN" ? f.GoalLineX * dir : spot;
            int yards = (int)Math.Round((end - los) * dir);
            if (receiver >= 0)
            {
                Line(w, receiver).RecYd += yards;
                var p = Line(w, passer);
                if (p != null) p.PassYd += yards;
                return;
            }
            var c = Line(w, e.Slot);
            if (c == null || c.Team != offense) return;
            c.Rushes++;
            c.RushYd += yards;
        }

        // A team's totals: yards, catches, tackles, picks, touchdowns, and points (with the Extra).
        public PlayerLine Team(int team)
        {
            var t = new PlayerLine { Slot = -1, Team = team };
            foreach (var l in lines.Values)
            {
                if (l.Team != team) continue;
                t.Rushes += l.Rushes; t.RushYd += l.RushYd; t.Catches += l.Catches; t.RecYd += l.RecYd;
                t.PassAtt += l.PassAtt; t.PassComp += l.PassComp; t.PassYd += l.PassYd;
                t.Tackles += l.Tackles; t.Sacks += l.Sacks; t.Picks += l.Picks; t.Jukes += l.Jukes;
                t.Tds += l.Tds; t.Points += l.Points;
            }
            t.Points += Extra(team);
            return t;
        }

        // How much a line did, for the player of the game.
        public static float Impact(PlayerLine l, StatsConfig c) =>
            l.Yards * c.mvpPerYd + l.Catches * c.mvpPerCatch + l.Tackles * c.mvpPerTackle + l.Sacks * c.mvpPerSack
            + l.Picks * c.mvpPerPick + l.Tds * c.mvpPerTd;

        // The player of the game: the biggest impact (the lowest slot on a tie); null when nobody did anything.
        public PlayerLine Mvp(StatsConfig c)
        {
            PlayerLine best = null; float most = 0f;
            foreach (var l in lines.Values)
            {
                float v = Impact(l, c);
                if (v > most) { best = l; most = v; }
            }
            return best;
        }

        // "112 YDS · 3 REC · 1 TD": the numbers that are not zero, the biggest kinds first ("NO STATS" for none).
        public static string Summary(PlayerLine l)
        {
            var parts = new List<string>();
            if (l.Tds != 0) parts.Add(l.Tds + " TD");
            if (l.Yards != 0 || l.Rushes != 0 || l.Catches != 0) parts.Add(l.Yards + " YDS");
            if (l.Catches != 0) parts.Add(l.Catches + " REC");
            if (l.PassComp != 0) parts.Add(l.PassComp + "/" + l.PassAtt + " PASSING, " + l.PassYd + " YDS");
            if (l.Picks != 0) parts.Add(l.Picks + " INT");
            if (l.Sacks != 0) parts.Add(l.Sacks + " SACK");
            if (l.Tackles != 0) parts.Add(l.Tackles + " TKL");
            return parts.Count == 0 ? "NO STATS" : string.Join(" · ", parts);
        }
    }
}
