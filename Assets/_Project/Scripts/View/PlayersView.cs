using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // One chibi figure per player (FootballFigure) and the ball. Reads the sim, never writes it.
    // Positions are blended between the last two sim ticks so 30 Hz data moves smoothly at 60 fps. A figure faces the
    // way it runs (standing: its team's direction of play), turned a little toward the camera so faces and numbers
    // show; running steps between two run frames by distance covered (stepped, no time-based wobble).
    // On top of that, the moves (Moves.cs): throws, catches, jukes, tackles... A man on the grass after a tackle stays
    // where and how he fell until the next huddle, though the sim may already be walking him back.
    public sealed class PlayersView
    {
        const float TurnToCamera = 42f;     // degrees a figure turns toward the camera (faces and numbers show)
        const float StrideM = 0.9f;         // metres per run frame

        readonly GameConfig cfg;
        readonly Material mat;
        readonly Transform root;
        readonly Dictionary<int, FootballFigure> bodies = new Dictionary<int, FootballFigure>();
        readonly Dictionary<int, string> kitOf = new Dictionary<int, string>();
        readonly Dictionary<int, Vector2> prev = new Dictionary<int, Vector2>();
        readonly Dictionary<int, float> travelled = new Dictionary<int, float>();
        readonly List<int> gone = new List<int>();
        public readonly Moves Moves = new Moves();
        // a man on the grass: where he went down, his facing, which fall (slot -> start tick)
        readonly Dictionary<int, Vector3> downAt = new Dictionary<int, Vector3>();
        readonly Dictionary<int, float> downYaw = new Dictionary<int, float>();
        readonly Dictionary<int, int> downStart = new Dictionary<int, int>();
        readonly Transform ball, ballShadow;
        Vector3 ballPrev;

        // Photo scenes (and later the play view) can hold a pose for a player: slot -> pose.
        public readonly Dictionary<int, FootballFigure.PoseKind> PoseOverride = new Dictionary<int, FootballFigure.PoseKind>();
        // ...and give a player an extra football to hold (slot -> true: right hand, false: left), the sim's ball aside
        public readonly Dictionary<int, bool> PropBalls = new Dictionary<int, bool>();
        readonly Dictionary<int, Transform> props = new Dictionary<int, Transform>();

        public PlayersView(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = FootballFigure.KitMaterial(Looks.Toon(baseMat), cfg.view);
            root = new GameObject("Players").transform;
            ball = FootballFigure.MakeBall(null, mat, cfg.view.ballLengthM).transform;
            // a dark spot on the grass under a ball in the air: shows how high it is and where it's going
            ballShadow = Looks.Flat(null, "BallShadow", Vector3.zero, new Vector2(cfg.view.ballLengthM, cfg.view.ballLengthM * 0.6f), new Color(0.12f, 0.22f, 0.1f), mat).transform;
        }

        public int Count => bodies.Count;

        public FootballFigure Figure(int slot) => bodies.TryGetValue(slot, out var f) ? f : null;

        // Call just before World.Step: remembers where everyone was, for the blend.
        public void BeforeStep(World w)
        {
            Moves.Observe(w);
            prev.Clear();
            foreach (var p in w.Players) prev[p.Slot] = new Vector2(p.X, p.Z);
            ballPrev = new Vector3(w.Ball.X, w.Ball.Y, w.Ball.Z);
        }

        // alpha: 0 = the previous tick, 1 = the latest.
        public void Render(World w, float alpha)
        {
            float yd = cfg.field.yardM;
            Moves.Observe(w);
            foreach (var p in w.Players)
            {
                var fig = Body(p);
                var move = PoseOverride.ContainsKey(p.Slot) ? default : Moves.Of(w, p);
                bool down = move.Kind == Move.Tackle || move.Kind == Move.Tackled;
                Vector2 now = new Vector2(p.X, p.Z);
                Vector2 was = prev.TryGetValue(p.Slot, out var v) ? v : now;
                Vector2 at = Vector2.Lerp(was, now, alpha);
                var t = fig.transform;
                var pos = new Vector3(at.x * yd, 0f, at.y * yd);
                float moved = (pos - t.localPosition).magnitude;

                // facing: the way it runs, else its team's direction of play; turned toward the camera
                float speed = Mathf.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
                float dir = w.Dir * (p.Team == w.Offense ? 1f : -1f);
                Vector2 face = speed > 0.5f ? new Vector2(p.VX, p.VZ).normalized : new Vector2(dir, 0f);
                float yaw = Mathf.Atan2(-face.y, face.x) * Mathf.Rad2Deg;
                yaw = TowardCamera(yaw);
                if (down)
                {
                    // stays where and how he went down
                    if (!downStart.TryGetValue(p.Slot, out int st) || st != move.StartTick)
                    {
                        downStart[p.Slot] = move.StartTick; downAt[p.Slot] = pos; downYaw[p.Slot] = yaw;
                    }
                    pos = downAt[p.Slot]; yaw = downYaw[p.Slot]; moved = 0f;
                }
                else downStart.Remove(p.Slot);
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);

                if (PoseOverride.TryGetValue(p.Slot, out var held)) { fig.Pose(held, 0); continue; }
                travelled[p.Slot] = (travelled.TryGetValue(p.Slot, out float d) ? d : 0f) + moved;
                int frame = (int)(travelled[p.Slot] / StrideM) % 2;
                bool carrier = w.Ball.State == BallState.Held && w.Ball.Carrier == p.Slot;
                if (!down && DefenseMovesView.PoseFor(w, p, out var dk, out int df)) { fig.Pose(dk, df); continue; }   // WS-03 defender moves
                if (PoseFor(move, frame, out var kind, out int f)) fig.Pose(kind, f);
                else if (speed > 0.5f) fig.Pose(carrier ? FootballFigure.PoseKind.Carry : FootballFigure.PoseKind.Run, frame);
                else fig.Pose(p.Pos == "OL" || p.Pos == "DL" ? FootballFigure.PoseKind.Stance : FootballFigure.PoseKind.Ready, 0);
            }
            // the ball: in the carrier's right hand, else where the sim has it, its long axis along its flight
            var b = w.Ball;
            Vector3 bnow = new Vector3(b.X, b.Y, b.Z);
            Vector3 bat = Vector3.Lerp(ballPrev, bnow, alpha);
            var holder = b.State == BallState.Held ? Figure(b.Carrier) : null;
            // the snap (Snap.cs) places the ball itself before the hike and on its way back to the QB
            bool snapping = SnapBall(w, alpha, holder);
            if (!snapping && holder != null)
            {
                ball.position = holder.HandR.position;
                ball.rotation = holder.HandR.rotation * Quaternion.Euler(0, 90, 0);
            }
            else if (!snapping)
            {
                ball.localPosition = new Vector3(bat.x * yd, bat.y * yd + cfg.view.ballWidthM * 0.5f, bat.z * yd);
                var bv = new Vector3(b.VX, b.State == BallState.Flying ? b.VY : 0f, b.VZ);
                if (bv.sqrMagnitude > 0.01f) ball.rotation = Quaternion.LookRotation(bv.normalized, Vector3.up);
            }
            foreach (var kv in PropBalls)
            {
                var f = Figure(kv.Key);
                if (f == null) continue;
                if (!props.TryGetValue(kv.Key, out var prop)) props[kv.Key] = prop = FootballFigure.MakeBall(null, mat, cfg.view.ballLengthM).transform;
                var hand = kv.Value ? f.HandR : f.HandL;
                prop.position = hand.position;
                prop.rotation = hand.rotation * Quaternion.Euler(0, 90, 0);
            }
            bool inAir = b.State == BallState.Flying;
            ballShadow.gameObject.SetActive(inAir);
            if (inAir) ballShadow.localPosition = new Vector3(bat.x * yd, 0.02f, bat.z * yd);
            gone.Clear();
            foreach (var kv in bodies) if (w.Find(kv.Key) == null) gone.Add(kv.Key);
            foreach (int slot in gone) { Object.Destroy(bodies[slot].gameObject); bodies.Remove(slot); kitOf.Remove(slot); downStart.Remove(slot); }
        }

        // The figure's pose for a move (false: none, run or stand as usual). stride: the run frame.
        public static bool PoseFor(MoveNow m, int stride, out FootballFigure.PoseKind kind, out int frame)
        {
            frame = 0;
            kind = FootballFigure.PoseKind.Ready;
            switch (m.Kind)
            {
                case Move.Aim: kind = FootballFigure.PoseKind.Throw; return true;
                case Move.Throw: kind = FootballFigure.PoseKind.Release; frame = Mathf.Min(1, m.Frame / 2); return true;
                case Move.Handoff: kind = FootballFigure.PoseKind.Handoff; return true;
                case Move.Snap: kind = FootballFigure.PoseKind.Stance; return true;
                case Move.TakeSnap: kind = FootballFigure.PoseKind.Handoff; return true;   // hands out in front for it
                case Move.Reach: kind = FootballFigure.PoseKind.Reach; frame = stride; return true;
                case Move.Secure: kind = FootballFigure.PoseKind.Secure; frame = stride; return true;
                case Move.Bobble: kind = FootballFigure.PoseKind.Bobble; frame = Mathf.Min(1, m.Frame / 2); return true;
                case Move.Juke: kind = FootballFigure.PoseKind.Juke; frame = Mathf.Min(1, m.Frame); return true;
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
                case Move.Block: kind = FootballFigure.PoseKind.Block; frame = m.Frame % 2; return true;
            }
            return false;
        }

        // Turn a facing (degrees, Unity yaw) a little toward the camera on the -z sideline.
        static float TowardCamera(float yaw)
        {
            // yaw 0 faces +x; +90 faces -z (the camera). Pull toward +90 by TurnToCamera.
            float delta = Mathf.DeltaAngle(yaw, 90f);
            return yaw + Mathf.Clamp(delta, -TurnToCamera, TurnToCamera);
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

        public Vector3 HeadPosition(int slot) =>
            bodies.TryGetValue(slot, out var f) ? f.Head.position + Vector3.up * cfg.view.playerHeightM * 0.22f : Vector3.zero;

        public Vector3 FeetPosition(int slot) => bodies.TryGetValue(slot, out var f) ? f.transform.position : Vector3.zero;

        FootballFigure Body(Player p)
        {
            string kit = p.Team + "/" + p.Number + "/" + p.Pos;
            if (bodies.TryGetValue(p.Slot, out var fig) && kitOf.TryGetValue(p.Slot, out var was) && was == kit) return fig;
            if (fig != null) Object.Destroy(fig.gameObject);
            var team = cfg.teams[Mathf.Clamp(p.Team, 0, cfg.teams.Count - 1)];
            fig = FootballFigure.Create(root, team, p.Number, FootballFigure.BuildOf(p.Pos), p.Slot * 7 + p.Number, cfg.view.playerHeightM, mat, TeamMarks.HelmetFor(team));
            fig.name = "Player " + p.Slot;
            bodies[p.Slot] = fig;
            kitOf[p.Slot] = kit;
            return fig;
        }
    }
}
