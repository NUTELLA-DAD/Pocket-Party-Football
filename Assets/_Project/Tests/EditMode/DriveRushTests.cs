using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // Drive rushes (Sim/DriveRush.cs, FEATURES.driveRush): a person held up by a blocker shoves him with the BULL RUSH stick.
    public sealed class DriveRushTests
    {
        const int Qb = 500;

        static void Pin(Player p, float x, float z) { p.X = x; p.Z = z; p.VX = p.VZ = 0f; p.HomeX = x; p.HomeZ = z; }

        // Hiked, then just three men left: a person at QB (holding the ball, five yards back), a person at OL on the
        // line holding BLOCK and a person at DL right across from him, rushing straight at the QB, held up: the
        // blocker is between him and the QB. The same scene as DriveBlockTests, seen from the other side of the ball.
        static World Scene(bool on, out Player o, out Player d, string oPos = "OL", string dPos = "DL")
        {
            var cfg = ConfigTests.Load();
            cfg.features["driveRush"] = on;
            cfg.features["driveBlock"] = false;   // (the blocker never drives back in these tests)
            cfg.features["defmoves"] = false;     // (no swim or bull rush roll shedding the block mid-test)
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, Qb);
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase);
            w.Step(new Dictionary<int, PlayerInput> { { Qb, new PlayerInput { Hike = true } } });
            Assert.AreEqual(Phase.Live, w.Phase);
            o = null; d = null;
            foreach (var p in w.Players)
            {
                if (o == null && p.Team == w.Offense && p.Pos == oPos) o = p;
                if (d == null && p.Team == w.Defense && p.Pos == dPos) d = p;
            }
            Assert.IsNotNull(o); Assert.IsNotNull(d);
            var keep = new HashSet<int> { Qb, o.Slot, d.Slot };
            var drop = new List<int>();
            foreach (var p in w.Players) if (!keep.Contains(p.Slot)) drop.Add(p.Slot);
            foreach (int s in drop) w.RemovePlayer(s);
            o.Human = true;
            d.Human = true;
            float los = w.LineOfScrimmageX;
            Pin(w.Find(Qb), los - w.Dir * 5f, 0f);
            Pin(o, los - w.Dir * 0.6f, 0f);
            Pin(d, los + w.Dir * 0.6f, 0f);
            Assert.AreSame(w.Find(Qb), LivePlay.Carrier(w));
            return w;
        }

        // Runs n ticks with the DL rushing straight at the QB with the BULL RUSH stick at (dx, dz) (field frame), the
        // OL holding BLOCK and standing still, the QB holding the ball.
        static void Drive(World w, Player d, float dx, float dz, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var inputs = new Dictionary<int, PlayerInput> { { Qb, new PlayerInput() } };
                foreach (var p in w.Players)
                {
                    if (!p.Human || p.Slot == Qb) continue;
                    if (p.Team == w.Offense) inputs[p.Slot] = new PlayerInput { Block = true };
                    else inputs[p.Slot] = p.Slot == d.Slot ? new PlayerInput { MoveX = -w.Dir, DriveX = dx, DriveZ = dz } : new PlayerInput { MoveX = -w.Dir };
                }
                w.Step(inputs);
            }
        }

        // ---- the switch ----

        [Test]
        public void OffMeansOffAndNoDriveRushTurnsItOff()
        {
            var a = Scene(false, out _, out var da);
            var b = Scene(false, out _, out var db);
            Drive(a, da, 0f, 1f, 20);
            Drive(b, db, 0f, 0f, 20);
            Assert.AreEqual(b.Hash(), a.Hash(), "with the switch off the stick changes nothing");
            Assert.AreEqual(0, a.Rush.Pushes.Count);

            var cfg = ConfigTests.Load();
            cfg.features["driveRush"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_DRIVE_RUSH" ? "1" : null);
            Assert.IsFalse(cfg.Feature("driveRush"));
        }

        // ---- the shove ----

        [Test]
        public void SidewaysShoveMovesTheBlockerAndTheRusherStaysOnHim()
        {
            var w = Scene(true, out var o, out var d);
            var c = Scene(true, out var oc, out var dc);
            Drive(w, d, 0f, 1f, 20);
            Drive(c, dc, 0f, 0f, 20);
            Assert.Greater(o.Z - oc.Z, 1f, "two thirds of a second of a full sideways shove moves the blocker about a yard and a half");
            float dx = d.X - o.X, dz = d.Z - o.Z;
            Assert.LessOrEqual(dx * dx + dz * dz, w.Cfg.bots.blockReachYd * w.Cfg.bots.blockReachYd, "the rusher steps with him");
            Assert.Greater(d.BlockedTicks, 0, "still held up");
            Assert.AreEqual(1, w.Rush.Pushes.Count);
            Assert.AreEqual(d.Slot, w.Rush.Pushes[0].Rusher);
            Assert.AreEqual(o.Slot, w.Rush.Pushes[0].Blocker);
            Assert.Greater(w.Rush.Pushes[0].VZ, 0f);
        }

        [Test]
        public void TurningHimSidewaysBeatsDrivingHimIntoTheQb()
        {
            var side = Scene(true, out var os, out var ds);
            var back = Scene(true, out var ob, out var db);
            var none = Scene(true, out var on, out var dn);
            Drive(side, ds, 0f, 1f, 15);
            Drive(back, db, -back.Dir, 0f, 15);   // toward his own QB: straight into the blocker's lean
            Drive(none, dn, 0f, 0f, 15);
            float sideways = os.Z - on.Z, intoQb = (on.X - ob.X) * back.Dir;
            Assert.Greater(sideways, 0f);
            Assert.Greater(sideways, intoQb * 2f, "sideways " + sideways + " vs into the QB " + intoQb);
        }

        [Test]
        public void ARipAcrossHimRunsTheShedClockFaster()
        {
            var side = Scene(true, out _, out var ds);
            var back = Scene(true, out _, out var db);
            var none = Scene(true, out _, out var dn);
            Drive(side, ds, 0f, 1f, 10);
            Drive(back, db, -back.Dir, 0f, 10);
            Drive(none, dn, 0f, 0f, 10);
            Assert.Greater(ds.BlockedTicks, dn.BlockedTicks, "a man ripping across his blocker gets free sooner");
            Assert.AreEqual(dn.BlockedTicks, db.BlockedTicks, "driving straight into him is no quicker a shed");
        }

        [Test]
        public void BiggerRusherShovesHarder()
        {
            var w = Scene(true, out var o, out var d);
            var lb = new Player { Pos = "LB" };
            var te = new Player { Pos = "TE" };
            float dlOl = DriveRush.Strength(w, d, o), dlTe = DriveRush.Strength(w, d, te), lbOl = DriveRush.Strength(w, lb, o);
            Assert.Greater(dlTe, dlOl, "a DL moves a tight end more easily than a lineman");
            Assert.Greater(dlOl, lbOl, "a DL moves a lineman more easily than a linebacker does");
            Assert.GreaterOrEqual(lbOl, w.Cfg.driveRush.minFrac);
            w.Cfg.features["ratings"] = false;
            Assert.AreEqual(1f, DriveRush.Strength(w, lb, o), 1e-6f, "no ratings: everyone shoves the same");
        }

        [Test]
        public void DrivingCostsStamina()
        {
            var w = Scene(true, out _, out var d);
            var c = Scene(true, out _, out var dc);
            Drive(w, d, 0f, 1f, 20);
            Drive(c, dc, 0f, 0f, 20);
            Assert.Less(d.Stamina, dc.Stamina);
        }

        [Test]
        public void NoShoveWithoutABlockerOrInTheDeadZoneOrForABot()
        {
            var w = Scene(true, out _, out var d);
            Drive(w, d, 0f, w.Cfg.driveRush.deadZone * 0.5f, 3);
            Assert.AreEqual(0, w.Rush.Pushes.Count, "a stick in the dead zone is just the bull rush press");

            var far = Scene(true, out var of, out var df);
            Pin(of, far.LineOfScrimmageX - far.Dir * 6f, 8f);
            Drive(far, df, 0f, 1f, 3);
            Assert.AreEqual(0, far.Rush.Pushes.Count, "nobody holding him: nothing to shove");

            var bot = Scene(true, out _, out var db);
            db.Human = false;
            Drive(bot, db, 0f, 1f, 3);
            Assert.AreEqual(0, bot.Rush.Pushes.Count, "bots never drive");
        }

        // ---- the phone ----

        [Test]
        public void ThePhonesStickCountsOnlyWhileBullRushIsHeldAndTheTouchIsStillABullRush()
        {
            var src = new PhoneSource(new Seat { Slot = 0 }) { Aid = "rush1" };
            Assert.IsTrue(src.OnDrive(1f, -0.5f, "rush1"));
            var i = src.Poll();
            Assert.AreEqual(0f, i.DriveX, "no BULL RUSH held: no drive");
            Assert.IsTrue(src.OnButton("bull", true, "rush1"));
            i = src.Poll();
            Assert.AreEqual(DefMove.BullRush, i.Def, "the touch is the bull rush press");
            Assert.AreEqual(1f, i.DriveX);
            Assert.AreEqual(0.5f, i.DriveZ, 1e-6f, "phone y is down, field z is away from the camera");
            i = src.Poll();
            Assert.AreEqual(DefMove.None, i.Def, "pressed once");
            Assert.AreEqual(1f, i.DriveX, "still held: still driving");
            Assert.IsTrue(src.OnButton("bull", false, "rush1"));
            Assert.IsTrue(src.OnButton("bull", true, "rush1"));
            i = src.Poll();
            Assert.AreEqual(0f, i.DriveX, "letting go of BULL RUSH zeroed the drive");
        }
    }
}
