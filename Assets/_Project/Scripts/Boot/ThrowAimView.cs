using System.Collections.Generic;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The throw stick on the TV (FEATURES.throwStick): while a person at QB holds the aim stick, the pass he would
    // throw is drawn from the ball to the grass, a line of dots along its flight (ThrowStick.Plan, the same flight the
    // sim will fly) and a ring where it comes down, catch-radius wide. Bullet: a low flat line; lob: a high arc. Drawing
    // only: the sim never sees the aim until the stick is let go. Nothing moves on its own (no wobble).
    public sealed class ThrowAimView : MonoBehaviour
    {
        const int Segments = 40, MaxDots = 48;
        const float LineM = 0.12f, Lift = 0.06f, DotM = 0.16f, DotEveryS = 0.07f;
        static readonly Color Spot = new Color(1f, 0.85f, 0.2f), Dot = new Color(1f, 1f, 1f);

        PartyHost host;
        GameConfig cfg;
        Material mat;
        Transform root, ring;
        readonly List<Transform> dots = new List<Transform>();

        public static ThrowAimView Attach(PartyHost h, Material baseMat)
        {
            var v = h.gameObject.AddComponent<ThrowAimView>();
            v.host = h;
            v.cfg = h.World.Cfg;
            v.mat = Looks.Toon(baseMat);
            v.Build();
            return v;
        }

        void Build()
        {
            root = new GameObject("ThrowAim").transform;
            var bl = new Blocks { Flag = Looks.Gloss };
            float r1 = cfg.play.catchRadiusYd * cfg.field.yardM, r0 = Mathf.Max(0f, r1 - LineM);
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * Mathf.PI * 2 / Segments, a1 = (i + 1) * Mathf.PI * 2 / Segments;
                Vector3 P(float r, float a) => new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                bl.Quad(P(r0, a0), P(r1, a0), P(r1, a1), P(r0, a1), Spot, Vector3.up);
            }
            ring = bl.Build(root, "AimSpot", mat, false).transform;
            var d = new Blocks { Flag = Looks.Gloss };
            d.Box(Vector3.zero, Vector3.one * DotM, Dot);
            var first = d.Build(root, "AimDot", mat, false).transform;
            dots.Add(first);
            for (int i = 1; i < MaxDots; i++) dots.Add(Instantiate(first.gameObject, root).transform);
            Hide();
        }

        void LateUpdate()
        {
            var w = host != null ? host.World : null;
            if (w == null || !ThrowStick.On(w) || w.Phase != Phase.Live || w.Thrown) { Hide(); return; }
            var qb = LivePlay.Qb(w);
            var src = qb != null && qb.Human ? host.SourceOf(qb.Slot) : null;
            if (src == null || !src.Aiming || LivePlay.Carrier(w) != qb) { Hide(); return; }

            var b = w.Ball;
            float m = cfg.field.yardM;
            if (!ThrowStick.Target(w, qb, src.AimX, -src.AimY, out float tx, out float tz) ||
                !ThrowStick.Plan(cfg.ball, w.Dt, b.X, b.Y, b.Z, tx, tz, src.AimArc, out var f)) { Hide(); return; }

            ring.gameObject.SetActive(true);
            ring.position = new Vector3(tx * m, Lift, tz * m);
            float g = cfg.ball.gravityYdS2;
            int n = 0;
            for (float t = DotEveryS; n < MaxDots && t < f.T; t += DotEveryS)
            {
                float y = b.Y + f.TrueVY * t - g * t * t * 0.5f;
                if (y < 0f) break;
                dots[n].position = new Vector3((b.X + f.VX * t) * m, y * m + cfg.view.ballWidthM * 0.5f, (b.Z + f.VZ * t) * m);
                dots[n].gameObject.SetActive(true);
                n++;
            }
            for (int i = n; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        }

        void Hide()
        {
            if (ring != null) ring.gameObject.SetActive(false);
            foreach (var d in dots) d.gameObject.SetActive(false);
        }
    }
}
