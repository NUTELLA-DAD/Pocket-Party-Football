using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // The snap on the TV (View/Snap.cs): before the hike the ball lies on the grass under the center's hands, at the
    // hike it travels back to the QB and he pulls it in. It only reads the World, so it never changes the game.
    public sealed class SnapTests
    {
        const int Me = 500;

        static World AtTheLine(Action<GameConfig> tune = null, Dictionary<string, string> env = null, string play = "slants")
        {
            var cfg = ConfigTests.Load(env);
            tune?.Invoke(cfg);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, Me);
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Me, play));
            return w;
        }

        static Dictionary<int, PlayerInput> Press(PlayerInput i) => new Dictionary<int, PlayerInput> { { Me, i } };

        static void Hike(World w)
        {
            w.Step(Press(new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
        }

        [Test]
        public void TheCenterIsTheLinemanInFrontOfTheQb()
        {
            var w = AtTheLine();
            var c = Snap.Center(w);
            var qb = LivePlay.Qb(w);
            Assert.IsNotNull(c);
            Assert.AreEqual("OL", c.Pos);
            Assert.AreEqual(w.Offense, c.Team);
            Assert.AreEqual(qb.HomeZ, c.HomeZ, 0.01f, "straight in front of the QB");
            foreach (var p in w.Players)
                if (p.Team == w.Offense && p.Pos == "OL" && p != c)
                    Assert.GreaterOrEqual(Math.Abs(p.HomeZ - qb.HomeZ), Math.Abs(c.HomeZ - qb.HomeZ));
        }

        [Test]
        public void BeforeTheHikeTheBallIsOnTheGrassAtTheCenter()
        {
            var w = AtTheLine();
            var c = Snap.Center(w);
            Assert.IsTrue(Snap.Spotted(w, out float x, out float z), "the ball is spotted in the line-up");
            Assert.AreEqual(c.HomeZ, z, 0.01f);
            Assert.AreEqual(c.HomeX + w.Dir * w.Cfg.anim.snapAheadYd, x, 0.01f, "just in front of his feet");
            Assert.AreEqual((x - w.LineOfScrimmageX) * w.Dir, -0.3f, 0.35f, "about on the line of scrimmage");
        }

        // The defense parked far away, so nothing interrupts the play while we watch the snap.
        static void ClearDefense(World w)
        {
            foreach (var p in w.Players) if (p.Team == w.Defense) { p.X = w.LineOfScrimmageX + w.Dir * 40f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; }
        }

        [Test]
        public void AtTheHikeItTravelsBackAndTheQbPullsItIn()
        {
            var w = AtTheLine();
            ClearDefense(w);
            var c = Snap.Center(w);
            var qb = LivePlay.Qb(w);
            int n = Snap.FlightTicks(w);
            Assert.Greater(n, 1, "it takes more than one tick");
            Assert.LessOrEqual(n, (int)(w.Cfg.sim.tickHz * 0.6f), "and well under a second");
            Hike(w);
            float last = -1f;
            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(Snap.InFlight(w, 0f, out float t, out float fx, out float fz), "tick " + i);
                Assert.That(t, Is.InRange(0f, 1f));
                Assert.Greater(t, last, "it keeps going back");
                last = t;
                Assert.AreEqual(c.HomeX + w.Dir * w.Cfg.anim.snapAheadYd, fx, 0.01f);
                Assert.AreEqual(Move.Snap, Snap.For(w, c), "the center stays down over the ball");
                Assert.AreEqual(Move.TakeSnap, Snap.For(w, qb), "the QB's hands are out for it");
                w.Step(null);
            }
            Assert.IsFalse(Snap.InFlight(w, 0f, out _, out _, out _), "it has arrived");
            Assert.AreEqual(Move.Secure, Snap.For(w, qb), "and he tucks it in");
            Assert.AreEqual(Move.None, Snap.For(w, c), "the center is free to block");
            for (int i = 0; i < (int)Math.Round(w.Cfg.anim.snapSecureS * w.Cfg.sim.tickHz); i++) w.Step(null);
            Assert.AreEqual(Move.None, Snap.For(w, qb), "then his own moves take over");
        }

        [Test]
        public void TheMovesShowTheSnapOnTheTv()
        {
            var w = AtTheLine();
            var m = new Moves();
            var c = Snap.Center(w);
            var qb = LivePlay.Qb(w);
            m.Observe(w);
            Hike(w);
            m.Observe(w);
            Assert.AreEqual(Move.Snap, m.Of(w, c).Kind);
            Assert.AreEqual(Move.TakeSnap, m.Of(w, qb).Kind, "not the cocked arm yet");
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.IsTrue(PlayersView.PoseFor(new MoveNow { Kind = Move.Snap }, 0, out var down, out _));
            Assert.AreEqual(FootballFigure.PoseKind.Stance, down, "the center is in his stance");
            Assert.IsTrue(PlayersView.PoseFor(new MoveNow { Kind = Move.TakeSnap }, 0, out var hands, out _));
            Assert.AreEqual(FootballFigure.PoseKind.Handoff, hands, "the QB's hands are out in front");
        }

        [Test]
        public void NoSnapSwitchesItOff()
        {
            var w = AtTheLine(env: new Dictionary<string, string> { { "NO_SNAP", "1" } });
            var c = Snap.Center(w);
            Assert.IsFalse(Snap.Spotted(w, out _, out _), "the QB just holds it, as before");
            Hike(w);
            Assert.IsFalse(Snap.InFlight(w, 0f, out _, out _, out _));
            Assert.AreEqual(Move.None, Snap.For(w, c));
            Assert.AreEqual(Move.None, Snap.For(w, LivePlay.Qb(w)));
        }

        [Test]
        public void AKickoffHasNoSnap()
        {
            var cfg = ConfigTests.Load();
            cfg.features["kicking"] = true;
            var w = WorldTests.Full(cfg);
            w.Step(null);
            Assert.AreEqual(KickKind.Kickoff, Kicking.Mode(w), "the game opens with a kickoff");
            Assert.IsFalse(Snap.Spotted(w, out _, out _));
        }

        [Test]
        public void WatchingNeverChangesTheGame()
        {
            var cfg = ConfigTests.Load();
            var a = WorldTests.Full(cfg, 11);
            var b = WorldTests.Full(cfg, 11);
            for (int i = 0; i < cfg.sim.tickHz * 40; i++)
            {
                a.Step(null);
                b.Step(null);
                Snap.Spotted(b, out _, out _);
                Snap.InFlight(b, 0.5f, out _, out _, out _);
                Snap.FlightTicks(b);
                foreach (var p in b.Players) Snap.For(b, p);
            }
            Assert.AreEqual(a.Hash(), b.Hash());
        }
    }
}
