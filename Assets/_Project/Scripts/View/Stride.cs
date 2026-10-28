using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // FEATURES.strideCycle: a running or walking figure's legs and arms through a whole stride, with every in-between.
    // The stride's phase (0..1 = a left step and a right step) moves with the distance run, never with the clock, so a
    // man standing still holds his pose and a sprinter's legs keep pace with the grass. From the phase:
    // - each foot is on the grass for the stance share of the stride: it touches down a little ahead of the hip with
    //   the knee a little bent, the knee gives under the load, then straightens (a run) or starts to fold (a walk) as
    //   the heel comes up and the toes push off (FEATURES.footFlex: the foot bending at its ball, the toes flat); the thigh turns so the foot stays put under the passing body (on the
    //   model's own leg lengths), and the hips sit as high as the planted leg holds them;
    // - in the swing the knee folds the heel up behind, the thigh drives forward and the shin unfolds to land again,
    //   along eased curves that carry on from the stance's own motion at both ends;
    // - in the air (a run) the hips rise; the arms swing against the legs, the elbows bending more as the arm comes
    //   forward, and the shoulders turn a little with them.
    // The knee's bend is set directly, never solved from where the foot must be: a short-thighed leg's knee would jump
    // about near straight. Only a foot that would end up under the grass bends its knee further to stay on it.
    // Walk, jog and sprint each have their own numbers (anim.stride*: one each, at strideWalkYdS and the players' jog
    // and sprint speeds), blended between by speed; slower than a walk everything shrinks toward standing.
    // Pure maths, so the EditMode tests can run it without a window.
    public struct StrideLegs
    {
        public float HipY;                 // the hip joint's height standing (rig cm, the figure's units)
        public float Thigh, Shin, Ankle;   // hip to knee, knee to ankle, the ankle's height standing (rig cm)
        public float ThighRest, ShinRest;  // each bone's angle from straight down standing (degrees, + forward)
        public float Foot, FootDropDeg;    // ankle to the ball of the foot (rig cm; 0: no foot) and that line's slope below level
        public float Bend;                 // the planted knee's bend, a share of the stride's (0: all of it; the model's strideBend)

        // The figure's own legs, for a figure with no model on.
        public static StrideLegs Figure => new StrideLegs { HipY = 60f, Thigh = 29f, Shin = 31f, Ankle = 0f };
    }

    public struct StridePose
    {
        public float Drop;                                   // the hips this far below standing (rig cm)
        public float ThighL, ShinL, ThighR, ShinR;           // the figure's leg angles (FootballFigure's thigh / shin)
        public float ToeL, ToeR;                             // each foot pitched this far (degrees, - = toes down)
        public float BallL, BallR;                           // FEATURES.footFlex: the toes bent up at the ball of the foot (degrees)
        public float ArmL, ForeL, ArmR, ForeR;               // the arm swing and elbow bend (FootballFigure's arm / fore)
        public float Twist;                                  // the shoulders turned (degrees, + toward his right)
    }

    public static class Stride
    {
        // the stance's knee: its bend at touch-down, under load at LoadAt of the stance, and at toe-off
        const float LoadAt = 0.4f;
        // the swing's keys (shares of the swing): the knee folded most at FoldAt, the thigh FoldDrive of the way to its
        // drive at FoldThighAt and all the way at DriveAt, the knee still UnfoldKeep of its fold there (it unfolds only
        // in the last of the swing, just before the foot lands)
        const float FoldAt = 0.35f, FoldThighAt = 0.38f, FoldDrive = 0.55f, DriveAt = 0.62f, UnfoldKeep = 0.9f;
        const float PushFrom = 0.3f;          // the heel comes up over the last part of the stance (the hip rolling over the toes)
        const float PointFrom = 0.25f;        // and the foot points over the last part of that
        const float ToeBackBy = 0.45f;        // the pointed foot comes back level by this share of the swing
        // the foot points most just after it leaves the grass (as a real one does): OffShare of the way there at toe-off,
        // all the way at PeakAt of the swing, so a run's short touch on the grass doesn't snap it down in one frame
        const float OffShare = 0.6f, PeakAt = 0.12f;
        const float StrikeTo = 0.2f, StrikeFrom = 0.7f;   // the toes up (strideHeelStrikeDeg) landing: from this share of the swing to this of the stance
        // a swing leg's say in the hips' height fades out while the other foot is down (it's in the air, it holds
        // nothing): Release leg lengths higher at the middle of that, so the planted leg's own bend sets the hips
        const float Release = 0.25f;
        const float ArmLead = 0.05f;          // the arms swing this share of a stride ahead of the legs
        const float Slope = 1e-3f;            // (a step along the stance, to measure how fast it moves at its ends)

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // walk / jog / sprint numbers at speed (yd/s): the gait's, blended between the two around it
        static float Gait(float[] v, float speed, AnimConfig a, PlayersConfig pc)
        {
            if (v == null || v.Length < 3) return 0f;
            if (speed <= pc.jogSpeedYdS)
                return Mathf.Lerp(v[0], v[1], Mathf.Clamp01((speed - a.strideWalkYdS) / Mathf.Max(0.01f, pc.jogSpeedYdS - a.strideWalkYdS)));
            return Mathf.Lerp(v[1], v[2], Mathf.Clamp01((speed - pc.jogSpeedYdS) / Mathf.Max(0.01f, pc.sprintSpeedYdS - pc.jogSpeedYdS)));
        }

        // A whole stride (two steps) at speed, in yards.
        public static float CycleYd(AnimConfig a, PlayersConfig pc, float speed) =>
            2f * Mathf.Max(speed, 0.05f) / Mathf.Max(0.1f, Gait(a.strideCadence, speed, a, pc));

        // The phase after running movedYd at speed.
        public static float Advance(float phase, AnimConfig a, PlayersConfig pc, float speed, float movedYd) =>
            Mathf.Repeat(phase + movedYd / CycleYd(a, pc, speed), 1f);

        // -1 at u = 0, +1 at u = 0.5, eased at both ends (a held swing, then the way back)
        public static float Wave(float u)
        {
            u = Mathf.Repeat(u, 1f);
            return u < 0.5f ? -1f + 2f * Smooth(u * 2f) : 1f - 2f * Smooth((u - 0.5f) * 2f);
        }

        // hip to ankle with the knee bent kneeDeg
        static float Span(StrideLegs g, float kneeDeg) =>
            Mathf.Sqrt(g.Thigh * g.Thigh + g.Shin * g.Shin + 2f * g.Thigh * g.Shin * Mathf.Cos(kneeDeg * Mathf.Deg2Rad));

        // the angle at the hip between the thigh and the line to the ankle, span apart
        static float AtHip(StrideLegs g, float span) =>
            Mathf.Acos(Mathf.Clamp((g.Thigh * g.Thigh + span * span - g.Shin * g.Shin) / (2f * g.Thigh * span), -1f, 1f)) * Mathf.Rad2Deg;

        // Hip at (0, hy), ankle wanted at (ax, ay): the thigh's angle from straight down (+ forward) and the knee's bend.
        public static void Reach(StrideLegs g, float hy, float ax, float ay, out float thighDeg, out float kneeDeg)
        {
            float dx = ax, dy = hy - ay;
            float l = Mathf.Clamp(Mathf.Sqrt(dx * dx + dy * dy), Mathf.Abs(g.Thigh - g.Shin) + 0.5f, (g.Thigh + g.Shin) * 0.9999f);
            float atKnee = Mathf.Acos(Mathf.Clamp((g.Thigh * g.Thigh + g.Shin * g.Shin - l * l) / (2f * g.Thigh * g.Shin), -1f, 1f)) * Mathf.Rad2Deg;
            thighDeg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg + AtHip(g, l);
            kneeDeg = 180f - atKnee;
        }

        // A smooth curve through (s, value) keys, each key's slope from its neighbours, the two ends' given.
        static float Curve(float s, float[] ks, float[] vs, float slope0, float slope1)
        {
            int n = ks.Length, j = 0;
            while (j < n - 2 && s > ks[j + 1]) j++;
            float m0 = j == 0 ? slope0 : (vs[j + 1] - vs[j - 1]) / (ks[j + 1] - ks[j - 1]);
            float m1 = j + 1 == n - 1 ? slope1 : (vs[j + 2] - vs[j]) / (ks[j + 2] - ks[j]);
            float h = ks[j + 1] - ks[j], t = Mathf.Clamp01((s - ks[j]) / h), t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * vs[j] + (t3 - 2f * t2 + t) * h * m0 + (-2f * t3 + 3f * t2) * vs[j + 1] + (t3 - t2) * h * m1;
        }

        // the gait at one speed, scaled down toward standing slower than a walk
        struct Step
        {
            public StrideLegs g;
            public float stance, sweep, ahead, land, load, push, heel, fold, drive, bob, point;
        }

        // the planted leg at s (0..1 of the stance): its knee, the foot (x ahead of the hip, y up), the hips' height it
        // holds, its thigh
        static void Planted(in Step k, float s, out float knee, out float x, out float y, out float hip, out float thigh)
        {
            knee = Curve(s, new[] { 0f, LoadAt, 1f }, new[] { k.land, k.load, k.push }, 0f, (k.push - k.load) / (1f - LoadAt));
            x = k.ahead - s * k.sweep;
            // the pointing foot turns about its ball, which stays on the grass: the ankle rises with it (a figure with no
            // foot measured: the heel just comes up strideHeelCm). The rise is the whole point's, though the foot shows only
            // OffShare of it by toe-off (the ball just leaving the grass), so the hips ride the same as before
            if (k.g.Foot > 0f)
            {
                float rest = k.g.FootDropDeg * Mathf.Deg2Rad, turn = k.point * Smooth((s - PointFrom) / (1f - PointFrom)) * Mathf.Deg2Rad;
                y = k.g.Ankle + k.g.Foot * (Mathf.Sin(rest + turn) - Mathf.Sin(rest));
            }
            else y = k.g.Ankle + k.heel * Smooth((s - PushFrom) / (1f - PushFrom));
            float l = Span(k.g, knee), r = Mathf.Clamp(x / l, -0.999f, 0.999f);
            hip = y + l * Mathf.Sqrt(1f - r * r);
            thigh = Mathf.Asin(r) * Mathf.Rad2Deg + AtHip(k.g, l);
        }

        // how fast the planted leg's knee (which 0) or thigh (1) turns at s, per share of the stance
        static float PlantedRate(in Step k, float s, int which)
        {
            float a = Mathf.Max(0f, s - Slope), b = Mathf.Min(1f, s + Slope);
            Planted(k, a, out float ka, out _, out _, out _, out float ta);
            Planted(k, b, out float kb, out _, out _, out _, out float tb);
            return ((which == 0 ? kb - ka : tb - ta)) / (b - a);
        }

        struct Leg { public bool planted; public float s, knee, thigh, hip, toe, ball; }

        // The pose at phase (0..1, the left foot touching down at 0, the right at 0.5) and speed (yd/s). cmPerYd: rig
        // cm a yard on the grass (the figure's scale).
        public static StridePose Pose(AnimConfig a, PlayersConfig pc, StrideLegs g, float phase, float speed, float cmPerYd, bool footFlex = true)
        {
            float amp = Mathf.Clamp01(speed / Mathf.Max(0.01f, a.strideWalkYdS));   // slower than a walk: smaller
            float leg = g.Thigh + g.Shin;
            float cycle = CycleYd(a, pc, speed) * cmPerYd;
            var k = new Step { g = g };
            k.stance = Mathf.Clamp(Gait(a.strideStance, speed, a, pc), 0.05f, 0.95f);
            float aheadShare = Mathf.Clamp(Gait(a.strideAhead, speed, a, pc), 0.05f, 0.95f);
            k.sweep = cycle * k.stance;
            float most = Gait(a.strideReach, speed, a, pc) * leg / Mathf.Max(aheadShare, 1f - aheadShare);
            if (k.sweep > most) { k.sweep = most; k.stance = Mathf.Max(0.05f, k.sweep / cycle); }   // a shorter touch, a longer flight
            k.sweep *= amp;
            k.ahead = k.sweep * aheadShare;
            float bend = g.Bend > 0f ? g.Bend : 1f;
            k.land = Gait(a.strideKneeLandDeg, speed, a, pc) * amp * bend;
            k.load = Gait(a.strideKneeDeg, speed, a, pc) * amp * bend;
            k.push = Gait(a.strideKneePushDeg, speed, a, pc) * amp * bend;
            k.fold = Gait(a.strideKneeFoldDeg, speed, a, pc) * amp;
            k.drive = Gait(a.strideThighDriveDeg, speed, a, pc) * amp;
            k.heel = Gait(a.strideHeelCm, speed, a, pc) * amp;
            k.bob = Gait(a.strideBobCm, speed, a, pc) * amp;
            float point = k.point = Gait(a.strideToeDeg, speed, a, pc) * amp;
            float strike = Gait(a.strideHeelStrikeDeg, speed, a, pc) * amp;
            // FEATURES.footFlex: the toes stay flat on the grass as the heel comes up (the foot bends at its ball), let go
            // after toe-off, and the foot lifts its toes in the swing to clear the grass
            float ball = footFlex ? point * Mathf.Clamp01(a.strideToeBendShare) : 0f;
            float lift = footFlex ? Gait(a.strideSwingToeUpDeg, speed, a, pc) * amp : 0f;

            // the stance's two ends, for the swing to carry on from and land on
            Planted(k, 1f, out float kOff, out _, out _, out float hOff, out float tOff);
            Planted(k, 0f, out float kOn, out _, out _, out float hOn, out float tOn);
            float r = (1f - k.stance) / k.stance;   // shares of the stance per share of the swing
            float kOffRate = PlantedRate(k, 1f, 0) * r, tOffRate = PlantedRate(k, 1f, 1) * r;
            float kOnRate = PlantedRate(k, 0f, 0) * r, tOnRate = PlantedRate(k, 0f, 1) * r;

            float air = (0.5f - k.stance) / (1f - k.stance);   // the share of a swing both feet are up, at each end
            var legs = new Leg[2];
            float hip = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                float u = Mathf.Repeat(phase + 0.5f * i, 1f);
                var l = new Leg();
                if (u < k.stance)
                {
                    l.planted = true;
                    l.s = u / k.stance;
                    Planted(k, l.s, out l.knee, out _, out _, out l.hip, out l.thigh);
                    float push = Smooth((l.s - PointFrom) / (1f - PointFrom));
                    // a toes-down landing (a sprint's) stays down and runs straight on into the push, never flat between
                    l.toe = strike < 0f ? Mathf.Lerp(strike, -point * OffShare, push)
                                        : -point * OffShare * push + strike * (1f - Smooth(l.s / StrikeTo));
                    l.ball = ball * OffShare * push;
                }
                else
                {
                    // the swing: heel up behind, the thigh driving through, the shin unfolding to land; the hips held
                    // by this leg go from toe-off to touch-down height, higher in the air
                    l.s = (u - k.stance) / (1f - k.stance);
                    l.knee = Curve(l.s, new[] { 0f, FoldAt, DriveAt, 1f }, new[] { kOff, k.fold, Mathf.Lerp(kOn, k.fold, UnfoldKeep), kOn }, kOffRate, kOnRate);
                    l.thigh = Curve(l.s, new[] { 0f, FoldThighAt, DriveAt, 1f }, new[] { tOff, Mathf.Lerp(tOff, k.drive, FoldDrive), k.drive, tOn }, tOffRate, tOnRate);
                    // up in the air while both feet are off the grass (a run): the start of this swing, and the end
                    float f = air <= 0f ? -1f : l.s < air ? l.s / air : l.s > 1f - air ? (l.s - (1f - air)) / air : -1f;
                    l.hip = Mathf.Lerp(hOff, hOn, Smooth(l.s)) + (f >= 0f ? k.bob * 4f * f * (1f - f) : 0f);
                    float down = Mathf.Max(air, 0f), held = 1f - 2f * down, m = held > 0f ? (l.s - down) / held : -1f;
                    if (m > 0f && m < 1f) l.hip += Release * leg * 16f * m * m * (1f - m) * (1f - m);
                    float pointed = l.s < PeakAt ? Mathf.Lerp(OffShare, 1f, Smooth(l.s / PeakAt)) : 1f - Smooth((l.s - PeakAt) / (ToeBackBy - PeakAt));
                    l.toe = -point * pointed + strike * Smooth((l.s - StrikeFrom) / (1f - StrikeFrom));
                    float up = (l.s - ToeBackBy) / (StrikeFrom - ToeBackBy);
                    if (up > 0f && up < 1f) l.toe += lift * 4f * up * (1f - up);
                    l.ball = ball * pointed;
                }
                hip = Mathf.Min(hip, l.hip);   // (as high as the lower leg holds him: no foot ends up under the grass)
                legs[i] = l;
            }

            var p = new StridePose { Drop = g.HipY - hip, ToeL = legs[0].toe, ToeR = legs[1].toe, BallL = legs[0].ball, BallR = legs[1].ball };
            Place(g, hip, legs[0], out p.ThighL, out p.ShinL);
            Place(g, hip, legs[1], out p.ThighR, out p.ShinR);

            // the arms against the legs: the left forward as the right leg comes through
            float w = Wave(phase + ArmLead);
            float fwd = Gait(a.strideArmFwdDeg, speed, a, pc) * amp, back = Gait(a.strideArmBackDeg, speed, a, pc) * amp;
            float c = (fwd + back) * 0.5f, half = (fwd - back) * 0.5f;
            p.ArmL = c + half * w; p.ArmR = c - half * w;
            float ef = Gait(a.strideElbowFrontDeg, speed, a, pc), eb = Gait(a.strideElbowBackDeg, speed, a, pc);
            p.ForeL = Mathf.Lerp(eb, ef, (w + 1f) * 0.5f); p.ForeR = Mathf.Lerp(eb, ef, (1f - w) * 0.5f);
            p.Twist = Gait(a.strideTwistDeg, speed, a, pc) * amp * w;
            return p;
        }

        // A leg's angles for the figure (its thigh, its shin turned from the thigh), the foot bent up onto the grass if
        // the hips sit lower than this leg would hold them.
        static void Place(StrideLegs g, float hip, Leg l, out float thigh, out float shin)
        {
            float t = l.thigh, kn = l.knee;
            float tr = t * Mathf.Deg2Rad, sr = (t - kn) * Mathf.Deg2Rad;
            float fx = g.Thigh * Mathf.Sin(tr) + g.Shin * Mathf.Sin(sr);
            float fy = hip - g.Thigh * Mathf.Cos(tr) - g.Shin * Mathf.Cos(sr);
            if (fy < g.Ankle) Reach(g, hip, fx, g.Ankle, out t, out kn);
            thigh = t - g.ThighRest;
            shin = (t - kn) - g.ShinRest - thigh;   // (the figure's shin turns from its thigh; its knee bends negative)
        }
    }
}
