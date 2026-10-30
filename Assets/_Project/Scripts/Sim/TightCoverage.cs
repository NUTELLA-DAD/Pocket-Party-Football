using System;

namespace PocketFootball.Sim
{
    // FEATURES.tightCoverage (NO_TIGHT_COVERAGE=1): a defender right on the receiver as the ball gets there can win it.
    // Numbers in game.json "tightCoverage". It works on a jump ball (FEATURES.catchPlay: a man of each team within
    // catchPlay.contestYd of each other, one of them going for the catch). Before it, the defender only got the ball
    // when the receiver dropped it and he then caught it himself (about 1 pass in 50 with a man right on him); now one
    // roll settles the jump ball first:
    // - How tight: 1 with the two within closeYd of each other, falling off in a straight line to 0 at contestYd.
    // - PICKED OFF: pickChance x tightness, plus inFrontBonus x tightness when the defender is nearer the ball than the
    //   receiver (he jumped the route), times the defender's hands (Ratings) and the weather (a wet ball). Feet out of
    //   bounds: knocked down instead (FEATURES.oobIncomplete).
    // - BROKEN UP: breakupChance x tightness: he gets a hand in and the ball goes to the grass, incomplete.
    // - Else the catch try goes on as before (a harder catch, contestChanceMul; a drop falls on).
    // So a receiver with a man draped on him catches it a bit under half the time; one with a step on his man nearly
    // always, as before.
    public static class TightCoverage
    {
        public static bool On(World w) => w.Cfg.Feature("tightCoverage") && CatchPlay.On(w);

        // 0 (just at contestYd) to 1 (within closeYd): how tight the defender is on the receiver.
        public static float Tightness(World w, Player receiver, Player defender)
        {
            var k = w.Cfg.tightCoverage;
            float far = w.Cfg.catchPlay.contestYd, near = Math.Min(k.closeYd, far);
            float d = LivePlay.Dist(receiver, defender);
            if (d <= near) return 1f;
            if (d >= far || far <= near) return 0f;
            return (far - d) / (far - near);
        }

        // The defender's odds of picking it off (pick) and of knocking it down (breakup) on this jump ball.
        public static void Odds(World w, Player receiver, Player defender, out float pick, out float breakup)
        {
            var k = w.Cfg.tightCoverage;
            float t = Tightness(w, receiver, defender);
            float p = k.pickChance;
            if (InFront(w, receiver, defender)) p += k.inFrontBonus;
            pick = Weather.Catch(w, Ratings.Catch(w, defender, p * t));
            breakup = k.breakupChance * t;
            if (pick + breakup > 1f) breakup = Math.Max(0f, 1f - pick);
        }

        // Is the defender nearer the ball than the receiver (he is under it first)?
        public static bool InFront(World w, Player receiver, Player defender)
        {
            var b = w.Ball;
            float rx = b.X - receiver.X, rz = b.Z - receiver.Z, dx = b.X - defender.X, dz = b.Z - defender.Z;
            return dx * dx + dz * dz < rx * rx + rz * rz;
        }

        public enum Result { None, Picked, BrokenUp }

        // The jump ball between `near` (the man going for the catch) and `rival` (the other team's man on him). One
        // roll, always taken when the switch is on and the two are of different teams. Picked: the defender has it (the
        // interception is pushed and the return begins or the whistle goes). BrokenUp: the ball is on the grass. None:
        // the catch try goes on as before.
        public static Result Resolve(World w, Player near, Player rival)
        {
            if (!On(w) || near == null || rival == null || near.Team == rival.Team) return Result.None;
            var receiver = near.Team == w.Offense ? near : rival;
            var defender = near.Team == w.Offense ? rival : near;
            Odds(w, receiver, defender, out float pick, out float breakup);
            float r = w.Rng.Value();
            if (r >= pick + breakup) return Result.None;
            w.CatchTried.Add(receiver.Slot);
            w.CatchTried.Add(defender.Slot);
            if (r < pick && !(defender.OutOfBounds && w.Cfg.Feature("oobIncomplete")))
            {
                CatchPlay.After(w, defender, receiver, false, true);   // (the TV: the defender comes down with it)
                DefenseMoves.PickOff(w, defender);
                return Result.Picked;
            }
            CatchPlay.After(w, receiver, defender, false, false);
            DefenseMoves.KnockDown(w);
            return Result.BrokenUp;
        }
    }

    // game.json "tightCoverage" (TightCoverage.cs). Yards and shares (0..1) at full tightness.
    public sealed class TightCoverageConfig
    {
        public float closeYd, pickChance, inFrontBonus, breakupChance;
    }
}
