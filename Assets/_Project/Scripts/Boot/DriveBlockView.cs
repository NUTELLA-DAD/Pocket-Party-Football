using System.Collections.Generic;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // Drive blocks on the TV (FEATURES.driveBlock, Sim/DriveBlock.cs): a fat yellow arrow on the grass under each
    // defender a person is shoving, pointing the way he is going (longer the harder the shove), and, when the line
    // has a gap the runner can hit (DriveBlock.Lane), a bright lane with chevrons from the line downfield through it.
    // Drive rushes (FEATURES.driveRush, Sim/DriveRush.cs): the same arrow, in blue, under each blocker a rusher is
    // shoving. Drawing only, no time-based motion; all of it hides while a replay plays.
    public sealed class DriveBlockView : MonoBehaviour
    {
        const float Lift = 0.07f, LaneLift = 0.04f, LanePastYd = 3f, MinArrow = 0.45f;
        static readonly Color Arrow = new Color(1f, 0.82f, 0.15f), RushArrow = new Color(0.35f, 0.65f, 1f), Lane = new Color(1f, 0.92f, 0.35f), Chevron = new Color(1f, 1f, 0.85f);

        PartyHost host;
        GameConfig cfg;
        Material mat, glow;
        Transform root, lane;
        readonly List<Transform> arrows = new List<Transform>(), rushArrows = new List<Transform>();

        public static DriveBlockView Attach(PartyHost h, Material baseMat)
        {
            var v = h.gameObject.AddComponent<DriveBlockView>();
            v.host = h;
            v.cfg = h.World.Cfg;
            v.mat = Looks.Toon(baseMat);
            v.glow = Looks.Glow(baseMat);
            v.root = new GameObject("DriveBlock").transform;
            v.lane = v.BuildLane();
            v.lane.gameObject.SetActive(false);
            return v;
        }

        // An arrow lying flat, pointing +X from its origin, one metre long.
        Transform BuildArrow(List<Transform> into, Color colour)
        {
            var bl = new Blocks { Flag = Looks.Gloss };
            bl.Quad(new Vector3(0.05f, 0, -0.13f), new Vector3(0.05f, 0, 0.13f), new Vector3(0.62f, 0, 0.13f), new Vector3(0.62f, 0, -0.13f), colour, Vector3.up);
            bl.Quad(new Vector3(0.6f, 0, -0.36f), new Vector3(0.6f, 0, 0.36f), new Vector3(1f, 0, 0f), new Vector3(1f, 0, 0f), colour, Vector3.up);
            var t = bl.Build(root, "Shove", mat, false).transform;
            into.Add(t);
            return t;
        }

        // The lane: a soft strip from 0 to 1 along +X, -0.5..0.5 across, with three chevrons pointing +X. Scaled and
        // turned to fit the gap each frame.
        Transform BuildLane()
        {
            var fill = new Blocks { Flag = 0.38f };
            fill.Quad(new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.5f), new Vector3(1, 0, 0.5f), new Vector3(1, 0, -0.5f), Lane, Vector3.up);
            var go = fill.Build(root, "Lane", glow, false);
            var marks = new Blocks { Flag = 0.85f };
            for (int i = 0; i < 3; i++)
            {
                float x = 0.25f + i * 0.22f, t = 0.05f, tip = 0.1f;
                marks.Quad(new Vector3(x, 0.01f, -0.38f), new Vector3(x + t, 0.01f, -0.38f), new Vector3(x + tip + t, 0.01f, 0f), new Vector3(x + tip, 0.01f, 0f), Chevron, Vector3.up);
                marks.Quad(new Vector3(x, 0.01f, 0.38f), new Vector3(x + tip, 0.01f, 0f), new Vector3(x + tip + t, 0.01f, 0f), new Vector3(x + t, 0.01f, 0.38f), Chevron, Vector3.up);
            }
            marks.Build(go.transform, "Chevrons", glow, false);
            return go.transform;
        }

        void LateUpdate()
        {
            var w = host != null ? host.World : null;
            int n = 0, r = 0;
            bool live = w != null && !Replay.OnScreen;
            if (live && DriveBlock.On(w))
            {
                n = Shoves(w);
                LaneNow(w);
            }
            else lane.gameObject.SetActive(false);
            if (live && DriveRush.On(w)) r = RushShoves(w);
            for (int i = n; i < arrows.Count; i++) arrows[i].gameObject.SetActive(false);
            for (int i = r; i < rushArrows.Count; i++) rushArrows[i].gameObject.SetActive(false);
        }

        int Shoves(World w)
        {
            float full = Mathf.Max(0.01f, cfg.driveBlock.pushYdS);
            int n = 0;
            foreach (var push in w.Drive.Pushes)
            {
                var d = w.Find(push.Defender);
                if (d == null) continue;
                var a = n < arrows.Count ? arrows[n] : BuildArrow(arrows, Arrow);
                n++;
                Place(a, d, push.VX, push.VZ, full);
            }
            return n;
        }

        // FEATURES.driveRush: a blue arrow under each blocker a rusher is shoving.
        int RushShoves(World w)
        {
            float full = Mathf.Max(0.01f, cfg.driveRush.pushYdS);
            int n = 0;
            foreach (var push in w.Rush.Pushes)
            {
                var o = w.Find(push.Blocker);
                if (o == null) continue;
                var a = n < rushArrows.Count ? rushArrows[n] : BuildArrow(rushArrows, RushArrow);
                n++;
                Place(a, o, push.VX, push.VZ, full);
            }
            return n;
        }

        // The arrow under a shoved man, pointing the way he is going, longer the harder the shove (a weak one still shows).
        void Place(Transform a, Player p, float vx, float vz, float full)
        {
            float m = cfg.field.yardM;
            a.gameObject.SetActive(true);
            float speed = Mathf.Sqrt(vx * vx + vz * vz);
            float len = (MinArrow + Mathf.Clamp01(speed / full)) * 1.6f * m;
            a.position = new Vector3(p.X * m, Lift, p.Z * m);
            a.rotation = Quaternion.Euler(0f, -Mathf.Atan2(vz, vx) * Mathf.Rad2Deg, 0f);
            a.localScale = new Vector3(len, 1f, Mathf.Max(1f, len * 0.6f));
        }

        void LaneNow(World w)
        {
            if (!DriveBlock.Lane(w, out float zMid, out float width)) { lane.gameObject.SetActive(false); return; }
            var c = cfg.driveBlock;
            float m = cfg.field.yardM;
            float open = Mathf.Max(1f, width - cfg.players.radiusYd * 2f);   // the space between the two men, not their middles
            float from = w.LineOfScrimmageX - w.Dir * c.laneBehindYd, length = c.laneBehindYd + c.laneDepthYd + LanePastYd;
            lane.gameObject.SetActive(true);
            lane.position = new Vector3(from * m, LaneLift, zMid * m);
            lane.rotation = Quaternion.Euler(0f, w.Dir >= 0f ? 0f : 180f, 0f);
            lane.localScale = new Vector3(length * m, 1f, open * m);
        }
    }
}
