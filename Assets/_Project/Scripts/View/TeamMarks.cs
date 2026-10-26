using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // Each team's mark is ONE texture, used everywhere (helmets, the centre of the field, the scoreboard, banners).
    // A PNG at Assets/_Project/Resources/Logos/<SHORT>.png (KC, SSK) replaces it, a one-file change; without one, the
    // game draws its own mark from code (our own simple marks, not the official logos).
    public static class TeamMarks
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        const int Size = 512;

        public static Texture2D For(TeamConfig t)
        {
            if (cache.TryGetValue(t.@short, out var tex) && tex != null) return tex;
            tex = Resources.Load<Texture2D>("Logos/" + t.@short);
            if (tex == null) tex = t.@short == "KC" ? Arrowhead(t) : Sweep(t);
            cache[t.@short] = tex;
            return tex;
        }

        // A white arrowhead pointing left, a team-colour rim, the team's letters inside.
        static Texture2D Arrowhead(TeamConfig t)
        {
            var p = new Painter(Size, Size, new Color(1, 1, 1, 0));
            Color team = Colors.Hex(t.color), white = Color.white;
            var shape = new List<Vector2> { new Vector2(28, 256), new Vector2(470, 450), new Vector2(420, 256), new Vector2(470, 62) };
            p.Fill(Grow(shape, 22), team);
            p.Fill(shape, white);
            var letters = Glyphs.Text(t.@short, 190, 0.05f, out float w);
            Offset(letters, new Vector2(270 - w * 0.5f, 160));
            p.Strokes(letters, 34, team);
            return p.ToTexture("Mark " + t.@short);
        }

        // A slanted, outlined first letter with three wheat-like chevrons.
        static Texture2D Sweep(TeamConfig t)
        {
            var p = new Painter(Size, Size, new Color(1, 1, 1, 0));
            Color team = Colors.Hex(t.color), white = Color.white;
            var letter = Glyphs.Text(t.@short.Substring(0, 1), 380, 0f, out float w);
            Slant(letter, 0.25f);
            Offset(letter, new Vector2(150 - w * 0.5f, 66));
            var wheat = new List<Vector2[]>();
            for (int i = 0; i < 3; i++)
            {
                float x = 300 + i * 52, y = 300 - i * 46;
                wheat.Add(new[] { new Vector2(x, y + 40), new Vector2(x + 44, y), new Vector2(x + 88, y + 40) });
            }
            p.Strokes(letter, 104, white);
            p.Strokes(wheat, 44, white);
            p.Strokes(letter, 70, team);
            p.Strokes(wheat, 18, team);
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
