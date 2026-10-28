namespace PocketFootball.Sim
{
    // The game's on-field helpers, brought over from the practice lab (drawn by Boot/FlightView.cs; the sim never
    // sees them). Each has its own switch:
    //   FEATURES.flightLine  (NO_FLIGHT_LINE=1):  once a pass is in the air, the soft ribbon of its flight to the grass.
    //   FEATURES.landingRing (NO_LANDING_RING=1): a ring where the pass is aimed, and the catch circle under the ball
    //                                             once it is low enough to catch.
    //   FEATURES.reachRing   (NO_REACH_RING=1):   a red ring under each person on defense, as wide as his TACKLE
    //                                             reaches; bolder while the ball carrier is inside it.
    public static class FlightMarks
    {
        public enum Reach { None, Out, In }

        public static bool LineOn(World w) => w.Cfg.Feature("flightLine");
        public static bool LandingOn(World w) => w.Cfg.Feature("landingRing");
        public static bool ReachOn(World w) => w.Cfg.Feature("reachRing");

        public static bool Flying(World w) => w.Ball.State == BallState.Flying;

        // The ball is coming down and low enough for a hand to reach it.
        public static bool Catchable(World w) => Flying(w) && w.Ball.VY < 0f && w.Ball.Y <= w.Cfg.play.catchMaxHeightYd;

        // The ball's flight from where it is now to the grass, one point a tick, flown the way Ball.Step flies it (so
        // the line is the flight). In yards; y is height. Returns how many points it filled (0: no ball in the air).
        public static int Path(World w, float[] x, float[] y, float[] z)
        {
            var b = w.Ball;
            if (!Flying(w) || x.Length == 0) return 0;
            float dt = w.Dt, g = w.Cfg.ball.gravityYdS2;
            float px = b.X, py = b.Y, pz = b.Z, vy = b.VY;
            int n = 0;
            x[n] = px; y[n] = py; z[n] = pz; n++;
            while (n < x.Length && py > 0f)
            {
                vy -= g * dt;
                px += b.VX * dt; py += vy * dt; pz += b.VZ * dt;
                if (py < 0f) py = 0f;
                x[n] = px; y[n] = py; z[n] = pz; n++;
            }
            return n;
        }

        // The ring under player p: none unless he is a person on defense during a live run with the ball in a hand;
        // In once the carrier is close enough for his TACKLE to reach.
        public static Reach TackleRing(World w, Player p)
        {
            if (!ReachOn(w) || p == null || !p.Human || w.Phase != Phase.Live) return Reach.None;
            var k = LivePlay.Carrier(w);
            if (k == null || k.Team == p.Team) return Reach.None;
            return LivePlay.Dist(p, k) <= LivePlay.TackleReach(w, p) ? Reach.In : Reach.Out;
        }
    }
}
