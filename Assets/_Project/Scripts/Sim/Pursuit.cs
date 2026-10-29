using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // FEATURES.pursuitLanes (NO_PURSUIT_LANES=1): a defense chasing the ball carrier the way coaches teach it. Before,
    // every chaser ran at the same spot (where the carrier would be by the time he got there), so a chase from behind
    // collapsed into one line of men on the carrier's heels and a cut ran them all into each other. Now the chasing team
    // shares one plan a tick:
    // - each chaser works out where he can really meet the carrier (the carrier keeping his speed and way): the soonest
    //   meeting at the chaser's own top speed, never more than leadMaxS ahead (a carrier who is getting away is led leadMaxS);
    // - the soonest of them is the hitter and runs straight at his meeting point;
    // - the others keep their lanes, counted out from the carrier's path on their own side (the inside man on each side
    //   first): each one aims laneGapYd further out to his side than the man inside him (laneMaxYd at most), so they come
    //   in as a fan, a cut back still meets a man (leverage) and nobody follows a team-mate's back;
    // - a chaser behind the carrier leads him only trailLead as far (he stays on his inside hip for the cut back instead
    //   of overrunning him), in lanes trailLane as wide;
    // - the lanes close as a chaser gets near: whole from spreadYd out, gone inside convergeYd (everyone to the ball);
    // - a team-mate closer than sepYd pushes his aim away, up to sepPush of the way to him per yard still to go.
    public static class Pursuit
    {
        public static bool On(World w) => w.Cfg.Feature("pursuitLanes");

        // A bot chasing the carrier: to his spot in this tick's plan, sprinting.
        public static PlayerInput Chase(World w, Player p, Player carrier)
        {
            var plan = w.ChasePlan;
            if (plan.Tick != w.Tick || plan.Carrier != carrier.Slot) plan.Build(w, carrier);
            if (!plan.Aim.TryGetValue(p.Slot, out var at)) at = new[] { carrier.X, carrier.Z };
            float dx = at[0] - p.X, dz = at[1] - p.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0f) return new PlayerInput { Sprint = true };
            return new PlayerInput { MoveX = dx / d, MoveZ = dz / d, Sprint = true };
        }

        // The soonest time t > 0 a man at (px, pz) running speed meets a carrier at (cx, cz) moving (vx, vz); -1: never.
        public static float MeetS(float px, float pz, float speed, float cx, float cz, float vx, float vz)
        {
            // |r + v t| = speed t: a t^2 + 2 b t + c = 0
            float rx = cx - px, rz = cz - pz;
            float a = vx * vx + vz * vz - speed * speed, b = rx * vx + rz * vz, c = rx * rx + rz * rz;
            float disc = b * b - a * c;
            if (disc < 0f) return -1f;
            float q = -(b + (b < 0f ? -1f : 1f) * (float)Math.Sqrt(disc));   // (the two roots as q / a and c / q: no loss near a = 0)
            float t0 = a != 0f ? q / a : -1f, t1 = q != 0f ? c / q : -1f;
            float lo = Math.Min(t0, t1), hi = Math.Max(t0, t1);
            return lo > 0f ? lo : hi > 0f ? hi : -1f;
        }

        // This tick's chase: every man not on the carrier's team, each with the spot he runs at (not hashed: rebuilt
        // every tick from the state).
        public sealed class Plan
        {
            public int Tick = -1, Carrier = -1, Hitter = -1;
            public readonly SortedDictionary<int, float[]> Aim = new SortedDictionary<int, float[]>();   // chaser slot -> (x, z)
            public readonly SortedDictionary<int, int> Lane = new SortedDictionary<int, int>();          // chaser slot -> lane (+ his left of the path, 0 at the carrier)
            readonly List<Player> chasers = new List<Player>();
            readonly List<float> meet = new List<float>(), side = new List<float>(), ahead = new List<float>();
            readonly List<int> order = new List<int>();

            internal void Build(World w, Player c)
            {
                var k = w.Cfg.pursuit;
                var pc = w.Cfg.players;
                Tick = w.Tick; Carrier = c.Slot; Hitter = -1;
                Aim.Clear(); Lane.Clear(); chasers.Clear(); meet.Clear(); side.Clear(); ahead.Clear();
                // the carrier's way (standing about: the way his team attacks) and his left
                float sp = (float)Math.Sqrt(c.VX * c.VX + c.VZ * c.VZ);
                float goal = c.Team == w.Offense ? w.Dir : -w.Dir;
                float hx = sp > 1f ? c.VX / sp : goal, hz = sp > 1f ? c.VZ / sp : 0f;
                float nx = -hz, nz = hx;
                float best = float.MaxValue;
                foreach (var q in w.Players)
                {
                    if (q.Team == c.Team) continue;
                    float top = (q.Tired ? pc.jogSpeedYdS : pc.sprintSpeedYdS) * Ratings.SpeedFrac(w, q);
                    float t = MeetS(q.X, q.Z, top, c.X, c.Z, c.VX, c.VZ);
                    t = t < 0f ? k.leadMaxS : Math.Min(t, k.leadMaxS);
                    t = BotsV3.PursuitLead(w, q, t);   // bots v3: some lead him long, some short
                    float rx = q.X - c.X, rz = q.Z - c.Z;
                    chasers.Add(q); meet.Add(t); side.Add(rx * nx + rz * nz); ahead.Add(rx * hx + rz * hz);
                    if (t < best) { best = t; Hitter = q.Slot; }   // (slot order: the first of a tie)
                }
                // the lanes: on each side of the path, counted out from the inside man (the hitter takes the middle)
                for (int s = 0; s < 2; s++)
                {
                    order.Clear();
                    for (int i = 0; i < chasers.Count; i++) if ((side[i] >= 0f) == (s == 0)) order.Add(i);
                    order.Sort((a, b) =>
                    {
                        int by = Math.Abs(side[a]).CompareTo(Math.Abs(side[b]));
                        return by != 0 ? by : chasers[a].Slot.CompareTo(chasers[b].Slot);
                    });
                    int n = 0;
                    foreach (int i in order) Lane[chasers[i].Slot] = chasers[i].Slot == Hitter ? 0 : (s == 0 ? n++ : -n++);
                }
                for (int i = 0; i < chasers.Count; i++)
                {
                    var q = chasers[i];
                    bool hitter = q.Slot == Hitter;
                    bool behind = ahead[i] < -k.behindYd && !hitter;
                    float lead = meet[i] * (behind ? k.trailLead : 1f);
                    float dist = (float)Math.Sqrt((q.X - c.X) * (q.X - c.X) + (q.Z - c.Z) * (q.Z - c.Z));
                    float open = k.spreadYd > k.convergeYd ? Clamp01((dist - k.convergeYd) / (k.spreadYd - k.convergeYd)) : dist > k.convergeYd ? 1f : 0f;
                    float off = Clamp(Lane[q.Slot] * k.laneGapYd * (behind ? k.trailLane : 1f), -k.laneMaxYd, k.laneMaxYd) * open;
                    float tx = c.X + c.VX * lead + nx * off, tz = c.Z + c.VZ * lead + nz * off;
                    // don't run up a team-mate's back
                    if (!hitter && open > 0f)
                        foreach (var o in chasers)
                        {
                            if (o == q) continue;
                            float ox = q.X - o.X, oz = q.Z - o.Z, od = (float)Math.Sqrt(ox * ox + oz * oz);
                            if (od <= 0f || od >= k.sepYd) continue;
                            float push = (k.sepYd - od) / k.sepYd * k.sepPush * open * dist;
                            tx += ox / od * push; tz += oz / od * push;
                        }
                    Aim[q.Slot] = new[] { Clamp(tx, -w.Cfg.field.HalfLength, w.Cfg.field.HalfLength), Clamp(tz, -w.Cfg.field.HalfWidth, w.Cfg.field.HalfWidth) };
                }
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    // game.json "pursuit" (FEATURES.pursuitLanes, Pursuit.cs). Seconds, yards, shares.
    public sealed class PursuitConfig
    {
        public float leadMaxS;                 // a chaser leads the carrier at most this far ahead
        public float laneGapYd, laneMaxYd;     // the lanes: this far apart, at most this far out from the carrier's path
        public float spreadYd, convergeYd;     // the lanes whole from spreadYd away, closed inside convergeYd
        public float behindYd, trailLead, trailLane;   // a chaser more than behindYd behind him: that share of the lead and of the lane
        public float sepYd, sepPush;           // a team-mate closer than sepYd pushes his aim away (sepPush of the way, per yard to go)
    }
}
