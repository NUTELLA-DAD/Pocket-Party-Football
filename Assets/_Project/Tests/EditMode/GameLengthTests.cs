using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.gameLength: the title's SETTINGS page picks how long a game runs (GameLength.cs).
    public sealed class GameLengthTests
    {
        static int Ticks(GameConfig cfg, float seconds) => (int)(seconds * cfg.sim.tickHz);

        [Test]
        public void TheLengthsLoadAndTheDefaultIsTheFullGame()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(GameLength.On(cfg));
            CollectionAssert.AreEqual(new[] { "quick", "short", "medium", "full" }, cfg.gameLength.lengths.ConvertAll(o => o.id));
            var full = GameLength.Get(cfg, cfg.gameLength.defaultId);
            Assert.AreEqual(cfg.clock.quarters, full.quarters, "the default is game.json's own clock");
            Assert.AreEqual(cfg.clock.quarterS, full.quarterS);
            Assert.AreEqual(cfg.clock.halftimeAfterQuarter, full.halftimeAfterQuarter);
            foreach (var o in cfg.gameLength.lengths)
            {
                Assert.IsFalse(string.IsNullOrEmpty(o.label), o.id + " has a label");
                Assert.GreaterOrEqual(o.quarters, 1, o.id);
                Assert.Greater(o.quarterS, 0f, o.id);
                Assert.LessOrEqual(o.halftimeAfterQuarter, o.quarters, o.id);
            }
        }

        [Test]
        public void StepWrapsRoundAndAnUnknownPickIsTheDefault()
        {
            var cfg = ConfigTests.Load();
            Assert.AreEqual("short", GameLength.Step(cfg, "quick", +1));
            Assert.AreEqual("full", GameLength.Step(cfg, "quick", -1));
            Assert.AreEqual("quick", GameLength.Step(cfg, "full", +1));
            Assert.AreEqual("full", GameLength.Get(cfg, "nonsense").id);
            Assert.AreEqual("full", GameLength.Get(cfg, null).id);
            Assert.AreEqual("quick", GameLength.Get(cfg, "QUICK").id);
        }

        [Test]
        public void ApplySetsTheClock()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(GameLength.Apply(cfg, "quick"));
            Assert.AreEqual(2, cfg.clock.quarters);
            Assert.AreEqual(60f, cfg.clock.quarterS);
            Assert.AreEqual(1, cfg.clock.halftimeAfterQuarter);
        }

        [Test]
        public void TheSwitchOffKeepsGameJsonsClock()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_GAME_LENGTH", "1" } });
            Assert.IsFalse(GameLength.On(cfg));
            Assert.IsFalse(GameLength.Apply(cfg, "quick"));
            Assert.AreEqual(4, cfg.clock.quarters);
            Assert.AreEqual(240f, cfg.clock.quarterS);
        }

        // A quick game: one-minute quarters, halftime after the first, over after the second.
        [Test]
        public void AQuickGameHasHalftimeAfterTheFirstQuarterAndEndsAfterTheSecond()
        {
            var cfg = ConfigTests.Load();
            cfg.features["clock"] = true;
            GameLength.Apply(cfg, "quick");
            var w = WorldTests.Full(cfg, 1);
            w.Step(null);
            Assert.AreEqual(Ticks(cfg, 60f), w.GameClock.TicksLeft, "a one-minute quarter");

            // the first quarter runs out at a whistle: halftime
            for (int i = 0; i < Ticks(cfg, 60f) && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "a snap within a minute");
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(Clock.IsHalftime(w), "halftime after quarter 1");
            Assert.AreEqual(2, w.GameClock.Quarter);
            for (int i = 0; i < Ticks(cfg, cfg.clock.halftimeS) + 2 && Clock.IsHalftime(w); i++) w.Step(null);
            Assert.IsTrue(w.GameClock.SecondHalf, "the second half started");

            // the second (last) quarter runs out: the game is over
            for (int i = 0; i < Ticks(cfg, 60f) && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "a second-half snap within a minute");
            w.GameClock.TicksLeft = 1;
            LivePlay.Whistle(w, "TACKLED", w.LineOfScrimmageX);
            w.Step(null);
            Assert.IsTrue(Clock.IsOver(w), "over after quarter 2");
        }
    }
}
