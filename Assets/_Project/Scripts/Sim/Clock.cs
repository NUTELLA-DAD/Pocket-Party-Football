using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // The game clock (WS-02), behind FEATURES.clock (NO_CLOCK=1: no clock, the game never ends).
    // Four quarters of clock.quarterS. The clock starts at the snap and runs through the play and on between plays,
    // except after the results in clock.stopOn (out of bounds, an incomplete pass, a score): then it waits for the
    // next snap. A quarter that runs out mid-play ends at the whistle. At half (after clock.halftimeAfterQuarter) the
    // game holds for halftimeS, then the sides switch: each team attacks the other end from the first half, and the
    // team that started on defense gets the ball at its own 25 (startOwnYd). After the last quarter the game is over:
    // it holds on the final whistle for good and Winner names the team with more points (-1: a tie).
    //
    // Overtime (FEATURES.overtime, NO_OVERTIME=1: a tie stands). Regulation ending level brings a break (overtime.breakS),
    // a coin toss from the game's seed, and an overtime period of PeriodS (a share of the game's quarter, kept between
    // overtime.minS and maxS, so a quick game still has time for two drives). Ends stay as in the second half. The
    // toss winner receives the kickoff; then, like the pros: both teams get one possession. The game ends as soon as the
    // team that had the ball second goes ahead, or the defense scores on the first possession; after both possessions a
    // team ahead wins. Still level: sudden death, the next score wins (a touchdown ends it, no try). The clock never
    // takes the second team's turn away: run out before both have had the ball, the period plays on at 0:00 until they
    // have. A period that runs out level is a tie, unless overtime.mustWin (cup and season games): then another
    // period, sudden death, until somebody scores. A touchdown as time runs out (any quarter) still gets its try.
    public sealed class ClockState
    {
        public bool Started;
        public int Quarter = 1;
        public int TicksLeft;            // in this quarter
        public bool Running;
        public bool SecondHalf;
        public int BreakTicks;           // > 0: halftime
        public bool Over;                // the game is over
        public int FirstOffense;         // who had the ball at the opening snap
        public float[] FirstDir;         // by team: the way it attacked in the first half
        public Phase LastPhase;
        // FEATURES.overtime
        public bool Overtime;            // regulation ended level: Quarter past clock.quarters is an overtime period
        public int OtFirst = -1;         // the toss winner, who receives first
        public OtStage OtStage;          // whose overtime possession it is, or sudden death
    }

    public enum OtStage { First, Second, SuddenDeath }

    // Config/game.json "overtime" (FEATURES.overtime). A period is quarterS * periodFrac, kept within minS..maxS.
    public sealed class OvertimeConfig
    {
        public float periodFrac, minS, maxS, breakS;
        public bool mustWin;             // no ties: periods until somebody scores (the cup and season hosts set it)
    }

    // The clock's numbers (game.json "clock").
    public sealed class ClockConfig
    {
        public int quarters, halftimeAfterQuarter;
        public float quarterS, halftimeS;
        public bool runsBetweenPlays;
        public List<string> stopOn = new List<string>();
    }

    public static class Clock
    {
        public static bool On(World w) => w.Cfg.Feature("clock");

        public static bool IsOver(World w) => On(w) && w.GameClock.Over;
        public static bool IsHalftime(World w) => On(w) && w.GameClock.BreakTicks > 0 && !w.GameClock.Overtime;

        // FEATURES.overtime
        public static bool OvertimeOn(World w) => On(w) && w.Cfg.Feature("overtime") && Downs.On(w) && w.Score.Length == 2;   // hygiene:allow (two teams)
        public static bool InOvertime(World w) => On(w) && w.GameClock.Overtime;
        public static bool IsOvertimeBreak(World w) => InOvertime(w) && w.GameClock.BreakTicks > 0;

        // 1 for the first overtime period, 2 for the second... (0 in regulation).
        public static int OvertimePeriod(World w) => InOvertime(w) ? w.GameClock.Quarter - w.Cfg.clock.quarters : 0;

        // An overtime period's length: a share of the quarter, within minS..maxS.
        public static float PeriodS(GameConfig cfg)
        {
            var o = cfg.overtime;
            float s = cfg.clock.quarterS * o.periodFrac;
            if (s < o.minS) s = o.minS;
            if (o.maxS > 0f && s > o.maxS) s = o.maxS;
            return s;
        }

        // Whole seconds left in the quarter, rounded up (0:00 only when it has truly run out).
        public static int SecondsLeft(World w)
        {
            int hz = w.Cfg.sim.tickHz;
            return (w.GameClock.TicksLeft + hz - 1) / hz;
        }

        // The way a team attacks: the offense goes w.Dir, the defense the other way.
        public static float AttackDir(World w, int team) => team == w.Offense ? w.Dir : -w.Dir;

        // The team with more points, -1 for a tie.
        public static int Winner(World w)
        {
            int best = -1, most = int.MinValue;
            bool tie = false;
            for (int t = 0; t < w.Score.Length; t++)
            {
                if (w.Score[t] > most) { best = t; most = w.Score[t]; tie = false; }
                else if (w.Score[t] == most) tie = true;
            }
            return tie ? -1 : best;
        }

        // After everything else this tick (World.Step's last call).
        public static void Step(World w)
        {
            if (!On(w)) return;
            var c = w.GameClock;
            var cfg = w.Cfg.clock;
            if (!c.Started)
            {
                c.Started = true;
                c.TicksLeft = Huddle.Ticks(w, cfg.quarterS);
                c.FirstOffense = w.Offense;
                c.FirstDir = new float[w.Score.Length];
                for (int t = 0; t < c.FirstDir.Length; t++) c.FirstDir[t] = AttackDir(w, t);
                c.LastPhase = w.Phase;
            }
            if (c.Over) { Hold(w); return; }
            if (c.BreakTicks > 0)
            {
                Hold(w);
                if (--c.BreakTicks == 0) { if (c.Overtime) StartOvertime(w); else StartSecondHalf(w); }
                c.LastPhase = w.Phase;
                return;
            }

            // the snap starts it; the whistle stops it after the stop results (or always, with runsBetweenPlays off)
            if (w.Phase == Phase.Live && c.LastPhase != Phase.Live) c.Running = true;
            if (w.Phase == Phase.Whistle && c.LastPhase == Phase.Live && (!cfg.runsBetweenPlays || cfg.stopOn.Contains(w.Result))) c.Running = false;
            if (c.Running && c.TicksLeft > 0) c.TicksLeft--;
            if (c.Overtime && w.Phase != Phase.Live && OvertimeDecided(w))
            {
                c.Running = false;
                c.Over = true;
                Hold(w);
                c.LastPhase = w.Phase;
                return;
            }
            // out of time: a live play is finished first (and, with overtime, a try after a touchdown, and the second
            // team's overtime possession)
            bool waits = OvertimeOn(w) && (w.Kick.Try || (c.Overtime && c.OtStage != OtStage.SuddenDeath));
            if (c.TicksLeft == 0 && w.Phase != Phase.Live && !waits) EndQuarter(w);
            c.LastPhase = w.Phase;
        }

        // Who has (or gets) the ball next: through a try it is still the scoring team's; a kickoff (set up or under
        // way) is the receiving team's.
        static int Holder(World w)
        {
            int next = w.PendingTurnover ? w.Defense : w.Offense;
            int other = next == 0 ? 1 : 0;
            return w.Kick.PendingKickoff || w.Kick.Kickoff ? other : next;
        }

        // Between plays in overtime: moves the possessions on and says whether somebody has won.
        static bool OvertimeDecided(World w)
        {
            var c = w.GameClock;
            int first = c.OtFirst, second = first == 0 ? 1 : 0;
            int lead = Winner(w), holder = Holder(w);
            if (c.OtStage == OtStage.First)
            {
                if (lead == second) return true;         // the defense scored on the first possession
                if (holder != first) c.OtStage = OtStage.Second;      // the first possession is over: the second team's turn
            }
            if (c.OtStage == OtStage.Second)
            {
                if (lead == second) return true;         // the second team went ahead
                if (holder != second)
                {
                    if (lead >= 0) return true;          // both have had the ball and one is ahead
                    c.OtStage = OtStage.SuddenDeath;                       // level after both: sudden death
                }
            }
            return c.OtStage == OtStage.SuddenDeath && lead >= 0;
        }

        static void EndQuarter(World w)
        {
            var c = w.GameClock;
            var cfg = w.Cfg.clock;
            c.Running = false;
            if (c.Quarter >= cfg.quarters)
            {
                bool level = Winner(w) < 0;
                if (level && !c.Overtime && OvertimeOn(w)) { BeginOvertime(w); return; }
                if (level && c.Overtime && w.Cfg.overtime.mustWin)
                {
                    // another period: sudden death goes on
                    c.Quarter++;
                    c.TicksLeft = Huddle.Ticks(w, PeriodS(w.Cfg));
                    return;
                }
                c.Over = true; Hold(w); return;
            }
            c.Quarter++;
            c.TicksLeft = Huddle.Ticks(w, cfg.quarterS);
            if (c.Quarter != cfg.halftimeAfterQuarter + 1) return;
            c.BreakTicks = Huddle.Ticks(w, cfg.halftimeS);
            if (c.BreakTicks > 0) Hold(w);
            else StartSecondHalf(w);
        }

        // Regulation ended level: the toss (from the game's seed, so the sim's own random numbers are untouched), then the
        // break before the first overtime period.
        static void BeginOvertime(World w)
        {
            var c = w.GameClock;
            c.Overtime = true;
            c.OtStage = OtStage.First;
            c.OtFirst = new Rng(w.Seed).Value() < 0.5f ? 0 : 1;
            c.Quarter++;
            c.TicksLeft = Huddle.Ticks(w, PeriodS(w.Cfg));
            c.BreakTicks = Huddle.Ticks(w, w.Cfg.overtime.breakS);
            if (c.BreakTicks > 0) Hold(w);
            else StartOvertime(w);
        }

        // The toss winner receives: the other team kicks off from its own 35 (no kicking: the toss winner starts at its
        // own 25). The teams keep their second-half ends.
        static void StartOvertime(World w)
        {
            var c = w.GameClock;
            var d = w.Cfg.downs;
            float goal = w.Cfg.field.GoalLineX;
            bool kick = Kicking.On(w);
            int receiving = c.OtFirst;
            w.Offense = kick ? (receiving == 0 ? 1 : 0) : receiving;
            w.Dir = -c.FirstDir[w.Offense];
            w.PendingTurnover = false;
            w.Kick.Try = false;
            w.Kick.PendingKickoff = kick;
            w.LineOfScrimmageX = -w.Dir * (goal - (kick ? w.Cfg.kicking.kickoffFromYd : d.startOwnYd));
            w.NextLos = w.LineOfScrimmageX;
            w.Down = 1;
            w.FirstDownX = w.LineOfScrimmageX + w.Dir * d.firstDownYd;
            w.Note = "";
            w.Result = "";
            Huddle.Begin(w);
        }

        // Halftime and the end of the game: the whistle phase, kept from running out so no huddle starts.
        static void Hold(World w)
        {
            w.Phase = Phase.Whistle;
            w.PhaseTicks = Huddle.Ticks(w, w.Cfg.play.whistleS) + 1;
        }

        // The sides switch: both teams attack the other end, the team that started on defense gets the ball at its 25.
        static void StartSecondHalf(World w)
        {
            var c = w.GameClock;
            var d = w.Cfg.downs;
            float goal = w.Cfg.field.GoalLineX;
            c.SecondHalf = true;
            w.Offense = c.FirstOffense == 0 ? 1 : 0;
            w.Dir = -c.FirstDir[w.Offense];
            w.PendingTurnover = false;
            w.LineOfScrimmageX = -w.Dir * (goal - d.startOwnYd);
            w.NextLos = w.LineOfScrimmageX;
            w.Down = 1;
            w.FirstDownX = w.LineOfScrimmageX + w.Dir * d.firstDownYd;
            w.Note = "";
            w.Result = "";
            Huddle.Begin(w);
        }
    }
}
