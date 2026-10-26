using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-e: downs and distance, first downs, turnovers, touchdowns and the score.
    public sealed class DownsTests
    {
        static World Fresh()
        {
            var w = WorldTests.Full(ConfigTests.Load());
            w.Step(null);   // the first huddle
            return w;
        }

        // Pretend a play ended: the whistle with this result at spot x (yards along the field).
        static void End(World w, string result, float x)
        {
            w.Phase = Phase.Live;
            LivePlay.Whistle(w, result, x);
            for (int i = 0; i < w.Cfg.play.whistleS * w.Cfg.sim.tickHz + 2 && w.Phase == Phase.Whistle; i++) w.Step(null);
        }

        [Test]
        public void TheGameStartsFirstAndTen()
        {
            var w = Fresh();
            Assert.AreEqual(1, w.Down);
            Assert.AreEqual(w.LineOfScrimmageX + w.Dir * 10f, w.FirstDownX, 1e-3f);
            StringAssert.StartsWith("1ST & 10", Downs.DownText(w));
            CollectionAssert.AreEqual(new[] { 0, 0 }, w.Score);
        }

        [Test]
        public void ShortOfTheLineIsTheNextDown()
        {
            var w = Fresh();
            float los = w.LineOfScrimmageX;
            End(w, "TACKLED", los + w.Dir * 4f);
            Assert.AreEqual(2, w.Down);
            Assert.AreEqual(los + w.Dir * 4f, w.LineOfScrimmageX, 1e-3f);
            Assert.AreEqual("2ND & 6", Downs.DownText(w));
            End(w, "INCOMPLETE", w.LineOfScrimmageX);
            Assert.AreEqual("3RD & 6", Downs.DownText(w));
        }

        [Test]
        public void ReachingTheLineIsAFirstDown()
        {
            var w = Fresh();
            float los = w.LineOfScrimmageX;
            End(w, "TACKLED", los + w.Dir * 12f);
            Assert.AreEqual(1, w.Down);
            Assert.AreEqual("FIRST DOWN", w.Note);
            Assert.AreEqual(los + w.Dir * 22f, w.FirstDownX, 1e-3f);
        }

        [Test]
        public void FourDownsShortIsATurnover()
        {
            var w = Fresh();
            int offense = w.Offense;
            float dir = w.Dir;
            for (int i = 0; i < 4; i++) End(w, "INCOMPLETE", w.LineOfScrimmageX);
            Assert.AreEqual(1 - offense, w.Offense, "the other team has the ball");
            Assert.AreEqual(-dir, w.Dir, "going the other way");
            Assert.AreEqual(1, w.Down);
            Assert.AreEqual("1ST & 10", Downs.DownText(w));
        }

        [Test]
        public void ATouchdownIsSixAndTheOtherTeamStartsAtItsOwn25()
        {
            var w = Fresh();
            int offense = w.Offense;
            float dir = w.Dir, goal = w.Cfg.field.GoalLineX;
            End(w, "TOUCHDOWN", goal * dir + dir);
            Assert.AreEqual(6, w.Score[offense]);
            Assert.AreEqual(1 - offense, w.Offense);
            Assert.AreEqual(dir * (goal - 25f), w.LineOfScrimmageX, 1e-3f, "the new offense starts at its own 25");
            Assert.AreEqual(w.LineOfScrimmageX - dir * 10f, w.FirstDownX, 1e-3f, "first and ten the other way");
        }

        [Test]
        public void AnInterceptionChangesHandsAtTheSpot()
        {
            var w = Fresh();
            int offense = w.Offense;
            End(w, "INTERCEPTED", 5f);
            Assert.AreEqual(1 - offense, w.Offense);
            Assert.AreEqual(5f, w.LineOfScrimmageX, 1e-3f);
        }

        [Test]
        public void InsideTheTenItIsGoalToGo()
        {
            var w = Fresh();
            float goal = w.Cfg.field.GoalLineX;
            End(w, "TACKLED", w.Dir * (goal - 6f));
            Assert.AreEqual("1ST & GOAL", Downs.DownText(w));
        }

        [Test]
        public void BotGamesScoreAndChangeHands()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg, 4);
            int changes = 0, offense = w.Offense;
            for (int i = 0; i < cfg.sim.tickHz * 60 * 15; i++)
            {
                w.Step(null);
                if (w.Offense != offense) { changes++; offense = w.Offense; }
            }
            TestContext.WriteLine("score " + w.Score[0] + "-" + w.Score[1] + ", " + changes + " changes of possession, " + w.PlaysRun + " plays");
            Assert.Greater(changes, 0, "the ball changes hands in 15 minutes");
        }

        [Test]
        public void NoDownsSwitchKeepsRestartingAtTheSpot()
        {
            var w = WorldTests.Full(ConfigTests.Load(new Dictionary<string, string> { { "NO_DOWNS", "1" } }));
            w.Step(null);
            int offense = w.Offense;
            for (int i = 0; i < 6; i++) End(w, "INCOMPLETE", w.LineOfScrimmageX);
            Assert.AreEqual(offense, w.Offense);
        }
    }
}
