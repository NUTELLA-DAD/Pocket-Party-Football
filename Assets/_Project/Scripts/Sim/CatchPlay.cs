using System;

namespace PocketFootball.Sim
{
    // FEATURES.catchPlay (NO_CATCH_PLAY=1): catches the way they happen on a Sunday. Numbers in game.json "catchPlay".
    // - A diving catch: a low ball (no higher than diveMaxHeightYd) that nobody has inside the catch circle can still be
    //   caught by a man laying out for it from up to diveReachYd further away, if he is running at it at diveMinSpeedYdS
    //   or more; his odds are diveChanceMul of a plain catch's, and caught or not he is on the grass for diveDownS
    //   (slowed as after a missed tackle, so he slides on his belly and gets up).
    // - A contested catch: a man of the other team within contestYd of the catcher as he goes up for it makes it a jump
    //   ball: the catcher's odds are contestChanceMul of a plain catch's, and the two come down tangled, both on the
    //   grass for tangleS. If he drops it the ball falls on, so the other man can still pick it off.
    // The note of the last try (CatchNote) is only for the TV (View/Moves.cs: the dive, the leap, the tangle); not hashed.
    public static class CatchPlay
    {
        public static bool On(World w) => w.Cfg.Feature("catchPlay");

        // The man laying out for a ball nobody has in the catch circle: the nearest who can reach it running at it; null none.
        // hisBall: the pass is the meant man's (FEATURES.passTarget), so no team-mate of his dives in front of him.
        public static Player Diver(World w, bool hisBall, Player meant)
        {
            var k = w.Cfg.catchPlay;
            var b = w.Ball;
            if (b.Y > k.diveMaxHeightYd) return null;
            float r = w.Cfg.play.catchRadiusYd, far = r + k.diveReachYd;
            Player best = null; float bd = float.MaxValue;
            foreach (var p in w.Players)
            {
                if (w.CatchTried.Contains(p.Slot) || p.StumbleTicks > 0) continue;
                if (hisBall && p.Team == w.Offense && p != meant) continue;
                float dx = b.X - p.X, dz = b.Z - p.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d <= r || d > far || d >= bd) continue;
                if ((dx * p.VX + dz * p.VZ) / d < k.diveMinSpeedYdS) continue;   // (running at it, not past it or away)
                best = p; bd = d;
            }
            return best;
        }

        // The man of the other team right on the catcher as he goes for it (the nearest within contestYd); null none.
        public static Player Rival(World w, Player near)
        {
            Player best = null; float bd = w.Cfg.catchPlay.contestYd;
            foreach (var p in w.Players)
            {
                if (p.Team == near.Team || w.CatchTried.Contains(p.Slot) || p.StumbleTicks > 0) continue;
                float d = LivePlay.Dist(p, near);
                if (d <= bd) { best = p; bd = d; }
            }
            return best;
        }

        // The catch chance with a dive or a man on him.
        public static float Chance(World w, float chance, bool dive, bool contested)
        {
            var k = w.Cfg.catchPlay;
            if (dive) chance *= k.diveChanceMul;
            if (contested) chance *= k.contestChanceMul;
            return chance;
        }

        // After the try: the diver and a tangled pair go down for a moment; the note for the TV.
        public static void After(World w, Player near, Player rival, bool dive, bool caught)
        {
            var k = w.Cfg.catchPlay;
            if (dive) near.StumbleTicks = Math.Max(near.StumbleTicks, Huddle.Ticks(w, k.diveDownS));
            if (rival != null)
            {
                int t = Huddle.Ticks(w, k.tangleS);
                near.StumbleTicks = Math.Max(near.StumbleTicks, t);
                rival.StumbleTicks = Math.Max(rival.StumbleTicks, t);
            }
            var n = w.CatchNote;
            n.Tick = w.Tick; n.Slot = near.Slot; n.Rival = rival != null ? rival.Slot : -1; n.Caught = caught;
            n.Style = dive ? CatchStyle.Dive : rival != null ? CatchStyle.Contested : CatchStyle.Plain;
        }
    }

    public enum CatchStyle { Plain, Dive, Contested }

    // The last catch try (FEATURES.catchPlay), for the TV.
    public sealed class CatchNote
    {
        public int Tick = -1, Slot = -1, Rival = -1;
        public CatchStyle Style;
        public bool Caught;
    }

    // game.json "catchPlay" (CatchPlay.cs). Yards, yards a second, seconds, shares.
    public sealed class CatchPlayConfig
    {
        public float diveReachYd, diveMaxHeightYd, diveMinSpeedYdS, diveChanceMul, diveDownS;
        public float contestYd, contestChanceMul, tangleS;
        public float trackLobApexYd, trackNearYd;   // a bot receiver comes back to a led pass he has outrun only if it goes up
                                                    // this high (a lob) with a defender this close to him (Bots.MeetThePass)
    }
}
