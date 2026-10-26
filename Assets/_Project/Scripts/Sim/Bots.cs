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
            if (Fumbles.Loose(w)) return Fumbles.Chase(w, p);   // WS-04: everyone scrambles for a fumble
            var kick = Kicking.BotThink(w, p); if (kick.HasValue) return kick.Value;   // WS-01: kick coverage and returns
            var b = w.Cfg.bots;
            float dir = w.Dir;
            var carrier = LivePlay.Carrier(w);
            var qb = LivePlay.Qb(w);
            var job = JobOfCalled(w, p);
            bool ballOut = w.Ball.State == BallState.Flying || w.Thrown || (carrier != null && carrier != qb);
            if (V2(w) && carrier != null && carrier == qb && Ahead(w, qb) > 0f) ballOut = true;   // WS-06: the QB took off, block and pursue

            // an interception return: his teammates block for him, the old offense chases him down
            if (carrier != null && carrier.Team != w.Offense && carrier != p)
                return p.Team == carrier.Team ? Block(w, p, carrier) : Pursue(w, p, carrier);
            if (carrier == p)
            {
                if (p == qb && !ballOut && !HasRunner(w) && V2(w)) return QbMoveV2(w, p, job);   // WS-06: drops, rolls out, scrambles
                if (p == qb && !ballOut && !HasRunner(w)) return QbMove(w, p, job);   // in the pocket
                if (p == qb && HasRunner(w)) return QbMove(w, p, job);                 // meet the runner for the handoff
                return RunWithBall(w, p);
            }
            if (p.Team == w.Offense)
            {
                // WS-06: the man the pass is for keeps running and goes to meet it (v1 stopped and blocked, so led passes fell short)
                if (V2(w) && w.Ball.State == BallState.Flying && p == Intended(w)) return MeetThePass(w, p);
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
            if (w.Ball.State == BallState.Flying && V2(w)) return DefendThePass(w, p);   // WS-06
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

        // ---- bots v2 (WS-06), behind FEATURES.botsV2 (NO_BOTS_V2=1: the v1 QB and receivers) ----
        // The QB runs his whole route (a drop, a bootleg roll-out; a route ending past the line is a designed run, the
        // sneak), reads as in v1, and scrambles: once his read deadline passes, or a free rusher gets within pressureYd
        // after escapeAfterS (before that he is still setting his feet: a quick shed is a sack), he tucks it and runs
        // (RunWithBall), still throwing to anyone who comes open while he is behind the line.
        // At the deadline or under pressure he throws to the most open man only if that man has deadlineOpenYd of space.
        // His throws miss by up to missYd + missPerYd per yard of throw (+ pressureMissYd under pressure), seeded, and
        // a throw longer than lobOverYd is a lob. The man a pass is for runs to where it is coming down.

        public static bool V2(World w) => w.Cfg.Feature("botsV2");

        static PlayerInput QbMoveV2(World w, Player p, JobConfig job)
        {
            if (DesignedRun(w, job))
            {
                // the sneak: follow the route to its end, then run like any carrier
                if (Ahead(w, p) > 0f || job.pts.Count == 0) return RunWithBall(w, p);
                return FollowRoute(w, p, job, true) ?? RunWithBall(w, p);
            }
            if (Scrambling(w, p)) return RunWithBall(w, p);
            if (job == null || job.pts.Count == 0) return default;
            return FollowRoute(w, p, job, false) ?? default;
        }

        // Route points in order; null at the end of the route.
        static PlayerInput? FollowRoute(World w, Player p, JobConfig job, bool sprint)
        {
            var b = w.Cfg.bots;
            while (p.RouteIndex < job.pts.Count - 1 && Near(p, Pt(w, job.pts[p.RouteIndex]), b.arriveYd)) p.RouteIndex++;
            var t = Pt(w, job.pts[p.RouteIndex]);
            if (p.RouteIndex == job.pts.Count - 1 && Near(p, t, b.arriveYd)) return null;
            return To(p, t[0], t[1], 1f, sprint || p.RouteIndex > 0);
        }

        // A QB route that ends past the line of scrimmage: he keeps it and runs.
        static bool DesignedRun(World w, JobConfig job) => job != null && job.kind == "route" && job.pts.Count > 0 && job.pts[job.pts.Count - 1][0] > 0f;

        static float Ahead(World w, Player p) => (p.X - w.LineOfScrimmageX) * w.Dir;

        static float Seconds(World w) => w.PlayTicks / (float)w.Cfg.sim.tickHz;

        public static bool Scrambling(World w, Player qb) => V2(w) && (Seconds(w) >= w.Cfg.bots.readDeadlineS || (Seconds(w) >= w.Cfg.bots.escapeAfterS && Pressured(w, qb)));

        // A defender in on the QB with no blocker on him.
        public static bool Pressured(World w, Player qb)
        {
            foreach (var d in w.Players)
                if (d.Team != qb.Team && d.BlockedTicks == 0 && LivePlay.Dist(d, qb) <= w.Cfg.bots.pressureYd) return true;
            return false;
        }

        // The man a pass is for keeps going the way he was (that is where the QB led him); in the last adjustS before it
        // comes down he can see where it will land and goes for that spot. So a throw that misses by more than he can
        // make up in adjustS falls incomplete.
        static PlayerInput MeetThePass(World w, Player p)
        {
            var bl = w.Ball;
            if (Landing(w) <= w.Cfg.bots.adjustS) return To(p, bl.TargetX, bl.TargetZ, 1f, true);
            float sp = (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
            if (sp <= 0f) return default;
            return new PlayerInput { MoveX = p.VX / sp, MoveZ = p.VZ / sp, Sprint = true };
        }

        // A defender with a pass in the air reads it like the receiver does: only in the last adjustS can he tell where
        // it comes down, and then he goes for it if he can get there in time. Until then, and if he can't, he closes on
        // the man it is for, to make the tackle (v1 sent everyone to the landing spot from the throw, so passes drew a
        // crowd that beat the receiver there and picked off one throw in six).
        static PlayerInput DefendThePass(World w, Player p)
        {
            var bl = w.Ball;
            float land = Landing(w);
            float reach = land * w.Cfg.players.sprintSpeedYdS + w.Cfg.play.catchRadiusYd;
            float dx = bl.TargetX - p.X, dz = bl.TargetZ - p.Z;
            if (land <= w.Cfg.bots.adjustS && dx * dx + dz * dz <= reach * reach) return To(p, bl.TargetX, bl.TargetZ, 1f, true);
            var r = Intended(w);
            return r != null ? Pursue(w, p, r) : To(p, bl.TargetX, bl.TargetZ, 1f, true);
        }

        // Seconds until a pass in the air is down at catching height: y + vy t - g t^2 / 2 = catchHeight.
        static float Landing(World w)
        {
            var bl = w.Ball;
            var bc = w.Cfg.ball;
            float g = bc.gravityYdS2, drop = bl.Y - bc.catchHeightYd;
            return (bl.VY + (float)Math.Sqrt(Math.Max(0f, bl.VY * bl.VY + (g + g) * drop))) / g;
        }

        // The receiver a pass in the air is meant for: the offense player nearest where it comes down (the QB aside).
        public static Player Intended(World w)
        {
            if (w.Ball.State != BallState.Flying) return null;
            var qb = LivePlay.Qb(w);
            Player best = null; float bd = float.MaxValue;
            foreach (var o in w.Players)
            {
                if (o.Team != w.Offense || o == qb) continue;
                float dx = o.X - w.Ball.TargetX, dz = o.Z - w.Ball.TargetZ, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        // The v2 read. Throws (itself, with the miss) and returns null; LivePlay's own throw is never reached.
        static Player QbReadV2(World w, Player qb, PlayConfig play)
        {
            var b = w.Cfg.bots;
            if (Ahead(w, qb) > 0f) return null;                                   // past the line: no forward pass
            if (play != null && qb.Spot >= 0 && qb.Spot < play.jobs.Count && DesignedRun(w, play.jobs[qb.Spot])) return null;
            float t = Seconds(w);
            bool pressured = Pressured(w, qb);
            Player target = null;
            if (t >= b.readDeadlineS)
            {
                // scrambling: anyone open, else keep running
                var best = MostOpen(w, out float o);
                if (best != null && o >= b.openYd) target = best;
            }
            else
            {
                int read = 0;
                for (int k = 1; k <= w.Cfg.play.receivers.Count && read < b.readTimesS.Count && target == null; k++)
                {
                    var r = LivePlay.Receiver(w, k);
                    var job = r != null ? JobOfCalled(w, r) : null;
                    if (r == null || job == null || job.kind != "route") continue;
                    if (t >= b.readTimesS[read] && Openness(w, r) >= b.openYd && LaneClear(w, qb, r)) target = r;
                    read++;
                }
                if (target == null && pressured && b.readTimesS.Count > 0 && t >= b.readTimesS[0])
                {
                    var best = MostOpen(w, out float o);
                    if (best != null && o >= b.deadlineOpenYd) target = best;
                }
                // the last moment in the pocket: the most open man if he has room, else a scramble
                if (target == null && t + w.Dt >= b.readDeadlineS)
                {
                    var best = MostOpen(w, out float o);
                    if (best != null && o >= b.deadlineOpenYd) target = best;
                }
            }
            if (target != null) Throw(w, qb, target, pressured);
            return null;
        }

        static Player MostOpen(World w, out float open)
        {
            Player best = null; open = -1f;
            for (int k = 1; k <= w.Cfg.play.receivers.Count; k++)
            {
                var r = LivePlay.Receiver(w, k);
                if (r == null || r == LivePlay.Qb(w) || !LaneClear(w, LivePlay.Qb(w), r)) continue;
                float o = Openness(w, r);
                if (o > open) { open = o; best = r; }
            }
            return best;
        }

        // No defender within laneYd of the back half of the throw (where a bullet is low enough to be picked off).
        static bool LaneClear(World w, Player qb, Player r)
        {
            float ax = (qb.X + r.X) * 0.5f, az = (qb.Z + r.Z) * 0.5f;
            float sx = r.X - ax, sz = r.Z - az, len2 = sx * sx + sz * sz;
            float lane = w.Cfg.bots.laneYd;
            foreach (var d in w.Players)
            {
                if (d.Team == qb.Team) continue;
                float u = len2 > 0f ? ((d.X - ax) * sx + (d.Z - az) * sz) / len2 : 0f;
                if (u < 0f || u > 1f) continue;
                float px = ax + sx * u - d.X, pz = az + sz * u - d.Z;
                if (px * px + pz * pz < lane * lane) return false;
            }
            return true;
        }

        // Lead him by the flight time, then miss by a seeded amount.
        static void Throw(World w, Player qb, Player r, bool pressured)
        {
            var b = w.Cfg.bots;
            var bc = w.Cfg.ball;
            float dx = r.X - qb.X, dz = r.Z - qb.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            bool lob = d > b.lobOverYd;
            float vy = (float)Math.Sqrt((bc.gravityYdS2 + bc.gravityYdS2) * bc.lobApexYd);
            float ft = lob ? (vy + vy) / bc.gravityYdS2 : d / bc.bulletSpeedYdS;
            float miss = b.missYd + d * b.missPerYd + (pressured ? b.pressureMissYd : 0f);
            float tx = r.X + r.VX * ft + w.Rng.Range(-miss, miss);
            float tz = r.Z + r.VZ * ft + w.Rng.Range(-miss, miss);
            if (w.Throw(tx, tz, lob)) w.Thrown = true;
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
            if (V2(w) && SteppingOut(w, p)) return Norm(dir * w.Cfg.bots.stepOutAheadFrac, p.Z >= 0f ? 1f : -1f, true);   // WS-06
            return Norm(dir, slide, true);
        }

        // Bots v2: a carrier within stepOutYd of a sideline with a tackler closing from the infield side (within
        // stepOutHitYd) steps out of bounds rather than take the hit.
        static bool SteppingOut(World w, Player p)
        {
            var b = w.Cfg.bots;
            float az = Math.Abs(p.Z);
            if (az < w.Cfg.field.HalfWidth - b.stepOutYd) return false;
            foreach (var d in w.Players)
                if (d.Team != p.Team && Math.Abs(d.Z) < az && LivePlay.Dist(p, d) <= b.stepOutHitYd) return true;
            return false;
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
            if (V2(w)) return QbReadV2(w, qb, play);   // WS-06
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
            var hot = Audibles.HotJob(w, p); if (hot != null) return hot;   // WS-13: a hot route replaces the called job
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
