using System;

namespace PocketFootball.Sim
{
    // How players move: speed up and slow down at a set rate, jog or sprint (sprint drains stamina; an empty tank
    // means no sprint until it refills to staminaRestart), bump each other apart, and get flagged out of bounds.
    public static class Mover
    {
        public static void Move(World w, Player p, PlayerInput input, float dt)
        {
            var c = w.Cfg.players;
            float mx = input.MoveX, mz = input.MoveZ;
            float m = (float)Math.Sqrt(mx * mx + mz * mz);
            if (m > 1f) { mx /= m; mz /= m; m = 1f; }

            // stamina
            if (p.Tired && p.Stamina >= c.staminaRestart) p.Tired = false;
            p.Sprinting = input.Sprint && m > 0f && !p.Tired && p.Stamina > 0f;
            if (p.Sprinting)
            {
                p.Stamina -= c.staminaDrainPerS * dt;
                if (p.Stamina <= 0f) { p.Stamina = 0f; p.Tired = true; p.Sprinting = false; }
            }
            else if (p.Stamina < 1f) p.Stamina = Math.Min(1f, p.Stamina + c.staminaRegenPerS * dt);

            // velocity eases toward the wanted one: accelYdS2 when speeding up, decelYdS2 when slowing or turning hard
            float top = p.Sprinting ? c.sprintSpeedYdS : c.jogSpeedYdS;
            float wantX = mx * top, wantZ = mz * top;
            float dx = wantX - p.VX, dz = wantZ - p.VZ;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d > 0f)
            {
                bool speedingUp = wantX * p.VX + wantZ * p.VZ >= 0f && wantX * wantX + wantZ * wantZ > p.VX * p.VX + p.VZ * p.VZ;
                float step = (speedingUp ? c.accelYdS2 : c.decelYdS2) * dt;
                if (step >= d) { p.VX = wantX; p.VZ = wantZ; }
                else { p.VX += dx / d * step; p.VZ += dz / d * step; }
            }
            p.X += p.VX * dt;
            p.Z += p.VZ * dt;
        }

        // Pairs closer than two radii are pushed apart along the line between them (each moves bumpPushFrac of the
        // overlap). Slot order, so the result never depends on anything but the state.
        public static void Bump(World w)
        {
            var c = w.Cfg.players;
            float min = c.radiusYd + c.radiusYd;
            var ps = w.Players;
            for (int i = 0; i < ps.Count; i++)
                for (int j = i + 1; j < ps.Count; j++)
                {
                    var a = ps[i]; var b = ps[j];
                    float dx = b.X - a.X, dz = b.Z - a.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= min * min) continue;
                    float d = (float)Math.Sqrt(d2);
                    float nx, nz;
                    if (d > 0f) { nx = dx / d; nz = dz / d; }
                    else { nx = a.Team <= b.Team ? 1f : -1f; nz = 0f; }    // exactly on top: split along X
                    float push = (min - d) * c.bumpPushFrac;
                    a.X -= nx * push; a.Z -= nz * push;
                    b.X += nx * push; b.Z += nz * push;
                }
        }

        // Out of bounds = past a sideline or an end line. Players can step a little way off (boundsMarginYd), no further.
        public static void Bounds(World w)
        {
            var f = w.Cfg.field;
            float margin = w.Cfg.players.boundsMarginYd;
            float maxX = f.HalfLength + margin, maxZ = f.HalfWidth + margin;
            foreach (var p in w.Players)
            {
                if (p.X < -maxX) { p.X = -maxX; if (p.VX < 0f) p.VX = 0f; }
                if (p.X > maxX) { p.X = maxX; if (p.VX > 0f) p.VX = 0f; }
                if (p.Z < -maxZ) { p.Z = -maxZ; if (p.VZ < 0f) p.VZ = 0f; }
                if (p.Z > maxZ) { p.Z = maxZ; if (p.VZ > 0f) p.VZ = 0f; }
                p.OutOfBounds = Math.Abs(p.X) > f.HalfLength || Math.Abs(p.Z) > f.HalfWidth;
            }
        }
    }
}
