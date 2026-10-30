using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.tightCoverage (Sim/TightCoverage.cs): a defender right on the receiver as the ball gets there picks it off
    // or knocks it down now and then; one a step or more off him hardly ever does.
    public sealed class TightCoverageTests
    {
        // A pass coming down low at (0, 0) onto a receiver standing there, and a defender `off` yards behind him (further
        // from the ball than the receiver; inFront: the same distance on the ball's side of him instead, under it first).
        static (World w, Player r, Player d) JumpBall(GameConfig cfg, int seed, float off, bool inFront = false)
        {
            var w = new World(cfg, seed);
            var r = w.AddPlayer(0, w.Offense, false);
            var d = w.AddPlayer(1, w.Defense, false);
            w.Phase = Phase.Live;
            var b = w.Ball;
            b.State = BallState.Flying; b.Carrier = -1;
            b.X = 0f; b.Z = 0f; b.Y = 1f; b.VX = 6f; b.VY = -3f; b.VZ = 0f;
            b.TargetX = 0f; b.TargetZ = 0f;
            if (inFront) { r.X = -off * 0.6f; d.X = off * 0.4f; }
            else { r.X = 0f; d.X = off; }
            r.Z = d.Z = 0f;
            r.VX = r.VZ = d.VX = d.VZ = 0f;
            return (w, r, d);
        }

        [Test]
        public void TheSwitchIsInGameJsonAndOffByEnv()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("tightCoverage"), "tightCoverage ships on");
            Assert.AreEqual("NO_TIGHT_COVERAGE", ConfigLoader.EnvName("tightCoverage"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_TIGHT_COVERAGE", "1" } }).Feature("tightCoverage"));
            var cfg = ConfigTests.Load();
            var k = cfg.tightCoverage;
            Assert.That(k.closeYd, Is.InRange(0f, cfg.catchPlay.contestYd));
            Assert.That(k.pickChance, Is.InRange(0.01f, 0.5f));
            Assert.That(k.inFrontBonus, Is.InRange(0f, 0.5f));
            Assert.That(k.breakupChance, Is.InRange(0.01f, 0.5f));
        }

        [Test]
        public void TheCloserHeIsTheTighterItIs()
        {
            var cfg = ConfigTests.Load();
            var k = cfg.tightCoverage;
            float far = cfg.catchPlay.contestYd;
            var (w, r, d) = JumpBall(cfg, 1, k.closeYd * 0.5f);
            Assert.AreEqual(1f, TightCoverage.Tightness(w, r, d), 1e-5f, "draped on him");
            d.X = far;
            Assert.AreEqual(0f, TightCoverage.Tightness(w, r, d), 1e-5f, "just at jump-ball distance");
            d.X = (k.closeYd + far) * 0.5f;
            Assert.AreEqual(0.5f, TightCoverage.Tightness(w, r, d), 1e-4f, "half way");
            d.X = far + 1f;
            Assert.AreEqual(0f, TightCoverage.Tightness(w, r, d), 1e-5f, "a step off him");
        }

        [Test]
        public void UnderTheBallFirstHeIsLikelierToPickIt()
        {
            var cfg = ConfigTests.Load();
            float off = cfg.tightCoverage.closeYd * 0.5f;
            var (w, r, d) = JumpBall(cfg, 1, off);
            Assert.IsFalse(TightCoverage.InFront(w, r, d));
            TightCoverage.Odds(w, r, d, out float behindPick, out float behindBreakup);
            (w, r, d) = JumpBall(cfg, 1, off, inFront: true);
            Assert.IsTrue(TightCoverage.InFront(w, r, d));
            TightCoverage.Odds(w, r, d, out float frontPick, out float frontBreakup);
            Assert.Greater(frontPick, behindPick, "he jumped the route");
            Assert.AreEqual(behindBreakup, frontBreakup, 1e-6f);
            Assert.LessOrEqual(frontPick + frontBreakup, 1f);
        }

        // Many seeds of the same jump ball: draped on him, the defender wins it a fair share of the time (picks and
        // knock-downs both happen); a step off, never (the catch goes on as before).
        [Test]
        public void ADefenderOnHimPicksAndBreaksUpPassesNowAndThen()
        {
            var cfg = ConfigTests.Load();
            int n = 600, picks = 0, breakups = 0;
            for (int seed = 1; seed <= n; seed++)
            {
                var (w, r, d) = JumpBall(cfg, seed, cfg.tightCoverage.closeYd * 0.5f);
                var res = TightCoverage.Resolve(w, r, d);
                if (res == TightCoverage.Result.Picked) { picks++; Assert.AreEqual(d.Slot, w.Ball.Carrier); }
                if (res == TightCoverage.Result.BrokenUp) { breakups++; Assert.AreNotEqual(BallState.Flying, w.Ball.State); }
            }
            TestContext.WriteLine("draped on him: " + picks + " picks, " + breakups + " broken up of " + n);
            Assert.That(picks / (float)n, Is.InRange(0.06f, 0.35f), "picks");
            Assert.That(breakups / (float)n, Is.InRange(0.1f, 0.35f), "broken up");
            for (int seed = 1; seed <= 50; seed++)
            {
                var (w, r, d) = JumpBall(cfg, seed, cfg.catchPlay.contestYd + 0.2f);
                Assert.AreEqual(TightCoverage.Result.None, TightCoverage.Resolve(w, r, d), "a step off him: his ball to catch");
            }
        }

        // Through the catch step itself (LivePlay.Step): a sure pick is INTERCEPTED, a sure knock-down INCOMPLETE, and
        // with NO_TIGHT_COVERAGE=1 the receiver just goes up for it as before.
        [Test]
        public void TheCatchStepUsesIt()
        {
            System.Func<System.Action<GameConfig>, World> run = tune =>
            {
                var cfg = ConfigTests.Load();
                cfg.features["intreturn"] = false;   // (the whistle at the pick, so the result is plain)
                cfg.features["ratings"] = false;     // (plain hands and a dry ball: a sure pick is a sure pick)
                cfg.features["weather"] = false; cfg.features["sandField"] = false;
                tune(cfg);
                var (w, _, _) = JumpBall(cfg, 3, cfg.tightCoverage.closeYd * 0.5f);
                LivePlay.Step(w);
                return w;
            };
            var picked = run(c => { c.tightCoverage.pickChance = 1f; c.tightCoverage.breakupChance = 0f; });
            Assert.AreEqual("INTERCEPTED", picked.Result);
            Assert.AreEqual(1, picked.Events.Total(EventKind.Interception));
            var broken = run(c => { c.tightCoverage.pickChance = 0f; c.tightCoverage.breakupChance = 1f; });
            Assert.AreEqual("INCOMPLETE", broken.Result, "knocked to the grass");
            var off = run(c => { c.features["tightCoverage"] = false; c.play.catchChanceOffense = 1f; c.play.catchChanceDefense = 0f; });
            Assert.AreEqual(0, off.Events.Total(EventKind.Interception));
            Assert.IsTrue(off.CatchTried.Contains(0), "the receiver went up for it");
            Assert.IsFalse(off.CatchTried.Contains(1), "and the defender never got a go of his own");
        }
    }
}
