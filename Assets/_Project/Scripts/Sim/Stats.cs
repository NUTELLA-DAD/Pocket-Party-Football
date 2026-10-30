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
    // plays that were not turnovers (an interception, a lost fumble) or kicks. A completion's yards go to the receiver
    // (and the passer's passing yards); anything else the offense carried (a handoff, a QB run, a sack, a fumble it
    // kept) is a rush by the man it was handed to, or the QB.
    // A touchdown's points go to the scorer: whatever his team's score went up by (6, or 2 on a try with
    // FEATURES.kicking). Points with no scorer in the events (kicks and safeties: WS-01 pushes no event for them) go
    // to the team's Extra, so the team totals always match the score.
    // Everyone plays both ways, so each player keeps two lines, one for each unit: what he did while his team had the
    // ball at the hike (the offense) and what he did while the other team did (the defense). The post-game screen lists
    // both, so a team's offense still shows when the game ends with it on defense.
    public sealed class PlayerLine
    {
        public int Slot, Team, Spot, Number;
        public string Pos = "";
        public bool Human;
        public int Recruit = -1;   // FEATURES.season: the recruit he wore (Season.Record puts the line on him)
        public bool Offense;       // the unit: his team had the ball at the hike
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
        int[] lastScore, scorer;
        int[] extra = new int[0];
        // the play under way
        bool live, picked, kick;
        float los, dir;
        bool started;
        int offense, holder = -1, passer = -1, receiver = -1;

        public static bool On(GameConfig cfg) => cfg.Feature("stats");

        public IEnumerable<PlayerLine> Lines => lines.Values;

        // Points a team scored that no player's touchdown explains (kicks, safeties).
        public int Extra(int team) => team >= 0 && team < extra.Length ? extra[team] : 0;

        static int Key(int slot, bool offense) => slot + slot + (offense ? 0 : 1);   // (two lines a slot)

        // A player's whole game, both units added up, with the spot he stands in now (a new line, not stored).
        public PlayerLine Of(Player p)
        {
            if (p == null) return new PlayerLine { Slot = -1, Team = -1 };
            var l = new PlayerLine { Slot = p.Slot };
            if (lines.TryGetValue(Key(p.Slot, true), out var o)) Add(l, o);
            if (lines.TryGetValue(Key(p.Slot, false), out var d)) Add(l, d);
            Fill(l, p);
            return l;
        }

        // A player's line for one unit, with that unit's spot, position and number (a zero line when he did nothing there).
        public PlayerLine Unit(World w, Player p, bool offense)
        {
            if (p == null) return new PlayerLine { Slot = -1, Team = -1 };
            if (!lines.TryGetValue(Key(p.Slot, offense), out var l)) l = new PlayerLine { Slot = p.Slot, Offense = offense };
            Fill(l, w, p, offense);
            return l;
        }

        static void Fill(PlayerLine l, Player p)
        {
            l.Team = p.Team; l.Spot = p.Spot; l.Number = p.Number; l.Pos = p.Pos; l.Human = p.Human; l.Recruit = p.Recruit;
        }

        // ...as he lines up with that unit (his spot there, and its position and number).
        static void Fill(PlayerLine l, World w, Player p, bool offense)
        {
            Fill(l, p);
            l.Offense = offense;
            var spots = offense ? w.Cfg.formation.offense : w.Cfg.formation.defense;
            int spot = Formation.TwoWay(w) ? Formation.SpotOn(w, p, offense) : p.Spot;
            if (spot < 0 || spot >= spots.Count) return;
            l.Spot = spot; l.Pos = spots[spot].pos; l.Number = spots[spot].number;
        }

        static void Add(PlayerLine to, PlayerLine l)
        {
            to.Rushes += l.Rushes; to.RushYd += l.RushYd; to.Catches += l.Catches; to.RecYd += l.RecYd;
            to.PassAtt += l.PassAtt; to.PassComp += l.PassComp; to.PassYd += l.PassYd;
            to.Tackles += l.Tackles; to.Sacks += l.Sacks; to.Picks += l.Picks; to.Jukes += l.Jukes;
            to.Tds += l.Tds; to.Points += l.Points;
        }

        // The line of the unit the player is on this play (his team had the ball at the hike, or not).
        PlayerLine Line(World w, int slot)
        {
            if (slot < 0) return null;
            var p = w.Find(slot);
            bool unit = p == null || p.Team == offense;
            int key = Key(slot, unit);
            if (!lines.TryGetValue(key, out var l)) { l = new PlayerLine { Slot = slot, Team = -1, Offense = unit }; lines[key] = l; }
            if (p != null) Fill(l, w, p, unit);
            return l;
        }

        // Call after the World stepped: once a tick, or once a frame (it reads every event since the last call).
        public void Observe(World w)
        {
            if (!On(w.Cfg)) return;
            int teams = w.Score.Length;
            if (lastScore == null) { lastScore = new int[teams]; scorer = new int[teams]; extra = new int[teams]; }
            if (!started) { offense = w.Offense; started = true; }   // (before the first hike: the team that starts with it)
            for (int t = 0; t < teams; t++) scorer[t] = -1;
            fresh.Clear();
            w.Events.Since(nextSeq, fresh);
            nextSeq = w.Events.Next;
            foreach (var e in fresh)
            {
                PlayerLine l;
                switch (e.Kind)
                {
                    case EventKind.Hike:
                        // (the line and the way stay put until the next huddle, so reading them now is the hike's)
                        live = true; picked = false; passer = -1; receiver = -1; holder = e.Slot;
                        los = w.LineOfScrimmageX; dir = w.Dir; offense = w.Offense;
                        kick = Kicking.Mode(w) != KickKind.None;   // (kickoffs, punts and field goals count no yards)
                        break;
                    case EventKind.Handoff:
                        holder = e.Slot;
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
                        if ((l = Line(w, e.Slot)) != null) l.Tds++;
                        if (e.Team >= 0 && e.Team < teams) scorer[e.Team] = e.Slot;
                        break;
                    case EventKind.Whistle:
                        if (live && !picked && !kick && e.Text != "FUMBLE LOST") Yards(w, e);
                        live = false;
                        break;
                }
            }
            // whatever the score did that no touchdown explains
            for (int t = 0; t < teams; t++)
            {
                int gained = w.Score[t] - lastScore[t];
                var by = scorer[t] >= 0 ? Line(w, scorer[t]) : null;
                if (by != null && gained > 0) by.Points += gained;
                else extra[t] += gained;
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
            // (no one has it and it is not a fumble: a pass in the air at the end of the play, no gain)
            if (e.Slot < 0 && !result.StartsWith("FUMBLE", StringComparison.Ordinal)) return;
            var c = Line(w, holder);
            if (c == null || c.Team != offense) return;
            c.Rushes++;
            c.RushYd += yards;
        }

        // A team's totals: yards, catches, tackles, picks, touchdowns, and points (with the Extra).
        public PlayerLine Team(int team)
        {
            var t = new PlayerLine { Slot = -1, Team = team };
            foreach (var l in lines.Values) if (l.Team == team) Add(t, l);
            t.Points += Extra(team);
            return t;
        }

        // How much a line did, for the player of the game.
        public static float Impact(PlayerLine l, StatsConfig c) =>
            l.Yards * c.mvpPerYd + l.Catches * c.mvpPerCatch + l.Tackles * c.mvpPerTackle + l.Sacks * c.mvpPerSack
            + l.Picks * c.mvpPerPick + l.Tds * c.mvpPerTd;

        // The player of the game: the biggest impact over both units (the lowest slot on a tie); null when nobody did
        // anything. His line adds up both units and shows the one where he did the most.
        public PlayerLine Mvp(StatsConfig c)
        {
            PlayerLine best = null; float most = 0f;
            foreach (var l in lines.Values)
            {
                if (!l.Offense && lines.ContainsKey(Key(l.Slot, true))) continue;   // (counted with his offense line)
                var whole = Whole(l.Slot, c);
                float v = Impact(whole, c);
                if (v > most) { best = whole; most = v; }
            }
            return best;
        }

        // Both of a slot's units added up, labelled with the unit where he did more.
        PlayerLine Whole(int slot, StatsConfig c)
        {
            lines.TryGetValue(Key(slot, true), out var o);
            lines.TryGetValue(Key(slot, false), out var d);
            var top = o == null ? d : d == null ? o : Impact(d, c) > Impact(o, c) ? d : o;
            var l = new PlayerLine { Slot = slot, Team = top.Team, Spot = top.Spot, Number = top.Number, Pos = top.Pos, Human = top.Human, Recruit = top.Recruit, Offense = top.Offense };
            if (o != null) Add(l, o);
            if (d != null) Add(l, d);
            return l;
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
