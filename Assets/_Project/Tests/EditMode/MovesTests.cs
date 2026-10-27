using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // The moves on the TV (View/Moves.cs): each one shows up on the right man when the sim does the thing.
    public sealed class MovesTests
    {
        const int Me = 500;

        static World AtTheLine(string offensePlay = "slants", Action<GameConfig> tune = null, Dictionary<string, string> env = null)
        {
            var cfg = ConfigTests.Load(env);
            tune?.Invoke(cfg);
            var w = WorldTests.Full(cfg);
            w.Step(null);                                  // the first huddle
            w.ReplaceBot(w.Offense, Me);                   // a person at QB
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Me, offensePlay));
            return w;
        }

        static Dictionary<int, PlayerInput> Press(PlayerInput i) => new Dictionary<int, PlayerInput> { { Me, i } };

        // Steps like the game does: the view looks after every tick.
        static void Step(World w, Moves m, Dictionary<int, PlayerInput> input = null) { w.Step(input); m.Observe(w); }

        static void Hike(World w, Moves m)
        {
            m.Observe(w);
            Step(w, m, Press(new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
        }

        // The snap takes the first moments of the play (Snap.cs): step past it.
        static void PastTheSnap(World w, Moves m)
        {
            int n = Snap.FlightTicks(w) + (int)Math.Round(w.Cfg.anim.snapSecureS * w.Cfg.sim.tickHz);
            for (int i = 0; i < n; i++) Step(w, m);
        }

        static void ClearDefense(World w)
        {
            foreach (var p in w.Players) if (p.Team == w.Defense) { p.X = w.LineOfScrimmageX + w.Cfg.formation.attackDir * 40f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; }
        }

        // A defender right in front of the QB.
        static Player InHisFace(World w)
        {
            var qb = LivePlay.Qb(w);
            var d = LivePlay.SpotOf(w, w.Defense, 0);
            d.X = qb.X + w.Dir * 0.6f; d.Z = qb.Z; d.VX = d.VZ = 0f;
            return d;
        }

        [Test]
        public void TheQbCocksHisArmThenThrows()
        {
            var w = AtTheLine();
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            PastTheSnap(w, m);
            Assert.AreEqual(Move.Aim, m.Of(w, qb).Kind, "ball in hand, standing in the pocket");
            Step(w, m, Press(new PlayerInput { Throw = 1 }));
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            var now = m.Of(w, qb);
            Assert.AreEqual(Move.Throw, now.Kind);
            Assert.AreEqual(0, now.Frame);
            for (int i = 0; i < w.Cfg.sim.tickHz; i++) Step(w, m);
            Assert.AreNotEqual(Move.Throw, m.Of(w, qb).Kind, "the follow-through ends");
        }

        [Test]
        public void TheReceiverPutsHisHandsUpThenTucksTheCatch()
        {
            var w = AtTheLine(tune: c => c.play.catchChanceOffense = 1f);
            ClearDefense(w);
            var m = new Moves();
            Hike(w, m);
            var target = LivePlay.Receiver(w, 1);
            for (int i = 0; i < 20; i++) Step(w, m);       // let the routes start
            Step(w, m, Press(new PlayerInput { Throw = 1 }));
            bool reached = false, secured = false;
            for (int i = 0; i < w.Cfg.sim.tickHz * 5 && !secured; i++)
            {
                Step(w, m);
                var k = m.Of(w, target).Kind;
                if (k == Move.Reach) { reached = true; Assert.AreEqual(BallState.Flying, w.Ball.State); }
                if (k == Move.Secure) { secured = true; Assert.AreEqual(target.Slot, w.Ball.Carrier); }
            }
            Assert.IsTrue(reached, "hands up as the ball comes in");
            Assert.IsTrue(secured, "the catch pulled in");
        }

        [Test]
        public void ADroppedPassIsABobble()
        {
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 0f; c.play.catchChanceDefense = 0f; });
            ClearDefense(w);
            var m = new Moves();
            Hike(w, m);
            var target = LivePlay.Receiver(w, 1);
            for (int i = 0; i < 20; i++) Step(w, m);       // let the routes start
            Step(w, m, Press(new PlayerInput { Throw = 1 }));
            bool bobbled = false;
            for (int i = 0; i < w.Cfg.sim.tickHz * 5 && !bobbled; i++)
            {
                Step(w, m);
                if (w.CatchTried.Contains(target.Slot)) bobbled = m.Of(w, target).Kind == Move.Bobble;
            }
            Assert.IsTrue(bobbled);
        }

        [Test]
        public void ATackleBringsBothDownAndTheyGetUpForTheHuddle()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 1f);
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            var d = InHisFace(w);
            Step(w, m);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("SACKED", w.Result);
            var down = m.Of(w, qb);
            Assert.AreEqual(Move.Tackled, down.Kind);
            Assert.IsTrue(down.OnBack, "hit from the front: over backwards");
            Assert.AreEqual(Move.Tackle, m.Of(w, d).Kind);
            Assert.IsFalse(down.GettingUp);
            // they stay down, then get up near the end of the whistle
            bool gotUp = false;
            while (w.Phase == Phase.Whistle)
            {
                var now = m.Of(w, qb);
                Assert.AreEqual(Move.Tackled, now.Kind);
                if (now.GettingUp) gotUp = true;
                Step(w, m);
            }
            Assert.IsTrue(gotUp);
            Assert.AreEqual(Move.None, m.Of(w, qb).Kind, "the next huddle: on his feet");
            Assert.AreEqual(Move.None, m.Of(w, d).Kind);
        }

        [Test]
        public void TackledFromBehindGoesDownOnHisFront()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 1f);
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            var d = LivePlay.SpotOf(w, w.Defense, 0);
            d.X = qb.X - w.Dir * 0.6f; d.Z = qb.Z; d.VX = d.VZ = 0f;
            Step(w, m);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual(Move.Tackled, m.Of(w, qb).Kind);
            Assert.IsFalse(m.Of(w, qb).OnBack);
        }

        [Test]
        public void APersonsLongTackleIsHisNotTheNearestMans()
        {
            // a person's tackle reaches half again a bot's: he tackles from further out than a bot standing nearer
            const int Them = 600;
            var w = AtTheLine(tune: c => c.play.tackleBase = 1f);
            w.ReplaceBot(w.Defense, Them);
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            var me = w.Find(Them);
            var near = LivePlay.SpotOf(w, w.Defense, 1);
            float reach = w.Cfg.play.tackleReachYd;
            me.X = qb.X + w.Dir * reach * 1.35f; me.Z = qb.Z; me.VX = me.VZ = 0f;
            near.X = qb.X; near.Z = qb.Z + reach * 1.1f; near.VX = near.VZ = 0f;
            Step(w, m, new Dictionary<int, PlayerInput> { { Them, new PlayerInput { Tackle = true } } });
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.Less(LivePlay.Dist(near, qb), LivePlay.Dist(me, qb), "the bot stands nearer");
            var t = m.Of(w, me);
            Assert.AreEqual(Move.Tackle, t.Kind, "the man who made it goes down with him");
            Assert.IsTrue(t.HasPartner);
            Assert.AreEqual(qb.Slot, t.Partner);
            Assert.AreNotEqual(Move.Tackle, m.Of(w, near).Kind);
            var c = m.Of(w, qb);
            Assert.AreEqual(Move.Tackled, c.Kind);
            Assert.AreEqual(me.Slot, c.Partner);
        }

        [Test]
        public void TheTacklerGoesDownAgainstTheCarrier()
        {
            // 2.5 yards apart: he lands 1.1 yards short of the carrier, on the line between them, facing him
            Assert.IsTrue(Moves.TackleContact(0f, 0f, 2.5f, 0f, 1.1f, out float x, out float z, out float ux, out float uz));
            Assert.AreEqual(1.4f, x, 1e-4f); Assert.AreEqual(0f, z, 1e-4f);
            Assert.AreEqual(1f, ux, 1e-4f); Assert.AreEqual(0f, uz, 1e-4f);
            // from the side and behind, and already closer than the contact: put back out to it
            Assert.IsTrue(Moves.TackleContact(3f, 4f, 3f, 4.5f, 1.1f, out x, out z, out ux, out uz));
            Assert.AreEqual(3f, x, 1e-4f); Assert.AreEqual(3.4f, z, 1e-4f);
            Assert.AreEqual(0f, ux, 1e-4f); Assert.AreEqual(1f, uz, 1e-4f);
            // on the same spot: nothing to line up on
            Assert.IsFalse(Moves.TackleContact(1f, 1f, 1f, 1f, 1.1f, out x, out z, out ux, out uz));
        }

        [Test]
        public void AMissedTackleIsAWhiff()
        {
            var w = AtTheLine(tune: c => { c.play.tackleBase = 0f; c.play.tackleSpeedBonus = 0f; c.play.tackleFromBehindBonus = 0f; });
            var m = new Moves();
            Hike(w, m);
            var d = InHisFace(w);
            Step(w, m);
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.Greater(d.StumbleTicks, 0);
            Assert.AreEqual(Move.Whiff, m.Of(w, d).Kind);
        }

        [Test]
        public void AJukeShows()
        {
            var w = AtTheLine();
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            Step(w, m, Press(new PlayerInput { Juke = true, MoveZ = 1f }));
            Assert.Greater(qb.JukeTicks, 0);
            Assert.AreEqual(Move.Juke, m.Of(w, qb).Kind);
            for (int i = 0; i < w.Cfg.play.jukeTicks + 2; i++) Step(w, m);
            Assert.AreNotEqual(Move.Juke, m.Of(w, qb).Kind);
        }

        [Test]
        public void ATouchdownIsCelebrated()
        {
            var w = AtTheLine();
            ClearDefense(w);
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            qb.X = w.Dir * (w.Cfg.field.GoalLineX - 0.5f);
            for (int i = 0; i < w.Cfg.sim.tickHz && w.Phase == Phase.Live; i++) Step(w, m, Press(new PlayerInput { MoveX = w.Dir, Sprint = true }));
            Assert.AreEqual("TOUCHDOWN", w.Result);
            Assert.AreEqual(Move.Celebrate, m.Of(w, qb).Kind);
        }

        [Test]
        public void FramesStepAtTheAnimRate()
        {
            var w = AtTheLine();
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            Step(w, m, Press(new PlayerInput { Throw = 1 }));
            int ticks = (int)Math.Round(w.Cfg.sim.tickHz / w.Cfg.anim.fps * 2f);   // two key frames' worth
            for (int i = 0; i < ticks; i++) Step(w, m);
            if (m.Of(w, qb).Kind == Move.Throw) Assert.AreEqual(2, m.Of(w, qb).Frame);
        }

        [Test]
        public void NoMovesSwitchesThemOff()
        {
            var w = AtTheLine(tune: c => c.play.tackleBase = 1f, env: new Dictionary<string, string> { { "NO_MOVES", "1" } });
            var m = new Moves();
            Hike(w, m);
            var qb = LivePlay.Qb(w);
            InHisFace(w);
            Step(w, m);
            Assert.AreEqual(Phase.Whistle, w.Phase, "the rules are untouched");
            foreach (var p in w.Players) Assert.AreEqual(Move.None, m.Of(w, p).Kind);
        }

        // Steps through the first seconds of a play until a blocker and his man have picked each other; null if none did.
        static (Player a, Player b) LockedPair(World w, Moves m, int seconds = 3)
        {
            for (int i = 0; i < w.Cfg.sim.tickHz * seconds && w.Phase == Phase.Live; i++)
            {
                Step(w, m);
                foreach (var p in w.Players)
                {
                    var mp = m.Of(w, p);
                    if (mp.Kind != Move.Block || !mp.Locked) continue;
                    var q = w.Find(mp.Partner);
                    var mq = m.Of(w, q);
                    if (mq.Kind == Move.Block && mq.Locked && mq.Partner == p.Slot) return (p, q);
                }
            }
            return (null, null);
        }

        static float DrawnGap(Moves m, Player a, Player b)
        {
            m.LockOffset(a.Slot, 1f, out float ax, out float az);
            m.LockOffset(b.Slot, 1f, out float bx, out float bz);
            float dx = b.X + bx - (a.X + ax), dz = b.Z + bz - (a.Z + az);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        [Test]
        public void ABlockerAndHisManLockArms()
        {
            var w = AtTheLine();
            var m = new Moves();
            Hike(w, m);
            PastTheSnap(w, m);
            var (a, b) = LockedPair(w, m);
            Assert.IsNotNull(a, "a lineman and the man across from him lock up");
            Assert.AreNotEqual(a.Team, b.Team);
            Assert.IsTrue(a.Blocking || b.Blocking, "one of them is the blocker");
            // the TV slides them together (or apart) until their hands meet
            bool met = false;
            for (int i = 0; i < w.Cfg.sim.tickHz && w.Phase == Phase.Live && !met; i++)
            {
                if (m.Of(w, a).Partner != b.Slot || m.Of(w, b).Partner != a.Slot) break;
                met = Math.Abs(DrawnGap(m, a, b) - w.Cfg.anim.lockGapYd) < 0.15f;
                Step(w, m);
            }
            Assert.IsTrue(met, "drawn lockGapYd apart");
        }

        [Test]
        public void ALockedPairSlidesBackOnceItBreaks()
        {
            var w = AtTheLine();
            var m = new Moves();
            Hike(w, m);
            PastTheSnap(w, m);
            var (a, b) = LockedPair(w, m);
            Assert.IsNotNull(a);
            for (int i = 0; i < w.Cfg.sim.tickHz * 30 && w.Phase != Phase.Huddle; i++) Step(w, m);
            foreach (var p in w.Players)
            {
                m.LockOffset(p.Slot, 1f, out float x, out float z);
                Assert.AreEqual(0f, x, 1e-4f, "drawn on his own spot again");
                Assert.AreEqual(0f, z, 1e-4f);
            }
        }

        [Test]
        public void NoBlockLockKeepsTheOldShove()
        {
            var w = AtTheLine(env: new Dictionary<string, string> { { "NO_BLOCK_LOCK", "1" } });
            var m = new Moves();
            Hike(w, m);
            PastTheSnap(w, m);
            bool blocked = false;
            for (int i = 0; i < w.Cfg.sim.tickHz * 3 && w.Phase == Phase.Live; i++)
            {
                Step(w, m);
                foreach (var p in w.Players)
                {
                    var mp = m.Of(w, p);
                    if (mp.Kind != Move.Block) continue;
                    blocked = true;
                    Assert.IsFalse(mp.Locked);
                    m.LockOffset(p.Slot, 1f, out float x, out float z);
                    Assert.AreEqual(0f, x); Assert.AreEqual(0f, z);
                }
            }
            Assert.IsTrue(blocked, "still blocks");
        }

        [Test]
        public void ALockedBlockUsesTheLockPose()
        {
            Assert.IsTrue(PlayersView.PoseFor(new MoveNow { Kind = Move.Block, Locked = true, Frame = 1 }, 0, out var k, out int f));
            Assert.AreEqual(FootballFigure.PoseKind.Lock, k);
            Assert.AreEqual(1, f);
            PlayersView.PoseFor(new MoveNow { Kind = Move.Block }, 0, out k, out f);
            Assert.AreEqual(FootballFigure.PoseKind.Block, k);
        }

        [Test]
        public void TheReceiverWatchesTheBallAllTheWayIn()
        {
            var w = AtTheLine(tune: c => c.play.catchChanceOffense = 1f);
            ClearDefense(w);
            var m = new Moves();
            Hike(w, m);
            var target = LivePlay.Receiver(w, 1);
            for (int i = 0; i < 20; i++) Step(w, m);
            Step(w, m, Press(new PlayerInput { Throw = 1 }));
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            for (int i = 0; i < w.Cfg.sim.tickHz * 5 && w.Ball.State == BallState.Flying; i++)
            {
                Assert.IsTrue(m.Watching(target.Slot), "eyes on the ball from the throw to the catch");
                Assert.IsFalse(m.Watching(LivePlay.Qb(w).Slot), "the thrower isn't the one watching");
                Step(w, m);
            }
            Assert.AreEqual(target.Slot, w.Ball.Carrier);
            Assert.IsFalse(m.Watching(target.Slot), "caught: done watching");
        }

        [Test]
        public void PlayersFaceStraightDownTheField()
        {
            var cfg = ConfigTests.Load(null);
            Assert.IsTrue(cfg.Feature("faceDownfield"));
            // yaw 0 faces +x (one end zone), 180 the other: no turn toward the camera on the -z sideline
            Assert.AreEqual(0f, PlayersView.TowardCamera(0f, PlayersView.CameraTurn(cfg, false)), 1e-3f);
            Assert.AreEqual(180f, PlayersView.TowardCamera(180f, PlayersView.CameraTurn(cfg, false)), 1e-3f);
            Assert.AreEqual(0f, PlayersView.CameraTurn(cfg, true), "a locked pair faces each other, not the camera");

            var old = ConfigTests.Load(new Dictionary<string, string> { { "NO_FACE_DOWNFIELD", "1" } });
            Assert.IsFalse(old.Feature("faceDownfield"));
            Assert.AreEqual(42f, PlayersView.TowardCamera(0f, PlayersView.CameraTurn(old, false)), 1e-3f, "off: the old turn to the camera");
            Assert.AreEqual(old.anim.lockTurnDeg, PlayersView.CameraTurn(old, true));
        }

        [Test]
        public void NoWatchBallSwitchesItOff()
        {
            var w = AtTheLine(env: new Dictionary<string, string> { { "NO_WATCH_BALL", "1" } });
            ClearDefense(w);
            var m = new Moves();
            Hike(w, m);
            for (int i = 0; i < 20; i++) Step(w, m);
            Step(w, m, Press(new PlayerInput { Throw = 1 }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 3; i++)
            {
                foreach (var p in w.Players) Assert.IsFalse(m.Watching(p.Slot));
                Step(w, m);
            }
        }

        [Test]
        public void WatchingNeverChangesTheGame()
        {
            var cfg = ConfigTests.Load();
            var a = WorldTests.Full(cfg, 7);
            var b = WorldTests.Full(cfg, 7);
            var m = new Moves();
            for (int i = 0; i < cfg.sim.tickHz * 40; i++)
            {
                a.Step(null);
                b.Step(null); m.Observe(b);
                foreach (var p in b.Players) { m.Of(b, p); m.Watching(p.Slot); m.LockOffset(p.Slot, 0.5f, out _, out _); }
            }
            Assert.AreEqual(a.Hash(), b.Hash());
        }
    }
}
