using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // One chibi figure per player (FootballFigure) and the ball. Reads the sim, never writes it.
    // Positions are blended between the last two sim ticks so 30 Hz data moves smoothly at 60 fps. A figure faces the
    // way it runs (standing: its team's direction of play), turned a little toward the camera so faces and numbers
    // show; running steps between two run frames by distance covered (stepped, no time-based wobble).
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
        readonly Transform ball, ballShadow;
        Vector3 ballPrev;

        // Photo scenes (and later the play view) can hold a pose for a player: slot -> pose.
        public readonly Dictionary<int, FootballFigure.PoseKind> PoseOverride = new Dictionary<int, FootballFigure.PoseKind>();

        public PlayersView(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = Looks.Toon(baseMat);
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
            prev.Clear();
            foreach (var p in w.Players) prev[p.Slot] = new Vector2(p.X, p.Z);
            ballPrev = new Vector3(w.Ball.X, w.Ball.Y, w.Ball.Z);
        }

        // alpha: 0 = the previous tick, 1 = the latest.
        public void Render(World w, float alpha)
        {
            float yd = cfg.field.yardM;
            foreach (var p in w.Players)
            {
                var fig = Body(p);
                Vector2 now = new Vector2(p.X, p.Z);
                Vector2 was = prev.TryGetValue(p.Slot, out var v) ? v : now;
                Vector2 at = Vector2.Lerp(was, now, alpha);
                var t = fig.transform;
                var pos = new Vector3(at.x * yd, 0f, at.y * yd);
                float moved = (pos - t.localPosition).magnitude;
                t.localPosition = pos;

                // facing: the way it runs, else its team's direction of play; turned toward the camera
                float speed = Mathf.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
                float dir = cfg.formation.attackDir * (p.Team == w.Offense ? 1f : -1f);
                Vector2 face = speed > 0.5f ? new Vector2(p.VX, p.VZ).normalized : new Vector2(dir, 0f);
                float yaw = Mathf.Atan2(-face.y, face.x) * Mathf.Rad2Deg;
                yaw = TowardCamera(yaw);
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);

                if (PoseOverride.TryGetValue(p.Slot, out var held)) { fig.Pose(held, 0); continue; }
                travelled[p.Slot] = (travelled.TryGetValue(p.Slot, out float d) ? d : 0f) + moved;
                int frame = (int)(travelled[p.Slot] / StrideM) % 2;
                bool carrier = w.Ball.State == BallState.Held && w.Ball.Carrier == p.Slot;
                if (speed > 0.5f) fig.Pose(carrier ? FootballFigure.PoseKind.Carry : FootballFigure.PoseKind.Run, frame);
                else fig.Pose(p.Pos == "OL" || p.Pos == "DL" ? FootballFigure.PoseKind.Stance : FootballFigure.PoseKind.Ready, 0);
            }
            // the ball: in the carrier's right hand, else where the sim has it, its long axis along its flight
            var b = w.Ball;
            Vector3 bnow = new Vector3(b.X, b.Y, b.Z);
            Vector3 bat = Vector3.Lerp(ballPrev, bnow, alpha);
            var holder = b.State == BallState.Held ? Figure(b.Carrier) : null;
            if (holder != null)
            {
                ball.position = holder.HandR.position;
                ball.rotation = holder.HandR.rotation * Quaternion.Euler(0, 90, 0);
            }
            else
            {
                ball.localPosition = new Vector3(bat.x * yd, bat.y * yd + cfg.view.ballWidthM * 0.5f, bat.z * yd);
                var bv = new Vector3(b.VX, b.State == BallState.Flying ? b.VY : 0f, b.VZ);
                if (bv.sqrMagnitude > 0.01f) ball.rotation = Quaternion.LookRotation(bv.normalized, Vector3.up);
            }
            bool inAir = b.State == BallState.Flying;
            ballShadow.gameObject.SetActive(inAir);
            if (inAir) ballShadow.localPosition = new Vector3(bat.x * yd, 0.02f, bat.z * yd);
            gone.Clear();
            foreach (var kv in bodies) if (w.Find(kv.Key) == null) gone.Add(kv.Key);
            foreach (int slot in gone) { Object.Destroy(bodies[slot].gameObject); bodies.Remove(slot); kitOf.Remove(slot); }
        }

        // Turn a facing (degrees, Unity yaw) a little toward the camera on the -z sideline.
        static float TowardCamera(float yaw)
        {
            // yaw 0 faces +x; +90 faces -z (the camera). Pull toward +90 by TurnToCamera.
            float delta = Mathf.DeltaAngle(yaw, 90f);
            return yaw + Mathf.Clamp(delta, -TurnToCamera, TurnToCamera);
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
            fig = FootballFigure.Create(root, team, p.Number, FootballFigure.BuildOf(p.Pos), p.Slot * 7 + p.Number, cfg.view.playerHeightM, mat, TeamMarks.For(team));
            fig.name = "Player " + p.Slot;
            bodies[p.Slot] = fig;
            kitOf[p.Slot] = kit;
            return fig;
        }
    }
}
