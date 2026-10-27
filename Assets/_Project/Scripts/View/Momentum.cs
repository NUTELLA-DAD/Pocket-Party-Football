using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // FEATURES.momentumLean: how a running figure carries its weight (View only; the sim's speeding up and slowing,
    // Mover.Move, is untouched). Once a sim tick, from the player's velocity:
    // - its facing turns toward the way it runs at a limited rate (anim.turnSlowDegS standing, turnFastDegS at sprint
    //   speed), never snapping round; a full turn-around swings through the camera side, so the face shows;
    // - a lean follows its velocity change, smoothed over leanTauS, so a short cut gives a small lean and a long hard
    //   brake (a reverse) a big one: forward speeding up, back slowing down, into the turn on a cut;
    // - a hard enough brake (past plantFrom) is a plant: the lead leg digs in ahead, the body leans back the new way,
    //   then the figure turns and pushes off leaning forward.
    // Pure maths on a small state per player, so the EditMode tests can run it without a window.
    public struct MomentumState
    {
        public bool Ready;
        public float Yaw;          // the facing, Unity yaw degrees (0 = +x, +90 = -z, toward the camera)
        public float Vx, Vz;       // the velocity last tick (yd/s)
        public float X, Z;         // where it was last tick (yd), to spot a jump (lined up again, set down after a tackle)
        public float Lx, Lz;       // the smoothed velocity change (yd/s^2), world axes
    }

    public static class Momentum
    {
        const float RunningYdS = 0.5f;   // slower than this a figure stands (PlayersView faces its team's way)
        const float JumpYd = 1.5f;       // moved further than its velocity explains in one tick: placed, not run

        public static float YawOf(float x, float z) => Mathf.Atan2(-z, x) * Mathf.Rad2Deg;

        // One sim tick. standYaw: the facing when standing (its team's way of play); topSpeed: sprint speed (yd/s).
        public static void Step(ref MomentumState s, AnimConfig a, float x, float z, float vx, float vz, float standYaw, float topSpeed, float dt)
        {
            float speed = Mathf.Sqrt(vx * vx + vz * vz);
            float moved = Mathf.Sqrt((x - s.X) * (x - s.X) + (z - s.Z) * (z - s.Z));
            if (!s.Ready || moved > JumpYd + speed * dt)
            {
                s = new MomentumState { Ready = true, Yaw = speed > RunningYdS ? YawOf(vx, vz) : standYaw, Vx = vx, Vz = vz, X = x, Z = z };
                return;
            }
            float ax = (vx - s.Vx) / dt, az = (vz - s.Vz) / dt;
            float cap = 2f * Mathf.Max(1f, a.leanRefYdS2), am = Mathf.Sqrt(ax * ax + az * az);
            if (am > cap) { ax *= cap / am; az *= cap / am; }   // (a shove or a stop the sim set outright)
            // it leans in quickly (leanTauS) and comes back up slower (leanFallS), so a lean holds a moment after the push
            float tau = ax * ax + az * az > s.Lx * s.Lx + s.Lz * s.Lz ? a.leanTauS : Mathf.Max(a.leanTauS, a.leanFallS);
            float k = tau > 0f ? 1f - Mathf.Exp(-dt / tau) : 1f;
            s.Lx += (ax - s.Lx) * k;
            s.Lz += (az - s.Lz) * k;

            // the way it wants to face: where it runs; nearly stopped, the way it already faces while its weight is still
            // moving (a reverse passing through zero, a hard stop), then its team's way
            float lean = Mathf.Sqrt(s.Lx * s.Lx + s.Lz * s.Lz);
            float target = speed > RunningYdS ? YawOf(vx, vz) : lean > 0.25f * a.leanRefYdS2 ? s.Yaw : standYaw;
            float rate = Mathf.Lerp(a.turnSlowDegS, a.turnFastDegS, Mathf.Clamp01(speed / Mathf.Max(0.1f, topSpeed))) * dt;
            s.Yaw = Mathf.Repeat(s.Yaw + Turn(s.Yaw, target, rate) + 180f, 360f) - 180f;
            s.Vx = vx; s.Vz = vz; s.X = x; s.Z = z;
        }

        // How far to turn this tick toward target (degrees, at most rate). The short way round, except a near turn-around
        // (past 150 degrees), which swings through +90 (facing the camera) so a reverse shows the face, not the back.
        public static float Turn(float yaw, float target, float rate)
        {
            float d = Mathf.DeltaAngle(yaw, target);
            if (Mathf.Abs(d) > 150f)
            {
                float pos = Mathf.Repeat(target - yaw, 360f);              // the turn going +
                bool viaCamera = Mathf.Repeat(90f - yaw, 360f) < pos;     // +90 lies on the way going +
                d = viaCamera ? pos : pos - 360f;
            }
            return Mathf.Clamp(d, -rate, rate);
        }

        // The lean now, in the figure's own frame: fwdDeg (+ forward, - back), sideDeg (+ toward its left), in
        // leanStepDeg steps. A sideways lean stops at sideCapDeg, the rest goes front or back (mid turn-around it stays
        // sat back, then leans into the new way, never tipping over sideways); running adds speedLeanDeg forward at
        // topSpeed (less slower), so a sprint and a jog differ. plant how hard it is braking past plantFrom, as stepped keys: 0, 0.5 (past 30% of the way) or 1 (past 75%).
        public static void Lean(in MomentumState s, AnimConfig a, float topSpeed, out float fwdDeg, out float sideDeg, out float plant)
        {
            fwdDeg = sideDeg = plant = 0f;
            float mag = Mathf.Sqrt(s.Lx * s.Lx + s.Lz * s.Lz);
            if (!s.Ready || a.leanRefYdS2 <= 0f) return;
            if (mag < 1e-3f) mag = 1e-3f;
            float r = s.Yaw * Mathf.Deg2Rad;
            float fx = Mathf.Cos(r), fz = -Mathf.Sin(r);    // forward (local +x)
            float lx = Mathf.Sin(r), lz = Mathf.Cos(r);     // its left (local +z)
            float af = s.Lx * fx + s.Lz * fz, al = s.Lx * lx + s.Lz * lz;
            float deg = a.leanMaxDeg * Mathf.Pow(Mathf.Clamp01(mag / a.leanRefYdS2), Mathf.Max(0.1f, a.leanGamma));
            float fwd = deg * af / mag, side = deg * al / mag;
            if (a.sideCapDeg > 0f && Mathf.Abs(side) > a.sideCapDeg)
            {
                float rest = Mathf.Sqrt(Mathf.Max(0f, fwd * fwd + side * side - a.sideCapDeg * a.sideCapDeg));
                side = Mathf.Sign(side) * a.sideCapDeg;
                fwd = fwd > 0f ? rest : -rest;
            }
            float speed = Mathf.Sqrt(s.Vx * s.Vx + s.Vz * s.Vz);
            fwd += a.speedLeanDeg * Mathf.Clamp01(speed / Mathf.Max(0.1f, topSpeed));
            fwdDeg = Step(fwd, a.leanStepDeg);
            sideDeg = Step(side, a.leanStepDeg);
            float brake = -af / a.leanRefYdS2;
            float p = a.plantFrom < 1f ? Mathf.Clamp01((brake - a.plantFrom) / (1f - a.plantFrom)) : 0f;
            plant = p >= 0.75f ? 1f : p >= 0.3f ? 0.5f : 0f;
        }

        static float Step(float deg, float step) => step > 0f ? Mathf.Round(deg / step) * step : deg;
    }
}
