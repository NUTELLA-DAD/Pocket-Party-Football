using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // Drive blocks (Sim/DriveBlock.cs, FEATURES.driveBlock): a person holding BLOCK shoves his man with the drive stick.
    public sealed class DriveBlockTests
    {
        const int Qb = 500;

        static Dictionary<int, PlayerInput> In(int slot, PlayerInput i) => new Dictionary<int, PlayerInput> { { slot, i } };

        static void Pin(Player p, float x, float z) { p.X = x; p.Z = z; p.VX = p.VZ = 0f; p.HomeX = x; p.HomeZ = z; }

        // Hiked, then just three men left: a person at QB (holding the ball, five yards back), a person at OL on the
        // line and a person at DL right across from him, rushing straight at the QB (Drive pushes his stick that way),
        // held up: the blocker is between him and the QB.
        static World Scene(bool on, out Player o, out Player d, string oPos = "OL", string dPos = "DL")
        {
            var cfg = ConfigTests.Load();
            cfg.features["driveBlock"] = on;
            cfg.features["defmoves"] = false;   // (no bot swim or bull rush shedding the block mid-test)
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, Qb);
            w.Calls[w.Defense] = "cover3";
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.AreEqual(Phase.Set, w.Phase);
            w.Step(In(Qb, new PlayerInput { Hike = true }));
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

        // Runs n ticks with the OL holding BLOCK and the drive stick at (dx, dz) (field frame), every other defender
        // who is a person rushing straight at the QB.
        static void Drive(World w, Player o, float dx, float dz, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var inputs = new Dictionary<int, PlayerInput>
                {
                    { o.Slot, new PlayerInput { Block = true, DriveX = dx, DriveZ = dz } },
                    { Qb, new PlayerInput() },
                };
                foreach (var p in w.Players) if (p.Human && p.Team == w.Defense) inputs[p.Slot] = new PlayerInput { MoveX = -w.Dir };
                w.Step(inputs);
            }
        }

        // ---- the switch ----

        [Test]
        public void OffMeansOffAndNoDriveBlockTurnsItOff()
        {
            var a = Scene(false, out var oa, out _);
            var b = Scene(false, out var ob, out _);
            Drive(a, oa, 0f, 1f, 20);
            Drive(b, ob, 0f, 0f, 20);
            Assert.AreEqual(b.Hash(), a.Hash(), "with the switch off the drive stick changes nothing");
            Assert.AreEqual(0, a.Drive.Pushes.Count);

            var cfg = ConfigTests.Load();
            cfg.features["driveBlock"] = true;
            ConfigLoader.ApplyEnvOverrides(cfg, k => k == "NO_DRIVE_BLOCK" ? "1" : null);
            Assert.IsFalse(cfg.Feature("driveBlock"));
        }

        // ---- the shove ----

        [Test]
        public void SidewaysShoveMovesTheManAndTheBlockerStaysOnHim()
        {
            var w = Scene(true, out var o, out var d);
            var c = Scene(true, out _, out var dc);
            Drive(w, o, 0f, 1f, 20);
            Drive(c, c.Find(o.Slot), 0f, 0f, 20);
            Assert.Greater(d.Z - dc.Z, 1.2f, "two thirds of a second of a full sideways shove moves him well over a yard");
            float dx = d.X - o.X, dz = d.Z - o.Z;
            Assert.LessOrEqual(dx * dx + dz * dz, w.Cfg.bots.blockReachYd * w.Cfg.bots.blockReachYd, "the blocker steps with him");
            Assert.Greater(d.BlockedTicks, 0, "still held up");
            Assert.AreEqual(1, w.Drive.Pushes.Count);
            Assert.AreEqual(d.Slot, w.Drive.Pushes[0].Defender);
            Assert.Greater(w.Drive.Pushes[0].VZ, 0f);
        }

        [Test]
        public void TurningHimSidewaysBeatsDrivingHimStraightBack()
        {
            var side = Scene(true, out var os, out var ds);
            var back = Scene(true, out var ob, out var db);
            var none = Scene(true, out var on, out var dn);
            Drive(side, os, 0f, 1f, 15);
            Drive(back, ob, side.Dir, 0f, 15);   // back toward his own end: straight against his rush at the QB
            Drive(none, on, 0f, 0f, 15);
            float sideways = ds.Z - dn.Z, straightBack = (db.X - dn.X) * back.Dir;
            Assert.Greater(sideways, 0f);
            Assert.Greater(sideways, straightBack * 2f, "sideways " + sideways + " vs back " + straightBack);
        }

        [Test]
        public void FightingTheShoveRunsTheShedClockFaster()
        {
            var back = Scene(true, out var ob, out var db);
            var none = Scene(true, out var on, out var dn);
            Drive(back, ob, back.Dir, 0f, 10);
            Drive(none, on, 0f, 0f, 10);
            Assert.Greater(db.BlockedTicks, dn.BlockedTicks, "a man fighting the drive gets free sooner");
        }

        [Test]
        public void BiggerBlockerShovesHarder()
        {
            var w = Scene(true, out var o, out var d);
            var lb = new Player { Pos = "LB" };
            var te = new Player { Pos = "TE" };
            float olDl = DriveBlock.Strength(w, o, d), olLb = DriveBlock.Strength(w, o, lb), teDl = DriveBlock.Strength(w, te, d);
            Assert.Greater(olLb, olDl, "an OL moves a linebacker more easily than a lineman");
            Assert.Greater(olDl, teDl, "an OL moves a lineman more easily than a TE does");
            Assert.GreaterOrEqual(teDl, w.Cfg.driveBlock.minFrac);
            w.Cfg.features["ratings"] = false;
            Assert.AreEqual(1f, DriveBlock.Strength(w, te, d), 1e-6f, "no ratings: everyone shoves the same");
        }

        [Test]
        public void DrivingCostsStamina()
        {
            var w = Scene(true, out var o, out _);
            var c = Scene(true, out var oc, out _);
            Drive(w, o, 0f, 1f, 20);
            Drive(c, oc, 0f, 0f, 20);
            Assert.Less(o.Stamina, oc.Stamina);
        }

        [Test]
        public void NoShoveWithoutAManHeldOrInTheDeadZoneOrForABot()
        {
            var w = Scene(true, out var o, out var d);
            Drive(w, o, 0f, w.Cfg.driveBlock.deadZone * 0.5f, 3);
            Assert.AreEqual(0, w.Drive.Pushes.Count, "a stick in the dead zone is just BLOCK");

            var far = Scene(true, out var of, out var df);
            Pin(df, far.LineOfScrimmageX + far.Dir * 6f, 8f);
            Drive(far, of, 0f, 1f, 3);
            Assert.AreEqual(0, far.Drive.Pushes.Count, "nobody held: nothing to shove");

            var bot = Scene(true, out var ob, out _);
            ob.Human = false;
            Drive(bot, ob, 0f, 1f, 3);
            Assert.AreEqual(0, bot.Drive.Pushes.Count, "bots never drive");
        }

        // ---- the lane on the TV ----

        // The carrier at the line; three defenders in the box at z = -2, 0.5 and 4 (the widest gap 3.5 yards).
        static World LaneScene(out Player left, out Player mid, out Player right)
        {
            var w = Scene(true, out var o, out var d);
            w.RemovePlayer(o.Slot);
            mid = d;
            left = w.AddPlayer(700, w.Defense, false);
            right = w.AddPlayer(701, w.Defense, false);
            float x = w.LineOfScrimmageX + w.Dir * 1f;
            Pin(left, x, -2f); Pin(mid, x, 0.5f); Pin(right, x, 4f);
            Pin(w.Find(Qb), w.LineOfScrimmageX - w.Dir * 2f, 0f);
            return w;
        }

        [Test]
        public void TheLaneOpensWhereAShovedManLeftAGap()
        {
            var w = LaneScene(out var left, out var mid, out var right);
            Assert.IsFalse(DriveBlock.Lane(w, out _, out _), "a gap nobody shoved open is not shown");
            w.Drive.DrivenAt[mid.Slot] = w.Tick;
            Assert.IsTrue(DriveBlock.Lane(w, out float z, out float width));
            Assert.AreEqual(2.25f, z, 1e-4f);
            Assert.AreEqual(3.5f, width, 1e-4f);

            right.Z = mid.Z + w.Cfg.driveBlock.laneOpenYd * 0.5f;   // closed again, and the other gap is 2.5
            Assert.IsFalse(DriveBlock.Lane(w, out _, out _), "too narrow to run through");
        }

        [Test]
        public void TheLaneGoesAwayAfterAThrowOrOnceTheRunnerIsThrough()
        {
            var w = LaneScene(out _, out var mid, out _);
            w.Drive.DrivenAt[mid.Slot] = w.Tick;
            Assert.IsTrue(DriveBlock.Lane(w, out _, out _));
            var qb = w.Find(Qb);
            qb.X = w.LineOfScrimmageX + w.Dir * (w.Cfg.driveBlock.laneDepthYd + 1f);
            Assert.IsFalse(DriveBlock.Lane(w, out _, out _), "past the box: no lane");
            qb.X = w.LineOfScrimmageX - w.Dir * 2f;
            w.Thrown = true;
            Assert.IsFalse(DriveBlock.Lane(w, out _, out _), "a pass: no lane");
        }

        // ---- the phone ----

        [Test]
        public void ThePhonesDriveStickCountsOnlyWhileBlockIsHeld()
        {
            var src = new PhoneSource(new Seat { Slot = 0 }) { Aid = "block1" };
            Assert.IsFalse(src.OnDrive(1f, 0f, "stale"), "the aid guard");
            Assert.IsTrue(src.OnDrive(1f, -0.5f, "block1"));
            var i = src.Poll();
            Assert.AreEqual(0f, i.DriveX, "no BLOCK held: no drive");
            Assert.IsTrue(src.OnButton("block", true, "block1"));
            i = src.Poll();
            Assert.IsTrue(i.Block);
            Assert.AreEqual(1f, i.DriveX);
            Assert.AreEqual(0.5f, i.DriveZ, 1e-6f, "phone y is down, field z is away from the camera");
            Assert.IsTrue(src.OnButton("block", false, "block1"));
            Assert.IsTrue(src.OnButton("block", true, "block1"));
            i = src.Poll();
            Assert.AreEqual(0f, i.DriveX, "letting go of BLOCK zeroed the drive");
        }
    }
}
