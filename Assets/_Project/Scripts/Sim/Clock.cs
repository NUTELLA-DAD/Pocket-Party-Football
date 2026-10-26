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
        public static bool IsHalftime(World w) => On(w) && w.GameClock.BreakTicks > 0;

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
                if (--c.BreakTicks == 0) StartSecondHalf(w);
                c.LastPhase = w.Phase;
                return;
            }

            // the snap starts it; the whistle stops it after the stop results (or always, with runsBetweenPlays off)
            if (w.Phase == Phase.Live && c.LastPhase != Phase.Live) c.Running = true;
            if (w.Phase == Phase.Whistle && c.LastPhase == Phase.Live && (!cfg.runsBetweenPlays || cfg.stopOn.Contains(w.Result))) c.Running = false;
            if (c.Running && c.TicksLeft > 0) c.TicksLeft--;
            // out of time: a live play is finished first
            if (c.TicksLeft == 0 && w.Phase != Phase.Live) EndQuarter(w);
            c.LastPhase = w.Phase;
        }

        static void EndQuarter(World w)
        {
            var c = w.GameClock;
            var cfg = w.Cfg.clock;
            c.Running = false;
            if (c.Quarter >= cfg.quarters) { c.Over = true; Hold(w); return; }
            c.Quarter++;
            c.TicksLeft = Huddle.Ticks(w, cfg.quarterS);
            if (c.Quarter != cfg.halftimeAfterQuarter + 1) return;
            c.BreakTicks = Huddle.Ticks(w, cfg.halftimeS);
            if (c.BreakTicks > 0) Hold(w);
            else StartSecondHalf(w);
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
