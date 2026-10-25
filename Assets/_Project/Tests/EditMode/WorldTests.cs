using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    public sealed class WorldTests
    {
        public static Dictionary<int, PlayerInput> Stick(int slot, float x, float z, bool sprint = false) =>
            new Dictionary<int, PlayerInput> { { slot, new PlayerInput { MoveX = x, MoveZ = z, Sprint = sprint } } };

        public static World Full(GameConfig cfg, int seed = 1)
        {
            var w = new World(cfg, seed);
            for (int i = 0; i < cfg.players.perTeam * cfg.teams.Count; i++) w.AddPlayer(i, i % cfg.teams.Count, false);
            return w;
        }

        // ---- formation ----
        [Test]
        public void TwentyTwoLineUpOnTheirOwnSides()
        {
            var cfg = ConfigTests.Load();
            var w = Full(cfg);
            Assert.AreEqual(22, w.Players.Count);
            Assert.AreEqual(11, w.TeamCount(0));
            Assert.AreEqual(11, w.TeamCount(1));
            float los = cfg.formation.lineOfScrimmageXYd, dir = cfg.formation.attackDir;
            foreach (var p in w.Players)
            {
                float ahead = (p.X - los) * dir;
                if (p.Team == w.Offense) Assert.Less(ahead, 0f, p.Pos + " #" + p.Number + " should be behind the line");
                else Assert.Greater(ahead, 0f, p.Pos + " #" + p.Number + " should be across the line");
                Assert.LessOrEqual(Math.Abs(p.Z), cfg.field.HalfWidth, "inside the sidelines");
            }
        }

        [Test]
        public void OffenseAndDefenseHaveTheRightPositions()
        {
            var w = Full(ConfigTests.Load());
            string Count(int team)
            {
                var c = new SortedDictionary<string, int>();
                foreach (var p in w.Players) if (p.Team == team) c[p.Pos] = (c.TryGetValue(p.Pos, out int n) ? n : 0) + 1;
                var parts = new List<string>();
                foreach (var kv in c) parts.Add(kv.Key + kv.Value);
                return string.Join(" ", parts);
            }
            Assert.AreEqual("OL5 QB1 RB1 TE1 WR3", Count(w.Offense));
            Assert.AreEqual("CB2 DL4 LB3 S2", Count(w.Defense));
        }

        [Test]
        public void JerseyNumbersAreUniquePerTeamAndTheQbHasTheBall()
        {
            var w = Full(ConfigTests.Load());
            foreach (int team in new[] { 0, 1 })
            {
                var seen = new HashSet<int>();
                foreach (var p in w.Players) if (p.Team == team) Assert.IsTrue(seen.Add(p.Number), "number " + p.Number + " twice");
            }
            var qb = w.Find(w.Ball.Carrier);
            Assert.IsNotNull(qb);
            Assert.AreEqual("QB", qb.Pos);
            Assert.AreEqual(w.Offense, qb.Team);
        }

        [Test]
        public void FullTeamTakesNoTwelfth()
        {
            var w = Full(ConfigTests.Load());
            Assert.IsNull(w.AddPlayer(99, 0, false));
        }

        [Test]
        public void PersonTakesABotsSpotAndTheBall()
        {
            var w = Full(ConfigTests.Load());
            var qbBot = w.Find(w.Ball.Carrier);
            var me = w.ReplaceBot(w.Offense, 500);
            Assert.IsNotNull(me);
            Assert.IsTrue(me.Human);
            Assert.AreEqual("QB", me.Pos, "the first person on offense is the QB");
            Assert.IsNull(w.Find(qbBot.Slot));
            Assert.AreEqual(11, w.TeamCount(w.Offense));
            Assert.AreEqual(500, w.Ball.Carrier);
            var mlb = w.ReplaceBot(w.Defense, 501);
            Assert.AreEqual("LB", mlb.Pos, "the first person on defense is the middle LB");
            Assert.AreEqual(0f, mlb.Z, "the middle one");
        }

        [Test]
        public void PlayersStaySortedBySlot()
        {
            var w = new World(ConfigTests.Load(), 1);
            foreach (int s in new[] { 7, 2, 9, 0, 4 }) w.AddPlayer(s, s % 2, false);
            for (int i = 1; i < w.Players.Count; i++) Assert.Less(w.Players[i - 1].Slot, w.Players[i].Slot);
        }

        // ---- bots ----
        [Test]
        public void BotsHoldTheirSpotUnlessWandering()
        {
            var cfg = ConfigTests.Load();
            var w = Full(cfg);
            for (int t = 0; t < cfg.sim.tickHz * 5; t++) w.Step(null);
            foreach (var p in w.Players) Assert.Less(Dist(p.X, p.Z, p.HomeX, p.HomeZ), cfg.bots.arriveYd + 0.01f);

            w.BotsWander = true;
            for (int t = 0; t < cfg.sim.tickHz * 20; t++) w.Step(null);
            int moved = 0;
            foreach (var p in w.Players)
            {
                if (Dist(p.X, p.Z, p.HomeX, p.HomeZ) > 2f) moved++;
                Assert.LessOrEqual(Math.Abs(p.X), cfg.field.HalfLength + cfg.players.radiusYd * 4f);
                Assert.LessOrEqual(Math.Abs(p.Z), cfg.field.HalfWidth + cfg.players.radiusYd * 4f);
            }
            Assert.Greater(moved, 11, "most bots should have wandered");

            w.BotsWander = false;
            w.LineUp();
            foreach (var p in w.Players) { Assert.AreEqual(p.HomeX, p.X); Assert.AreEqual(p.HomeZ, p.Z); }
        }

        static float Dist(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        [Test]
        public void RngIsSeeded()
        {
            var a = new Rng(42); var b = new Rng(42); var c = new Rng(43);
            for (int i = 0; i < 100; i++)
            {
                float v = a.Value();
                Assert.AreEqual(v, b.Value());
                Assert.That(v, Is.InRange(0f, 0.99999999f));
            }
            Assert.AreNotEqual(new Rng(42).Next(), c.Next());
        }
    }
}
