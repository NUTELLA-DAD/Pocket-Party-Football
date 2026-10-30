using System.Collections.Generic;
using UnityEngine;

namespace PocketFootball.View
{
    // Bold block digits and the alphabet as centre-line strokes, in a box 1 wide and 1.4 tall (y up). Drawn with a
    // thick pen they read as stadium paint, jersey numbers and the names on jersey backs (JerseyNames); the same
    // strokes paint the field, the shirts and the team marks, so there are no font files.
    public static class Glyphs
    {
        public const float Width = 1f, Height = 1.4f;

        static Vector2[] P(params float[] xy)
        {
            var a = new Vector2[xy.Length / 2];
            for (int i = 0; i < a.Length; i++) a[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return a;
        }

        static Vector2[] Arc(float cx, float cy, float rx, float ry, float from, float to, int n = 14)
        {
            var a = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)n) * Mathf.Deg2Rad;
                a[i] = new Vector2(cx + Mathf.Cos(t) * rx, cy + Mathf.Sin(t) * ry);
            }
            return a;
        }

        static readonly Dictionary<char, Vector2[][]> Map = new Dictionary<char, Vector2[][]>
        {
            ['0'] = new[] { Arc(0.5f, 0.7f, 0.42f, 0.62f, 0f, 360f, 28) },
            ['1'] = new[] { P(0.22f, 1.12f, 0.55f, 1.38f, 0.55f, 0.02f) },
            ['2'] = new[] { Arc(0.5f, 1.0f, 0.4f, 0.36f, 160f, -20f, 10), P(0.88f, 0.86f, 0.08f, 0.04f, 0.95f, 0.04f) },
            ['3'] = new[] { Arc(0.48f, 1.04f, 0.38f, 0.32f, 150f, -90f, 12), Arc(0.48f, 0.38f, 0.44f, 0.36f, 90f, -150f, 12) },
            ['4'] = new[] { P(0.72f, 0.02f, 0.72f, 1.38f, 0.04f, 0.46f, 0.98f, 0.46f) },
            ['5'] = new[] { P(0.92f, 1.36f, 0.14f, 1.36f, 0.1f, 0.8f), Arc(0.48f, 0.42f, 0.44f, 0.4f, 135f, -150f, 14) },
            ['6'] = new[] { Arc(0.5f, 0.42f, 0.42f, 0.4f, 0f, 360f, 20), Arc(0.92f, 0.62f, 0.84f, 0.76f, 90f, 175f, 10) },
            ['7'] = new[] { P(0.04f, 1.36f, 0.96f, 1.36f, 0.36f, 0.02f) },
            ['8'] = new[] { Arc(0.5f, 1.05f, 0.33f, 0.31f, 0f, 360f, 20), Arc(0.5f, 0.38f, 0.42f, 0.37f, 0f, 360f, 22) },
            ['9'] = new[] { Arc(0.5f, 0.98f, 0.42f, 0.4f, 0f, 360f, 20), Arc(0.08f, 0.78f, 0.84f, 0.76f, -90f, 5f, 10) },
            ['A'] = new[] { P(0.06f, 0.02f, 0.5f, 1.38f, 0.94f, 0.02f), P(0.24f, 0.5f, 0.76f, 0.5f) },
            ['B'] = new[] { P(0.12f, 0.02f, 0.12f, 1.38f, 0.52f, 1.38f), Arc(0.52f, 1.04f, 0.38f, 0.34f, 90f, -90f, 10), P(0.52f, 0.7f, 0.12f, 0.7f), Arc(0.54f, 0.36f, 0.42f, 0.34f, 90f, -90f, 10), P(0.54f, 0.02f, 0.12f, 0.02f) },
            ['C'] = new[] { Arc(0.56f, 0.7f, 0.46f, 0.64f, 50f, 310f, 22) },
            ['D'] = new[] { P(0.12f, 0.02f, 0.12f, 1.38f, 0.4f, 1.38f), Arc(0.4f, 0.7f, 0.54f, 0.68f, 90f, -90f, 14), P(0.4f, 0.02f, 0.12f, 0.02f) },
            ['E'] = new[] { P(0.92f, 1.38f, 0.12f, 1.38f, 0.12f, 0.02f, 0.92f, 0.02f), P(0.12f, 0.72f, 0.8f, 0.72f) },
            ['F'] = new[] { P(0.92f, 1.38f, 0.12f, 1.38f, 0.12f, 0.02f), P(0.12f, 0.72f, 0.8f, 0.72f) },
            ['G'] = new[] { Arc(0.54f, 0.7f, 0.44f, 0.66f, 40f, 320f, 22), P(0.9f, 0.26f, 0.9f, 0.64f, 0.58f, 0.64f) },
            ['H'] = new[] { P(0.1f, 0.02f, 0.1f, 1.38f), P(0.9f, 0.02f, 0.9f, 1.38f), P(0.1f, 0.7f, 0.9f, 0.7f) },
            ['I'] = new[] { P(0.5f, 0.02f, 0.5f, 1.38f), P(0.2f, 1.38f, 0.8f, 1.38f), P(0.2f, 0.02f, 0.8f, 0.02f) },
            ['J'] = new[] { P(0.2f, 1.38f, 0.92f, 1.38f), P(0.66f, 1.38f, 0.66f, 0.4f), Arc(0.4f, 0.4f, 0.26f, 0.38f, 0f, -180f, 10) },
            ['K'] = new[] { P(0.12f, 1.38f, 0.12f, 0.02f), P(0.9f, 1.38f, 0.14f, 0.62f), P(0.38f, 0.82f, 0.92f, 0.02f) },
            ['L'] = new[] { P(0.14f, 1.38f, 0.14f, 0.02f, 0.92f, 0.02f) },
            ['M'] = new[] { P(0.08f, 0.02f, 0.08f, 1.38f, 0.5f, 0.5f, 0.92f, 1.38f, 0.92f, 0.02f) },
            ['N'] = new[] { P(0.1f, 0.02f, 0.1f, 1.38f, 0.9f, 0.02f, 0.9f, 1.38f) },
            ['O'] = new[] { Arc(0.5f, 0.7f, 0.44f, 0.66f, 0f, 360f, 28) },
            ['P'] = new[] { P(0.12f, 0.02f, 0.12f, 1.38f, 0.52f, 1.38f), Arc(0.52f, 1.0f, 0.4f, 0.38f, 90f, -90f, 12), P(0.52f, 0.62f, 0.12f, 0.62f) },
            ['Q'] = new[] { Arc(0.5f, 0.7f, 0.44f, 0.66f, 0f, 360f, 28), P(0.6f, 0.4f, 0.98f, 0.0f) },
            ['R'] = new[] { P(0.12f, 0.02f, 0.12f, 1.38f, 0.52f, 1.38f), Arc(0.52f, 1.0f, 0.4f, 0.38f, 90f, -90f, 12), P(0.52f, 0.62f, 0.12f, 0.62f), P(0.5f, 0.62f, 0.94f, 0.02f) },
            ['S'] = new[] { Arc(0.5f, 1.04f, 0.4f, 0.34f, 20f, 270f, 14), Arc(0.5f, 0.36f, 0.42f, 0.34f, 90f, -160f, 14) },
            ['T'] = new[] { P(0.06f, 1.38f, 0.94f, 1.38f), P(0.5f, 1.38f, 0.5f, 0.02f) },
            ['U'] = new[] { P(0.1f, 1.38f, 0.1f, 0.44f), Arc(0.5f, 0.44f, 0.4f, 0.42f, 180f, 360f, 12), P(0.9f, 0.44f, 0.9f, 1.38f) },
            ['V'] = new[] { P(0.06f, 1.38f, 0.5f, 0.02f, 0.94f, 1.38f) },
            ['W'] = new[] { P(0.04f, 1.38f, 0.26f, 0.02f, 0.5f, 0.9f, 0.74f, 0.02f, 0.96f, 1.38f) },
            ['X'] = new[] { P(0.08f, 1.38f, 0.92f, 0.02f), P(0.92f, 1.38f, 0.08f, 0.02f) },
            ['Y'] = new[] { P(0.08f, 1.38f, 0.5f, 0.7f, 0.92f, 1.38f), P(0.5f, 0.7f, 0.5f, 0.02f) },
            ['Z'] = new[] { P(0.08f, 1.38f, 0.92f, 1.38f, 0.08f, 0.02f, 0.92f, 0.02f) },
            ['-'] = new[] { P(0.2f, 0.7f, 0.8f, 0.7f) },
            ['\''] = new[] { P(0.5f, 1.38f, 0.44f, 1.1f) },
        };

        public static bool Has(char c) => Map.ContainsKey(c);

        public static Vector2[][] Of(char c) => Map.TryGetValue(c, out var s) ? s : new Vector2[0][];

        // The strokes of a whole string, laid out left to right with `gap` between glyphs, scaled to `height`.
        // Returns the strokes in a box starting at (0, 0); width tells how wide the text came out.
        public static List<Vector2[]> Text(string text, float height, float gap, out float width)
        {
            float k = height / Height;
            var outp = new List<Vector2[]>();
            float x = 0f;
            foreach (char ch in text)
            {
                foreach (var stroke in Of(ch))
                {
                    var s = new Vector2[stroke.Length];
                    for (int i = 0; i < s.Length; i++) s[i] = new Vector2(x + stroke[i].x * k, stroke[i].y * k);
                    outp.Add(s);
                }
                x += (Width + gap) * k;
            }
            width = x - gap * k;
            return outp;
        }
    }

    // A tiny software painter for code-made textures (the field paint, the team marks, HUD pieces): thick strokes,
    // filled polygons and discs, anti-aliased over about a pixel. Coordinates are in pixels, y up.
    public sealed class Painter
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public Painter(int w, int h, Color fill)
        {
            W = w; H = h;
            Px = new Color32[w * h];
            Color32 f = fill;
            for (int i = 0; i < Px.Length; i++) Px[i] = f;
        }

        public void Blend(int x, int y, Color c, float cover)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || cover <= 0f) return;
            int i = y * W + x;
            float a = c.a * Mathf.Clamp01(cover);
            Color d = Px[i];
            float oa = a + d.a * (1f - a);
            Px[i] = oa <= 0f ? new Color(0, 0, 0, 0)
                : new Color((c.r * a + d.r * d.a * (1f - a)) / oa, (c.g * a + d.g * d.a * (1f - a)) / oa, (c.b * a + d.b * d.a * (1f - a)) / oa, oa);
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return (p - (a + ab * t)).magnitude;
        }

        // A round-ended pen of `width` along the polyline.
        public void Stroke(IList<Vector2> pts, float width, Color c)
        {
            float r = width * 0.5f;
            Vector2 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            int x0 = Mathf.FloorToInt(mn.x - r - 1), x1 = Mathf.CeilToInt(mx.x + r + 1);
            int y0 = Mathf.FloorToInt(mn.y - r - 1), y1 = Mathf.CeilToInt(mx.y + r + 1);
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(H - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(W - 1, x1); x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float d = float.MaxValue;
                    for (int i = 0; i + 1 < pts.Count; i++) d = Mathf.Min(d, SegDist(q, pts[i], pts[i + 1]));
                    if (pts.Count == 1) d = (q - pts[0]).magnitude;
                    Blend(x, y, c, r - d + 0.5f);
                }
        }

        public void Strokes(IEnumerable<Vector2[]> strokes, float width, Color c) { foreach (var s in strokes) Stroke(s, width, c); }

        // A filled polygon (even-odd), anti-aliased by 2x2 supersampling.
        public void Fill(IList<Vector2> poly, Color c)
        {
            Vector2 mn = poly[0], mx = poly[0];
            foreach (var p in poly) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            for (int y = Mathf.Max(0, (int)mn.y - 1); y <= Mathf.Min(H - 1, (int)mx.y + 1); y++)
                for (int x = Mathf.Max(0, (int)mn.x - 1); x <= Mathf.Min(W - 1, (int)mx.x + 1); x++)
                {
                    int hit = 0;
                    for (int s = 0; s < 4; s++)
                        if (Inside(poly, new Vector2(x + 0.25f + 0.5f * (s & 1), y + 0.25f + 0.5f * (s >> 1)))) hit++;
                    Blend(x, y, c, hit / 4f);
                }
        }

        static bool Inside(IList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        public void Disc(Vector2 c, float r, Color col)
        {
            for (int y = Mathf.Max(0, (int)(c.y - r - 1)); y <= Mathf.Min(H - 1, (int)(c.y + r + 1)); y++)
                for (int x = Mathf.Max(0, (int)(c.x - r - 1)); x <= Mathf.Min(W - 1, (int)(c.x + r + 1)); x++)
                    Blend(x, y, col, r - (new Vector2(x + 0.5f, y + 0.5f) - c).magnitude + 0.5f);
        }

        public Texture2D ToTexture(string name, bool mips = true)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, mips) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(Px);
            t.Apply(mips, false);
            return t;
        }
    }
}
