using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The wall line of the stadium bowl: a rounded rectangle round the field (metres, on the ground). t runs along it
    // from the far side's left end, rightwards along the far side, down the right end, back along the near side and up
    // the left end (Length in all). d is how far out past the wall line: a row of seats d out follows the same shape,
    // and every t stays on one line straight out from the field (on the straights the same x or z, round a corner the
    // same angle), so aisles, crowd slices and the wave all run straight up through the rows.
    public sealed class BowlPath
    {
        public readonly float A, ZNear, ZFar, R, Length;
        readonly float[] len = new float[8], start = new float[8];

        public BowlPath(float halfX, float zNear, float zFar, float corner)
        {
            A = halfX; ZNear = zNear; ZFar = zFar;
            R = Mathf.Clamp(corner, 0f, Mathf.Min(halfX, (zFar - zNear) * 0.5f));
            float sx = 2f * (A - R), sz = zFar - zNear - 2f * R, arc = Mathf.PI * 0.5f * R;
            float t = 0f;
            for (int i = 0; i < 8; i++)
            {
                len[i] = i % 2 == 1 ? arc : i % 4 == 0 ? sx : sz;
                start[i] = t;
                t += len[i];
            }
            Length = t;
        }

        // 0 far side, 1 its right corner, 2 right end, 3 corner, 4 near side, 5 corner, 6 left end, 7 corner.
        public int Segment(float t, out float share)
        {
            t = Mathf.Repeat(t, Length);
            int s = 7;
            for (int i = 0; i < 8; i++) if (t < start[i] + len[i]) { s = i; break; }
            share = len[s] > 0f ? Mathf.Clamp01((t - start[s]) / len[s]) : 0f;
            return s;
        }

        public float SegmentStart(int s) => start[s];
        public float SegmentLength(int s) => len[s];

        // Round a corner: its centre and the angles (degrees, from +x toward +z) it turns through.
        void Corner(int s, out Vector3 c, out float a0, out float a1)
        {
            float ax = A - R;
            switch (s)
            {
                case 1: c = new Vector3(ax, 0, ZFar - R); a0 = 90f; a1 = 0f; return;
                case 3: c = new Vector3(ax, 0, ZNear + R); a0 = 0f; a1 = -90f; return;
                case 5: c = new Vector3(-ax, 0, ZNear + R); a0 = -90f; a1 = -180f; return;
                default: c = new Vector3(-ax, 0, ZFar - R); a0 = 180f; a1 = 90f; return;
            }
        }

        // The point d out past the wall line at t (on the ground).
        public Vector3 At(float t, float d)
        {
            int s = Segment(t, out float f);
            float ax = A - R;
            switch (s)
            {
                case 0: return new Vector3(Mathf.Lerp(-ax, ax, f), 0, ZFar + d);
                case 2: return new Vector3(A + d, 0, Mathf.Lerp(ZFar - R, ZNear + R, f));
                case 4: return new Vector3(Mathf.Lerp(ax, -ax, f), 0, ZNear - d);
                case 6: return new Vector3(-A - d, 0, Mathf.Lerp(ZNear + R, ZFar - R, f));
            }
            Corner(s, out var c, out float a0, out float a1);
            float a = Mathf.Lerp(a0, a1, f) * Mathf.Deg2Rad;
            return c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (R + d);
        }

        // Straight out, away from the field (flat).
        public Vector3 Out(float t)
        {
            int s = Segment(t, out float f);
            switch (s)
            {
                case 0: return Vector3.forward;
                case 2: return Vector3.right;
                case 4: return Vector3.back;
                case 6: return Vector3.left;
            }
            Corner(s, out _, out float a0, out float a1);
            float a = Mathf.Lerp(a0, a1, f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        }

        // How much longer a row d out is than the wall line here: 1 on the straights, (R + d) / R round a corner.
        public float Stretch(float t, float d) => Segment(t, out _) % 2 == 1 && R > 0f ? (R + d) / R : 1f;

        // The whole length of a row d out.
        public float LengthAt(float d) => Length + (R > 0f ? 2f * Mathf.PI * d : 0f);

        // Where to cut the bowl into flat pieces: every segM along the straights, every cornerDeg round a corner, always
        // at the joins; the last one is Length (the same place as 0), closing the loop.
        public List<float> Stations(float segM, float cornerDeg)
        {
            var ts = new List<float>();
            for (int i = 0; i < 8; i++)
            {
                if (len[i] <= 0f) continue;
                int n = i % 2 == 1 ? Mathf.Max(1, Mathf.CeilToInt(90f / Mathf.Max(1f, cornerDeg))) : Mathf.Max(1, Mathf.CeilToInt(len[i] / Mathf.Max(0.5f, segM)));
                for (int k = 0; k < n; k++) ts.Add(start[i] + len[i] * k / n);
            }
            ts.Add(Length);
            return ts;
        }
    }

    // The outdoor stadium bowl round the field (FEATURES.stadiumBowl; numbers in game.json stadium + stadium.bowl):
    // the lower tier (the old far-side rows, now all the way round, with stair aisles and tunnel mouths), a concourse,
    // a fascia with a glowing ribbon board and team banners, the steeper upper tier, a rim wall with flags, the outer wall
    // with pilasters, a light tower at each corner, a big scoreboard over the left end, and outside a car park, a ring of
    // trees and the ground running off into the haze. Built once from code, seeded; nothing in it moves (the crowd that
    // sits in it is Stadium.cs's). None of it casts shadows: the low sunset sun would put the whole field in the stands'
    // shade.
    public sealed class Bowl
    {
        public readonly BowlPath Path;
        readonly GameConfig cfg;
        readonly StadiumConfig sc;
        readonly BowlConfig bc;
        readonly Transform root;
        readonly Material mat, glow;
        readonly List<float> ts;
        Texture2D boardTex;
        Material boardMat;
        int[] boardScore;
        readonly Dictionary<string, Material> marks = new Dictionary<string, Material>();
        uint seed = 777;

        float Rand() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xffffff) / 16777216f; }

        public Bowl(GameConfig cfg, Transform root, Material toon, Material glow)
        {
            this.cfg = cfg;
            sc = cfg.stadium;
            bc = sc.bowl;
            this.root = root;
            mat = toon;
            this.glow = glow;
            Path = MakePath(cfg);
            ts = Path.Stations(bc.segM, bc.cornerDeg);
        }

        public static BowlPath MakePath(GameConfig cfg)
        {
            var f = cfg.field;
            var sc = cfg.stadium;
            float m = f.yardM;
            float side = (f.HalfWidth + f.sidelineBorderYd) * m;
            return new BowlPath(f.HalfLength * m + sc.bowl.endBackM, -(side + sc.bowl.nearBackM), side + sc.wallBackM, sc.bowl.cornerM);
        }

        // ---- where the rows are (d out past the wall line, y up) ----
        public float LowerD(int r) => sc.rowBackM + r * sc.rowDepthM;
        public float LowerY(int r) => sc.firstRowM + r * sc.rowRiseM;
        public float LowerTopY => LowerY(sc.rows - 1);
        public float ConcourseD => LowerD(sc.rows - 1) + sc.rowDepthM * 0.5f;
        public float FasciaD => ConcourseD + bc.concourseM;
        public float UpperBaseY => LowerTopY + bc.fasciaM;
        public float UpperD(int r) => FasciaD + bc.upperBackM + r * bc.upperRowDepthM;
        public float UpperY(int r) => UpperBaseY + (r + 1) * bc.upperRowRiseM;
        public float RimD => UpperD(bc.upperRows - 1) + bc.upperRowDepthM * 0.5f;
        public float RimTopY => UpperY(bc.upperRows - 1) + bc.rimM;
        public float OuterD => RimD + bc.rimThickM;

        // A stair aisle at every aisleEveryM along the wall line, aisleWidthM wide in every row.
        public bool InAisle(float t, float d) =>
            sc.aisleEveryM > 0f && Mathf.Repeat(t, sc.aisleEveryM) < sc.aisleWidthM / Path.Stretch(t, d);

        // A tunnel mouth in the lower tier (rows portalRow .. portalRow + portalRows - 1), halfway between aisles.
        public bool InPortal(float t, int row, float margin = 0.3f)
        {
            if (bc.portalEveryM <= 0f || row < bc.portalRow || row >= bc.portalRow + bc.portalRows) return false;
            float w = bc.portalWidthM / Path.Stretch(t, LowerD(row));
            float u = Mathf.Repeat(t - bc.portalEveryM * 0.5f, bc.portalEveryM);
            return u < w + margin || u > bc.portalEveryM - margin;
        }

        public void Build()
        {
            Stands();
            Towers();
            Flags();
            Scoreboard();
            Outside();
        }

        // ---- flat pieces along the whole loop ----
        // A vertical face d out from y0 to y1, facing the field (outward = false) or away from it.
        void Riser(Blocks b, float d, float y0, float y1, Color c, bool outward = false)
        {
            for (int i = 0; i + 1 < ts.Count; i++) RiserPiece(b, ts[i], ts[i + 1], d, y0, y1, c, outward);
        }

        void RiserPiece(Blocks b, float ta, float tb, float d, float y0, float y1, Color c, bool outward = false)
        {
            Vector3 a = Path.At(ta, d), e = Path.At(tb, d);
            var n = Path.Out((ta + tb) * 0.5f);
            b.Quad(a + Vector3.up * y0, a + Vector3.up * y1, e + Vector3.up * y1, e + Vector3.up * y0, c, outward ? n : -n);
        }

        // A flat floor from d0 out to d1 at height y.
        void Tread(Blocks b, float d0, float d1, float y, Color c)
        {
            for (int i = 0; i + 1 < ts.Count; i++) TreadPiece(b, ts[i], ts[i + 1], d0, d1, y, c);
        }

        void TreadPiece(Blocks b, float ta, float tb, float d0, float d1, float y, Color c)
        {
            var up = Vector3.up * y;
            b.Quad(Path.At(ta, d0) + up, Path.At(ta, d1) + up, Path.At(tb, d1) + up, Path.At(tb, d0) + up, c, Vector3.up);
        }

        // One row of steps: its riser up from the row below, then its tread.
        void Row(Blocks b, float d, float depth, float yBelow, float y, Color c)
        {
            Riser(b, d - depth * 0.5f, yBelow, y, c);
            Tread(b, d - depth * 0.5f, d + depth * 0.5f, y, c);
        }

        void Stands()
        {
            var rows = new Blocks { Flag = Looks.Paint };   // the steps: no ink line on every row (it would turn to noise)
            var hard = new Blocks();                         // walls, fascia, rim, outer wall: inked
            float dep = sc.rowDepthM, udep = bc.upperRowDepthM;
            Color concrete = Colors.Hex(sc.concreteColor), seat = Colors.Hex(sc.seatColor), aisle = Colors.Hex(sc.aisleColor);
            Color uConcrete = Colors.Hex(bc.upperConcreteColor), uSeat = Colors.Hex(bc.upperSeatColor);

            // the lower tier: the wall, a walkway, rows all the way round
            Riser(hard, 0f, 0f, sc.wallHeightM, Colors.Hex(sc.wallColor));
            Tread(rows, 0f, LowerD(0) - dep * 0.5f, sc.wallHeightM, concrete);
            for (int r = 0; r < sc.rows; r++)
                Row(rows, LowerD(r), dep, r == 0 ? sc.wallHeightM : LowerY(r - 1), LowerY(r), r % 2 == 0 ? concrete : seat);

            // the concourse, the fascia (a ribbon board glowing along its middle), the upper tier
            Tread(rows, ConcourseD, FasciaD, LowerTopY, concrete);
            Riser(hard, FasciaD, LowerTopY, UpperBaseY, Colors.Hex(bc.fasciaColor));
            Tread(rows, FasciaD, UpperD(0) - udep * 0.5f, UpperBaseY, uConcrete);
            for (int r = 0; r < bc.upperRows; r++)
                Row(rows, UpperD(r), udep, r == 0 ? UpperBaseY : UpperY(r - 1), UpperY(r), r % 2 == 0 ? uConcrete : uSeat);

            // the rim, its cap and the outer wall (a dark band where the concourse opens to the outside), pilasters
            Riser(hard, RimD, UpperY(bc.upperRows - 1), RimTopY, Colors.Hex(bc.rimColor));
            Tread(hard, RimD, OuterD, RimTopY, Colors.Hex(bc.rimColor));
            Riser(hard, OuterD, 0f, RimTopY, Colors.Hex(bc.outerColor), true);
            Riser(rows, OuterD + 0.03f, LowerTopY + 0.3f, LowerTopY + 2.6f, Colors.Hex(bc.portalColor), true);
            if (bc.pilasterEveryM > 0f)
            {
                Color pil = Colors.Hex(bc.pilasterColor);
                for (float t = 0f; t < Path.Length; t += bc.pilasterEveryM / Path.Stretch(t, OuterD))
                {
                    var n = Path.Out(t);
                    var at = Path.At(t, OuterD + 0.3f) + Vector3.up * (RimTopY * 0.5f + 0.2f);
                    hard.Box(at, new Vector3(0.7f, RimTopY * 0.5f + 0.2f, 0.35f), pil, Quaternion.LookRotation(n));
                }
            }

            // the stair aisles (a touch proud of the rows), straight up through both tiers
            if (sc.aisleEveryM > 0f)
                for (float ta = 0f; ta < Path.Length; ta += sc.aisleEveryM)
                {
                    for (int r = 0; r < sc.rows; r++)
                    {
                        float d = LowerD(r), w = sc.aisleWidthM / Path.Stretch(ta, d);
                        RiserPiece(rows, ta, ta + w, d - dep * 0.5f - 0.02f, r == 0 ? sc.wallHeightM : LowerY(r - 1), LowerY(r) + 0.02f, aisle);
                        TreadPiece(rows, ta, ta + w, d - dep * 0.5f - 0.02f, d + dep * 0.5f, LowerY(r) + 0.02f, aisle);
                    }
                    for (int r = 0; r < bc.upperRows; r++)
                    {
                        float d = UpperD(r), w = sc.aisleWidthM / Path.Stretch(ta, d);
                        RiserPiece(rows, ta, ta + w, d - udep * 0.5f - 0.02f, r == 0 ? UpperBaseY : UpperY(r - 1), UpperY(r) + 0.02f, aisle);
                        TreadPiece(rows, ta, ta + w, d - udep * 0.5f - 0.02f, d + udep * 0.5f, UpperY(r) + 0.02f, aisle);
                    }
                }

            // the tunnel mouths: a concrete block standing out of the rows with a dark opening facing the field
            if (bc.portalEveryM > 0f && bc.portalRows > 0)
            {
                Color dark = Colors.Hex(bc.portalColor);
                int r0 = bc.portalRow, r1 = Mathf.Min(sc.rows - 1, bc.portalRow + bc.portalRows - 1);
                for (float ta = bc.portalEveryM * 0.5f; ta < Path.Length; ta += bc.portalEveryM)
                {
                    float d0 = LowerD(r0) - dep * 0.5f - 0.05f, d1 = LowerD(r1) + dep * 0.5f;
                    float w = bc.portalWidthM / Path.Stretch(ta, d0), tb = ta + w;
                    float y0 = r0 == 0 ? sc.wallHeightM : LowerY(r0 - 1), y1 = LowerY(r1) + 0.15f;
                    RiserPiece(hard, ta, tb, d0, y0, y1, concrete);
                    TreadPiece(hard, ta, tb, d0, d1, y1, concrete);
                    Vector3 a0 = Path.At(ta, d0), a1 = Path.At(ta, d1), b0 = Path.At(tb, d0), b1 = Path.At(tb, d1);
                    hard.Quad(a0 + Vector3.up * y0, a0 + Vector3.up * y1, a1 + Vector3.up * y1, a1 + Vector3.up * y0, concrete, a0 - b0);
                    hard.Quad(b0 + Vector3.up * y0, b0 + Vector3.up * y1, b1 + Vector3.up * y1, b1 + Vector3.up * y0, concrete, b0 - a0);
                    // the opening: inset from the sides and the top
                    float inset = 0.35f / Mathf.Max(0.1f, (b0 - a0).magnitude);
                    Vector3 o0 = Vector3.Lerp(a0, b0, inset), o1 = Vector3.Lerp(a0, b0, 1f - inset), front = -Path.Out((ta + tb) * 0.5f) * 0.03f;
                    rows.Quad(o0 + front + Vector3.up * y0, o0 + front + Vector3.up * (y1 - 0.4f), o1 + front + Vector3.up * (y1 - 0.4f), o1 + front + Vector3.up * y0, dark, front);
                }
            }

            rows.Build(root, "BowlRows", mat, false);
            hard.Build(root, "BowlWalls", mat, false);
            Fascia();
        }

        // The ribbon board along the fascia (glowing, in the home colour) and team banners on it along the straights.
        void Fascia()
        {
            if (glow != null)
            {
                var ribbon = new Blocks { Flag = 1f };
                float y0 = LowerTopY + bc.fasciaM * 0.3f, y1 = LowerTopY + bc.fasciaM * 0.7f;
                Riser(ribbon, FasciaD - 0.04f, y0, y1, Colors.Hex(bc.ribbonColor) * bc.lampGlow * 0.6f);
                ribbon.Build(root, "RibbonBoard", glow, false);
            }
            if (bc.bannerEveryM <= 0f) return;
            float h = bc.fasciaM * 0.62f, y = LowerTopY + bc.fasciaM * 0.5f;
            int k = 0;
            for (int s = 0; s < 8; s += 2)
            {
                float len = Path.SegmentLength(s);
                int n = Mathf.FloorToInt(len / bc.bannerEveryM);
                for (int i = 0; i < n; i++)
                {
                    float t = Path.SegmentStart(s) + (i + 0.5f) * len / n;
                    var team = cfg.teams[k++ % 2 == 0 ? 1 : 0];
                    var facing = -Path.Out(t);
                    var at = Path.At(t, FasciaD - 0.08f) + Vector3.up * y;
                    var panel = new Blocks();
                    var right = Vector3.Cross(Vector3.up, -facing);
                    var c = Colors.Hex(team.color);
                    panel.Quad(at - right * h * 1.4f - Vector3.up * h * 0.5f, at - right * h * 1.4f + Vector3.up * h * 0.5f,
                        at + right * h * 1.4f + Vector3.up * h * 0.5f, at + right * h * 1.4f - Vector3.up * h * 0.5f, c, facing);
                    panel.Build(root, "FasciaBanner", mat, false);
                    Mark(team, at + facing * 0.03f, facing, h * 0.85f);
                }
            }
        }

        // A team's mark on a wall, facing `facing`, `size` metres square.
        void Mark(TeamConfig team, Vector3 at, Vector3 facing, float size)
        {
            if (!marks.TryGetValue(team.@short, out var mark))
            {
                mark = new Material(mat) { name = "Mark " + team.@short };
                mark.SetTexture("_MainTex", TeamMarks.For(team));
                mark.SetColor("_PlayerColor", Colors.Hex(team.color));
                marks[team.@short] = mark;
            }
            var q = Looks.Flat(root, "BowlMark", at, new Vector2(size, size), Color.white, mark);
            q.transform.localRotation = Quaternion.LookRotation(Vector3.up, facing);
        }

        // ---- a light tower outside each corner: a tapering mast, a lamp head aimed at the middle of the field ----
        void Towers()
        {
            var steel = new Blocks();
            var lamps = new Blocks { Flag = 1f };
            Color sCol = Colors.Hex(bc.towerColor), lamp = Colors.Hex(bc.lampColor) * bc.lampGlow;
            float zc = (Path.ZFar + Path.ZNear) * 0.5f, H = bc.towerHeightM;
            for (int s = 1; s < 8; s += 2)
            {
                float t = Path.SegmentStart(s) + Path.SegmentLength(s) * 0.5f;
                var b = Path.At(t, OuterD + bc.towerOutM);
                // the mast in three tapering pieces, a platform under the head
                float[] w = { 1.5f, 1.15f, 0.85f };
                for (int i = 0; i < 3; i++)
                    steel.Box(b + Vector3.up * H * (i + 0.5f) / 3f, new Vector3(w[i], H / 6f + 0.1f, w[i]), sCol);
                var aim = new Vector3(0, 0, zc) - b;
                aim.y = 0f;
                var flat = Quaternion.LookRotation(aim.normalized);
                var q = flat * Quaternion.Euler(bc.towerTiltDeg, 0, 0);
                int nx = Mathf.Max(1, bc.towerLampsX), ny = Mathf.Max(1, bc.towerLampsY);
                float lm = bc.towerLampM, gap = lm * 0.35f;
                float hw = nx * (lm + gap) * 0.5f + 0.4f, hh = ny * (lm + gap) * 0.5f + 0.4f;
                var head = b + Vector3.up * (H + hh * 0.6f);
                steel.Box(head, new Vector3(hw, hh, 0.45f), sCol, q);
                steel.Box(b + Vector3.up * (H - 0.2f) + flat * Vector3.forward * 0.8f, new Vector3(hw * 0.8f, 0.15f, 1.3f), sCol, flat);
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        var c = head + q * new Vector3((ix - (nx - 1) * 0.5f) * (lm + gap), (iy - (ny - 1) * 0.5f) * (lm + gap), 0.5f);
                        Vector3 rx = q * Vector3.right * lm * 0.5f, ry = q * Vector3.up * lm * 0.5f;
                        lamps.Quad(c - rx - ry, c - rx + ry, c + rx + ry, c + rx - ry, lamp, q * Vector3.forward);
                    }
            }
            steel.Build(root, "LightTowers", mat, false);
            if (glow != null) lamps.Build(root, "Floodlights", glow, false);
        }

        // ---- flags along the rim, in the two teams' colours, all blowing the same way ----
        void Flags()
        {
            if (bc.flagEveryM <= 0f) return;
            var b = new Blocks();
            Color pole = Colors.Hex("#f2f2ee");
            float d = RimD + bc.rimThickM * 0.5f, y = RimTopY;
            int k = 0;
            for (float t = bc.flagEveryM * 0.5f; t < Path.Length; t += bc.flagEveryM / Path.Stretch(t, d))
            {
                var team = cfg.teams[k++ % 2 == 0 ? 1 : 0];
                var at = Path.At(t, d) + Vector3.up * y;
                b.Box(at + Vector3.up * bc.flagPoleM * 0.5f, new Vector3(0.08f, bc.flagPoleM * 0.5f, 0.08f), pole);
                var n = Path.Out(t);
                var along = Vector3.Cross(Vector3.up, n);   // the wind blows along the rim
                var top = at + Vector3.up * bc.flagPoleM;
                Vector3 mid = top + along * bc.flagWM * 0.5f + n * 0.35f, end = top + along * bc.flagWM;
                Color c = Colors.Hex(team.color), c2 = Colors.Hex(team.color2);
                var dn = Vector3.down * bc.flagHM;
                var stripe = Vector3.down * bc.flagHM * 0.3f;
                foreach (var f in new[] { n, -n })
                {
                    b.Quad(top, top + dn, mid + dn, mid, c, f);
                    b.Quad(mid, mid + dn, end + dn, end, c, f);
                    b.Quad(mid + stripe + f * 0.01f, mid + stripe * 2f + f * 0.01f, end + stripe * 2f + f * 0.01f, end + stripe + f * 0.01f, c2, f);
                }
            }
            b.Build(root, "Flags", mat, false);
        }

        // ---- the scoreboard over the left end: two legs on the rim, a dark frame, a screen with the live score ----
        void Scoreboard()
        {
            if (bc.boardWidthM <= 0f) return;
            float t = Path.SegmentStart(6) + Path.SegmentLength(6) * 0.5f;
            var facing = -Path.Out(t);
            var right = Vector3.Cross(facing, Vector3.up);
            float W = bc.boardWidthM, H = bc.boardHeightM, bottom = RimTopY + bc.boardLiftM;
            var centre = Path.At(t, RimD) + Vector3.up * (bottom + H * 0.5f);
            var q = Quaternion.LookRotation(facing);
            var b = new Blocks();
            Color frame = Colors.Hex(bc.boardColor);
            b.Box(centre - facing * 0.4f, new Vector3(W * 0.5f + 0.7f, H * 0.5f + 0.7f, 0.6f), frame, q, true);
            foreach (float s in new[] { -0.3f, 0.3f })
            {
                float h = bottom - RimTopY + 0.5f;
                b.Box(Path.At(t, RimD) + right * W * s - facing * 0.4f + Vector3.up * (RimTopY + h * 0.5f - 0.3f), new Vector3(0.7f, h * 0.5f, 0.7f), frame, q);
            }
            b.Build(root, "Scoreboard", mat, false);
            boardMat = new Material(mat) { name = "ScoreboardScreen" };
            var screen = Looks.Flat(root, "ScoreboardScreen", centre + facing * 0.22f, new Vector2(W, H), Color.white, boardMat);
            screen.transform.localRotation = Quaternion.LookRotation(Vector3.up, facing);
            SetScore(new[] { 0, 0 });
        }

        // The screen shows each team's colour, letters and score; repainted only when the score changes.
        public void SetScore(int[] score)
        {
            if (boardMat == null || score == null || score.Length < 2 || cfg.teams.Count < 2) return;
            if (boardScore != null && boardScore[0] == score[0] && boardScore[1] == score[1]) return;
            boardScore = new[] { score[0], score[1] };
            const int Wp = 512, Hp = 224;
            var p = new Painter(Wp, Hp, Colors.Hex("#141b2b"));
            for (int i = 0; i < 2; i++)
            {
                var team = cfg.teams[i];
                float x0 = i == 0 ? 10 : Wp * 0.5f + 5, x1 = i == 0 ? Wp * 0.5f - 5 : Wp - 10;
                p.Fill(new[] { new Vector2(x0, 10), new Vector2(x0, Hp - 10), new Vector2(x1, Hp - 10), new Vector2(x1, 10) }, Colors.Hex(team.color));
                p.Fill(new[] { new Vector2(x0, 10), new Vector2(x0, 32), new Vector2(x1, 32), new Vector2(x1, 10) }, Colors.Hex(team.color2));
                var name = Glyphs.Text(team.@short, 52, 0.12f, out float nw);
                Shift(name, new Vector2((x0 + x1) * 0.5f - nw * 0.5f, Hp - 82));
                p.Strokes(name, 11, Color.white);
                var digits = Glyphs.Text(boardScore[i].ToString(), 96, 0.12f, out float dw);
                Shift(digits, new Vector2((x0 + x1) * 0.5f - dw * 0.5f, 46));
                p.Strokes(digits, 17, Color.white);
            }
            if (boardTex != null) Object.Destroy(boardTex);
            boardTex = p.ToTexture("ScoreboardScreen");
            boardMat.SetTexture("_MainTex", boardTex);
        }

        static void Shift(List<Vector2[]> strokes, Vector2 by)
        {
            foreach (var s in strokes) for (int i = 0; i < s.Length; i++) s[i] += by;
        }

        // ---- outside: the ground off into the haze, a car park round the stadium with painted bays, a ring of trees ----
        void Outside()
        {
            Looks.Flat(root, "Ground", Vector3.down * 0.06f, new Vector2(bc.groundRadiusM * 2f, bc.groundRadiusM * 2f), Colors.Hex(bc.groundColor), mat);
            // inside the bowl, past the field's own surround: the same grass right up to the wall
            float zc = (Path.ZFar + Path.ZNear) * 0.5f;
            Looks.Flat(root, "Apron", new Vector3(0, -0.03f, zc), new Vector2(Path.A * 2f, Path.ZFar - Path.ZNear), Colors.Hex(cfg.field.surroundColor), mat);
            var lot = new Blocks { Flag = Looks.Paint };
            Tread(lot, OuterD, OuterD + bc.parkingM, -0.04f, Colors.Hex(bc.parkingColor));
            Color line = Colors.Hex("#d9d6cc");
            for (int row = 0; row < 3; row++)
            {
                float d0 = OuterD + 4f + row * 6.5f;
                if (d0 + 5f > OuterD + bc.parkingM) break;
                for (float t = 0f; t < Path.Length; t += 2.8f / Path.Stretch(t, d0))
                {
                    Vector3 a = Path.At(t, d0), e = Path.At(t, d0 + 5f), side = Vector3.Cross(Vector3.up, Path.Out(t)) * 0.07f;
                    lot.Quad(a - side + Vector3.up * -0.03f, e - side + Vector3.up * -0.03f, e + side + Vector3.up * -0.03f, a + side + Vector3.up * -0.03f, line, Vector3.up);
                }
            }
            lot.Build(root, "CarPark", mat, false);

            var trees = new Blocks();
            Color leaf = Colors.Hex(bc.treeColor), leafDark = Colors.Hex(bc.treeDarkColor), trunk = Colors.Hex(bc.trunkColor);
            float inner = OuterD + bc.parkingM + bc.treeInM;
            for (int i = 0; i < bc.trees; i++)
            {
                float t = Rand() * Path.Length, d = inner + Rand() * (bc.treeOutM - bc.treeInM);
                var at = Path.At(t, d);
                float h = 6f + Rand() * 5f, r = h * (0.3f + Rand() * 0.08f);
                trees.Box(at + Vector3.up * h * 0.12f, new Vector3(0.3f, h * 0.12f, 0.3f), trunk);
                var c = Rand() < 0.5f ? leaf : leafDark;
                Cone(trees, at + Vector3.up * h * 0.2f, r, h * 0.55f, c, Rand() * 60f);
                Cone(trees, at + Vector3.up * h * 0.48f, r * 0.72f, h * 0.52f, c, Rand() * 60f);
            }
            trees.Build(root, "Trees", mat, false);
        }

        // A six-sided cone (a toon pine) standing on `at`.
        static void Cone(Blocks b, Vector3 at, float r, float h, Color c, float turnDeg)
        {
            const int Sides = 6;
            var top = at + Vector3.up * h;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = (turnDeg + i * 360f / Sides) * Mathf.Deg2Rad, a1 = (turnDeg + (i + 1) * 360f / Sides) * Mathf.Deg2Rad;
                Vector3 p0 = at + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r, p1 = at + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                var mid = (p0 + p1) * 0.5f - at;
                b.Quad(p0, p1, top, top, c, mid.normalized * h + Vector3.up * r);
            }
        }
    }
}
