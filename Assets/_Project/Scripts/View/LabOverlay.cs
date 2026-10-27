using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The practice lab's hitboxes, drawn flat on the grass at their true size (no squash): a white circle for each
    // player's body (the bump circle), red for how far a tackle reaches, blue for a blocker's reach, yellow where a
    // pass can be caught (orange at the spot it is aimed at), and a soft white ribbon along the ball's flight path.
    public sealed class LabOverlay
    {
        const int Segments = 48;
        const float LineM = 0.09f, Lift = 0.05f, ArcHalfM = 0.16f, ArcEveryS = 0.03f, ArcCore = 0.9f, ArcFadeInM = 0.6f;
        const int MaxArc = 160;
        // across the ribbon: (share of its half width, alpha share): a bright middle that melts away at both edges
        static readonly float[] Across = { -1f, -0.45f, 0f, 0.45f, 1f }, AcrossA = { 0f, 0.65f, 1f, 0.65f, 0f };

        static readonly Color Body = new Color(1f, 1f, 1f), Tackle = new Color(0.95f, 0.22f, 0.2f), Block = new Color(0.25f, 0.55f, 1f),
            Catch = new Color(1f, 0.9f, 0.15f), Aim = new Color(1f, 0.55f, 0.1f), Arc = new Color(1f, 1f, 1f);

        readonly GameConfig cfg;
        readonly Material mat;
        readonly Transform root;
        readonly Dictionary<int, Transform> bodies = new Dictionary<int, Transform>(), reach = new Dictionary<int, Transform>(),
            humanReach = new Dictionary<int, Transform>(), blocks = new Dictionary<int, Transform>();
        readonly Transform catchNow, aim;
        readonly Mesh arc;
        readonly GameObject arcGo;
        readonly List<Vector3> arcV = new List<Vector3>();
        readonly List<Color> arcC = new List<Color>();
        readonly List<int> arcT = new List<int>();
        readonly Vector3[] path = new Vector3[MaxArc];
        bool shown = true;

        public LabOverlay(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = Looks.Toon(baseMat);
            root = new GameObject("LabOverlay").transform;
            catchNow = Ring(cfg.play.catchRadiusYd, Catch, "CatchCircle");
            aim = Ring(cfg.play.catchRadiusYd, Aim, "AimCircle");
            arc = new Mesh { name = "Arc" };
            arc.MarkDynamic();
            arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(root, false);
            arcGo.AddComponent<MeshFilter>().sharedMesh = arc;
            var ar = arcGo.AddComponent<MeshRenderer>();
            ar.sharedMaterial = Looks.Glow(baseMat);
            ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ar.receiveShadows = false;
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
                for (float t = 0f; n < MaxArc; t += ArcEveryS)
                {
                    float y = b.Y + b.VY * t - g * t * t * 0.5f;
                    if (y < 0f) break;
                    path[n++] = new Vector3((b.X + b.VX * t) * m, y * m + cfg.view.ballWidthM * 0.5f, (b.Z + b.VZ * t) * m);
                }
            }
            Ribbon(n);
        }

        // The flight path as one smooth ribbon turned to face the camera: soft at its edges, fading in from the ball
        // (so it never hides it) and out where it meets the grass.
        void Ribbon(int n)
        {
            arcV.Clear(); arcC.Clear(); arcT.Clear();
            var cam = Camera.main;
            if (n >= 2 && cam != null)
            {
                int k = Across.Length;
                float run = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) run += (path[i] - path[i - 1]).magnitude;
                    Vector3 along = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                    Vector3 side = Vector3.Cross(along, cam.transform.position - path[i]).normalized;
                    float fade = Mathf.Clamp01(run / ArcFadeInM) * Mathf.Clamp01((n - 1 - i) / 6f);
                    for (int j = 0; j < k; j++)
                    {
                        arcV.Add(path[i] + side * (Across[j] * ArcHalfM));
                        arcC.Add(Looks.VertexColor(Arc, ArcCore * AcrossA[j] * fade));
                    }
                    if (i == 0) continue;
                    int s0 = (i - 1) * k, s1 = i * k;
                    for (int j = 0; j + 1 < k; j++)
                    {
                        arcT.Add(s0 + j); arcT.Add(s1 + j); arcT.Add(s1 + j + 1);
                        arcT.Add(s0 + j); arcT.Add(s1 + j + 1); arcT.Add(s0 + j + 1);
                    }
                }
            }
            arc.Clear();
            arc.SetVertices(arcV);
            arc.SetColors(arcC);
            arc.SetTriangles(arcT, 0);
            arc.RecalculateBounds();
            arcGo.SetActive(arcT.Count > 0);
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
