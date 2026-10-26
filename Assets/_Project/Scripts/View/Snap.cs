using System;
using PocketFootball.Sim;

namespace PocketFootball.View
{
    // The snap on the TV (FEATURES.snap; NO_SNAP=1: the QB just holds the ball from the line-up, as before).
    // The sim gives the QB the ball as everyone lines up and the hike is a single tick, so on its own the ball sits in
    // the QB's hand 5 yards behind an empty-handed center. This shows a real one: in the huddle and the line-up the ball
    // lies on the grass under the center's hands (anim.snapAheadYd in front of his feet, along the field); at the hike
    // it flies back into the QB's hands (the punter's, the holder's) at anim.snapSpeedYdS, never quicker than
    // anim.snapMinS, rising a little on the way (anim.snapArcYd), and he pulls it in (anim.snapSecureS) before his own
    // moves take over. The center stays down in his stance until it has gone. Read straight from the World every
    // frame (the phase, the play's ticks), so the replay shows it too. Not on a kickoff; if the QB hands off or throws
    // before it reaches him it goes to whoever has the ball by then. Pure C#: tests drive it with a World.
    public static class Snap
    {
        public static bool On(World w) => w.Cfg.Feature("snap") && LivePlay.On(w);

        // The man who snaps it: the offense lineman lined up straightest in front of the QB.
        public static Player Center(World w)
        {
            var qb = LivePlay.Qb(w);
            if (qb == null) return null;
            Player best = null; float bd = float.MaxValue;
            foreach (var p in w.Players)
            {
                if (p.Team != w.Offense || p.Pos != "OL") continue;
                float d = Math.Abs(p.HomeZ - qb.HomeZ);
                if (d < bd) { best = p; bd = d; }
            }
            return best;
        }

        // Before the hike: the ball on the grass at the center's hands. x, z: where (yards).
        public static bool Spotted(World w, out float x, out float z)
        {
            x = z = 0f;
            if (w.Phase != Phase.Huddle && w.Phase != Phase.Set) return false;
            var c = Ready(w);
            if (c == null || w.Ball.State != BallState.Held || w.Ball.Carrier != LivePlay.Qb(w).Slot) return false;
            Spot(w, c, out x, out z);
            return true;
        }

        // How long the ball is on its way back, in ticks: from the spot to where the QB lined up.
        public static int FlightTicks(World w)
        {
            var c = Center(w); var qb = LivePlay.Qb(w);
            if (c == null || qb == null) return 0;
            Spot(w, c, out float x, out float z);
            float dx = qb.HomeX - x, dz = qb.HomeZ - z;
            var a = w.Cfg.anim;
            float s = Math.Max(a.snapMinS, (float)Math.Sqrt(dx * dx + dz * dz) / a.snapSpeedYdS);
            return Math.Max(1, (int)Math.Round(s * w.Cfg.sim.tickHz));
        }

        // The ball on its way back: t 0 (leaving the center's hands) .. 1 (in the carrier's), from (fromX, fromZ).
        // alpha: the blend between the last two ticks, as the view draws.
        public static bool InFlight(World w, float alpha, out float t, out float fromX, out float fromZ)
        {
            t = fromX = fromZ = 0f;
            var c = Live(w);
            if (c == null || w.Ball.State != BallState.Held) return false;
            int n = FlightTicks(w);
            float at = SinceHike(w) + alpha;
            if (at >= n) return false;
            t = Math.Max(0f, at / n);
            Spot(w, c, out fromX, out fromZ);
            return true;
        }

        // The center's and the QB's part (Moves.Of): down in the stance until it's gone; hands out for it, then
        // pulling it in. Move.None: nothing to do with the snap.
        public static Move For(World w, Player p)
        {
            var c = Live(w);
            if (c == null) return Move.None;
            int n = FlightTicks(w), at = SinceHike(w);
            if (p == c && at < n) return Move.Snap;
            if (p != LivePlay.Qb(w) || LivePlay.Carrier(w) != p) return Move.None;
            if (at < n) return Move.TakeSnap;
            if (at < n + (int)Math.Round(w.Cfg.anim.snapSecureS * w.Cfg.sim.tickHz)) return Move.Secure;
            return Move.None;
        }

        // The center, when the snap is on and this play has one (not a kickoff, no center in the line-up: null).
        static Player Ready(World w)
        {
            if (!On(w) || LivePlay.Qb(w) == null || Kicking.Mode(w) == KickKind.Kickoff) return null;
            return Center(w);
        }

        // The center, while the snap is still in the air or just caught (the play live, nothing thrown yet), else null.
        static Player Live(World w)
        {
            if (w.Phase != Phase.Live || w.Thrown) return null;
            var c = Ready(w);
            var carrier = LivePlay.Carrier(w);
            return c != null && carrier != null && carrier.Team == w.Offense ? c : null;
        }

        // Ticks since the hike: the hike's own tick counts the play's first tick (LivePlay.Step).
        static int SinceHike(World w) => Math.Max(0, w.PlayTicks - 1);

        static void Spot(World w, Player c, out float x, out float z)
        {
            x = c.HomeX + w.Dir * w.Cfg.anim.snapAheadYd;
            z = c.HomeZ;
        }
    }
}
