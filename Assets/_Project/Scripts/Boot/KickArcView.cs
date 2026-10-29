using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The kick's arc on the TV (FEATURES.kickArc): while a person lines up a punt or a field goal, a shaded arrow lies
    // on the grass from the ball to where the kick comes down, pointing the way he steers it, and a soft ribbon above
    // it shows the flight. Lined up it shows a kick at arcPreviewPower and the kind's own climb; after the snap it
    // follows the POWER meter, then the PITCH ANGLE needle, so the room sees each tap change the kick. The arrow
    // turns red while the power is in the hook zone. No wind in the picture (the flags on the posts are for reading
    // that). Drawing only: it reads the sim and never writes to it.
    public sealed class KickArcView : MonoBehaviour
    {
        const float EveryS = 0.05f, GroundEveryM = 0.5f, Lift = 0.04f, HeadLenM = 2.2f, HeadHalfM = 1.3f, PastPostsYd = 3f;
        static readonly Color Gold = new Color(1f, 0.84f, 0.2f), Hot = new Color(0.95f, 0.3f, 0.22f), Sky = new Color(1f, 0.97f, 0.85f);

        PartyHost host;
        Transform root;
        ArcRibbon air, ground;
        Mesh headMesh;
        GameObject head;
        readonly Vector3[] headV = new Vector3[4];
        readonly Color[] headC = new Color[4];

        public static KickArcView Attach(PartyHost h, Material baseMat)
        {
            var v = h.gameObject.AddComponent<KickArcView>();
            v.host = h;
            v.Build(baseMat);
            return v;
        }

        void Build(Material baseMat)
        {
            root = new GameObject("KickArc").transform;
            ground = new ArcRibbon(root, "KickArcGround", baseMat, Gold, 0.85f, 0.75f, true);
            air = new ArcRibbon(root, "KickArcAir", baseMat, Sky, 0.2f, 0.9f, false);
            headMesh = new Mesh { name = "KickArcHead" };
            headMesh.MarkDynamic();
            head = new GameObject("KickArcHead");
            head.transform.SetParent(root, false);
            head.AddComponent<MeshFilter>().sharedMesh = headMesh;
            var r = head.AddComponent<MeshRenderer>();
            r.sharedMaterial = Looks.Glow(baseMat);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Hide();
        }

        void LateUpdate()
        {
            var w = host != null ? host.World : null;
            if (w == null || !Kicking.ArcOn(w) || Replay.OnScreen) { Hide(); return; }
            var kicker = Kicking.Kicker(w);
            var stage = Kicking.Stage(w);
            if (kicker == null || !kicker.Human || kicker.BotHelp || stage == Kicking.SweepStage.None || !Kicking.WaitingToKick(w, kicker)) { Hide(); return; }

            var c = w.Cfg.kicking;
            var k = w.Kick;
            float m = w.Cfg.field.yardM, dir = w.Dir, g = w.Cfg.ball.gravityYdS2;
            Kicking.ArcNow(w, out float power, out float pitchDeg);
            float bx = Kicking.Holder(w) != null ? k.HoldX : kicker.X, bz = Kicking.Holder(w) != null ? k.HoldZ : kicker.Z;
            float yaw = k.Aim * c.maxAimDeg * Mathf.Deg2Rad, climb = pitchDeg * Mathf.Deg2Rad;
            float ux = dir * Mathf.Cos(yaw), uz = -dir * Mathf.Sin(yaw);   // the kicker's right = -z going +x (Kicking.Launch)
            float speed = Kicking.Speed(w, power), flat = speed * Mathf.Cos(climb), vy = speed * Mathf.Sin(climb);
            bool goal = Kicking.Mode(w) == KickKind.FieldGoal;
            float posts = Mathf.Abs(dir * w.Cfg.field.HalfLength - bx);

            // the flight, one point every EveryS seconds, until it comes down (a field goal: just past the posts)
            int n = 0;
            float land = 0f;
            for (float t = 0f; n < ArcRibbon.Max; t += EveryS)
            {
                float y = c.kickHeightYd + vy * t - 0.5f * g * t * t, d = flat * t;
                if (y < 0f || goal && d > posts + PastPostsYd) break;
                air.Path[n++] = new Vector3((bx + ux * d) * m, y * m, (bz + uz * d) * m);
                land = d;
            }
            air.Draw(n);

            // the arrow on the grass, from the ball to under where it comes down, and its head
            float len = land * m;
            int gn = 0;
            for (float s = 0f; gn < ArcRibbon.Max - 1 && s < len - HeadLenM * 0.5f; s += GroundEveryM)
                ground.Path[gn++] = new Vector3(bx * m + ux * s, Lift, bz * m + uz * s);
            ground.Draw(gn);
            var tint = stage == Kicking.SweepStage.Power && power >= c.shankFromPower ? Hot : Gold;
            Head(new Vector3(bx * m + ux * len, Lift, bz * m + uz * len), new Vector3(ux, 0f, uz), tint);
        }

        // A flat arrowhead on the grass, its tip at `tip`, pointing along `fwd`.
        void Head(Vector3 tip, Vector3 fwd, Color tint)
        {
            var side = Vector3.Cross(Vector3.up, fwd).normalized;
            var back = tip - fwd * HeadLenM;
            headV[0] = tip;
            headV[1] = back + side * HeadHalfM;
            headV[2] = back - side * HeadHalfM;
            headV[3] = back + fwd * (HeadLenM * 0.3f);
            headC[0] = Looks.VertexColor(tint, 0.95f);
            headC[1] = Looks.VertexColor(tint, 0.35f);
            headC[2] = Looks.VertexColor(tint, 0.35f);
            headC[3] = Looks.VertexColor(tint, 0.9f);
            headMesh.Clear();
            headMesh.vertices = headV;
            headMesh.colors = headC;
            headMesh.triangles = new[] { 0, 3, 1, 0, 2, 3, 0, 1, 3, 0, 3, 2 };
            headMesh.RecalculateBounds();
            head.SetActive(true);
        }

        void Hide()
        {
            air?.Hide();
            ground?.Hide();
            if (head != null) head.SetActive(false);
        }
    }
}
