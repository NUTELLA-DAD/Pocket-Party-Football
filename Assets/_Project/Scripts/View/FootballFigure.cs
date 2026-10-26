using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // A chibi football player (docs/art/ART_DIRECTION.md): about 2.5 heads tall, a big glossy helmet with a white
    // facemask and the team mark, big eyes, the jersey number on chest and back, white pants with team stripes, team
    // socks and cleats. Linemen are wide and bulky with black gloves and elbow bands; QBs and kickers slim.
    // Built from code with FigureKit (rig units = cm, facing +x, left = +z) as ONE skinned mesh with 11 rigid bones;
    // poses are bone angles, stepped (no time-based wobble).
    public sealed class FootballFigure : MonoBehaviour
    {
        public enum Build { Slim, Mid, Big }
        public enum PoseKind
        {
            Stand, Ready, Stance, Block, Throw, Run, Carry, Backpedal, Dive, Catch,
            // the moves (Moves.cs): frame picks the key where a move has more than one
            Release, Handoff, Reach, Secure, Bobble, Juke, Whiff, FallFront, FallBack, Prone, Supine, Kneel, Celebrate,
        }

        public Transform Hips, Torso, Head, ArmL, ForeL, ArmR, ForeR, ThighL, ShinL, ThighR, ShinR, HandR, HandL;
        float hipY;

        // per-team trims the config doesn't carry (view only)
        sealed class Style { public bool HelmetStripe, SleeveStripes; public Color Stripe, Stripe2, NumberEdge; }
        static Style StyleOf(TeamConfig t)
        {
            Color c = Colors.Hex(t.color), c2 = Colors.Hex(t.color2);
            if (t.@short == "KC") return new Style { HelmetStripe = false, SleeveStripes = true, Stripe = c, Stripe2 = Colors.Hex("#ffb612"), NumberEdge = Colors.Hex("#ffb612") };
            return new Style { HelmetStripe = true, SleeveStripes = true, Stripe = c, Stripe2 = c, NumberEdge = c };
        }

        static readonly string[] Skins = { "#f2c8a5", "#e0a77f", "#c0855a", "#9a6240", "#74462b" };

        // The players' material: the shared toon look with a warmer, same-hue shade (view.playerShade*), one instance.
        static Material kitMat;
        static Material kitFrom;
        public static Material KitMaterial(Material toon, ViewConfig v)
        {
            if (toon == null || string.IsNullOrEmpty(v.playerShadeHue)) return toon;
            if (kitMat != null && kitFrom == toon) return kitMat;
            kitFrom = toon;
            kitMat = new Material(toon) { name = "Toon (players)" };
            kitMat.SetColor("_ShadeHue", Colors.Hex(v.playerShadeHue));
            kitMat.SetFloat("_ShadeUnify", v.playerShadeUnify);
            if (v.playerShadeSaturation > 0f) kitMat.SetFloat("_ShadeSaturation", v.playerShadeSaturation);
            if (!string.IsNullOrEmpty(v.playerShadeLift)) kitMat.SetColor("_ShadeLift", Colors.Hex(v.playerShadeLift));
            if (!string.IsNullOrEmpty(v.playerBounce)) kitMat.SetColor("_BounceColor", Colors.Hex(v.playerBounce));
            return kitMat;
        }

        public static Build BuildOf(string pos) => pos == "OL" || pos == "DL" ? Build.Big : pos == "QB" || pos == "K" ? Build.Slim : Build.Mid;

        public static FootballFigure Create(Transform parent, TeamConfig teamCfg, int number, Build build, int seed, float heightM, Material mat, Texture2D mark)
        {
            const float RigHeight = 182f;
            var holder = new GameObject("Figure #" + number).transform;
            holder.SetParent(parent, false);
            holder.localScale = Vector3.one * heightM / RigHeight;
            var fig = holder.gameObject.AddComponent<FootballFigure>();
            var st = StyleOf(teamCfg);
            float W = build == Build.Big ? 1.55f : build == Build.Slim ? 0.98f : 1.12f;
            float T = build == Build.Big ? 1.5f : build == Build.Slim ? 0.98f : 1.18f;
            // gauntlet 3 (concept 01): a stubby chibi build, short thick legs, a deep chest, pads wider than the big helmet
            float hipY = 60f, kneeY = 31f, ankleY = 8f, neckY = 115f, shY = 108f;
            fig.hipY = hipY;
            float legZ = 10.5f * W, armZ = 25.5f * W;
            float elbowY = shY - 22f, wristY = shY - 39f, handY = shY - 45f;

            Transform Bone(string n, Transform p, Vector3 worldAt)
            {
                var t = new GameObject(n).transform;
                t.SetParent(p, false);
                t.position = holder.TransformPoint(worldAt);
                return t;
            }
            fig.Hips = Bone("Hips", holder, new Vector3(0, hipY, 0));
            fig.Torso = Bone("Torso", fig.Hips, new Vector3(0, hipY + 6, 0));
            fig.Head = Bone("Head", fig.Torso, new Vector3(0, neckY, 0));
            fig.ArmL = Bone("ArmL", fig.Torso, new Vector3(0, shY, armZ));
            fig.ForeL = Bone("ForeL", fig.ArmL, new Vector3(2, elbowY, armZ + 3 * W));
            fig.ArmR = Bone("ArmR", fig.Torso, new Vector3(0, shY, -armZ));
            fig.ForeR = Bone("ForeR", fig.ArmR, new Vector3(2, elbowY, -armZ - 3 * W));
            fig.ThighL = Bone("ThighL", fig.Hips, new Vector3(0, hipY, legZ));
            fig.ShinL = Bone("ShinL", fig.ThighL, new Vector3(0, kneeY, legZ));
            fig.ThighR = Bone("ThighR", fig.Hips, new Vector3(0, hipY, -legZ));
            fig.ShinR = Bone("ShinR", fig.ThighR, new Vector3(0, kneeY, -legZ));
            fig.HandL = Bone("HandL", fig.ForeL, new Vector3(7, handY, armZ + 3 * W));
            fig.HandR = Bone("HandR", fig.ForeR, new Vector3(7, handY, -armZ - 3 * W));
            var bones = new[] { fig.Hips, fig.Torso, fig.Head, fig.ArmL, fig.ForeL, fig.ArmR, fig.ForeR, fig.ThighL, fig.ShinL, fig.ThighR, fig.ShinR };
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * holder.localToWorldMatrix;

            Color team = Colors.Hex(teamCfg.color), white = Colors.Hex("#f4f2ec"), black = Colors.Hex("#1e1e22");
            Color skin = Colors.Hex(Skins[Mathf.Abs(seed) % Skins.Length]);
            Color glove = build == Build.Big ? black : team;
            var k = new FigureKit();
            void On(int b) { k.SetBone(b, Matrix4x4.identity); k.Flag = FigureKit.Matte; }

            // ---- legs ----
            foreach (float s in new[] { 1f, -1f })
            {
                float z = s * legZ;
                On(s > 0 ? 8 : 10);   // shins
                k.Limb(new Vector3(0, kneeY + 2, z), new Vector3(2, ankleY + 4, z), 8.8f * T, 7.2f * T, team);                 // sock
                k.Ellipsoid(new Vector3(6, 6.5f, z), new Vector3(14.5f, 7.2f, 8.6f) * Mathf.Lerp(1f, T, 0.5f), team);            // cleat
                k.Ellipsoid(new Vector3(6, 2.6f, z), new Vector3(15f, 3f, 9f) * Mathf.Lerp(1f, T, 0.5f), white);                // sole
                On(s > 0 ? 7 : 9);    // thighs
                k.Limb(new Vector3(0, hipY, z), new Vector3(1, kneeY + 1, z), 13.5f * T, 11f * T, white);                       // pants
                k.Ellipsoid(new Vector3(6, kneeY + 4, z), new Vector3(7.5f, 7.5f, 8.5f) * T, white);                             // knee pad
                k.Limb(new Vector3(0, hipY - 2, z + s * 12.8f * T), new Vector3(1, kneeY + 3, z + s * 10.6f * T), 2.2f, 2f, st.Stripe);   // stripes
                if (st.Stripe2 != st.Stripe)
                    k.Limb(new Vector3(-2.8f, hipY - 2, z + s * 12.5f * T), new Vector3(-1.8f, kneeY + 3, z + s * 10.3f * T), 1.6f, 1.5f, st.Stripe2);
            }

            // ---- hips and torso ----
            On(0);
            k.Ellipsoid(new Vector3(0, hipY + 5, 0), new Vector3(17f * W, 12f, 21.5f * W), white);
            k.Ellipsoid(new Vector3(0, hipY + 11, 0), new Vector3(17.3f * W, 2.4f, 21.8f * W), Colors.Hex("#2b2b30"));     // belt
            On(1);
            // a broad chest that tapers to the belt (gauntlet 3: the pads, not the helmet, set the width)
            var body = new FigureKit.Profile(Vector3.zero, new[]
            {
                hipY + 10, 16.5f * W, 20f * W,
                hipY + 20, 18.5f * W, 22.5f * W,
                95f, 20.5f * W, 25f * W,
                104f, 20f * W, 24.5f * W,
                111f, 13.5f * W, 16.5f * W,
                116f, 5f, 6f,
            });
            k.Lathe(body, team, 28);
            // big shoulder pads, wider than the helmet, with the "TV numbers" on top where the camera sees them
            string num = number.ToString();
            foreach (float s in new[] { 1f, -1f })
            {
                float padW = Mathf.Max(W, 1.12f);   // (QBs and kickers get full-size pads: never narrower than the helmet)
                var c = new Vector3(0, shY + 2, s * 20f * padW);
                var r = new Vector3(18f * padW, 12.5f * T, 18f * padW);
                k.Ellipsoid(c, r, team);
                var top = FigureKit.OnEllipsoid(c, r, Quaternion.identity, new Vector3(0, 0.55f, s), s);
                Number(k, top, num, 12.5f * Mathf.Lerp(1f, padW, 0.5f), 3.4f, 0f, 0f, st.NumberEdge, white);
            }
            // the numbers, chest and back: big, white with a team-trim edge (the back reads left to right from behind)
            Number(k, FigureKit.OnLathe(body, 0f), num, 21f, 5.4f, 0f, 85.5f, st.NumberEdge, white);
            Number(k, FigureKit.OnLathe(body, 180f), num, 23f, 5.6f, 0f, 87f, st.NumberEdge, white);
            k.Limb(new Vector3(0, neckY - 6, 0), new Vector3(1, neckY + 6, 0), 7f * T, 6.5f * T, skin);   // neck

            // ---- arms ----
            foreach (float s in new[] { 1f, -1f })
            {
                float z = s * armZ;
                On(s > 0 ? 3 : 5);
                k.Limb(new Vector3(0, shY, z), new Vector3(1, shY - 12, z + s * 1.5f * W), 11f * T, 10f * T, team);              // sleeve
                if (st.SleeveStripes)
                {
                    // two bands round the sleeve's cuff (Chiefs red-gold-white, Roughriders white)
                    k.Limb(new Vector3(0.8f, shY - 7.5f, z + s * 1.1f * W), new Vector3(0.9f, shY - 9f, z + s * 1.2f * W), 10.6f * T, 10.55f * T, st.Stripe2 != st.Stripe ? st.Stripe2 : white, 12, false, false);
                    k.Limb(new Vector3(0.9f, shY - 10f, z + s * 1.3f * W), new Vector3(1f, shY - 11.4f, z + s * 1.45f * W), 10.4f * T, 10.35f * T, white, 12, false, false);
                }
                k.Limb(new Vector3(1, shY - 10, z + s * 1.5f * W), new Vector3(2, elbowY, z + s * 3 * W), 9.6f * T, 8.6f * T, skin);   // upper arm
                if (build == Build.Big) k.Limb(new Vector3(1.6f, elbowY + 5, z + s * 2.3f * W), new Vector3(2, elbowY + 0.5f, z + s * 3 * W), 9.2f * T, 8.9f * T, black);
                On(s > 0 ? 4 : 6);
                k.Limb(new Vector3(2, elbowY, z + s * 3 * W), new Vector3(6, wristY + 1, z + s * 3 * W), 8.4f * T, 7f * T, skin);   // forearm
                if (build != Build.Big) k.Limb(new Vector3(5.2f, wristY + 5, z + s * 3 * W), new Vector3(6, wristY + 0.5f, z + s * 3 * W), 7.4f * T, 7.3f * T, white);   // wristband
                else k.Limb(new Vector3(3, elbowY - 6, z + s * 3 * W), new Vector3(4.6f, elbowY - 12, z + s * 3 * W), 8.4f * T, 8f * T, black);
                // the glove: a palm, three fat fingers curling forward and a thumb turned in (concept 01's mitts)
                float gz = z + s * 3 * W;
                k.Ellipsoid(new Vector3(7.5f, handY + 2f, gz), new Vector3(8.5f, 8.5f, 7.5f) * T, glove);
                foreach (float f in new[] { -1f, 0f, 1f })
                    k.Limb(new Vector3(9f, handY - 2f, gz + f * 2.9f * T), new Vector3(12.5f, handY - 9.5f, gz + f * 3.1f * T), 2.7f * T, 2.4f * T, glove, 8);
                k.Limb(new Vector3(11f, handY + 3f, gz - s * 3f * T), new Vector3(15.5f, handY - 1f, gz - s * 5.5f * T), 2.8f * T, 2.4f * T, glove, 8);
            }

            // ---- the head: face, eyes, helmet, facemask ----
            // (modelled on a neck at 120 cm; HeadAt moves it onto this neck at HeadScale)
            On(2);
            k.M = HeadAt(neckY);
            var faceC = new Vector3(8, 141, 0);
            var faceR = new Vector3(17, 19, 18.5f);
            k.Ellipsoid(faceC, faceR, skin, 12, 20);
            Color iris = Colors.Hex("#6b3e22");
            foreach (float s in new[] { 1f, -1f })
            {
                var eye = FigureKit.OnEllipsoid(faceC, faceR, Quaternion.identity, new Vector3(1f, 0.05f, 0f), 1f);
                float x = s * 8.2f, y = 1.5f;
                // (concept 01: big whites, a brown iris, a small pupil and a glint, under a heavy slanted brow)
                k.Spot(eye, new Vector2(x, y), new Vector2(6.6f, 7.8f), 0, 0.3f, white, 16, FigureKit.Gloss);
                k.Spot(eye, new Vector2(x + s * 0.8f, y - 0.6f), new Vector2(4.4f, 5.6f), 0, 0.55f, iris, 14, FigureKit.Paint);
                k.Spot(eye, new Vector2(x + s * 0.8f, y - 0.8f), new Vector2(2f, 2.8f), 0, 0.7f, black, 12, FigureKit.Paint);
                k.Spot(eye, new Vector2(x - s * 0.6f, y + 1.8f), new Vector2(1.5f, 1.6f), 0, 0.85f, white, 10, FigureKit.Gloss);
                // a determined brow: thick, low at the nose, rising outward
                k.Stroke(eye, new[] { new Vector2(x - s * 6f, y + 6f), new Vector2(x + s * 5.5f, y + 9.6f) }, new[] { 3.8f, 2.6f }, 0.5f, Colors.Hex("#2e2018"));
            }
            var mouth = FigureKit.OnEllipsoid(faceC, faceR, Quaternion.identity, new Vector3(1f, -0.35f, 0f), 1f);
            k.Stroke(mouth, new[] { new Vector2(-3.5f, 0.8f), new Vector2(0, -0.4f), new Vector2(3.5f, 0.8f) }, new[] { 1.2f, 1.5f, 1.2f }, 0.4f, Colors.Hex("#5a2a20"));

            // the helmet: a big shell open at the front (its pole points forward and a little down)
            var helmetC = new Vector3(0, 150, 0);
            var helmetR = new Vector3(32, 31, 31);
            var q = Quaternion.FromToRotation(Vector3.up, new Vector3(1f, -0.42f, 0f).normalized);
            k.Flag = FigureKit.Shiny;
            k.Ellipsoid(helmetC, helmetR, q, team, 16, 28, 54f, 180f);
            k.Flag = FigureKit.Matte;
            if (st.HelmetStripe)
            {
                var top = FigureKit.OnEllipsoid(helmetC, helmetR, Quaternion.identity, Vector3.up, 1f, Vector3.right);
                k.Stroke(top, new[] { new Vector2(0, 22), new Vector2(0, 0), new Vector2(0, -26), new Vector2(0, -44) }, new[] { 9.5f, 10f, 10f, 9.5f }, 0.5f, white);
            }
            // facemask: two cross bars low on the face (below the eyes, concept 01) and one upright down the middle
            float fm = 2.2f;
            k.Tube(new[] { new Vector3(16, 136, -26), new Vector3(29, 133, -15), new Vector3(33, 132, 0), new Vector3(29, 133, 15), new Vector3(16, 136, 26) },
                   Rad(5, fm), Cols(5, white), 8);
            k.Tube(new[] { new Vector3(13, 126, -20), new Vector3(24, 122, -10), new Vector3(28, 121, 0), new Vector3(24, 122, 10), new Vector3(13, 126, 20) },
                   Rad(5, fm), Cols(5, white), 8);
            k.Limb(new Vector3(33, 132, 0), new Vector3(28, 121, 0), fm, fm, white, 8);

            var mesh = k.Build("Player_" + teamCfg.@short + "_" + number, bind);
            var smr = holder.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = fig.Hips;
            smr.sharedMaterial = mat;
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(new Vector3(0, 0, 0), new Vector3(260, 260, 260));
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // the team mark on both sides of the helmet: one replaceable texture (TeamMarks)
            if (mark != null)
            {
                var decal = new Material(mat) { name = "Mark " + teamCfg.@short };
                decal.SetTexture("_MainTex", mark);
                decal.SetColor("_PlayerColor", team);
                decal.SetFloat("_Specular", 0.6f);
                // (big, wrapped round the shell like the concept's; read left to right on both sides, as in concept 01)
                var toHead = fig.Head.worldToLocalMatrix * holder.localToWorldMatrix * HeadAt(neckY);
                foreach (float s in new[] { 1f, -1f })
                    HelmetMark(fig.Head, toHead, decal, s, false);
            }
            fig.Pose(PoseKind.Ready, 0);
            return fig;
        }

        // The head is modelled round a neck at 120 cm; this places it on the real neck at HeadScale (gauntlet 3: the
        // concept 01 chibi, a helmet about a third of the height, still narrower than the shoulder pads).
        const float HeadScale = 1.08f;
        static Matrix4x4 HeadAt(float neckY) =>
            Matrix4x4.Translate(new Vector3(0, neckY, 0)) * Matrix4x4.Scale(Vector3.one * HeadScale) * Matrix4x4.Translate(new Vector3(0, -120f, 0));

        // A team mark laid onto the side of the helmet (side +1: the left, +z), bent to the shell: a grid of points on
        // the helmet sphere (head-model units, round (0, 150, 0)), lifted a hair off it, with the texture's uv; flip
        // mirrors it left to right. toHead: head-model space -> the head bone's space.
        static void HelmetMark(Transform head, Matrix4x4 toHead, Material decal, float side, bool flip)
        {
            const int N = 8;
            const float Size = 42f, Radius = 31f, Lift = 0.9f;
            var c = new Vector3(0, 150, 0);
            Vector3 n = new Vector3(0.12f, 0.16f, side).normalized;   // (a little forward, where the 3/4 view shows it)
            Vector3 v = (Vector3.up - n * Vector3.Dot(Vector3.up, n)).normalized;
            Vector3 u = Vector3.Cross(n, v).normalized;   // the viewer's right, seen from that side
            var verts = new Vector3[(N + 1) * (N + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var cols = new Color[verts.Length];
            var col = QualitySettings.activeColorSpace == ColorSpace.Linear ? Color.white.linear : Color.white;
            col.a = FigureKit.Gloss;
            var nm = toHead.inverse.transpose;
            for (int i = 0; i <= N; i++)
                for (int j = 0; j <= N; j++)
                {
                    float a = (i / (float)N - 0.5f) * Size, b = (j / (float)N - 0.5f) * Size;
                    Vector3 d = (n * Radius + u * a + v * b).normalized;
                    int ix = i * (N + 1) + j;
                    verts[ix] = toHead.MultiplyPoint3x4(c + d * (Radius + Lift));
                    norms[ix] = nm.MultiplyVector(d).normalized;
                    uvs[ix] = new Vector2(flip ? 1f - i / (float)N : i / (float)N, j / (float)N);
                    cols[ix] = col;
                }
            var tris = new List<int>();
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    int a = i * (N + 1) + j, b = a + 1, cc = a + N + 1, dd = cc + 1;
                    foreach (var t in new[] { (a, cc, dd), (a, dd, b) })
                    {
                        int p0 = t.Item1, p1 = t.Item2, p2 = t.Item3;
                        // (front = cross(b - a, c - a) along the outward normal, as FigureKit builds them)
                        if (Vector3.Dot(Vector3.Cross(verts[p1] - verts[p0], verts[p2] - verts[p0]), norms[p0]) < 0) { int sw = p1; p1 = p2; p2 = sw; }
                        tris.Add(p0); tris.Add(p1); tris.Add(p2);
                    }
                }
            var m = new Mesh { name = "HelmetMark" };
            m.vertices = verts; m.normals = norms; m.uv = uvs; m.colors = cols;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            var go = new GameObject("Mark");
            go.transform.SetParent(head, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = decal;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A jersey number painted on a surface: white strokes over a wider team-trim edge, centred on (cx, cy).
        static void Number(FigureKit k, FigureKit.Surface surf, string num, float hgt, float sw, float cx, float cy, Color edge, Color fill)
        {
            var strokes = Glyphs.Text(num, hgt, 0.18f, out float w);
            foreach (var stroke in strokes)
            {
                var pts = new Vector2[stroke.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(stroke[i].x - w * 0.5f + cx, stroke[i].y - hgt * 0.5f + cy);
                var wid = new float[pts.Length];
                for (int i = 0; i < wid.Length; i++) wid[i] = sw + 1.8f;
                k.Stroke(surf, pts, wid, 0.35f, edge, 2);
                for (int i = 0; i < wid.Length; i++) wid[i] = sw;
                k.Stroke(surf, pts, wid, 0.6f, fill, 2);
            }
        }

        static Vector2[] Rad(int n, float r) { var a = new Vector2[n]; for (int i = 0; i < n; i++) a[i] = new Vector2(r, r); return a; }
        static Color[] Cols(int n, Color c) { var a = new Color[n]; for (int i = 0; i < n; i++) a[i] = c; return a; }

        // ---- poses: bone angles in degrees. Z turns a limb forward (+) / back (-); X spreads it sideways. ----
        // drop lowers the hips (cm; negative = a hop); tilt tips the whole body over at the hips (+ onto his front,
        // - onto his back), for the falls and lying on the grass.
        struct P
        {
            public float drop, tilt, lean, head, armL, armLx, foreL, armR, armRx, foreR, thighL, thighLx, shinL, thighR, thighRx, shinR;
        }

        // running legs for the moves done on the run (reach, secure): the Run pose's, by stride frame
        static P RunLegs(P p, int frame)
        {
            var r = Get(PoseKind.Run, frame);
            p.thighL = r.thighL; p.thighLx = r.thighLx; p.shinL = r.shinL;
            p.thighR = r.thighR; p.thighRx = r.thighRx; p.shinR = r.shinR;
            return p;
        }

        static P Get(PoseKind k, int frame)
        {
            switch (k)
            {
                case PoseKind.Stand: return new P { armL = 4, armLx = 10, foreL = 12, armR = 4, armRx = 10, foreR = 12, thighLx = 3, thighRx = 3 };
                case PoseKind.Ready: return new P { drop = 13, lean = 24, head = -14, armL = 25, armLx = 18, foreL = 45, armR = 25, armRx = 18, foreR = 45, thighL = 18, thighLx = 9, shinL = -30, thighR = 18, thighRx = 9, shinR = -30 };
                // a lineman's set (concept 01's 68 and 64): hips low and knees wide, chest up so the number shows, gloves out wide
                case PoseKind.Stance: return new P { drop = 23, lean = 26, head = -12, armL = 50, armLx = 48, foreL = 42, armR = 50, armRx = 48, foreR = 42, thighL = 62, thighLx = 30, shinL = -80, thighR = 62, thighRx = 30, shinR = -80 };
                case PoseKind.Block: return frame % 2 == 0
                        ? new P { drop = 20, lean = 42, head = -30, armL = 82, armLx = 10, foreL = 12, armR = 82, armRx = 10, foreR = 12, thighL = 48, thighLx = 10, shinL = -55, thighR = -12, thighRx = 10, shinR = -24 }
                        : new P { drop = 18, lean = 46, head = -32, armL = 92, armLx = 10, foreL = 4, armR = 92, armRx = 10, foreR = 4, thighL = -12, thighLx = 10, shinL = -24, thighR = 48, thighRx = 10, shinR = -55 };   // the shove: arms out, legs driving
                case PoseKind.Throw: return new P { drop = 4, lean = -6, head = 0, armL = 62, armLx = 20, foreL = 30, armR = -150, armRx = 22, foreR = 70, thighL = 24, thighLx = 6, shinL = -18, thighR = -18, thighRx = 8, shinR = -12 };
                case PoseKind.Carry: return frame % 2 == 0
                        ? new P { drop = 6, lean = 20, head = -10, armL = 30, armLx = 8, foreL = 100, armR = -30, armRx = 12, foreR = 70, thighL = 48, thighLx = 4, shinL = -70, thighR = -28, thighRx = 4, shinR = -40 }
                        : new P { drop = 6, lean = 20, head = -10, armL = 30, armLx = 8, foreL = 100, armR = 35, armRx = 12, foreR = 70, thighL = -28, thighLx = 4, shinL = -40, thighR = 48, thighRx = 4, shinR = -70 };
                case PoseKind.Run: return frame % 2 == 0
                        ? new P { drop = 5, lean = 16, head = -8, armL = -38, armLx = 12, foreL = 75, armR = 45, armRx = 12, foreR = 75, thighL = 46, thighLx = 4, shinL = -66, thighR = -26, thighRx = 4, shinR = -42 }
                        : new P { drop = 5, lean = 16, head = -8, armL = 45, armLx = 12, foreL = 75, armR = -38, armRx = 12, foreR = 75, thighL = -26, thighLx = 4, shinL = -42, thighR = 46, thighRx = 4, shinR = -66 };
                case PoseKind.Backpedal: return new P { drop = 15, lean = 24, head = -16, armL = 30, armLx = 22, foreL = 60, armR = 30, armRx = 22, foreR = 60, thighL = frame % 2 == 0 ? 30 : 6, thighLx = 12, shinL = -40, thighR = frame % 2 == 0 ? 6 : 30, thighRx = 12, shinR = -40 };
                case PoseKind.Dive: return new P { drop = -8, lean = 70, head = -40, armL = 160, armLx = 14, foreL = 10, armR = 160, armRx = 14, foreR = 10, thighL = -30, thighLx = 6, shinL = -30, thighR = -45, thighRx = 6, shinR = -10 };
                case PoseKind.Catch: return new P { drop = 2, lean = -10, head = 18, armL = 165, armLx = 12, foreL = 20, armR = 165, armRx = 12, foreR = 20, thighL = 30, thighLx = 6, shinL = -50, thighR = -10, thighRx = 6, shinR = -20 };
                // the throw: 0 the arm comes over the top, 1 the follow-through across the body
                case PoseKind.Release: return frame == 0
                        ? new P { drop = 6, lean = 18, head = -8, armL = -30, armLx = 20, foreL = 40, armR = 150, armRx = 15, foreR = 20, thighL = 30, thighLx = 6, shinL = -20, thighR = -25, thighRx = 8, shinR = -15 }
                        : new P { drop = 10, lean = 32, head = -14, armL = -40, armLx = 20, foreL = 60, armR = 50, armRx = 30, foreR = 30, thighL = 35, thighLx = 6, shinL = -30, thighR = -30, thighRx = 8, shinR = -40 };
                case PoseKind.Handoff: return new P { drop = 10, lean = 22, head = -10, armL = 60, armLx = 25, foreL = 40, armR = 70, armRx = 25, foreR = 30, thighL = 24, thighLx = 9, shinL = -30, thighR = 10, thighRx = 9, shinR = -28 };
                // hands up for a pass coming in, on the run
                case PoseKind.Reach: return RunLegs(new P { drop = 4, lean = 4, head = -20, armL = 140, armLx = 14, foreL = 25, armR = 140, armRx = 14, foreR = 25 }, frame);
                // caught: the ball pulled in to the chest with both arms
                case PoseKind.Secure: return RunLegs(new P { drop = 8, lean = 20, head = -6, armL = 60, armLx = 6, foreL = 120, armR = 50, armRx = 8, foreR = 110 }, frame);
                // a drop: 0 hands wide as it pops out, 1 head down
                case PoseKind.Bobble: return frame == 0
                        ? new P { drop = 4, lean = -8, head = -20, armL = 150, armLx = 45, foreL = 30, armR = 150, armRx = 45, foreR = 30, thighL = 20, thighLx = 6, shinL = -30, thighR = -6, thighRx = 6, shinR = -20 }
                        : new P { drop = 6, lean = 30, head = 20, armL = -5, armLx = 8, foreL = 10, armR = -5, armRx = 8, foreR = 10, thighL = 8, thighLx = 4, shinL = -10, thighR = 8, thighRx = 4, shinR = -10 };
                // a juke: 0 the plant, low and wide, the free arm out; 1 the push off the other way
                case PoseKind.Juke: return frame == 0
                        ? new P { drop = 18, lean = 22, head = -14, armL = 50, armLx = 55, foreL = 20, armR = -20, armRx = 12, foreR = 90, thighL = 30, thighLx = 30, shinL = -40, thighR = -10, thighRx = 25, shinR = -30 }
                        : new P { drop = 10, lean = 28, head = -16, armL = 30, armLx = 40, foreL = 40, armR = -20, armRx = 12, foreR = 90, thighL = -20, thighLx = 10, shinL = -50, thighR = 50, thighRx = 5, shinR = -70 };
                // a missed tackle: arms everywhere, stumbling steps
                case PoseKind.Whiff: return frame % 2 == 0
                        ? new P { drop = 10, tilt = 25, lean = 0, head = -20, armL = 120, armLx = 40, foreL = 30, armR = -40, armRx = 40, foreR = 50, thighL = 50, thighLx = 6, shinL = -40, thighR = -30, thighRx = 6, shinR = -60 }
                        : new P { drop = 10, tilt = 20, lean = 0, head = -20, armL = -40, armLx = 40, foreL = 50, armR = 120, armRx = 40, foreR = 30, thighL = -30, thighLx = 6, shinL = -60, thighR = 50, thighRx = 6, shinR = -40 };
                // going down: 0 the hit, 1 halfway to the grass
                case PoseKind.FallFront: return frame == 0
                        ? new P { drop = 6, tilt = 25, head = -20, armL = 120, armLx = 30, foreL = 30, armR = 100, armRx = 30, foreR = 40, thighL = 10, thighLx = 6, shinL = -30, thighR = -20, thighRx = 6, shinR = -50 }
                        : new P { drop = 22, tilt = 50, head = -30, armL = 150, armLx = 18, foreL = 20, armR = 150, armRx = 18, foreR = 20, thighL = 0, thighLx = 6, shinL = -40, thighR = -10, thighRx = 6, shinR = -60 };
                case PoseKind.FallBack: return frame == 0
                        ? new P { drop = 6, tilt = -20, head = 20, armL = 70, armLx = 50, foreL = 40, armR = 70, armRx = 50, foreR = 40, thighL = 30, thighLx = 8, shinL = -20, thighR = 10, thighRx = 8, shinR = -10 }
                        : new P { drop = 22, tilt = -45, head = 25, armL = 60, armLx = 65, foreL = 30, armR = 60, armRx = 65, foreR = 30, thighL = 40, thighLx = 10, shinL = -40, thighR = 20, thighRx = 10, shinR = -30 };
                // on the grass: face down, arms stretched out ahead (the carrier still has the ball), feet kicked up
                case PoseKind.Prone: return new P { drop = 42, tilt = 80, head = -40, armL = 170, armLx = 16, foreL = 10, armR = 170, armRx = 16, foreR = 10, thighL = -10, thighLx = 8, shinL = -50, thighR = -10, thighRx = 12, shinR = -30 };
                // on the grass: flat on his back, arms flung out wide on the grass, knees up
                case PoseKind.Supine: return new P { drop = 46, tilt = -88, head = 20, armL = -25, armLx = 75, foreL = 15, armR = -25, armRx = 75, foreR = 15, thighL = 40, thighLx = 10, shinL = -90, thighR = 30, thighRx = 14, shinR = -70 };
                // getting up: one knee down
                case PoseKind.Kneel: return new P { drop = 30, tilt = 15, head = -10, armL = 60, armLx = 10, foreL = 40, armR = 30, armRx = 14, foreR = 60, thighL = 80, thighLx = 6, shinL = -90, thighR = -10, thighRx = 6, shinR = -100 };
                // a touchdown: 0 a hop with both arms up, 1 the ball held high and a fist pump
                case PoseKind.Celebrate: return frame % 2 == 0
                        ? new P { drop = -6, lean = -6, head = -16, armL = 175, armLx = 20, foreL = 10, armR = 175, armRx = 20, foreR = 10, thighL = 30, thighLx = 6, shinL = -60, thighR = 10, thighRx = 6, shinR = -40 }
                        : new P { drop = 0, lean = 0, head = -10, armL = 40, armLx = 40, foreL = 80, armR = 170, armRx = 15, foreR = 10, thighL = 4, thighLx = 4, shinL = -6, thighR = 4, thighRx = 4, shinR = -6 };
            }
            return default;
        }

        PoseKind current = (PoseKind)(-1);
        int currentFrame = -1;

        public void Pose(PoseKind kind, int frame)
        {
            if (kind == current && frame == currentFrame) return;
            current = kind; currentFrame = frame;
            var p = Get(kind, frame);
            Hips.localPosition = new Vector3(0, hipY - p.drop, 0);
            Hips.localRotation = Quaternion.Euler(0, 0, -p.tilt);
            Torso.localRotation = Quaternion.Euler(0, 0, -p.lean);
            Head.localRotation = Quaternion.Euler(0, 0, -p.head);
            ArmL.localRotation = Quaternion.Euler(-p.armLx, 0, p.armL);
            ArmR.localRotation = Quaternion.Euler(p.armRx, 0, p.armR);
            ForeL.localRotation = Quaternion.Euler(0, 0, p.foreL);
            ForeR.localRotation = Quaternion.Euler(0, 0, p.foreR);
            ThighL.localRotation = Quaternion.Euler(-p.thighLx, 0, p.thighL);
            ThighR.localRotation = Quaternion.Euler(p.thighRx, 0, p.thighR);
            ShinL.localRotation = Quaternion.Euler(0, 0, p.shinL);
            ShinR.localRotation = Quaternion.Euler(0, 0, p.shinR);
        }

        public PoseKind Current => current;
        public int CurrentFrame => currentFrame;

        // A football: brown, pointed, white laces. Rig units (cm), long axis along +z.
        public static GameObject MakeBall(Transform parent, Material mat, float lengthM)
        {
            var k = new FigureKit();
            k.Flag = FigureKit.Shiny;
            var r = new Vector3(8.5f, 8.5f, 14f);
            k.Ellipsoid(Vector3.zero, r, Quaternion.identity, Colors.Hex("#8a4a24"), 12, 18);
            k.Flag = FigureKit.Matte;
            var top = FigureKit.OnEllipsoid(Vector3.zero, r, Quaternion.identity, Vector3.up, 1f, Vector3.forward);
            k.Stroke(top, new[] { new Vector2(0, -5), new Vector2(0, 5) }, new[] { 1.2f, 1.2f }, 0.3f, Color.white, 2, FigureKit.Paint);
            for (int i = -2; i <= 2; i++) k.Stroke(top, new[] { new Vector2(-1.8f, i * 2.2f), new Vector2(1.8f, i * 2.2f) }, new[] { 0.9f, 0.9f }, 0.35f, Color.white, 1, FigureKit.Paint);
            var go = Looks.Static(parent, "Ball", k, mat);
            go.transform.localScale = Vector3.one * lengthM / 28f;
            return go;
        }
    }
}
