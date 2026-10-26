using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // The practice lab (Config/lab.json): every drill sets up, runs on the game's own rules and starts again
    // after the whistle; the log says what happened.
    public sealed class LabTests
    {
        static Lab Make(string drill, System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            tune?.Invoke(cfg);
            var w = new World(cfg, 1);
            var lab = new Lab(w, Lab.Parse(File.ReadAllText("Assets/_Project/Config/lab.json")));
            int i = lab.FindDrill(drill);
            Assert.GreaterOrEqual(i, 0, "drill " + drill + " is in lab.json");
            lab.Pick(i);
            return lab;
        }

        static void Tick(Lab lab, PlayerInput you = default)
        {
            var input = new Dictionary<int, PlayerInput>();
            if (lab.You >= 0) input[lab.You] = you;
            lab.W.Step(input);
            lab.AfterTick();
        }

        static int Secs(Lab lab, float s) => (int)(s * lab.W.Cfg.sim.tickHz);

        [Test]
        public void EveryDrillSetsUpWithYouInIt()
        {
            var lab = Make("throw");
            for (int i = 0; i < lab.C.drills.Count; i++)
            {
                lab.Pick(i);
                var d = lab.Drill;
                var w = lab.W;
                Assert.AreEqual(d.offense.Count + d.defense.Count, w.Players.Count, d.id + ": the drill's players and nobody else");
                Assert.IsNotNull(Huddle.Find(w, d.offensePlay), d.id + ": offense play " + d.offensePlay);
                Assert.IsNotNull(Huddle.Find(w, d.defensePlay), d.id + ": defense play " + d.defensePlay);
                Assert.AreEqual(BallState.Held, w.Ball.State, d.id + ": somebody has the ball");
                Assert.AreEqual(LivePlay.SpotOf(w, w.Offense, d.carrier).Slot, w.Ball.Carrier, d.id + ": the right player has the ball");
                Assert.AreEqual(d.live ? Phase.Live : Phase.Set, w.Phase, d.id);
                if (d.you == "none") { Assert.AreEqual(-1, lab.You); foreach (var p in w.Players) Assert.IsFalse(p.Human, d.id + ": bots only"); continue; }
                var me = w.Find(lab.You);
                Assert.IsNotNull(me, d.id + ": you are on the field");
                Assert.IsTrue(me.Human);
                Assert.AreEqual(d.you == "offense" ? w.Offense : w.Defense, me.Team, d.id);
                Assert.AreEqual(d.youSpot, me.Spot, d.id);
                int humans = 0;
                foreach (var p in w.Players) if (p.Human) humans++;
                Assert.AreEqual(1, humans, d.id + ": only you are a person");
            }
        }

        [Test]
        public void QbDrillThrowCaughtThenItRunsAgain()
        {
            var lab = Make("throw", c => c.play.catchChanceOffense = 1f);
            var w = lab.W;
            Assert.AreEqual(Phase.Set, w.Phase, "waits for your snap");
            Tick(lab, new PlayerInput { Hike = true });
            Assert.AreEqual(Phase.Live, w.Phase);
            Tick(lab, new PlayerInput { Throw = 1 });      // (at once: bots stop their route when the ball is in the air)
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            for (int i = 0; i < Secs(lab, 4f) && w.Phase == Phase.Live; i++) Tick(lab);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("CAUGHT", w.Result, "a catch ends the throwing drill");
            Assert.That(string.Join("\n", lab.Log), Does.Contain("CAUGHT by WR #11"));
            Assert.That(string.Join("\n", lab.Log), Does.Contain("Bullet by QB #9 (you)"));
            for (int i = 0; i < Secs(lab, w.Cfg.play.whistleS + 1f) && w.Phase != Phase.Set; i++) Tick(lab);
            Assert.AreEqual(Phase.Set, w.Phase, "the drill lines up again");
            Assert.AreEqual(1, lab.Rep);
            Assert.AreEqual(lab.You, w.Ball.Carrier, "the ball is back with you");
        }

        [Test]
        public void CatchDrillThrowsABulletThenALob()
        {
            var lab = Make("catch", c => c.play.catchChanceOffense = 1f);
            var w = lab.W;
            var kinds = new List<bool>();
            for (int rep = 0; rep < 2; rep++)
            {
                Assert.AreEqual(rep, lab.Rep);
                for (int i = 0; i < Secs(lab, 12f) && lab.Rep == rep; i++)
                {
                    var was = w.Ball.State;
                    Tick(lab);
                    if (was == BallState.Held && w.Ball.State == BallState.Flying) kinds.Add(w.Ball.VY > 8f);   // (a lob leaves at about 12 yd/s upward)
                    if (w.Phase == Phase.Whistle) Assert.AreEqual("CAUGHT", w.Result, "standing still, the throw comes to you");
                }
            }
            CollectionAssert.AreEqual(new[] { false, true }, kinds, "a bullet, then a lob");
        }

        [Test]
        public void TackleDrillTackleInReachIsLogged()
        {
            // (you are the only defender: no bot tries; the carrier's spin, stiff arm and dive do not help him and nothing is fumbled, so in reach is a sure tackle)
            var lab = Make("tackle", c => { c.play.tackleBase = 1f; c.carrier.spinTackleMul = c.carrier.stiffArmTackleMul = c.carrier.diveTackleMul = 1f; c.carrier.fumbleChance = c.carrier.fumbleBlindsideBonus = 0f; });
            var w = lab.W;
            for (int i = 0; i < Secs(lab, 5f) && w.Phase == Phase.Live; i++)
            {
                var me = w.Find(lab.You);
                var k = LivePlay.Carrier(w);
                float dx = k.X - me.X, dz = k.Z - me.Z, d = (float)System.Math.Sqrt(dx * dx + dz * dz);
                bool reach = d <= Lab.TackleReach(w, me);
                Tick(lab, new PlayerInput { MoveX = dx / d, MoveZ = dz / d, Sprint = true, Tackle = reach });
            }
            Assert.AreEqual("TACKLED", w.Result);
            Assert.That(string.Join("\n", lab.Log), Does.Contain("TACKLED by LB #54 (you)"));
        }

        [Test]
        public void JukeDrillDefendersChaseYou()
        {
            var lab = Make("juke");
            var w = lab.W;
            var me = w.Find(lab.You);
            Assert.AreEqual(lab.You, w.Ball.Carrier, "you have the ball");
            float start = LivePlay.Dist(lab.NearestTackler(), me);
            for (int i = 0; i < Secs(lab, 1f); i++) Tick(lab);
            Assert.Less(LivePlay.Dist(lab.NearestTackler(), w.Find(lab.You)), start, "they come after you");
            Tick(lab, new PlayerInput { Juke = true, MoveZ = 1f });
            Assert.That(string.Join("\n", lab.Log), Does.Contain("Juke by RB #21 (you)"));
        }

        [Test]
        public void WatchDrillPlaysAWholeDownByItself()
        {
            var lab = Make("watch");
            var w = lab.W;
            bool live = false;
            for (int i = 0; i < Secs(lab, 30f) && lab.Rep == 0; i++) { Tick(lab); live |= w.Phase == Phase.Live; }
            Assert.IsTrue(live, "the bot QB hikes");
            Assert.AreEqual(1, lab.Rep, "after the whistle the drill starts again");
        }

        [Test]
        public void TackleOddsMatchTheGame()
        {
            var lab = Make("tackle");
            var w = lab.W;
            var me = w.Find(lab.You);
            var k = LivePlay.Carrier(w);
            me.VX = me.VZ = 0f;
            Assert.AreEqual(w.Cfg.play.tackleBase, Lab.TackleOdds(w, me, k), 1e-5f, "standing still, head on: the base odds");
            Assert.AreEqual(w.Cfg.play.tackleReachYd * 1.5f, Lab.TackleReach(w, me), 1e-5f, "a person's TACKLE reaches half as far again");
        }
    }
}
