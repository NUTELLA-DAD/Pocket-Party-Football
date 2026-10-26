using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-c: hike, pass, catch, run, tackle, the whistle.
    public sealed class LivePlayTests
    {
        const int Me = 500;

        // A world lined up and Set, both plays called, a person at QB. Odds pinned for the test where asked.
        static World AtTheLine(string offensePlay = "slants", string defensePlay = "cover3", System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            tune?.Invoke(cfg);
            var w = WorldTests.Full(cfg);
            w.Step(null);                                  // the first huddle
            w.ReplaceBot(w.Offense, Me);                   // a person at QB
            w.Calls[w.Defense] = defensePlay;              // (the bot defense has called)
            Assert.IsTrue(Huddle.Lock(w, Me, offensePlay));
            Assert.AreEqual(Phase.Set, w.Phase);
            return w;
        }

        static void Run(World w, int ticks, Dictionary<int, PlayerInput> input = null) { for (int i = 0; i < ticks; i++) w.Step(input); }
        static Dictionary<int, PlayerInput> Press(PlayerInput i) => new Dictionary<int, PlayerInput> { { Me, i } };

        static void ClearDefense(World w)
        {
            foreach (var p in w.Players) if (p.Team == w.Defense) { p.X = w.LineOfScrimmageX + w.Cfg.formation.attackDir * 40f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; }
        }

        [Test]
        public void ThePersonAtQbHikes()
        {
            var w = AtTheLine();
            Run(w, 5);
            Assert.AreEqual(Phase.Set, w.Phase, "waits for the QB");
            w.Step(Press(new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.AreEqual(Me, w.Ball.Carrier);
        }

        [Test]
        public void ABotQbHikesByItself()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            for (int i = 0; i < cfg.sim.tickHz * 60 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
        }

        [Test]
        public void PassToTheTriangleIsCaught()
        {
            var w = AtTheLine(tune: c => c.play.catchChanceOffense = 1f);
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            var target = LivePlay.Receiver(w, 1);
            Assert.AreEqual("WR", target.Pos, "the triangle is the first wide receiver");
            Run(w, 20);                                    // let the routes start
            w.Step(Press(new PlayerInput { Throw = 1 }));
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Ball.State == BallState.Flying; i++) w.Step(null);
            Assert.AreEqual(BallState.Held, w.Ball.State, "caught");
            Assert.AreEqual(target.Slot, w.Ball.Carrier);
            Assert.AreEqual(Phase.Live, w.Phase, "the catch keeps the play alive: run!");
        }

        [Test]
        public void ABallNobodyCatchesIsIncomplete()
        {
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 0f; c.play.catchChanceDefense = 0f; });
            float los = w.LineOfScrimmageX;
            w.Step(Press(new PlayerInput { Hike = true }));
            w.Step(Press(new PlayerInput { Throw = 2, Lob = true }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 6 && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("INCOMPLETE", w.Result);
            Assert.AreEqual(los, w.NextLos, "back to the line");
        }

        [Test]
        public void ADefenderCanIntercept()
        {
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 0f; c.play.catchChanceDefense = 1f; });
            w.Step(Press(new PlayerInput { Hike = true }));
            var target = LivePlay.Receiver(w, 1);
            // a defender standing on the receiver
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense) { d = p; break; }
            target.X = w.LineOfScrimmageX + 12f; target.Z = -15f; target.HomeX = target.X; target.HomeZ = target.Z;
            w.Step(Press(new PlayerInput { Throw = 1 }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Phase == Phase.Live; i++)
            {
                d.X = w.Ball.TargetX; d.Z = w.Ball.TargetZ; d.VX = d.VZ = 0f;
                w.Step(null);
            }
            Assert.AreEqual("INTERCEPTED", w.Result);
        }

        [Test]
        public void ABotDefenderTacklesTheCarrier()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 1f);
            w.Step(Press(new PlayerInput { Hike = true }));
            var qb = w.Find(Me);
            // run straight at the defense
            for (int i = 0; i < w.Cfg.sim.tickHz * 10 && w.Phase == Phase.Live; i++) w.Step(Press(new PlayerInput { MoveX = w.Cfg.formation.attackDir }));
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.That(w.Result, Is.EqualTo("TACKLED").Or.EqualTo("SACKED"));
            Assert.AreEqual(qb.X, w.NextLos, 1e-3f, "the next play starts where he went down");
        }

        [Test]
        public void AJukeMakesTheTacklerMiss()
        {
            var w = AtTheLine(tune: c => { c.play.tackleBase = 1f; c.play.tackleFromBehindBonus = 0f; });
            w.Step(Press(new PlayerInput { Hike = true }));
            var qb = w.Find(Me);
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense && !p.Human) { d = p; break; }
            foreach (var p in w.Players) if (p.Team == w.Defense && p != d) { p.X = qb.X + 30f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; }
            d.X = qb.X + 3f; d.Z = qb.Z;
            w.Step(Press(new PlayerInput { Juke = true }));
            Assert.Greater(qb.JukeTicks, 0);
            d.X = qb.X + 0.8f; d.Z = qb.Z; d.TackleCooldown = 0;
            w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase, "juked: the tackle missed");
            Assert.Greater(d.StumbleTicks, 0, "the tackler stumbles");
        }

        [Test]
        public void HbDiveHandsOffToTheBack()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.Calls[w.Offense] = "dive"; w.Calls[w.Defense] = "cover3";
            Run(w, (int)(cfg.huddle.seconds * cfg.sim.tickHz));
            Assert.AreEqual(Phase.Set, w.Phase);
            for (int i = 0; i < cfg.sim.tickHz * 8 && w.Phase != Phase.Live; i++) w.Step(null);
            var rb = LivePlay.SpotOf(w, w.Offense, 1);
            for (int i = 0; i < cfg.sim.tickHz * 3 && w.Ball.Carrier != rb.Slot && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.AreEqual(rb.Slot, w.Ball.Carrier, "the RB took the handoff");
        }

        [Test]
        public void RunningIntoTheEndZoneIsATouchdown()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 0f);
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            var qb = w.Find(Me);
            qb.X = w.Cfg.field.GoalLineX - 1f; qb.Z = 0f;
            Run(w, w.Cfg.sim.tickHz, Press(new PlayerInput { MoveX = w.Cfg.formation.attackDir }));
            Assert.AreEqual("TOUCHDOWN", w.Result);
        }

        [Test]
        public void OutOfBoundsStopsThePlayThere()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 0f);
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 10 && w.Phase == Phase.Live; i++) w.Step(Press(new PlayerInput { MoveZ = -1f }));
            Assert.AreEqual("OUT OF BOUNDS", w.Result);
        }

        [Test]
        public void AfterTheWhistleTheNextHuddleStartsAtTheSpot()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 1f);
            w.Step(Press(new PlayerInput { Hike = true }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 10 && w.Phase == Phase.Live; i++) w.Step(Press(new PlayerInput { MoveX = w.Cfg.formation.attackDir }));
            float spot = w.NextLos;
            Run(w, (int)(w.Cfg.play.whistleS * w.Cfg.sim.tickHz) + 1);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            Assert.AreEqual(spot, w.LineOfScrimmageX, 1e-3f);
        }

        [Test]
        public void NoLivePlaySwitchKeepsTheOldLoop()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_LIVEPLAY", "1" } });
            var w = WorldTests.Full(cfg);
            for (int i = 0; i < cfg.sim.tickHz * 60; i++) { w.Step(null); Assert.AreNotEqual(Phase.Live, w.Phase); }
        }
    }
}
