using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.pursuitLanes (Sim/Pursuit.cs): a defense chasing the ball carrier takes real meeting points and keeps its
    // lanes, instead of every man running at the same spot and piling up into one clump on the carrier's heels.
    public sealed class PursuitTests
    {
        [Test]
        public void TheSwitchIsInGameJsonAndOffByEnv()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("pursuitLanes"));
            Assert.AreEqual("NO_PURSUIT_LANES", ConfigLoader.EnvName("pursuitLanes"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_PURSUIT_LANES", "1" } }).Feature("pursuitLanes"));
        }

        [Test]
        public void TheMeetingPointIsWhereBothGetThereTogether()
        {
            // a carrier at (10, 0) running +x at 7; a chaser at the origin who runs 9
            float t = Pursuit.MeetS(0f, 0f, 9f, 10f, 0f, 7f, 0f);
            Assert.AreEqual(10f / (9f - 7f), t, 1e-3f, "straight behind him: the gap closes at the difference in speed");
            // from the side: the distance he runs is his speed times the same time
            t = Pursuit.MeetS(0f, -8f, 9f, 0f, 0f, 7f, 0f);
            Assert.Greater(t, 0f);
            float mx = 7f * t, mz = 0f;
            Assert.AreEqual(9f * t, (float)System.Math.Sqrt(mx * mx + (mz + 8f) * (mz + 8f)), 1e-3f);
            // a slower man straight behind a faster one never catches him
            Assert.Less(Pursuit.MeetS(0f, 0f, 6f, 10f, 0f, 8f, 0f), 0f);
        }

        // One carrier running +x at 8 down the middle, six chasers in a row across the field ten yards behind him.
        static (World w, Player carrier, List<Player> chasers) Chase(bool lanes)
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            cfg.features["pursuitLanes"] = lanes;
            var w = new World(cfg, 3);
            var c = w.AddPlayer(0, w.Offense, false);
            var list = new List<Player>();
            for (int i = 0; i < 6; i++) list.Add(w.AddPlayer(1 + i, w.Defense, false));
            w.Ball.GiveTo(c, cfg.ball);
            w.Phase = Phase.Live;
            c.X = 0f; c.Z = 0f; c.VX = w.Dir * 8f; c.VZ = 0f;
            for (int i = 0; i < list.Count; i++) { list[i].X = -w.Dir * 10f; list[i].Z = -7.5f + 3f * i; list[i].VX = list[i].VZ = 0f; }
            return (w, c, list);
        }

        [Test]
        public void ChasersBehindHimFanOutInTheirOwnLanes()
        {
            var (w, c, list) = Chase(true);
            var k = w.Cfg.pursuit;
            foreach (var p in list) Pursuit.Chase(w, p, c);
            var plan = w.ChasePlan;
            Assert.AreEqual(w.Tick, plan.Tick);
            Assert.AreEqual(c.Slot, plan.Carrier);
            var aims = new List<float>();
            foreach (var p in list) { Assert.IsTrue(plan.Aim.ContainsKey(p.Slot)); aims.Add(plan.Aim[p.Slot][1]); }
            for (int i = 1; i < aims.Count; i++)
            {
                TestContext.WriteLine("chaser at z " + list[i].Z + ": lane " + plan.Lane[list[i].Slot] + ", aims at z " + aims[i]);
                Assert.Greater(aims[i], aims[i - 1], "the lanes keep the order the chasers are in across the field: nobody crosses behind a team-mate");
                Assert.GreaterOrEqual(aims[i] - aims[i - 1], k.laneGapYd * k.trailLane * 0.5f, "and they stay apart");
            }
            Assert.LessOrEqual(aims[aims.Count - 1] - aims[0], k.laneMaxYd * 2f + 1e-3f, "no lane is further than laneMaxYd out");
        }

        // ---- the pursuit drill, on the game's own rules ----

        static Lab Drill(bool lanes, int seed, bool tackles)
        {
            var cfg = ConfigTests.Load();
            cfg.features["pursuitLanes"] = lanes;
            if (!tackles) { cfg.play.tackleBase = -100f; cfg.play.tackleFromBehindBonus = 0f; }   // nobody brings him down: the whole chase
            var w = new World(cfg, seed);
            var lab = new Lab(w, Lab.Parse(File.ReadAllText("Assets/_Project/Config/lab.json")));
            int i = lab.FindDrill("pursuit");
            Assert.GreaterOrEqual(i, 0, "the pursuit drill is in lab.json");
            lab.Pick(i);
            return lab;
        }

        // The runs the gauntlet uses (Boot/PursuitReel.cs keeps the same ones): straight, a sweep to the sideline, a
        // zigzag; +x is down the field (the lab attacks +x).
        public static PlayerInput Path(string path, int tick, float hz)
        {
            float s = tick / hz;
            switch (path)
            {
                case "sweep": return s < 1.4f ? new PlayerInput { MoveX = 0.55f, MoveZ = 0.83f, Sprint = true } : new PlayerInput { MoveX = 1f, MoveZ = 0.12f, Sprint = true };
                case "zigzag": return ((int)(s / 0.8f)) % 2 == 0 ? new PlayerInput { MoveX = 0.85f, MoveZ = 0.53f, Sprint = true } : new PlayerInput { MoveX = 0.85f, MoveZ = -0.53f, Sprint = true };
                default: return new PlayerInput { MoveX = 1f, Sprint = true };
            }
        }

        // One rep: (ticks of chaser pairs bunched up away from the ball, ticks run, how it ended).
        static (int bunched, int ticks, string result) Rep(Lab lab, string path)
        {
            var w = lab.W;
            int bunched = 0, ticks = 0, starts = lab.Starts;
            float far = w.Cfg.pursuit.convergeYd, close = w.Cfg.players.radiusYd * 2f + 0.4f;
            var input = new Dictionary<int, PlayerInput>();
            while (lab.Starts == starts && ticks < 30 * 20)
            {
                input[lab.You] = Path(path, ticks, w.Cfg.sim.tickHz);
                w.Step(input);
                if (w.Phase != Phase.Live) { string r = w.Result; lab.AfterTick(); return (bunched, ticks, r); }
                lab.AfterTick();
                ticks++;
                var c = LivePlay.Carrier(w);
                if (c == null) continue;
                var ds = new List<Player>();
                foreach (var p in w.Players) if (p.Team != c.Team && LivePlay.Dist(p, c) > far) ds.Add(p);
                for (int i = 0; i < ds.Count; i++)
                    for (int j = i + 1; j < ds.Count; j++)
                        if (LivePlay.Dist(ds[i], ds[j]) < close) bunched++;
            }
            return (bunched, ticks, w.Result);
        }

        [Test]
        public void AChaseFromBehindNoLongerPilesUpIntoOneClump()
        {
            int on = 0, off = 0;
            foreach (string path in new[] { "straight", "sweep", "zigzag" })
                for (int seed = 1; seed <= 3; seed++)
                {
                    var a = Rep(Drill(true, seed, false), path);
                    var b = Rep(Drill(false, seed, false), path);
                    TestContext.WriteLine(path + " seed " + seed + ": bunched pair-ticks " + a.bunched + " in lanes (" + a.ticks + " ticks), " + b.bunched + " without (" + b.ticks + " ticks)");
                    on += a.bunched; off += b.bunched;
                }
            TestContext.WriteLine("all: " + on + " in lanes, " + off + " without");
            Assert.Greater(off, 100, "without lanes the chase piles up (the bug this fixes)");
            Assert.Less(on, off / 3, "in lanes the chasers stay apart");
        }

        [Test]
        public void TheLanesStillBringHimDown()
        {
            int tackled = 0, reps = 0;
            foreach (string path in new[] { "straight", "sweep", "zigzag" })
                for (int seed = 1; seed <= 3; seed++)
                {
                    var r = Rep(Drill(true, seed, true), path);
                    reps++;
                    TestContext.WriteLine(path + " seed " + seed + ": " + r.result + " after " + r.ticks + " ticks");
                    if (r.result == "TACKLED") tackled++;
                }
            Assert.GreaterOrEqual(tackled, reps * 2 / 3, "most breakaways still end in a tackle");
        }
    }
}
