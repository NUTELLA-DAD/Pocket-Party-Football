using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The toon scoreboard (FEATURES.toonHud): the title screen's and the phone's navy and gold. A gold-rimmed navy bar
    // with a dark ink edge across the top centre; each team a plate in its colours with a lighter top half (cel
    // shading, no gradients), its emblem on a cream badge that pokes out past the end of the bar, its short name and
    // its score in a dark box; the quarter and the clock in the middle; the down and distance on a gold tab tucked
    // under the middle. Everything is rounded boxes and Pocket Block lettering (UiFont), drawn fresh each frame.
    public static class ToonHud
    {
        public static readonly Color Gold = new Color(1f, 0.788f, 0.157f), GoldHi = new Color(1f, 0.949f, 0.659f), GoldLo = new Color(0.851f, 0.541f, 0f);
        public static readonly Color Navy = new Color(0.063f, 0.129f, 0.353f), NavyHi = new Color(0.106f, 0.204f, 0.471f), NavyDeep = new Color(0.039f, 0.086f, 0.251f);
        public static readonly Color Cream = new Color(0.984f, 0.953f, 0.863f);

        // Where every piece goes, in GUI space (y down), for a screen `screenW` wide at scale s (height / 1080).
        public struct Layout
        {
            public float K;                          // one unit at this screen size
            public Rect Bar, Center, Tab;            // Tab: zero with no down and distance
            public Rect[] Plate, Badge, Name, Score; // [0] away (left), [1] home (right)
            public float BannerTop;                  // the huddle / whistle banner goes under this
        }

        public static Layout Measure(float screenW, float s, bool tab)
        {
            float k = s * 1.06f;
            float plateW = 272 * k, centerW = 196 * k, gap = 8 * k, rim = 8 * k, h = 80 * k, y0 = 20 * k;
            float total = plateW * 2 + centerW + gap * 2 + rim * 2;
            float x0 = (screenW - total) * 0.5f;
            var l = new Layout { K = k, Plate = new Rect[2], Badge = new Rect[2], Name = new Rect[2], Score = new Rect[2] };
            l.Bar = new Rect(x0, y0, total, h);
            float iy = y0 + rim, ih = h - rim * 2;
            l.Plate[0] = new Rect(x0 + rim, iy, plateW, ih);
            l.Center = new Rect(l.Plate[0].xMax + gap, iy, centerW, ih);
            l.Plate[1] = new Rect(l.Center.xMax + gap, iy, plateW, ih);
            float d = 96 * k, poke = 18 * k, sw = 104 * k, sp = 6 * k;
            l.Badge[0] = new Rect(x0 - poke, y0 + h * 0.5f - d * 0.5f, d, d);
            l.Badge[1] = new Rect(l.Bar.xMax + poke - d, l.Badge[0].y, d, d);
            l.Score[0] = new Rect(l.Plate[0].xMax - sp - sw, iy + sp, sw, ih - sp * 2);
            l.Score[1] = new Rect(l.Plate[1].x + sp, iy + sp, sw, ih - sp * 2);
            l.Name[0] = new Rect(l.Badge[0].xMax + 4 * k, iy, l.Score[0].x - l.Badge[0].xMax - 8 * k, ih);
            l.Name[1] = new Rect(l.Score[1].xMax + 4 * k, iy, l.Badge[1].x - l.Score[1].xMax - 8 * k, ih);
            l.Tab = tab ? new Rect(l.Center.center.x - 115 * k, l.Bar.yMax - 14 * k, 230 * k, 48 * k) : Rect.zero;
            l.BannerTop = (tab ? l.Tab.yMax : l.Bar.yMax) + 14 * k;
            return l;
        }

        static GUIStyle text;

        static GUIStyle Text(float size, TextAnchor anchor)
        {
            text ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Normal, richText = false, wordWrap = false, clipping = TextClipping.Overflow };
            text.font = UiFont.Font;
            text.alignment = anchor;
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            return text;
        }

        public static void Scoreboard(GameConfig cfg, int[] score, string quarter, string clock, string down, float s)
        {
            bool tab = !string.IsNullOrEmpty(down);
            var l = Measure(Screen.width, s, tab);
            float k = l.K, R = 24 * k;

            Box(Grow(l.Bar, 0f, 7 * k, 0f, -7 * k), new Color(0f, 0f, 0f, 0.38f), R);   // drop shadow
            if (tab)
            {
                Box(Grow(l.Tab, 4 * k), UiFont.Ink, 18 * k);
                Box(l.Tab, Gold, 14 * k);
                Box(Lower(l.Tab, 0.42f), GoldLo, 0f, 0f, 14 * k, 14 * k);
            }
            Box(Grow(l.Bar, 4 * k), UiFont.Ink, R + 4 * k);
            Box(l.Bar, Gold, R);
            Box(Lower(l.Bar, 0.5f), GoldLo, 0f, 0f, R, R);
            Box(new Rect(l.Bar.x + R, l.Bar.y + 2 * k, l.Bar.width - 2 * R, 3 * k), GoldHi, 1.5f * k);
            var inner = Grow(l.Bar, -5 * k);
            Box(inner, Navy, R - 5 * k);
            Box(Upper(inner, 0.5f), NavyHi, R - 5 * k, R - 5 * k, 0f, 0f);

            for (int t = 0; t < 2 && t < cfg.teams.Count; t++)
            {
                var team = cfg.teams[t];
                Color c = Colors.Hex(team.color), c2 = Colors.Hex(team.color2);
                var p = l.Plate[t];
                float pr = 12 * k;
                Box(Grow(p, 2 * k), UiFont.Ink, pr + 2 * k);
                Box(p, Color.Lerp(c, Color.black, 0.18f), pr);
                Box(Upper(p, 0.52f), Color.Lerp(c, Color.white, 0.12f), pr, pr, 0f, 0f);
                Box(new Rect(p.x, p.yMax - 6 * k, p.width, 6 * k), c2, 0f, 0f, pr, pr);   // the second colour along the foot

                var sb = l.Score[t];
                Box(Grow(sb, 2 * k), UiFont.Ink, 12 * k);
                Box(sb, NavyDeep, 10 * k);
                UiFont.Toon(new Rect(sb.x, sb.y + 1 * k, sb.width, sb.height), score != null && t < score.Length ? score[t].ToString() : "0",
                    Text(50 * k, TextAnchor.MiddleCenter), Gold, 2.6f * k);
                UiFont.Toon(l.Name[t], team.@short, Text(36 * k, TextAnchor.MiddleCenter), Cream, 2.4f * k);

                var b = l.Badge[t];
                Box(Grow(b, 4 * k), UiFont.Ink, b.width * 0.5f + 4 * k);
                Box(b, Gold, b.width * 0.5f);
                Box(Grow(b, -6 * k), Cream, b.width * 0.5f - 6 * k);
                var mark = TeamMarks.For(team);
                if (mark != null) GUI.DrawTexture(Grow(b, -15 * k), mark, ScaleMode.ScaleToFit);
            }

            var cr = l.Center;
            if (string.IsNullOrEmpty(clock))
                UiFont.Toon(cr, quarter, Text(38 * k, TextAnchor.MiddleCenter), Gold, 2.4f * k);
            else
            {
                UiFont.Toon(new Rect(cr.x, cr.y + 1 * k, cr.width, 22 * k), quarter, Text(22 * k, TextAnchor.MiddleCenter), Gold, 1.8f * k);
                UiFont.Toon(new Rect(cr.x, cr.y + 21 * k, cr.width, cr.height - 21 * k), clock, Text(40 * k, TextAnchor.MiddleCenter), Cream, 2.4f * k);
            }
            if (tab)
            {
                var shown = new Rect(l.Tab.x, l.Bar.yMax + 4 * k, l.Tab.width, l.Tab.yMax - l.Bar.yMax - 4 * k);
                UiFont.Toon(shown, down, Text(26 * k, TextAnchor.MiddleCenter), NavyDeep, 0f);
            }
        }

        // A navy pill with a gold rim under the scoreboard (the huddle, the whistle, HALFTIME): cream lettering.
        public static void Banner(string line, float s, bool tab)
        {
            if (string.IsNullOrEmpty(line)) return;
            var l = Measure(Screen.width, s, tab);
            float k = l.K;
            var st = Text(32 * k, TextAnchor.MiddleCenter);
            float w = st.CalcSize(new GUIContent(line)).x + 64 * k, h = 52 * k;
            var r = new Rect((Screen.width - w) * 0.5f, l.BannerTop, w, h);
            Box(Grow(r, 0f, 5 * k, 0f, -5 * k), new Color(0f, 0f, 0f, 0.35f), h * 0.5f);
            Box(Grow(r, 3 * k), UiFont.Ink, h * 0.5f + 3 * k);
            Box(r, Gold, h * 0.5f);
            var inner = Grow(r, -4 * k);
            Box(inner, Navy, inner.height * 0.5f);
            Box(Upper(inner, 0.5f), NavyHi, inner.height * 0.5f, inner.height * 0.5f, 0f, 0f);
            UiFont.Toon(new Rect(r.x, r.y + 1 * k, r.width, r.height), line, st, Cream, 2f * k);
        }

        // ---- boxes (rounded by radius, all four corners or each one: top-left, top-right, bottom-right, bottom-left)

        static void Box(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

        static void Box(Rect r, Color c, float tl, float tr, float br, float bl) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(tl, tr, br, bl));

        static Rect Grow(Rect r, float d) => new Rect(r.x - d, r.y - d, r.width + d * 2, r.height + d * 2);

        // (left, down, right, up: how far each edge moves out; the drop shadow moves its top edge down)
        static Rect Grow(Rect r, float left, float down, float right, float up) => new Rect(r.x - left, r.y - up, r.width + left + right, r.height + up + down);

        static Rect Upper(Rect r, float frac) => new Rect(r.x, r.y, r.width, r.height * frac);

        static Rect Lower(Rect r, float frac) => new Rect(r.x, r.yMax - r.height * frac, r.width, r.height * frac);
    }
}
