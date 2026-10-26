using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // WS-02: the game clock, four quarters, the stop rules, the halftime side switch, the end of the game.
    // FEATURES.clock ships off (so the bot-game baseline is untouched); these tests turn it on.
    public sealed class ClockTests
    {
        static GameConfig Cfg(float quarterS = 0f)
        {
            var cfg = ConfigTests.Load();
            cfg.features["clock"] = true;
            if (quarterS > 0f) cfg.clock.quarterS = quarterS;
            return cfg;
        }

        static int Ticks(GameConfig cfg, float seconds) => (int)(seconds * cfg.sim.tickHz);

        static bool StepUntil(World w, System.Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks && !done(); i++) w.Step(null);
            return done();
        }

        // Plays on until the next snap.
        static void GoLive(World w)
        {
            Assert.IsTrue(StepUntil(w, () => w.Phase == Phase.Live, Ticks(w.Cfg, 60f)), "a snap within a minute");
        }

        // A fresh 22-bot game with the clock on, one tick in (the clock has started, the first huddle is open).
        static World Fresh(GameConfig cfg, int seed = 1)
        {
            var w = WorldTests.Full(cfg, seed);
            w.Step(null);
            return w;
        }

        // ---- config and the switch ----

        [Test]
        public void TheClockShipsOffBehindItsOwnSwitch()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("clock"), "FEATURES.clock is on in game.json (the merge captain turned it on, baseline re-pinned)");
            Assert.AreEqual("NO_CLOCK", ConfigLoader.EnvName("clock"));
            var on = Cfg();
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_CLOCK" ? "1" : null);
            Assert.IsFalse(on.Feature("clock"), "NO_CLOCK=1 turns it off");

            // off: the game just keeps going, the clock never starts
            var w = WorldTests.Full(on);
            for (int i = 0; i < Ticks(on, 120f); i++) w.Step(null);
            Assert.IsFalse(w.GameClock.Started);
            Assert.IsFalse(Clock.IsOver(w));
            Assert.Greater(w.PlaysRun, 0);
        }

        [Test]
        public void TheClockBlockLoads()
        {
            var c = ConfigTests.Load().clock;
            Assert.AreEqual(4, c.quarters);
            Assert.AreEqual(240f, c.quarterS);
            Assert.AreEqual(2, c.halftimeAfterQuarter);
            Assert.Greater(c.halftimeS, 0f);
            Assert.IsTrue(c.runsBetweenPlays);
            CollectionAssert.IsSubsetOf(new[] { "INCOMPLETE", "OUT OF BOUNDS", "TOUCHDOWN", "PICK SIX" }, c.stopOn);
            CollectionAssert.DoesNotContain(c.stopOn, "TACKLED");
        }

        // ---- running and stopping ----

        [Test]
        public void TheClockWaitsForTheFirstSnap()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            int full = Ticks(cfg, cfg.clock.quarterS);
            Assert.AreEqual(1, w.GameClock.Quarter);
            for (int i = 0; i < Ticks(cfg, 5f); i++) w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            Assert.AreEqual(full, w.GameClock.TicksLeft, "no time comes off in the first huddle");
            Assert.AreEqual("4:00", Scoreboard.ClockText(w));
            GoLive(w);
            Assert.IsTrue(w.GameClock.Running, "the snap starts the clock");
            Assert.AreEqual(full - 1, w.GameClock.TicksLeft);
        }

        [TestCase("INCOMPLETE")]
        [TestCase("OUT OF BOUNDS")]
        [TestCase("TOUCHDOWN")]
        public void TheClockStopsAfterThisAndRestartsAtTheSnap(string result)
        {
            var w = Fresh(Cfg());
            GoLive(w);
            LivePlay.Whistle(w, result, w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsFalse(w.GameClock.Running, "stopped after " + result);
            int left = w.GameClock.TicksLeft;
            int hikes = w.HuddleNumber;
            // the whistle, the next huddle, the line-up: no time comes off
            GoLive(w);
            Assert.Greater(w.HuddleNumber, hikes, "a new huddle came between");
            Assert.IsTrue(w.GameClock.Running, "the snap restarts it");
            Assert.AreEqual(left - 1, w.GameClock.TicksLeft, "only the snap's own tick came off");
        }

        [TestCase("TACKLED")]
        [TestCase("SACKED")]
        public void TheClockRunsOnAfterThis(string result)
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            GoLive(w);
            LivePlay.Whistle(w, result, w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(w.GameClock.Running, "still running after " + result);
            int left = w.GameClock.TicksLeft;
            int n = Ticks(cfg, cfg.play.whistleS) / 2;
            for (int i = 0; i < n; i++) w.Step(null);
            Assert.AreEqual(left - n, w.GameClock.TicksLeft, "time comes off between plays");
        }

        [Test]
        public void AQuarterThatRunsOutMidPlayEndsAtTheWhistle()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            GoLive(w);
            w.GameClock.TicksLeft = 1;
            w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "(a play can't end one tick after the snap)");
            Assert.AreEqual(0, w.GameClock.TicksLeft, "out of time");
            Assert.AreEqual(1, w.GameClock.Quarter, "the play is finished first");
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.AreEqual(2, w.GameClock.Quarter);
            Assert.AreEqual(Ticks(cfg, cfg.clock.quarterS), w.GameClock.TicksLeft, "a full quarter on the clock");
            Assert.IsFalse(w.GameClock.Running, "waiting for the next snap");
            Assert.IsFalse(w.GameClock.SecondHalf, "the 2nd quarter is still the first half");
        }

        // ---- halftime ----

        [Test]
        public void SidesSwitchAtHalf()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            int first = w.Offense;
            float dir0 = Clock.AttackDir(w, 0), dir1 = Clock.AttackDir(w, 1);
            w.GameClock.Quarter = cfg.clock.halftimeAfterQuarter;
            GoLive(w);
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(Clock.IsHalftime(w), "the half is over");
            Assert.AreEqual(cfg.clock.halftimeAfterQuarter + 1, w.GameClock.Quarter);
            StringAssert.StartsWith("HALFTIME", Scoreboard.TvLine(w));
            Assert.AreEqual("HALF", Scoreboard.QuarterText(w));

            // the break: nobody huddles or plays
            int huddles = w.HuddleNumber, plays = w.PlaysRun;
            for (int i = 0; i < Ticks(cfg, cfg.clock.halftimeS) - 2; i++)
            {
                w.Step(null);
                Assert.AreEqual(Phase.Whistle, w.Phase);
            }
            Assert.AreEqual(huddles, w.HuddleNumber);
            Assert.IsTrue(StepUntil(w, () => !Clock.IsHalftime(w), Ticks(cfg, cfg.clock.halftimeS)), "halftime ends");

            // the second half: the teams attack the other ends; the team that started on defense has the ball at its 25
            Assert.IsTrue(w.GameClock.SecondHalf);
            Assert.AreEqual(-dir0, Clock.AttackDir(w, 0), "team 0 switched ends");
            Assert.AreEqual(-dir1, Clock.AttackDir(w, 1), "team 1 switched ends");
            Assert.AreNotEqual(first, w.Offense, "the other team gets the ball");
            float own25 = -w.Dir * (cfg.field.GoalLineX - cfg.downs.startOwnYd);
            Assert.AreEqual(own25, w.LineOfScrimmageX, 1e-3f);
            Assert.AreEqual(1, w.Down);
            StringAssert.StartsWith("1ST & 10", Downs.DownText(w));
            Assert.AreEqual(Phase.Huddle, w.Phase);
            Assert.AreEqual(plays, w.PlaysRun);
            var qb = LivePlay.Qb(w);
            Assert.Less((qb.X - w.LineOfScrimmageX) * w.Dir, 0f, "the QB lines up behind the line, facing the new way");
            Assert.AreEqual(Ticks(cfg, cfg.clock.quarterS), w.GameClock.TicksLeft);
            Assert.IsFalse(w.GameClock.Running, "the clock waits for the second-half snap");
        }

        // ---- the end ----

        [Test]
        public void TheGameEndsAfterTheLastQuarterAndStaysOver()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            w.GameClock.Quarter = cfg.clock.quarters;
            w.GameClock.SecondHalf = true;
            GoLive(w);
            w.Score[0] = 21; w.Score[1] = 14;
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(Clock.IsOver(w));
            Assert.AreEqual(0, Clock.Winner(w));
            Assert.AreEqual("FINAL", Scoreboard.QuarterText(w));
            StringAssert.StartsWith("FINAL", Scoreboard.TvLine(w));
            StringAssert.Contains(cfg.teams[0].name + " WIN", Scoreboard.TvLine(w));
            StringAssert.Contains("21-14", Scoreboard.WinnerText(w));
            int huddles = w.HuddleNumber, plays = w.PlaysRun;
            for (int i = 0; i < Ticks(cfg, 30f); i++) w.Step(null);
            Assert.IsTrue(Clock.IsOver(w));
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual(huddles, w.HuddleNumber, "no more huddles");
            Assert.AreEqual(plays, w.PlaysRun, "no more plays");
            CollectionAssert.AreEqual(new[] { 21, 14 }, w.Score);
        }

        [Test]
        public void TheWinnerHasMorePoints()
        {
            var w = Fresh(Cfg());
            w.Score[0] = 7; w.Score[1] = 13;
            Assert.AreEqual(1, Clock.Winner(w));
            w.Score[0] = 13;
            Assert.AreEqual(-1, Clock.Winner(w), "a tie");
            StringAssert.StartsWith("TIE GAME", Scoreboard.WinnerText(w));
        }

        // The gate: whole 22-bot games with short quarters play all four and stop, sides switched at half.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ABotGameEndsAfterFourQuarters(int seed)
        {
            var cfg = Cfg(60f);
            var w = WorldTests.Full(cfg, seed);
            var quarterAtPlay = new List<int>();
            int stops = 0, restarts = 0, runsOn = 0;
            bool wasRunning = false;
            Phase last = w.Phase;
            float dir0 = 0f;
            for (int i = 0; i < Ticks(cfg, 60f * 60f) && !Clock.IsOver(w); i++)
            {
                var c = w.GameClock;
                int quarterBefore = c.Quarter;
                w.Step(null);
                if (i == 0) dir0 = Clock.AttackDir(w, 0);
                // each team attacks one end all half, the other end all second half
                Assert.AreEqual(c.SecondHalf ? -dir0 : dir0, Clock.AttackDir(w, 0), "team 0's end, tick " + w.Tick);
                // (the game's last whistle stops the clock for good, whatever the result)
                if (last == Phase.Live && w.Phase == Phase.Whistle && c.Quarter == quarterBefore && !c.Over)
                {
                    quarterAtPlay.Add(c.Quarter);
                    bool shouldStop = cfg.clock.stopOn.Contains(w.Result);
                    Assert.AreEqual(!shouldStop, c.Running, "after " + w.Result + " the clock " + (shouldStop ? "stops" : "runs on"));
                    if (shouldStop) stops++; else runsOn++;
                }
                if (w.Phase == Phase.Live && last != Phase.Live && !wasRunning) restarts++;
                wasRunning = c.Running;
                last = w.Phase;
            }
            string report = "seed " + seed + ": " + w.PlaysRun + " plays, score " + w.Score[0] + "-" + w.Score[1] + ", " + stops + " stops, " + runsOn + " run-ons, " + restarts + " restarts";
            TestContext.WriteLine(report);
            Assert.IsTrue(Clock.IsOver(w), "the game ended: " + report);
            Assert.AreEqual(cfg.clock.quarters, w.GameClock.Quarter);
            Assert.AreEqual(0, w.GameClock.TicksLeft);
            Assert.IsTrue(w.GameClock.SecondHalf, "there was a second half");
            for (int q = 1; q <= cfg.clock.quarters; q++) CollectionAssert.Contains(quarterAtPlay, q, "a play in quarter " + q + ": " + report);
            Assert.Greater(runsOn, 0, "some plays let the clock run on: " + report);
            Assert.Greater(restarts, 0, "the snap starts the clock: " + report);
            Assert.AreEqual(Clock.Winner(w), w.Score[0] > w.Score[1] ? 0 : w.Score[1] > w.Score[0] ? 1 : -1);
            int plays = w.PlaysRun;
            for (int i = 0; i < Ticks(cfg, 30f); i++) w.Step(null);
            Assert.AreEqual(plays, w.PlaysRun, "nothing happens after the final whistle");
        }

        [Test]
        public void SameSeedSameClock()
        {
            var cfg = Cfg(60f);
            var a = WorldTests.Full(cfg, 7);
            var b = WorldTests.Full(cfg, 7);
            for (int i = 0; i < Ticks(cfg, 300f); i++) { a.Step(null); b.Step(null); }
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.AreEqual(a.GameClock.Quarter, b.GameClock.Quarter);
            Assert.AreEqual(a.GameClock.TicksLeft, b.GameClock.TicksLeft);
        }
    }
}
