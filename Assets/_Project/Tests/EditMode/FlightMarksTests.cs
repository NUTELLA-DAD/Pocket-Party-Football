using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // The lab's helpers in the game (Sim/FlightMarks.cs, drawn by Boot/FlightView.cs): the pass's flight line, the
    // landing and catch rings, and the tackle reach ring under people on defense. Each has its own switch.
    public sealed class FlightMarksTests
    {
        static readonly string[] Switches = { "flightLine", "landingRing", "reachRing" };

        static GameConfig Cfg(bool on = true)
        {
            var cfg = ConfigTests.Load();
            foreach (var s in Switches) cfg.features[s] = on;
            cfg.features["kicking"] = false;
            return cfg;
        }

        // A live run: the offense's spot-0 player carries, everyone else stands far away; one person on defense.
        static World Run(GameConfig cfg, out Player carrier, out Player me)
        {
            var w = new World(cfg, 1);
            carrier = w.AddPlayer(1, w.Offense, false, 0);
            me = w.AddPlayer(2, w.Defense, true, 0);
            w.AddPlayer(3, w.Defense, false, 1);
            w.LineUp();
            w.Ball.GiveTo(carrier, cfg.ball);
            w.Phase = Phase.Live;
            carrier.X = 0f; carrier.Z = 0f;
            return w;
        }

        [Test]
        public void TheSwitchesAreInGameJsonWithTheirEnvOverrides()
        {
            var cfg = ConfigTests.Load();
            foreach (var s in Switches) Assert.IsTrue(cfg.features.ContainsKey(s) && cfg.features[s], "FEATURES." + s + " is in game.json and on");
            Assert.AreEqual("NO_FLIGHT_LINE", ConfigLoader.EnvName("flightLine"));
            Assert.AreEqual("NO_LANDING_RING", ConfigLoader.EnvName("landingRing"));
            Assert.AreEqual("NO_REACH_RING", ConfigLoader.EnvName("reachRing"));
            var on = Cfg();
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_FLIGHT_LINE" || k == "NO_LANDING_RING" || k == "NO_REACH_RING" ? "1" : null);
            var w = new World(on, 1);
            Assert.IsFalse(FlightMarks.LineOn(w) || FlightMarks.LandingOn(w) || FlightMarks.ReachOn(w), "NO_...=1 turns each off");
        }

        [Test]
        public void TheReachRingShowsUnderAPersonOnDefenseAndGoesBoldInReach()
        {
            var w = Run(Cfg(), out var k, out var me);
            float reach = LivePlay.TackleReach(w, me);
            me.X = reach + 2f; me.Z = 0f;
            Assert.AreEqual(FlightMarks.Reach.Out, FlightMarks.TackleRing(w, me), "out of reach: the thin ring");
            me.X = reach - 0.1f;
            Assert.AreEqual(FlightMarks.Reach.In, FlightMarks.TackleRing(w, me), "in reach: the bold ring");
            Assert.AreEqual(FlightMarks.Reach.None, FlightMarks.TackleRing(w, w.Find(3)), "never under a bot");
            Assert.AreEqual(FlightMarks.Reach.None, FlightMarks.TackleRing(w, k), "never under the carrier's own side");
        }

        [Test]
        public void TheReachRingMatchesWhatTheTackleButtonReaches()
        {
            var w = Run(Cfg(), out var k, out var me);
            Assert.AreEqual(w.Cfg.play.tackleReachYd * 1.5f, LivePlay.TackleReach(w, me), 1e-5f, "a person reaches half as far again");
            Assert.AreEqual(w.Cfg.play.tackleReachYd, LivePlay.TackleReach(w, w.Find(3)), 1e-5f, "a bot's own reach");
            Assert.AreEqual(LivePlay.TackleReach(w, me), Lab.TackleReach(w, me), 1e-5f, "the lab and the game agree");
        }

        [Test]
        public void NoReachRingOutsideALiveRunOrWithTheSwitchOff()
        {
            var w = Run(Cfg(), out var k, out var me);
            me.X = 0.5f; me.Z = 0f;
            w.Phase = Phase.Whistle;
            Assert.AreEqual(FlightMarks.Reach.None, FlightMarks.TackleRing(w, me), "after the whistle");
            w.Phase = Phase.Live;
            w.Throw(20f, 0f, true);
            Assert.AreEqual(FlightMarks.Reach.None, FlightMarks.TackleRing(w, me), "nobody carries a pass in the air");
            var off = Run(Cfg(on: false), out _, out var me2);
            me2.X = 0.5f; me2.Z = 0f;
            Assert.AreEqual(FlightMarks.Reach.None, FlightMarks.TackleRing(off, me2), "switched off");
        }

        [Test]
        public void TheFlightLineRunsFromTheBallToTheGrassAndMatchesTheRealFlight()
        {
            var w = Run(Cfg(), out _, out _);
            w.Throw(20f, 3f, true);
            Assert.IsTrue(FlightMarks.Flying(w));
            var x = new float[ArcRibbonMax]; var y = new float[ArcRibbonMax]; var z = new float[ArcRibbonMax];
            int n = FlightMarks.Path(w, x, y, z);
            Assert.Greater(n, 2, "a line to draw");
            Assert.AreEqual(w.Ball.X, x[0], 1e-4f); Assert.AreEqual(w.Ball.Y, y[0], 1e-4f); Assert.AreEqual(w.Ball.Z, z[0], 1e-4f);
            for (int i = 0; i < n; i++) Assert.GreaterOrEqual(y[i], 0f, "never under the grass");
            Assert.AreEqual(0f, y[n - 1], 1e-5f, "it ends on the grass");
            // the real flight lands on the line's points, tick by tick (the line is the flight the sim will fly)
            for (int i = 1; i <= 10 && i < n && w.Ball.State == BallState.Flying; i++)
            {
                foreach (var p in w.Players) { p.X = -40f; p.Z = 20f; p.VX = p.VZ = 0f; }   // nobody catches it
                w.Step(null);
                if (w.Ball.State != BallState.Flying) break;
                Assert.AreEqual(x[i], w.Ball.X, 1e-3f, "x after " + i + " ticks");
                Assert.AreEqual(z[i], w.Ball.Z, 1e-3f, "z after " + i + " ticks");
                Assert.AreEqual(y[i], w.Ball.Y, 1e-3f, "height after " + i + " ticks");
            }
        }

        [Test]
        public void NoFlightLineWithoutABallInTheAir()
        {
            var w = Run(Cfg(), out _, out _);
            var x = new float[8]; var y = new float[8]; var z = new float[8];
            Assert.AreEqual(0, FlightMarks.Path(w, x, y, z), "the ball is in a hand");
            Assert.IsFalse(FlightMarks.Catchable(w));
        }

        [Test]
        public void TheCatchCircleShowsOnlyOnTheWayDownWithinReach()
        {
            var w = Run(Cfg(), out _, out _);
            w.Throw(25f, 0f, true);
            var b = w.Ball;
            var seen = new List<bool>();
            for (int i = 0; i < w.Cfg.sim.tickHz * 8 && b.State == BallState.Flying; i++)
            {
                bool c = FlightMarks.Catchable(w);
                if (c) { Assert.Less(b.VY, 0f, "coming down"); Assert.LessOrEqual(b.Y, w.Cfg.play.catchMaxHeightYd); }
                seen.Add(c);
                // keep it from being caught or swatted: it flies on to the grass
                foreach (var p in w.Players) { p.X = -40f; p.Z = 20f; p.VX = p.VZ = 0f; }
                w.Step(null);
            }
            Assert.IsFalse(seen[0], "not as it leaves the hand going up");
            Assert.Contains(true, seen, "catchable before it lands");
        }

        const int ArcRibbonMax = 160;
    }
}
