using System;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-a: the ball flies in 3D (height), lands where it was aimed, bounces and rolls to a stop.
    public sealed class BallTests
    {
        static (World w, GameConfig cfg) Ready()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            return (w, cfg);
        }

        // Steps until the ball first comes down to catch height on the way down; returns ticks taken.
        static int FlyToCatchHeight(World w, GameConfig cfg, out float x, out float z)
        {
            int t = 0;
            x = z = 0;
            while (t++ < cfg.sim.tickHz * 10)
            {
                w.Step(null);
                if (w.Ball.VY < 0f && w.Ball.Y <= cfg.ball.catchHeightYd) { x = w.Ball.X; z = w.Ball.Z; return t; }
            }
            Assert.Fail("the ball never came down");
            return t;
        }

        [Test]
        public void HeldBallRidesWithTheQb()
        {
            var (w, cfg) = Ready();
            var qb = w.Find(w.Ball.Carrier);
            w.Step(null);
            Assert.AreEqual(BallState.Held, w.Ball.State);
            Assert.AreEqual(qb.X, w.Ball.X);
            Assert.AreEqual(cfg.ball.carryHeightYd, w.Ball.Y);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PassComesDownWhereItWasAimed(bool lob)
        {
            var (w, cfg) = Ready();
            float tx = w.Ball.X + 20f, tz = w.Ball.Z + 8f;
            Assert.IsTrue(w.Throw(tx, tz, lob));
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            Assert.AreEqual(-1, w.Ball.Carrier);
            FlyToCatchHeight(w, cfg, out float x, out float z);
            float miss = (float)Math.Sqrt((x - tx) * (x - tx) + (z - tz) * (z - tz));
            Assert.Less(miss, 1f, "lands within a yard of the target");
        }

        [Test]
        public void LobGoesHigherAndSlowerThanABullet()
        {
            var (wl, cfg) = Ready();
            var (wb, _) = Ready();
            float tx = wl.Ball.X + 20f, tz = wl.Ball.Z;
            wl.Throw(tx, tz, true);
            wb.Throw(tx, tz, false);
            float topL = 0f, topB = 0f;
            int tl = 0, tb = 0;
            while (wl.Ball.State == BallState.Flying && (wl.Ball.VY > 0f || wl.Ball.Y > cfg.ball.catchHeightYd)) { wl.Step(null); topL = Math.Max(topL, wl.Ball.Y); tl++; }
            while (wb.Ball.State == BallState.Flying && (wb.Ball.VY > 0f || wb.Ball.Y > cfg.ball.catchHeightYd)) { wb.Step(null); topB = Math.Max(topB, wb.Ball.Y); tb++; }
            Assert.Greater(topL, topB + 3f);
            Assert.Greater(tl, tb);
            Assert.AreEqual(cfg.ball.carryHeightYd + cfg.ball.lobApexYd, topL, 0.5f);
        }

        [Test]
        public void UncaughtBallBouncesRollsAndStops()
        {
            var (w, cfg) = Ready();
            w.Throw(w.Ball.X + 15f, w.Ball.Z, false);
            bool bounced = false;
            for (int i = 0; i < cfg.sim.tickHz * 10; i++)
            {
                float vyBefore = w.Ball.VY;
                w.Step(null);
                Assert.GreaterOrEqual(w.Ball.Y, 0f, "never under the grass");
                if (vyBefore < 0f && w.Ball.VY > 0f) bounced = true;
            }
            Assert.IsTrue(bounced, "bounces at least once");
            Assert.AreEqual(BallState.Loose, w.Ball.State);
            Assert.AreEqual(0f, w.Ball.Y);
            Assert.AreEqual(0f, w.Ball.VX);
            Assert.AreEqual(0f, w.Ball.VZ);
        }

        [Test]
        public void OnlyTheCarrierCanThrow()
        {
            var (w, _) = Ready();
            Assert.IsTrue(w.Throw(0f, 0f, true));
            Assert.IsFalse(w.Throw(0f, 0f, true), "already in the air");
            w.LineUp();
            Assert.AreEqual(BallState.Held, w.Ball.State);
            Assert.AreEqual("QB", w.Find(w.Ball.Carrier).Pos);
        }
    }
}
