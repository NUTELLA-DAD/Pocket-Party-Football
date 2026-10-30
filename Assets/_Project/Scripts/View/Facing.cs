using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // Which way a figure's body faces, once a sim tick (PlayersView draws it; Tests/EditMode/TurnAudit.cs measures it
    // over whole bot games without a window). On top of the momentum facing (Momentum.cs: toward where he runs, at a
    // limited rate):
    // - a locked block faces his man, a QB in the pocket faces down the field or where he aims (FEATURES.qbStrafe), and
    //   a QB letting the ball go faces where it goes;
    // - a man watching a pass turns his shoulders, then his head, then the rest of his body toward the ball
    //   (FEATURES.watchBall);
    // FEATURES.calmTurns (NO_CALM_TURNS=1): every one of those turns goes through the same turn rate as a run instead of
    // snapping in one tick, a ball straight behind a man doesn't flip him from one shoulder to the other, a run that
    // barely moves (a shuffle, a jostle in a block) doesn't swing him round after every wobble of its velocity, and the
    // feet step round with any turn on the spot (Pivot), so a body never spins over still feet.
    public struct FacingState
    {
        public MomentumState M;
        public float WasYaw;     // the body's facing a tick ago (NaN: none yet)
        public float Yaw;        // the body's facing now (Unity yaw degrees), the momentum facing plus Extra
        public float Extra;      // the body's extra turn after a ball behind him (FEATURES.watchBall), relative to M.Yaw
        public float Side;       // which shoulder a ball straight behind him is over (+1 / -1; 0: none yet)
        public float Twist, Head, Up;   // the look (FootballFigure.Look) while watching
        public bool Watching;
        public float Turned;     // how far the body turned this tick (degrees, 0 after being placed)
        public bool Placed;      // this tick it was placed (lined up, set down), its facing set outright
        public float Vx, Vz;     // FEATURES.calmTurns: the velocity the facing follows, smoothed over turnTauS
    }

    public static class Facing
    {
        public static bool Calm(GameConfig cfg) => cfg.Feature("calmTurns");

        // One sim tick. standYaw: its team's way of play; faceYaw: a facing that wins over the run's (NaN: none), turned
        // to at the turn rate; watch: following a pass at (bx, by, bz), eyesYd his eyes off the grass.
        public static void Step(ref FacingState s, GameConfig cfg, float x, float z, float vx, float vz, float standYaw, float faceYaw,
            bool watch, float bx, float by, float bz, float eyesYd, float dt)
        {
            var a = cfg.anim;
            bool calm = Calm(cfg);
            bool fresh = !s.M.Ready;
            s.WasYaw = fresh ? float.NaN : s.Yaw;
            // (a run that barely moves: its velocity smoothed, so a wobble in a jostle doesn't swing him round)
            float fx = vx, fz = vz;
            if (calm)
            {
                float k = a.turnTauS > 0f && !fresh ? 1f - Mathf.Exp(-dt / a.turnTauS) : 1f;
                s.Vx += (vx - s.Vx) * k; s.Vz += (vz - s.Vz) * k;
                float sp = Mathf.Sqrt(vx * vx + vz * vz), sm = Mathf.Sqrt(s.Vx * s.Vx + s.Vz * s.Vz);
                // (a real run is taken at once; only a slow one, under a jog, follows the smoothed way)
                float trust = Mathf.Clamp01((sp - a.turnTrustFromYdS) / Mathf.Max(0.1f, cfg.players.jogSpeedYdS - a.turnTrustFromYdS));
                if (sm > 1e-3f && sp > 1e-3f)
                {
                    float wx = Mathf.Lerp(s.Vx / sm, vx / sp, trust), wz = Mathf.Lerp(s.Vz / sm, vz / sp, trust);
                    float wl = Mathf.Sqrt(wx * wx + wz * wz);
                    if (wl > 1e-3f) { fx = wx / wl * sp; fz = wz / wl * sp; }
                }
            }
            bool placed = Momentum.Step(ref s.M, a, x, z, vx, vz, standYaw, cfg.players.sprintSpeedYdS, dt, faceYaw, fx, fz, calm ? a.turnMaxDegS : 0f);
            s.Placed = placed;
            if (placed) { s.Extra = 0f; s.Side = 0f; s.Vx = vx; s.Vz = vz; }

            // watching a pass: the shoulders, then the head, then the rest of the body (in lookStepDeg steps)
            s.Watching = watch;
            s.Twist = s.Head = 0f; s.Up = float.NaN;
            float want = 0f;
            if (watch)
            {
                float step = Mathf.Max(1f, a.lookStepDeg);
                float dx = bx - x, dz = bz - z, flat = Mathf.Sqrt(dx * dx + dz * dz);
                float rel = flat > 0.05f ? Mathf.DeltaAngle(s.M.Yaw, Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg) : 0f;
                if (calm)
                {
                    // a ball straight behind him stays over the shoulder it was over, until it is well round the other side
                    if (s.Side != 0f && Mathf.Abs(rel) > 180f - a.behindHoldDeg && Mathf.Sign(rel) != s.Side) rel += 360f * s.Side;
                    if (Mathf.Abs(rel) > 1f) s.Side = Mathf.Sign(rel);
                }
                rel = Mathf.Round(rel / step) * step;
                float reach = a.lookTwistDeg + a.lookHeadDeg;
                want = rel > reach ? rel - reach : rel < -reach ? rel + reach : 0f;
                // (the rest of the turn: at once, or at the turn rate)
                s.Extra = calm ? s.Extra + Turn(s.Extra, want, Mathf.Min(a.turnSlowDegS, a.turnMaxDegS) * dt) : want;
                float left = rel - s.Extra;
                s.Twist = Mathf.Clamp(left, -a.lookTwistDeg, a.lookTwistDeg);
                s.Head = Mathf.Clamp(left - s.Twist, -a.lookHeadDeg, a.lookHeadDeg);
                s.Up = Mathf.Round(Mathf.Atan2(by - eyesYd, Mathf.Max(flat, 0.3f)) * Mathf.Rad2Deg / step) * step;
            }
            else if (calm) s.Extra += Turn(s.Extra, 0f, Mathf.Min(a.turnSlowDegS, a.turnMaxDegS) * dt);
            else s.Extra = 0f;
            float yaw = s.M.Yaw + s.Extra;
            if (calm && !placed && !float.IsNaN(s.WasYaw) && a.turnMaxDegS > 0f)
            {
                // the run's turn and the watch's together turn no faster than turnMaxDegS: the watch gives way
                float d = Mathf.DeltaAngle(s.WasYaw, yaw), most = a.turnMaxDegS * dt;
                if (Mathf.Abs(d) > most) { s.Extra -= d - Mathf.Sign(d) * most; yaw = s.M.Yaw + s.Extra; }
            }
            s.Yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            s.Turned = placed || float.IsNaN(s.WasYaw) ? 0f : Mathf.DeltaAngle(s.WasYaw, s.Yaw);
        }

        static float Turn(float from, float to, float rate) => Mathf.Clamp(to - from, -rate, rate);

        // FEATURES.calmTurns: a turn on the spot steps the feet round (one step every turnStepDeg), as a share of a whole
        // stride (two steps): the stride phase moves by this as well as by the distance run.
        public static float Pivot(GameConfig cfg, float turnedDeg) =>
            Calm(cfg) && cfg.anim.turnStepDeg > 0f ? 0.5f * Mathf.Abs(turnedDeg) / cfg.anim.turnStepDeg : 0f;

        // FEATURES.qbStrafe (NO_QB_STRAFE=1): where the QB faces with the ball in the pocket, before he throws: where his
        // throw stick aims (aimX, aimZ in field yards; NaN: not aiming), else straight down the field. While the ball
        // leaves his hand (throwing), where it is going. NaN: no say (a handoff play, past the line, after the throw).
        public static float QbYaw(World w, Player p, bool throwing, float aimX, float aimZ)
        {
            if (!w.Cfg.Feature("qbStrafe") || w.Phase != Phase.Live) return float.NaN;
            var b = w.Ball;
            if (throwing && b.State == BallState.Flying && !w.Kick.Kicked) return Momentum.YawOf(b.TargetX - p.X, b.TargetZ - p.Z);
            if (!InPocket(w, p)) return float.NaN;
            if (!float.IsNaN(aimX) && aimX * aimX + aimZ * aimZ > 1e-4f) return Momentum.YawOf(aimX, aimZ);
            return Momentum.YawOf(w.Dir, 0f);
        }

        // The QB with the ball behind the line on a pass play, not yet thrown.
        public static bool InPocket(World w, Player p)
        {
            if (w.Phase != Phase.Live || p != LivePlay.Qb(w) || LivePlay.Carrier(w) != p || w.Thrown) return false;
            var play = LivePlay.Called(w, w.Offense);
            if (play != null && play.runner >= 0) return false;
            return (p.X - w.LineOfScrimmageX) * w.Dir <= 0f;   // (past the line he's a runner)
        }

        // FEATURES.qbStrafe: a QB moving while he faces another way strafes: running backward from where he faces, his
        // stride runs backward too (a backpedal), so his feet never moonwalk. +1 forward, -1 backward.
        public static float StrideWay(float yaw, float vx, float vz)
        {
            if (vx * vx + vz * vz < 1e-4f) return 1f;
            return Mathf.Abs(Mathf.DeltaAngle(yaw, Momentum.YawOf(vx, vz))) > 100f ? -1f : 1f;
        }

        // FEATURES.qbStrafe: how far his legs turn from where he faces (faceYaw) toward where he runs: none running
        // straight ahead, straight back or straight across (a backpedal, a shuffle), most (strafeHipDeg) on the
        // diagonals, so it never flips from one side to the other; nothing standing.
        public static float StrafeLegs(GameConfig cfg, float faceYaw, float vx, float vz)
        {
            float speed = Mathf.Sqrt(vx * vx + vz * vz);
            if (speed < 1e-3f) return 0f;
            float rel = Mathf.DeltaAngle(faceYaw, Momentum.YawOf(vx, vz)) * Mathf.Deg2Rad;
            float moving = Mathf.Clamp01((speed - 0.5f) / Mathf.Max(0.1f, cfg.anim.strideWalkYdS));
            return cfg.anim.strafeHipDeg * Mathf.Sin(rel + rel) * moving;
        }

        // FEATURES.reachTrack: how far above level (degrees) the ball at (bx, by, bz) is from the shoulders of a man at
        // (x, z), shoulderYd off the grass, stepped to lookStepDeg like the look; FootballFigure.ReachAt points the arms there.
        public static float ReachUp(AnimConfig a, float x, float z, float shoulderYd, float bx, float by, float bz)
        {
            float dx = bx - x, dz = bz - z, flat = Mathf.Sqrt(dx * dx + dz * dz);
            float step = Mathf.Max(1f, a.lookStepDeg);
            return Mathf.Round(Mathf.Atan2(by - shoulderYd, Mathf.Max(flat, 0.3f)) * Mathf.Rad2Deg / step) * step;
        }
    }
}
