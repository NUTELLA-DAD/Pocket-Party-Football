using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The field (concept 04): one painted texture over the playing surface and its white border: mown stripes brushed
    // with seeded dabs along the mowing direction, thin soft yard lines, sidelines, hash marks, big yard numbers with
    // arrows (upright to the camera, both rows), the home team's mark in the middle in two tones. Around it, the
    // sideline grass. Drawn with the grass shader (Art/Shaders/Grass.shader: fine strokes up close, a warm grade) or,
    // with FEATURES.grassShader off, the plain toon shader; cast shadows land on it either way.
    // World units are metres; the config is in yards (field and field.paint in game.json).
    public static class FieldView
    {
        const int PxPerYd = 32;

        public static GameObject Build(GameConfig cfg, Material baseMat, Transform parent = null)
        {
            var f = cfg.field;
            float m = f.yardM;
            var root = new GameObject("Field");
            if (parent != null) root.transform.SetParent(parent, false);
            var ground = Ground(cfg, baseMat);

            // the sideline grass all round (darker than the field, unstriped)
            float gx = (f.HalfLength + f.surroundYd) * m, gz = (f.HalfWidth + f.surroundYd) * m;
            Looks.Flat(root.transform, "Surround", Vector3.down * 0.01f, new Vector2(gx * 2, gz * 2), Colors.Hex(f.surroundColor), ground);

            // the painted field and its border
            float hx = f.HalfLength + f.sidelineBorderYd, hz = f.HalfWidth + f.sidelineBorderYd;
            var paint = new Material(ground) { name = "FieldPaint" };
            paint.SetTexture("_MainTex", Paint(cfg, hx, hz));
            var field = Looks.Flat(root.transform, "Paint", Vector3.zero, new Vector2(hx * 2 * m, hz * 2 * m), Color.white, paint);
            field.name = "Paint";
            return root;
        }

        // The ground's material: the grass shader (Resources/Grass.mat) with its numbers from game.json, or the plain toon
        // material when FEATURES.grassShader is off (NO_GRASS_SHADER=1) or the shader cannot run on this machine.
        public static Material Ground(GameConfig cfg, Material baseMat)
        {
            var toon = Looks.Toon(baseMat);
            if (!cfg.Feature("grassShader")) return toon;
            var grass = Resources.Load<Material>("Grass");
            if (grass == null || grass.shader == null || !grass.shader.isSupported) return toon;
            var pp = cfg.field.paint;
            var mat = new Material(grass) { name = "Grass" };
            mat.SetFloat("_DetailM", pp.detailM);
            mat.SetFloat("_DetailAmount", pp.detailAmount);
            mat.SetFloat("_DetailFadePx", pp.detailFadePx);
            mat.SetFloat("_GradeSaturation", pp.gradeSaturation);
            if (pp.gradeGain != null && pp.gradeGain.Length >= 3) mat.SetVector("_GradeGain", new Vector4(pp.gradeGain[0], pp.gradeGain[1], pp.gradeGain[2], 1));
            if (pp.gradeLift != null && pp.gradeLift.Length >= 3) mat.SetVector("_GradeLift", new Vector4(pp.gradeLift[0], pp.gradeLift[1], pp.gradeLift[2], 0));
            return mat;
        }

        // The paint, x in [-hx, hx] and z in [-hz, hz] yards. Texture v = 0 at -z (the near side).
        static Texture2D Paint(GameConfig cfg, float hx, float hz)
        {
            var f = cfg.field;
            var pp = f.paint;
            int w = Mathf.CeilToInt(hx * 2 * PxPerYd), h = Mathf.CeilToInt(hz * 2 * PxPerYd);
            var p = new Painter(w, h, Colors.Hex(f.lineColor));
            Color a = Colors.Hex(f.grassColor), b = Colors.Hex(f.grassAltColor), ez = Colors.Hex(f.endZoneColor);
            bool striped = cfg.Feature("fieldStripes");
            float px(float x) => (x + hx) * PxPerYd;
            float pz(float z) => (z + hz) * PxPerYd;

            // grass: stripes between the goal lines, the end zones, brush dabs, a little grain and some broad patches
            // (all seeded, so every run matches)
            int x0 = (int)px(-f.HalfLength), x1 = (int)px(f.HalfLength), z0 = (int)pz(-f.HalfWidth), z1 = (int)pz(f.HalfWidth);
            var dabs = Brush(pp, x1 - x0, z1 - z0);
            float patch = Mathf.Max(pp.patchYd * PxPerYd, 1f);
            for (int y = z0; y < z1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float xd = x / (float)PxPerYd - hx;
                    Color c;
                    if (Mathf.Abs(xd) > f.GoalLineX) c = ez;
                    else c = striped && Mathf.FloorToInt((xd + f.GoalLineX) / f.stripeYd) % 2 == 1 ? b : a;
                    float v = dabs[(y - z0) * (x1 - x0) + (x - x0)];
                    float n = (Hash(x, y) - 0.5f) * 2f * pp.grainStrength + (Blotch(x / patch, y / patch) - 0.5f) * 2f * pp.patchStrength;
                    float olive = (Blotch(x / (patch * 2.25f) + 7f, y / (patch * 2.25f)) - 0.5f) * 0.12f;
                    // (a lighter dab is a touch warmer, a darker one a touch cooler)
                    p.Px[y * w + x] = new Color(c.r * (1 + n + olive + v * 1.25f), c.g * (1 + n + v), c.b * (1 + n + v * 0.5f - olive * 0.5f), 1f);
                }

            Color line = Colors.Hex(f.lineColor);
            float lw = f.lineWidthYd * PxPerYd, soft = pp.lineSoftYd * PxPerYd, la = pp.lineAlpha;
            // yard lines, goal lines, end lines
            foreach (float x in FieldLayout.YardLineXs(f))
                Soft(p, new[] { new Vector2(px(x), pz(-f.HalfWidth)), new Vector2(px(x), pz(f.HalfWidth)) }, lw * (Mathf.Abs(Mathf.Abs(x) - f.GoalLineX) < 0.01f ? 1.6f : 1f), soft, line, la);
            // hash marks, plus the short ticks along both sidelines
            foreach (var hm in FieldLayout.HashMarks(f))
                Soft(p, new[] { new Vector2(px(hm[0]), pz(hm[1] - f.hashLengthYd * 0.5f)), new Vector2(px(hm[0]), pz(hm[1] + f.hashLengthYd * 0.5f)) }, lw * 0.8f, soft, line, la);
            for (int i = 1; i < (int)(f.GoalLineX * 2); i++)
            {
                if (i % (int)f.lineEveryYd == 0) continue;
                float x = -f.GoalLineX + i;
                foreach (float s in new[] { -1f, 1f })
                    Soft(p, new[] { new Vector2(px(x), pz(s * (f.HalfWidth - 1f))), new Vector2(px(x), pz(s * (f.HalfWidth - 1f - f.hashLengthYd))) }, lw * 0.8f, soft, line, la);
            }

            // the numbers: tops 9 yards in from each sideline, both rows upright to the camera, worn a little (the grass
            // shows through)
            float numH = pp.numberHeightYd * PxPerYd, stroke = pp.numberStrokeYd * PxPerYd, gap = pp.numberGapYd * PxPerYd;
            for (int yd = 10; yd <= 50; yd += 10)
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = -f.GoalLineX + yd;
                    if (side > 0 && yd == 50) continue;
                    if (side > 0) x = f.GoalLineX - yd;
                    foreach (float row in new[] { -1f, 1f })
                    {
                        float zc = row * (f.HalfWidth - 9f - pp.numberHeightYd * 0.5f);
                        float bottom = pz(zc) - numH * 0.5f;
                        var tens = Glyphs.Text((yd / 10).ToString(), numH, 0f, out float tw);
                        var zero = Glyphs.Text("0", numH, 0f, out float zw);
                        Shift(tens, new Vector2(px(x) - gap - tw, bottom));
                        Shift(zero, new Vector2(px(x) + gap, bottom));
                        foreach (var g in tens) Soft(p, g, stroke, soft, line, pp.numberAlpha);
                        foreach (var g in zero) Soft(p, g, stroke, soft, line, pp.numberAlpha);
                        if (yd == 50) continue;
                        // the arrow points to the nearer goal, beside the number on that side
                        float dir = x < 0 ? -1f : 1f;
                        float ax = dir < 0 ? px(x) - gap - tw - 0.45f * PxPerYd : px(x) + gap + zw + 0.45f * PxPerYd;
                        float ay = bottom + numH * 0.5f, s = pp.arrowYd * PxPerYd;
                        p.Fill(new[] { new Vector2(ax + dir * s, ay), new Vector2(ax - dir * s * 0.6f, ay + s * 0.8f), new Vector2(ax - dir * s * 0.6f, ay - s * 0.8f) }, new Color(line.r, line.g, line.b, pp.numberAlpha));
                    }
                }

            // the home team's mark in the middle, in two tones of paint
            if (cfg.teams.Count > 1)
            {
                var home = cfg.teams[1];
                Color grass = a;
                Color light = Color.Lerp(grass, line, pp.logoLightMix), dark = Color.Lerp(grass, Colors.Hex(home.color), pp.logoDarkMix) * pp.logoDarkValue;
                CentreMark(p, TeamMarks.For(home), Colors.Hex(home.color), new Vector2(px(0), pz(0)), pp.logoWidthYd * PxPerYd, pp.logoHeightYd * PxPerYd, light, dark, pp.logoSplit, pp.logoAlpha);
            }
            return p.ToTexture("FieldPaint");
        }

        static void Shift(System.Collections.Generic.List<Vector2[]> s, Vector2 by) { foreach (var a in s) for (int i = 0; i < a.Length; i++) a[i] += by; }

        // A pen stroke with a soft edge: a faint wider pass `soft` pixels out each side, then the core, together `alpha`
        // opaque in the middle.
        static void Soft(Painter p, Vector2[] pts, float width, float soft, Color c, float alpha)
        {
            float halo = alpha * 0.35f;
            float core = 1f - (1f - alpha) / Mathf.Max(1f - halo, 1e-3f);
            if (soft > 0.01f) p.Stroke(pts, width + soft * 2f, new Color(c.r, c.g, c.b, halo));
            else core = alpha;
            p.Stroke(pts, width, new Color(c.r, c.g, c.b, core));
        }

        // The brush: seeded paint dabs (long along the mowing direction, leaning a little either way), each a touch
        // lighter or darker than the grass; later dabs lie over earlier ones. One value per pixel of a bw x bh area.
        static float[] Brush(FieldPaintConfig pp, int bw, int bh)
        {
            var v = new float[bw * bh];
            int count = Mathf.RoundToInt(bw * (float)bh / (PxPerYd * PxPerYd) * pp.brushDabsPerSqYd);
            float half = pp.brushLenYd * PxPerYd * 0.5f, wid = Mathf.Max(pp.brushWidthYd * PxPerYd * 0.5f, 0.5f);
            float tilt = pp.brushTiltDeg * Mathf.Deg2Rad;
            for (int i = 0; i < count; i++)
            {
                float cx = Hash(i, 101) * bw, cy = Hash(i, 202) * bh;
                float ang = (Hash(i, 303) - 0.5f) * 2f * tilt, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                float shade = (Hash(i, 404) - 0.5f) * 2f * pp.brushStrength;
                float len = Mathf.Max(half * (0.6f + 0.8f * Hash(i, 505)), 0.5f);
                int r = Mathf.CeilToInt(len) + 1;
                for (int y = Mathf.Max(0, (int)cy - r); y < Mathf.Min(bh, (int)cy + r); y++)
                    for (int x = Mathf.Max(0, (int)cx - r); x < Mathf.Min(bw, (int)cx + r); x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float u = (dx * ca + dy * sa) / len, t = (dy * ca - dx * sa) / wid;
                        float d = u * u + t * t;
                        if (d >= 1f) continue;
                        // a flat dab with a soft rim
                        float k = Mathf.Clamp01((1f - d) * 3f);
                        int j = y * bw + x;
                        v[j] = Mathf.Lerp(v[j], shade, k);
                    }
            }
            return v;
        }

        // A team's mark painted onto the grass in two tones: centred at c, fitted inside w x h pixels (its shape kept,
        // its empty margins trimmed), the parts in the team's colour in `dark`, everything else (white, trim, a darker
        // letter) in `light`, at `alpha`: a light emblem on a dark field, as in concept 04. The mark is the team's ONE
        // texture (TeamMarks: Resources/Logos/<SHORT>.png when there is one), so swapping that file swaps this too.
        static void CentreMark(Painter p, Texture2D t, Color team, Vector2 c, float w, float h, Color light, Color dark, float split, float alpha)
        {
            var src = Pixels(t);
            if (src == null) return;
            int tw = t.width, th = t.height;
            // the mark's visible bounds
            int bx0 = tw, by0 = th, bx1 = -1, by1 = -1;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    if (src[y * tw + x].a > 0.05f) { bx0 = Mathf.Min(bx0, x); bx1 = Mathf.Max(bx1, x); by0 = Mathf.Min(by0, y); by1 = Mathf.Max(by1, y); }
            if (bx1 < bx0) return;
            float sw = bx1 - bx0 + 1, sh = by1 - by0 + 1;
            float scale = Mathf.Min(w / sw, h / sh);
            int nw = Mathf.RoundToInt(sw * scale), nh = Mathf.RoundToInt(sh * scale);
            int ox = Mathf.RoundToInt(c.x - nw * 0.5f), oy = Mathf.RoundToInt(c.y - nh * 0.5f);
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    var s = Bilinear(src, tw, th, bx0 + (x + 0.5f) / scale - 0.5f, by0 + (y + 0.5f) / scale - 0.5f);
                    if (s.a <= 0.003f) continue;
                    // how far this pixel's colour is from the team colour (0 = the same, about 1.7 = opposite)
                    float far = Mathf.Sqrt((s.r - team.r) * (s.r - team.r) + (s.g - team.g) * (s.g - team.g) + (s.b - team.b) * (s.b - team.b));
                    float tone = Mathf.Clamp01((far - split) / 0.12f + 0.5f);
                    var col = Color.Lerp(dark, light, tone);
                    col.a = 1f;
                    p.Blend(ox + x, oy + y, col, s.a * alpha);
                }
        }

        // The texture's pixels, also from a PNG that was not marked Read/Write (copied through the GPU); null if neither works.
        static Color[] Pixels(Texture2D t)
        {
            if (t == null) return null;
            if (t.isReadable)
            {
                try { return t.GetPixels(); }
                catch (UnityException) { }
            }
            try
            {
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                copy.Apply(false, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = copy.GetPixels();
                if (Application.isPlaying) Object.Destroy(copy); else Object.DestroyImmediate(copy);
                return px;
            }
            catch (System.Exception) { return null; }
        }

        static Color Bilinear(Color[] src, int tw, int th, float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            Color At(int px, int py) => src[Mathf.Clamp(py, 0, th - 1) * tw + Mathf.Clamp(px, 0, tw - 1)];
            return Color.Lerp(Color.Lerp(At(ix, iy), At(ix + 1, iy), fx), Color.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), fx), fy);
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
