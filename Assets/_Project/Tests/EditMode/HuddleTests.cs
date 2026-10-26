using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-b: the secret huddle's rules.
    public sealed class HuddleTests
    {
        const int Qb = 500, Rb = 501, Mlb = 600;

        static (World w, GameConfig cfg) Started(Dictionary<string, string> env = null)
        {
            var cfg = ConfigTests.Load(env);
            var w = WorldTests.Full(cfg);
            w.Step(null);   // the first tick starts the huddle
            return (w, cfg);
        }

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(null); }

        [Test]
        public void TheGameOpensWithAHuddle()
        {
            var (w, cfg) = Started();
            Assert.AreEqual(Phase.Huddle, w.Phase);
            Assert.AreEqual((int)(cfg.huddle.seconds * cfg.sim.tickHz), w.PhaseTicks);
            Assert.IsNull(w.Calls[0]);
            Assert.IsNull(w.Calls[1]);
        }

        [Test]
        public void TheCallerIsTheFirstPersonBySpot()
        {
            var (w, _) = Started();
            Assert.IsNull(Huddle.Caller(w, w.Offense), "all bots: no caller");
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Offense, Rb);
            Assert.AreEqual(Qb, Huddle.Caller(w, w.Offense).Slot, "the QB calls");
            w.ReplaceBot(w.Defense, Mlb);
            Assert.AreEqual("LB", Huddle.Caller(w, w.Defense).Pos, "the middle LB calls");
        }

        [Test]
        public void OnlyTheCallerLocksTeammatesSuggest()
        {
            var (w, _) = Started();
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Offense, Rb);
            Assert.IsFalse(Huddle.Lock(w, Rb, "dive"), "a teammate can't lock");
            Assert.IsTrue(Huddle.Suggest(w, Rb, "dive"));
            Assert.AreEqual(1, Huddle.Thumbs(w, w.Offense)["dive"]);
            Assert.IsTrue(Huddle.Suggest(w, Rb, "slants"), "a new suggestion replaces the old");
            Assert.IsFalse(Huddle.Thumbs(w, w.Offense).ContainsKey("dive"));
            Assert.IsFalse(Huddle.Lock(w, Qb, "cover3"), "not from the other side's playbook");
            Assert.IsFalse(Huddle.Lock(w, Qb, "nope"));
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.IsTrue(Huddle.IsLocked(w, w.Offense));
            Assert.IsFalse(Huddle.Suggest(w, Rb, "dive"), "too late once locked");
            Assert.IsFalse(Huddle.Lock(w, Qb, "dive"), "locked is locked");
            Assert.AreEqual(Phase.Huddle, w.Phase, "the other team hasn't called yet");
        }

        [Test]
        public void BothLockedGoesToTheLineWithJobs()
        {
            var (w, _) = Started();
            var qb = w.ReplaceBot(w.Offense, Qb);
            var mlb = w.ReplaceBot(w.Defense, Mlb);
            Assert.IsNull(Huddle.JobOf(w, qb), "no job in the huddle");
            Huddle.Lock(w, Qb, "slants");
            Huddle.Lock(w, Mlb, "mblitz");
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.AreEqual("slants", w.Calls[w.Offense]);
            Assert.AreEqual("mblitz", w.Calls[w.Defense]);
            Assert.AreEqual("route", Huddle.JobOf(w, qb).kind);
            Assert.AreEqual("rush", Huddle.JobOf(w, mlb).kind, "the middle LB blitzes in Man Blitz");
            foreach (var p in w.Players) Assert.IsNotNull(Huddle.JobOf(w, p), "every player has a job, bots too");
        }

        [Test]
        public void TimeUpTheBotCoachPicksTheMostSuggested()
        {
            var (w, cfg) = Started();
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Offense, Rb);
            Huddle.Suggest(w, Rb, "dive");
            Run(w, (int)(cfg.huddle.seconds * cfg.sim.tickHz));
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.AreEqual("dive", w.Calls[w.Offense], "the caller never locked: the suggestion wins");
            Assert.That(w.Calls[w.Defense], Is.EqualTo("cover3").Or.EqualTo("mblitz"), "the all-bot defense got a play");
        }

        [Test]
        public void AfterTheSetTheNextHuddleStarts()
        {
            var (w, cfg) = Started();
            Run(w, (int)(cfg.huddle.seconds * cfg.sim.tickHz));
            Assert.AreEqual(Phase.Set, w.Phase);
            Run(w, (int)(cfg.huddle.setSeconds * cfg.sim.tickHz));
            Assert.AreEqual(Phase.Huddle, w.Phase);
            Assert.IsNull(w.Calls[0]);
        }

        [Test]
        public void SameSeedSameBotCalls()
        {
            var cfg = ConfigTests.Load();
            var calls = new List<string>[2];
            for (int k = 0; k < 2; k++)
            {
                var w = WorldTests.Full(cfg, 9);
                calls[k] = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    Run(w, (int)((cfg.huddle.seconds + cfg.huddle.setSeconds) * cfg.sim.tickHz));
                    calls[k].Add(w.Calls[0] + "/" + w.Calls[1]);
                }
            }
            CollectionAssert.AreEqual(calls[0], calls[1]);
        }

        [Test]
        public void NoHuddleSwitchTurnsItOff()
        {
            var (w, cfg) = Started(new Dictionary<string, string> { { "NO_HUDDLE", "1" } });
            Run(w, (int)(cfg.huddle.seconds * cfg.sim.tickHz) * 3);
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.IsNull(w.Calls[0]);
        }
    }
}
