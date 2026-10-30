using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.overtime: a game level after regulation goes to overtime. The toss winner receives, both teams get one
    // possession, then the next score wins. The clock never takes the second team's turn away; mustWin (cup and season
    // games) plays more periods until somebody scores.
    public sealed class OvertimeTests
    {
        static GameConfig Cfg(bool kicking = true)
        {
            var cfg = ConfigTests.Load();
            cfg.features["clock"] = true;
            cfg.features["overtime"] = true;
            cfg.features["kicking"] = kicking;
            return cfg;
        }

        static int Ticks(GameConfig cfg, float seconds) => (int)(seconds * cfg.sim.tickHz);

        static bool StepUntil(World w, System.Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks && !done(); i++) w.Step(null);
            return done();
        }

        static void GoLive(World w)
        {
            Assert.IsTrue(StepUntil(w, () => w.Phase == Phase.Live, Ticks(w.Cfg, 60f)), "a snap within a minute");
        }

        // One play: the next snap, then the whistle with this result at this spot (default: the line of scrimmage).
        static void Play(World w, string result, float? spot = null)
        {
            GoLive(w);
            LivePlay.Whistle(w, result, spot ?? w.LineOfScrimmageX);
            w.Step(null);
        }

        // The last play of regulation ends level at 7-7: the overtime break. Returns the toss winner.
        static int EndRegulationLevel(World w)
        {
            var cfg = w.Cfg;
            w.GameClock.Quarter = cfg.clock.quarters;   // (no SecondHalf: that would bring the second-half kickoff)
            GoLive(w);
            w.Score[0] = 7; w.Score[1] = 7;
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsFalse(Clock.IsOver(w), "a tie goes to overtime");
            Assert.IsTrue(Clock.InOvertime(w));
            return w.GameClock.OtFirst;
        }

        // Through the break to the overtime kickoff, and the kickoff returned: the toss winner has the ball.
        static int ToFirstPossession(World w)
        {
            int first = EndRegulationLevel(w);
            Assert.IsTrue(StepUntil(w, () => !Clock.IsOvertimeBreak(w), Ticks(w.Cfg, w.Cfg.overtime.breakS) + 2), "the break ends");
            Play(w, "TACKLED");   // the kickoff, run back
            Assert.IsFalse(Clock.IsOver(w));
            Assert.AreEqual(OtStage.First, w.GameClock.OtStage);
            Assert.IsTrue(StepUntil(w, () => w.Phase == Phase.Huddle, Ticks(w.Cfg, 10f)), "a huddle after the kickoff");
            Assert.AreEqual(first, w.Offense, "the toss winner has the ball");
            return first;
        }

        static World Fresh(GameConfig cfg, int seed = 1)
        {
            var w = WorldTests.Full(cfg, seed);
            w.Step(null);
            return w;
        }

        // ---- config and the switch ----

        [Test]
        public void OvertimeIsOnBehindItsOwnSwitch()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("overtime"));
            Assert.AreEqual("NO_OVERTIME", ConfigLoader.EnvName("overtime"));
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_OVERTIME" ? "1" : null);
            Assert.IsFalse(cfg.Feature("overtime"));
            var o = ConfigTests.Load().overtime;
            Assert.Greater(o.periodFrac, 0f);
            Assert.Greater(o.minS, 0f);
            Assert.GreaterOrEqual(o.maxS, o.minS);
            Assert.IsFalse(o.mustWin, "party games may still end in a tie after one overtime period");
        }

        [Test]
        public void TheOvertimePeriodFollowsTheGameLength()
        {
            var cfg = ConfigTests.Load();
            var o = cfg.overtime;
            foreach (var len in cfg.gameLength.lengths)
            {
                var c = ConfigTests.Load();
                GameLength.Apply(c, len.id);
                float s = Clock.PeriodS(c);
                Assert.That(s, Is.InRange(o.minS, o.maxS), len.id);
                Assert.AreEqual(System.Math.Min(o.maxS, System.Math.Max(o.minS, len.quarterS * o.periodFrac)), s, 1e-3f, len.id);
            }
            var full = ConfigTests.Load(); GameLength.Apply(full, "full");
            var quick = ConfigTests.Load(); GameLength.Apply(quick, "quick");
            Assert.Greater(Clock.PeriodS(full), Clock.PeriodS(quick), "a longer game, a longer overtime");
        }

        // ---- getting there ----

        [Test]
        public void AWinnerAfterRegulationMeansNoOvertime()
        {
            var w = Fresh(Cfg());
            w.GameClock.Quarter = w.Cfg.clock.quarters;
            GoLive(w);
            w.Score[0] = 10; w.Score[1] = 7;
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(Clock.IsOver(w));
            Assert.IsFalse(Clock.InOvertime(w));
            Assert.AreEqual("FINAL", Scoreboard.QuarterText(w));
        }

        [Test]
        public void SwitchedOffATieStands()
        {
            var cfg = Cfg();
            cfg.features["overtime"] = false;
            var w = Fresh(cfg);
            w.GameClock.Quarter = cfg.clock.quarters;   // (no SecondHalf: that would bring the second-half kickoff)
            GoLive(w);
            w.Score[0] = 7; w.Score[1] = 7;
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(Clock.IsOver(w));
            Assert.AreEqual(-1, Clock.Winner(w));
            StringAssert.StartsWith("TIE GAME", Scoreboard.WinnerText(w));
        }

        [Test]
        public void ATieBringsTheBreakTheTossAndAKickoffToTheTossWinner()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            int first = EndRegulationLevel(w);
            Assert.That(first, Is.InRange(0, 1));
            Assert.IsTrue(Clock.IsOvertimeBreak(w));
            Assert.IsFalse(Clock.IsHalftime(w), "the overtime break is no halftime");
            Assert.AreEqual(cfg.clock.quarters + 1, w.GameClock.Quarter);
            Assert.AreEqual("OT", Scoreboard.QuarterText(w));
            Assert.AreEqual("", Scoreboard.ClockText(w));
            StringAssert.StartsWith("OVERTIME", Scoreboard.TvLine(w));
            StringAssert.Contains(cfg.teams[first].name + " WIN THE TOSS AND RECEIVE", Scoreboard.TvLine(w));

            float dir0 = -w.GameClock.FirstDir[0];   // (EndRegulationLevel skips halftime, so the second-half end is the other one)
            int huddles = w.HuddleNumber;
            for (int i = 0; i < Ticks(cfg, cfg.overtime.breakS) - 2; i++) { w.Step(null); Assert.AreEqual(Phase.Whistle, w.Phase); }
            Assert.AreEqual(huddles, w.HuddleNumber, "nobody plays in the break");
            Assert.IsTrue(StepUntil(w, () => !Clock.IsOvertimeBreak(w), Ticks(cfg, cfg.overtime.breakS)), "the break ends");

            Assert.IsTrue(w.Kick.Kickoff, "overtime opens with a kickoff");
            Assert.AreNotEqual(first, w.Offense, "the toss loser kicks");
            Assert.AreEqual(dir0, Clock.AttackDir(w, 0), "the teams keep their second-half ends");
            Assert.AreEqual(Ticks(cfg, Clock.PeriodS(cfg)), w.GameClock.TicksLeft, "a full overtime period");
            Assert.IsFalse(w.GameClock.Running, "the clock waits for the kick");
        }

        [Test]
        public void WithoutKickingTheTossWinnerStartsAtItsOwn25()
        {
            var cfg = Cfg(kicking: false);
            var w = Fresh(cfg);
            int first = EndRegulationLevel(w);
            Assert.IsTrue(StepUntil(w, () => !Clock.IsOvertimeBreak(w), Ticks(cfg, cfg.overtime.breakS) + 2));
            Assert.AreEqual(first, w.Offense);
            Assert.AreEqual(-w.Dir * (cfg.field.GoalLineX - cfg.downs.startOwnYd), w.LineOfScrimmageX, 1e-3f);
        }

        [Test]
        public void TheTossComesFromTheSeed()
        {
            int a = EndRegulationLevel(Fresh(Cfg(), 5));
            int b = EndRegulationLevel(Fresh(Cfg(), 5));
            Assert.AreEqual(a, b, "same seed, same toss");
            bool zero = false, one = false;
            for (int seed = 1; seed <= 16 && !(zero && one); seed++)
            {
                int t = Clock.Toss(seed, 0);
                zero |= t == 0; one |= t == 1;
            }
            Assert.IsTrue(zero && one, "either team can win the toss");
            Assert.AreNotEqual(Clock.Toss(1, 40), Clock.Toss(1, 41), "a seed-1 game: the toss moves with the plays run");
        }

        // ---- the possessions ----

        [Test]
        public void AFirstPossessionTouchdownStillGivesTheOtherTeamTheBall()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            int first = ToFirstPossession(w), second = first == 0 ? 1 : 0;
            Play(w, "TOUCHDOWN");
            Assert.IsFalse(Clock.IsOver(w), "no sudden death on the first possession");
            Assert.IsTrue(w.Kick.Try, "the try is still the first possession");
            Play(w, "INCOMPLETE");   // the try fails
            Assert.IsFalse(Clock.IsOver(w), "the other team gets its turn");
            Assert.AreEqual(OtStage.Second, w.GameClock.OtStage);
            Play(w, "TACKLED");      // the kickoff, run back
            Play(w, "INTERCEPTED");  // and the second team gives it away
            Assert.IsTrue(Clock.IsOver(w), "both have had the ball and one is ahead");
            Assert.AreEqual(first, Clock.Winner(w));
            Assert.AreEqual("F/OT", Scoreboard.QuarterText(w));
            StringAssert.EndsWith("IN OT", Scoreboard.WinnerText(w));
            StringAssert.Contains(cfg.teams[first].name + " WIN", Scoreboard.WinnerText(w));
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void TheSecondTeamGoingAheadEndsItAtOnce()
        {
            var w = Fresh(Cfg());
            int first = ToFirstPossession(w), second = first == 0 ? 1 : 0;
            Play(w, "INTERCEPTED");   // the first possession ends without a score
            Assert.AreEqual(OtStage.Second, w.GameClock.OtStage);
            Assert.IsFalse(Clock.IsOver(w));
            Play(w, "TOUCHDOWN");
            Assert.IsTrue(Clock.IsOver(w), "ahead on the second possession: no try needed");
            Assert.AreEqual(second, Clock.Winner(w));
            int plays = w.PlaysRun;
            for (int i = 0; i < Ticks(w.Cfg, 30f); i++) w.Step(null);
            Assert.AreEqual(plays, w.PlaysRun, "nothing more is played");
        }

        [Test]
        public void ADefensiveScoreOnTheFirstPossessionEndsIt()
        {
            var w = Fresh(Cfg());
            int first = ToFirstPossession(w), second = first == 0 ? 1 : 0;
            Play(w, "TACKLED", -w.Dir * (w.Cfg.field.GoalLineX + 1f));   // tackled in his own end zone
            Assert.AreEqual("SAFETY", w.Result);
            Assert.IsTrue(Clock.IsOver(w));
            Assert.AreEqual(second, Clock.Winner(w));
        }

        [Test]
        public void LevelAfterBothPossessionsTheNextScoreWins()
        {
            var w = Fresh(Cfg());
            int first = ToFirstPossession(w), second = first == 0 ? 1 : 0;
            Play(w, "TOUCHDOWN"); Play(w, "INCOMPLETE");   // first: a touchdown, the try fails
            Play(w, "TACKLED");                            // the kickoff
            Play(w, "TOUCHDOWN");                          // second: level, the try decides
            Assert.IsFalse(Clock.IsOver(w), "level: the second team tries");
            Play(w, "INCOMPLETE");                         // the try fails too
            Assert.IsFalse(Clock.IsOver(w), "still level after both possessions");
            Assert.AreEqual(OtStage.SuddenDeath, w.GameClock.OtStage);
            Play(w, "TACKLED");                            // the kickoff: the first team has it again
            Assert.IsTrue(StepUntil(w, () => w.Phase == Phase.Huddle, Ticks(w.Cfg, 10f)));
            StringAssert.Contains("NEXT SCORE WINS", Scoreboard.TvLine(w));
            Play(w, "INTERCEPTED");
            Assert.IsFalse(Clock.IsOver(w), "a turnover is no score");
            Play(w, "TOUCHDOWN");
            Assert.IsTrue(Clock.IsOver(w), "the next score wins, no try");
            Assert.AreEqual(second, Clock.Winner(w));
        }

        [Test]
        public void ATwoPointTryThatGoesAheadOnTheSecondPossessionWins()
        {
            var w = Fresh(Cfg());
            int first = ToFirstPossession(w), second = first == 0 ? 1 : 0;
            Play(w, "TOUCHDOWN"); Play(w, "INCOMPLETE");
            Play(w, "TACKLED");
            Play(w, "TOUCHDOWN");
            Play(w, "TOUCHDOWN");   // the try run in: two points
            Assert.IsTrue(Clock.IsOver(w));
            Assert.AreEqual(second, Clock.Winner(w));
        }

        // ---- the clock ----

        [Test]
        public void TheClockNeverTakesTheSecondTeamsTurnAway()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            ToFirstPossession(w);
            GoLive(w);
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            for (int i = 0; i < 3; i++) w.Step(null);
            Assert.AreEqual(0, w.GameClock.TicksLeft);
            Assert.IsFalse(Clock.IsOver(w), "out of time on the first possession: play on");
            Play(w, "INTERCEPTED");
            Assert.AreEqual(OtStage.Second, w.GameClock.OtStage);
            Assert.IsFalse(Clock.IsOver(w), "the second team still gets the ball");
            Play(w, "INTERCEPTED");   // no score: level after both, and no time left
            Assert.IsTrue(Clock.IsOver(w), "a party game's overtime can end level");
            Assert.AreEqual(-1, Clock.Winner(w));
            StringAssert.StartsWith("TIE GAME", Scoreboard.WinnerText(w));
            Assert.AreEqual(cfg.clock.quarters + 1, w.GameClock.Quarter, "one overtime period");
        }

        [Test]
        public void ACupOrSeasonGamePlaysAnotherPeriodUntilSomebodyScores()
        {
            var cfg = Cfg();
            cfg.overtime.mustWin = true;
            var w = Fresh(cfg);
            ToFirstPossession(w);
            Play(w, "INTERCEPTED");
            Play(w, "INTERCEPTED");
            Assert.AreEqual(OtStage.SuddenDeath, w.GameClock.OtStage);
            GoLive(w);
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsFalse(Clock.IsOver(w), "no ties: another period");
            Assert.AreEqual(cfg.clock.quarters + 2, w.GameClock.Quarter);
            Assert.AreEqual("OT2", Scoreboard.QuarterText(w));
            Assert.AreEqual(Ticks(cfg, Clock.PeriodS(cfg)), w.GameClock.TicksLeft);
            int scorer = w.PendingTurnover ? w.Defense : w.Offense;
            Assert.IsTrue(StepUntil(w, () => w.Phase == Phase.Huddle, Ticks(cfg, 10f)));
            Play(w, "TOUCHDOWN");
            Assert.IsTrue(Clock.IsOver(w));
            Assert.AreEqual(scorer, Clock.Winner(w));
        }

        [Test]
        public void ATouchdownAsTimeRunsOutStillGetsItsTry()
        {
            var cfg = Cfg();
            var w = Fresh(cfg);
            w.GameClock.Quarter = cfg.clock.quarters;
            Play(w, "TACKLED");   // the opening kickoff, run back: the touchdown must come on a scrimmage play
            GoLive(w);
            w.Score[0] = 0; w.Score[1] = 0;
            int scorer = w.Offense;
            w.Score[scorer == 0 ? 1 : 0] = 7;
            LivePlay.Whistle(w, "TOUCHDOWN", w.LineOfScrimmageX);   // 6-7 as time runs out
            w.GameClock.TicksLeft = 0;
            w.Step(null);
            Assert.IsFalse(Clock.IsOver(w), "the try comes first");
            Assert.IsTrue(w.Kick.Try);
            Play(w, "TOUCHDOWN");   // two points
            Assert.IsTrue(Clock.IsOver(w));
            Assert.IsFalse(Clock.InOvertime(w));
            Assert.AreEqual(scorer, Clock.Winner(w));
        }

        // The gate: whole 22-bot quick games, held level to the end of regulation, play overtime to a winner when
        // they must (a cup or season game).
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ABotCupGameLevelAfterRegulationEndsWithAWinner(int seed)
        {
            var cfg = Cfg();
            GameLength.Apply(cfg, "quick");
            cfg.overtime.mustWin = true;
            var w = WorldTests.Full(cfg, seed);
            int hz = cfg.sim.tickHz;
            // a few plays in, between plays (no try pending): call it the end of regulation, level
            Assert.IsTrue(StepUntil(w, () => w.PlaysRun >= 10 && w.Phase == Phase.Whistle && !w.Kick.Try && w.GameClock.BreakTicks == 0, hz * 60 * 30));
            w.GameClock.Quarter = cfg.clock.quarters;
            w.GameClock.TicksLeft = 0;
            w.Score[1] = w.Score[0];
            w.Step(null);
            Assert.IsTrue(Clock.InOvertime(w), "level at the end: overtime");
            StepUntil(w, () => Clock.IsOver(w), hz * 60 * 60);
            string s = "seed " + seed + ": " + w.Score[0] + "-" + w.Score[1] + " after " + w.PlaysRun + " plays, quarter " + w.GameClock.Quarter;
            TestContext.WriteLine(s);
            Assert.IsTrue(Clock.IsOver(w), "the game ended: " + s);
            Assert.IsTrue(Clock.InOvertime(w), "it went to overtime: " + s);
            Assert.GreaterOrEqual(Clock.Winner(w), 0, "a winner: " + s);
            StringAssert.EndsWith("IN OT", Scoreboard.WinnerText(w));
        }
    }
}
