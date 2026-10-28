using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // One chibi figure per player (FootballFigure) and the ball. Reads the sim, never writes it.
    // Positions are blended between the last two sim ticks so 30 Hz data moves smoothly at 60 fps. A figure faces the
    // way it runs (standing: its team's direction of play), straight down the field (FEATURES.faceDownfield: turned
    // only anim.cameraTurnDeg toward the camera; off: 42 degrees, so faces and numbers show); running steps between
    // two run frames by distance covered (stepped, no time-based wobble); FEATURES.strideCycle (Stride.cs): a whole
    // stride instead, every in-between, its phase moved by the distance run.
    // On top of that, the moves (Moves.cs): throws, catches, jukes, tackles... A man on the grass after a tackle stays
    // where and how he fell until the next huddle, though the sim may already be walking him back.
    // FEATURES.momentumLean (Momentum.cs): the facing turns at a limited rate instead of snapping, and a running figure
    // leans into its speeding up, slowing and cuts; a hard brake plants the lead leg and leans back the new way.
    public sealed class PlayersView
    {
        const float TurnToCamera = 42f;     // degrees a figure turns toward the camera without FEATURES.faceDownfield
        const float StrideM = 0.9f;         // metres per run frame

        readonly GameConfig cfg;
        readonly Material mat;
        readonly Transform root;
        readonly Dictionary<int, FootballFigure> bodies = new Dictionary<int, FootballFigure>();
        readonly Dictionary<int, string> kitOf = new Dictionary<int, string>();
        readonly Dictionary<int, ModelSkin> skins = new Dictionary<int, ModelSkin>();
        readonly Dictionary<int, Vector2> prev = new Dictionary<int, Vector2>();
        readonly Dictionary<int, float> travelled = new Dictionary<int, float>();
        readonly Dictionary<int, float> stridePhase = new Dictionary<int, float>();
        readonly Dictionary<int, int> strideLast = new Dictionary<int, int>();   // the last tick each man was striding
        readonly Dictionary<int, (float speed, int tick)> strideShown = new Dictionary<int, (float, int)>();   // the speed his stride is drawn at
        const float StrideShrinkTicks = 6f;   // a walk's stride shrinks to nothing over this many ticks at the most
        readonly Dictionary<int, MomentumState> carry = new Dictionary<int, MomentumState>();
        readonly Dictionary<int, int> carryTick = new Dictionary<int, int>();
        // FEATURES.smoothMotion: the facing a tick ago, so a turn blends across the frames between ticks (NaN: none yet)
        readonly Dictionary<int, float> yawWas = new Dictionary<int, float>();
        readonly List<int> gone = new List<int>();
        public readonly Moves Moves = new Moves();
        // a man on the grass: where he went down, his facing, which fall (slot -> start tick)
        readonly Dictionary<int, Vector3> downAt = new Dictionary<int, Vector3>();
        readonly Dictionary<int, float> downYaw = new Dictionary<int, float>();
        readonly Dictionary<int, Vector3> downFrom = new Dictionary<int, Vector3>();
        readonly Dictionary<int, int> downStart = new Dictionary<int, int>();
        readonly Transform ball, ballShadow;
        Vector3 ballPrev;
        // the ball in the air (FEATURES.ballSpin): the tick it went up, and whether it was kicked
        bool flying, kicked;
        int flightTick;

        // Photo scenes (and later the play view) can hold a pose for a player: slot -> pose.
        public readonly Dictionary<int, FootballFigure.PoseKind> PoseOverride = new Dictionary<int, FootballFigure.PoseKind>();
        public readonly Dictionary<int, int> PoseFrameOverride = new Dictionary<int, int>();   // (that pose's key frame; none: 0)
        // ...and give a player an extra football to hold (slot -> true: right hand, false: left), the sim's ball aside
        public readonly Dictionary<int, bool> PropBalls = new Dictionary<int, bool>();
        readonly Dictionary<int, Transform> props = new Dictionary<int, Transform>();

        public PlayersView(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = FootballFigure.KitMaterial(Looks.Toon(baseMat), cfg.view);
            root = new GameObject("Players").transform;
            ball = FootballFigure.MakeBall(null, mat, cfg.view.ballLengthM, cfg.Feature("modelBall")).transform;
            // a dark spot on the grass under a ball in the air: shows how high it is and where it's going
            ballShadow = Looks.Flat(null, "BallShadow", Vector3.zero, new Vector2(cfg.view.ballLengthM, cfg.view.ballLengthM * 0.6f), new Color(0.12f, 0.22f, 0.1f), mat).transform;
        }

        public int Count => bodies.Count;

        public FootballFigure Figure(int slot) => bodies.TryGetValue(slot, out var f) ? f : null;

        // The facing and lean state a player's figure carries (FEATURES.momentumLean), for the motion reel.
        public bool MomentumOf(int slot, out MomentumState s) => carry.TryGetValue(slot, out s);
        // FEATURES.strideCycle: where a man is in his stride (0..1, the left foot down at 0), -1 if he hasn't run yet
        public float StridePhaseOf(int slot) => stridePhase.TryGetValue(slot, out float ph) ? ph : -1f;

        // Call just before World.Step: remembers where everyone was, for the blend.
        public void BeforeStep(World w)
        {
            Moves.Observe(w);
            prev.Clear();
            foreach (var p in w.Players) prev[p.Slot] = new Vector2(p.X, p.Z);
            ballPrev = new Vector3(w.Ball.X, w.Ball.Y, w.Ball.Z);
        }

        // The blend the last Render drew at (0 = the previous tick, 1 = the latest), for the camera's follow.
        public float Alpha { get; private set; } = 1f;

        // Where a player is drawn this frame (yd), between the last two ticks like his figure.
        public float ShownX(Player p) => prev.TryGetValue(p.Slot, out var v) ? Mathf.Lerp(v.x, p.X, Alpha) : p.X;
        public float BallShownX(World w) => Mathf.Lerp(ballPrev.x, w.Ball.X, Alpha);

        // alpha: 0 = the previous tick, 1 = the latest.
        public void Render(World w, float alpha)
        {
            Alpha = alpha;
            bool smooth = cfg.Feature("smoothMotion");
            float yd = cfg.field.yardM;
            Moves.Observe(w);
            foreach (var p in w.Players)
            {
                var fig = Body(w, p);
                var move = PoseOverride.ContainsKey(p.Slot) ? default : Moves.Of(w, p);
                bool down = move.Kind == Move.Tackle || move.Kind == Move.Tackled;
                Vector2 now = new Vector2(p.X, p.Z);
                Vector2 was = prev.TryGetValue(p.Slot, out var v) ? v : now;
                Vector2 at = Vector2.Lerp(was, now, alpha);
                Moves.LockOffset(p.Slot, alpha, out float ox, out float oz);   // a locked pair slides together (FEATURES.blockLock)
                at += new Vector2(ox, oz);
                var t = fig.transform;
                var pos = new Vector3(at.x * yd, 0f, at.y * yd);
                float moved = (pos - new Vector3(t.localPosition.x, 0f, t.localPosition.z)).magnitude;   // (along the grass: a jump is no stride)

                // facing: the way it runs, else its team's direction of play; turned toward the camera by turn
                float speed = Mathf.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
                float dir = w.Dir * (p.Team == w.Offense ? 1f : -1f);
                Vector2 face = speed > 0.5f ? new Vector2(p.VX, p.VZ).normalized : new Vector2(dir, 0f);
                float turn = CameraTurn(cfg, false);
                bool momentum = cfg.Feature("momentumLean") && !PoseOverride.ContainsKey(p.Slot);
                var ms = default(MomentumState);
                if (momentum)
                {
                    // once a sim tick (the view renders the same tick more than once)
                    carry.TryGetValue(p.Slot, out ms);
                    if (!carryTick.TryGetValue(p.Slot, out int seen) || seen != w.Tick)
                    {
                        yawWas[p.Slot] = ms.Ready ? ms.Yaw : float.NaN;
                        Momentum.Step(ref ms, cfg.anim, p.X, p.Z, p.VX, p.VZ, Momentum.YawOf(dir, 0f), cfg.players.sprintSpeedYdS, w.Dt);
                        carry[p.Slot] = ms; carryTick[p.Slot] = w.Tick;
                    }
                    // the facing turns once a tick (up to 30 degrees at a time); smoothMotion blends it across the frames between
                    float shownYaw = smooth && yawWas.TryGetValue(p.Slot, out float yw) && !float.IsNaN(yw) ? Mathf.LerpAngle(yw, ms.Yaw, alpha) : ms.Yaw;
                    float r = shownYaw * Mathf.Deg2Rad;
                    face = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
                }
                if (move.Kind == Move.Block && move.Locked && w.Find(move.Partner) is Player q)
                {
                    // locked arms: square up to him, turned only a little toward the camera so the hands meet
                    Moves.LockOffset(q.Slot, alpha, out float qx, out float qz);
                    var them = new Vector2(q.X + qx, q.Z + qz) - (now + new Vector2(ox, oz));
                    if (them.sqrMagnitude > 1e-4f) { face = them.normalized; turn = CameraTurn(cfg, true); }
                }
                float yaw = Mathf.Atan2(-face.y, face.x) * Mathf.Rad2Deg;
                yaw = TowardCamera(yaw, turn);
                fig.Look(0f, 0f, float.NaN);
                bool watching = !down && !PoseOverride.ContainsKey(p.Slot) && Moves.Watching(p.Slot);
                if (watching) yaw = WatchBall(w, p, fig, yaw);
                if (down)
                {
                    // stays where and how he went down
                    if (!downStart.TryGetValue(p.Slot, out int st) || st != move.StartTick)
                    {
                        downFrom[p.Slot] = pos;
                        if (cfg.Feature("tackleContact") && move.HasPartner) Contact(w, move, at, alpha, ref pos, ref yaw);
                        downStart[p.Slot] = move.StartTick; downAt[p.Slot] = pos; downYaw[p.Slot] = yaw;
                    }
                    // the tackler's launch (the first key) is halfway in to the carrier, then he's on him
                    pos = move.Kind == Move.Tackle && move.Frame == 0 && !move.GettingUp ? Vector3.Lerp(downFrom[p.Slot], downAt[p.Slot], 0.5f) : downAt[p.Slot];
                    pos += new Vector3(move.KnockX * yd, 0f, move.KnockZ * yd);   // FEATURES.hitAngle: a big hit drives them back
                    yaw = downYaw[p.Slot]; moved = 0f;
                }
                else
                {
                    downStart.Remove(p.Slot);
                    pos.y += DefenseMovesView.LiftYd(w, p) * yd;   // a defender's JUMP leaves the grass
                }
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);
                fig.Carry(0f, 0f, 0f, 0f, 0f);
                fig.Stride(false, default); fig.Settle(0f);

                if (PoseOverride.TryGetValue(p.Slot, out var held)) { fig.Idle(0f, 0f); fig.Pose(held, PoseFrameOverride.TryGetValue(p.Slot, out int hf) ? hf : 0); continue; }
                travelled[p.Slot] = (travelled.TryGetValue(p.Slot, out float d) ? d : 0f) + moved;
                float strideMoved = moved;   // (the stride holds only in a plant slower than a walk: braking on the move, the feet keep stepping)
                // (with the momentum lean a sprint takes longer strides than a jog)
                float stride = momentum ? StrideM * (0.8f + 0.4f * Mathf.Clamp01(speed / cfg.players.sprintSpeedYdS)) : StrideM;
                int frame = (int)(travelled[p.Slot] / stride) % 2;
                bool carrier = w.Ball.State == BallState.Held && w.Ball.Carrier == p.Slot;
                bool striding = cfg.Feature("strideCycle") && !down && speed > 0.5f;
                float phase = stridePhase.TryGetValue(p.Slot, out float ph) ? ph : 0f;
                var dk = FootballFigure.PoseKind.Ready; int df = 0;
                bool defMove = !down && DefenseMovesView.PoseFor(w, p, out dk, out df);
                // a man with nothing else on: his head follows the ball (FEATURES.headTrack), and standing he breathes
                // and shifts his weight (FEATURES.idleMotion)
                bool free = !down && !defMove && move.Kind == Move.None;
                if (free && !carrier && !watching)
                {
                    BallAt(w, out float bx, out float by, out float bz);
                    if (Alive.HeadTrack(cfg, yaw, p.X, p.Z, bx, by, bz, out float hy, out float tip)) fig.Track(hy, tip);
                }
                float breath = 0f, shift = 0f;
                if (free && speed <= 0.5f) Alive.Idle(cfg, Alive.Key(cfg, w.Tick, p.Slot), out breath, out shift);
                fig.Idle(breath, shift);
                if (defMove) { fig.Pose(dk, df); continue; }   // WS-03 defender moves
                bool locked = move.Kind == Move.Block && move.Locked;
                if (momentum && !down && !locked && (move.Kind == Move.None || move.Kind == Move.Reach || move.Kind == Move.Secure))
                {
                    // the lean: leanFeetShare tips the whole body over its feet, the rest bends the chest
                    Momentum.Lean(ms, cfg.anim, out float fwd, out float side, out float plant);
                    // planted (or leaning hard nearly stopped): the feet stay put, no pedalling in place
                    if (plant > 0f || (speed < 2f && Mathf.Abs(fwd) + Mathf.Abs(side) >= 8f)) { frame = 0; moved = 0f; if (plant > 0f && speed < cfg.anim.strideWalkYdS) strideMoved = 0f; }
                    // the speed lean bends the spine (a sprint's silhouette), the rest of the lean tips him over his feet,
                    // less so in a plant, where the bent legs take it
                    float run = Mathf.Clamp01(speed / cfg.players.sprintSpeedYdS);
                    float spine = cfg.anim.speedLeanDeg * run * run;
                    float feet = Mathf.Clamp01(cfg.anim.leanFeetShare) * (plant > 0f ? 0.6f : 1f);
                    float tip = (fwd - spine) * feet;
                    t.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(side * feet, 0f, -tip);
                    float pump = Mathf.Clamp01((speed - cfg.players.jogSpeedYdS) / Mathf.Max(0.1f, cfg.players.sprintSpeedYdS - cfg.players.jogSpeedYdS));
                    fig.Carry(fwd - tip, side * (1f - feet), fwd, move.Kind == Move.None ? plant : 0f, cfg.anim.cutSpreadDeg, -side * cfg.anim.cutTwistShare, pump);
                    // planted in the middle of a turn-around: no stand-up pose for a tick
                    if (move.Kind == Move.None && speed <= 0.5f && plant > 0f)
                    { fig.Idle(0f, 0f); fig.Pose(carrier ? FootballFigure.PoseKind.Carry : FootballFigure.PoseKind.Run, frame); continue; }
                }
                // the stride (FEATURES.strideCycle): its phase moves with the distance run (held through a plant)
                if (striding)
                {
                    phase = Stride.Advance(phase, cfg.anim, cfg.players, speed, strideMoved / yd);
                    stridePhase[p.Slot] = phase;
                    // braking hard (a stop takes a tick or two) a stride smaller than a walk's shrinks over StrideShrinkTicks, not at once,
                    // so the last stride pose the stop eases from still has bent legs
                    float shown = speed;
                    if (strideShown.TryGetValue(p.Slot, out var drawn))
                        shown = Mathf.Max(speed, Mathf.Min(drawn.speed, cfg.anim.strideWalkYdS) * (1f - (w.Tick - drawn.tick) / StrideShrinkTicks));
                    if (drawn.tick != w.Tick) strideShown[p.Slot] = (shown, w.Tick);
                    fig.Stride(true, Stride.Pose(cfg.anim, cfg.players, fig.Legs, phase, shown, yd * FootballFigure.RigHeight / cfg.view.playerHeightM, cfg.Feature("footFlex")));
                    strideLast[p.Slot] = w.Tick;
                }
                else if (!down && move.Kind == Move.None && strideLast.TryGetValue(p.Slot, out int last))
                {
                    // just stopped: the last stride eases into the standing pose
                    float left = 1f - (w.Tick - last - 1 + alpha) / Mathf.Max(1, cfg.anim.strideSettleTicks);
                    if (left > 0f) fig.Settle(left * left * (3f - 2f * left)); else strideLast.Remove(p.Slot);
                }
                if (PoseFor(move, frame, out var kind, out int f)) fig.Pose(kind, f);
                else if (speed > 0.5f) fig.Pose(carrier ? FootballFigure.PoseKind.Carry : FootballFigure.PoseKind.Run, frame);
                else fig.Pose(p.Pos == "OL" || p.Pos == "DL" ? FootballFigure.PoseKind.Stance : FootballFigure.PoseKind.Ready, 0);
            }
            Eyes(w);
            // the ball: in the carrier's right hand, else where the sim has it, its long axis along its flight
            var b = w.Ball;
            Vector3 bnow = new Vector3(b.X, b.Y, b.Z);
            Vector3 bat = Vector3.Lerp(ballPrev, bnow, alpha);
            var holder = b.State == BallState.Held ? Figure(b.Carrier) : null;
            // the snap (Snap.cs) places the ball itself before the hike and on its way back to the QB
            bool snapping = SnapBall(w, alpha, holder);
            // the hand with the ball in it closes round it (FEATURES.handCurl, ModelSkin)
            foreach (var body in bodies.Values) body.HoldL = body.HoldR = false;
            if (!snapping && holder != null)
            {
                holder.HoldR = true;
                ball.position = holder.HandR.position;
                ball.rotation = holder.HandR.rotation * Quaternion.Euler(0, 90, 0);
            }
            else if (!snapping)
            {
                ball.localPosition = new Vector3(bat.x * yd, bat.y * yd + cfg.view.ballWidthM * 0.5f, bat.z * yd);
                var bv = new Vector3(b.VX, b.State == BallState.Flying ? b.VY : 0f, b.VZ);
                if (bv.sqrMagnitude > 0.01f) ball.rotation = Quaternion.LookRotation(bv.normalized, Vector3.up);
                if (b.State == BallState.Flying && bv.sqrMagnitude > 0.01f)
                {
                    // the spiral turns it about its long axis (+z), a kick end over end about its width (+x)
                    if (!flying || w.Tick < flightTick) { flightTick = w.Tick; kicked = w.Kick.Kicked && !w.Kick.Landed; }
                    int keys = (int)((w.Tick - flightTick) * cfg.anim.fps / Mathf.Max(1, cfg.sim.tickHz));
                    float spin = Alive.Spin(cfg, keys, kicked);
                    ball.rotation *= kicked ? Quaternion.Euler(spin, 0f, 0f) : Quaternion.Euler(0f, 0f, spin);
                }
            }
            flying = b.State == BallState.Flying;
            foreach (var kv in PropBalls)
            {
                var f = Figure(kv.Key);
                if (f == null) continue;
                if (!props.TryGetValue(kv.Key, out var prop)) props[kv.Key] = prop = FootballFigure.MakeBall(null, mat, cfg.view.ballLengthM, cfg.Feature("modelBall")).transform;
                var hand = kv.Value ? f.HandR : f.HandL;
                if (kv.Value) f.HoldR = true; else f.HoldL = true;
                prop.position = hand.position;
                prop.rotation = hand.rotation * Quaternion.Euler(0, 90, 0);
            }
            bool inAir = b.State == BallState.Flying;
            ballShadow.gameObject.SetActive(inAir);
            if (inAir) ballShadow.localPosition = new Vector3(bat.x * yd, 0.02f, bat.z * yd);
            gone.Clear();
            foreach (var kv in bodies) if (w.Find(kv.Key) == null) gone.Add(kv.Key);
            foreach (int slot in gone) { Object.Destroy(bodies[slot].gameObject); bodies.Remove(slot); kitOf.Remove(slot); skins.Remove(slot); carry.Remove(slot); carryTick.Remove(slot); yawWas.Remove(slot); stridePhase.Remove(slot); strideLast.Remove(slot); strideShown.Remove(slot); downStart.Remove(slot); downFrom.Remove(slot); }
        }

        // A tackle that meets (Moves.TackleContact): the tackler goes down against the carrier, facing him; the carrier
        // goes down along the hit, over backwards toward the tackler's side or on his front away from it.
        void Contact(World w, MoveNow move, Vector2 at, float alpha, ref Vector3 pos, ref float yaw)
        {
            var other = w.Find(move.Partner);
            if (other == null) return;
            Vector2 onow = new Vector2(other.X, other.Z);
            Vector2 oat = Vector2.Lerp(prev.TryGetValue(other.Slot, out var ov) ? ov : onow, onow, alpha);
            bool tackler = move.Kind == Move.Tackle;
            Vector2 t = tackler ? at : oat, c = tackler ? oat : at;
            float contactYd = cfg.view.tackleContactM / cfg.field.yardM;
            if (!Moves.TackleContact(t.x, t.y, c.x, c.y, contactYd, out float x, out float z, out float ux, out float uz)) return;
            if (tackler) { pos = new Vector3(x * cfg.field.yardM, 0f, z * cfg.field.yardM); yaw = Mathf.Atan2(-uz, ux) * Mathf.Rad2Deg; }
            else yaw = move.OnBack ? Mathf.Atan2(uz, -ux) * Mathf.Rad2Deg : Mathf.Atan2(-uz, ux) * Mathf.Rad2Deg;
        }

        // The figure's pose for a move (false: none, run or stand as usual). stride: the run frame.
        public static bool PoseFor(MoveNow m, int stride, out FootballFigure.PoseKind kind, out int frame)
        {
            frame = 0;
            kind = FootballFigure.PoseKind.Ready;
            switch (m.Kind)
            {
                // FEATURES.throwMotion: wound up, then four keys from the release (NO_THROW_MOTION=1: the two-key throw)
                case Move.Aim: kind = FootballFigure.PoseKind.Throw; frame = m.Full ? 1 : 0; return true;
                case Move.Throw: kind = FootballFigure.PoseKind.Release; frame = m.Full ? 2 + Mathf.Min(3, m.Frame) : Mathf.Min(1, m.Frame / 2); return true;
                case Move.KickSet: kind = FootballFigure.PoseKind.KickSet; frame = m.Frame; return true;
                case Move.Kick: kind = FootballFigure.PoseKind.Kick; frame = Mathf.Min(4, m.Frame); return true;
                case Move.Handoff: kind = FootballFigure.PoseKind.Handoff; return true;
                case Move.Snap: kind = FootballFigure.PoseKind.Stance; return true;
                case Move.TakeSnap: kind = FootballFigure.PoseKind.Handoff; return true;   // hands out in front for it
                case Move.Reach: kind = FootballFigure.PoseKind.Reach; frame = stride; return true;
                case Move.Secure: kind = FootballFigure.PoseKind.Secure; frame = stride; return true;
                case Move.Bobble: kind = FootballFigure.PoseKind.Bobble; frame = Mathf.Min(1, m.Frame / 2); return true;
                case Move.Juke: kind = FootballFigure.PoseKind.Juke; frame = Mathf.Min(1, m.Frame); return true;
                case Move.Stagger: kind = FootballFigure.PoseKind.Stagger; frame = m.Frame % 2; return true;   // FEATURES.hitAngle: off balance
                case Move.Whiff:
                    if (m.Frame == 0) kind = FootballFigure.PoseKind.Dive;
                    else { kind = FootballFigure.PoseKind.Whiff; frame = m.Frame; }
                    return true;
                case Move.Tackle:
                case Move.Tackled:
                    if (m.GettingUp)
                    {
                        // one knee, then a crouch, then up
                        kind = m.UpFrame < 2 ? FootballFigure.PoseKind.Kneel : m.UpFrame < 3 ? FootballFigure.PoseKind.Ready : FootballFigure.PoseKind.Stand;
                        return true;
                    }
                    bool back = m.Kind == Move.Tackled && m.OnBack;
                    if (m.Frame == 0 && m.Kind == Move.Tackle) kind = FootballFigure.PoseKind.Dive;   // the tackler launches
                    else if (m.Frame < 2) { kind = back ? FootballFigure.PoseKind.FallBack : FootballFigure.PoseKind.FallFront; frame = m.Frame; }
                    else kind = back ? FootballFigure.PoseKind.Supine : FootballFigure.PoseKind.Prone;
                    return true;
                case Move.Celebrate: kind = FootballFigure.PoseKind.Celebrate; frame = m.Frame; return true;
                case Move.Block: kind = m.Locked ? FootballFigure.PoseKind.Lock : FootballFigure.PoseKind.Block; frame = m.Frame % 2; return true;
            }
            return false;
        }

        // Turn a facing (degrees, Unity yaw) a little toward the camera on the -z sideline.
        // How far a figure turns toward the camera (degrees); locked: a blocker squared up to his man.
        public static float CameraTurn(GameConfig cfg, bool locked)
        {
            if (!cfg.Feature("faceDownfield")) return locked ? cfg.anim.lockTurnDeg : TurnToCamera;
            return locked ? Mathf.Min(cfg.anim.lockTurnDeg, cfg.anim.cameraTurnDeg) : cfg.anim.cameraTurnDeg;
        }

        public static float TowardCamera(float yaw, float turn = TurnToCamera)
        {
            // yaw 0 faces +x; +90 faces -z (the camera). Pull toward +90 by turn.
            float delta = Mathf.DeltaAngle(yaw, 90f);
            return yaw + Mathf.Clamp(delta, -turn, turn);
        }

        // FEATURES.watchBall: a man following a pass in the air turns his shoulders toward the ball, then his head, then
        // (if it's still behind him) his whole body; his head tips up to it. Angles in lookStepDeg steps, from the sim's
        // tick (no blend), so it steps like the rest of the look. Returns the body's facing.
        float WatchBall(World w, Player p, FootballFigure fig, float yaw)
        {
            var a = cfg.anim;
            var b = w.Ball;
            float dx = b.X - p.X, dz = b.Z - p.Z, flat = Mathf.Sqrt(dx * dx + dz * dz);
            float step = Mathf.Max(1f, a.lookStepDeg);
            float Step(float deg) => Mathf.Round(deg / step) * step;
            float rel = flat > 0.05f ? Step(Mathf.DeltaAngle(yaw, Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg)) : 0f;
            float twist = Mathf.Clamp(rel, -a.lookTwistDeg, a.lookTwistDeg);
            float head = Mathf.Clamp(rel - twist, -a.lookHeadDeg, a.lookHeadDeg);
            float eyes = cfg.view.playerHeightM * 0.8f / cfg.field.yardM;   // his eyes, in yards off the grass
            float up = Step(Mathf.Atan2(b.Y - eyes, Mathf.Max(flat, 0.3f)) * Mathf.Rad2Deg);
            fig.Look(twist, head, up);
            return yaw + rel - twist - head;
        }

        // The snap (Snap.cs): before the hike the ball lies on the grass at the center's hands, along the field; at the
        // hike it goes back into the hands of whoever has it (the QB), rising a little on the way. False: not a snap.
        bool SnapBall(World w, float alpha, FootballFigure holder)
        {
            float yd = cfg.field.yardM, rest = cfg.view.ballWidthM * 0.5f;
            if (Snap.Spotted(w, out float sx, out float sz))
            {
                ball.localPosition = new Vector3(sx * yd, rest, sz * yd);
                ball.rotation = Quaternion.LookRotation(new Vector3(w.Dir, 0f, 0f), Vector3.up);
                return true;
            }
            if (holder == null || !Snap.InFlight(w, alpha, out float t, out sx, out sz)) return false;
            var from = new Vector3(sx * yd, rest, sz * yd);
            var to = holder.HandR.position;
            var at = Vector3.Lerp(from, to, t);
            at.y += 4f * t * (1f - t) * cfg.anim.snapArcYd * yd;
            ball.position = at;
            if ((to - from).sqrMagnitude > 1e-4f) ball.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
            return true;
        }

        public Vector3 BallPosition => ball.position;

        // Where the ball is, for the heads that follow it (yards): in the carrier's arms, on the grass at the snap, or
        // where the sim has it.
        static void BallAt(World w, out float x, out float y, out float z)
        {
            var b = w.Ball;
            x = b.X; y = b.Y; z = b.Z;
            if (Snap.Spotted(w, out float sx, out float sz)) { x = sx; y = 0f; z = sz; return; }
            var k = b.State == BallState.Held ? w.Find(b.Carrier) : null;
            if (k != null) { x = k.X; z = k.Z; y = w.Cfg.ball.carryHeightYd; }
        }

        // FEATURES.eyesLook: each man's eyes follow the ball from where his head points now (the carrier looks
        // where he's running), glance aside now and then and blink, in key frames (Alive.Eyes).
        // Where each man's eyes look (FEATURES.eyesLook), his face after a big play (FEATURES.faceReactions) and the grass
        // on his kit (FEATURES.grassStains), onto his renderer.
        readonly Reactions reactions = new Reactions();
        public Reactions Reactions => reactions;

        void Eyes(World w)
        {
            reactions.Observe(w);
            if (!cfg.Feature("eyesLook") && !cfg.Feature("faceReactions") && !cfg.Feature("grassStains")) return;
            float yd = cfg.field.yardM;
            BallAt(w, out float bx, out float by, out float bz);
            var aim = root.TransformPoint(new Vector3(bx * yd, by * yd, bz * yd));
            foreach (var p in w.Players)
            {
                if (!bodies.TryGetValue(p.Slot, out var fig) || !skins.TryGetValue(p.Slot, out var skin) || skin == null) continue;
                float yaw = 0f, up = 0f;
                bool carrier = w.Ball.State == BallState.Held && w.Ball.Carrier == p.Slot;
                // the head's own frame: it faces its +x, its left is +z
                var d = fig.Head.InverseTransformDirection(aim - fig.Head.position);
                if (!carrier && d.sqrMagnitude > 1e-4f)
                {
                    yaw = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
                    up = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                }
                Alive.Eyes(cfg, Alive.Key(cfg, w.Tick, p.Slot), p.Slot, yaw, up, out float x, out float y, out float blink);
                skin.Eyes(x, y, blink, reactions.Mood(w, p.Slot));
                skin.Stain(reactions.Stain(w, p.Slot));
            }
        }

        public Vector3 HeadPosition(int slot) =>
            bodies.TryGetValue(slot, out var f) ? f.Head.position + Vector3.up * cfg.view.playerHeightM * 0.22f : Vector3.zero;

        public Vector3 FeetPosition(int slot) => bodies.TryGetValue(slot, out var f) ? f.transform.position : Vector3.zero;

        // A kit change (a new team, number or position, or the QB turning into the kicker for a kick) builds a new body.
        FootballFigure Body(World w, Player p)
        {
            string pos = Kicking.ShowsKicker(w, p) ? "K" : p.Pos;
            string kit = p.Team + "/" + p.Number + "/" + pos;
            if (bodies.TryGetValue(p.Slot, out var fig) && kitOf.TryGetValue(p.Slot, out var was) && was == kit) return fig;
            if (fig != null) Object.Destroy(fig.gameObject);
            var team = cfg.teams[Mathf.Clamp(p.Team, 0, cfg.teams.Count - 1)];
            fig = FootballFigure.Create(root, "Player " + p.Slot, cfg.view.playerHeightM);
            ModelSkin.Wear(fig, team, pos, cfg, mat, p.Number);   // the team's (or the position's) rigged model over the pose rig
            JerseyMarks.SetNumber(fig.gameObject, cfg, p.Number);   // FEATURES.jerseyNumbers: his own number
            team.ModelFor(pos, cfg.Feature("posModels"), out string model, out _, out _, p.Number);
            JerseyMarks.SetSkin(fig.gameObject, cfg, model, JerseyMarks.SkinPick(p.Slot));   // FEATURES.skinTones: his own skin
            skins[p.Slot] = fig.GetComponentInChildren<ModelSkin>();
            fig.SwayCm = cfg.anim.idleSwayCm; fig.RollDeg = cfg.anim.idleRollDeg; fig.KneeDeg = cfg.anim.idleKneeDeg;
            bodies[p.Slot] = fig;
            kitOf[p.Slot] = kit;
            return fig;
        }
    }
}
