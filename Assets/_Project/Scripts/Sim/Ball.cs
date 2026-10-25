using System;

namespace PocketFootball.Sim
{
    public enum BallState { Held, Flying, Loose }

    // The ball has height (Y, yards). Held: rides with its carrier. Flying: gravity, bounces when it lands.
    // Loose: rolls along the grass and slows to a stop. (Catches, fumbles and the whistle come in M1-c.)
    public sealed class Ball
    {
        public BallState State = BallState.Held;
        public int Carrier = -1;
        public float X, Y, Z, VX, VY, VZ;
        public float TargetX, TargetZ;       // where the current pass is aimed (view: shadow marker, M1-c: catches)

        public void Reset() { State = BallState.Held; Carrier = -1; VX = VY = VZ = 0; }

        public void GiveTo(Player p, BallConfig c)
        {
            State = BallState.Held; Carrier = p.Slot;
            X = p.X; Y = c.carryHeightYd; Z = p.Z; VX = VY = VZ = 0;
        }

        public void Drop()
        {
            Carrier = -1;
            State = BallState.Loose;
            VX = VY = VZ = 0;
        }

        // Aimed to come down at catchHeightYd over (tx, tz). Bullet: flies at bulletSpeedYdS along the ground.
        // Lob: rises lobApexYd above the throw height and drops in.
        public void Launch(float tx, float tz, bool lob, BallConfig c)
        {
            float dx = tx - X, dz = tz - Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float g = c.gravityYdS2;
            float drop = Y - c.catchHeightYd;
            float vy, t;
            if (lob)
            {
                vy = (float)Math.Sqrt((g + g) * c.lobApexYd);
                // time to rise and fall to catch height: vy t - g t^2 / 2 = -drop
                t = (vy + (float)Math.Sqrt(vy * vy + (g + g) * drop)) / g;
            }
            else
            {
                t = dist / c.bulletSpeedYdS;
                vy = (g * t * t * 0.5f - drop) / t;
            }
            if (t <= 0f) return;
            State = BallState.Flying; Carrier = -1;
            VX = dx / t; VZ = dz / t; VY = vy;
            TargetX = tx; TargetZ = tz;
        }

        public void Step(World w, float dt)
        {
            var c = w.Cfg.ball;
            if (State == BallState.Held)
            {
                var p = w.Find(Carrier);
                if (p == null) { Drop(); return; }
                X = p.X; Z = p.Z; Y = c.carryHeightYd; VX = p.VX; VZ = p.VZ; VY = 0;
                return;
            }
            if (State == BallState.Flying)
            {
                VY -= c.gravityYdS2 * dt;
                X += VX * dt; Y += VY * dt; Z += VZ * dt;
                if (Y <= 0f)
                {
                    Y = 0f;
                    VY = -VY * c.bounce;
                    VX *= c.bounce + (1f - c.bounce) * 0.5f;
                    VZ *= c.bounce + (1f - c.bounce) * 0.5f;
                    if (VY < c.settleVyYdS) { VY = 0f; State = BallState.Loose; }
                }
                return;
            }
            // loose: roll and slow down
            float s = (float)Math.Sqrt(VX * VX + VZ * VZ);
            if (s <= c.restSpeedYdS) { VX = VZ = 0f; return; }
            float ns = Math.Max(0f, s - c.rollDecelYdS2 * dt);
            VX *= ns / s; VZ *= ns / s;
            X += VX * dt; Z += VZ * dt;
        }
    }
}
