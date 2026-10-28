using System;

namespace PocketFootball.Sim
{
    // Blocking (M1-d). A defender with a blocker in reach, the blocker standing between him and the ball, is held up:
    // his speed drops to blockedSpeedFrac. After shedAfterS of being blocked he sheds it and runs free for shedFreeS
    // (no block holds him then), so a pass rush always gets home in the end. Blockers: offense bots whose job is to
    // block (and every offense bot once the ball has left the QB), and people holding BLOCK. FEATURES.ratings: a bigger
    // blocker holds his man longer.
    public static class Blocking
    {
        public static void Step(World w)
        {
            if (!LivePlay.On(w) || w.Phase != Phase.Live) return;
            var b = w.Cfg.bots;
            float dt = w.Dt;
            var ball = LivePlay.Carrier(w);
            float bx = ball != null ? ball.X : w.Ball.X, bz = ball != null ? ball.Z : w.Ball.Z;
            // bots v3: under a kick in the air the receiving team blocks for the man who will field it
            int blockers = ball != null ? ball.Team : BotsV3.On(w) && Kicking.ReturnerNow(w) != null ? w.Defense : w.Offense;
            foreach (var d in w.Players)
            {
                if (d.Team == blockers || d == ball) continue;
                if (d.FreeTicks > 0) { d.FreeTicks--; continue; }
                Player held = null;
                foreach (var o in w.Players)
                {
                    if (o.Team != blockers || !o.Blocking || o == ball) continue;
                    float dx = d.X - o.X, dz = d.Z - o.Z;
                    if (dx * dx + dz * dz > b.blockReachYd * b.blockReachYd) continue;
                    // between: the blocker is on the ball's side of the defender
                    if ((o.X - d.X) * (bx - d.X) + (o.Z - d.Z) * (bz - d.Z) <= 0f) continue;
                    held = o;
                    break;
                }
                if (held == null) { d.BlockedTicks = 0; continue; }
                d.BlockedTicks++;
                if (d.BlockedTicks >= b.shedAfterS * Ratings.ShedFrac(w, held, d) * w.Cfg.sim.tickHz)   // FEATURES.ratings: size
                {
                    d.BlockedTicks = 0;
                    d.FreeTicks = (int)(b.shedFreeS * w.Cfg.sim.tickHz);
                    continue;
                }
                // held up: give back most of this tick's movement
                float keep = b.blockedSpeedFrac;
                d.X -= d.VX * dt * (1f - keep);
                d.Z -= d.VZ * dt * (1f - keep);
                d.VX *= keep; d.VZ *= keep;
            }
        }
    }
}
