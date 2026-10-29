using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // On-field marks drawn over the paint (concept 04): the blue line of scrimmage, the yellow first-down line, and a
    // cyan ring on the grass under each person's player (and the soft FIND ME glow under it).
    public sealed class Markings
    {
        readonly GameConfig cfg;
        readonly Transform los, first;
        readonly Material mat, glow;
        readonly Dictionary<int, Transform> rings = new Dictionary<int, Transform>();
        readonly Dictionary<int, Transform> glows = new Dictionary<int, Transform>();
        readonly Transform root;

        public static readonly Color Cyan = new Color(0.35f, 0.92f, 1f);

        public Markings(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = Looks.Toon(baseMat);
            glow = Looks.Glow(baseMat);
            root = new GameObject("Markings").transform;
            // (a little wider than the painted yard lines, so they read as paint rather than tape; field.paint in game.json)
            var pp = cfg.field.paint;
            float m = cfg.field.yardM, len = cfg.field.widthYd * m, w = pp.scrimmageWidthYd * m;
            los = Looks.Flat(root, "LineOfScrimmage", Vector3.zero, new Vector2(w, len), Colors.Hex(pp.scrimmageColor), mat, Looks.Gloss).transform;
            first = Looks.Flat(root, "FirstDown", Vector3.zero, new Vector2(w, len), Colors.Hex(pp.firstDownColor), mat, Looks.Gloss).transform;
        }

        public void SetLines(float losX, float firstX)
        {
            float m = cfg.field.yardM;
            los.localPosition = new Vector3(losX * m, 0.025f, 0);
            first.localPosition = new Vector3(firstX * m, 0.03f, 0);
        }

        // A ring under each slot in `who` (at the given world positions); others' rings are hidden.
        public void Rings(IEnumerable<KeyValuePair<int, Vector3>> who)
        {
            var seen = new HashSet<int>();
            foreach (var kv in who)
            {
                if (!rings.TryGetValue(kv.Key, out var r)) { r = Ring(); rings[kv.Key] = r; }
                r.gameObject.SetActive(true);
                r.position = new Vector3(kv.Value.x, 0.04f, kv.Value.z);
                seen.Add(kv.Key);
            }
            foreach (var kv in rings) if (!seen.Contains(kv.Key)) kv.Value.gameObject.SetActive(false);
        }

        // FIND ME: a soft glow of the team colour on the grass under the player, pulsing with the ring (stepped at
        // 8 a second, no wobble).
        public void Beam(int slot, Vector3 feet, Color color, bool on, float now)
        {
            if (!glows.TryGetValue(slot, out var g))
            {
                if (!on) return;
                g = Looks.SoftDisc(root, "FindMe", cfg.view.findMeRadiusM, color, cfg.view.findMeAlpha, glow, 0.85f).transform;
                glows[slot] = g;
            }
            g.gameObject.SetActive(on);
            bool big = on && ((int)(now * 8f)) % 2 == 0;
            if (on)
            {
                g.position = new Vector3(feet.x, 0.035f, feet.z);
                g.localScale = Vector3.one * (big ? 1.1f : 1f);
            }
            if (rings.TryGetValue(slot, out var r))
                r.localScale = Vector3.one * (big ? 1.15f : 1f);
        }

        Transform Ring()
        {
            var b = new Blocks { Flag = Looks.Gloss };
            const int n = 40;
            float r0 = cfg.view.playerHeightM * 0.3f, r1 = r0 + 0.17f;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                Vector3 P(float r, float a) => new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r * 0.85f);
                b.Quad(P(r0, a0), P(r1, a0), P(r1, a1), P(r0, a1), Cyan, Vector3.up);
            }
            return b.Build(root, "Ring", mat, false).transform;
        }
    }

    // The screen overlay (immediate-mode GUI, no assets): the scoreboard bar at the top centre, the triangle over your
    // player (team colour), the shape badges over receivers. Pieces are painted once into small textures.
    public static class Hud
    {
        static Texture2D round, disc, tri;
        static GUIStyle text;

        // Where the TV's join panel (QR code, room code) is on screen this frame, else Rect.zero: other overlays keep clear of it.
        public static Rect JoinPanel;

        // Moves a box sideways so it stays on screen and out of the join panel (its left edge then sits just left of it).
        public static Rect Clear(Rect r, float margin)
        {
            var j = JoinPanel;
            if (j.width > 0f && r.Overlaps(j)) r.x = j.x - margin - r.width;
            r.x = Mathf.Clamp(r.x, margin, Mathf.Max(margin, Screen.width - margin - r.width));
            return r;
        }

        static void Init()
        {
            if (round != null) return;
            round = Shape(64, 64, p => p.Fill(RoundRect(4, 4, 56, 56, 14), Color.white));
            disc = Shape(64, 64, p => p.Disc(new Vector2(32, 32), 29, Color.white));
            tri = Shape(64, 64, p => p.Fill(new[] { new Vector2(6, 58), new Vector2(58, 58), new Vector2(32, 10) }, Color.white));
            text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = false };
            text.normal.textColor = Color.white;
        }

        static Texture2D Shape(int w, int h, System.Action<Painter> draw)
        {
            var p = new Painter(w, h, new Color(1, 1, 1, 0));
            draw(p);
            return p.ToTexture("hud", false);
        }

        static List<Vector2> RoundRect(float x, float y, float w, float h, float r)
        {
            var pts = new List<Vector2>();
            void Corner(float cx, float cy, float a0)
            {
                for (int i = 0; i <= 6; i++) { float a = (a0 + i * 15f) * Mathf.Deg2Rad; pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r)); }
            }
            Corner(x + w - r, y + r, -90); Corner(x + w - r, y + h - r, 0); Corner(x + r, y + h - r, 90); Corner(x + r, y + r, 180);
            return pts;
        }

        static void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }
        static void Label(Rect r, string s, int size, Color c) { text.fontSize = size; text.normal.textColor = c; GUI.Label(r, s, text); }

        // ARR 14 | GRN 21 | 3RD 2:18 | 2ND & 10, at the top centre. s = screen scale (height / 1080).
        public static void Scoreboard(GameConfig cfg, int[] score, string quarter, string clock, string down, float s)
        {
            if (cfg.Feature("toonHud") && UiFont.On) { ToonHud.Scoreboard(cfg, score, quarter, clock, down, s); return; }   // FEATURES.toonHud
            Init();
            s *= 1.12f;
            float h = 70 * s, y = 16 * s;
            float teamW = 240 * s, infoW = 168 * s, downW = 156 * s;
            float total = teamW * 2 + infoW + downW + 6 * s;
            float x = (Screen.width - total) * 0.5f;
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.95f);
            GUI.DrawTexture(new Rect(x - 7 * s, y - 6 * s, total + 14 * s, h + 12 * s), round);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(x - 3 * s, y - 2 * s, total + 6 * s, 2 * s), Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int t = 0; t < 2 && t < cfg.teams.Count; t++)
            {
                var team = cfg.teams[t];
                var r = new Rect(x, y, teamW, h);
                Box(r, Colors.Hex(team.color) * 0.92f + new Color(0, 0, 0, 0.08f));
                Box(new Rect(r.x + teamW * 0.62f, y, teamW * 0.38f, h), Colors.Hex(team.color) * 0.62f + new Color(0, 0, 0, 0.38f));
                var mark = TeamMarks.For(team);
                GUI.DrawTexture(new Rect(r.x + 8 * s, y + 6 * s, h - 12 * s, h - 12 * s), mark, ScaleMode.ScaleToFit);
                Label(new Rect(r.x + h - 4 * s, y, teamW * 0.62f - h + 4 * s, h), team.@short, Mathf.RoundToInt(34 * s), Color.white);
                Label(new Rect(r.x + teamW * 0.62f, y, teamW * 0.38f, h), score[t].ToString(), Mathf.RoundToInt(46 * s), Color.white);
                x += teamW + 2 * s;
            }
            x += 4 * s;
            Box(new Rect(x, y, infoW, h), new Color(0.12f, 0.12f, 0.14f, 0.95f));
            Label(new Rect(x, y, infoW, h), quarter + "  " + clock, Mathf.RoundToInt(30 * s), Color.white);
            x += infoW + 6 * s;
            Box(new Rect(x, y + 3 * s, 2 * s, h - 6 * s), new Color(1f, 1f, 1f, 0.35f));
            Box(new Rect(x, y, downW, h), new Color(0.16f, 0.16f, 0.18f, 0.95f));
            Label(new Rect(x, y, downW, h), down, Mathf.RoundToInt(30 * s), Color.white);
        }

        // A person off the edge of the picture: a disc in their team colour with their number at the nearest edge and an
        // arrow pointing their way. Returns false (and draws nothing) when they are on screen.
        public static bool IsOffScreen(Camera cam, Vector3 world)
        {
            var v = cam.WorldToViewportPoint(world);
            return !(v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f);
        }

        public static bool EdgePortrait(Camera cam, Vector3 world, Color team, int number, float s)
        {
            if (!IsOffScreen(cam, world)) return false;
            Init();
            var v = cam.WorldToViewportPoint(world);
            if (v.z <= 0f) { v.x = 1f - v.x; v.y = 0f; }
            float d = 58 * s, m = 12 * s;
            float x = Mathf.Clamp(v.x * Screen.width, m + d * 0.5f, Screen.width - m - d * 0.5f);
            float y = Mathf.Clamp((1f - v.y) * Screen.height, m + d * 0.5f, Screen.height - m - d * 0.5f);
            var r = new Rect(x - d * 0.5f, y - d * 0.5f, d, d);
            GUI.color = Markings.Cyan;
            GUI.DrawTexture(new Rect(r.x - 4 * s, r.y - 4 * s, r.width + 8 * s, r.height + 8 * s), disc);
            GUI.color = team;
            GUI.DrawTexture(r, disc);
            GUI.color = Color.white;
            Label(r, number.ToString(), Mathf.RoundToInt(26 * s), Color.white);
            // the arrow, outside the disc on the side they are
            Vector2 dir = new Vector2(v.x - 0.5f, 0.5f - v.y).normalized;
            float aw = 22 * s;
            var c = new Vector2(x, y) + dir * (d * 0.5f + aw * 0.6f);
            var pivot = c;
            var m0 = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f, pivot);
            GUI.color = Markings.Cyan;
            GUI.DrawTexture(new Rect(c.x - aw * 0.5f, c.y - aw * 0.5f, aw, aw), tri);
            GUI.matrix = m0;
            GUI.color = Color.white;
            return true;
        }

        // A small triangle in `fill` (the team colour) pointing down at `screen` (GUI coordinates: y down), with a dark
        // edge so it reads on grass and on jerseys. `dim` fades it (phone asleep, or a bot is playing for them).
        public static void Pointer(Vector2 screen, Color fill, bool dim, float s, bool flash = false)
        {
            Init();
            float w = 22 * s, a = dim ? 0.45f : 1f;
            // FIND ME: the outline goes white on every other 8 fps step (a small highlight, nothing over the play)
            GUI.color = flash ? Color.white : new Color(0.06f, 0.06f, 0.08f, 0.9f * a);
            GUI.DrawTexture(new Rect(screen.x - w * 0.5f - 3 * s, screen.y - w - 3 * s, w + 6 * s, w + 6 * s), tri);
            GUI.color = new Color(fill.r, fill.g, fill.b, a);
            GUI.DrawTexture(new Rect(screen.x - w * 0.5f, screen.y - w, w, w), tri);
            GUI.color = Color.white;
        }

        // A round badge with a painted receiver shape: 0 triangle, 1 circle, 2 square, 3 diamond, 4 star (the phone's
        // receiver buttons show the same shapes, so the QB knows who is who).
        static Texture2D[] shapes;
        public static void ShapeBadge(Vector2 screen, int shape, float s)
        {
            Init();
            if (shapes == null)
            {
                shapes = new Texture2D[5];
                Color ink = new Color(0.08f, 0.08f, 0.1f);
                shapes[0] = Shape(64, 64, p => p.Fill(new[] { new Vector2(32, 54), new Vector2(54, 14), new Vector2(10, 14) }, ink));
                var ring = new Vector2[33];
                for (int i = 0; i <= 32; i++) { float a = i * Mathf.PI * 2f / 32f; ring[i] = new Vector2(32 + Mathf.Cos(a) * 18, 32 + Mathf.Sin(a) * 18); }
                shapes[1] = Shape(64, 64, p => p.Stroke(ring, 9, ink));
                shapes[2] = Shape(64, 64, p => p.Fill(new[] { new Vector2(13, 13), new Vector2(51, 13), new Vector2(51, 51), new Vector2(13, 51) }, ink));
                shapes[3] = Shape(64, 64, p => p.Fill(new[] { new Vector2(32, 58), new Vector2(56, 32), new Vector2(32, 6), new Vector2(8, 32) }, ink));
                var star = new System.Collections.Generic.List<Vector2>();
                for (int i = 0; i < 10; i++) { float a = (90 + i * 36) * Mathf.Deg2Rad, r = i % 2 == 0 ? 26 : 11; star.Add(new Vector2(32 + Mathf.Cos(a) * r, 32 + Mathf.Sin(a) * r)); }
                shapes[4] = Shape(64, 64, p => p.Fill(star, ink));
            }
            float d = 40 * s;
            var rr = new Rect(screen.x - d * 0.5f, screen.y - d, d, d);
            GUI.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            GUI.DrawTexture(new Rect(rr.x - 3 * s, rr.y - 3 * s, rr.width + 6 * s, rr.height + 6 * s), disc);
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.DrawTexture(rr, disc);
            GUI.color = Color.white;
            if (shape >= 0 && shape < shapes.Length) GUI.DrawTexture(new Rect(rr.x + d * 0.2f, rr.y + d * 0.2f, d * 0.6f, d * 0.6f), shapes[shape]);
        }

        // A round badge with a shape letter (△ ○ □ ◇ ☆ in the game; any short text).
        public static void Badge(Vector2 screen, string label, Color fill, float s)
        {
            Init();
            float d = 40 * s;
            var r = new Rect(screen.x - d * 0.5f, screen.y - d, d, d);
            GUI.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            GUI.DrawTexture(new Rect(r.x - 3 * s, r.y - 3 * s, r.width + 6 * s, r.height + 6 * s), disc);
            GUI.color = fill;
            GUI.DrawTexture(r, disc);
            GUI.color = Color.white;
            Label(r, label, Mathf.RoundToInt(24 * s), new Color(0.08f, 0.08f, 0.1f));
        }
    }
}
