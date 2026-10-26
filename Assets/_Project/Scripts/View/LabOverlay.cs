using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The practice lab's hitboxes, drawn flat on the grass at their true size (no squash): a white circle for each
    // player's body (the bump circle), red for how far a tackle reaches, blue for a blocker's reach, yellow where a
    // pass can be caught (orange at the spot it is aimed at), and dots along the ball's flight path.
    public sealed class LabOverlay
    {
        const int Segments = 48;
        const float LineM = 0.09f, Lift = 0.05f, DotM = 0.12f, DotEveryS = 0.08f;
        const int MaxDots = 60;

        static readonly Color Body = new Color(1f, 1f, 1f), Tackle = new Color(0.95f, 0.22f, 0.2f), Block = new Color(0.25f, 0.55f, 1f),
            Catch = new Color(1f, 0.9f, 0.15f), Aim = new Color(1f, 0.55f, 0.1f), Arc = new Color(1f, 1f, 1f);

        readonly GameConfig cfg;
        readonly Material mat;
        readonly Transform root;
        readonly Dictionary<int, Transform> bodies = new Dictionary<int, Transform>(), reach = new Dictionary<int, Transform>(),
            humanReach = new Dictionary<int, Transform>(), blocks = new Dictionary<int, Transform>();
        readonly Transform catchNow, aim;
        readonly List<Transform> dots = new List<Transform>();
        bool shown = true;

        public LabOverlay(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = Looks.Toon(baseMat);
            root = new GameObject("LabOverlay").transform;
            catchNow = Ring(cfg.play.catchRadiusYd, Catch, "CatchCircle");
            aim = Ring(cfg.play.catchRadiusYd, Aim, "AimCircle");
            var d = new Blocks { Flag = Looks.Gloss };
            d.Box(Vector3.zero, Vector3.one * DotM, Arc);
            var first = d.Build(root, "ArcDot", mat, false).transform;
            dots.Add(first);
            for (int i = 1; i < MaxDots; i++) dots.Add(Object.Instantiate(first.gameObject, root).transform);
        }

        public bool Shown
        {
            get => shown;
            set { shown = value; root.gameObject.SetActive(value); }
        }

        // feet: each player's drawn position (the view's blend between ticks), so the circles stay under the bodies.
        public void Draw(World w, System.Func<int, Vector3> feet)
        {
            if (!shown) return;
            float m = cfg.field.yardM;
            var carrier = LivePlay.Carrier(w);
            var seen = new HashSet<int>();
            foreach (var p in w.Players)
            {
                seen.Add(p.Slot);
                Vector3 at = feet(p.Slot);
                at.y = Lift;
                Show(bodies, p.Slot, cfg.players.radiusYd, Body, at, true);
                bool tackler = carrier != null && p.Team != carrier.Team && w.Phase == Phase.Live;
                Show(reach, p.Slot, cfg.play.tackleReachYd, Tackle, at, tackler && !p.Human);
                Show(humanReach, p.Slot, Lab.TackleReach(w, p), Tackle, at, tackler && p.Human);
                Show(blocks, p.Slot, cfg.bots.blockReachYd, Block, at, p.Blocking && w.Phase == Phase.Live);
            }
            Hide(bodies, seen); Hide(reach, seen); Hide(humanReach, seen); Hide(blocks, seen);

            // the pass: where it is aimed, and the circle it can be caught in once it comes down low enough
            var b = w.Ball;
            bool flying = b.State == BallState.Flying;
            aim.gameObject.SetActive(flying);
            if (flying) aim.position = new Vector3(b.TargetX * m, Lift, b.TargetZ * m);
            bool catchable = flying && b.VY < 0f && b.Y <= cfg.play.catchMaxHeightYd;
            catchNow.gameObject.SetActive(catchable);
            if (catchable) catchNow.position = new Vector3(b.X * m, Lift + 0.01f, b.Z * m);

            // the flight path from here to the grass
            int n = 0;
            if (flying)
            {
                float g = cfg.ball.gravityYdS2;
                for (float t = 0f; n < MaxDots; t += DotEveryS)
                {
                    float y = b.Y + b.VY * t - g * t * t * 0.5f;
                    if (y < 0f) break;
                    dots[n].position = new Vector3((b.X + b.VX * t) * m, y * m + cfg.view.ballWidthM * 0.5f, (b.Z + b.VZ * t) * m);
                    dots[n].gameObject.SetActive(true);
                    n++;
                }
            }
            for (int i = n; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        }

        void Show(Dictionary<int, Transform> set, int slot, float radiusYd, Color c, Vector3 at, bool on)
        {
            if (!set.TryGetValue(slot, out var r))
            {
                if (!on) return;
                r = Ring(radiusYd, c, "Ring" + slot);
                set[slot] = r;
            }
            r.gameObject.SetActive(on);
            if (on) r.position = at;
        }

        static readonly List<int> drop = new List<int>();
        static void Hide(Dictionary<int, Transform> set, HashSet<int> seen)
        {
            drop.Clear();
            foreach (var kv in set) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (int k in drop) { Object.Destroy(set[k].gameObject); set.Remove(k); }
        }

        // A thin flat ring; radiusYd is its outer edge (the hitbox's true size).
        Transform Ring(float radiusYd, Color c, string name)
        {
            var bl = new Blocks { Flag = Looks.Gloss };
            float r1 = radiusYd * cfg.field.yardM, r0 = Mathf.Max(0f, r1 - LineM);
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * Mathf.PI * 2 / Segments, a1 = (i + 1) * Mathf.PI * 2 / Segments;
                Vector3 P(float r, float a) => new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                bl.Quad(P(r0, a0), P(r1, a0), P(r1, a1), P(r0, a1), c, Vector3.up);
            }
            return bl.Build(root, name, mat, false).transform;
        }
    }
}
