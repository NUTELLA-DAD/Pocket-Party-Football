namespace PocketFootball.Sim
{
    // Downs and the score (M1-e), behind FEATURES.downs (NO_DOWNS=1: every play simply restarts at the spot).
    // Gain the line to gain within four downs for a new first down (the line moves 10 yards on; inside the 10 it is
    // the goal line: "& GOAL"). Four downs without it: the other team takes over at the spot. An interception: the
    // other team takes over where it was caught. A touchdown: 6 points, then the other team starts at its own 25.
    // A change of possession happens at the next huddle (Huddle.Step), so the result shows first.
    public static class Downs
    {
        public static bool On(World w) => w.Cfg.Feature("downs");

        public static void Apply(World w, string result, float spotX)
        {
            if (!On(w)) return;
            var d = w.Cfg.downs;
            float goal = w.Cfg.field.GoalLineX;
            float dir = w.Dir;
            w.Note = "";
            if (result == "TOUCHDOWN")
            {
                w.Score[w.Offense] += d.touchdownPoints;
                // the other team starts at its own 25: its own goal line is the one we just scored on
                TurnOver(w, dir * (goal - d.startOwnYd));
                return;
            }
            if (result == "INTERCEPTED") { TurnOver(w, w.NextLos); w.Note = "TURNOVER"; return; }
            if (result == "PICK SIX")
            {
                // 6 to the defense; the team that threw it gets it back at its own 25, still going the same way
                w.Score[w.Defense] += d.touchdownPoints;
                w.NextLos = -dir * (goal - d.startOwnYd);
                w.Down = 1;
                w.FirstDownX = LineToGain(w, w.NextLos, dir);
                return;
            }
            float spot = w.NextLos;
            if ((spot - w.FirstDownX) * dir >= 0f)
            {
                w.Down = 1;
                w.FirstDownX = LineToGain(w, spot, dir);
                w.Note = "FIRST DOWN";
                return;
            }
            w.Down++;
            if (w.Down > d.downsToGain) { TurnOver(w, spot); w.Note = "TURNOVER ON DOWNS"; }
        }

        // The other team gets the ball at x (applied at the next huddle): first and ten going the other way.
        static void TurnOver(World w, float x)
        {
            w.PendingTurnover = true;
            w.NextLos = x;
            w.Down = 1;
            w.FirstDownX = LineToGain(w, x, -w.Dir);
        }

        static float LineToGain(World w, float spot, float dir)
        {
            float goal = w.Cfg.field.GoalLineX;
            float x = spot + dir * w.Cfg.downs.firstDownYd;
            return x * dir > goal ? goal * dir : x;
        }

        // "2ND & 6", "1ST & GOAL" (for the scoreboard and the phones).
        static readonly string[] Names = { "1ST", "2ND", "3RD", "4TH" };

        // (after the whistle it already describes the next play: its spot and, after a change of possession, its way)
        public static string DownText(World w)
        {
            string n = Names[System.Math.Max(0, System.Math.Min(Names.Length - 1, w.Down - 1))];
            float dir = w.PendingTurnover ? -w.Dir : w.Dir;
            float los = w.Phase == Phase.Whistle ? w.NextLos : w.LineOfScrimmageX;
            float goal = w.Cfg.field.GoalLineX;
            if (w.FirstDownX * dir >= goal) return n + " & GOAL";
            int togo = (int)System.Math.Ceiling((w.FirstDownX - los) * dir);
            return n + " & " + (togo < 1 ? 1 : togo);
        }
    }
}
