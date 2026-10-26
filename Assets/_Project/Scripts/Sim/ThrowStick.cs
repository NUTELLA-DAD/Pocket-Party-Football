using System;

namespace PocketFootball.Sim
{
    // The throw stick (FEATURES.throwStick; NO_THROW_STICK=1 turns it off): the QB's phone gets a second stick that aims
    // the pass at a spot on the field instead of at a receiver. The stick is in the move stick's frame (x along the
    // field, z across it), pushed all the way it reaches play.throwMaxYards from the QB. The arc goes from 0, a flat
    // bullet at ball.bulletSpeedYdS, to 1, a lob with apex ball.lobApexYd; in between the flight time is blended, so
    // the longer the QB holds the aim the higher and softer it goes. Either way it comes down at catch height over the
    // spot. The man nearest that spot runs to it (Bots.Live), and the TV draws the arc while the QB aims (ThrowAimView).
    public static class ThrowStick
    {
        public static bool On(World w) => w.Cfg.Feature("throwStick");

        public struct Flight { public float VX, VY, VZ, T, TrueVY; }

        // Where a throw with this stick lands; false while the stick is still in the dead zone.
        public static bool Target(World w, Player qb, float aimX, float aimZ, out float tx, out float tz)
        {
            float len = (float)Math.Sqrt(aimX * aimX + aimZ * aimZ);
            tx = qb.X; tz = qb.Z;
            if (len < w.Cfg.play.throwDeadZone) return false;   // let go this near the middle: no throw
            if (len > 1f) { aimX /= len; aimZ /= len; }
            float r = w.Cfg.play.throwMaxYards;
            tx = qb.X + aimX * r;
            tz = qb.Z + aimZ * r;
            return true;
        }

        // The flight from (x, y, z) that comes down at catch height over (tx, tz). VY is for the sim's step (Ball.Step
        // takes gravity off before it moves, which drops the ball g dt t / 2 short of the smooth curve: half a yard
        // on a flat bullet), so it is raised by g dt / 2 to land right on the spot. The smooth curve the ball then
        // follows starts at TrueVY.
        public static bool Plan(BallConfig c, float dt, float x, float y, float z, float tx, float tz, float arc, out Flight f)
        {
            f = default;
            arc = arc < 0f ? 0f : arc > 1f ? 1f : arc;
            float dx = tx - x, dz = tz - z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float g = c.gravityYdS2;
            float drop = y - c.catchHeightYd;
            float bullet = dist / c.bulletSpeedYdS;
            float vyLob = (float)Math.Sqrt((g + g) * c.lobApexYd);
            float lob = (vyLob + (float)Math.Sqrt(vyLob * vyLob + (g + g) * drop)) / g;
            float t = bullet + (lob - bullet) * arc;
            if (t <= 0f) return false;
            f.T = t;
            f.VX = dx / t; f.VZ = dz / t;
            f.TrueVY = (g * t * t * 0.5f - drop) / t;
            f.VY = f.TrueVY + g * dt * 0.5f;
            return true;
        }

        // The QB lets go of the stick: the ball leaves his hand for the spot.
        public static bool Throw(World w, Player qb, float aimX, float aimZ, float arc)
        {
            var b = w.Ball;
            if (b.State != BallState.Held || b.Carrier != qb.Slot) return false;
            if (!Target(w, qb, aimX, aimZ, out float tx, out float tz)) return false;
            if (!Plan(w.Cfg.ball, w.Dt, b.X, b.Y, b.Z, tx, tz, arc, out var f)) return false;
            b.Fly(f.VX, f.VY, f.VZ, tx, tz);
            w.Thrown = true;
            w.StickThrow = true;
            w.Events.Push(w, EventKind.Throw, qb, Bots.Intended(w));
            return true;
        }
    }
}
