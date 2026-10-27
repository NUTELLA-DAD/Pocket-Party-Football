using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The little things that keep a man alive on the TV, all counted in sim ticks and stepped at anim.fps (no wobble):
    // FEATURES.idleMotion: a man standing still breathes (his chest rises a degree or two) and shifts his weight from
    // foot to foot, each man on his own beat so the line doesn't breathe as one.
    // FEATURES.headTrack: a man with nothing else to look at turns his head (only his head) toward the ball and tips
    // it up to a ball in the air. (The models have no eye bones: their eyes are painted on behind the facemask.)
    // FEATURES.ballSpin: a pass spirals about its long axis; a kick tumbles end over end.
    public static class Alive
    {
        // The key frame for a man at tick (slot puts each man on his own beat).
        public static int Key(GameConfig cfg, int tick, int slot) =>
            (int)(tick * cfg.anim.fps / Mathf.Max(1, cfg.sim.tickHz)) + slot * 7;

        // 0..1..0 over keys keys, by key frame: a triangle, not a sine of time.
        static float Tri(int key, int keys)
        {
            keys = Mathf.Max(2, keys);
            int k = ((key % keys) + keys) % keys;
            return 1f - Mathf.Abs(2f * k / keys - 1f);
        }

        // The idle for key: breath (degrees the chest comes up), shift (-1..1: his weight on his left .. right foot,
        // held a moment at each end). Off: both 0.
        public static void Idle(GameConfig cfg, int key, out float breathDeg, out float shift)
        {
            breathDeg = 0f; shift = 0f;
            if (!cfg.Feature("idleMotion")) return;
            var a = cfg.anim;
            breathDeg = Tri(key, a.idleBreathKeys) * a.idleBreathDeg;
            shift = Mathf.Clamp((Tri(key, a.idleShiftKeys) - 0.5f) * 3f, -1f, 1f);
        }

        // FEATURES.headTrack: how far a man facing yaw (degrees, Unity yaw) at (x, z) turns his head toward the ball at
        // (bx, by, bz) (yards), and tips it up (degrees, - = down), in lookStepDeg steps and no further than
        // trackHeadDeg / trackUpDeg / trackDownDeg. False: off, or the ball is at his feet.
        public static bool HeadTrack(GameConfig cfg, float yaw, float x, float z, float bx, float by, float bz, out float headYaw, out float tip)
        {
            headYaw = 0f; tip = 0f;
            if (!cfg.Feature("headTrack")) return false;
            var a = cfg.anim;
            float dx = bx - x, dz = bz - z, flat = Mathf.Sqrt(dx * dx + dz * dz);
            if (flat < 0.3f) return false;
            float step = Mathf.Max(1f, a.lookStepDeg);
            float Step(float deg) => Mathf.Round(deg / step) * step;
            float rel = Mathf.DeltaAngle(yaw, Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg);
            headYaw = Step(Mathf.Clamp(rel, -a.trackHeadDeg, a.trackHeadDeg));
            float eyes = cfg.view.playerHeightM * 0.8f / cfg.field.yardM;
            float up = Mathf.Atan2(by - eyes, flat) * Mathf.Rad2Deg;
            tip = Step(Mathf.Clamp(up, -a.trackDownDeg, a.trackUpDeg));
            return true;
        }

        // FEATURES.ballSpin: the ball's turn (degrees) keys key frames into its flight: about its long axis for a pass
        // (the spiral), across it for a kick (end over end). Off: 0.
        public static float Spin(GameConfig cfg, int keys, bool kick)
        {
            if (!cfg.Feature("ballSpin") || keys <= 0) return 0f;
            float per = kick ? cfg.anim.tumbleDegPerKey : cfg.anim.spiralDegPerKey;
            return Mathf.Repeat(keys * per, 360f);
        }
    }
}
