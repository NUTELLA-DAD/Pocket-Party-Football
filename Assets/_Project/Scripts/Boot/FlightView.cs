using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The practice lab's helpers in the real game (Sim/FlightMarks.cs has the switches and the rules):
    // once a pass is in the air, its flight to the grass as the same soft ribbon as the aim (ArcRibbon), the ring where
    // it is aimed (the aim spot's yellow, so the ring the QB aimed simply stays put) and a white catch circle under the
    // ball once it is low enough to catch; and a red ring under each person on defense as wide as his TACKLE reaches,
    // bold while the carrier is inside it. Drawing only. The lab draws its own (LabOverlay), so this stays off there;
    // and all of it hides while a replay plays.
    public sealed class FlightView : MonoBehaviour
    {
        const int Segments = 40;
        const float Lift = 0.05f, LineM = 0.12f, ReachLineM = 0.1f, ReachBoldM = 0.26f;
        static readonly Color Spot = new Color(1f, 0.85f, 0.2f), Catch = new Color(1f, 1f, 1f),
            Reach = new Color(0.8f, 0.16f, 0.13f), ReachIn = new Color(1f, 0.25f, 0.18f);

        PartyHost host;
        GameConfig cfg;
        Material mat;
        Transform root, landing, catchNow;
        ArcRibbon arc;
        readonly float[] px = new float[ArcRibbon.Max], py = new float[ArcRibbon.Max], pz = new float[ArcRibbon.Max];
        readonly Dictionary<int, Transform> reachOut = new Dictionary<int, Transform>(), reachIn = new Dictionary<int, Transform>();
        readonly HashSet<int> seen = new HashSet<int>();
        static readonly List<int> drop = new List<int>();

        public static FlightView Attach(PartyHost h, Material baseMat)
        {
            var v = h.gameObject.AddComponent<FlightView>();
            v.host = h;
            v.cfg = h.World.Cfg;
            v.mat = Looks.Toon(baseMat);
            v.root = new GameObject("FlightMarks").transform;
            v.landing = v.Ring(v.cfg.play.catchRadiusYd, LineM, Spot, "LandingSpot");
            v.catchNow = v.Ring(v.cfg.play.catchRadiusYd, LineM, Catch, "CatchCircle");
            v.arc = new ArcRibbon(v.root, "FlightArc", baseMat);
            v.landing.gameObject.SetActive(false);
            v.catchNow.gameObject.SetActive(false);
            return v;
        }

        void LateUpdate()
        {
            var w = host != null ? host.World : null;
            bool lab = LabMode.Instance != null && LabMode.Instance.Lab != null;
            if (w == null || lab || Replay.OnScreen)   // (nothing of the live play over a replay)
            {
                landing.gameObject.SetActive(false);
                catchNow.gameObject.SetActive(false);
                arc.Hide();
                Rings(null);
                return;
            }
            float m = cfg.field.yardM;
            var b = w.Ball;

            // the pass: where it is aimed, and the circle it can be caught in once it comes down low enough
            bool flying = FlightMarks.Flying(w), landingOn = FlightMarks.LandingOn(w);
            landing.gameObject.SetActive(landingOn && flying);
            if (landingOn && flying) landing.position = new Vector3(b.TargetX * m, Lift, b.TargetZ * m);
            bool catchable = landingOn && FlightMarks.Catchable(w);
            catchNow.gameObject.SetActive(catchable);
            if (catchable) catchNow.position = new Vector3(b.X * m, Lift + 0.01f, b.Z * m);

            // the flight path from here to the grass
            int n = FlightMarks.LineOn(w) ? FlightMarks.Path(w, px, py, pz) : 0;
            for (int i = 0; i < n; i++) arc.Path[i] = new Vector3(px[i] * m, py[i] * m + cfg.view.ballWidthM * 0.5f, pz[i] * m);
            arc.Draw(n);

            Rings(w);
        }

        // The tackle reach rings under the people on defense (w null: none).
        void Rings(World w)
        {
            seen.Clear();
            if (w != null)
                foreach (var p in w.Players)
                {
                    var r = FlightMarks.TackleRing(w, p);
                    if (r == FlightMarks.Reach.None) continue;
                    seen.Add(p.Slot);
                    Vector3 at = host.Players.FeetPosition(p.Slot);
                    at.y = Lift;
                    float yd = LivePlay.TackleReach(w, p);
                    Show(reachOut, p.Slot, yd, ReachLineM, Reach, at, r == FlightMarks.Reach.Out);
                    Show(reachIn, p.Slot, yd, ReachBoldM, ReachIn, at, r == FlightMarks.Reach.In);
                }
            Hide(reachOut); Hide(reachIn);
        }

        void Show(Dictionary<int, Transform> set, int slot, float radiusYd, float lineM, Color c, Vector3 at, bool on)
        {
            if (!set.TryGetValue(slot, out var r))
            {
                if (!on) return;
                r = Ring(radiusYd, lineM, c, "Reach" + slot);
                set[slot] = r;
            }
            r.gameObject.SetActive(on);
            if (on) r.position = at;
        }

        // Every ring for a player who no longer needs one goes away (he left, or the run is over).
        void Hide(Dictionary<int, Transform> set)
        {
            drop.Clear();
            foreach (var kv in set) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (int k in drop) { Destroy(set[k].gameObject); set.Remove(k); }
        }

        // A thin flat ring; radiusYd is its outer edge (true size).
        Transform Ring(float radiusYd, float lineM, Color c, string name)
        {
            var bl = new Blocks { Flag = Looks.Gloss };
            float r1 = radiusYd * cfg.field.yardM, r0 = Mathf.Max(0f, r1 - lineM);
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
