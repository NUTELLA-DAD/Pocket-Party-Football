using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    public sealed class WorldTests
    {
        static Dictionary<int, PlayerInput> Stick(int slot, float x, float z) => new Dictionary<int, PlayerInput> { { slot, new PlayerInput { MoveX = x, MoveZ = z } } };

        [Test]
        public void TeamsSpawnOnTheirOwnHalfWithNumbers()
        {
            var w = new World(ConfigTests.Load(), 1);
            var a = w.AddPlayer(0, 0, true);
            var b = w.AddPlayer(1, 1, true);
            var c = w.AddPlayer(2, 0, false);
            Assert.Less(a.X, 0f);
            Assert.Greater(b.X, 0f);
            Assert.AreEqual(1, a.Number);
            Assert.AreEqual(1, b.Number);
            Assert.AreEqual(2, c.Number);
            Assert.AreNotEqual(a.Z, c.Z);
        }

        [Test]
        public void HumanWalksAtWalkSpeed()
        {
            var cfg = ConfigTests.Load();
            var w = new World(cfg, 1);
            var p = w.AddPlayer(5, 0, true);
            float x0 = p.X;
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(Stick(5, 1f, 0f));
            Assert.AreEqual(x0 + cfg.players.walkSpeedYdS, p.X, 1e-3f);
            // no input: stands still
            float x1 = p.X;
            w.Step(null);
            Assert.AreEqual(x1, p.X);
        }

        [Test]
        public void DiagonalIsNotFaster()
        {
            var cfg = ConfigTests.Load();
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            w.Step(Stick(0, 1f, 1f));
            float speed = (float)System.Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);
            Assert.AreEqual(cfg.players.walkSpeedYdS, speed, 1e-3f);
        }

        [Test]
        public void PlayersStayNearTheField()
        {
            var cfg = ConfigTests.Load();
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            for (int i = 0; i < cfg.sim.tickHz * 30; i++) w.Step(Stick(0, -1f, -1f));
            Assert.AreEqual(-(cfg.field.HalfLength + cfg.players.boundsMarginYd), p.X, 1e-3f);
            Assert.AreEqual(-(cfg.field.HalfWidth + cfg.players.boundsMarginYd), p.Z, 1e-3f);
        }

        [Test]
        public void BotsWanderButStayOnTheField()
        {
            var cfg = ConfigTests.Load();
            var w = new World(cfg, 7);
            for (int i = 0; i < 22; i++) w.AddPlayer(i, i % 2, false);
            var start = new float[22];
            for (int i = 0; i < 22; i++) start[i] = w.Players[i].X;
            for (int t = 0; t < cfg.sim.tickHz * 20; t++) w.Step(null);
            int moved = 0;
            foreach (var p in w.Players)
            {
                if (System.Math.Abs(p.X - start[p.Slot]) > 1f) moved++;
                Assert.LessOrEqual(System.Math.Abs(p.X), cfg.field.HalfLength);
                Assert.LessOrEqual(System.Math.Abs(p.Z), cfg.field.HalfWidth);
            }
            Assert.Greater(moved, 11, "most bots should have wandered");
        }

        [Test]
        public void PlayersStaySortedBySlot()
        {
            var w = new World(ConfigTests.Load(), 1);
            foreach (int s in new[] { 7, 2, 9, 0, 4 }) w.AddPlayer(s, s % 2, false);
            for (int i = 1; i < w.Players.Count; i++) Assert.Less(w.Players[i - 1].Slot, w.Players[i].Slot);
        }

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
