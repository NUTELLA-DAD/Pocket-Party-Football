using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketFootball.View
{
    // Smooth, indexed, vertex-coloured shapes for the crew figures (LOOK.md L1), all gathered into ONE skinned mesh
    // per figure: every vertex belongs to exactly one bone (rigid parts, so one draw call per figure).
    // Shapes: ellipsoids (whole or a cap), lathes (bodies turned round the y axis), tubes swept along a path (limbs,
    // tails, ears, horns, scarves), and decals: thin painted shapes laid onto an ellipsoid or a lathe (eyes, brows,
    // mouths, stripes), lifted a hair off the surface and shaded with the surface's own normal.
    // Vertex alpha carries the toon shader's flags (L0): 1 outline + matte, 0.75 shiny, 0.5 shiny without outline,
    // 0.25 matte without outline (decals). Tangent xyz (w = 1): the outline direction, always the shape's TRUE surface
    // normal, so the ink hull stays an even closed shell even where the shading normal is bent (faces, lids, brows).
    public sealed class FigureKit
    {
        public const float Matte = 1f, Shiny = 0.75f, Gloss = 0.5f, Paint = 0.25f;

        readonly List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>(), odir = new List<Vector3>();
        readonly List<Color> col = new List<Color>();
        readonly List<int> tris = new List<int>();
        readonly List<int> bone = new List<int>();

        public int Bone;                                    // the bone the next shapes belong to
        public Matrix4x4 Rest = Matrix4x4.identity;         // that bone's space -> mesh space (at rest)
        public Matrix4x4 M = Matrix4x4.identity;            // extra placement of the next shapes, in bone space
        public float Flag = Matte;                          // vertex alpha for the next shapes

        public int TriangleCount => tris.Count / 3;

        public void SetBone(int index, Matrix4x4 rest) { Bone = index; Rest = rest; M = Matrix4x4.identity; ProxyW = 0f; FaceW = 0f; OutlineProxyW = 0f; }

        // The lit face: while FaceW > 0, normals on the front of a head (around FaceDir from FaceC) are pulled toward
        // FaceDir, most at the very front and fading out toward the sides, so the face stays lit almost flat and the
        // light/shade split sits behind the eyes, at the cheek edge; the back of the head keeps its normal split.
        public Vector3 FaceC, FaceDir = Vector3.right;
        public float FaceW;

        // Bent normals (the anime / island-adventure trick): while ProxyW > 0, the shading normal of every new vertex is
        // pulled toward the normal of a smooth proxy ellipsoid (centre ProxyC, radii ProxyR, in shape space), so a
        // muzzle, cheeks and brows shade as part of ONE round head: one clean light/shade split across the face.
        public Vector3 ProxyC, ProxyR = Vector3.one;
        public float ProxyW;
        public float OutlineProxyW;   // the outline direction pulled toward the proxy too (0..1)

        static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        int V(Vector3 p, Vector3 n, Color c, float flag)
        {
            var f = Rest * M;
            var on = n.normalized;
            if (OutlineProxyW > 0f)   // (a surface with creases: the ink shell follows a smooth convex proxy there, no slivers)
            {
                var d = p - ProxyC;
                on = Vector3.Lerp(on, new Vector3(d.x / (ProxyR.x * ProxyR.x), d.y / (ProxyR.y * ProxyR.y), d.z / (ProxyR.z * ProxyR.z)).normalized, OutlineProxyW).normalized;
            }
            odir.Add(f.inverse.transpose.MultiplyVector(on).normalized);
            if (ProxyW > 0f)
            {
                var d = p - ProxyC;
                var pn = new Vector3(d.x / (ProxyR.x * ProxyR.x), d.y / (ProxyR.y * ProxyR.y), d.z / (ProxyR.z * ProxyR.z)).normalized;
                n = Vector3.Lerp(n.normalized, pn, ProxyW);
            }
            if (FaceW > 0f)
            {
                float front = Mathf.Clamp01((Vector3.Dot((p - FaceC).normalized, FaceDir) + 0.05f) / 0.5f);
                n = Vector3.Lerp(n.normalized, FaceDir, FaceW * front * front * (3f - 2f * front));
            }
            pos.Add(f.MultiplyPoint3x4(p));
            nrm.Add(f.inverse.transpose.MultiplyVector(n).normalized);
            var l = Lin(c); l.a = flag;
            col.Add(l);
            bone.Add(Bone);
            return pos.Count - 1;
        }

        // a triangle facing along its vertices' normals (Unity: front = cross(b - a, c - a)); slivers are skipped
        void T(int a, int b, int c)
        {
            Vector3 cr = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            if (cr.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(cr, nrm[a] + nrm[b] + nrm[c]) < 0) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        // a (rows+1) x (cols+1) sheet of shared vertices
        void Grid(int rows, int cols, Func<int, int, (Vector3 p, Vector3 n)> at, Func<int, Color> rowColor, float flag)
        {
            int s = pos.Count;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++) { var (p, n) = at(r, c); V(p, n, rowColor(r), flag); }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i00 = s + r * (cols + 1) + c, i01 = i00 + 1, i10 = i00 + cols + 1, i11 = i10 + 1;
                    T(i00, i10, i11); T(i00, i11, i01);
                }
        }

        // ---- ellipsoids --------------------------------------------------------------------------------------
        // radii r round centre c, turned by q (its pole is q's y); th0..th1 (degrees from the top pole) cut a band or
        // a cap out of it
        public void Ellipsoid(Vector3 c, Vector3 r, Quaternion q, Color color, int rings = 10, int segs = 18, float th0 = 0f, float th1 = 180f)
        {
            Grid(rings, segs, (i, j) =>
            {
                float th = Mathf.Lerp(th0, th1, i / (float)rings) * Mathf.Deg2Rad, ph = j * Mathf.PI * 2f / segs;
                var u = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                return (c + q * Vector3.Scale(u, r), q * new Vector3(u.x / r.x, u.y / r.y, u.z / r.z));
            }, _ => color, Flag);
        }

        public void Ellipsoid(Vector3 c, Vector3 r, Color color, int rings = 10, int segs = 18) => Ellipsoid(c, r, Quaternion.identity, color, rings, segs);
        public void Ball(Vector3 c, float r, Color color, int rings = 6, int segs = 10) => Ellipsoid(c, new Vector3(r, r, r), Quaternion.identity, color, rings, segs);

        // ---- lathes ------------------------------------------------------------------------------------------
        // A body turned round the y axis through `c`: the profile gives, per height y, the radius ax toward +x and bz
        // toward +z (an elliptical section). Smoothed (Catmull-Rom) between the given points. ph0..ph1 (degrees,
        // 0 = +x, 90 = +z) open it (a coat's open front); `inner` then lines the inside in that colour.
        public sealed class Profile
        {
            public readonly List<float> Y = new List<float>(), A = new List<float>(), B = new List<float>();
            public readonly List<int> Src = new List<int>();   // the given point each dense point came from
            public Vector3 C;
            // yab: y, ax, bz per point. Two points at the same height make a crisp step (a band's edge): the curve is
            // kept straight next to a step so it never overshoots.
            public Profile(Vector3 c, float[] yab, int sub = 3)
            {
                C = c;
                int n = yab.Length / 3;
                bool Step(int i) => i >= 0 && i < n - 1 && Mathf.Abs(yab[i * 3] - yab[(i + 1) * 3]) < 1e-3f;
                for (int i = 0; i < n - 1; i++)
                {
                    bool straight = Step(i - 1) || Step(i) || Step(i + 1);
                    int ks = Step(i) ? 1 : sub;
                    for (int k = 0; k < ks; k++)
                    {
                        float t = k / (float)ks;
                        if (straight)
                        {
                            Y.Add(Mathf.Lerp(yab[i * 3], yab[i * 3 + 3], t)); A.Add(Mathf.Lerp(yab[i * 3 + 1], yab[i * 3 + 4], t)); B.Add(Mathf.Lerp(yab[i * 3 + 2], yab[i * 3 + 5], t));
                        }
                        else { Y.Add(CR(yab, i, t, 0)); A.Add(Mathf.Max(0f, CR(yab, i, t, 1))); B.Add(Mathf.Max(0f, CR(yab, i, t, 2))); }
                        Src.Add(i);
                    }
                }
                Y.Add(yab[(n - 1) * 3]); A.Add(yab[(n - 1) * 3 + 1]); B.Add(yab[(n - 1) * 3 + 2]); Src.Add(n - 1);
            }
            static float CR(float[] v, int i, float t, int o)
            {
                int n = v.Length / 3;
                float p0 = v[Mathf.Max(0, i - 1) * 3 + o], p1 = v[i * 3 + o], p2 = v[(i + 1) * 3 + o], p3 = v[Mathf.Min(n - 1, i + 2) * 3 + o];
                // a radius that reaches 0 at an end closes the body there: keep the curve from overshooting
                return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
            }
            // the surface at angle ph (radians) and profile index i
            public (Vector3 p, Vector3 n) At(int i, float ph)
            {
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(Y.Count - 1, i + 1);
                // one-sided next to a step, so a band's side keeps its own slope
                if (i0 < i && Mathf.Abs(Y[i] - Y[i0]) < 1e-3f) i0 = i;
                if (i1 > i && Mathf.Abs(Y[i1] - Y[i]) < 1e-3f) i1 = i;
                if (i0 == i1) { i0 = Mathf.Max(0, i - 1); i1 = i; }
                float dy = Mathf.Max(1e-4f, Mathf.Abs(Y[i1] - Y[i0])) * Mathf.Sign(Y[i1] - Y[i0] + 1e-6f);
                float da = (A[i1] - A[i0]) / dy, db = (B[i1] - B[i0]) / dy;
                return Surf(Y[i], A[i], B[i], da, db, ph, Mathf.Sign(Y[Y.Count - 1] - Y[0] + 1e-6f));
            }
            Vector3 SurfP(float y, float a, float b, float ph) => C + new Vector3(a * Mathf.Cos(ph), y, b * Mathf.Sin(ph));
            (Vector3, Vector3) Surf(float y, float a, float b, float da, float db, float ph, float dir)
            {
                float cs = Mathf.Cos(ph), sn = Mathf.Sin(ph);
                var n = new Vector3(b * cs, -(a * db * sn * sn + da * b * cs * cs), a * sn);
                if (a < 1e-3f && b < 1e-3f) n = Vector3.up * -Mathf.Sign(da + db + 1e-6f);   // a closing pole
                return (SurfP(y, a, b, ph), n.normalized);
            }
            // the surface at height y and an arc length x round from the front (for decals)
            public (Vector3 p, Vector3 n) AtArc(float y, float x, float frontDeg)
            {
                int j = 0;
                bool up = Y[Y.Count - 1] > Y[0];
                while (j < Y.Count - 2 && (up ? Y[j + 1] < y : Y[j + 1] > y)) j++;
                float t = Mathf.Clamp01(Mathf.InverseLerp(Y[j], Y[j + 1], y));
                float a = Mathf.Lerp(A[j], A[j + 1], t), b = Mathf.Lerp(B[j], B[j + 1], t);
                float dy = Y[j + 1] - Y[j]; if (Mathf.Abs(dy) < 1e-4f) dy = 1e-4f;
                float da = (A[j + 1] - A[j]) / dy, db = (B[j + 1] - B[j]) / dy;
                float r = Mathf.Max(0.5f, frontDeg % 180f == 0 ? b : a);
                float ph = frontDeg * Mathf.Deg2Rad + x / r;
                return Surf(y, a, b, da, db, ph, 1f);
            }
        }

        public void Lathe(Profile pr, Color color, int segs = 24, float ph0 = 0f, float ph1 = 360f, Color? inner = null, Func<int, Color> rowColor = null)
        {
            int rows = pr.Y.Count - 1;
            Func<int, Color> rc = rowColor ?? (_ => color);
            Grid(rows, segs, (i, j) => pr.At(i, Mathf.Lerp(ph0, ph1, j / (float)segs) * Mathf.Deg2Rad), rc, Flag);
            if (inner != null)
                Grid(rows, segs, (i, j) => { var (p, n) = pr.At(i, Mathf.Lerp(ph0, ph1, j / (float)segs) * Mathf.Deg2Rad); return (p - n * 0.05f, -n); }, _ => inner.Value, Flag);
        }

        public void Lathe(Vector3 c, float[] yab, Color color, int segs = 24, int sub = 3) => Lathe(new Profile(c, yab, sub), color, segs);

        // colours per given profile point (crisp where two points share a height)
        public void Lathe(Profile pr, Color[] cols, int segs = 24, float ph0 = 0f, float ph1 = 360f, Color? inner = null)
            => Lathe(pr, cols[0], segs, ph0, ph1, inner, i => cols[Mathf.Min(pr.Src[i], cols.Length - 1)]);

        // ---- tubes -------------------------------------------------------------------------------------------
        // A tube swept along the path pts (smoothed), its section an ellipse with half-widths rad[i].x (across, along
        // `side` where it can) and rad[i].y; colours change crisply from point to point; rounded ends when asked.
        // zoneCol: a second colour on the side of the tube facing zoneDir (an inner ear, a lapel), its half-width
        // zoneHalf(t) radians round the section at t (0..1 along the tube): one smooth surface, a crisp colour edge.
        public void Tube(IList<Vector3> pts, IList<Vector2> rad, IList<Color> cols, int sides = 12, int sub = 3,
                         bool capStart = true, bool capEnd = true, Vector3? side = null, bool loop = false,
                         Vector3? zoneDir = null, Color? zoneCol = null, Func<float, float> zoneHalf = null)
        {
            var P = new List<Vector3>(); var R = new List<Vector2>(); var C = new List<Color>();
            int n = pts.Count;
            for (int i = 0; i < (loop ? n : n - 1); i++)
                for (int k = 0; k < sub; k++)
                {
                    float t = k / (float)sub;
                    Vector3 p0 = pts[loop ? (i - 1 + n) % n : Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[loop ? (i + 2) % n : Mathf.Min(n - 1, i + 2)];
                    P.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                    R.Add(Vector2.Lerp(rad[i], rad[(i + 1) % n], t));
                    C.Add(cols[Mathf.Min(i, cols.Count - 1)]);
                }
            if (loop) { P.Add(P[0]); R.Add(R[0]); C.Add(C[0]); }
            else { P.Add(pts[n - 1]); R.Add(rad[n - 1]); C.Add(cols[Mathf.Min(n - 1, cols.Count - 1)]); }

            // rounded ends: a dome of rings past each end
            const int CapRings = 2;
            if (capStart && !loop && R[0].sqrMagnitude > 1e-6f)
            {
                Vector3 d = (P[1] - P[0]).normalized; Vector3 p = P[0]; Vector2 r = R[0]; Color c = C[0];
                for (int j = 1; j <= CapRings; j++)
                {
                    float a = 90f * j / CapRings * Mathf.Deg2Rad;
                    P.Insert(0, p - d * (Mathf.Sin(a) * (r.x + r.y) * 0.5f)); R.Insert(0, r * Mathf.Cos(a)); C.Insert(0, c);
                }
            }
            if (capEnd && !loop && R[R.Count - 1].sqrMagnitude > 1e-6f)
            {
                int last = P.Count - 1;
                Vector3 d = (P[last] - P[last - 1]).normalized; Vector3 p = P[last]; Vector2 r = R[last]; Color c = C[last];
                for (int j = 1; j <= CapRings; j++)
                {
                    float a = 90f * j / CapRings * Mathf.Deg2Rad;
                    P.Add(p + d * (Mathf.Sin(a) * (r.x + r.y) * 0.5f)); R.Add(r * Mathf.Cos(a)); C.Add(c);
                }
            }

            int m = P.Count;
            var Tg = new Vector3[m]; var S = new Vector3[m]; var B = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                Vector3 a = P[Mathf.Max(0, i - 1)], b = P[Mathf.Min(m - 1, i + 1)];
                if (loop && (i == 0 || i == m - 1)) { a = P[m - 2]; b = P[1]; }
                Tg[i] = (b - a).sqrMagnitude > 1e-10f ? (b - a).normalized : (i > 0 ? Tg[i - 1] : Vector3.up);
            }
            Vector3 hint = side ?? Vector3.forward;
            Vector3 s0 = hint - Tg[0] * Vector3.Dot(hint, Tg[0]);
            if (s0.sqrMagnitude < 1e-6f) { hint = Vector3.right; s0 = hint - Tg[0] * Vector3.Dot(hint, Tg[0]); }
            S[0] = s0.normalized;
            for (int i = 1; i < m; i++)
            {
                Vector3 s = S[i - 1] - Tg[i] * Vector3.Dot(S[i - 1], Tg[i]);
                S[i] = s.sqrMagnitude > 1e-8f ? s.normalized : S[i - 1];
            }
            for (int i = 0; i < m; i++) B[i] = Vector3.Cross(Tg[i], S[i]);

            (Vector3, Vector3) At(int i, int j) => AtPh(i, j * Mathf.PI * 2f / sides);
            (Vector3, Vector3) AtPh(int i, float ph)
            {
                float cs = Mathf.Cos(ph), sn = Mathf.Sin(ph);
                Vector2 r = R[i];
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(m - 1, i + 1);
                float ds = (P[i1] - P[i0]).magnitude;
                float slope = ds > 1e-5f ? ((R[i1].x + R[i1].y) - (R[i0].x + R[i0].y)) * 0.5f / ds : 0f;
                Vector3 radial = (S[i] * (cs / Mathf.Max(1e-3f, r.x)) + B[i] * (sn / Mathf.Max(1e-3f, r.y))).normalized;
                Vector3 nn = (radial - Tg[i] * slope).normalized;
                if (r.x < 1e-3f && r.y < 1e-3f) nn = i == 0 ? -Tg[i] : Tg[i];
                return (P[i] + S[i] * (cs * r.x) + B[i] * (sn * r.y), nn);
            }
            if (zoneCol != null && zoneDir != null && zoneHalf != null)
            {
                // the zone's centre angle per row (where the section's normal looks along zoneDir)
                var pc = new float[m];
                Vector3 zd = zoneDir.Value.normalized;
                for (int i = 0; i < m; i++)
                    pc[i] = Mathf.Atan2(Vector3.Dot(zd, B[i]) * Mathf.Max(R[i].y, 1e-3f), Vector3.Dot(zd, S[i]) * Mathf.Max(R[i].x, 1e-3f));
                for (int i = 1; i < m; i++) { while (pc[i] - pc[i - 1] > Mathf.PI) pc[i] -= Mathf.PI * 2f; while (pc[i] - pc[i - 1] < -Mathf.PI) pc[i] += Mathf.PI * 2f; }
                float H(int i) => Mathf.Clamp(zoneHalf(i / (float)(m - 1)), 0.002f, Mathf.PI - 0.05f);
                int zs = Mathf.Max(4, sides / 2), rs = Mathf.Max(4, sides - zs + 2);
                Grid(m - 1, zs, (r, j) => AtPh(r, pc[r] + Mathf.Lerp(-H(r), H(r), j / (float)zs)), _ => zoneCol.Value, Flag);
                for (int st = 0, i = 1; i <= m; i++)
                {
                    if (i < m && C[i] == C[st]) continue;
                    int e = Mathf.Min(i, m - 1), s0i = st;
                    if (e > st) Grid(e - st, rs, (r, j) => AtPh(s0i + r, pc[s0i + r] + H(s0i + r) + (Mathf.PI * 2f - 2f * H(s0i + r)) * j / rs), _ => C[s0i], Flag);
                    st = i;
                }
                return;
            }
            // one sheet per run of a colour (crisp bands)
            int start = 0;
            for (int i = 1; i <= m; i++)
            {
                if (i < m && C[i] == C[start]) continue;
                int end = Mathf.Min(i, m - 1);
                if (end > start) { int s0i = start; Grid(end - start, sides, (r, j) => At(s0i + r, j), _ => C[s0i], Flag); }
                start = i;
            }
        }

        // a straight tube from a to b, radius ra at a and rb at b
        public void Limb(Vector3 a, Vector3 b, float ra, float rb, Color c, int sides = 12, bool capA = true, bool capB = true)
            => Tube(new[] { a, (a + b) * 0.5f, b }, new[] { new Vector2(ra, ra), new Vector2((ra + rb) * 0.5f, (ra + rb) * 0.5f), new Vector2(rb, rb) }, new[] { c, c, c }, sides, 1, capA, capB);

        // ---- decals ------------------------------------------------------------------------------------------
        // A surface to paint on: maps a point of a little flat map (rig units, x across, y up) onto the shape.
        public delegate (Vector3 p, Vector3 n) Surface(Vector2 xy);

        // an ellipsoid's surface round the point it shows in direction `dir` (ellipsoid-local); the map's y follows
        // `up`, its x points to `side` (+1: toward +z; mirror the eyes with -1)
        public static Surface OnEllipsoid(Vector3 c, Vector3 r, Quaternion q, Vector3 dir, float side = 1f, Vector3? up = null)
        {
            Quaternion iq = Quaternion.Inverse(q);
            (Vector3, Vector3) Project(Vector3 p)
            {
                Vector3 d = iq * (p - c);
                float k = 1f / Mathf.Sqrt(Mathf.Max(1e-8f, (d.x / r.x) * (d.x / r.x) + (d.y / r.y) * (d.y / r.y) + (d.z / r.z) * (d.z / r.z)));
                Vector3 l = d * k;
                return (c + q * l, (q * new Vector3(l.x / (r.x * r.x), l.y / (r.y * r.y), l.z / (r.z * r.z))).normalized);
            }
            var (a, n) = Project(c + q * dir.normalized * 1000f);
            Vector3 u0 = up ?? (q * Vector3.up);
            Vector3 v = (u0 - n * Vector3.Dot(u0, n)).normalized;
            Vector3 u = Vector3.Cross(n, v).normalized;
            // keep x pointing toward +z (or -z for side -1) whatever the anchor
            if (Vector3.Dot(u, q * Vector3.forward) * side < 0) u = -u;
            return xy => Project(a + u * xy.x + v * xy.y);
        }

        // a lathe's surface: x is the arc length round from the angle `frontDeg`, y the height
        public static Surface OnLathe(Profile pr, float frontDeg = 0f) => xy => pr.AtArc(xy.y, xy.x, frontDeg);

        // a filled ellipse (centre c, half-widths r, turned rot degrees) lifted `lift` off the surface
        // (small spots are a single fan; big ones get a middle ring so they hug the curve)
        public void Spot(Surface s, Vector2 c, Vector2 r, float rot, float lift, Color color, int segs = 18, float flag = Paint)
        {
            float cr = Mathf.Cos(rot * Mathf.Deg2Rad), sr = Mathf.Sin(rot * Mathf.Deg2Rad);
            int rings = Mathf.Max(r.x, r.y) > 4f ? 2 : 1;
            Grid(rings, segs, (i, j) =>
            {
                float ph = j * Mathf.PI * 2f / segs, k = i / (float)rings;
                var e = new Vector2(Mathf.Cos(ph) * r.x * k, Mathf.Sin(ph) * r.y * k);
                var (p, n) = s(c + new Vector2(e.x * cr - e.y * sr, e.x * sr + e.y * cr));
                return (p + n * lift, n);
            }, _ => color, flag);
        }

        // a painted stroke through the points (smoothed), its width at each point
        public void Stroke(Surface s, IList<Vector2> pts, IList<float> widths, float lift, Color color, int sub = 4, float flag = Paint)
        {
            var P = new List<Vector2>(); var W = new List<float>();
            int n = pts.Count;
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sub; k++)
                {
                    float t = k / (float)sub;
                    Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(n - 1, i + 2)];
                    P.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                    W.Add(Mathf.Lerp(widths[i], widths[i + 1], t));
                }
            P.Add(pts[n - 1]); W.Add(widths[n - 1]);
            int m = P.Count;
            Grid(m - 1, 2, (i, j) =>
            {
                Vector2 d = P[Mathf.Min(m - 1, i + 1)] - P[Mathf.Max(0, i - 1)];
                Vector2 perp = new Vector2(-d.y, d.x).normalized;
                var (p, nn) = s(P[i] + perp * (W[i] * (j - 1) * 0.5f));
                return (p + nn * lift, nn);
            }, _ => color, flag);
        }

        // a filled polygon (convex or star-shaped round its centroid)
        // (the edges and the inside are subdivided so the patch hugs a curved surface)
        public void Patch(Surface s, IList<Vector2> outline, float lift, Color color, float flag = Paint, int sub = 4, int rings = 3)
        {
            Vector2 c = Vector2.zero; foreach (var p in outline) c += p; c /= outline.Count;
            var dense = new List<Vector2>();
            for (int i = 0; i < outline.Count; i++)
                for (int k = 0; k < sub; k++) dense.Add(Vector2.Lerp(outline[i], outline[(i + 1) % outline.Count], k / (float)sub));
            int n = dense.Count;
            Grid(rings, n, (i, j) =>
            {
                var q = Vector2.Lerp(c, dense[j % n], i / (float)rings);
                var (p, nn) = s(q);
                return (p + nn * lift, nn);
            }, _ => color, flag);
        }

        // ---- radial bodies --------------------------------------------------------------------------------
        // One smooth closed surface round a centre: its distance from the centre in each direction comes from a
        // function (e.g. a head, a muzzle and cheeks smoothly merged), so the parts share ONE silhouette and outline.
        // Normals from the surface itself (finite differences).
        public delegate float Radius(Vector3 dir);

        public static (Vector3 p, Vector3 n) RadialAt(Vector3 c, Radius r, Vector3 d)
        {
            d = d.normalized;
            Vector3 a = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(d, a);
            const float E = 0.012f;
            Vector3 P(Vector3 dd) { dd.Normalize(); return c + dd * r(dd); }
            var n = Vector3.Cross(P(d + a * E) - P(d - a * E), P(d + b * E) - P(d - b * E)).normalized;
            if (Vector3.Dot(n, d) < 0) n = -n;
            return (c + d * r(d), n);
        }

        // the surface sampled in rings round `axis` (the pole), denser toward the pole when warp > 1 (a snout there)
        public void Radial(Vector3 c, Radius r, Vector3 axis, int rings, int segs, Color color, float warp = 1f)
        {
            var q = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            Grid(rings, segs, (i, j) =>
            {
                float th = Mathf.PI * Mathf.Pow(i / (float)rings, warp), ph = j * Mathf.PI * 2f / segs;
                return RadialAt(c, r, q * new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
            }, _ => color, Flag);
        }

        public static Surface OnRadial(Vector3 c, Radius r, Vector3 dir, float side = 1f, Vector3? up = null)
        {
            var (a, n) = RadialAt(c, r, dir);
            Vector3 u0 = up ?? Vector3.up;
            Vector3 v = (u0 - n * Vector3.Dot(u0, n)).normalized;
            Vector3 u = Vector3.Cross(n, v).normalized;
            if (Vector3.Dot(u, Vector3.forward) * side < 0) u = -u;
            return xy => RadialAt(c, r, a + u * xy.x + v * xy.y - c);
        }

        // the far hit of the ray c + t d with an ellipsoid (centre e, radii er, turned eq); -1e9 when it misses
        public static float RayEllipsoid(Vector3 c, Vector3 d, Vector3 e, Vector3 er, Quaternion eq)
        {
            var iq = Quaternion.Inverse(eq);
            Vector3 o = iq * (c - e), dd = iq * d;
            o = new Vector3(o.x / er.x, o.y / er.y, o.z / er.z); dd = new Vector3(dd.x / er.x, dd.y / er.y, dd.z / er.z);
            float A = Vector3.Dot(dd, dd), B = 2f * Vector3.Dot(o, dd), C = Vector3.Dot(o, o) - 1f, disc = B * B - 4f * A * C;
            if (disc < 0) return -1e9f;
            float t = (-B + Mathf.Sqrt(disc)) / (2f * A);
            return t > 0 ? t : -1e9f;
        }

        // a smooth maximum (a soft union of two radii; k is the width of the blend)
        public static float SMax(float a, float b, float k)
        {
            if (b < -1e8f) return a;
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Max(a, b) + h * h * k * 0.25f;
        }

        // paints the part of a radial surface where `inside` holds, seen round `axis`: a fan whose rim follows the
        // region's edge exactly (found along each spoke), so a colour zone has a crisp, smooth boundary
        public void RegionDecal(Vector3 c, Radius r, Vector3 axis, Func<Vector3, bool> inside, float maxDeg, float lift, Color color, int rings = 4, int segs = 40)
        {
            axis.Normalize();
            Vector3 a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(axis, a);
            Vector3 D(float rho, float psi) => axis * Mathf.Cos(rho) + (a * Mathf.Cos(psi) + b * Mathf.Sin(psi)) * Mathf.Sin(rho);
            var rim = new float[segs];
            float max = maxDeg * Mathf.Deg2Rad, step = max / 48f;
            for (int j = 0; j < segs; j++)
            {
                float psi = j * Mathf.PI * 2f / segs, lo = 0f, hi = 0f;
                for (float rho = step; rho <= max; rho += step) { if (!inside(D(rho, psi))) { hi = rho; break; } lo = rho; }
                if (hi > lo) for (int it = 0; it < 7; it++) { float m = (lo + hi) * 0.5f; if (inside(D(m, psi))) lo = m; else hi = m; }
                rim[j] = lo;
            }
            Grid(rings, segs, (i, j) =>
            {
                var (p, n) = RadialAt(c, r, D(rim[j % segs] * i / rings, j * Mathf.PI * 2f / segs));
                return (p + n * lift, n);
            }, _ => color, Paint);
        }

        // A whole radial body in two colour zones that share ONE exact boundary curve: the zone round `axis` where
        // `inside` holds (e.g. a muzzle and cheeks) and the rest. One closed surface, one silhouette and outline, smooth
        // normals across the seam, and a crisp colour edge with no decal floating over it.
        // rimFn (optional): the zone's edge given directly, psi (round the axis; psi = 90 deg points along
        // cross(axis, cross(axis, up)), i.e. DOWN for a forward axis) -> the angle from the axis (radians): a designed,
        // perfectly smooth colour shape instead of one found from the geometry
        public void RadialZones(Vector3 c, Radius r, Vector3 axis, Func<Vector3, bool> inside, float maxDeg, Color inColor, Color outColor, int ringsIn, int ringsOut, int segs, float warpOut = 1.6f, Func<float, float> rimFn = null)
        {
            axis.Normalize();
            Vector3 a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(axis, a);
            Vector3 D(float rho, float psi) => axis * Mathf.Cos(rho) + (a * Mathf.Cos(psi) + b * Mathf.Sin(psi)) * Mathf.Sin(rho);
            var rim = new float[segs];
            float max = maxDeg * Mathf.Deg2Rad, step = max / 60f;
            for (int j = 0; j < segs && rimFn != null; j++) rim[j] = rimFn(j * Mathf.PI * 2f / segs);
            for (int j = 0; j < segs && rimFn == null; j++)
            {
                float psi = j * Mathf.PI * 2f / segs, lo = 0f, hi = 0f;
                for (float rho = step; rho <= max; rho += step) { if (!inside(D(rho, psi))) { hi = rho; break; } lo = rho; }
                if (hi > lo) for (int it = 0; it < 8; it++) { float m = (lo + hi) * 0.5f; if (inside(D(m, psi))) lo = m; else hi = m; }
                rim[j] = Mathf.Max(lo, 0.02f);
            }
            // (a little smoothing round the rim, so the edge is a clean curve)
            var sm = new float[segs];
            for (int j = 0; j < segs; j++) sm[j] = rimFn != null ? rim[j] : (rim[(j + segs - 1) % segs] + 2f * rim[j] + rim[(j + 1) % segs]) * 0.25f;
            float Psi(int j) => j * Mathf.PI * 2f / segs;
            Grid(ringsIn, segs, (i, j) => RadialAt(c, r, D(sm[j % segs] * i / ringsIn, Psi(j))), _ => inColor, Flag);
            Grid(ringsOut, segs, (i, j) =>
            {
                float t = Mathf.Pow(i / (float)ringsOut, warpOut), rho0 = sm[j % segs];
                return RadialAt(c, r, D(rho0 + (Mathf.PI - rho0) * t, Psi(j)));
            }, _ => outColor, Flag);
        }

        // ---- sheets ------------------------------------------------------------------------------------------
        // a two-sided curved sheet with a little thickness (a hat brim): f(u, v) gives the mid surface
        public void Sheet(Func<float, float, Vector3> f, int rows, int cols, float thickness, Color top, Color bottom, bool wrapV = false)
        {
            (Vector3, Vector3) At(int i, int j, float side)
            {
                float u = i / (float)rows, v = j / (float)cols;
                float du = 0.5f / rows, dv = 0.5f / cols;
                var p = f(u, v);
                var nu = f(Mathf.Min(1f, u + du), v) - f(Mathf.Max(0f, u - du), v);
                var nv = f(u, wrapV ? v + dv : Mathf.Min(1f, v + dv)) - f(u, wrapV ? v - dv : Mathf.Max(0f, v - dv));
                var n = Vector3.Cross(nu, nv).normalized * side;
                return (p + n * thickness * 0.5f, n);
            }
            Grid(rows, cols, (i, j) => At(i, j, 1f), _ => top, Flag);
            Grid(rows, cols, (i, j) => At(i, j, -1f), _ => bottom, Flag);
        }

        // the outline directions (w = 1: the ToonCore outline pass uses them instead of the shading normal)
        Vector4[] Tangents()
        {
            var tg = new Vector4[pos.Count];
            for (int i = 0; i < tg.Length; i++) { var d = odir[i]; tg[i] = new Vector4(d.x, d.y, d.z, 1f); }
            return tg;
        }

        // a plain (not skinned) mesh, for props such as the sword
        public Mesh BuildStatic(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(pos);
            m.SetNormals(nrm);
            m.SetColors(col);
            m.SetTangents(Tangents());
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- the skinned mesh --------------------------------------------------------------------------------
        public Mesh Build(string name, Matrix4x4[] bindposes)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(pos);
            m.SetNormals(nrm);
            m.SetColors(col);
            m.SetTangents(Tangents());
            m.SetTriangles(tris, 0);
            var bw = new BoneWeight[pos.Count];
            for (int i = 0; i < bw.Length; i++) bw[i] = new BoneWeight { boneIndex0 = bone[i], weight0 = 1f };
            m.boneWeights = bw;
            m.bindposes = bindposes;
            m.RecalculateBounds();
            return m;
        }
    }
}
