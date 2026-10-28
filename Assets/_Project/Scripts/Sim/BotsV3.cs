using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Bots v3 (FEATURES.botsV3, NO_BOTS_V3=1): bots that react like people and block like a team.
    //
    // 1. Reactions. v2 bots all saw a throw, a catch, a handoff or a kick coming down on the very tick it happened and
    //    turned together. Now a bot takes in a change (what the ball is doing, who has it) only after a reaction time:
    //    reactMinS close to the ball up to reactMaxS reactFarYd away, quicker for a quick man (each bot draws a quickness
    //    once, seeded), plus a seeded jitter. Until then he keeps doing what he was doing. The man with the ball, the QB,
    //    the man a pass is for and the kick returner are watching the ball and react at once. The offense goes on the
    //    snap; the defense reacts to it (the line at once, the safeties last).
    // 2. Blocking. v2 blockers each ran at the defender nearest the ball, a step short of him, at a jog: behind a sprinting
    //    coverage man that was a chase that never caught up. Now the team with the ball shares one plan a tick: the
    //    threats (opponents closing on the ball, soonest to arrive first) are handed out one each to the blocker who can
    //    meet each soonest (a blocker keeps the man he has unless someone is clearly better placed). A blocker runs to the
    //    point on his man's line to the ball that he reaches first (never closer than shieldYd to the ball, never more
    //    than meetMaxYd up the field), sprinting when he has ground to make up. Blockers with nobody to take form a
    //    convoy ahead of the ball carrier (convoyAheadYd, convoyGapYd apart); in pass protection they hold their spot.
    // 3. Kicks. The kicking team covers in lanes (laneGapYd apart across the field, by line-up order) and only closes on
    //    the ball inside laneUntilYd of it. The receiving team sets up the return while the ball is in the air: the same
    //    plan protects the landing spot, and the blockers with nobody to take build a wall wallAheadYd in front of it.
    public static class BotsV3
    {
        public static bool On(World w) => w.Cfg.Feature("botsV3");

        // ---- reactions ----

        // What a bot reacts to: what the ball is doing and whether a kick is down or being run back (plus who has it, kept apart).
        public static int Situation(World w)
        {
            int s = (int)w.Ball.State;
            s += s + (w.Thrown ? 1 : 0);
            s += s + (w.Intercepted ? 1 : 0);
            s += s + (w.Kick.Landed ? 1 : 0);
            s += s + (w.Kick.Returned ? 1 : 0);
            return s;
        }

        // At the hike: the offense knows the count; the defense has to see the snap.
        public static void AtTheSnap(World w, Player p)
        {
            p.ReactTick = -1;
            p.HeldX = p.HeldZ = 0f; p.HeldSprint = p.HeldBlock = false;
            p.BlockSlot = -1;
            bool knows = p.Team == w.Offense;
            p.SeenSituation = knows ? Situation(w) : -1;
            p.SeenCarrier = knows ? w.Ball.Carrier : -1;
        }

        // True: this bot has not taken in the latest change yet; `held` is what he keeps doing meanwhile.
        public static bool Delayed(World w, Player p, out PlayerInput held)
        {
            held = default;
            int sit = Situation(w), carrier = w.Ball.Carrier;
            if (p.SeenSituation == sit && p.SeenCarrier == carrier) return false;
            if (p.ReactTick < 0) p.ReactTick = w.Tick + (int)Math.Round(ReactionS(w, p) * w.Cfg.sim.tickHz);
            if (w.Tick < p.ReactTick)
            {
                held = new PlayerInput { MoveX = p.HeldX, MoveZ = p.HeldZ, Sprint = p.HeldSprint, Block = p.HeldBlock };
                return true;
            }
            p.ReactTick = -1;
            p.SeenSituation = sit;
            p.SeenCarrier = carrier;
            return false;
        }

        // What he decided this tick, kept for while his next reaction takes.
        public static void Remember(Player p, PlayerInput i)
        {
            p.HeldX = i.MoveX; p.HeldZ = i.MoveZ; p.HeldSprint = i.Sprint; p.HeldBlock = i.Block;
        }

        static float ReactionS(World w, Player p)
        {
            var c = w.Cfg.botsV3;
            if (w.Ball.Carrier == p.Slot) return 0f;
            if (p.Team == w.Offense && p.Spot == 0) return 0f;             // the QB (and the kicker) made it happen
            if (Bots.Intended(w) == p) return 0f;                            // the man the pass is for
            if (Kicking.ReturnerNow(w) == p) return 0f;                      // the returner under a kick
            var meant = Bots.Intended(w);                                    // a defender on the man a pass is for is watching him
            if (meant != null && p.Team != w.Offense && LivePlay.Dist(p, meant) <= c.coverNearYd) return 0f;
            float dx = w.Ball.X - p.X, dz = w.Ball.Z - p.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= c.reactNearYd) return 0f;
            float far = c.reactFarYd > 0f ? Math.Min(1f, d / c.reactFarYd) : 1f;
            float s = c.reactMinS + (c.reactMaxS - c.reactMinS) * far;
            if (w.Ball.State == BallState.Flying && p.Team != w.Offense) s = Math.Min(s, c.reactThrowMaxS);   // the defense is watching the QB
            s *= 1f - p.Quick * c.reactQuickFrac;
            return s + w.Rng.Range(0f, c.reactJitterS);
        }

        // A pursuit lead a little long or short by the man (Bots.Pursue).
        public static float PursuitLead(World w, Player p, float t) => On(w) ? t * (1f + (p.Quick - 0.5f) * w.Cfg.botsV3.pursuitLeadVar) : t;

        // ---- blocking ----

        // This tick's plan: one team's blockers, each with the opponent he takes (not hashed: rebuilt every tick from the state).
        public sealed class Plan
        {
            public int Tick = -1, Team = -1;
            public readonly SortedDictionary<int, int> Assigned = new SortedDictionary<int, int>();   // blocker slot -> opponent slot
            public readonly List<Player> Free = new List<Player>();                                   // blockers with nobody to take, by slot
            readonly List<Player> blockers = new List<Player>(), threats = new List<Player>();
            readonly List<float> arrive = new List<float>();

            internal void Build(World w, Player asker, int team, float bx, float bz, Player skip)
            {
                var c = w.Cfg.botsV3;
                Tick = w.Tick; Team = team;
                Assigned.Clear(); Free.Clear(); blockers.Clear(); threats.Clear(); arrive.Clear();
                int carrier = w.Ball.Carrier;
                foreach (var q in w.Players)
                {
                    if (q.Team != team || q.Slot == carrier || q == skip) continue;
                    if (q.Human && !q.BotHelp && q != asker) continue;
                    if (q.Blocking || q == asker) blockers.Add(q);
                }
                // the threats: opponents closing on the ball (or already on it), soonest to arrive first
                foreach (var d in w.Players)
                {
                    if (d.Team == team) continue;
                    float lx = bx - d.X, lz = bz - d.Z, l = (float)Math.Sqrt(lx * lx + lz * lz);
                    if (l > c.threatRangeYd) continue;
                    float closing = l > 0f ? (lx * d.VX + lz * d.VZ) / l : 0f;
                    if (closing <= 0f && l > c.threatNearYd) continue;
                    float t = l / Math.Max(c.threatMinSpeedYdS, closing);
                    int at = 0;
                    while (at < threats.Count && (arrive[at] < t || (arrive[at] == t && threats[at].Slot < d.Slot))) at++;
                    threats.Insert(at, d); arrive.Insert(at, t);
                }
                foreach (var d in threats)
                {
                    Player best = null; float bestCost = float.MaxValue;
                    foreach (var b in blockers)
                    {
                        if (Assigned.ContainsKey(b.Slot)) continue;
                        Meet(w, b, d, bx, bz, out _, out _, out float tb);
                        float cost = tb - (b.BlockSlot == d.Slot ? c.keepBonusS : 0f);
                        if (cost < bestCost || (cost == bestCost && best != null && b.Slot < best.Slot)) { bestCost = cost; best = b; }
                    }
                    if (best != null && bestCost <= c.blockRangeS) Assigned[best.Slot] = d.Slot;
                }
                foreach (var b in blockers)
                {
                    b.BlockSlot = Assigned.TryGetValue(b.Slot, out int ds) ? ds : -1;
                    if (b.BlockSlot < 0) Free.Add(b);
                }
            }
        }

        static Plan PlanFor(World w, Player asker, float bx, float bz, Player skip = null)
        {
            var plan = w.BotPlan;
            if (plan.Tick != w.Tick || plan.Team != asker.Team) plan.Build(w, asker, asker.Team, bx, bz, skip);
            return plan;
        }

        // The point on the opponent's line to the ball that the blocker reaches before him (true), or, when he can't
        // beat him anywhere along it, the spot in front of the ball on the opponent's side (false). tb: his time to it.
        static bool Meet(World w, Player b, Player d, float bx, float bz, out float mx, out float mz, out float tb)
        {
            var c = w.Cfg.botsV3;
            float lx = bx - d.X, lz = bz - d.Z, l = (float)Math.Sqrt(lx * lx + lz * lz);
            float vb = Math.Max(w.Cfg.players.jogSpeedYdS, w.Cfg.players.sprintSpeedYdS * Ratings.SpeedFrac(w, b));
            float sp = (float)Math.Sqrt(d.VX * d.VX + d.VZ * d.VZ);
            float vd = Math.Max(w.Cfg.players.jogSpeedYdS, sp);
            float uMin = l > c.meetMaxYd ? 1f - c.meetMaxYd / l : 0f;
            float uMax = l > c.shieldYd ? 1f - c.shieldYd / l : 0f;
            int n = Math.Max(1, c.interceptSteps);
            for (int i = 0; i <= n; i++)
            {
                float u = uMin + (uMax - uMin) * i / n;
                float px = d.X + lx * u, pz = d.Z + lz * u;
                float dx = px - b.X, dz = pz - b.Z;
                tb = (float)Math.Sqrt(dx * dx + dz * dz) / vb;
                if (tb <= l * u / vd + c.meetLeadS) { mx = px; mz = pz; return true; }
            }
            mx = d.X + lx * uMax; mz = d.Z + lz * uMax;
            float fx = mx - b.X, fz = mz - b.Z;
            tb = (float)Math.Sqrt(fx * fx + fz * fz) / vb;
            return false;
        }

        // A blocker for the ball carrier (or the ball). open: the ball is on the move (a run, a return), so spare
        // blockers lead the way; else (pass protection) they hold.
        public static PlayerInput Block(World w, Player p, Player carrier)
        {
            bool flying = carrier == null && w.Ball.State == BallState.Flying;
            float bx = carrier != null ? carrier.X : flying ? w.Ball.TargetX : w.Ball.X;
            float bz = carrier != null ? carrier.Z : flying ? w.Ball.TargetZ : w.Ball.Z;
            var qb = LivePlay.Qb(w);
            // the ball with the QB behind the line, or in the air: nobody to lead, so the spare blockers hold (a convoy to
            // where a pass comes down put a lineman under the ball)
            bool pocket = flying || (carrier != null && carrier == qb && carrier.Team == w.Offense && (carrier.X - w.LineOfScrimmageX) * w.Dir <= 0f);
            var plan = PlanFor(w, p, bx, bz);
            PlayerInput i;
            if (plan.Assigned.TryGetValue(p.Slot, out int ds) && w.Find(ds) is Player d) i = TakeOn(w, p, d, bx, bz);
            else if (pocket) i = Hold(w, p);
            else i = Convoy(w, p, plan, bx, bz, p.Team == w.Offense ? w.Dir : -w.Dir);
            i.Block = true;
            return i;
        }

        // Meet him where we get there first, a step on his side of it so we end up between him and the ball.
        static PlayerInput TakeOn(World w, Player p, Player d, float bx, float bz)
        {
            var c = w.Cfg.botsV3;
            float mx, mz;
            if (LivePlay.Dist(p, d) <= c.engageYd)
            {
                // close: straight at him, a step on the ball's side (a meeting point out on his line to the ball swings
                // away as the carrier runs wide, and he goes round it)
                mx = d.X; mz = d.Z;
                float lx = bx - d.X, lz = bz - d.Z, ll = (float)Math.Sqrt(lx * lx + lz * lz);
                if (ll > 0f) { mx += lx / ll * w.Cfg.bots.arriveYd; mz += lz / ll * w.Cfg.bots.arriveYd; }
                return To(w, p, mx, mz, Far(p, mx, mz) > c.sprintOverYd);
            }
            Meet(w, p, d, bx, bz, out mx, out mz, out _);
            float tx = d.X - mx, tz = d.Z - mz, tl = (float)Math.Sqrt(tx * tx + tz * tz);
            if (tl > 0f) { mx += tx / tl * w.Cfg.bots.arriveYd; mz += tz / tl * w.Cfg.bots.arriveYd; }
            return To(w, p, mx, mz, Far(p, mx, mz) > c.sprintOverYd);
        }

        // Nobody to take and nobody to lead: a blocker by trade keeps his job's spot, anyone else stays where he is.
        static PlayerInput Hold(World w, Player p)
        {
            var job = Bots.JobOfCalled(w, p);
            if (job == null || job.kind != "block" || job.pts.Count == 0) return default;
            float x = w.LineOfScrimmageX + w.Dir * job.pts[0][0], z = job.pts[0][1];
            return Far(p, x, z) <= w.Cfg.bots.arriveYd ? default : To(w, p, x, z, false);
        }

        // The convoy: ahead of the carrier, spread across, by slot.
        static PlayerInput Convoy(World w, Player p, Plan plan, float bx, float bz, float dir)
        {
            var c = w.Cfg.botsV3;
            float off = 0f, side = 1f, z = bz;
            foreach (var m in plan.Free)
            {
                if (m == p) { z = bz + side * off; break; }
                side = -side;
                if (side > 0f) off += c.convoyGapYd;
            }
            float x = bx + dir * c.convoyAheadYd;
            return To(w, p, x, z, Far(p, x, z) > c.sprintOverYd);
        }

        // ---- kicks (the ball in the air or on the grass, before anyone has it) ----

        // tx, tz: where it comes down (or lies). Kicking team: cover in lanes. Receiving team: the returner goes for it,
        // the rest set up the return (take the coverage men on, or build the wall).
        public static PlayerInput KickThink(World w, Player p, float tx, float tz, Player returner)
        {
            var c = w.Cfg.botsV3;
            if (p.Team == w.Offense)
            {
                if (Math.Abs(tx - p.X) <= c.laneUntilYd) return To(w, p, tx, tz, true);
                Rank(w, p, w.Offense, Kicking.Kicker(w), out int rank, out int n);
                float z = Clamp(tz + (rank - (n - 1) * 0.5f) * c.laneGapYd, -w.Cfg.field.HalfWidth, w.Cfg.field.HalfWidth);
                return To(w, p, tx, z, true);
            }
            if (p == returner) return To(w, p, tx, tz, true);
            var plan = PlanFor(w, p, tx, tz, returner);
            PlayerInput i;
            if (plan.Assigned.TryGetValue(p.Slot, out int ds) && w.Find(ds) is Player d) i = TakeOn(w, p, d, tx, tz);
            else
            {
                int rank = 0, n = plan.Free.Count;
                for (int k = 0; k < n; k++) if (plan.Free[k] == p) rank = k;
                float x = tx - w.Dir * c.wallAheadYd;
                float z = Clamp(tz + (rank - (n - 1) * 0.5f) * c.wallGapYd, -w.Cfg.field.HalfWidth, w.Cfg.field.HalfWidth);
                i = To(w, p, x, z, Far(p, x, z) > c.sprintOverYd);
            }
            i.Block = true;
            return i;
        }

        // Where this man stands among his team-mates across the field (by line-up spot, then slot), leaving one out.
        static void Rank(World w, Player p, int team, Player skip, out int rank, out int n)
        {
            rank = 0; n = 0;
            foreach (var q in w.Players)
            {
                if (q.Team != team || q == skip) continue;
                n++;
                if (q == p) continue;
                if (q.HomeZ < p.HomeZ || (q.HomeZ == p.HomeZ && q.Slot < p.Slot)) rank++;
            }
        }

        // ---- helpers ----

        static float Far(Player p, float x, float z) { float dx = x - p.X, dz = z - p.Z; return (float)Math.Sqrt(dx * dx + dz * dz); }

        static PlayerInput To(World w, Player p, float x, float z, bool sprint)
        {
            x = Clamp(x, -w.Cfg.field.HalfLength, w.Cfg.field.HalfLength);
            z = Clamp(z, -w.Cfg.field.HalfWidth, w.Cfg.field.HalfWidth);
            float dx = x - p.X, dz = z - p.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0f) return default;
            return new PlayerInput { MoveX = dx / d, MoveZ = dz / d, Sprint = sprint };
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    // game.json "botsV3". Seconds, yards, shares 0..1.
    public sealed class BotsV3Config
    {
        // reactions: reactMinS next to the ball .. reactMaxS at reactFarYd and beyond, times (1 - quickness * reactQuickFrac),
        // plus up to reactJitterS; inside reactNearYd of the ball a man reacts at once
        public float reactMinS, reactMaxS, reactFarYd, reactJitterS, reactQuickFrac, reactNearYd;
        public float coverNearYd;      // a defender this close to the man a pass is for sees the throw at once
        public float reactThrowMaxS;   // no defender takes longer than this to see a pass leave the QB's hand
        public float pursuitLeadVar;   // a chaser's lead on the carrier, long or short by up to this share by his quickness
        // blocking: an opponent within threatRangeYd of the ball closing on it (or within threatNearYd whatever he does) is a
        // threat, ordered by his time to it at no less than threatMinSpeedYdS; a blocker takes one he can meet within
        // blockRangeS (keeping the man he has is worth keepBonusS); the meeting point is where he gets there first (plus
        // meetLeadS of slack), between shieldYd from the ball and meetMaxYd up the field, interceptSteps points tried;
        // he sprints to anything further than sprintOverYd
        public float threatRangeYd, threatNearYd, threatMinSpeedYdS, blockRangeS, keepBonusS, meetLeadS, shieldYd, meetMaxYd, sprintOverYd;
        public float engageYd;         // inside this of his man a blocker runs straight at him (a step on the ball's side) instead of to a meeting point
        public int interceptSteps;
        public float convoyAheadYd, convoyGapYd;   // the spare blockers ahead of a carrier on the move
        public float routeBlockYd;     // a route ending no further than this past the line is run to its end before its man blocks
        // kicks: coverage lanes laneGapYd apart, closing on the ball inside laneUntilYd; the return wall wallAheadYd in
        // front of where it comes down, wallGapYd apart
        public float laneGapYd, laneUntilYd, wallAheadYd, wallGapYd;
    }
}
