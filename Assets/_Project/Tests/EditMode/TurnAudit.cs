using System;
using System.Collections.Generic;
using System.Text;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // The turn gauntlet: plays whole bot games headless and runs every man's facing through the same steps PlayersView
    // draws with (Facing.cs, Momentum.cs, the stride's phase), once a sim tick, then counts what looks wrong:
    // - snaps: the body turning more than SnapDeg in one tick (a turn faster than SnapDeg * 30 a second);
    // - spins: SpinDeg or more of turning inside SpinTicks (a fifth of a second);
    // - skates: the body turning SkateDeg or more in a tick while the feet don't step at all;
    // - the QB: ticks in the pocket facing more than BackDeg away from where he should (down the field), and at the throw
    //   how far his body is off the way the ball goes.
    // No NUnit and no Unity here, so tools/turn-audit runs it outside Unity too (TurnAuditTests holds the limits).
    public sealed class TurnAudit
    {
        public const float SnapDeg = 20f, SpinDeg = 120f, SkateDeg = 6f, BackDeg = 100f;
        public const int SpinTicks = 6;

        public int Ticks, Snaps, Spins, Skates, PocketTicks, PocketBack, Throws;
        public float MaxTurn, ThrowOffSum, ThrowOffMax;
        public readonly SortedDictionary<string, int> SnapsBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> SpinsBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> SkatesBy = new SortedDictionary<string, int>(StringComparer.Ordinal);

        sealed class Man
        {
            public FacingState F;
            public float Phase, X, Z;
            public bool Seen;
            public readonly float[] Turns = new float[SpinTicks];
            public readonly float[] Steps = new float[SpinTicks];
            public int SpinCool, ThrowTick = -1;
            public float LastShown;
        }

        readonly SortedDictionary<int, Man> men = new SortedDictionary<int, Man>();
        readonly Moves moves = new Moves();

        public static TurnAudit Run(GameConfig cfg, int seed, int seconds)
        {
            var w = new World(cfg, seed);
            for (int i = 0; i < cfg.players.perTeam * cfg.teams.Count; i++) w.AddPlayer(i, i % cfg.teams.Count, false);
            var audit = new TurnAudit();
            for (int t = 0; t < seconds * cfg.sim.tickHz; t++) { w.Step(null); audit.Tick(w); }
            return audit;
        }

        static void Add(SortedDictionary<string, int> d, string k) => d[k] = (d.TryGetValue(k, out int n) ? n : 0) + 1;

        // What the man was doing, for the report.
        static string Why(World w, Player p, MoveNow move, bool watch, bool qb)
        {
            if (qb) return "qb";
            if (move.Kind == Move.Block) return move.Locked ? "block-locked" : "block";
            if (watch) return "watching";
            if (move.Kind != Move.None) return move.Kind.ToString().ToLowerInvariant();
            float sp = (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
            if (w.Phase != Phase.Live) return "dead-ball";
            return sp < w.Cfg.players.jogSpeedYdS ? "slow-run" : "run";
        }

        public void Tick(World w)
        {
            var cfg = w.Cfg;
            moves.Observe(w);
            bool calm = Facing.Calm(cfg);
            float eyes = cfg.view.playerHeightM * 0.8f / cfg.field.yardM;
            foreach (var p in w.Players)
            {
                if (!men.TryGetValue(p.Slot, out var m)) men[p.Slot] = m = new Man { X = p.X, Z = p.Z };
                var move = moves.Of(w, p);
                bool down = move.Kind == Move.Tackle || move.Kind == Move.Tackled || move.Kind == Move.Tangle;
                float speed = (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
                float dir = w.Dir * (p.Team == w.Offense ? 1f : -1f);
                float face = Facing.QbYaw(w, p, move.Kind == Move.Throw, float.NaN, float.NaN);
                bool qb = !float.IsNaN(face);
                float lockYaw = float.NaN;
                if (move.Kind == Move.Block && move.Locked && w.Find(move.Partner) is Player q)
                {
                    moves.LockOffset(q.Slot, 1f, out float qx, out float qz);
                    moves.LockOffset(p.Slot, 1f, out float ox, out float oz);
                    float dx = q.X + qx - p.X - ox, dz = q.Z + qz - p.Z - oz;
                    if (dx * dx + dz * dz > 1e-4f) lockYaw = Momentum.YawOf(dx, dz);
                }
                if (qb && move.Kind != Move.Throw) face += Facing.StrafeLegs(cfg, face, p.VX, p.VZ);
                if (calm && !float.IsNaN(lockYaw)) face = lockYaw;
                bool watch = !down && moves.Watching(p.Slot);
                Facing.Step(ref m.F, cfg, p.X, p.Z, p.VX, p.VZ, Momentum.YawOf(dir, 0f), face, watch, w.Ball.X, w.Ball.Y, w.Ball.Z, eyes, w.Dt);
                // (the old way: a locked block faced his man outright, over the momentum facing)
                float yaw = !calm && !float.IsNaN(lockYaw) && !watch ? lockYaw : m.F.Yaw;
                float turned = m.Seen && !m.F.Placed && !down ? UnityEngine.Mathf.DeltaAngle(m.LastShown, yaw) : 0f;
                m.LastShown = yaw;

                // the feet: the stride's phase, as PlayersView moves it
                float moved = (float)Math.Sqrt((p.X - m.X) * (p.X - m.X) + (p.Z - m.Z) * (p.Z - m.Z));
                if (moved > 1.5f + speed * w.Dt) moved = 0f;   // placed, not run
                m.X = p.X; m.Z = p.Z;
                bool locked = move.Kind == Move.Block && move.Locked;
                float stepMoved = moved;
                if (!down && !locked && (move.Kind == Move.None || move.Kind == Move.Reach || move.Kind == Move.Secure))
                {
                    Momentum.Lean(m.F.M, cfg.anim, out float fwd, out float side, out float plant);
                    if (plant > 0f && speed < cfg.anim.strideWalkYdS) stepMoved = 0f;
                }
                float step = 0f;
                bool striding = cfg.Feature("strideCycle") && !down && speed > 0.5f;
                if (striding) step += Math.Abs(stepMoved / Stride.CycleYd(cfg.anim, cfg.players, speed));
                if (!down) step += Facing.Pivot(cfg, turned);
                m.Phase += step;

                if (m.Seen && !down)
                {
                    Ticks++;
                    string why = Why(w, p, move, watch, qb);
                    float at = Math.Abs(turned);
                    if (at > MaxTurn) MaxTurn = at;
                    if (at > SnapDeg) { Snaps++; Add(SnapsBy, why); }
                    if (at >= SkateDeg && step <= 1e-4f) { Skates++; Add(SkatesBy, why); }
                    Array.Copy(m.Turns, 1, m.Turns, 0, SpinTicks - 1); m.Turns[SpinTicks - 1] = turned;
                    float sum = 0f; foreach (float v in m.Turns) sum += v;
                    if (m.SpinCool > 0) m.SpinCool--;
                    else if (Math.Abs(sum) >= SpinDeg) { Spins++; Add(SpinsBy, why); m.SpinCool = SpinTicks; }
                    if (Facing.InPocket(w, p))
                    {
                        PocketTicks++;
                        if (Math.Abs(UnityEngine.Mathf.DeltaAngle(yaw, Momentum.YawOf(w.Dir, 0f))) > BackDeg) PocketBack++;
                    }
                    if (move.Kind == Move.Throw && w.Ball.State == BallState.Flying && p.Slot == LivePlay.Qb(w)?.Slot && m.ThrowTick != move.StartTick)
                    {
                        m.ThrowTick = move.StartTick;
                        float off = Math.Abs(UnityEngine.Mathf.DeltaAngle(yaw, Momentum.YawOf(w.Ball.TargetX - p.X, w.Ball.TargetZ - p.Z)));
                        Throws++; ThrowOffSum += off; if (off > ThrowOffMax) ThrowOffMax = off;
                    }
                }
                else Array.Clear(m.Turns, 0, SpinTicks);
                m.Seen = true;
            }
        }


        public string Report()
        {
            var sb = new StringBuilder();
            sb.Append(Ticks).Append(" man-ticks: snaps ").Append(Snaps).Append(", spins ").Append(Spins).Append(", skates ").Append(Skates)
              .Append(", fastest turn ").Append(MaxTurn.ToString("0")).Append(" deg in a tick");
            sb.Append("; QB in the pocket facing away ").Append(PocketBack).Append('/').Append(PocketTicks)
              .Append(", throws ").Append(Throws).Append(" off by ").Append((ThrowOffSum / Math.Max(1, Throws)).ToString("0")).Append(" deg on average (worst ").Append(ThrowOffMax.ToString("0")).Append(')');
            void List(string name, SortedDictionary<string, int> d)
            {
                if (d.Count == 0) return;
                sb.Append("\n  ").Append(name).Append(':');
                foreach (var kv in d) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
            }
            List("snaps", SnapsBy); List("spins", SpinsBy); List("skates", SkatesBy);
            return sb.ToString();
        }
    }
}
