using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // The turn gauntlet's limits (TurnAudit.cs): over whole bot games nobody spins round fast, nobody turns over still
    // feet, and the QB faces down the field in the pocket and toward his throw (FEATURES.calmTurns, qbStrafe). The old
    // turning fails the same audit, so it is known to see the problem.
    public sealed class TurnAuditTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void NobodySpinsOverStillFeet(int seed)
        {
            var a = TurnAudit.Run(ConfigTests.Load(), seed, 240);
            TestContext.WriteLine("seed " + seed + ": " + a.Report());
            Assert.Greater(a.Ticks, 100000, "a real game was audited: " + a.Report());
            Assert.AreEqual(0, a.Snaps, "no turn faster than " + TurnAudit.SnapDeg * 30 + " degrees a second: " + a.Report());
            Assert.AreEqual(0, a.Spins, "no " + TurnAudit.SpinDeg + " degree spin inside a fifth of a second: " + a.Report());
            Assert.AreEqual(0, a.Skates, "the feet step whenever the body turns: " + a.Report());
            Assert.Greater(a.PocketTicks, 0, "the QB had the ball in the pocket: " + a.Report());
            Assert.AreEqual(0, a.PocketBack, "the QB in the pocket faces down the field: " + a.Report());
            Assert.Greater(a.Throws, 0, "passes were thrown: " + a.Report());
            Assert.Less(a.ThrowOffSum / a.Throws, 35f, "the QB's body is turned toward his throw as it goes: " + a.Report());
        }

        [Test]
        public void TheOldTurningFailsTheAudit()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { ["NO_CALM_TURNS"] = "1", ["NO_QB_STRAFE"] = "1" });
            var a = TurnAudit.Run(cfg, 1, 120);
            TestContext.WriteLine("old: " + a.Report());
            Assert.Greater(a.Snaps, 100, a.Report());
            Assert.Greater(a.Skates, 100, a.Report());
            Assert.Greater(a.PocketBack, 0, "the old QB turned his back to the field dropping back: " + a.Report());
        }

        static GameConfig Cfg() => ConfigTests.Load();

        [Test]
        public void ABallStraightBehindStaysOverOneShoulder()
        {
            var cfg = Cfg();
            var s = default(FacingState);
            float dt = 1f / cfg.sim.tickHz;
            // running at +x, the ball comes from behind a little to his left, then crosses just to his right
            for (int i = 0; i < 30; i++) Facing.Step(ref s, cfg, i * 0.2f, 0f, 6f, 0f, 0f, float.NaN, true, i * 0.2f - 10f, 3f, 0.5f, 1.5f, dt);
            float side = s.Side;
            float extra = s.Extra;
            Assert.AreNotEqual(0f, side);
            Facing.Step(ref s, cfg, 6f, 0f, 6f, 0f, 0f, float.NaN, true, -4f, 3f, -0.5f, 1.5f, dt);
            Assert.AreEqual(side, s.Side, "a ball moving just past straight behind him doesn't flip him to the other shoulder");
            Assert.Less(UnityEngine.Mathf.Abs(s.Extra - extra), cfg.anim.turnMaxDegS * dt + 0.01f);
        }

        [Test]
        public void TurningOnTheSpotStepsTheFeet()
        {
            var cfg = Cfg();
            Assert.Greater(Facing.Pivot(cfg, 35f), 0f);
            Assert.AreEqual(0.5f, Facing.Pivot(cfg, cfg.anim.turnStepDeg), 1e-4f, "one step per turnStepDeg (half a stride)");
            var off = ConfigTests.Load(new Dictionary<string, string> { ["NO_CALM_TURNS"] = "1" });
            Assert.AreEqual(0f, Facing.Pivot(off, 35f));
        }

        [Test]
        public void TheStrafingQbsLegsNeverFlipSides()
        {
            var cfg = Cfg();
            float last = Facing.StrafeLegs(cfg, 0f, 3f, 0f);
            Assert.AreEqual(0f, last, 1e-3f, "running straight at where he aims: legs square");
            Assert.AreEqual(0f, Facing.StrafeLegs(cfg, 0f, -3f, 0f), 1e-3f, "backpedalling: legs square");
            Assert.AreEqual(0f, Facing.StrafeLegs(cfg, 0f, 0f, 3f), 1e-3f, "a shuffle across: legs square");
            for (int deg = 0; deg <= 360; deg += 5)
            {
                float r = deg * UnityEngine.Mathf.Deg2Rad;
                float now = Facing.StrafeLegs(cfg, 0f, 3f * UnityEngine.Mathf.Cos(r), -3f * UnityEngine.Mathf.Sin(r));
                Assert.LessOrEqual(UnityEngine.Mathf.Abs(now), cfg.anim.strafeHipDeg + 1e-3f);
                Assert.Less(UnityEngine.Mathf.Abs(now - last), 0.2f * cfg.anim.strafeHipDeg, "5 degrees more round turns the legs only a little, at " + deg);
                last = now;
            }
            Assert.AreEqual(-1f, Facing.StrideWay(0f, -3f, 0f), "running backward from where he faces: a backpedal");
            Assert.AreEqual(1f, Facing.StrideWay(0f, 3f, 0.5f));
        }

        [Test]
        public void TheQbFacesHisAimInThePocket()
        {
            var cfg = Cfg();
            var w = WorldTests.Full(cfg, 1);
            for (int i = 0; i < 4000 && !(w.Phase == Phase.Live && LivePlay.Carrier(w) == LivePlay.Qb(w) && Facing.InPocket(w, LivePlay.Qb(w))); i++) w.Step(null);
            var qb = LivePlay.Qb(w);
            Assume.That(Facing.InPocket(w, qb), "found a QB in the pocket");
            Assert.AreEqual(Momentum.YawOf(w.Dir, 0f), Facing.QbYaw(w, qb, false, float.NaN, float.NaN), 1e-3f, "not aiming: down the field");
            Assert.AreEqual(Momentum.YawOf(0.5f, 1f), Facing.QbYaw(w, qb, false, 0.5f, 1f), 1e-3f, "aiming: where the stick points");
            var off = ConfigTests.Load(new Dictionary<string, string> { ["NO_QB_STRAFE"] = "1" });
            Assert.IsTrue(float.IsNaN(Facing.QbYaw(new World(off, 1), qb, false, 0.5f, 1f)));
        }

        [Test]
        public void HandsReachUpToTheBall()
        {
            var a = Cfg().anim;
            Assert.Greater(Facing.ReachUp(a, 0f, 0f, 1.5f, 3f, 4.5f, 0f), 30f, "a ball above and ahead: arms up toward it");
            Assert.Less(Facing.ReachUp(a, 0f, 0f, 1.5f, 3f, 1f, 0f), 0f, "a low ball: arms down toward it");
        }
    }
}
