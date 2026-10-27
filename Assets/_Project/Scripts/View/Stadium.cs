using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The stadium round the field (concept 04, WS-10). Far side: each team's players, staff and refs on the sideline,
    // small tents against the wall, benches, coolers and ice chests, the wall with team banners, then the stands full
    // of a card crowd that waves. Near side: the home bench row seen from behind at the bottom of the picture (players
    // with their numbers, staff in caps, a ref, coolers, tents behind). Chunky goal posts on both end lines.
    // Orange down markers on both sidelines. FEATURES.cheerleaders (NO_CHEERLEADERS=1): each team's cheer squad in
    // front of its half of the far sideline, Kenny's rigged cheerleader in the team's colours, cheering together in
    // stepped poses. FEATURES.refModel (NO_REF_MODEL=1): the sideline refs wear Kenny's rigged referee. Every number is
    // in game.json "stadium". Built once from code, seeded (the same stadium every run).
    //
    // The crowd: every fan is a flat card (shirt, head, cap, arms, sometimes a sign). The stands are cut into slices;
    // each slice is built in four looks (two idle looks, half up, standing with arms up) and only swaps its mesh on
    // stadium.fps key frames, so the crowd moves in steps like the players' keys (no sine-of-time, no wobble). A wave
    // of standing fans runs along the stands now and then; between waves the slices flip between their idle looks.
    public sealed class Stadium
    {
        public const int IdleA = 0, IdleB = 1, Rising = 2, Up = 3, Poses = 4;

        readonly GameConfig cfg;
        readonly StadiumConfig sc;
        readonly Material mat;
        readonly Transform root;
        readonly List<Transform> markers = new List<Transform>();
        readonly List<CrowdSlice> slices = new List<CrowdSlice>();
        readonly Bowl bowl;   // FEATURES.stadiumBowl: the full bowl round the field (Bowl.cs), else null
        int lastKey = int.MinValue;
        uint seed = 12345;

        sealed class CrowdSlice { public MeshFilter filter; public Mesh[] poses; public int pose, index; }

        readonly List<(FootballFigure fig, int team)> cheerleaders = new List<(FootballFigure, int)>();
        int lastCheerKey = int.MinValue;
        public int CheerleaderCount => cheerleaders.Count;

        float Rand() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xffffff) / 16777216f; }
        T Pick<T>(T[] a) => a[(int)(Rand() * a.Length) % a.Length];

        public Stadium(GameConfig cfg, Material baseMat)
        {
            this.cfg = cfg;
            sc = cfg.stadium;
            mat = Looks.Toon(baseMat);
            root = new GameObject("Stadium").transform;
            float m = cfg.field.yardM;
            float side = (cfg.field.HalfWidth + cfg.field.sidelineBorderYd) * m;   // where the white border ends (m)
            if (cfg.Feature("stadiumBowl"))
            {
                bowl = new Bowl(cfg, root, mat, Looks.Glow(null));
                bowl.Build();
                WallBanners(side + sc.wallBackM, bowl.Path.A - bowl.Path.R);
                BowlCrowd();
            }
            else
            {
                Stands(side + sc.wallBackM);
                Crowd(side + sc.wallBackM);
            }
            FarSideline(side);
            NearSideline(side);
            if (cfg.Feature("cheerleaders")) Cheerleaders(side);
            GoalPosts();
            for (int i = 0; i < 4; i++) markers.Add(DownMarker(i < 2 ? 1f : -1f));
            root.gameObject.AddComponent<StadiumClock>().Tick = Tick;
        }

        // ---- the stands: a wall with banners, rising rows of seats, stair aisles, a roof edge ----
        float RowY(int r) => sc.firstRowM + r * sc.rowRiseM;
        float RowZ(float wallZ, int r) => wallZ + sc.rowBackM + r * sc.rowDepthM;
        bool InAisle(float x) => sc.aisleEveryM > 0f && Mathf.Repeat(x + sc.halfLengthM, sc.aisleEveryM) < sc.aisleWidthM;

        void Stands(float wallZ)
        {
            float hx = sc.halfLengthM;
            var b = new Blocks();
            Color concrete = Colors.Hex(sc.concreteColor), seat = Colors.Hex(sc.seatColor), aisle = Colors.Hex(sc.aisleColor);
            b.Box(new Vector3(0, sc.wallHeightM * 0.5f, wallZ), new Vector3(hx, sc.wallHeightM * 0.5f, 0.25f), Colors.Hex(sc.wallColor));
            for (int r = 0; r < sc.rows; r++)
            {
                float y = RowY(r), z = RowZ(wallZ, r);
                b.Box(new Vector3(0, y * 0.5f, z), new Vector3(hx, y * 0.5f, sc.rowDepthM * 0.5f + 0.02f), r % 2 == 0 ? concrete : seat);
                if (sc.aisleEveryM > 0f)
                    for (float x = -hx + sc.aisleEveryM; x < hx; x += sc.aisleEveryM)   // the stairs, a touch proud of the seats
                        b.Box(new Vector3(x + sc.aisleWidthM * 0.5f, y * 0.5f + 0.02f, z - 0.02f), new Vector3(sc.aisleWidthM * 0.5f, y * 0.5f, sc.rowDepthM * 0.5f + 0.03f), aisle);
            }
            b.Box(new Vector3(0, RowY(sc.rows) + 2.5f, RowZ(wallZ, sc.rows) - 2f), new Vector3(hx, 0.6f, 4f), Colors.Hex(sc.roofColor));
            b.Build(root, "Stands", mat);
            WallBanners(wallZ, hx);
        }

        // Banners on the far wall from -hx to +hx: team colour panels with the team mark, as far apart as the old
        // full-length stand had them.
        void WallBanners(float wallZ, float hx)
        {
            float gap = sc.banners > 0 ? sc.halfLengthM * 2f / sc.banners : 0f;
            int count = gap > 0f ? Mathf.Min(sc.banners, Mathf.FloorToInt(hx * 2f / gap)) : 0;
            if (count > 0) gap = hx * 2f / count;
            for (int i = 0; i < count; i++)
            {
                var team = cfg.teams[i % 2 == 0 ? 1 : 0];
                float x = -hx + (i + 0.5f) * gap;
                float y = sc.wallHeightM * 0.55f;
                var panel = new Blocks();
                panel.Box(new Vector3(x, y, wallZ - 0.3f), new Vector3(3.2f, sc.wallHeightM * 0.41f, 0.05f), Colors.Hex(team.color));
                panel.Build(root, "Banner", mat, false);
                var mark = new Material(mat) { name = "Banner " + team.@short };
                mark.SetTexture("_MainTex", TeamMarks.For(team));
                mark.SetColor("_PlayerColor", Colors.Hex(team.color));
                var q = Looks.Flat(root, "BannerMark", new Vector3(x, y, wallZ - 0.37f), new Vector2(1.1f, 1.1f), Color.white, mark);
                q.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            }
        }

        // ---- the card crowd ----
        void Crowd(float wallZ)
        {
            float x0 = -sc.halfLengthM, x1 = sc.halfLengthM;
            int count = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / Mathf.Max(0.1f, sc.sliceM)));
            var cards = new Cards[count * Poses];
            for (int i = 0; i < cards.Length; i++) cards[i] = new Cards();

            Color home = Colors.Hex(cfg.teams[1].color), away = Colors.Hex(cfg.teams[0].color);
            Color[] homes = { home, home, Color.Lerp(home, Color.black, 0.25f), Colors.Hex(cfg.teams[1].color2) };
            Color[] aways = { away, away, Colors.Hex(cfg.teams[0].color2) };
            Color[] neutrals = Hexes(sc.neutralColors);
            Color[] skins = { Colors.Hex("#f2c8a5"), Colors.Hex("#d6a07a"), Colors.Hex("#a8704a"), Colors.Hex("#7a4b2e") };
            Color[] signs = { Color.white, Colors.Hex(cfg.teams[1].color2), home, away };
            Color trousers = Colors.Hex("#2c2f36");

            for (int r = 0; r < sc.rows; r++)
            {
                float y = RowY(r), z = RowZ(wallZ, r) - 0.15f;
                for (float x = x0 + Rand() * sc.seatM; x < x1; x += sc.seatM + Rand() * sc.seatJitterM)
                {
                    if (InAisle(x) || Rand() < sc.emptyChance) continue;
                    float u = Rand();
                    Color shirt = u < sc.homeShare ? Pick(homes) : u < sc.homeShare + sc.awayShare ? Pick(aways) : Pick(neutrals);
                    Color skin = Pick(skins);
                    Color? cap = Rand() < sc.capChance ? (Color?)(Rand() < 0.5f ? shirt : Pick(homes)) : null;
                    Color? sign = Rand() < sc.signChance ? (Color?)Pick(signs) : null;
                    float h = sc.fanM * (0.9f + Rand() * 0.2f);
                    int idleA = IdleArms(), idleB = IdleArms();
                    if (sign.HasValue) idleA = idleB = 2;   // a sign stays up
                    int slice = Mathf.Clamp((int)((x - x0) / sc.sliceM), 0, count - 1);
                    var at = new Vector3(x, y, z);
                    for (int p = 0; p < Poses; p++)
                    {
                        int arms = p == IdleA ? idleA : p == IdleB ? idleB : p == Rising ? 1 : 2;
                        float stand = p == Up ? sc.standM : p == Rising ? sc.standM * 0.5f : 0f;
                        Fan(cards[slice * Poses + p], at, Vector3.right, Vector3.back, h, stand, arms, shirt, skin, trousers, cap, sign);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Crowd" + i);
                go.transform.SetParent(root, false);
                var s = new CrowdSlice { poses = new Mesh[Poses], pose = IdleA, index = i };
                for (int p = 0; p < Poses; p++) s.poses[p] = cards[i * Poses + p].Build("Crowd" + i + "." + p);
                s.filter = go.AddComponent<MeshFilter>();
                s.filter.sharedMesh = s.poses[IdleA];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                slices.Add(s);
            }
        }

        // ---- the card crowd in the bowl (FEATURES.stadiumBowl): every row of both tiers, all the way round ----
        // Slices are cut along the wall line (sliceM each), straight up through both tiers, so the wave runs right
        // round the stadium. The lower tier keeps all four looks; round the ends, the corners and the near side its
        // two idle looks are one mesh, and the upper tier only sits or stands (less to keep in memory).
        void BowlCrowd()
        {
            var path = bowl.Path;
            int count = Mathf.Max(1, Mathf.CeilToInt(path.Length / Mathf.Max(0.1f, sc.sliceM)));
            var lower = new Cards[count * Poses];
            var upper = new Cards[count * 2];
            for (int i = 0; i < lower.Length; i++) lower[i] = new Cards();
            for (int i = 0; i < upper.Length; i++) upper[i] = new Cards();

            Color home = Colors.Hex(cfg.teams[1].color), away = Colors.Hex(cfg.teams[0].color);
            Color[] homes = { home, home, Color.Lerp(home, Color.black, 0.25f), Colors.Hex(cfg.teams[1].color2) };
            Color[] aways = { away, away, Colors.Hex(cfg.teams[0].color2) };
            Color[] neutrals = Hexes(sc.neutralColors);
            Color[] skins = { Colors.Hex("#f2c8a5"), Colors.Hex("#d6a07a"), Colors.Hex("#a8704a"), Colors.Hex("#7a4b2e") };
            Color[] signs = { Color.white, Colors.Hex(cfg.teams[1].color2), home, away };
            Color trousers = Colors.Hex("#2c2f36");

            int rows = sc.rows + sc.bowl.upperRows;
            for (int r = 0; r < rows; r++)
            {
                bool up = r >= sc.rows;
                int ur = r - sc.rows;
                float d = (up ? bowl.UpperD(ur) : bowl.LowerD(r)) - 0.15f, y = up ? bowl.UpperY(ur) : bowl.LowerY(r);
                float seat = up ? sc.bowl.upperSeatM : sc.seatM, empty = up ? sc.bowl.upperEmptyChance : sc.emptyChance;
                for (float t = Rand() * seat; t < path.Length; t += (seat + Rand() * sc.seatJitterM) / path.Stretch(t, d))
                {
                    if (bowl.InAisle(t, d) || (!up && bowl.InPortal(t, r)) || Rand() < empty) continue;
                    float u = Rand();
                    Color shirt = u < sc.homeShare ? Pick(homes) : u < sc.homeShare + sc.awayShare ? Pick(aways) : Pick(neutrals);
                    Color skin = Pick(skins);
                    Color? cap = Rand() < sc.capChance ? (Color?)(Rand() < 0.5f ? shirt : Pick(homes)) : null;
                    Color? sign = !up && Rand() < sc.signChance ? (Color?)Pick(signs) : null;
                    float h = sc.fanM * (0.9f + Rand() * 0.2f);
                    int idleA = IdleArms(), idleB = IdleArms();
                    if (sign.HasValue) idleA = idleB = 2;   // a sign stays up
                    int slice = Mathf.Clamp((int)(t / sc.sliceM), 0, count - 1);
                    var n = path.Out(t);
                    var at = path.At(t, d) + Vector3.up * y;
                    var right = Vector3.Cross(Vector3.up, n);
                    if (up)
                    {
                        Fan(upper[slice * 2], at, right, -n, h, 0f, idleA == 2 ? 2 : 0, shirt, skin, trousers, cap, null, true);
                        Fan(upper[slice * 2 + 1], at, right, -n, h, sc.standM, 2, shirt, skin, trousers, cap, null, true);
                        continue;
                    }
                    for (int p = 0; p < Poses; p++)
                    {
                        int arms = p == IdleA ? idleA : p == IdleB ? idleB : p == Rising ? 1 : 2;
                        float stand = p == Up ? sc.standM : p == Rising ? sc.standM * 0.5f : 0f;
                        Fan(lower[slice * Poses + p], at, right, -n, h, stand, arms, shirt, skin, trousers, cap, sign);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                // the far side (the one the TV camera faces) keeps both idle looks
                bool far = path.Segment((i + 0.5f) * sc.sliceM, out _) == 0;
                var low = new Mesh[Poses];
                for (int p = 0; p < Poses; p++)
                    low[p] = p == IdleB && !far ? low[IdleA] : lower[i * Poses + p].Build("Crowd" + i + "." + p);
                var parent = bowl.IsNear((i + 0.5f) * sc.sliceM) ? bowl.NearRoot : root;
                AddSlice("Crowd" + i, low, parent);
                Mesh sit = upper[i * 2].Build("Upper" + i + ".0"), stood = upper[i * 2 + 1].Build("Upper" + i + ".1");
                AddSlice("Upper" + i, new[] { sit, sit, stood, stood }, parent);
            }
        }

        // One crowd slice: its looks (IdleA, IdleB, Rising, Up), the same index along the stands as its neighbours'.
        void AddSlice(string name, Mesh[] poses, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = new CrowdSlice { poses = poses, pose = IdleA, index = slices.Count / 2 };
            s.filter = go.AddComponent<MeshFilter>();
            s.filter.sharedMesh = s.poses[IdleA];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            slices.Add(s);
        }

        // The scoreboard over the end shows the score (only with the bowl).
        public void SetScore(int[] score) { bowl?.SetScore(score); }

        // Arms for an idle look: mostly down, now and then out or up.
        int IdleArms() { float u = Rand(); return u < sc.idleArmsChance ? 2 : u < sc.idleArmsChance * 2f ? 1 : 0; }

        // One fan as a flat card: legs when standing, shirt, head, cap, arms (0 down, 1 out, 2 up), a sign held up.
        // The card stands at `seat`, `right` along its row and `toward` the field (the camera's side). simple: no arms
        // hanging down (the upper tier, too far off to see them).
        void Fan(Cards c, Vector3 seat, Vector3 right, Vector3 toward, float h, float stand, int arms, Color shirt, Color skin, Color trousers, Color? cap, Color? sign, bool simple = false)
        {
            c.Right = right;
            c.Normal = (toward + Vector3.up * sc.cardTiltUp).normalized;   // facing the field, shaded as if leaning back
            Vector3 P(float x, float y, float z) => seat + right * x + Vector3.up * (y - seat.y) + toward * z;
            float hip = seat.y + stand, torso = h * 0.56f, headR = h * 0.14f;
            if (stand > 0f) c.Rect(P(0, seat.y + stand * 0.5f, -0.01f), h * 0.13f, stand * 0.5f, trousers);
            c.Rect(P(0, hip + torso * 0.5f, 0), h * 0.19f, torso * 0.5f, shirt);
            float headY = hip + torso + headR * 0.9f;
            c.Rect(P(0, headY, 0.01f), headR * 0.85f, headR, skin);
            if (cap.HasValue) c.Rect(P(0, headY + headR * 0.75f, 0.02f), headR * 0.95f, headR * 0.35f, cap.Value);
            float armL = h * 0.36f, armW = h * 0.055f;
            if (!(simple && arms == 0))
                for (int s = -1; s <= 1; s += 2)
                {
                    var dir = arms == 0 ? new Vector2(s * 0.12f, -1f) : arms == 1 ? new Vector2(s * 0.75f, 0.66f) : new Vector2(s * 0.22f, 1f);
                    dir.Normalize();
                    var shoulder = P(s * h * 0.17f, hip + torso * 0.9f, 0.02f);
                    var mid = shoulder + (right * dir.x + Vector3.up * dir.y) * armL * 0.5f;
                    c.Rect(mid, armW, armL * 0.5f, arms == 0 ? shirt : skin, Mathf.Atan2(-dir.x, dir.y));
                }
            if (sign.HasValue && !simple) c.Rect(P(0, headY + headR + h * 0.2f, 0.03f), h * 0.34f, h * 0.2f, sign.Value);
        }

        static Color[] Hexes(List<string> hex)
        {
            if (hex == null || hex.Count == 0) return new[] { Color.white };
            var c = new Color[hex.Count];
            for (int i = 0; i < c.Length; i++) c[i] = Colors.Hex(hex[i]);
            return c;
        }

        // The key frame at this time (stepped at fps).
        public static int CrowdKey(float seconds, float fps) => fps > 0f ? Mathf.FloorToInt(seconds * fps) : 0;

        // Which look a crowd slice shows on a key frame: the wave (rising, up, sitting back down) when it passes,
        // otherwise one of the two idle looks, flipped every idleKeys keys at a different key for each slice.
        public static int SlicePose(StadiumConfig sc, int key, int slice)
        {
            if (sc.waveEveryS > 0f && sc.fps > 0f && sc.sliceM > 0f)
            {
                int period = Mathf.Max(1, Mathf.RoundToInt(sc.waveEveryS * sc.fps));
                int k = ((key % period) + period) % period;
                float step = sc.waveSpeedMS / sc.fps / sc.sliceM;   // slices the front moves per key
                float d = k * step - slice, w = Mathf.Max(1f, sc.waveWidthM / sc.sliceM), r = Mathf.Max(1f, step);
                if (d >= 0f && d < w + 2f * r) return d < r || d >= w + r ? Rising : Up;   // rise, stand, sit back down
            }
            uint h = Mix((uint)slice * 2654435761u ^ (uint)((key + slice * 7) / Mathf.Max(1, sc.idleKeys)));
            return (h & 1u) == 0u ? IdleA : IdleB;
        }

        static uint Mix(uint h) { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; return h; }

        // Called every frame (StadiumClock); swaps slice meshes only when the key frame changes.
        void Tick(float seconds)
        {
            int ck = CrowdKey(seconds, sc.cheerFps);
            if (ck != lastCheerKey)
            {
                lastCheerKey = ck;
                foreach (var c in cheerleaders)
                    if (c.fig != null) c.fig.Pose(FootballFigure.PoseKind.Cheer, CheerFrame(sc, ck, c.team));
            }
            int key = CrowdKey(seconds, sc.fps);
            if (key == lastKey) return;
            lastKey = key;
            for (int i = 0; i < slices.Count; i++)
            {
                int p = SlicePose(sc, key, slices[i].index);
                if (p == slices[i].pose) continue;
                slices[i].pose = p;
                slices[i].filter.sharedMesh = slices[i].poses[p];
            }
        }

        // ---- the far sideline: each team's players and staff, refs, tents, benches, coolers ----
        void FarSideline(float side)
        {
            var props = new Blocks();
            float wallZ = side + sc.wallBackM, s = sc.farScale;
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                var team = cfg.teams[t];
                float sx = t == 1 ? -1f : 1f;   // home (index 1) on the left half, visitors on the right
                for (int i = 0; i < sc.tentsPerTeam; i++)
                    Tent(props, new Vector3(sx * (sc.tentFirstXM + i * sc.tentGapM), 0, wallZ - sc.tentFromWallM), Colors.Hex(team.color), sc.tentScale);
                float benchZ = side + sc.farRowM + sc.farDepthM + 0.4f;
                props.Box(new Vector3(sx * sc.benchXM, 0.45f * s, benchZ), new Vector3(sc.benchHalfM, 0.45f * s, 0.35f * s), Colors.Hex("#4a4f55"));
                for (int i = 0; i < sc.coolerXM.Count; i++)
                {
                    var at = new Vector3(sx * sc.coolerXM[i], 0, benchZ + 0.6f);
                    if (i % 2 == 0) Cooler(props, at, s); else IceChest(props, at, s);
                }
                props.Box(new Vector3(sx * (sc.benchXM + sc.benchHalfM + 1.2f), 0.45f * s, benchZ), new Vector3(0.25f * s, 0.45f * s, 0.25f * s), Colors.Hex("#c8372d"));   // a bin
                // the team's players stand in a crowd near the 50, staff all along
                int n = Mathf.Max(1, sc.farPlayersPerTeam);
                for (int i = 0; i < sc.farPlayersPerTeam; i++)
                    Player(team, sx * (sc.farFromCentreM + (i + Rand()) * sc.farSpreadM / n), side + sc.farRowM + Rand() * sc.farDepthM, s, 90f + (Rand() - 0.5f) * 40f);
                int ns = Mathf.Max(1, sc.farStaffPerTeam);
                for (int i = 0; i < sc.farStaffPerTeam; i++)
                    Staff(team, new Vector3(sx * ((i + Rand()) * sc.staffSpreadM / ns), 0, side + sc.farRowM * 0.6f + Rand() * sc.farDepthM), 90f + (Rand() - 0.5f) * 40f, s);
            }
            foreach (float x in sc.farRefsXM) Referee(new Vector3(x, 0, side + 0.8f), 90f, s);
            props.Build(root, "FarSideline", mat);
        }

        // ---- the cheerleaders: each team's squad in a row at the front of the far sideline, facing the field ----
        void Cheerleaders(float side)
        {
            var kit = FootballFigure.KitMaterial(mat, cfg.view);
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                var team = cfg.teams[t];
                float sx = t == 1 ? -1f : 1f;   // in front of the team's own half, like its players (home on the left)
                for (int i = 0; i < sc.cheerPerTeam; i++)
                {
                    var f = FootballFigure.Create(root, "Cheerleader " + team.@short + " " + (i + 1), cfg.view.playerHeightM * sc.cheerScale);
                    f.transform.localPosition = new Vector3(sx * (sc.cheerFromCentreM + i * sc.cheerGapM), 0, side + sc.cheerRowM);
                    f.transform.localRotation = Quaternion.Euler(0, 90f, 0);   // facing the field (and the camera)
                    if (ModelSkin.Wear(f, team.cheerModel, team.cheerTexture, kit, false) == null)
                    {
                        // no model: no cheerleader
                        if (Application.isPlaying) Object.Destroy(f.gameObject); else Object.DestroyImmediate(f.gameObject);
                        continue;
                    }
                    f.Pose(FootballFigure.PoseKind.Cheer, CheerFrame(sc, 0, t));
                    cheerleaders.Add((f, t));
                }
            }
        }

        // The cheer pose (FootballFigure.PoseKind.Cheer frame) a team's squad shows on a cheer key: the routine in step,
        // the second squad cheerTeamOffset keys behind the first.
        public static int CheerFrame(StadiumConfig sc, int key, int team)
        {
            int n = sc.cheerRoutine == null ? 0 : sc.cheerRoutine.Count;
            if (n == 0) return 0;
            int k = key - team * sc.cheerTeamOffset;
            return sc.cheerRoutine[((k % n) + n) % n];
        }

        // ---- the near sideline: the home bench row from behind ----
        void NearSideline(float side)
        {
            var props = new Blocks();
            var home = cfg.teams[1];
            float s = sc.nearScale, step = sc.nearHalfLengthM * 2f / Mathf.Max(1, sc.nearCount);
            for (int i = 0; i < sc.nearCount; i++)
            {
                float x = -sc.nearHalfLengthM + (i + 0.2f + Rand() * 0.6f) * step;
                float z = -side - sc.nearRowM - Rand() * sc.nearDepthM;
                float yaw = -90f + (Rand() - 0.5f) * 50f;
                float u = Rand();
                if (u < sc.nearPropShare)
                {
                    if (Rand() < 0.5f) Cooler(props, new Vector3(x, 0, z + 0.6f), s); else IceChest(props, new Vector3(x, 0, z + 0.6f), s);
                }
                else if (u < sc.nearPropShare + sc.nearStaffShare) Staff(home, new Vector3(x, 0, z), yaw, s);
                else Player(home, x, z, s, yaw);
            }
            foreach (float x in sc.nearRefsXM) Referee(new Vector3(x, 0, -side - sc.nearRowM + 0.6f), -90f, s);
            foreach (float x in sc.nearTentsXM)
                Tent(props, new Vector3(x, 0, -side - sc.nearRowM - sc.nearDepthM - 1f), Colors.Hex(home.color), sc.nearTentScale);
            props.Build(root, "NearSideline", mat);
        }

        // A player on the sideline: the team's model (ModelSkin), standing, baked still.
        void Player(TeamConfig team, float x, float z, float scale, float yaw)
        {
            var f = FootballFigure.Create(root, "Sideline player", cfg.view.playerHeightM * scale);
            f.transform.localPosition = new Vector3(x, 0, z);
            f.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (ModelSkin.Wear(f, team, null, cfg, FootballFigure.KitMaterial(mat, cfg.view))) ModelSkin.Freeze(f, FootballFigure.PoseKind.Stand);
            Rand(); Rand(); Rand();   // (what the code-built player drew: number, build, skin; the rest of the seeded layout stays put)
        }

        // A ref on the sideline. FEATURES.refModel (NO_REF_MODEL=1): Kenny's rigged referee (stadium.refModel), standing,
        // baked still; otherwise, or if the model is missing, the code-built person in stripes.
        void Referee(Vector3 at, float yaw, float scale)
        {
            if (cfg.Feature("refModel"))
            {
                var f = FootballFigure.Create(root, "Referee", cfg.view.playerHeightM * scale);
                f.transform.localPosition = at;
                f.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                if (ModelSkin.Wear(f, sc.refModel, sc.refTexture, FootballFigure.KitMaterial(mat, cfg.view), false) != null)
                {
                    ModelSkin.Freeze(f, FootballFigure.PoseKind.Stand);
                    Rand();   // (what the code-built ref drew: skin; the rest of the seeded layout stays put)
                    return;
                }
                if (Application.isPlaying) Object.Destroy(f.gameObject); else Object.DestroyImmediate(f.gameObject);
            }
            Person(at, Color.white, Colors.Hex("#1e1e22"), Color.white, true, yaw, scale);
        }

        // A coach or trainer: a team polo (or white), khaki or dark trousers, a team cap.
        void Staff(TeamConfig team, Vector3 at, float yaw, float scale)
        {
            Color tc = Colors.Hex(team.color);
            Color shirt = Rand() < 0.7f ? tc : Colors.Hex("#f3f1ea");
            Color trousers = Rand() < 0.5f ? Colors.Hex("#c9b48a") : Colors.Hex("#2c2f36");
            Color cap = Rand() < 0.75f ? tc : Colors.Hex("#2c2f36");
            Person(at, shirt, trousers, cap, false, yaw, scale);
        }

        // A small tent: four poles, a peaked white canopy, a team-coloured valance. s scales it.
        void Tent(Blocks b, Vector3 at, Color skirt, float s)
        {
            float w = 2.6f * s, d = 2.2f * s, h = 2.3f * s, peak = 3.3f * s;
            foreach (var c in new[] { new Vector3(-w, 0, -d), new Vector3(w, 0, -d), new Vector3(-w, 0, d), new Vector3(w, 0, d) })
                b.Box(at + c + Vector3.up * h * 0.5f, new Vector3(0.06f * s, h * 0.5f, 0.06f * s), Colors.Hex("#c9ccd0"));
            Color canvas = Colors.Hex("#f4f4f2");
            Vector3 top = at + Vector3.up * peak;
            Vector3 a = at + new Vector3(-w, h, -d), bb = at + new Vector3(w, h, -d), c2 = at + new Vector3(w, h, d), d2 = at + new Vector3(-w, h, d);
            b.Quad(a, top, top, bb, canvas, Vector3.back + Vector3.up);
            b.Quad(bb, top, top, c2, canvas, Vector3.right + Vector3.up);
            b.Quad(c2, top, top, d2, canvas, Vector3.forward + Vector3.up);
            b.Quad(d2, top, top, a, canvas, Vector3.left + Vector3.up);
            foreach (float sd in new[] { -1f, 1f })
                b.Box(at + new Vector3(0, h - 0.22f * s, sd * d), new Vector3(w, 0.22f * s, 0.03f), skirt);
        }

        // A drinks table with two orange jugs. s scales it.
        void Cooler(Blocks b, Vector3 at, float s)
        {
            b.Box(at + new Vector3(0, 0.45f, 0) * s, new Vector3(0.9f, 0.45f, 0.4f) * s, Colors.Hex("#55595f"));
            foreach (float sx in new[] { -0.4f, 0.4f })
            {
                b.Box(at + new Vector3(sx, 1.15f, 0) * s, new Vector3(0.3f, 0.25f, 0.3f) * s, Colors.Hex("#f08a24"));
                b.Box(at + new Vector3(sx, 1.43f, 0) * s, new Vector3(0.31f, 0.04f, 0.31f) * s, Colors.Hex("#f6f6f2"));
            }
        }

        // A big orange ice chest on the grass with a white lid. s scales it.
        void IceChest(Blocks b, Vector3 at, float s)
        {
            b.Box(at + new Vector3(0, 0.32f, 0) * s, new Vector3(0.6f, 0.32f, 0.36f) * s, Colors.Hex("#f07a1a"));
            b.Box(at + new Vector3(0, 0.68f, 0) * s, new Vector3(0.62f, 0.05f, 0.38f) * s, Colors.Hex("#f6f6f2"));
        }

        // A person on the sideline (a coach, a trainer, a ref, the chain crew), chibi like the players (concept 04):
        // a big round head about a third of the height, a cap, a shirt (black and white stripes for a ref), trousers,
        // shoes. holding: both hands forward at chest height (on a down marker's pole). Static mesh, scale a share of
        // the players' height.
        GameObject Person(Vector3 at, Color shirt, Color trousers, Color cap, bool stripes, float yaw, float scale, bool holding = false)
        {
            var k = new FigureKit();
            Color skin = Colors.Hex(Rand() < 0.5f ? "#e9b892" : "#a8704a");
            Color shoe = Colors.Hex("#1e1e22");
            foreach (float z in new[] { 9f, -9f })
            {
                k.Limb(new Vector3(0, 8, z), new Vector3(0, 60, z * 1.1f), 9f, 10.5f, trousers);
                k.Ellipsoid(new Vector3(4, 5, z), new Vector3(12, 5.5f, 7.5f), shoe);
            }
            var body = new FigureKit.Profile(Vector3.zero, new[] { 56f, 17f, 21f, 76f, 18f, 23f, 98f, 18f, 24f, 108f, 13f, 17f, 114f, 6f, 6f });
            k.Lathe(body, shirt, 22);
            if (stripes)
                for (int i = -3; i <= 3; i++)
                {
                    var st = FigureKit.OnLathe(body, i * 50f);
                    k.Stroke(st, new[] { new Vector2(0, 58), new Vector2(0, 106) }, new[] { 4.6f, 4.6f }, 0.4f, Colors.Hex("#1e1e22"), 2);
                }
            foreach (float sd in new[] { 1f, -1f })
            {
                if (holding)
                {
                    k.Limb(new Vector3(2, 102, sd * 21), new Vector3(16, 92, sd * 14), 7.5f, 6.5f, shirt);
                    k.Limb(new Vector3(16, 92, sd * 14), new Vector3(26, 98, sd * 6), 6f, 5.5f, skin);
                    k.Ball(new Vector3(28, 99, sd * 5), 6f, skin);
                }
                else
                {
                    k.Limb(new Vector3(0, 104, sd * 22), new Vector3(3, 72, sd * 25), 7.5f, 6.5f, shirt);
                    k.Limb(new Vector3(3, 74, sd * 25), new Vector3(5, 60, sd * 25), 5.8f, 5.2f, skin);
                    k.Ball(new Vector3(5, 56, sd * 25), 6f, skin);
                }
            }
            k.Limb(new Vector3(0, 108, 0), new Vector3(0, 118, 0), 7f, 7f, skin);   // neck
            var headC = new Vector3(3, 136, 0);
            var headR = new Vector3(21, 23, 21);
            k.Ellipsoid(headC, headR, skin, 12, 18);
            // a face that reads at a distance: two dark eyes under the cap
            foreach (float sd in new[] { 1f, -1f })
            {
                var face = FigureKit.OnEllipsoid(headC, headR, Quaternion.identity, new Vector3(1f, -0.05f, 0f), 1f);
                k.Spot(face, new Vector2(sd * 7f, 0f), new Vector2(2.6f, 3.6f), 0, 0.4f, Colors.Hex("#2a1c14"), 10);
            }
            k.Ellipsoid(new Vector3(1, 146, 0), new Vector3(22.5f, 14f, 22.5f), cap, 8, 18);   // cap
            k.Ellipsoid(new Vector3(20, 145, 0), new Vector3(12, 2f, 15), cap, 4, 12);       // brim
            var go = Looks.Static(root, stripes ? "Ref" : "Coach", k, mat);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * cfg.view.playerHeightM * scale / 170f;
            return go;
        }

        // ---- the goal posts: chunky yellow, a padded base post behind the end line, a gooseneck, crossbar, uprights ----
        void GoalPosts()
        {
            float endX = cfg.field.HalfLength * cfg.field.yardM;
            Color yellow = Colors.Hex(sc.postColor), pad = Colors.Hex(sc.postPadColor), ribbon = Colors.Hex(sc.postRibbonColor);
            float t = sc.postThickM * 0.5f, bar = sc.postCrossbarM, half = sc.postWidthM * 0.5f, up = sc.postUprightM;
            var b = new Blocks();
            foreach (float sx in new[] { -1f, 1f })
            {
                float baseX = sx * (endX + sc.postBackM);
                b.Box(new Vector3(baseX, bar * 0.5f, 0), new Vector3(t * 1.3f, bar * 0.5f, t * 1.3f), yellow);                              // the base post
                b.Box(new Vector3(baseX, sc.postPadM * 0.5f, 0), new Vector3(t * 2.2f, sc.postPadM * 0.5f, t * 2.2f), pad);                  // its pad
                b.Box(new Vector3(sx * (endX + sc.postBackM * 0.5f), bar, 0), new Vector3(sc.postBackM * 0.5f + t, t, t), yellow);           // the gooseneck
                b.Box(new Vector3(sx * endX, bar, 0), new Vector3(t, t, half + t), yellow);                                                  // the crossbar
                foreach (float sz in new[] { -1f, 1f })
                {
                    b.Box(new Vector3(sx * endX, bar + up * 0.5f, sz * half), new Vector3(t * 0.8f, up * 0.5f, t * 0.8f), yellow);          // an upright
                    b.Box(new Vector3(sx * endX, bar + up + sc.postRibbonM * 0.5f, sz * half), new Vector3(t * 0.5f, sc.postRibbonM * 0.5f, t * 0.5f), ribbon);
                }
            }
            b.Build(root, "GoalPosts", mat);
        }

        // An orange down marker: a pole with a box on top, held by one of the chain crew standing just off the field
        // (side +1: the far sideline, -1: the near one).
        Transform DownMarker(float side)
        {
            var b = new Blocks();
            b.Box(new Vector3(0, 1.1f, 0), new Vector3(0.07f, 1.1f, 0.07f), Colors.Hex("#f07a1a"));
            var go = b.Build(root, "DownMarker", mat);
            var k = new FigureKit();
            k.Ellipsoid(new Vector3(0, 2.4f, 0), new Vector3(0.36f, 0.36f, 0.1f), Quaternion.identity, Colors.Hex("#f07a1a"), 8, 16);
            k.Ellipsoid(new Vector3(0, 2.4f, -0.05f), new Vector3(0.2f, 0.2f, 0.06f), Quaternion.identity, Colors.Hex("#1e1e22"), 6, 12);
            Looks.Static(go.transform, "Flag", k, mat);
            float crewS = side > 0 ? sc.crewScale : sc.nearScale * sc.crewScale;
            var crew = Person(new Vector3(0, 0, side * sc.crewBackM), Color.white, Colors.Hex("#1e1e22"), Colors.Hex(sc.crewCapColor), true, side > 0 ? 90f : -90f, crewS, true);
            crew.transform.SetParent(go.transform, false);
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

        // Flat upright cards with one shading normal, gathered into one mesh (one per crowd slice and look).
        sealed class Cards
        {
            readonly List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>();
            readonly List<Color32> col = new List<Color32>();
            readonly List<int> tris = new List<int>();
            public Vector3 Normal = Vector3.back, Right = Vector3.right;

            // A rectangle standing upright along Right, facing the field, turned by angle (radians) round its centre.
            public void Rect(Vector3 c, float hw, float hh, Color color, float angle = 0f)
            {
                var ax = (Right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * hw;
                var ay = (-Right * Mathf.Sin(angle) + Vector3.up * Mathf.Cos(angle)) * hh;
                int s = pos.Count;
                Color32 vc = Looks.VertexColor(color, Looks.Paint);
                foreach (var p in new[] { c - ax - ay, c - ax + ay, c + ax + ay, c + ax - ay }) { pos.Add(p); nrm.Add(Normal); col.Add(vc); }
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (pos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(pos);
                m.SetNormals(nrm);
                m.SetColors(col);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                m.UploadMeshData(true);   // (only ever swapped whole: no need to keep a copy in memory)
                return m;
            }
        }
    }

    // Drives the crowd's key frames from the TV's clock (unscaled, so a slow-motion replay does not slow the crowd).
    public sealed class StadiumClock : MonoBehaviour
    {
        public System.Action<float> Tick;
        void Update() { Tick?.Invoke(Time.unscaledTime); }
    }
}
