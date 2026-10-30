using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.afterPlay (Sim/AfterPlay.cs): after the whistle each man pulls up and huffs, high-fives a teammate,
    // walks back, wanders off or lingers; the huddle keeps everyone where he stood and they walk to their new spots.
    public sealed class AfterPlayTests
    {
        static GameConfig Cfg(bool on)
        {
            var cfg = ConfigTests.Load();
            cfg.features["afterPlay"] = on;
            cfg.features["botQuickCall"] = false;   // (the whole huddle, so there is time to walk back)
            return cfg;
        }

        static int Ticks(GameConfig c, float s) => (int)(s * c.sim.tickHz);

        static void StepUntil(World w, System.Func<bool> done, float maxS)
        {
            for (int i = 0; i < Ticks(w.Cfg, maxS) && !done(); i++) w.Step(null);
            Assert.IsTrue(done(), "it happened within " + maxS + " s");
        }

        // A 22-bot game run to the end of its first play, one tick into the whistle (the moods are drawn).
        static World AtWhistle(bool on, int seed = 1)
        {
            var w = WorldTests.Full(Cfg(on), seed);
            StepUntil(w, () => w.Phase == Phase.Live, 60f);
            StepUntil(w, () => w.Phase == Phase.Whistle, w.Cfg.play.maxPlayS + 5f);
            w.Step(null);
            return w;
        }

        static float Dist(float ax, float az, float bx, float bz) => (float)System.Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        [Test]
        public void ItShipsOffBehindItsSwitchAndItsEnvOverride()
        {
            var shipped = ConfigTests.Load(ConfigTests.AsShipped);
            Assert.IsTrue(shipped.features.ContainsKey("afterPlay"), "FEATURES.afterPlay is in game.json");
            Assert.IsFalse(shipped.Feature("afterPlay"), "a new sim rule ships off (no re-pin from a cloud thread)");
            Assert.AreEqual("NO_AFTER_PLAY", ConfigLoader.EnvName("afterPlay"));
            var on = Cfg(true);
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_AFTER_PLAY" ? "1" : null);
            Assert.IsFalse(on.Feature("afterPlay"));
            var c = shipped.afterPlay;
            Assert.AreEqual(5, c.weights.Length, "one weight per mood: Huff, HighFive, WalkBack, Wander, Linger");
            Assert.LessOrEqual(c.reactMinS, c.reactMaxS);
            Assert.LessOrEqual(c.walkFracMin, c.walkFracMax);
            Assert.Less(c.walkFracMax, 1f, "a walk, slower than a jog");
            Assert.Less(c.homeByS, shipped.huddle.seconds);
        }

        [Test]
        public void OffEveryoneIsOnHisSpotAsSoonAsTheHuddleStarts()
        {
            var w = AtWhistle(false);
            Assert.AreEqual(AfterMood.None, AfterPlay.MoodOf(w, 0, out _));
            StepUntil(w, () => w.Phase == Phase.Huddle, w.Cfg.play.whistleS + 1f);
            foreach (var p in w.Players) Assert.AreEqual(0f, Dist(p.X, p.Z, p.HomeX, p.HomeZ), 1e-3f, "slot " + p.Slot + " lined up");
        }

        [Test]
        public void EveryoneGetsAMoodAndTheyVary()
        {
            var w = AtWhistle(true);
            var moods = new HashSet<AfterMood>();
            foreach (var p in w.Players)
            {
                var m = AfterPlay.MoodOf(w, p.Slot, out var r);
                Assert.AreNotEqual(AfterMood.None, m, "slot " + p.Slot + " has something to do");
                moods.Add(m);
                if (m != AfterMood.HighFive) continue;
                // high fives come in pairs of teammates
                var mate = w.Find(r.Partner);
                Assert.IsNotNull(mate, "slot " + p.Slot + " has a man to slap hands with");
                Assert.AreEqual(p.Team, mate.Team);
                Assert.AreEqual(AfterMood.HighFive, AfterPlay.MoodOf(w, mate.Slot, out var mr));
                Assert.AreEqual(p.Slot, mr.Partner);
            }
            Assert.GreaterOrEqual(moods.Count, 3, "not everyone does the same thing");
        }

        [Test]
        public void TheMoodsChangeFromPlayToPlay()
        {
            var w = AtWhistle(true);
            var first = new List<AfterMood>();
            foreach (var p in w.Players) first.Add(AfterPlay.MoodOf(w, p.Slot, out _));
            StepUntil(w, () => w.Phase == Phase.Live, 60f);
            StepUntil(w, () => w.Phase == Phase.Whistle, w.Cfg.play.maxPlayS + 5f);
            w.Step(null);
            var second = new List<AfterMood>();
            foreach (var p in w.Players) second.Add(AfterPlay.MoodOf(w, p.Slot, out _));
            CollectionAssert.AreNotEqual(first, second);
        }

        [Test]
        public void SameSeedSameScatter()
        {
            var a = AtWhistle(true, 7);
            var b = AtWhistle(true, 7);
            for (int i = 0; i < Ticks(a.Cfg, a.Cfg.play.whistleS + 5f); i++) { a.Step(null); b.Step(null); }
            Assert.AreEqual(a.Hash(), b.Hash());
        }

        [Test]
        public void TheHuddleKeepsEveryoneWhereHeStood()
        {
            var w = AtWhistle(true);
            var at = new Dictionary<int, (float x, float z)>();
            while (w.Phase == Phase.Whistle)
            {
                at.Clear();
                foreach (var p in w.Players) at[p.Slot] = (p.X, p.Z);
                w.Step(null);
            }
            Assert.AreEqual(Phase.Huddle, w.Phase);
            float step = w.Cfg.players.sprintSpeedYdS * w.Dt + w.Cfg.players.radiusYd;   // (one tick's walk, and a bump)
            float farthest = 0f;
            foreach (var p in w.Players)
            {
                Assert.LessOrEqual(Dist(p.X, p.Z, at[p.Slot].x, at[p.Slot].z), step, "slot " + p.Slot + " was not snapped to his spot");
                farthest = System.Math.Max(farthest, Dist(p.X, p.Z, p.HomeX, p.HomeZ));
            }
            Assert.Greater(farthest, 2f, "the play left somebody well away from his new spot");
        }

        [Test]
        public void TheyAreAllBackOnTheirSpotsByTheLineUp()
        {
            var w = AtWhistle(true);
            StepUntil(w, () => w.Phase == Phase.Huddle, w.Cfg.play.whistleS + 1f);
            // the last tick of the huddle: the next one lines up for the snap
            StepUntil(w, () => w.PhaseTicks <= 1, w.Cfg.huddle.seconds + 1f);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            foreach (var p in w.Players)
                Assert.LessOrEqual(Dist(p.X, p.Z, p.HomeX, p.HomeZ), 3f, "slot " + p.Slot + " walked back in time");
        }

        [Test]
        public void TheBallCarrierStaysWhereHeWentDownThroughTheWhistle()
        {
            var w = AtWhistle(true);
            for (int plays = 0; plays < 12 && w.Result != "TACKLED" && w.Result != "SACKED"; plays++)
            {
                StepUntil(w, () => w.Phase == Phase.Live, 60f);
                StepUntil(w, () => w.Phase == Phase.Whistle, w.Cfg.play.maxPlayS + 5f);
                w.Step(null);
            }
            Assume.That(w.Result == "TACKLED" || w.Result == "SACKED", "a tackle within a dozen plays");
            var c = LivePlay.Carrier(w);
            Assert.IsNotNull(c);
            AfterPlay.MoodOf(w, c.Slot, out var r);
            Assert.IsTrue(r.Down, "the man tackled is down");
            while (w.Phase == Phase.Whistle)
            {
                var input = AfterPlay.Think(w, c);
                Assert.AreEqual(0f, input.MoveX); Assert.AreEqual(0f, input.MoveZ);
                w.Step(null);
            }
        }

        [Test]
        public void APersonPushingHisStickMovesHimself()
        {
            var cfg = Cfg(true);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Defense, 500);
            StepUntil(w, () => w.Phase == Phase.Live, 60f);
            StepUntil(w, () => w.Phase == Phase.Whistle, cfg.play.maxPlayS + 5f);
            w.Step(null);
            Assert.IsTrue(AfterPlay.Drives(w, me, default), "hands off the phone: walked back like a bot");
            Assert.IsFalse(AfterPlay.Drives(w, me, new PlayerInput { MoveX = 1f }), "his own stick: he goes where he likes");
        }
    }
}
