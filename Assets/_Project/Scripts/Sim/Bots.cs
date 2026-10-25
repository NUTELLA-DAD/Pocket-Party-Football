using System;

namespace PocketFootball.Sim
{
    // Bot v0 (M0-d): wanders. Every so often it picks a random spot nearby on the field and walks there.
    // The real per-position brains come in M1-d; this is the safety-net driver for the baseline.
    public static class Bots
    {
        public static PlayerInput Think(World w, Player p)
        {
            var b = w.Cfg.bots;
            var f = w.Cfg.field;
            if (w.Tick >= p.NextThinkTick)
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

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
