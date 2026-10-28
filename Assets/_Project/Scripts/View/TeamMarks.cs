using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // Each team's mark is ONE texture, used everywhere (the centre of the field, the scoreboard, banners).
    // It is the PNG at Assets/_Project/Resources/Logos/<SHORT>.png (ARR the Arrows, GRN the Grains: the same emblems
    // the helmets wear, see tools/rebrand), a one-file change; without one, the game draws a plain mark from code.
    public static class TeamMarks
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        const int Size = 512;

        public static Texture2D For(TeamConfig t)
        {
            if (cache.TryGetValue(t.@short, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("Logos/" + t.@short);
            if (tex == null) tex = Sweep(t);
            cache[t.@short] = tex;
            return tex;
        }

        // A slanted banner with a white edge, a bold italic first letter, wheat chevrons at both ends.
        static Texture2D Sweep(TeamConfig t)
        {
            var p0 = new Painter(Size, Size, new Color(1, 1, 1, 0));
            Color tc = Colors.Hex(t.color), wh = Color.white, dark = tc * 0.72f + new Color(0, 0, 0, 0.28f);
            var banner = new List<Vector2> { new Vector2(20, 150), new Vector2(400, 150), new Vector2(492, 362), new Vector2(112, 362) };
            p0.Fill(Grow(banner, 14), wh);
            p0.Fill(banner, tc);
            var s0 = Glyphs.Text(t.@short.Substring(0, 1), 300, 0f, out float sw0);
            Slant(s0, 0.32f);
            Offset(s0, new Vector2(212 - sw0 * 0.5f, 92));
            p0.Strokes(s0, 128, wh);
            p0.Strokes(s0, 92, dark);
            // wheat: chevron rows pointing out at both ends
            for (int i = 0; i < 3; i++)
            {
                float y = 214 + i * 34;
                float lx = 70 + i * 10, rx = 446 - (2 - i) * 10;
                p0.Stroke(new[] { new Vector2(lx + 34, y + 16), new Vector2(lx, y), new Vector2(lx + 34, y - 16) }, 12, wh);
                p0.Stroke(new[] { new Vector2(rx - 34, y + 16), new Vector2(rx, y), new Vector2(rx - 34, y - 16) }, 12, wh);
            }
            return p0.ToTexture("Mark " + t.@short);
        }

        static Texture2D SweepOld(TeamConfig t)
        {
            var p = new Painter(Size, Size, new Color(1, 1, 1, 0));
            Color team = Colors.Hex(t.color), white = Color.white;
            var letter = Glyphs.Text(t.@short.Substring(0, 1), 340, 0f, out float w);
            Slant(letter, 0.25f);
            Offset(letter, new Vector2(160 - w * 0.5f, 86));
            // three wheat grains trailing off the top of the letter, short fat strokes leaning back
            var wheat = new List<Vector2[]>();
            for (int i = 0; i < 3; i++)
            {
                float x = 352 + i * 48, y = 404 - i * 30;
                wheat.Add(new[] { new Vector2(x, y), new Vector2(x + 30, y + 24) });
                // and the same, mirrored, trailing off the bottom left
                float mx = 512 - x - 30, my = 512 - y - 24;
                wheat.Add(new[] { new Vector2(mx, my), new Vector2(mx + 30, my + 24) });
            }
            p.Strokes(letter, 150, white);
            p.Strokes(wheat, 64, white);
            p.Strokes(letter, 118, team);
            p.Strokes(wheat, 38, team);
            return p.ToTexture("Mark " + t.@short);
        }

        static void Offset(List<Vector2[]> s, Vector2 by) { foreach (var a in s) for (int i = 0; i < a.Length; i++) a[i] += by; }
        static void Slant(List<Vector2[]> s, float k) { foreach (var a in s) for (int i = 0; i < a.Length; i++) a[i].x += a[i].y * k; }

        static List<Vector2> Grow(List<Vector2> poly, float by)
        {
            Vector2 c = Vector2.zero;
            foreach (var v in poly) c += v;
            c /= poly.Count;
            var g = new List<Vector2>();
            foreach (var v in poly) g.Add(c + (v - c) * ((v - c).magnitude + by) / Mathf.Max((v - c).magnitude, 1e-3f));
            return g;
        }
    }
}
