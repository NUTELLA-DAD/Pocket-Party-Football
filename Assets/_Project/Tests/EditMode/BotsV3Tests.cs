using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // Bots v3 (Sim/BotsV3.cs, FEATURES.botsV3): bots take in a change at different moments instead of all turning on
    // the same tick, the team with the ball hands its blockers out one man each and meets the coverage head on, and
    // kick coverage runs lanes while the return team sets up a wall.
    public sealed class BotsV3Tests
    {
        // The game as it ships (every switch on, kicking included), with bots v3 on or off.
        public static GameConfig Cfg(bool v3)
        {
            var cfg = ConfigTests.Load(ConfigTests.AsShipped);
            var keys = new List<string>(cfg.features.Keys);
            foreach (var k in keys) cfg.features[k] = true;
            cfg.features["botsV3"] = v3;
            return cfg;
        }

        static World Game(bool v3, int seed = 1) => WorldTests.Full(Cfg(v3), seed);

        static int Ticks(World w, float s) => (int)(s * w.Cfg.sim.tickHz);

        // Runs until the next tick with a throw in the live play (the throw lands in the events of the tick it is made).
        static bool UntilAThrow(World w, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                w.Step(null);
                foreach (var e in w.Events.ThisTick) if (e.Kind == EventKind.Throw && w.Phase == Phase.Live) return true;
            }
            return false;
        }

        [Test]
        public void DefendersTakeInAThrowAtDifferentMoments()
        {
            var w = Game(true);
            int throws = 0, spreads = 0;
            var seen = new SortedSet<int>();
            for (int n = 0; n < 6 && UntilAThrow(w, Ticks(w, 120f)); n++)
            {
                throws++;
                w.Step(null);   // the tick after: every bot has now looked at the ball in the air and set his reaction
                seen.Clear();
                int near = 0;
                foreach (var p in w.Players)
                {
                    if (p.Team == w.Offense) continue;
                    if (p.ReactTick >= 0) seen.Add(p.ReactTick); else near++;
                }
                TestContext.WriteLine("throw " + throws + " at tick " + w.Tick + ": " + seen.Count + " different reaction ticks (" + string.Join(" ", seen) + "), " + near + " on it at once");
                if (seen.Count >= 3 && seen.Max - seen.Min >= 3) spreads++;
            }
            Assert.GreaterOrEqual(throws, 3, "the bots throw passes");
            Assert.GreaterOrEqual(spreads, throws / 2, "after a throw the defenders turn at different ticks, not all at once");
        }

        [Test]
        public void WithV3OffEveryoneTurnsOnTheSameTick()
        {
            var w = Game(false);
            Assert.IsTrue(UntilAThrow(w, Ticks(w, 120f)));
            w.Step(null);
            foreach (var p in w.Players) Assert.AreEqual(-1, p.ReactTick, "no reactions queued with the switch off");
        }

        [Test]
        public void TheOffenseGoesOnTheSnapTheDefenseHasToSeeIt()
        {
            var w = Game(true);
            // past the opening kickoff to the first scrimmage snap
            int hikes = 0;
            for (int i = 0; i < Ticks(w, 120f); i++)
            {
                w.Step(null);
                bool hiked = false;
                foreach (var e in w.Events.ThisTick) if (e.Kind == EventKind.Hike) hiked = true;
                if (hiked && Kicking.Mode(w) == KickKind.None) { hikes++; break; }
            }
            Assert.AreEqual(1, hikes, "a scrimmage play was snapped");
            w.Step(null);
            int offenseWaiting = 0, defenseWaiting = 0, defenseOnIt = 0;
            foreach (var p in w.Players)
            {
                bool waiting = p.ReactTick > w.Tick;
                if (p.Team == w.Offense) { if (waiting) offenseWaiting++; }
                else if (waiting) defenseWaiting++; else defenseOnIt++;
            }
            TestContext.WriteLine("one tick after the snap: offense waiting " + offenseWaiting + ", defense waiting " + defenseWaiting + ", defense already moving " + defenseOnIt);
            Assert.AreEqual(0, offenseWaiting, "the offense knows the count");
            Assert.Greater(defenseWaiting, 0, "the deep defenders take a moment to see the snap");
        }

        // What a kick return looked like: ticks in which a coverage man was held up by a block, and how many of the
        // return team's blockers were out in front of the returner (toward the coverage) a second after the catch.
        public sealed class Returns
        {
            public int Count, HeldTicks, InFront, Blockers, Yards;
            public override string ToString() => Count + " returns, " + HeldTicks + " held-up ticks (" + (HeldTicks / (float)System.Math.Max(1, Count)).ToString("0.0") + " a return), "
                + InFront + " of " + Blockers + " blockers in front a second after the catch, " + (Yards / (float)System.Math.Max(1, Count)).ToString("0.0") + " yards a return";
        }

        public static Returns Measure(bool v3, int games, int minutes)
        {
            var r = new Returns();
            for (int seed = 1; seed <= games; seed++)
            {
                var w = Game(v3, seed);
                bool was = false; int caughtAt = -1; float startX = 0f;
                for (int i = 0; i < minutes * 60 * w.Cfg.sim.tickHz; i++)
                {
                    w.Step(null);
                    bool returning = w.Phase == Phase.Live && w.Kick.Returned && Kicking.Mode(w) != KickKind.None;
                    var carrier = LivePlay.Carrier(w);
                    if (returning && !was && carrier != null) { r.Count++; caughtAt = w.Tick; startX = carrier.X; }
                    if (returning && carrier != null)
                    {
                        foreach (var p in w.Players) if (p.Team == w.Offense && p.BlockedTicks > 0) r.HeldTicks++;
                        if (w.Tick == caughtAt + w.Cfg.sim.tickHz)
                        {
                            float way = -w.Dir;   // the returner runs against the kicking team's way
                            foreach (var p in w.Players)
                            {
                                if (p.Team == w.Offense || p == carrier) continue;
                                r.Blockers++;
                                if ((p.X - carrier.X) * way > 0f) r.InFront++;
                            }
                        }
                    }
                    if (was && !returning && w.Phase != Phase.Live && caughtAt >= 0)
                    {
                        // (the whistle came this tick or earlier; the spot the next play starts from says how far he got)
                        r.Yards += (int)System.Math.Round((startX - w.NextLos) * w.Dir);
                        caughtAt = -1;
                    }
                    was = returning;
                }
            }
            return r;
        }

        [Test]
        public void TheReturnTeamBlocksForTheReturner()
        {
            var on = Measure(true, 3, 10);
            var off = Measure(false, 3, 10);
            TestContext.WriteLine("bots v3 on:  " + on);
            TestContext.WriteLine("bots v3 off: " + off);
            Assert.GreaterOrEqual(on.Count, 6, "kicks get returned");
            Assert.Greater(on.HeldTicks / (float)on.Count, off.HeldTicks / (float)System.Math.Max(1, off.Count), "the return team holds coverage men up more than v2 did");
            Assert.Greater(on.InFront, on.Blockers / 2, "most blockers are out in front of the returner a second after the catch");
        }

        [Test]
        public void RunBlockersEachTakeADifferentMan()
        {
            var w = Game(true);
            for (int i = 0; i < Ticks(w, 60f) && w.Phase != Phase.Huddle; i++) w.Step(null);   // past the kickoff
            w.Calls[w.Offense] = "dive"; w.Calls[w.Defense] = "cover3";
            for (int i = 0; i < Ticks(w, 30f) && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            // the handoff, then a second of the run
            bool handed = false;
            for (int i = 0; i < Ticks(w, 3f) && !handed; i++)
            {
                w.Step(null);
                foreach (var e in w.Events.ThisTick) if (e.Kind == EventKind.Handoff) handed = true;
            }
            Assert.IsTrue(handed, "the dive hands off");
            for (int i = 0; i < w.Cfg.sim.tickHz && w.Phase == Phase.Live; i++) w.Step(null);
            if (w.Phase != Phase.Live) Assert.Inconclusive("the run was over inside a second");
            var taken = new SortedDictionary<int, int>();
            int blockers = 0;
            var sb = new StringBuilder();
            foreach (var p in w.Players)
            {
                if (p.Team != w.Offense || !p.Blocking) continue;
                blockers++;
                sb.Append(p.Pos).Append('#').Append(p.Number).Append(p.BlockSlot >= 0 ? " on " + p.BlockSlot : " convoy").Append(", ");
                if (p.BlockSlot >= 0) taken[p.BlockSlot] = (taken.TryGetValue(p.BlockSlot, out int n) ? n : 0) + 1;
            }
            TestContext.WriteLine(sb.ToString());
            Assert.GreaterOrEqual(blockers, 6, "the offense blocks on a run");
            Assert.GreaterOrEqual(taken.Count, 4, "at least four defenders have a blocker on them");
            foreach (var kv in taken) Assert.LessOrEqual(kv.Value, 1, "no two blockers on defender slot " + kv.Key);
        }

        [Test]
        public void KickCoverageRunsLanes()
        {
            var w = Game(true);
            // the opening kickoff: wait for the kick, then half a second
            for (int i = 0; i < Ticks(w, 30f) && !(w.Phase == Phase.Live && w.Kick.Kicked); i++) w.Step(null);
            Assert.IsTrue(w.Kick.Kicked, "the game opens with a kickoff");
            for (int i = 0; i < Ticks(w, 1.5f) && !w.Kick.Returned; i++) w.Step(null);
            var zs = new List<float>();
            foreach (var p in w.Players) if (p.Team == w.Offense && p != Kicking.Kicker(w)) zs.Add(p.Z);
            zs.Sort();
            float spread = zs[zs.Count - 1] - zs[0];
            TestContext.WriteLine("coverage across the field 1.5 s after the kick: " + string.Join(" ", zs.ConvertAll(z => z.ToString("0"))) + " (spread " + spread.ToString("0") + " yd)");
            Assert.Greater(spread, 25f, "the coverage keeps its lanes instead of bunching on the ball");
        }

        [Test]
        public void SameSeedSameGameWithV3()
        {
            var a = Headless.RunBots(Cfg(true), 7, 20, 22);
            var b = Headless.RunBots(Cfg(true), 7, 20, 22);
            CollectionAssert.AreEqual(a, b);
        }
    }
}
