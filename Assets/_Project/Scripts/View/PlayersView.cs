using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // One capsule per player in team colours (stand-ins until the M4 art), and the ball. Reads the sim, never writes it.
    // Positions are blended between the last two sim ticks so 30 Hz data moves smoothly at 60 fps.
    public sealed class PlayersView
    {
        readonly GameConfig cfg;
        readonly Material baseMat;
        readonly Transform root;
        readonly Dictionary<int, Transform> bodies = new Dictionary<int, Transform>();
        readonly Dictionary<int, int> teamOf = new Dictionary<int, int>();
        readonly Dictionary<int, Vector2> prev = new Dictionary<int, Vector2>();
        readonly List<Material> teamMats = new List<Material>();
        readonly List<int> gone = new List<int>();
        readonly Transform ball, ballShadow;
        Vector3 ballPrev;

        public PlayersView(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            this.baseMat = baseMat;
            root = new GameObject("Players").transform;
            foreach (var t in cfg.teams)
            {
                var m = new Material(baseMat) { name = "Team " + t.@short };
                m.SetColor("_BaseColor", Colors.Hex(t.color));
                m.SetFloat("_Smoothness", 0.2f);
                teamMats.Add(m);
            }
            var bgo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bgo.name = "Ball";
            Object.Destroy(bgo.GetComponent<Collider>());
            ball = bgo.transform;
            ball.localScale = new Vector3(cfg.view.ballWidthM, cfg.view.ballWidthM, cfg.view.ballLengthM);
            var bm = new Material(baseMat) { name = "Ball" };
            bm.SetColor("_BaseColor", new Color(0.55f, 0.29f, 0.13f));
            bgo.GetComponent<MeshRenderer>().sharedMaterial = bm;
            // a dark spot on the grass under a ball in the air: shows how high it is and where it's going
            var sgo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sgo.name = "BallShadow";
            Object.Destroy(sgo.GetComponent<Collider>());
            ballShadow = sgo.transform;
            ballShadow.localScale = new Vector3(cfg.view.ballLengthM, 0.005f, cfg.view.ballLengthM);
            var sm = new Material(baseMat) { name = "BallShadow" };
            sm.SetColor("_BaseColor", new Color(0.1f, 0.15f, 0.1f));
            var sr = sgo.GetComponent<MeshRenderer>();
            sr.sharedMaterial = sm;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public int Count => bodies.Count;

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
                var body = Body(p);
                Vector2 now = new Vector2(p.X, p.Z);
                Vector2 was = prev.TryGetValue(p.Slot, out var v) ? v : now;
                Vector2 at = Vector2.Lerp(was, now, alpha);
                body.localPosition = new Vector3(at.x * yd, cfg.view.playerHeightM * 0.5f, at.y * yd);
            }
            // the ball: its long axis follows its flight (level when held or rolling)
            var b = w.Ball;
            Vector3 bnow = new Vector3(b.X, b.Y, b.Z);
            Vector3 bat = Vector3.Lerp(ballPrev, bnow, alpha);
            ball.localPosition = new Vector3(bat.x * yd, bat.y * yd + cfg.view.ballWidthM * 0.5f, bat.z * yd);
            if (b.State == BallState.Held) ball.localPosition += new Vector3(0f, 0f, -cfg.view.playerRadiusM);   // tucked on the camera side, so it shows
            var bv = new Vector3(b.VX, b.State == BallState.Flying ? b.VY : 0f, b.VZ);
            if (bv.sqrMagnitude > 0.01f) ball.rotation = Quaternion.LookRotation(bv.normalized, Vector3.up);
            bool inAir = b.State == BallState.Flying;
            ballShadow.gameObject.SetActive(inAir);
            if (inAir) ballShadow.localPosition = new Vector3(bat.x * yd, 0.02f, bat.z * yd);
            gone.Clear();
            foreach (var kv in bodies) if (w.Find(kv.Key) == null) gone.Add(kv.Key);
            foreach (int slot in gone) { Object.Destroy(bodies[slot].gameObject); bodies.Remove(slot); teamOf.Remove(slot); }
        }

        public Vector3 BallPosition => ball.position;

        public Vector3 HeadPosition(int slot) =>
            bodies.TryGetValue(slot, out var t) ? t.position + Vector3.up * cfg.view.playerHeightM * 0.5f : Vector3.zero;

        Transform Body(Player p)
        {
            if (!bodies.TryGetValue(p.Slot, out var t))
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Player " + p.Slot;
                Object.Destroy(go.GetComponent<Collider>());
                t = go.transform;
                t.SetParent(root, false);
                float r = cfg.view.playerRadiusM * 2f;
                t.localScale = new Vector3(r, cfg.view.playerHeightM * 0.5f, r);
                bodies[p.Slot] = t;
            }
            if (!teamOf.TryGetValue(p.Slot, out int team) || team != p.Team)
            {
                teamOf[p.Slot] = p.Team;
                if (p.Team >= 0 && p.Team < teamMats.Count) t.GetComponent<MeshRenderer>().sharedMaterial = teamMats[p.Team];
            }
            return t;
        }
    }
}
