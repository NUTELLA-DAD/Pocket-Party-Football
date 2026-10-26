using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The field (concept 04): one painted texture over the playing surface and its white border: mown stripes, yard
    // lines, sidelines, hash marks, big yard numbers with arrows (upright to the camera, both rows), the home team's
    // mark in the middle. Around it, the sideline grass. Shaded by the toon shader (cast shadows land on it).
    // World units are metres; the config is in yards.
    public static class FieldView
    {
        const int PxPerYd = 32;

        public static GameObject Build(GameConfig cfg, Material baseMat, Transform parent = null)
        {
            var f = cfg.field;
            float m = f.yardM;
            var root = new GameObject("Field");
            if (parent != null) root.transform.SetParent(parent, false);
            var toon = Looks.Toon(baseMat);

            // the sideline grass all round (darker than the field, unstriped)
            float gx = (f.HalfLength + f.surroundYd) * m, gz = (f.HalfWidth + f.surroundYd) * m;
            Looks.Flat(root.transform, "Surround", Vector3.down * 0.01f, new Vector2(gx * 2, gz * 2), Colors.Hex(f.surroundColor), toon);

            // the painted field and its border
            float hx = f.HalfLength + f.sidelineBorderYd, hz = f.HalfWidth + f.sidelineBorderYd;
            var paint = new Material(toon) { name = "FieldPaint" };
            paint.SetTexture("_MainTex", Paint(cfg, hx, hz));
            var field = Looks.Flat(root.transform, "Paint", Vector3.zero, new Vector2(hx * 2 * m, hz * 2 * m), Color.white, paint);
            field.name = "Paint";
            return root;
        }

        // The paint, x in [-hx, hx] and z in [-hz, hz] yards. Texture v = 0 at -z (the near side).
        static Texture2D Paint(GameConfig cfg, float hx, float hz)
        {
            var f = cfg.field;
            int w = Mathf.CeilToInt(hx * 2 * PxPerYd), h = Mathf.CeilToInt(hz * 2 * PxPerYd);
            var p = new Painter(w, h, Colors.Hex(f.lineColor));
            Color a = Colors.Hex(f.grassColor), b = Colors.Hex(f.grassAltColor), ez = Colors.Hex(f.endZoneColor);
            bool striped = cfg.Feature("fieldStripes");
            float px(float x) => (x + hx) * PxPerYd;
            float pz(float z) => (z + hz) * PxPerYd;

            // grass: stripes between the goal lines, the end zones, a little mown texture (seeded, so every run matches)
            int x0 = (int)px(-f.HalfLength), x1 = (int)px(f.HalfLength), z0 = (int)pz(-f.HalfWidth), z1 = (int)pz(f.HalfWidth);
            for (int y = z0; y < z1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float xd = x / (float)PxPerYd - hx;
                    Color c;
                    if (Mathf.Abs(xd) > f.GoalLineX) c = ez;
                    else c = striped && Mathf.FloorToInt((xd + f.GoalLineX) / f.stripeYd) % 2 == 1 ? b : a;
                    float n = (Hash(x, y) - 0.5f) * 0.08f + (Blotch(x / 6f, y / 6f) - 0.5f) * 0.07f + (Blotch(x / 40f, y / 40f) - 0.5f) * 0.09f;
                    float olive = (Blotch(x / 90f + 7f, y / 90f) - 0.5f) * 0.12f;
                    p.Px[y * w + x] = new Color(c.r * (1 + n + olive), c.g * (1 + n), c.b * (1 + n - olive * 0.5f), 1f);
                }

            Color line = Colors.Hex(f.lineColor);
            float lw = f.lineWidthYd * PxPerYd;
            // yard lines, goal lines, end lines
            foreach (float x in FieldLayout.YardLineXs(f))
                p.Stroke(new[] { new Vector2(px(x), pz(-f.HalfWidth)), new Vector2(px(x), pz(f.HalfWidth)) }, lw * (Mathf.Abs(Mathf.Abs(x) - f.GoalLineX) < 0.01f ? 1.6f : 1f), line);
            // hash marks, plus the short ticks along both sidelines
            foreach (var hm in FieldLayout.HashMarks(f))
                p.Stroke(new[] { new Vector2(px(hm[0]), pz(hm[1] - f.hashLengthYd * 0.5f)), new Vector2(px(hm[0]), pz(hm[1] + f.hashLengthYd * 0.5f)) }, lw * 0.8f, line);
            for (int i = 1; i < (int)(f.GoalLineX * 2); i++)
            {
                if (i % (int)f.lineEveryYd == 0) continue;
                float x = -f.GoalLineX + i;
                foreach (float s in new[] { -1f, 1f })
                    p.Stroke(new[] { new Vector2(px(x), pz(s * (f.HalfWidth - 1f))), new Vector2(px(x), pz(s * (f.HalfWidth - 1f - f.hashLengthYd))) }, lw * 0.8f, line);
            }

            // the numbers: tops 9 yards in from each sideline, 2 yards tall, both rows upright to the camera
            float numH = 2.4f * PxPerYd, stroke = 0.48f * PxPerYd, gap = 0.36f * PxPerYd;
            Color numCol = new Color(line.r, line.g, line.b, 0.88f);
            for (int yd = 10; yd <= 50; yd += 10)
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = -f.GoalLineX + yd;
                    if (side > 0 && yd == 50) continue;
                    if (side > 0) x = f.GoalLineX - yd;
                    foreach (float row in new[] { -1f, 1f })
                    {
                        float zc = row * (f.HalfWidth - 9f - 1.3f);
                        float bottom = pz(zc) - numH * 0.5f;
                        var tens = Glyphs.Text((yd / 10).ToString(), numH, 0f, out float tw);
                        var zero = Glyphs.Text("0", numH, 0f, out float zw);
                        Shift(tens, new Vector2(px(x) - gap - tw, bottom));
                        Shift(zero, new Vector2(px(x) + gap, bottom));
                        p.Strokes(tens, stroke, numCol);
                        p.Strokes(zero, stroke, numCol);
                        if (yd == 50) continue;
                        // the arrow points to the nearer goal, beside the number on that side
                        float dir = x < 0 ? -1f : 1f;
                        float ax = dir < 0 ? px(x) - gap - tw - 0.45f * PxPerYd : px(x) + gap + zw + 0.45f * PxPerYd;
                        float ay = bottom + numH * 0.5f, s = 0.42f * PxPerYd;
                        p.Fill(new[] { new Vector2(ax + dir * s, ay), new Vector2(ax - dir * s * 0.6f, ay + s * 0.8f), new Vector2(ax - dir * s * 0.6f, ay - s * 0.8f) }, line);
                    }
                }

            // the home team's mark in the middle
            if (cfg.teams.Count > 1) Stamp(p, TeamMarks.For(cfg.teams[1]), new Vector2(px(0), pz(0)), 17f * PxPerYd, 14f * PxPerYd, 0.9f);
            return p.ToTexture("FieldPaint");
        }

        static void Shift(System.Collections.Generic.List<Vector2[]> s, Vector2 by) { foreach (var a in s) for (int i = 0; i < a.Length; i++) a[i] += by; }

        // A texture laid onto the paint, centred at c, w x h pixels, at `alpha`.
        static void Stamp(Painter p, Texture2D t, Vector2 c, float w, float h, float alpha)
        {
            Color32[] src;
            try { src = t.GetPixels32(); }
            catch (UnityException) { return; }   // (a PNG replacement must be marked Read/Write to be painted in)
            int x0 = (int)(c.x - w * 0.5f), y0 = (int)(c.y - h * 0.5f), nw = (int)w, nh = (int)h;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int sx = x * t.width / nw, sy = y * t.height / nh;
                    Color s = src[sy * t.width + sx];
                    p.Blend(x0 + x, y0 + y, new Color(s.r, s.g, s.b, 1f), s.a * alpha);
                }
        }

        // seeded value noise: the same grass every run
        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        static float Blotch(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }
    }

    public static class Colors
    {
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
