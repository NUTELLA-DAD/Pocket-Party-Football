using System;

namespace PocketFootball.Sim
{
    // Bot v0: holds its formation spot (walks back when bumped off it). With World.BotsWander on (the baseline,
    // the B key) it picks a random spot nearby every so often and walks there instead.
    // The real per-position brains come in M1-d.
    public static class Bots
    {
        public static PlayerInput Think(World w, Player p)
        {
            if (w.Phase == Phase.Live && LivePlay.On(w)) return Live(w, p);
            var b = w.Cfg.bots;
            var f = w.Cfg.field;
            if (!w.BotsWander) { p.GoalX = p.HomeX; p.GoalZ = p.HomeZ; }
            else if (w.Tick >= p.NextThinkTick)
            {
                p.GoalX = Clamp(p.X + w.Rng.Range(-b.wanderYd, b.wanderYd), -f.HalfLength, f.HalfLength);
                p.GoalZ = Clamp(p.Z + w.Rng.Range(-b.wanderYd, b.wanderYd), -f.HalfWidth, f.HalfWidth);
                p.NextThinkTick = w.Tick + b.thinkEveryTicks + w.Rng.Below(b.thinkJitterTicks + 1);
            }
            float dx = p.GoalX - p.X, dz = p.GoalZ - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= b.arriveYd) return default;
            return new PlayerInput { MoveX = dx / d * b.speedFrac, MoveZ = dz / d * b.speedFrac };
        }

        // A live play, bot v0.5 (M1-d makes these smart): the ball carrier runs for the end zone; the other offense
        // players run their route or hold their block; the defense rushes, covers its man or zone, and chases the ball
        // (the carrier, or where a pass is coming down) once it has left the QB.
        static PlayerInput Live(World w, Player p)
        {
            float dir = w.Cfg.formation.attackDir;
            var carrier = LivePlay.Carrier(w);
            var qb = LivePlay.Qb(w);
            var job = JobOfCalled(w, p);
            if (carrier == p && (p != qb || w.Thrown || HasRunner(w)))
                return To(p, p.X + dir * (p.Team == w.Offense ? 1f : -1f) * w.Cfg.field.lineEveryYd, p.Z, 1f);
            if (p.Team == w.Offense)
            {
                if (job == null || job.pts.Count == 0) return default;
                if (job.kind == "route")
                {
                    while (p.RouteIndex < job.pts.Count - 1 && Near(p, Pt(w, job.pts[p.RouteIndex]), w.Cfg.bots.arriveYd)) p.RouteIndex++;
                    var t = Pt(w, job.pts[p.RouteIndex]);
                    if (p.RouteIndex == job.pts.Count - 1 && Near(p, t, w.Cfg.bots.arriveYd)) return default;
                    return To(p, t[0], t[1], 1f);
                }
                var at = Pt(w, job.pts[0]);
                return To(p, at[0], at[1], 1f);
            }
            // defense
            bool ballOut = w.Ball.State == BallState.Flying || (carrier != null && carrier != qb);
            if (w.Ball.State == BallState.Flying) return To(p, w.Ball.TargetX, w.Ball.TargetZ, 1f);
            if (ballOut && carrier != null)
            {
                float lead = w.Cfg.play.pursuitLeadS;
                return To(p, carrier.X + carrier.VX * lead, carrier.Z + carrier.VZ * lead, 1f);
            }
            if (job == null) return default;
            if (job.kind == "rush" && qb != null) return To(p, qb.X, qb.Z, 1f);
            if (job.kind == "man")
            {
                var m = LivePlay.SpotOf(w, w.Offense, job.target);
                if (m != null) return To(p, m.X + dir, m.Z, 1f);
            }
            if (job.kind == "zone" && job.pts.Count > 0) { var z = Pt(w, job.pts[0]); return To(p, z[0], z[1], 1f); }
            return default;
        }

        static bool HasRunner(World w) { var pl = LivePlay.Called(w, w.Offense); return pl != null && pl.runner >= 0; }

        static JobConfig JobOfCalled(World w, Player p)
        {
            var play = LivePlay.Called(w, p.Team);
            return play != null && p.Spot >= 0 && p.Spot < play.jobs.Count ? play.jobs[p.Spot] : null;
        }

        // a play point [ahead, z] in field yards
        static float[] Pt(World w, float[] pt) => new[] { w.LineOfScrimmageX + w.Cfg.formation.attackDir * pt[0], pt[1] };

        static bool Near(Player p, float[] t, float r) { float dx = t[0] - p.X, dz = t[1] - p.Z; return dx * dx + dz * dz <= r * r; }

        static PlayerInput To(Player p, float x, float z, float speed)
        {
            float dx = x - p.X, dz = z - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0f) return default;
            return new PlayerInput { MoveX = dx / d * speed, MoveZ = dz / d * speed };
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
