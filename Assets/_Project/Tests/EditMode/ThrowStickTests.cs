using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // The throw stick (Sim/ThrowStick.cs, FEATURES.throwStick): the QB aims at a spot, the arc runs from bullet to lob,
    // the ball comes down over the spot, and the receiver nearest it keeps running there instead of stopping to block.
    public sealed class ThrowStickTests
    {
        const int Qb = 500;

        static GameConfig Cfg(bool on = true)
        {
            var cfg = ConfigTests.Load();
            cfg.features["throwStick"] = on;
            cfg.features["botsV2"] = false;   // the v1 receivers are the ones that used to stop and block
            cfg.features["kicking"] = false;
            return cfg;
        }

        // Hiked, a person at QB holding the ball.
        static World Live(GameConfig cfg)
        {
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, Qb);
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase);
            w.Step(In(new PlayerInput { Hike = true }));
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.AreEqual(Qb, w.Ball.Carrier);
            return w;
        }

        static Dictionary<int, PlayerInput> In(PlayerInput i) => new Dictionary<int, PlayerInput> { { Qb, i } };

        // Everyone but the QB far away, so nobody catches, swats or tackles.
        static void ClearField(World w)
        {
            foreach (var p in w.Players)
                if (p.Slot != Qb) { p.X = w.LineOfScrimmageX - w.Dir * 45f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; p.VX = p.VZ = 0f; }
        }

        // Throws with the stick at (ax, az) and flies it until it is back down at catch height; returns the top height.
        static float ThrowAndFly(World w, float ax, float az, float arc, out float landX, out float landZ, out int ticks)
        {
            w.Step(In(new PlayerInput { ThrowAt = true, AimX = ax, AimZ = az, Arc = arc }));
            Assert.AreEqual(BallState.Flying, w.Ball.State, "thrown");
            Assert.IsTrue(w.Thrown && w.StickThrow);
            float top = w.Ball.Y, c = w.Cfg.ball.catchHeightYd;
            landX = w.Ball.X; landZ = w.Ball.Z; ticks = 1;
            for (; ticks < w.Cfg.sim.tickHz * 8 && w.Ball.State == BallState.Flying; ticks++)
            {
                float px = w.Ball.X, pz = w.Ball.Z, py = w.Ball.Y;
                w.Step(null);
                if (w.Ball.Y > top) top = w.Ball.Y;
                if (w.Ball.VY < 0f && py >= c && w.Ball.Y < c)
                {
                    // between two ticks: where it crossed catch height
                    float k = (py - c) / (py - w.Ball.Y);
                    landX = px + (w.Ball.X - px) * k; landZ = pz + (w.Ball.Z - pz) * k;
                    return top;
                }
            }
            Assert.Fail("the pass never came back down to catch height");
            return top;
        }

        [Test]
        public void TheSwitchIsInGameJsonWithItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.features.ContainsKey("throwStick"), "FEATURES.throwStick is in game.json");
            Assert.Greater(cfg.play.throwMaxYards, 0f, "play.throwMaxYards");
            Assert.AreEqual("NO_THROW_STICK", ConfigLoader.EnvName("throwStick"));
            var on = Cfg();
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_THROW_STICK" ? "1" : null);
            Assert.IsFalse(ThrowStick.On(new World(on, 1)), "NO_THROW_STICK=1 turns it off");
        }

        [Test]
        public void WithTheSwitchOffTheStickThrowsNothing()
        {
            var w = Live(Cfg(on: false));
            w.Step(In(new PlayerInput { ThrowAt = true, AimX = w.Dir * 0.5f, Arc = 0f }));
            Assert.AreEqual(BallState.Held, w.Ball.State);
            Assert.IsFalse(w.Thrown);
        }

        [Test]
        public void TheMiddleOfTheStickThrowsNothing()
        {
            var w = Live(Cfg());
            w.Step(In(new PlayerInput { ThrowAt = true, AimX = 0.05f, AimZ = 0.05f, Arc = 0.5f }));
            Assert.AreEqual(BallState.Held, w.Ball.State, "inside the dead zone");
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void ItComesDownOverTheSpotTheStickPointsAt(float arc)
        {
            var w = Live(Cfg());
            ClearField(w);
            var qb = w.Find(Qb);
            float ax = w.Dir * 0.5f, az = -0.25f;   // half way downfield, a little toward the camera
            float tx = qb.X + ax * w.Cfg.play.throwMaxYards, tz = qb.Z + az * w.Cfg.play.throwMaxYards;
            ThrowAndFly(w, ax, az, arc, out float lx, out float lz, out _);
            Assert.AreEqual(tx, w.Ball.TargetX, 0.001f, "aimed at the spot");
            Assert.AreEqual(tz, w.Ball.TargetZ, 0.001f);
            Assert.AreEqual(tx, lx, 0.3f, "came down over it, arc " + arc);
            Assert.AreEqual(tz, lz, 0.3f);
        }

        [Test]
        public void ALobGoesHigherAndSlowerThanABullet()
        {
            var a = Live(Cfg()); ClearField(a);
            float bulletTop = ThrowAndFly(a, a.Dir * 0.6f, 0f, 0f, out _, out _, out int bulletTicks);
            var b = Live(Cfg()); ClearField(b);
            float lobTop = ThrowAndFly(b, b.Dir * 0.6f, 0f, 1f, out _, out _, out int lobTicks);
            Assert.Greater(lobTop, bulletTop + 2f, "the lob goes up");
            Assert.Greater(lobTicks, bulletTicks, "and takes longer to come down");
            Assert.AreEqual(a.Cfg.ball.lobApexYd + a.Cfg.ball.carryHeightYd, lobTop, 0.5f, "a full lob tops out at lobApexYd");
        }

        [Test]
        public void TheStickIsCappedAtFullPush()
        {
            var w = Live(Cfg());
            ClearField(w);
            var qb = w.Find(Qb);
            w.Step(In(new PlayerInput { ThrowAt = true, AimX = w.Dir * 3f, AimZ = 0f, Arc = 0.3f }));
            Assert.AreEqual(qb.X + w.Dir * w.Cfg.play.throwMaxYards, w.Ball.TargetX, 0.01f, "never further than throwMaxYards");
        }

        [Test]
        public void TheReceiverKeepsRunningToWhereItLands()
        {
            var cfg = Cfg();
            cfg.play.catchChanceOffense = 1f;      // the catch itself is not what this test is about
            cfg.features["receiving"] = false;     // (no CATCH timing for a bot)
            var w = Live(cfg);
            // the defense out of the way, so the play is just the throw and the run
            foreach (var p in w.Players)
                if (p.Team == w.Defense) { p.X = w.LineOfScrimmageX + w.Dir * 45f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; p.VX = p.VZ = 0f; }
            for (int i = 0; i < w.Cfg.sim.tickHz; i++) w.Step(null);   // a second into his route
            var qb = w.Find(Qb);
            var r = LivePlay.Receiver(w, 1);
            // lead him 12 yards downfield of where he is now
            float tx = r.X + w.Dir * 12f, tz = r.Z;
            float max = w.Cfg.play.throwMaxYards;
            w.Step(In(new PlayerInput { ThrowAt = true, AimX = (tx - qb.X) / max, AimZ = (tz - qb.Z) / max, Arc = 1f }));
            Assert.AreEqual(BallState.Flying, w.Ball.State);
            var target = Bots.Intended(w);
            Assert.AreEqual(r, target, "he is the man nearest the spot");
            float before = Dist(r, w.Ball.TargetX, w.Ball.TargetZ);
            bool everBlocked = false;
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Ball.State == BallState.Flying; i++)
            {
                w.Step(null);
                everBlocked |= r.Blocking;
            }
            Assert.IsFalse(everBlocked, "he never stopped to block");
            Assert.Less(Dist(r, w.Ball.TargetX, w.Ball.TargetZ), before - 6f, "he ran toward the landing spot");
            Assert.AreEqual(r.Slot, w.Ball.Carrier, "and caught it there");
        }

        static float Dist(Player p, float x, float z) { float dx = p.X - x, dz = p.Z - z; return (float)System.Math.Sqrt(dx * dx + dz * dz); }

        // ---- the phone ----

        [Test]
        public void ThePhoneStreamsTheAimAndLetsGoToThrow()
        {
            var src = new PhoneSource(new Seat { Slot = 0 }) { Aid = "qb3" };
            Assert.IsFalse(src.OnAim(0.5f, -0.5f, 0.2f, false, "qb2"), "a stale screen's aim is ignored");
            Assert.IsFalse(src.Aiming);
            Assert.IsTrue(src.OnAim(0.5f, -0.5f, 0.2f, false, "qb3"));
            Assert.IsTrue(src.Aiming);
            Assert.AreEqual(0.2f, src.AimArc);
            Assert.IsFalse(src.Poll().ThrowAt, "aiming alone throws nothing");
            Assert.IsTrue(src.OnAim(0f, 0f, 0f, true, "qb3"));
            Assert.IsFalse(src.Aiming, "cancelled");

            Assert.IsTrue(src.OnThrowStick(0.6f, -0.4f, 0.9f, "qb3"));
            var i = src.Poll();
            Assert.IsTrue(i.ThrowAt);
            Assert.AreEqual(0.6f, i.AimX, 1e-6f);
            Assert.AreEqual(0.4f, i.AimZ, 1e-6f, "stick up (y < 0) is away from the camera (+Z), like the move stick");
            Assert.AreEqual(0.9f, i.Arc, 1e-6f);
            Assert.IsFalse(src.Poll().ThrowAt, "one throw per let-go");
            Assert.IsFalse(src.OnThrowStick(0.6f, -0.4f, 0.9f, "old"), "the aid guard");
        }
    }
}
