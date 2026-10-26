using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // WS-12: the stats add up (to the score, and to the sim's own yards), the post-game screen fits 22 players
    // without overlap on any TV, and a score sets off the celebration. FEATURES.stats ships off, so each test turns
    // it on in its own config.
    public sealed class StatsTests
    {
        static GameConfig On(Dictionary<string, string> env = null)
        {
            var cfg = ConfigTests.Load(env);
            cfg.features["stats"] = true;
            return cfg;
        }

        static string Table(World w, Stats s)
        {
            var sb = new StringBuilder();
            sb.Append("score ").Append(w.Score[0]).Append('-').Append(w.Score[1]).Append(", ").Append(w.PlaysRun).Append(" plays\n");
            foreach (var l in s.Lines)
                sb.Append("  slot ").Append(l.Slot).Append(" team ").Append(l.Team).Append(" #").Append(l.Number).Append(' ').Append(l.Pos)
                  .Append(": ").Append(Stats.Summary(l)).Append(" (").Append(l.Points).Append(" pts)\n");
            return sb.ToString();
        }

        // ---- the gate: a bot game's stats add up ----

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ABotGamesStatsAddUpToTheScore(int seed)
        {
            var cfg = On();
            var w = WorldTests.Full(cfg, seed);
            var s = new Stats();
            var yards = new int[cfg.teams.Count];   // the sim's own yards, by the team that had the ball, turnovers left out
            int gained = 0;
            for (int i = 0; i < cfg.sim.tickHz * 60 * 15; i++)
            {
                int offense = w.Offense;
                w.Step(null);
                s.Observe(w);
                if (w.YardsGained != gained)
                {
                    if (w.Result != "INTERCEPTED" && w.Result != "PICK SIX" && w.Result != "FUMBLE LOST") yards[offense] += w.YardsGained - gained;
                    gained = w.YardsGained;
                }
            }
            string table = Table(w, s);
            TestContext.WriteLine("seed " + seed + ": " + table);
            Assert.Greater(w.PlaysRun, 20, "plays happened: " + table);
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                int points = 0, teamYards = 0;
                foreach (var l in s.Lines)
                {
                    if (l.Team != t) continue;
                    points += l.Points; teamYards += l.Yards;
                }
                Assert.AreEqual(w.Score[t], points, "team " + t + ": the players' points are the score\n" + table);
                Assert.AreEqual(0, s.Extra(t), "no points without a scorer (kicking is off)\n" + table);
                Assert.AreEqual(w.Score[t], s.Team(t).Points, "the team line is the score");
                Assert.AreEqual(yards[t], teamYards, "team " + t + ": the players' yards are the sim's yards\n" + table);
                Assert.AreEqual(teamYards, s.Team(t).Yards);
            }
            // every tackle, sack and pick in the events is on someone's line
            int allTackles = 0, allSacks = 0, allPicks = 0, allTds = 0;
            foreach (var l in s.Lines) { allTackles += l.Tackles; allSacks += l.Sacks; allPicks += l.Picks; allTds += l.Tds; }
            Assert.AreEqual(w.Events.Total(EventKind.Tackle) + w.Events.Total(EventKind.Sack), allTackles, "tackles (a sack is a tackle too)");
            Assert.AreEqual(w.Events.Total(EventKind.Sack), allSacks, "sacks");
            Assert.AreEqual(w.Events.Total(EventKind.Interception), allPicks, "picks");
            Assert.AreEqual(w.Events.Total(EventKind.Touchdown), allTds, "touchdowns");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void WithKickingTheScorersAndKicksStillMakeTheScore(int seed)
        {
            // the game as shipped: kickoffs, tries (a try run in is worth 2 to its scorer), field goals and safeties
            var cfg = On(ConfigTests.AsShipped);
            Assert.IsTrue(Kicking.On(new World(cfg, seed)), "kicking is on as shipped");
            var w = WorldTests.Full(cfg, seed);
            var s = new Stats();
            for (int i = 0; i < cfg.sim.tickHz * 60 * 15; i++) { w.Step(null); s.Observe(w); }
            string table = Table(w, s);
            TestContext.WriteLine("seed " + seed + ", kicking on: " + table + " extra " + s.Extra(0) + "/" + s.Extra(1));
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                int points = 0;
                foreach (var l in s.Lines) if (l.Team == t) points += l.Points;
                Assert.GreaterOrEqual(s.Extra(t), 0, "kicks and safeties only add points\n" + table);
                Assert.AreEqual(w.Score[t], points + s.Extra(t), "team " + t + ": scorers plus kicks are the score\n" + table);
                Assert.AreEqual(w.Score[t], s.Team(t).Points);
            }
            foreach (var l in s.Lines)
                Assert.GreaterOrEqual(l.Points, l.Tds * cfg.kicking.tryTouchdownPoints, "slot " + l.Slot + ": every touchdown scored at least a try's worth");
        }

        [Test]
        public void ThreeBotGamesScoreSomething()
        {
            // (so the test above checks real points, not 0 = 0 everywhere)
            int total = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                var cfg = On();
                var w = WorldTests.Full(cfg, seed);
                var s = new Stats();
                for (int i = 0; i < cfg.sim.tickHz * 60 * 15; i++) { w.Step(null); s.Observe(w); }
                total += w.Score[0] + w.Score[1];
            }
            Assert.Greater(total, 0, "three 15-minute bot games score at least once");
        }

        [Test]
        public void ObservingOncePerFrameCountsTheSame()
        {
            // the TV observes once a frame, often after two or three ticks
            var cfg = On();
            var a = WorldTests.Full(cfg, 2);
            var b = WorldTests.Full(cfg, 2);
            var sa = new Stats(); var sb = new Stats();
            for (int i = 0; i < cfg.sim.tickHz * 60 * 5; i++)
            {
                a.Step(null); sa.Observe(a);
                b.Step(null); if (i % 3 == 2) sb.Observe(b);
            }
            sb.Observe(b);
            Assert.AreEqual(Table(a, sa), Table(b, sb));
        }

        [Test]
        public void CountingStatsNeverChangesTheGame()
        {
            var cfg = On();
            var a = WorldTests.Full(cfg, 3);
            var b = WorldTests.Full(cfg, 3);
            var s = new Stats();
            for (int i = 0; i < cfg.sim.tickHz * 60 * 3; i++) { a.Step(null); b.Step(null); s.Observe(b); }
            Assert.AreEqual(a.Hash(), b.Hash());
        }

        [Test]
        public void StatsShipOffAndNoStatsTurnsThemOff()
        {
            Assert.IsFalse(Stats.On(ConfigTests.Load()), "FEATURES.stats ships off");
            var cfg = ConfigTests.Load();
            cfg.features["stats"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_STATS" ? "1" : null);
            Assert.IsFalse(Stats.On(cfg));
            var w = WorldTests.Full(cfg, 1);
            var s = new Stats();
            for (int i = 0; i < cfg.sim.tickHz * 60; i++) { w.Step(null); s.Observe(w); }
            CollectionAssert.IsEmpty(s.Lines, "nothing is counted with the switch off");
        }

        // ---- a scripted touchdown: the scorer's line, and the celebration ----

        // To the snap with both teams called (bots hike), then the ball carrier a step past the goal line.
        static (World w, Player scorer, float los) RunItIn(GameConfig cfg)
        {
            var w = WorldTests.Full(cfg, 1);
            w.Step(null);
            w.Calls[w.Offense] = "slants"; w.Calls[w.Defense] = "cover3";
            for (int i = 0; i < cfg.sim.tickHz * 40 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "the snap");
            var c = LivePlay.Carrier(w);
            Assert.IsNotNull(c, "someone has the ball");
            float los = w.LineOfScrimmageX, goal = cfg.field.GoalLineX;
            c.X = w.Dir * (goal + 1f); c.Z = 0f;
            foreach (var p in w.Players) if (p.Team == w.Defense) { p.X = c.X - w.Dir * 15f; p.HomeX = p.X; }
            return (w, c, los);
        }

        [Test]
        public void ATouchdownIsTheScorersSixPointsAndHisYards()
        {
            var cfg = On();
            var (w, c, los) = RunItIn(cfg);
            var s = new Stats();
            s.Observe(w);
            int offense = w.Offense;
            float dir = w.Dir;
            w.Step(null);
            s.Observe(w);
            Assert.AreEqual("TOUCHDOWN", w.Result);
            var l = s.Of(c);
            Assert.AreEqual(1, l.Tds);
            Assert.AreEqual(cfg.downs.touchdownPoints, l.Points);
            Assert.AreEqual(w.Score[offense], s.Team(offense).Points);
            Assert.AreEqual((int)System.Math.Round((cfg.field.GoalLineX * dir - los) * dir), l.Yards, "to the goal line");
            Assert.AreEqual(l, s.Mvp(cfg.stats), "the only scorer is the player of the game");
            StringAssert.StartsWith("1 TD", Stats.Summary(l));
        }

        [Test]
        public void AScoreSetsOffTheCelebration()
        {
            var cfg = On();
            var (w, c, _) = RunItIn(cfg);
            // a teammate and an opponent close by
            Player mate = null, opp = null;
            foreach (var p in w.Players)
            {
                if (mate == null && p.Team == c.Team && p != c) mate = p;
                if (opp == null && p.Team != c.Team) opp = p;
            }
            mate.X = c.X - w.Dir * 2f; mate.Z = 1f;
            opp.X = c.X - w.Dir * 3f; opp.Z = -1f;
            var cheers = new Celebrations();
            cheers.Observe(w);
            w.Step(null);
            cheers.Observe(w);
            Assert.AreEqual("TOUCHDOWN", w.Result);
            Assert.AreEqual(Cheer.Spike, cheers.Of(w, c.Slot).Kind, "the scorer spikes it");
            Assert.AreEqual(Cheer.Leap, cheers.Of(w, mate.Slot).Kind, "a teammate jumps");
            Assert.AreEqual(Cheer.Slump, cheers.Of(w, opp.Slot).Kind, "the other team hangs its head");
            int most = 1 + 2 * cfg.stats.maxCheer;
            Assert.LessOrEqual(cheers.Count, most);
            // the frames step at anim.fps
            int tick0 = w.Tick;
            int ticksPerFrame = (int)(cfg.sim.tickHz / cfg.anim.fps);
            for (int i = 0; i < ticksPerFrame + 1; i++) { w.Step(null); cheers.Observe(w); }
            Assert.AreEqual((int)((w.Tick - tick0 + 1) * cfg.anim.fps / cfg.sim.tickHz), cheers.Of(w, mate.Slot).Frame);
            // the spike is short; the cheering is over after celebrateS
            for (int i = 0; i < cfg.stats.spikeS * cfg.sim.tickHz; i++) { w.Step(null); cheers.Observe(w); }
            Assert.AreEqual(Cheer.None, cheers.Of(w, c.Slot).Kind, "back to Moves' own celebration");
            for (int i = 0; i < cfg.stats.celebrateS * cfg.sim.tickHz; i++) { w.Step(null); cheers.Observe(w); }
            Assert.AreEqual(Cheer.None, cheers.Of(w, mate.Slot).Kind);
            Assert.AreEqual(0, cheers.Count);
        }

        [Test]
        public void NoCelebrationWithTheSwitchOff()
        {
            var cfg = ConfigTests.Load();   // stats off
            var (w, c, _) = RunItIn(cfg);
            var cheers = new Celebrations();
            cheers.Observe(w);
            w.Step(null);
            cheers.Observe(w);
            Assert.AreEqual("TOUCHDOWN", w.Result);
            Assert.AreEqual(Cheer.None, cheers.Of(w, c.Slot).Kind);
        }

        // ---- the post-game screen ----

        static IEnumerable<Vector2> Screens()
        {
            yield return new Vector2(1920, 1080);
            yield return new Vector2(1280, 720);
            yield return new Vector2(3840, 2160);
            yield return new Vector2(1366, 768);
            yield return new Vector2(1024, 768);
            yield return new Vector2(2560, 1080);
        }

        static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax - 0.01f && b.xMin < a.xMax - 0.01f && a.yMin < b.yMax - 0.01f && b.yMin < a.yMax - 0.01f;

        static bool Inside(Rect r, Vector2 screen) => r.xMin >= -0.01f && r.yMin >= -0.01f && r.xMax <= screen.x + 0.01f && r.yMax <= screen.y + 0.01f;

        [Test]
        public void ThePostGameScreenFits22PlayersWithoutOverlap()
        {
            var cfg = On();
            int teams = cfg.teams.Count, rows = cfg.players.perTeam;
            Assert.AreEqual(22, teams * rows);
            foreach (var sc in Screens())
            {
                var l = PostGame.Lay(sc.x, sc.y, teams, rows, cfg.stats);
                var all = new List<Rect> { l.Title, l.Score, l.Mvp };
                for (int t = 0; t < teams; t++) { all.Add(l.TeamHead[t]); all.Add(l.ColumnHead[t]); all.AddRange(l.Rows[t]); }
                Assert.AreEqual(3 + teams * (2 + rows), all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    Assert.IsTrue(Inside(all[i], sc), sc + ": box " + i + " " + all[i] + " is on the screen");
                    Assert.Greater(all[i].width, 0f); Assert.Greater(all[i].height, 0f);
                    for (int j = i + 1; j < all.Count; j++) Assert.IsFalse(Overlap(all[i], all[j]), sc + ": boxes " + i + " " + all[i] + " and " + j + " " + all[j] + " overlap");
                }
                float s = sc.y / 1080f;
                Assert.GreaterOrEqual(l.RowFont, cfg.stats.minRowPx * s - 0.01f, sc + ": the rows are big enough to read from the couch");
                // the name column holds a whole name: about 0.6 em a capital
                float nameW = l.Rows[0][0].width * PostGame.ColumnFrac[2];
                Assert.GreaterOrEqual(nameW, cfg.stats.nameChars * l.RowFont * 0.6f, sc + ": a " + cfg.stats.nameChars + "-letter name fits");
            }
        }

        [Test]
        public void TheColumnsAddUpToTheRow()
        {
            float sum = 0f;
            foreach (float f in PostGame.ColumnFrac) sum += f;
            Assert.AreEqual(1f, sum, 1e-4f);
            Assert.AreEqual(PostGame.Columns.Length, PostGame.ColumnFrac.Length);
        }

        [Test]
        public void TheScreenListsEveryPlayerOfBothTeams()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg, 1);
            var s = new Stats();
            for (int i = 0; i < cfg.sim.tickHz * 60 * 5; i++) { w.Step(null); s.Observe(w); }
            var seen = new HashSet<int>();
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                var rows = PostGame.TeamRows(w, s, t);
                Assert.AreEqual(cfg.players.perTeam, rows.Count);
                for (int r = 1; r < rows.Count; r++)
                    Assert.GreaterOrEqual(Stats.Impact(rows[r - 1], cfg.stats), Stats.Impact(rows[r], cfg.stats), "the biggest impact first");
                foreach (var l in rows) { Assert.AreEqual(t, l.Team); seen.Add(l.Slot); }
            }
            Assert.AreEqual(22, seen.Count);
        }

        [Test]
        public void ARowCutsLongNamesAndCallsBotsBots()
        {
            var cfg = On();
            var l = new PlayerLine { Number = 87, Pos = "WR", RecYd = 40, RushYd = 2, Catches = 3, Tds = 1 };
            var cells = PostGame.Cells(l, "abcdefghijklmnopq", cfg.stats);
            CollectionAssert.AreEqual(new[] { "87", "WR", "ABCDEFGHIJKL", "42", "3", "0", "0", "1" }, cells);
            Assert.AreEqual("BOT", PostGame.Cells(l, null, cfg.stats)[2]);
            Assert.AreEqual("1 TD · 42 YDS · 3 REC", Stats.Summary(l));
            Assert.AreEqual("NO STATS", Stats.Summary(new PlayerLine()));
        }
    }
}
