using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The stadium round the field (concept 04): on the far side a sideline with each team's players, coaches, refs,
    // tents, benches and coolers, a low wall with team banners, then stands full of a small team-coloured crowd; on the
    // near side the backs of the bench players at the bottom of the picture. Orange down markers on both sidelines.
    // Built once from code, seeded (the same crowd every run).
    public sealed class Stadium
    {
        readonly GameConfig cfg;
        readonly Material mat;
        readonly Transform root;
        readonly List<Transform> markers = new List<Transform>();
        uint seed = 12345;
        // people on the far sideline are drawn smaller than the players (concept 04: a dense row under the crowd)
        const float SidelineScale = 0.6f;

        float Rand() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xffffff) / 16777216f; }

        public Stadium(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            mat = Looks.Toon(baseMat);
            root = new GameObject("Stadium").transform;
            float m = cfg.field.yardM;
            float side = (cfg.field.HalfWidth + cfg.field.sidelineBorderYd) * m;   // where the white border ends (m)
            Stands(side);
            Sidelines(side);
            for (int i = 0; i < 4; i++) markers.Add(DownMarker());
        }

        // ---- the stands: a wall with banners, then rising rows of seated fans ----
        void Stands(float side)
        {
            float wallZ = side + 5.6f, x0 = -95f, x1 = 95f;
            var b = new Blocks();
            Color wall = Colors.Hex("#2f6b47"), concrete = Colors.Hex("#c9cec9"), seat = Colors.Hex("#aab3ae");
            b.Box(new Vector3(0, 0.75f, wallZ), new Vector3((x1 - x0) * 0.5f, 0.75f, 0.25f), wall);
            int rows = 26;
            var crowd = new Blocks { Flag = Looks.Gloss };
            Color[] shirts = { Colors.Hex(cfg.teams[1].color), Colors.Hex(cfg.teams[1].color), Colors.Hex("#2a7a45"), Colors.Hex("#1f6b3c"), Colors.Hex(cfg.teams[1].color), Colors.Hex("#f3f1ea"), Colors.Hex(cfg.teams[0].color) };
            Color[] skins = { Colors.Hex("#f2c8a5"), Colors.Hex("#d6a07a"), Colors.Hex("#a8704a"), Colors.Hex("#7a4b2e") };
            for (int r = 0; r < rows; r++)
            {
                float z = wallZ + 1.2f + r * 1.15f, y = 1.6f + r * 0.95f;
                b.Box(new Vector3(0, y * 0.5f, z), new Vector3((x1 - x0) * 0.5f, y * 0.5f, 0.6f), r % 2 == 0 ? concrete : seat);
                for (float x = x0 + 0.3f + Rand() * 0.3f; x < x1; x += 0.5f + Rand() * 0.12f)
                {
                    if (Rand() < 0.06f) continue;   // an empty seat here and there
                    Color shirt = shirts[(int)(Rand() * shirts.Length) % shirts.Length];
                    float h = 0.62f + Rand() * 0.1f;
                    crowd.Box(new Vector3(x, y + h * 0.45f, z - 0.15f), new Vector3(0.2f, h * 0.45f, 0.15f), shirt);
                    crowd.Box(new Vector3(x, y + h * 0.9f + 0.12f, z - 0.15f), new Vector3(0.11f, 0.12f, 0.11f), skins[(int)(Rand() * skins.Length) % skins.Length]);
                    if (Rand() < 0.18f) crowd.Box(new Vector3(x + 0.18f, y + h + 0.32f, z - 0.2f), new Vector3(0.05f, 0.28f, 0.05f), shirt);   // an arm up
                }
            }
            // a roof edge over the top rows
            b.Box(new Vector3(0, 1.6f + rows * 0.95f + 2.5f, wallZ + rows * 1.15f - 2f), new Vector3((x1 - x0) * 0.5f, 0.6f, 4f), Colors.Hex("#d9ddd8"));
            b.Build(root, "Stands", mat);
            crowd.Build(root, "Crowd", mat, false);

            // banners on the wall: team colour panels with the team mark
            for (int i = 0; i < 12; i++)
            {
                var team = cfg.teams[i % 2 == 0 ? 1 : 0];
                float x = -80f + i * 14.5f;
                var panel = new Blocks();
                panel.Box(new Vector3(x, 0.8f, wallZ - 0.3f), new Vector3(3.2f, 0.62f, 0.05f), Colors.Hex(team.color));
                panel.Build(root, "Banner", mat, false);
                var mark = new Material(mat) { name = "Banner " + team.@short };
                mark.SetTexture("_MainTex", TeamMarks.For(team));
                mark.SetColor("_PlayerColor", Colors.Hex(team.color));
                var q = Looks.Flat(root, "BannerMark", new Vector3(x, 0.8f, wallZ - 0.37f), new Vector2(1.1f, 1.1f), Color.white, mark);
                q.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            }
        }

        // ---- the sidelines: teams, coaches, refs, tents, benches, coolers ----
        void Sidelines(float side)
        {
            var props = new Blocks();
            // far side: home team (index 1) on the left half, visitors on the right
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                var team = cfg.teams[t];
                float sx = t == 1 ? -1f : 1f;
                // tents and a bench behind the players
                Tent(props, new Vector3(sx * 44f, 0, side + 3.4f), Colors.Hex(team.color));
                Tent(props, new Vector3(sx * 30f, 0, side + 4.2f), Colors.Hex(team.color));
                props.Box(new Vector3(sx * 22f, 0.25f, side + 4.4f), new Vector3(9f, 0.25f, 0.35f), Colors.Hex("#4a4f55"));
                Cooler(props, new Vector3(sx * 8f, 0, side + 4.2f));
                Cooler(props, new Vector3(sx * 36f, 0, side + 4.2f));
                // players standing along the far sideline
                for (int i = 0; i < 20; i++)
                {
                    float x = sx * (5f + i * 2.1f + Rand() * 1.2f), z = side + 1.8f + Rand() * 2.4f;
                    int number = 10 + (int)(Rand() * 80);
                    var f = FootballFigure.Create(root, team, number, Rand() < 0.3f ? FootballFigure.Build.Big : FootballFigure.Build.Mid, (int)(Rand() * 999), cfg.view.playerHeightM * SidelineScale, mat, TeamMarks.For(team));
                    f.transform.localPosition = new Vector3(x, 0, z);
                    f.transform.localRotation = Quaternion.Euler(0, 90f + (Rand() - 0.5f) * 40f, 0);
                    f.Pose(FootballFigure.PoseKind.Stand, 0);
                }
                // coaches in team polos
                for (int i = 0; i < 5; i++) Person(new Vector3(sx * (8f + i * 7f + Rand() * 2f), 0, side + 1.2f + Rand() * 0.8f), i % 2 == 0 ? Colors.Hex(team.color) : Colors.Hex("#f3f1ea"), Colors.Hex("#2c2f36"), false, 90f + (Rand() - 0.5f) * 40f);
                // the near side: bench players' backs at the bottom of the picture
                for (int i = 0; i < 16; i++)
                {
                    float x = sx * (2f + i * 2.6f + Rand() * 1.0f), z = -side - 6.5f - Rand() * 1.4f;
                    var f = FootballFigure.Create(root, team, 10 + (int)(Rand() * 80), Rand() < 0.35f ? FootballFigure.Build.Big : FootballFigure.Build.Mid, (int)(Rand() * 999), cfg.view.playerHeightM * 1.15f, mat, TeamMarks.For(team));
                    f.transform.localPosition = new Vector3(x, 0, z);
                    f.transform.localRotation = Quaternion.Euler(0, -90f + (Rand() - 0.5f) * 50f, 0);
                    f.Pose(FootballFigure.PoseKind.Stand, 0);
                }
                Cooler(props, new Vector3(sx * 30f, 0, -side - 2.5f));
            }
            // referees on both sidelines
            foreach (float x in new[] { -24f, 2f, 26f }) Person(new Vector3(x, 0, side + 0.8f), Color.white, Colors.Hex("#1e1e22"), true, 90f);
            foreach (float x in new[] { -30f, 14f }) Person(new Vector3(x, 0, -side - 1.2f), Color.white, Colors.Hex("#1e1e22"), true, -90f);
            props.Build(root, "Sideline", mat);
        }

        void Tent(Blocks b, Vector3 at, Color skirt)
        {
            float w = 2.6f, d = 2.2f, h = 2.3f, peak = 3.3f;
            foreach (var c in new[] { new Vector3(-w, 0, -d), new Vector3(w, 0, -d), new Vector3(-w, 0, d), new Vector3(w, 0, d) })
                b.Box(at + c + Vector3.up * h * 0.5f, new Vector3(0.05f, h * 0.5f, 0.05f), Colors.Hex("#c9ccd0"));
            Color canvas = Colors.Hex("#f4f4f2");
            Vector3 top = at + Vector3.up * peak;
            Vector3 a = at + new Vector3(-w, h, -d), bb = at + new Vector3(w, h, -d), c2 = at + new Vector3(w, h, d), d2 = at + new Vector3(-w, h, d);
            b.Quad(a, top, top, bb, canvas, Vector3.back + Vector3.up);
            b.Quad(bb, top, top, c2, canvas, Vector3.right + Vector3.up);
            b.Quad(c2, top, top, d2, canvas, Vector3.forward + Vector3.up);
            b.Quad(d2, top, top, a, canvas, Vector3.left + Vector3.up);
            b.Box(at + new Vector3(0, h - 0.2f, -d), new Vector3(w, 0.2f, 0.03f), skirt);
        }

        void Cooler(Blocks b, Vector3 at)
        {
            b.Box(at + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.45f, 0.4f), Colors.Hex("#55595f"));          // table
            b.Box(at + new Vector3(-0.4f, 1.15f, 0), new Vector3(0.3f, 0.25f, 0.3f), Colors.Hex("#f08a24"));       // jugs
            b.Box(at + new Vector3(0.4f, 1.15f, 0), new Vector3(0.3f, 0.25f, 0.3f), Colors.Hex("#f08a24"));
            b.Box(at + new Vector3(-0.4f, 1.43f, 0), new Vector3(0.31f, 0.04f, 0.31f), Colors.Hex("#f6f6f2"));
            b.Box(at + new Vector3(0.4f, 1.43f, 0), new Vector3(0.31f, 0.04f, 0.31f), Colors.Hex("#f6f6f2"));
        }

        // A plain person (a coach or a ref): a cap, a head, a shirt (stripes for a ref), trousers. Static mesh.
        void Person(Vector3 at, Color shirt, Color trousers, bool stripes, float yaw)
        {
            var k = new FigureKit();
            Color skin = Colors.Hex(Rand() < 0.5f ? "#e9b892" : "#a8704a");
            k.Limb(new Vector3(0, 8, 9), new Vector3(0, 80, 9), 8f, 9f, trousers);
            k.Limb(new Vector3(0, 8, -9), new Vector3(0, 80, -9), 8f, 9f, trousers);
            k.Ellipsoid(new Vector3(3, 4, 9), new Vector3(11, 5, 6.5f), Colors.Hex("#1e1e22"));
            k.Ellipsoid(new Vector3(3, 4, -9), new Vector3(11, 5, 6.5f), Colors.Hex("#1e1e22"));
            var body = new FigureKit.Profile(Vector3.zero, new[] { 78f, 15f, 19f, 100f, 16f, 21f, 122f, 15f, 20f, 132f, 6f, 6f });
            k.Lathe(body, shirt, 22);
            if (stripes)
                for (int i = -3; i <= 3; i++)
                {
                    var s = FigureKit.OnLathe(body, i * 50f);
                    k.Stroke(s, new[] { new Vector2(0, 80), new Vector2(0, 128) }, new[] { 4f, 4f }, 0.4f, Colors.Hex("#1e1e22"), 2);
                }
            foreach (float s in new[] { 1f, -1f })
            {
                k.Limb(new Vector3(0, 124, s * 20), new Vector3(4, 84, s * 23), 6.5f, 5.5f, shirt);
                k.Ball(new Vector3(5, 80, s * 23), 5.5f, skin);
            }
            k.Ellipsoid(new Vector3(2, 150, 0), new Vector3(15, 17, 15), skin, 10, 16);
            k.Ellipsoid(new Vector3(0, 160, 0), new Vector3(15.5f, 9f, 15.5f), stripes ? Color.white : shirt, 8, 16);   // cap
            k.Ellipsoid(new Vector3(14, 157, 0), new Vector3(9, 1.6f, 11), stripes ? Color.white : shirt, 4, 12);       // brim
            var go = Looks.Static(root, stripes ? "Ref" : "Coach", k, mat);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * cfg.view.playerHeightM * (at.z > 0 ? SidelineScale : 0.95f) / 170f;
        }

        // An orange down marker: a pole with a box on top.
        Transform DownMarker()
        {
            var b = new Blocks();
            b.Box(new Vector3(0, 1.1f, 0), new Vector3(0.07f, 1.1f, 0.07f), Colors.Hex("#f07a1a"));
            var go = b.Build(root, "DownMarker", mat);
            var k = new FigureKit();
            k.Ellipsoid(new Vector3(0, 2.4f, 0), new Vector3(0.36f, 0.36f, 0.1f), Quaternion.identity, Colors.Hex("#f07a1a"), 8, 16);
            k.Ellipsoid(new Vector3(0, 2.4f, -0.05f), new Vector3(0.2f, 0.2f, 0.06f), Quaternion.identity, Colors.Hex("#1e1e22"), 6, 12);
            Looks.Static(go.transform, "Flag", k, mat);
            return go.transform;
        }

        // The markers stand at the line of scrimmage and the first-down line, on both sidelines (yards).
        public void SetMarkers(float losX, float firstX)
        {
            float m = cfg.field.yardM;
            float z = (cfg.field.HalfWidth + cfg.field.sidelineBorderYd) * m + 1.2f;   // on the sideline, past the white border
            markers[0].localPosition = new Vector3(losX * m, 0, z);
            markers[1].localPosition = new Vector3(firstX * m, 0, z);
            markers[2].localPosition = new Vector3(losX * m, 0, -z);
            markers[3].localPosition = new Vector3(firstX * m, 0, -z);
        }
    }
}
