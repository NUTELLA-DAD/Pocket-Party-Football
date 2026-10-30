using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.tutorial: the first-drive tutorial (Config/tutorial.json, Sim/Tutorial.cs): its steps set up as lab
    // drills, nothing counts before Begin, a goal met shows NICE and moves on, too many tries move on anyway.
    public sealed class TutorialTests
    {
        static readonly string[] Goals = { "complete", "gain", "juke", "tackle", "play" };

        static Tutorial Make(System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            tune?.Invoke(cfg);
            var w = new World(cfg, 1);
            var t = new Tutorial(w, Tutorial.Parse(File.ReadAllText("Assets/_Project/Config/tutorial.json")));
            t.Setup();
            return t;
        }

        static void Tick(Tutorial t, PlayerInput you = default)
        {
            var input = new Dictionary<int, PlayerInput>();
            if (t.Lab.You >= 0) input[t.Lab.You] = you;
            t.W.Step(input);
            t.Lab.AfterTick();
            t.AfterTick();
        }

        static int Secs(Tutorial t, float s) => (int)(s * t.W.Cfg.sim.tickHz);

        [Test]
        public void TheSwitchIsOnAndEveryStepIsAPlayableDrill()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("tutorial"));
            Assert.AreEqual("NO_TUTORIAL", ConfigLoader.EnvName("tutorial"));
            var t = Make();
            Assert.IsTrue(t.Ready);
            Assert.AreEqual(4, t.StepCount);
            Assert.Greater(t.C.giveUpReps, 0);
            Assert.Greater(t.C.doneS, 0f);
            for (int i = 0; i < t.StepCount; i++)
            {
                var s = t.C.steps[i];
                CollectionAssert.Contains(Goals, s.goal, s.id);
                Assert.IsFalse(string.IsNullOrEmpty(s.title), s.id + " has a title");
                Assert.IsFalse(string.IsNullOrEmpty(s.text), s.id + " says what to press");
                Assert.AreNotEqual("none", s.drill.you, s.id + ": you play in every step");
                t.Lab.Pick(i);
                var w = t.W;
                Assert.IsNotNull(Huddle.Find(w, s.drill.offensePlay), s.id + ": offense play " + s.drill.offensePlay);
                Assert.IsNotNull(Huddle.Find(w, s.drill.defensePlay), s.id + ": defense play " + s.drill.defensePlay);
                var me = w.Find(t.Lab.You);
                Assert.IsNotNull(me, s.id + ": you are on the field");
                Assert.IsTrue(me.Human, s.id);
            }
        }

        [Test]
        public void NothingCountsBeforeBegin()
        {
            var t = Make();
            Assert.IsFalse(t.Started);
            Assert.AreEqual(t.C.introTitle, t.Title);
            for (int i = 0; i < Secs(t, 30f); i++) Tick(t);
            Assert.IsFalse(t.Done);
            Assert.AreEqual(0, t.Step);
        }

        // Step 2 (gain): carry the ball goalYd past the line and the step is done; doneS later step 3 is on the field.
        [Test]
        public void AGoalMetShowsNiceThenTheNextStep()
        {
            var t = Make();
            t.Begin();
            t.Skip();
            for (int i = 0; i <= Secs(t, t.C.doneS) + 1 && t.Step == 0; i++) Tick(t);
            Assert.AreEqual(1, t.Step, "skipped to the run");
            Assert.AreEqual("gain", t.Current.goal);
            Assert.IsFalse(t.Done);
            var w = t.W;
            var me = w.Find(t.Lab.You);
            Assert.AreEqual(w.Ball.Carrier, me.Slot, "you have the ball");
            me.X = w.LineOfScrimmageX + w.Dir * (t.Current.goalYd + 1f);
            Tick(t);
            Assert.IsTrue(t.Done, "10 yards: done");
            Assert.IsFalse(t.GaveUp);
            Assert.AreEqual(t.C.niceText, t.Title);
            for (int i = 0; i <= Secs(t, t.C.doneS) + 1 && t.Step == 1; i++) Tick(t);
            Assert.AreEqual(2, t.Step, "on to the tackle");
            Assert.AreEqual("defense", t.Current.drill.you);
        }

        // Standing still at QB never completes a pass: after giveUpReps tries the step moves on anyway.
        [Test]
        public void TooManyTriesMoveOn()
        {
            var t = Make();
            t.Begin();
            for (int i = 0; i < Secs(t, 300f) && !t.Done; i++) Tick(t);
            Assert.IsTrue(t.Done, "the throw step gave up");
            Assert.IsTrue(t.GaveUp);
            Assert.AreEqual(t.C.moveOnText, t.Title);
        }

        [Test]
        public void TheLastStepFinishesTheTutorial()
        {
            var t = Make();
            t.Begin();
            for (int step = 0; step < t.StepCount; step++)
            {
                t.Skip();
                for (int i = 0; i <= Secs(t, t.C.doneS) + 1 && t.Done; i++) Tick(t);
            }
            Assert.IsTrue(t.Finished);
            Assert.AreEqual(t.C.finishTitle, t.Title);
            Assert.IsFalse(t.ReadyToLeave);
            for (int i = 0; i <= Secs(t, t.C.finishS) + 1; i++) Tick(t);
            Assert.IsTrue(t.ReadyToLeave);
        }
    }
}
