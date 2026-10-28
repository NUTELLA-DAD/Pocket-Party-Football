using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // FEATURES.jerseyNumbers and FEATURES.eyesLook: every player wears his own number and his eyes move. The numbers and
    // irises were painted out of each model's colour maps; its marks map (tools/rig/marks.py, game.json "marks") tells
    // the toon shader which texels are a number (chest, back, shoulders) or an eye, and the shader draws them there:
    // the digits from the game's own glyphs (Glyphs), the iris where the player's eyes are looking, the lids on a blink.
    // The material carries the model's map and the team's number colours; each player's renderer carries his number and
    // his look (a MaterialPropertyBlock). With jerseyNumbers off everyone wears the number first painted on the model.
    public static class JerseyMarks
    {
        static readonly int MarksId = Shader.PropertyToID("_Marks"), MarksTexId = Shader.PropertyToID("_MarksTex"),
            DigitsTexId = Shader.PropertyToID("_DigitsTex"), DigitsId = Shader.PropertyToID("_NumberDigits"),
            FillId = Shader.PropertyToID("_NumberFill"), EdgeId = Shader.PropertyToID("_NumberEdge"),
            StyleId = Shader.PropertyToID("_NumberStyle"), LookId = Shader.PropertyToID("_EyeLook"),
            IrisId = Shader.PropertyToID("_IrisColor"), LidId = Shader.PropertyToID("_LidColor");

        // The digit atlas: ten cells side by side, each the glyph's box (1 x 1.4) with Margin round it, CellPx pixels a
        // glyph unit; each pixel the distance to the glyph's centre line over MaxDist (the shader's 0.6).
        const float Margin = 0.4f, MaxDist = 0.6f;
        const int CellPx = 40;
        static Texture2D digits;
        static readonly Dictionary<string, Texture2D> maps = new Dictionary<string, Texture2D>();
        static MaterialPropertyBlock block;

        public static bool On(GameConfig cfg) => cfg.Feature("jerseyNumbers") || cfg.Feature("eyesLook");

        public static Texture2D Digits()
        {
            if (digits != null) return digits;
            int cw = Mathf.RoundToInt((Glyphs.Width + 2 * Margin) * CellPx), ch = Mathf.RoundToInt((Glyphs.Height + 2 * Margin) * CellPx);
            var px = new byte[cw * 10 * ch];
            for (int d = 0; d < 10; d++)
            {
                var strokes = Glyphs.Of((char)('0' + d));
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        var p = new Vector2((x + 0.5f) / CellPx - Margin, (y + 0.5f) / CellPx - Margin);
                        float best = MaxDist;
                        foreach (var s in strokes)
                            for (int i = 0; i + 1 < s.Length; i++) best = Mathf.Min(best, SegDist(p, s[i], s[i + 1]));
                        px[y * cw * 10 + d * cw + x] = (byte)Mathf.RoundToInt(best / MaxDist * 255f);
                    }
            }
            digits = new Texture2D(cw * 10, ch, TextureFormat.R8, false, true)
            { name = "Jersey digits", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            digits.SetPixelData(px, 0);
            digits.Apply(false, false);
            return digits;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return (p - (a + ab * t)).magnitude;
        }

        // Ready a model's material for its marks: its map, the digits, the team's number colours, the eye colours.
        // False (the material untouched): both features off, or the model has no marks.
        public static bool Dress(Material m, GameConfig cfg, TeamConfig team, string model)
        {
            var mm = cfg.marks.For(model);
            if (!On(cfg) || mm == null || string.IsNullOrEmpty(mm.map)) return false;
            if (!maps.TryGetValue(mm.map, out var map))
            {
                maps[mm.map] = map = Resources.Load<Texture2D>(mm.map);
                if (map == null) Debug.LogWarning("[marks] no marks map at Resources/" + mm.map + "; " + model + " shows its painted-out jersey");
            }
            if (map == null) return false;
            m.SetFloat(MarksId, 1f);
            m.SetTexture(MarksTexId, map);
            m.SetTexture(DigitsTexId, Digits());
            m.SetVector(StyleId, new Vector4(cfg.marks.penHalf, cfg.marks.edge, cfg.marks.gap, 0f));
            m.SetColor(FillId, Hex(team?.numberFill, Color.white));
            m.SetColor(EdgeId, Hex(team?.numberEdge, Color.white));
            m.SetColor(IrisId, Hex(mm.iris, new Color(0.4f, 0.22f, 0.1f)));
            m.SetColor(LidId, Hex(mm.lid, new Color(0.6f, 0.38f, 0.22f)));
            // jerseyNumbers off: everyone on this model wears the number first painted on it
            m.SetVector(DigitsId, cfg.Feature("jerseyNumbers") ? Vector4.zero : Digits(mm.number));
            return true;
        }

        static Color Hex(string hex, Color fallback) => string.IsNullOrEmpty(hex) ? fallback : Colors.Hex(hex);

        // The shader's _NumberDigits for a number: tens (or the only digit), ones, how many digits; 0..99.
        public static Vector4 Digits(int number)
        {
            number = Mathf.Clamp(number, 0, 99);
            return number >= 10 ? new Vector4(number / 10, number % 10, 2, 0) : new Vector4(number, 0, 1, 0);
        }

        // A player's own number on every renderer under him (a live model or a sideline player baked still).
        public static void SetNumber(GameObject holder, GameConfig cfg, int number)
        {
            if (holder == null || !cfg.Feature("jerseyNumbers")) return;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) Set(r, DigitsId, Digits(number));
        }

        // Where a player's eyes look (iris radii: x to his left, y up) and how shut his lids are (0..1).
        public static void SetEyes(Renderer r, float x, float y, float blink) => Set(r, LookId, new Vector4(x, y, blink, 0f));

        static void Set(Renderer r, int id, Vector4 v)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);   // (keeps the number while the eyes change, and the other way round)
            block.SetVector(id, v);
            r.SetPropertyBlock(block);
        }
    }
}
