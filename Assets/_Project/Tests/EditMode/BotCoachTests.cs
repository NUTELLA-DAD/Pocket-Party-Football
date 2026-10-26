using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-06: the bot coach calls plays for the down, the distance, the field and the score; bots v2 QBs scramble.
    public sealed class BotCoachTests
    {
        public static GameConfig On(Dictionary<string, string> env = null)
        {
            var cfg = ConfigTests.Load(env);
            cfg.features["botcoach"] = true;
            cfg.features["botsV2"] = true;
            return cfg;
        }

        // A world at a given spot: down, yards to go, yards to the goal line.
        static World At(GameConfig cfg, int down, float togo, float toGoal, int seed = 1)
        {
            var w = WorldTests.Full(cfg, seed);
            w.Step(null);   // the first huddle
            w.LineOfScrimmageX = w.Dir * (cfg.field.GoalLineX - toGoal);
            w.Down = down;
            float line = w.LineOfScrimmageX + w.Dir * togo;
            w.FirstDownX = line * w.Dir > cfg.field.GoalLineX ? w.Dir * cfg.field.GoalLineX : line;
            return w;
        }

        static SortedDictionary<string, int> Calls(World w, int team, int n)
        {
            var c = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                string id = BotCoach.Call(w, team);
                c[id] = (c.TryGetValue(id, out int k) ? k : 0) + 1;
            }
            return c;
        }

        static int Sum(SortedDictionary<string, int> c, params string[] ids)
        {
            int n = 0;
            foreach (var id in ids) if (c.TryGetValue(id, out int k)) n += k;
            return n;
        }

        static string Show(SortedDictionary<string, int> c)
        {
            var parts = new List<string>();
            foreach (var kv in c) parts.Add(kv.Key + " " + kv.Value);
            return string.Join(", ", parts);
        }

        [Test]
        public void TheCoachIsOffUntilSwitchedOn()
        {
            var cfg = ConfigTests.Load();
            Assert.IsFalse(cfg.Feature("botcoach"), "ships off, so the pinned bot games don't change");
            Assert.IsFalse(cfg.Feature("botsV2"));
            var w = At(cfg, 1, 10, 60);
            Assert.IsNull(BotCoach.Call(w, w.Offense));
            var off = On();
            ConfigLoader.ApplyEnvOverrides(off, k => k == "NO_BOTCOACH" || k == "NO_BOTS_V2" ? "1" : null);
            Assert.IsFalse(off.Feature("botcoach"), "NO_BOTCOACH=1 turns it off");
            Assert.IsFalse(off.Feature("botsV2"), "NO_BOTS_V2=1 turns bots v2 off");
        }

        [Test]
        public void ItReadsTheSituation()
        {
            var cfg = On();
            var w = At(cfg, 3, 1, 8);
            CollectionAssert.AreEquivalent(new[] { "short", "goal", "late", "redzone", "fgrange" }, BotCoach.Tags(w, w.Offense));
            w = At(cfg, 1, 10, 95);
            CollectionAssert.AreEquivalent(new[] { "long", "backedup" }, BotCoach.Tags(w, w.Offense));
            w = At(cfg, 2, 5, 50);
            w.Score[w.Offense] = 14;
            CollectionAssert.AreEquivalent(new[] { "medium", "ahead" }, BotCoach.Tags(w, w.Offense));
            CollectionAssert.AreEquivalent(new[] { "medium", "behind" }, BotCoach.Tags(w, w.Defense));
            w = At(cfg, 4, 3, 50);
            CollectionAssert.Contains(BotCoach.Tags(w, w.Offense), "fourth");
        }

        [Test]
        public void ShortYardageRunsTheBall()
        {
            var cfg = On();
            var w = At(cfg, 3, 1, 60);
            var off = Calls(w, w.Offense, 400);
            TestContext.WriteLine("3rd and 1 offense: " + Show(off));
            Assert.Greater(Sum(off, "sneak", "dive", "toss"), 400 / 2, "mostly runs: " + Show(off));
            Assert.Less(Sum(off, "verts"), 400 / 10, "hardly a deep shot: " + Show(off));
            var def = Calls(w, w.Defense, 400);
            TestContext.WriteLine("3rd and 1 defense: " + Show(def));
            Assert.Greater(Sum(def, "goalline", "mblitz", "zblitz"), 400 / 2, "the defense stacks the box: " + Show(def));
        }

        [Test]
        public void LongYardagePassesTheBall()
        {
            var cfg = On();
            var w = At(cfg, 3, 15, 60);
            var off = Calls(w, w.Offense, 400);
            TestContext.WriteLine("3rd and 15 offense: " + Show(off));
            Assert.Greater(Sum(off, "verts", "slants", "boot", "screen"), 400 * 3 / 4, "mostly passes: " + Show(off));
            Assert.Less(Sum(off, "sneak"), 400 / 20, "no sneak on 3rd and 15: " + Show(off));
            var def = Calls(w, w.Defense, 400);
            TestContext.WriteLine("3rd and 15 defense: " + Show(def));
            Assert.Greater(Sum(def, "cover2", "cover3", "prevent"), 400 / 2, "the defense drops into coverage: " + Show(def));
            Assert.Less(Sum(def, "goalline"), 400 / 10, "no goal line defense: " + Show(def));
        }

        [Test]
        public void NoDeepShotsOrPreventInTheRedZone()
        {
            var cfg = On();
            var w = At(cfg, 1, 8, 8);
            var off = Calls(w, w.Offense, 400);
            var def = Calls(w, w.Defense, 400);
            TestContext.WriteLine("1st and goal at the 8: " + Show(off) + " / " + Show(def));
            Assert.AreEqual(0, Sum(off, "verts"), Show(off));
            Assert.AreEqual(0, Sum(def, "prevent"), Show(def));
        }

        [Test]
        public void ItOnlyCallsPlaysFromThePlaybook()
        {
            var cfg = On();
            var w = At(cfg, 4, 12, 70);
            var off = Calls(w, w.Offense, 200);
            foreach (var kv in off) Assert.AreEqual("offense", Huddle.Find(w, kv.Key).side, kv.Key);
            foreach (var kv in Calls(w, w.Defense, 200)) Assert.AreEqual("defense", Huddle.Find(w, kv.Key).side, kv.Key);
        }

        [Test]
        public void SameSeedSameCalls()
        {
            var cfg = On();
            var a = At(cfg, 2, 6, 40, 7);
            var b = At(cfg, 2, 6, 40, 7);
            for (int i = 0; i < 50; i++) Assert.AreEqual(BotCoach.Call(a, a.Offense), BotCoach.Call(b, b.Offense));
        }

        [Test]
        public void APersonsSuggestionStillBeatsTheCoach()
        {
            var cfg = On();
            var w = At(cfg, 3, 15, 60);
            w.ReplaceBot(w.Offense, 500);
            w.ReplaceBot(w.Offense, 501);
            Assert.IsTrue(Huddle.Suggest(w, 501, "sneak"));
            for (int i = 0; i < cfg.huddle.seconds * cfg.sim.tickHz + 1 && w.Phase == Phase.Huddle; i++) w.Step(null);
            Assert.AreEqual("sneak", w.Calls[w.Offense], "the coach goes with the thumbs-up, even on 3rd and 15");
        }

        // ---- bots v2: the QB ----

        static World Snap(GameConfig cfg, string offense, string defense, int seed = 1)
        {
            var w = WorldTests.Full(cfg, seed);
            w.Step(null);
            w.Calls[w.Offense] = offense; w.Calls[w.Defense] = defense;
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            return w;
        }

        [Test]
        public void TheQbScramblesWhenNobodyIsOpen()
        {
            var cfg = On();
            // nobody can get open: every receiver has a defender glued to him, so the QB tucks it and runs
            cfg.bots.deadlineOpenYd = cfg.field.lengthYd;
            cfg.bots.openYd = cfg.field.lengthYd;
            var w = Snap(cfg, "slants", "cover3");
            var qb = LivePlay.Qb(w);
            float deepest = float.MinValue;
            for (int i = 0; i < cfg.play.maxPlayS * cfg.sim.tickHz && w.Phase == Phase.Live; i++)
            {
                w.Step(null);
                deepest = System.Math.Max(deepest, (qb.X - w.LineOfScrimmageX) * w.Dir);
            }
            Assert.IsFalse(w.Thrown, "no one open: no throw");
            Assert.Greater(deepest, -cfg.formation.offense[0].back, "he left his drop and ran (got to " + deepest + ", result " + w.Result + ")");
        }

        [Test]
        public void TheQbNeverThrowsFromPastTheLine()
        {
            var cfg = On();
            cfg.bots.deadlineOpenYd = cfg.field.lengthYd;
            var w = Snap(cfg, "slants", "cover3");
            var qb = LivePlay.Qb(w);
            for (int i = 0; i < cfg.play.maxPlayS * cfg.sim.tickHz && w.Phase == Phase.Live; i++)
            {
                bool past = LivePlay.Carrier(w) == qb && (qb.X - w.LineOfScrimmageX) * w.Dir > 0f;
                bool before = w.Thrown;
                w.Step(null);
                if (past) Assert.AreEqual(before, w.Thrown, "a throw from past the line of scrimmage");
            }
        }

        [Test]
        public void TheSneakIsARun()
        {
            var cfg = On();
            var w = Snap(cfg, "sneak", "cover3");
            for (int i = 0; i < cfg.play.maxPlayS * cfg.sim.tickHz && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.IsFalse(w.Thrown, "the QB keeps it on a sneak");
            StringAssert.DoesNotContain("SACKED", w.Result);
        }

        [Test]
        public void TheReceiverRunsOnToMeetThePass()
        {
            var cfg = On();
            var w = Snap(cfg, "verts", "cover2");
            for (int i = 0; i < cfg.play.maxPlayS * cfg.sim.tickHz && w.Phase == Phase.Live && w.Ball.State != BallState.Flying; i++) w.Step(null);
            if (w.Ball.State != BallState.Flying) Assert.Inconclusive("no throw this seed: " + w.Result);
            var r = Bots.Intended(w);
            Assert.IsNotNull(r);
            float before = Dist(r, w.Ball.TargetX, w.Ball.TargetZ);
            for (int i = 0; i < cfg.sim.tickHz / 3 && w.Ball.State == BallState.Flying; i++) w.Step(null);
            Assert.Less(Dist(r, w.Ball.TargetX, w.Ball.TargetZ), before, "he closes on the spot the ball comes down");
        }

        static float Dist(Player p, float x, float z) { float dx = p.X - x, dz = p.Z - z; return (float)System.Math.Sqrt(dx * dx + dz * dz); }
    }
}
