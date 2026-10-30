using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-05 receiving: CALL FOR IT and the CATCH timing window (Sim/Receiving.cs, FEATURES.receiving).
    public sealed class ReceivingTests
    {
        const int Me = 500;   // the person at QB

        static GameConfig Cfg(bool on, System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            cfg.features["receiving"] = on;
            tune?.Invoke(cfg);
            return cfg;
        }

        // Lined up and Set with a person at QB and the defense far away; `rcv` (the triangle WR) is made a person too.
        static World AtTheLine(GameConfig cfg, out Player rcv)
        {
            var w = WorldTests.Full(cfg);
            w.Step(null);                                  // the first huddle
            w.ReplaceBot(w.Offense, Me);
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Me, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase);
            foreach (var p in w.Players)
                if (p.Team == w.Defense) { p.X = w.LineOfScrimmageX + w.Cfg.formation.attackDir * 40f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; }
            rcv = LivePlay.Receiver(w, 1);
            Assert.AreEqual("WR", rcv.Pos);
            rcv.Human = true;                              // he stands still (nobody moves his stick) and taps when told
            return w;
        }

        static World Hiked(GameConfig cfg, out Player rcv)
        {
            var w = AtTheLine(cfg, out rcv);
            w.Step(Press(Me, new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
            return w;
        }

        static Dictionary<int, PlayerInput> Press(int slot, PlayerInput i) => new Dictionary<int, PlayerInput> { { slot, i } };
        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(null); }

        // ---- the switch ----

        [Test]
        public void TheSwitchShipsOffAndHasItsEnvOverride()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("receiving"), "FEATURES.receiving is on in game.json (the merge captain turned it on)");
            Assert.AreEqual("NO_RECEIVING", ConfigLoader.EnvName("receiving"));
            var cfg = ConfigTests.Load();
            cfg.features["receiving"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_RECEIVING" ? "1" : null);
            Assert.IsFalse(cfg.Feature("receiving"));
        }

        [Test]
        public void TheNumbersAreInTheReceivingBlock()
        {
            var c = ConfigTests.Load().receiving;
            Assert.Greater(c.callPulseS, 0f);
            Assert.Greater(c.callCooldownS, 0f);
            Assert.Greater(c.catchWindowS, 0f);
            Assert.Greater(c.catchForgetS, c.catchWindowS, "an early jump is remembered longer than the window");
            Assert.Greater(c.catchWindowBonus, 0f);
            Assert.Greater(c.catchEarlyPenalty, 0f);
            Assert.That(c.catchChanceMax, Is.InRange(0f, 1f));
        }

        [Test]
        public void BotGamesAreTheSameWithTheSwitchOnOrOff()
        {
            var off = WorldTests.Full(Cfg(false), 2);
            var on = WorldTests.Full(Cfg(true), 2);
            int ticks = off.Cfg.sim.tickHz * off.Cfg.baseline.seconds;
            for (int i = 0; i < ticks; i++)
            {
                off.Step(null); on.Step(null);
                Assert.AreEqual(off.Hash(), on.Hash(), "tick " + i + ": bots never tap, so a bot game must not change");
            }
        }

        [Test]
        public void EveryReceiverHasItsOwnShape()
        {
            // the QB's buttons, the throw and the CALL FOR IT pulse all find a receiver by his shape
            var shapes = ConfigTests.Load().play.shapes;
            Assert.AreEqual(ConfigTests.Load().play.receivers.Count, shapes.Count);
            Assert.AreEqual(shapes.Count, new HashSet<string>(shapes).Count, "two receivers share a shape: " + string.Join(" ", shapes));
            foreach (var sh in shapes) StringAssert.DoesNotContain("?", sh, "a shape was lost to a text encoding");
        }

        [Test]
        public void EveryReceiverHasItsOwnColour()
        {
            // FEATURES.colorTargets: the QB's button and the badge over the receiver share one colour per shape
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("colorTargets"), "FEATURES.colorTargets is on in game.json");
            Assert.AreEqual("NO_COLOR_TARGETS", ConfigLoader.EnvName("colorTargets"));
            Assert.AreEqual(cfg.play.shapes.Count, cfg.play.shapeColors.Count, "one colour per shape");
            var seen = new HashSet<string>();
            for (int k = 1; k <= cfg.play.shapes.Count; k++)
            {
                string c = Receiving.ColorOf(cfg, k);
                StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", c, "shape " + k + " colour");
                Assert.IsTrue(seen.Add(c.ToLowerInvariant()), "two receivers share a colour: " + c);
            }
            Assert.AreEqual("", Receiving.ColorOf(cfg, 0), "not a receiver: no colour");
            Assert.AreEqual("", Receiving.ColorOf(cfg, cfg.play.shapes.Count + 1));
            ConfigLoader.ApplyEnvOverrides(cfg, n => n == "NO_COLOR_TARGETS" ? "1" : null);
            Assert.AreEqual("", Receiving.ColorOf(cfg, 1), "NO_COLOR_TARGETS=1: the plain gold badge and ivory button");
        }

        // ---- the CATCH window ----

        [Test]
        public void ATapInsideTheWindowRaisesTheChanceAndATooEarlyTapLowersIt()
        {
            var w = Hiked(Cfg(true), out var rcv);
            var c = w.Cfg.receiving;
            float plain = w.Cfg.play.catchChanceOffense;
            int window = Huddle.Ticks(w, c.catchWindowS), forget = Huddle.Ticks(w, c.catchForgetS);
            Assert.AreEqual(plain, Receiving.Chance(w, rcv, plain), "no tap: the plain chance");

            w.Step(Press(rcv.Slot, new PlayerInput { Catch = true }));
            int tapped = w.Tick - 1;
            float good = System.Math.Min(c.catchChanceMax, plain + c.catchWindowBonus);
            float early = System.Math.Max(0f, plain - c.catchEarlyPenalty);
            Assert.AreEqual(good, Receiving.Chance(w, rcv, plain), 1e-6f, "right after the tap");
            Assert.Greater(good, plain);

            Run(w, tapped + window - w.Tick);
            Assert.AreEqual(window, w.Tick - tapped);
            Assert.AreEqual(good, Receiving.Chance(w, rcv, plain), 1e-6f, "the last tick of the window");
            w.Step(null);
            Assert.AreEqual(early, Receiving.Chance(w, rcv, plain), 1e-6f, "one tick later: he jumped too soon");
            Assert.Less(early, plain);

            Run(w, tapped + forget - w.Tick);
            Assert.AreEqual(early, Receiving.Chance(w, rcv, plain), 1e-6f, "still landing");
            w.Step(null);
            Assert.AreEqual(plain, Receiving.Chance(w, rcv, plain), "an old tap is forgotten");
            Assert.AreEqual(Phase.Live, w.Phase, "(the play stayed live all along)");
        }

        [Test]
        public void TheWindowNeverLowersAChanceAboveTheCap()
        {
            var w = Hiked(Cfg(true), out var rcv);
            w.Step(Press(rcv.Slot, new PlayerInput { Catch = true }));
            float high = (w.Cfg.receiving.catchChanceMax + 1f) * 0.5f;
            Assert.AreEqual(high, Receiving.Chance(w, rcv, high));
        }

        [Test]
        public void TappingOverAndOverIsAJumpTooSoon()
        {
            var w = Hiked(Cfg(true), out var rcv);
            float plain = w.Cfg.play.catchChanceOffense;
            int window = Huddle.Ticks(w, w.Cfg.receiving.catchWindowS);
            for (int i = 0; i <= window + 1; i++) w.Step(Press(rcv.Slot, new PlayerInput { Catch = true }));
            Assert.Less(Receiving.Chance(w, rcv, plain), plain, "only the first tap counts, and it is too old now");
        }

        [Test]
        public void TheDefenseAndTheSwitchOffKeepThePlainChance()
        {
            var w = Hiked(Cfg(true), out var rcv);
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense) { d = p; break; }
            d.Human = true;
            w.Step(new Dictionary<int, PlayerInput> { { d.Slot, new PlayerInput { Catch = true } } });
            Assert.AreEqual(w.Cfg.play.catchChanceDefense, Receiving.Chance(w, d, w.Cfg.play.catchChanceDefense), "CATCH is the offense's button");

            var off = Hiked(Cfg(false), out var rcvOff);
            off.Step(Press(rcvOff.Slot, new PlayerInput { Catch = true }));
            Assert.AreEqual(off.Cfg.play.catchChanceOffense, Receiving.Chance(off, rcvOff, off.Cfg.play.catchChanceOffense), "switch off: nothing changes");
        }

        // A real pass to the standing triangle WR. With a coin-flip plain chance and a window that makes it a sure
        // catch (and an early jump a sure drop), the tap decides it. tap: ticks after the throw to tap CATCH, -1 none.
        static (bool caught, int tryTick, int throwTick) Pass(int tap)
        {
            var cfg = Cfg(true, c =>
            {
                c.play.catchChanceOffense = 0.5f;
                c.receiving.catchWindowBonus = 0.5f; c.receiving.catchEarlyPenalty = 0.5f; c.receiving.catchChanceMax = 1f;
            });
            var w = Hiked(cfg, out var rcv);
            Run(w, 10);
            w.Step(Press(Me, new PlayerInput { Throw = 1 }));
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            int thrown = w.Tick, tried = -1;
            for (int i = 0; i < cfg.sim.tickHz * 4 && w.Ball.State == BallState.Flying && w.Phase == Phase.Live; i++)
            {
                w.Step(i == tap ? Press(rcv.Slot, new PlayerInput { Catch = true }) : null);
                if (tried < 0 && w.CatchTried.Contains(rcv.Slot)) tried = w.Tick - 1;
            }
            return (w.Ball.State == BallState.Held && w.Ball.Carrier == rcv.Slot, tried, thrown);
        }

        [Test]
        public void ThePassIsCaughtWithATimelyTapAndDroppedWithAnEarlyOne()
        {
            var dry = Pass(-1);
            Assert.GreaterOrEqual(dry.tryTick, 0, "the ball came down to him");
            var cfg = Cfg(true);
            int window = (int)(cfg.receiving.catchWindowS * cfg.sim.tickHz);
            int last = dry.tryTick - dry.throwTick;                 // the step index of the catch try
            Assert.Greater(last - window - 1, 0, "the ball is in the air longer than the window");
            Assert.LessOrEqual(last, (int)(cfg.receiving.catchForgetS * cfg.sim.tickHz), "a tap at the throw is still remembered at the catch");

            Assert.IsTrue(Pass(last - window).caught, "tapped at the start of the window: caught");
            Assert.IsTrue(Pass(last - 1).caught, "tapped just before the ball got there: caught");
            Assert.IsFalse(Pass(last - window - 1).caught, "tapped one tick too soon: dropped");
            Assert.IsFalse(Pass(0).caught, "tapped at the throw: dropped");
        }

        // ---- CALL FOR IT ----

        [Test]
        public void CallForItPulsesForTheConfiguredTime()
        {
            var w = Hiked(Cfg(true), out var rcv);
            Assert.AreEqual(1, Receiving.ShapeOf(w, rcv));
            Assert.IsFalse(Receiving.Calling(w, rcv));
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            Assert.IsTrue(Receiving.Calling(w, rcv));
            Assert.AreEqual(w.Tick - 1, Receiving.CallStart(w, rcv));
            Assert.AreEqual(1, w.Recv.Calls);
            int pulse = Huddle.Ticks(w, w.Cfg.receiving.callPulseS);
            Run(w, pulse - 2);
            Assert.IsTrue(Receiving.Calling(w, rcv), "still pulsing");
            w.Step(null);
            Assert.IsFalse(Receiving.Calling(w, rcv), "the pulse ends");
            Assert.AreEqual(-1, Receiving.CallStart(w, rcv));
        }

        [Test]
        public void CallForItWaitsForTheCooldown()
        {
            var w = Hiked(Cfg(true), out var rcv);
            var c = w.Cfg.receiving;
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            int first = w.Tick - 1;
            int pulse = Huddle.Ticks(w, c.callPulseS), cooldown = Huddle.Ticks(w, c.callCooldownS);
            Assume.That(cooldown, Is.GreaterThan(pulse + 1), "this test needs a cooldown longer than the pulse");
            Run(w, first + pulse + 1 - w.Tick);
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            Assert.IsFalse(Receiving.Calling(w, rcv), "too soon to call again");
            Run(w, first + cooldown - w.Tick);
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            Assert.IsTrue(Receiving.Calling(w, rcv), "after the cooldown");
            Assert.AreEqual(2, w.Recv.Calls);
        }

        [Test]
        public void OnlyReceiversCallAndOnlyBeforeTheThrow()
        {
            var w = Hiked(Cfg(true), out var rcv);
            Player ol = null, d = null;
            foreach (var p in w.Players)
            {
                if (p.Team == w.Offense && p.Pos == "OL" && ol == null) ol = p;
                if (p.Team == w.Defense && d == null) d = p;
            }
            ol.Human = true; d.Human = true;
            Assert.AreEqual(0, Receiving.ShapeOf(w, ol));
            Assert.AreEqual(0, Receiving.ShapeOf(w, d));
            w.Step(new Dictionary<int, PlayerInput> { { ol.Slot, new PlayerInput { Call = true } }, { d.Slot, new PlayerInput { Call = true } } });
            Assert.IsFalse(Receiving.Calling(w, ol), "a lineman has no shape to pulse");
            Assert.IsFalse(Receiving.Calling(w, d), "the defense can't call for it");
            Assert.AreEqual(0, w.Recv.Calls);

            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            Assert.IsTrue(Receiving.Calling(w, rcv));
            w.Step(Press(Me, new PlayerInput { Throw = 2 }));
            Assert.IsTrue(w.Thrown);
            Assert.IsFalse(Receiving.Calling(w, rcv), "the pulse stops at the throw");
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            Assert.AreEqual(1, w.Recv.Calls, "no calling once the ball is in the air");
        }

        [Test]
        public void SwitchOffNobodyCalls()
        {
            var w = Hiked(Cfg(false), out var rcv);
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true }));
            Assert.IsFalse(Receiving.Calling(w, rcv));
            Assert.AreEqual(0, w.Recv.Calls);
        }

        [Test]
        public void ANewPlayForgetsTheLastPlaysTaps()
        {
            var w = Hiked(Cfg(true), out var rcv);
            w.Step(Press(rcv.Slot, new PlayerInput { Call = true, Catch = true }));
            Assert.AreEqual(1, w.Recv.CallTick.Count);
            LivePlay.Whistle(w, "PLAY OVER", w.LineOfScrimmageX);
            w.Step(null);
            Assert.AreEqual(0, w.Recv.CallTick.Count);
            Assert.AreEqual(0, w.Recv.CatchTick.Count);
        }
    }
}
