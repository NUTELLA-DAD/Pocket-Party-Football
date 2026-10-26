using System;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-a: speed, acceleration, sprint stamina, bumping, out of bounds.
    public sealed class MoverTests
    {
        static (World w, Player p, GameConfig cfg) One()
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            p.X = 0; p.Z = 0;
            return (w, p, cfg);
        }

        static float Speed(Player p) => (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);

        [Test]
        public void SpeedsUpOverTimeThenJogs()
        {
            var (w, p, cfg) = One();
            w.Step(WorldTests.Stick(0, 1f, 0f));
            Assert.Less(Speed(p), cfg.players.jogSpeedYdS * 0.5f, "not instant: accelerates");
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f));
            Assert.AreEqual(cfg.players.jogSpeedYdS, Speed(p), 1e-3f);
        }

        [Test]
        public void StopsQuicklyButNotInstantly()
        {
            var (w, p, cfg) = One();
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f));
            w.Step(null);
            Assert.Greater(Speed(p), 0f);
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(null);
            Assert.AreEqual(0f, Speed(p));
        }

        [Test]
        public void DiagonalIsNotFaster()
        {
            var (w, p, cfg) = One();
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 1f));
            Assert.AreEqual(cfg.players.jogSpeedYdS, Speed(p), 1e-3f);
        }

        [Test]
        public void SprintIsFasterUntilTired()
        {
            var (w, p, cfg) = One();
            var c = cfg.players;
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f, true));
            Assert.AreEqual(c.sprintSpeedYdS, Speed(p), 1e-3f);
            Assert.Less(p.Stamina, 1f);
            // sprint until the tank is empty
            int guard = 0;
            while (!p.Tired && guard++ < cfg.sim.tickHz * 30) w.Step(WorldTests.Stick(0, 1f, 0f, true));
            Assert.IsTrue(p.Tired, "sprinting long enough empties the tank");
            float sprintSeconds = (float)guard / cfg.sim.tickHz + 1f;
            Assert.AreEqual(1f / c.staminaDrainPerS, sprintSeconds, 0.2f);
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f, true));
            Assert.AreEqual(c.jogSpeedYdS, Speed(p), 1e-3f, "tired: holding SPRINT only jogs");
            // rest: the tank refills and sprint comes back at staminaRestart
            guard = 0;
            while (p.Tired && guard++ < cfg.sim.tickHz * 30) w.Step(null);
            Assert.IsFalse(p.Tired);
            Assert.GreaterOrEqual(p.Stamina, c.staminaRestart);
        }

        [Test]
        public void StandingStillRefillsStamina()
        {
            var (w, p, cfg) = One();
            p.Stamina = 0.5f;
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(null);
            Assert.AreEqual(0.5f + cfg.players.staminaRegenPerS, p.Stamina, 1e-3f);
        }

        [Test]
        public void PlayersBumpAndNeverOverlap()
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            var w = new World(cfg, 1);
            var a = w.AddPlayer(0, 0, true);
            var b = w.AddPlayer(1, 1, true);
            a.X = -3; a.Z = 0; b.X = 3; b.Z = 0;
            var both = WorldTests.Stick(0, 1f, 0f);
            both[1] = new PlayerInput { MoveX = -1f };
            float min = cfg.players.radiusYd * 2f;
            for (int i = 0; i < cfg.sim.tickHz * 3; i++)
            {
                w.Step(both);
                Assert.GreaterOrEqual(b.X - a.X, min * cfg.players.bumpPushFrac, "pushed apart every tick");
            }
            Assert.Less(a.X, b.X, "they never pass through each other");
            Assert.AreEqual(-a.X, b.X, 0.01f, "equal shove from both sides");
        }

        [Test]
        public void RunningPastTheSidelineIsOutOfBounds()
        {
            var (w, p, cfg) = One();
            Assert.IsFalse(p.OutOfBounds);
            for (int i = 0; i < cfg.sim.tickHz * 10; i++) w.Step(WorldTests.Stick(0, 0f, -1f));
            Assert.IsTrue(p.OutOfBounds);
            Assert.AreEqual(-(cfg.field.HalfWidth + cfg.players.boundsMarginYd), p.Z, 1e-3f, "stops a few yards off the field");
            for (int i = 0; i < cfg.sim.tickHz * 3; i++) w.Step(WorldTests.Stick(0, 0f, 1f));
            Assert.IsFalse(p.OutOfBounds, "back on the field");
        }

        [Test]
        public void PastTheEndLineIsOutOfBoundsToo()
        {
            var (w, p, cfg) = One();
            p.X = cfg.field.HalfLength - 1f;
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f));
            Assert.IsTrue(p.OutOfBounds);
        }
    }
}
