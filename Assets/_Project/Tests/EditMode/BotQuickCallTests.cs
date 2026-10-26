using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.botQuickCall (Sim/Huddle.cs): a team with no people on it locks its coach's call after bots.coachCallS
    // instead of sitting through the whole huddle; a team with people keeps huddle.seconds; the huddle ends as soon as
    // both teams have locked.
    public sealed class BotQuickCallTests
    {
        static GameConfig Cfg(bool on)
        {
            var cfg = ConfigTests.Load();
            cfg.features["botQuickCall"] = on;
            return cfg;
        }

        static int Ticks(GameConfig c, float s) => (int)(s * c.sim.tickHz);

        // Ticks from the start of the first huddle until it ends.
        static int HuddleLength(World w)
        {
            w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            int n = 1;
            while (w.Phase == Phase.Huddle && n < Ticks(w.Cfg, 60f)) { w.Step(null); n++; }
            return n;
        }

        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.features.ContainsKey("botQuickCall"), "FEATURES.botQuickCall is in game.json");
            Assert.Greater(cfg.bots.coachCallS, 0f);
            Assert.Less(cfg.bots.coachCallS, cfg.huddle.seconds);
            Assert.AreEqual("NO_BOT_QUICK_CALL", ConfigLoader.EnvName("botQuickCall"));
            var on = Cfg(true);
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_BOT_QUICK_CALL" ? "1" : null);
            Assert.IsFalse(on.Feature("botQuickCall"));
        }

        [Test]
        public void AnAllBotHuddleEndsAfterCoachCallS()
        {
            var cfg = Cfg(true);
            int n = HuddleLength(WorldTests.Full(cfg));
            Assert.That(n, Is.InRange(Ticks(cfg, cfg.bots.coachCallS), Ticks(cfg, cfg.bots.coachCallS) + 2), "the coaches call it after coachCallS");
        }

        [Test]
        public void WithTheSwitchOffBotsUseTheWholeHuddle()
        {
            var cfg = Cfg(false);
            int n = HuddleLength(WorldTests.Full(cfg));
            Assert.That(n, Is.InRange(Ticks(cfg, cfg.huddle.seconds) - 1, Ticks(cfg, cfg.huddle.seconds) + 1));
        }

        [Test]
        public void ATeamWithAPersonKeepsTheFullHuddleAndItEndsWhenHeLocks()
        {
            var cfg = Cfg(true);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Offense, 500);   // the QB: the offense's caller
            for (int i = 0; i < Ticks(cfg, cfg.bots.coachCallS) + 2; i++) w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase, "the person's team still has its huddle");
            Assert.IsTrue(Huddle.IsLocked(w, w.Defense), "the all-bot defense locked after coachCallS");
            Assert.IsFalse(Huddle.IsLocked(w, w.Offense), "nobody locked for the person");
            Assert.IsTrue(Huddle.Lock(w, me.Slot, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase, "both locked: the huddle ends at once");
        }

        [Test]
        public void APersonWhoNeverLocksGetsTheCoachAtTimeUp()
        {
            var cfg = Cfg(true);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, 500);
            for (int i = 0; i < Ticks(cfg, cfg.huddle.seconds) + 2 && w.Phase == Phase.Huddle; i++) w.Step(null);
            Assert.AreEqual(Phase.Set, w.Phase, "time up: the coach called it");
            Assert.IsTrue(Huddle.IsLocked(w, w.Offense));
        }
    }
}
