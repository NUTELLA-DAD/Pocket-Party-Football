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

        // FEATURES.passTarget: the pass is for the man whose receiver button was pressed (the shape over his head on the
        // TV), not whoever happens to be nearest where it comes down
        [Test]
        public void ThePassIsForTheManWhoseButtonWasPressed()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("passTarget"), "FEATURES.passTarget ships on");
            Assert.AreEqual("NO_PASS_TARGET", ConfigLoader.EnvName("passTarget"));
            foreach (bool on in new[] { true, false })
            {
                var w = AtTheLine(tune: c => c.features["passTarget"] = on);
                ClearDefense(w);
                w.Step(Press(new PlayerInput { Hike = true }));
                Run(w, 20);
                var circle = LivePlay.Receiver(w, 2);
                var square = LivePlay.Receiver(w, 3);
                w.Step(Press(new PlayerInput { Throw = 2 }));
                Assert.AreEqual(BallState.Flying, w.Ball.State);
                // the square stands right where it comes down; the circle is somewhere else
                square.X = w.Ball.TargetX; square.Z = w.Ball.TargetZ;
                if (on)
                {
                    Assert.AreEqual(circle.Slot, w.PassTarget);
                    Assert.AreSame(circle, Bots.Intended(w), "the circle's pass: he goes to meet it");
                }
                else Assert.AreSame(square, Bots.Intended(w), "NO_PASS_TARGET=1: the man nearest where it lands");
            }
        }

        [Test]
        public void TheManThePassIsForCatchesItAheadOfATeammate()
        {
            foreach (bool on in new[] { true, false })
            {
                var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 1f; c.features["passTarget"] = on; });
                ClearDefense(w);
                w.Step(Press(new PlayerInput { Hike = true }));
                Run(w, 20);
                var circle = LivePlay.Receiver(w, 2);
                var square = LivePlay.Receiver(w, 3);
                w.Step(Press(new PlayerInput { Throw = 2 }));
                // both under the ball as it comes down, the square a little nearer. Who goes up for it first? (A catch
                // can still be dropped: ratings and the CATCH timing set the odds, so the first try is what counts here.)
                int first = -1;
                for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Ball.State == BallState.Flying && first < 0; i++)
                {
                    // (where the ball will be after this tick, the circle a little short of it along its flight)
                    float bx = w.Ball.X + w.Ball.VX * w.Dt, bz = w.Ball.Z + w.Ball.VZ * w.Dt;
                    float sp = (float)System.Math.Sqrt(w.Ball.VX * w.Ball.VX + w.Ball.VZ * w.Ball.VZ) + 1e-4f, back = w.Cfg.play.catchRadiusYd * 0.3f;
                    square.X = bx; square.Z = bz; square.VX = square.VZ = 0f;
                    circle.X = bx - w.Ball.VX / sp * back; circle.Z = bz - w.Ball.VZ / sp * back; circle.VX = circle.VZ = 0f;
                    w.Step(null);
                    foreach (int slot in w.CatchTried) first = slot;
                }
                Assert.AreEqual(on ? circle.Slot : square.Slot, first, on ? "the circle goes up for his ball" : "NO_PASS_TARGET=1: the nearest man goes up");
            }
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
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 0f; c.play.catchChanceDefense = 1f; c.tightCoverage.breakupChance = 0f; c.features["formations"] = false; });
            w.Step(Press(new PlayerInput { Hike = true }));
            var target = LivePlay.Receiver(w, 1);
            // a defender standing on the receiver
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense) { d = p; break; }
            target.X = w.LineOfScrimmageX + 12f; target.Z = -15f; target.HomeX = target.X; target.HomeZ = target.Z;
            w.Step(Press(new PlayerInput { Throw = 1 }));
            // (to the end of the play: with a return the man who picked it runs until he is caught; WS-06)
            for (int i = 0; i < w.Cfg.sim.tickHz * (w.Cfg.play.maxPlayS + 1) && w.Phase == Phase.Live; i++)
            {
                d.X = w.Ball.TargetX; d.Z = w.Ball.TargetZ; d.VX = d.VZ = 0f;
                w.Step(null);
            }
            Assert.AreEqual("INTERCEPTED", w.Result);
        }

        // FEATURES.oobIncomplete: the pass comes down to a receiver standing past the sideline. With the rule on it is
        // incomplete (no gain, back to the line); NO_OOB_INCOMPLETE=1 gives the old way (caught, then out of bounds there).
        static World CatchOutOfBounds(bool rule)
        {
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 1f; c.play.catchChanceDefense = 0f; c.features["oobIncomplete"] = rule; });
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            var target = LivePlay.Receiver(w, 1);
            float oobZ = -(w.Cfg.field.HalfWidth + 1f);
            target.X = w.LineOfScrimmageX + w.Dir * 12f; target.Z = oobZ; target.VX = target.VZ = 0f; target.HomeX = target.X; target.HomeZ = target.Z;
            w.Step(Press(new PlayerInput { Throw = 1 }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Phase == Phase.Live && w.Ball.State == BallState.Flying; i++)
            {
                target.X = w.Ball.TargetX; target.Z = oobZ; target.VX = target.VZ = 0f;
                w.Step(null);
            }
            return w;
        }

        [Test]
        public void ACatchOutOfBoundsIsIncomplete()
        {
            var w = CatchOutOfBounds(true);
            float los = w.LineOfScrimmageX;
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("INCOMPLETE", w.Result, "his feet were out: no catch");
            Assert.AreEqual(los, w.NextLos, "back to the line, no gain");
            Assert.AreNotEqual(BallState.Held, w.Ball.State);
            Assert.AreEqual("NO_OOB_INCOMPLETE", ConfigLoader.EnvName("oobIncomplete"));
            Assert.IsTrue(ConfigTests.Load().Feature("oobIncomplete"), "on in game.json");
        }

        [Test]
        public void NoOobIncompleteKeepsTheOldCatch()
        {
            var w = CatchOutOfBounds(false);
            Assert.AreEqual("OUT OF BOUNDS", w.Result, "caught, then whistled out where he stood");
        }

        static void NoTackles(GameConfig c) { c.play.tackleBase = 0f; c.play.tackleSpeedBonus = 0f; c.play.tackleFromBehindBonus = 0f; }

        // Interception returns (FEATURES.intreturn): the defender who catches it runs until the whistle.
        static (World w, Player picker) PickedOff(System.Action<GameConfig> tune = null)
        {
            // (the base line-up these were written for: in a play's own formation nobody may be near enough to stop the return)
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 0f; c.play.catchChanceDefense = 1f; c.tightCoverage.breakupChance = 0f; c.features["formations"] = false; tune?.Invoke(c); });
            w.Step(Press(new PlayerInput { Hike = true }));
            var target = LivePlay.Receiver(w, 1);
            target.X = w.LineOfScrimmageX + w.Dir * 12f; target.Z = -15f;
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense && !p.Human) { d = p; break; }
            w.Step(Press(new PlayerInput { Throw = 1 }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && !w.Intercepted && w.Phase == Phase.Live; i++)
            {
                d.X = w.Ball.TargetX; d.Z = w.Ball.TargetZ; d.VX = d.VZ = 0f;
                w.Step(null);
            }
            Assert.IsTrue(w.Intercepted, "picked off");
            // whoever made the pick (a cover man next to the receiver can get there before d does)
            foreach (var p in w.Players) if (p.Slot == w.Ball.Carrier) d = p;
            Assert.AreEqual(w.Defense, d.Team, "a defender has it");
            return (w, d);
        }

        [Test]
        public void AnInterceptionIsReturnedNotWhistledAtTheCatch()
        {
            var (w, d) = PickedOff(NoTackles);
            Assert.AreEqual(Phase.Live, w.Phase, "the play goes on: he runs it back");
            Assert.AreEqual(d.Slot, w.Ball.Carrier);
            float caughtAt = d.X;
            Run(w, w.Cfg.sim.tickHz);
            if (w.Phase == Phase.Live) Assert.Greater((caughtAt - d.X) * w.Dir, 1f, "he runs the other way, toward the end zone he defends against");
        }

        [Test]
        public void AReturnTackledChangesHandsWhereHeWentDown()
        {
            var (w, d) = PickedOff(c => c.play.tackleBase = 1f);
            int thrower = w.Offense;
            for (int i = 0; i < w.Cfg.sim.tickHz * 20 && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("INTERCEPTED", w.Result, "it ends as an interception, whoever tackled him");
            float spot = w.NextLos;
            Run(w, (int)(w.Cfg.play.whistleS * w.Cfg.sim.tickHz) + 2);
            Assert.AreEqual(1 - thrower, w.Offense, "the other team has it now");
            Assert.AreEqual(spot, w.LineOfScrimmageX, 1e-3f, "where the return ended");
        }

        [Test]
        public void APickSixScoresForTheDefense()
        {
            var (w, d) = PickedOff(NoTackles);
            int thrower = w.Offense, picker = w.Defense;
            float dir = w.Dir, goal = w.Cfg.field.GoalLineX;
            d.X = -dir * (goal - 1f);   // a step from the end zone he is running at
            Run(w, w.Cfg.sim.tickHz, null);
            for (int i = 0; i < w.Cfg.sim.tickHz * 5 && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.AreEqual("PICK SIX", w.Result);
            Assert.AreEqual(6, w.Score[picker]);
            Run(w, (int)(w.Cfg.play.whistleS * w.Cfg.sim.tickHz) + 2);
            Assert.AreEqual(thrower, w.Offense, "the team that threw it gets it back");
            Assert.AreEqual(-dir * (goal - 25f), w.LineOfScrimmageX, 1e-3f, "at its own 25");
        }

        [Test]
        public void NoIntReturnSwitchEndsThePlayAtTheCatch()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_INTRETURN", "1" } });
            Assert.IsFalse(cfg.Feature("intreturn"));
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
            float spot = w.Hit.Last == HitKind.BigHit || w.Hit.Last == HitKind.Trip ? w.Hit.FromX : qb.X;   // FEATURES.hitAngle: a big hit is spotted where he was hit, a trip where he was tripped
            Assert.AreEqual(spot, w.NextLos, 1e-3f, "the next play starts where he went down");
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
            cfg.features["botQuickCall"] = false;   // (this test times the full huddle; botQuickCall cuts an all-bot one to bots.coachCallS)
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
