using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.customPlays: the offense's caller draws routes on top of a pass play in the huddle; bots run them.
    public sealed class CustomPlaysTests
    {
        const int Qb = 500, Rb = 501, Mlb = 600, Wr = 502;

        // The first huddle, with people at QB, RB and the defense's middle LB. FEATURES.customPlays on unless `on` is
        // false; then the env switches, as the game applies them.
        static World InTheHuddle(bool on = true, Dictionary<string, string> env = null)
        {
            var cfg = ConfigTests.Load();
            cfg.features["customPlays"] = on;
            if (env != null) ConfigLoader.ApplyEnvOverrides(cfg, k => env.TryGetValue(k, out var v) ? v : null);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Offense, Rb);
            w.ReplaceBot(w.Defense, Mlb);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            return w;
        }

        static List<CustomRoute> Route(int spot, params float[][] pts) =>
            new List<CustomRoute> { new CustomRoute { spot = spot, pts = new List<float[]>(pts) } };

        static Dictionary<int, PlayerInput> Hike => new Dictionary<int, PlayerInput> { { Qb, new PlayerInput { Hike = true } } };

        [Test]
        public void ShipsOnAndNoCustomPlaysTurnsItOff()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("customPlays"), "on in game.json");
            Assert.AreEqual("NO_CUSTOM_PLAYS", ConfigLoader.EnvName("customPlays"));
            var off = InTheHuddle(on: false);
            Assert.IsFalse(CustomPlays.Lock(off, Qb, "slants", Route(2, new[] { 10f, -20f })));
            Assert.IsFalse(Huddle.IsLocked(off, off.Offense));
            var env = InTheHuddle(env: new Dictionary<string, string> { { "NO_CUSTOM_PLAYS", "1" } });
            Assert.IsFalse(CustomPlays.Lock(env, Qb, "slants", Route(2, new[] { 10f, -20f })), "NO_CUSTOM_PLAYS=1");
        }

        [Test]
        public void TheConfigBlockIsSane()
        {
            var c = ConfigTests.Load().customPlays;
            Assert.Greater(c.maxPoints, 1);
            Assert.Greater(c.maxAheadYd, 0f);
            Assert.Greater(c.maxBackYd, 0f);
            Assert.GreaterOrEqual(c.sidelineInYd, 0f);
            Assert.IsNotEmpty(c.name);
            Assert.IsNotEmpty(c.text);
        }

        [Test]
        public void OnlyTheOffensesCallerDrawsInTheHuddle()
        {
            var w = InTheHuddle();
            Assert.IsTrue(CustomPlays.CanDraw(w, w.Find(Qb)));
            Assert.IsFalse(CustomPlays.CanDraw(w, w.Find(Rb)), "a teammate suggests, he doesn't call");
            Assert.IsFalse(CustomPlays.CanDraw(w, w.Find(Mlb)), "the defense has nothing to draw");
            Assert.IsFalse(CustomPlays.Lock(w, Rb, "slants", Route(2, new[] { 10f, -20f })));
            Assert.IsFalse(CustomPlays.Lock(w, Mlb, "cover3", Route(2, new[] { 10f, -20f })));
            Assert.IsTrue(CustomPlays.Lock(w, Qb, "slants", Route(2, new[] { 10f, -20f })));
            Assert.AreEqual("slants", w.Calls[w.Offense], "the base play is the call");
            Assert.IsFalse(CustomPlays.CanDraw(w, w.Find(Qb)), "locked: nothing more to draw");
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "verts", Route(2, new[] { 10f, -20f })));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "verts", Route(2, new[] { 10f, -20f })), "not at the line");
        }

        [Test]
        public void OnlyAPassPlayCanBeTheBase()
        {
            var w = InTheHuddle();
            int team = w.Offense;
            foreach (var id in new[] { "slants", "verts", "screen", "boot" }) Assert.IsTrue(CustomPlays.IsBase(w, team, Huddle.Find(w, id)), id);
            foreach (var id in new[] { "dive", "toss" }) Assert.IsFalse(CustomPlays.IsBase(w, team, Huddle.Find(w, id)), id + " is a handoff");
            Assert.IsFalse(CustomPlays.IsBase(w, team, Huddle.Find(w, "sneak")), "the sneak is a QB run");
            Assert.IsFalse(CustomPlays.IsBase(w, team, Huddle.Find(w, "punt")), "a kick (and not in the book on 1st down)");
            Assert.IsFalse(CustomPlays.IsBase(w, team, Huddle.Find(w, "cover3")), "a defense play");
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "dive", Route(2, new[] { 10f, -20f })));
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "nope", Route(2, new[] { 10f, -20f })));
        }

        [Test]
        public void ABadDrawingIsRefused()
        {
            var w = InTheHuddle();
            var c = w.Cfg.customPlays;
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", new List<CustomRoute>()), "nothing drawn");
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", null));
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", Route(0, new[] { 10f, 0f })), "the QB is not a receiver");
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", Route(6, new[] { 10f, 0f })), "nor is the center");
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", Route(2)), "a route with no points");
            var many = new float[c.maxPoints + 1][];
            for (int i = 0; i < many.Length; i++) many[i] = new[] { i + 1f, -20f };
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", Route(2, many)), "more than maxPoints");
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", Route(2, new[] { float.NaN, 0f })));
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", Route(2, new[] { 10f })), "a point needs ahead and across");
            var twice = Route(2, new[] { 10f, -20f });
            twice.AddRange(Route(2, new[] { 5f, -20f }));
            Assert.IsFalse(CustomPlays.Lock(w, Qb, "slants", twice), "one route per receiver");
            Assert.IsFalse(Huddle.IsLocked(w, w.Offense), "none of those called a play");
        }

        [Test]
        public void PointsOffTheFieldArePulledBackOn()
        {
            var w = InTheHuddle();
            var c = w.Cfg.customPlays;
            Assert.IsTrue(CustomPlays.Lock(w, Qb, "slants", Route(2, new[] { 999f, -999f }, new[] { -999f, 999f })));
            var job = CustomPlays.JobOf(w, LivePlay.SpotOf(w, w.Offense, 2));
            float side = w.Cfg.field.HalfWidth - c.sidelineInYd;
            Assert.AreEqual(c.maxAheadYd, job.pts[0][0], 1e-4f);
            Assert.AreEqual(-side, job.pts[0][1], 1e-4f);
            Assert.AreEqual(-c.maxBackYd, job.pts[1][0], 1e-4f);
            Assert.AreEqual(side, job.pts[1][1], 1e-4f);
            Assert.AreEqual("route", job.kind);
            Assert.AreEqual(c.text, job.text);
        }

        [Test]
        public void DrawnRoutesReplaceOnlyThoseReceivers()
        {
            var w = InTheHuddle();
            Assert.IsTrue(CustomPlays.Lock(w, Qb, "slants", Route(1, new[] { 3f, 6f }, new[] { 15f, 6f })));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            var rb = w.Find(Rb);
            var slants = Huddle.Find(w, "slants");
            Assert.AreEqual("block", slants.jobs[1].kind, "in Slants the RB stays in to block...");
            var drawn = Bots.JobOfCalled(w, rb);
            Assert.AreEqual("route", drawn.kind, "...but the caller drew him a route");
            Assert.AreEqual(15f, drawn.pts[1][0], 1e-4f);
            var wr = LivePlay.SpotOf(w, w.Offense, 2);
            Assert.AreSame(slants.jobs[2], Bots.JobOfCalled(w, wr), "an undrawn receiver runs the base play's route");
            Assert.IsTrue(CustomPlays.IsCustom(w, w.Offense));
            Assert.IsFalse(CustomPlays.IsCustom(w, w.Defense));
        }

        [Test]
        public void AHotRouteBeatsTheDrawingAndAnAudibleThrowsItAway()
        {
            var w = InTheHuddle();
            Assert.IsTrue(CustomPlays.Lock(w, Qb, "slants", Route(1, new[] { 3f, 6f }, new[] { 15f, 6f })));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            var rb = w.Find(Rb);
            if (Audibles.On(w))
            {
                Assert.IsTrue(Audibles.PickHot(w, Rb, "hot_go"));
                Assert.AreSame(Audibles.HotJob(w, rb), Bots.JobOfCalled(w, rb), "a hot route at the line beats the drawing");
                Assert.IsTrue(Audibles.PickHot(w, Rb, ""));
                Assert.AreSame(CustomPlays.JobOf(w, rb), Bots.JobOfCalled(w, rb), "and back to the drawn route");
                Assert.IsTrue(Audibles.Call(w, Qb, "verts"));
                Assert.IsFalse(CustomPlays.IsCustom(w, w.Offense), "the drawing belonged to the old play");
                Assert.IsNull(CustomPlays.JobOf(w, rb));
                Assert.AreSame(Huddle.Find(w, "verts").jobs[1], Bots.JobOfCalled(w, rb));
            }
        }

        [Test]
        public void TheNextDownStartsClean()
        {
            var w = InTheHuddle();
            Assert.IsTrue(CustomPlays.Lock(w, Qb, "slants", Route(1, new[] { 15f, 6f })));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            Huddle.Begin(w);
            Assert.IsFalse(CustomPlays.IsCustom(w, w.Offense));
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            Assert.IsNull(CustomPlays.JobOf(w, w.Find(Rb)), "a plain Slants the next down: the RB blocks again");
            Assert.AreEqual("block", Bots.JobOfCalled(w, w.Find(Rb)).kind);
        }

        // A bot playing for an idle person runs the drawn route: the RB heads up the field instead of staying in to block.
        [Test]
        public void TheBotRunsTheDrawnRoute()
        {
            int Run(bool drawn)
            {
                var w = InTheHuddle();
                var rb = w.Find(Rb);
                var home = w.Spots(w.Offense)[rb.Spot];
                if (drawn) Assert.IsTrue(CustomPlays.Lock(w, Qb, "slants", Route(1, new[] { 20f, home.z })));
                else Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
                Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
                rb.IdleTicks = (int)(w.Cfg.bots.idleTakeoverS * w.Cfg.sim.tickHz);   // he put his phone down a while ago
                w.Step(Hike);
                w.Step(null);
                Assert.AreEqual(Phase.Live, w.Phase);
                Assert.IsTrue(rb.BotHelp, "the RB isn't touching his phone, so a bot plays him");
                var go = CustomPlays.JobOf(w, rb);
                Assert.AreEqual(drawn, go != null);
                int checkedTicks = 0;
                int ticks = (int)(w.Cfg.bots.readTimesS[0] * w.Cfg.sim.tickHz);
                for (int i = 0; i < ticks && w.Phase == Phase.Live && !w.Thrown && w.Ball.Carrier == LivePlay.Qb(w).Slot; i++)
                {
                    var input = Bots.Think(w, rb);
                    if (drawn)
                    {
                        var pt = go.pts[go.pts.Count - 1];
                        float tx = w.LineOfScrimmageX + w.Dir * pt[0] - rb.X, tz = pt[1] - rb.Z;
                        float tl = (float)System.Math.Sqrt(tx * tx + tz * tz), il = (float)System.Math.Sqrt(input.MoveX * input.MoveX + input.MoveZ * input.MoveZ);
                        Assert.IsFalse(input.Block, "on his drawn route he runs, he doesn't block");
                        Assert.Greater(il, 0f, "he is moving");
                        Assert.Greater((input.MoveX * tx + input.MoveZ * tz) / (il * tl), 0.99f, "straight at the end of the drawn route");
                    }
                    else Assert.IsTrue(input.Block, "in Slants the RB stays in to block");
                    checkedTicks++;
                    w.Step(null);
                }
                return checkedTicks;
            }
            Assert.Greater(Run(false), 0);
            Assert.Greater(Run(true), 0);
        }

        [Test]
        public void SameSeedSameGameWithADrawnPlay()
        {
            ulong Play()
            {
                var w = InTheHuddle();
                var routes = Route(2, new[] { 6f, -20f }, new[] { 12f, -8f });
                routes.AddRange(Route(1, new[] { 2f, 8f }, new[] { 10f, 14f }));
                Assert.IsTrue(CustomPlays.Lock(w, Qb, "verts", routes));
                Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
                w.Step(Hike);
                for (int i = 0; i < w.Cfg.sim.tickHz * 3; i++) w.Step(null);
                return w.Hash();
            }
            Assert.AreEqual(Play(), Play());
        }
    }
}
