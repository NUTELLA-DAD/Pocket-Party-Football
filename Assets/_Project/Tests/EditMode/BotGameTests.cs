using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-d's gate: 22-bot games play like football. Bots are for "did we break anything?", never "is it fun?", so the
    // ranges are wide; the numbers are printed so a person can look.
    public sealed class BotGameTests
    {
        static World Play(int seed, int minutes)
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg, seed);
            for (int i = 0; i < minutes * 60 * cfg.sim.tickHz; i++) w.Step(null);
            return w;
        }

        static string Report(World w)
        {
            var sb = new StringBuilder();
            sb.Append(w.PlaysRun).Append(" plays, ").Append(w.YardsGained).Append(" yards (")
              .Append((w.YardsGained / (float)System.Math.Max(1, w.PlaysRun)).ToString("0.0")).Append(" a play):");
            foreach (var kv in w.Results) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
            return sb.ToString();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BotGamesLookLikeFootball(int seed)
        {
            var w = Play(seed, 10);
            string r = Report(w);
            TestContext.WriteLine("seed " + seed + ": " + r);
            Assert.GreaterOrEqual(w.PlaysRun, 20, "plays keep happening: " + r);
            float ypp = w.YardsGained / (float)w.PlaysRun;
            Assert.That(ypp, Is.InRange(1.5f, 15f), "yards per play: " + r);
            int Count(string k) => w.Results.TryGetValue(k, out int n) ? n : 0;
            Assert.Less(Count("SACKED"), w.PlaysRun * 0.4f, "the line holds up most of the time: " + r);
            Assert.Less(Count("INCOMPLETE"), w.PlaysRun * 0.6f, "most passes get somewhere: " + r);
            Assert.Less(Count("TOUCHDOWN"), w.PlaysRun * 0.3f, "touchdowns are special: " + r);
            Assert.Less(Count("PLAY OVER"), w.PlaysRun * 0.1f, "plays end on a whistle, not the clock: " + r);
        }

        [Test]
        public void ABlockHoldsARusherUp()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.Calls[w.Offense] = "slants"; w.Calls[w.Defense] = "mblitz";
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(null);
            int held = 0;
            foreach (var p in w.Players) if (p.Team == w.Defense && p.BlockedTicks > 0) held++;
            Assert.Greater(held, 0, "the line engages the rush");
        }

        [Test]
        public void ABotPlaysForAnIdlePerson()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Defense, 500);
            w.Calls[w.Offense] = "slants"; w.Calls[w.Defense] = "cover3";
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            int idle = (int)(cfg.bots.idleTakeoverS * cfg.sim.tickHz);
            bool helped = false;
            for (int i = 0; i < idle + cfg.sim.tickHz && w.Phase == Phase.Live; i++) { w.Step(null); helped |= me.BotHelp; }
            Assert.IsTrue(helped || w.Phase != Phase.Live, "after " + cfg.bots.idleTakeoverS + " s idle a bot plays the spot");
            // touching the stick takes it back
            w.Step(new Dictionary<int, PlayerInput> { { 500, new PlayerInput { MoveX = 1f } } });
            Assert.IsFalse(me.BotHelp);
        }

        [Test]
        public void TheBotQbReadsBeforeHeThrows()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.Calls[w.Offense] = "verts"; w.Calls[w.Defense] = "cover2";
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            int first = (int)(cfg.bots.readTimesS[0] * cfg.sim.tickHz);
            for (int i = 0; i < first - 2; i++) w.Step(null);
            Assert.IsFalse(w.Thrown, "no throw before his first read");
            for (int i = 0; i < cfg.bots.readDeadlineS * cfg.sim.tickHz && !w.Thrown && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.IsTrue(w.Thrown || w.Phase != Phase.Live, "he throws by his deadline (or was sacked first)");
        }
    }
}
