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

        // A live play, bots v1 (M1-d). Offense: the ball carrier runs for the end zone, round the nearest tackler;
        // blockers pick up the nearest rusher and stay between him and the ball; receivers run their routes, then
        // block for whoever has the ball. Defense: rushers go for the QB round their blocker; man coverage shadows its
        // man a step downfield; zone defenders hold their zone and take whoever runs into it; once the ball has left
        // the QB everyone takes a pursuit angle on the carrier, or runs to where a pass is coming down.
        static PlayerInput Live(World w, Player p)
        {
            var kick = Kicking.BotThink(w, p); if (kick.HasValue) return kick.Value;   // WS-01: kick coverage and returns
            var b = w.Cfg.bots;
            float dir = w.Dir;
            var carrier = LivePlay.Carrier(w);
            var qb = LivePlay.Qb(w);
            var job = JobOfCalled(w, p);
            bool ballOut = w.Ball.State == BallState.Flying || w.Thrown || (carrier != null && carrier != qb);

            // an interception return: his teammates block for him, the old offense chases him down
            if (carrier != null && carrier.Team != w.Offense && carrier != p)
                return p.Team == carrier.Team ? Block(w, p, carrier) : Pursue(w, p, carrier);
            if (carrier == p)
            {
                if (p == qb && !ballOut && !HasRunner(w)) return QbMove(w, p, job);   // in the pocket
                if (p == qb && HasRunner(w)) return QbMove(w, p, job);                 // meet the runner for the handoff
                return RunWithBall(w, p);
            }
            if (p.Team == w.Offense)
            {
                if (ballOut || job == null || job.kind == "block") return Block(w, p, carrier);
                if (job.kind == "route" && job.pts.Count > 0)
                {
                    while (p.RouteIndex < job.pts.Count - 1 && Near(p, Pt(w, job.pts[p.RouteIndex]), b.arriveYd)) p.RouteIndex++;
                    var t = Pt(w, job.pts[p.RouteIndex]);
                    if (p.RouteIndex == job.pts.Count - 1 && Near(p, t, b.arriveYd)) return default;
                    return To(p, t[0], t[1], 1f, p.RouteIndex > 0);
                }
                return default;
            }
            // defense
            if (w.Ball.State == BallState.Flying) return To(p, w.Ball.TargetX, w.Ball.TargetZ, 1f, true);
            if (ballOut && carrier != null) return Pursue(w, p, carrier);
            if (job == null) return default;
            if (job.kind == "rush" && qb != null) return Rush(w, p, qb);
            if (job.kind == "man")
            {
                var m = LivePlay.SpotOf(w, w.Offense, job.target);
                if (m != null) return To(p, m.X + dir, m.Z, 1f, true);
            }
            if (job.kind == "zone" && job.pts.Count > 0)
            {
                var z = Pt(w, job.pts[0]);
                float r = job.radius > 0f ? job.radius : b.openYd;
                // take whoever runs into the zone
                Player mark = null; float md = float.MaxValue;
                foreach (var o in w.Players)
                {
                    if (o.Team != w.Offense || o == qb) continue;
                    float dx = o.X - z[0], dz = o.Z - z[1], d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d < r + b.openYd && d < md) { md = d; mark = o; }
                }
                if (mark != null) return To(p, mark.X + dir, mark.Z, 1f, false);
                return To(p, z[0], z[1], 1f, false);
            }
            return default;
        }

        // The QB: drop to his set point and hold it (the throw comes from QbRead, in LivePlay).
        static PlayerInput QbMove(World w, Player p, JobConfig job)
        {
            if (job == null || job.pts.Count == 0) return default;
            var t = Pt(w, job.pts[0]);
            if (Near(p, t, w.Cfg.bots.arriveYd)) return default;
            return To(p, t[0], t[1], 1f, false);
        }

        // With the ball: for the end zone, sliding away from the nearest defender in front.
        static PlayerInput RunWithBall(World w, Player p)
        {
            float dir = p.Team == w.Offense ? w.Dir : -w.Dir;
            float side = 0f, nearest = float.MaxValue;
            foreach (var d in w.Players)
            {
                if (d.Team == p.Team) continue;
                if ((d.X - p.X) * dir < 0f) continue;
                float dist = LivePlay.Dist(p, d);
                if (dist < nearest) { nearest = dist; side = p.Z >= d.Z ? 1f : -1f; }
            }
            float slide = nearest < w.Cfg.bots.wanderYd ? side * (1f - nearest / w.Cfg.bots.wanderYd) : 0f;
            return Norm(dir, slide, true);
        }

        // A blocker: the defender nearest the ball; stand between them, a step off him.
        static PlayerInput Block(World w, Player p, Player carrier)
        {
            float bx = carrier != null ? carrier.X : w.Ball.X, bz = carrier != null ? carrier.Z : w.Ball.Z;
            Player best = null; float bd = float.MaxValue;
            foreach (var d in w.Players)
            {
                if (d.Team == p.Team) continue;
                float toBall = (float)Math.Sqrt((d.X - bx) * (d.X - bx) + (d.Z - bz) * (d.Z - bz));
                float score = LivePlay.Dist(p, d) + toBall * 0.5f;
                if (score < bd) { bd = score; best = d; }
            }
            if (best == null) return default;
            float dx = bx - best.X, dz = bz - best.Z, dl = (float)Math.Sqrt(dx * dx + dz * dz);
            float tx = best.X, tz = best.Z;
            if (dl > 0f) { tx += dx / dl * w.Cfg.bots.arriveYd; tz += dz / dl * w.Cfg.bots.arriveYd; }
            var i = To(p, tx, tz, 1f, false);
            i.Block = true;
            return i;
        }

        // A pass rusher: for the QB, round the blocker in his way.
        static PlayerInput Rush(World w, Player p, Player qb)
        {
            float tx = qb.X, tz = qb.Z;
            foreach (var o in w.Players)
            {
                if (o.Team == p.Team || !o.Blocking) continue;
                if (LivePlay.Dist(p, o) > w.Cfg.bots.blockReachYd * (1f + 0.5f)) continue;
                tz += (p.Z >= o.Z ? 1f : -1f) * w.Cfg.bots.rushAroundYd;
                break;
            }
            return To(p, tx, tz, 1f, p.FreeTicks > 0);
        }

        // A pursuit angle: run at where the carrier will be when we get there.
        static PlayerInput Pursue(World w, Player p, Player carrier)
        {
            float t = LivePlay.Dist(p, carrier) / Math.Max(w.Cfg.players.sprintSpeedYdS, w.Cfg.players.jogSpeedYdS);
            return To(p, carrier.X + carrier.VX * t, carrier.Z + carrier.VZ * t, 1f, true);
        }

        // The QB reads: receiver k (the routes, in shape order) is read at readTimesS[k]; he throws to the first one
        // open (no defender within openYd); at readDeadlineS he throws to the most open. Null: keep holding it.
        public static Player QbRead(World w, Player qb, PlayConfig play)
        {
            var b = w.Cfg.bots;
            float t = w.PlayTicks / (float)w.Cfg.sim.tickHz;
            int read = 0;
            for (int k = 1; k <= w.Cfg.play.receivers.Count && read < b.readTimesS.Count; k++)
            {
                var r = LivePlay.Receiver(w, k);
                var job = r != null ? JobOfCalled(w, r) : null;
                if (r == null || job == null || job.kind != "route") continue;
                if (t >= b.readTimesS[read] && Openness(w, r) >= b.openYd) return r;
                read++;
            }
            if (t < b.readDeadlineS) return null;
            Player best = null; float bo = -1f;
            for (int k = 1; k <= w.Cfg.play.receivers.Count; k++)
            {
                var r = LivePlay.Receiver(w, k);
                if (r == null) continue;
                float o = Openness(w, r);
                if (o > bo) { bo = o; best = r; }
            }
            return best;
        }

        static float Openness(World w, Player r)
        {
            float open = float.MaxValue;
            foreach (var d in w.Players) if (d.Team != w.Offense) open = Math.Min(open, LivePlay.Dist(d, r));
            return open;
        }

        static bool HasRunner(World w) { var pl = LivePlay.Called(w, w.Offense); return pl != null && pl.runner >= 0; }

        static JobConfig JobOfCalled(World w, Player p)
        {
            var play = LivePlay.Called(w, p.Team);
            return play != null && p.Spot >= 0 && p.Spot < play.jobs.Count ? play.jobs[p.Spot] : null;
        }

        // a play point [ahead, z] in field yards
        static float[] Pt(World w, float[] pt) => new[] { w.LineOfScrimmageX + w.Dir * pt[0], pt[1] };

        static bool Near(Player p, float[] t, float r) { float dx = t[0] - p.X, dz = t[1] - p.Z; return dx * dx + dz * dz <= r * r; }

        static PlayerInput To(Player p, float x, float z, float speed, bool sprint = false)
        {
            float dx = x - p.X, dz = z - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0f) return default;
            return new PlayerInput { MoveX = dx / d * speed, MoveZ = dz / d * speed, Sprint = sprint };
        }

        static PlayerInput Norm(float x, float z, bool sprint)
        {
            float d = (float)Math.Sqrt(x * x + z * z);
            return d <= 0f ? default : new PlayerInput { MoveX = x / d, MoveZ = z / d, Sprint = sprint };
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
