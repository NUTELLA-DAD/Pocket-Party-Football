using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-13: audibles (the caller changes the play at the line, once a down) and hot routes (a receiver swaps his route).
    public sealed class AudiblesTests
    {
        const int Qb = 500, Rb = 501, Mlb = 600, Cb = 601;

        // Both plays called by people, lined up at the line (Phase.Set). FEATURES.audibles on unless `on` is false;
        // then the env switches, as the game applies them.
        static World AtTheLine(bool on = true, Dictionary<string, string> env = null)
        {
            var cfg = ConfigTests.Load();
            cfg.features["audibles"] = on;
            if (env != null) ConfigLoader.ApplyEnvOverrides(cfg, k => env.TryGetValue(k, out var v) ? v : null);
            var w = WorldTests.Full(cfg);
            w.Step(null);                                  // the first huddle
            w.ReplaceBot(w.Offense, Qb);
            w.ReplaceBot(w.Offense, Rb);
            w.ReplaceBot(w.Defense, Mlb);
            w.ReplaceBot(w.Defense, Cb);
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            Assert.AreEqual(Phase.Set, w.Phase);
            return w;
        }

        static Dictionary<int, PlayerInput> Hike => new Dictionary<int, PlayerInput> { { Qb, new PlayerInput { Hike = true } } };

        [Test]
        public void ShipsOffAndNoAudiblesTurnsItOff()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("audibles"), "on in game.json (the merge captain turned it on)");
            var off = AtTheLine(on: false);
            Assert.IsFalse(Audibles.Call(off, Qb, "verts"));
            Assert.IsFalse(Audibles.PickHot(off, Rb, "hot_go"));
            Assert.AreEqual("slants", off.Calls[off.Offense]);
            var env = AtTheLine(env: new Dictionary<string, string> { { "NO_AUDIBLES", "1" } });
            Assert.IsFalse(Audibles.Call(env, Qb, "verts"), "NO_AUDIBLES=1");
        }

        [Test]
        public void TheConfigBlockHasTwoHotRoutes()
        {
            var cfg = ConfigTests.Load();
            Assert.AreEqual(1, cfg.audibles.perDown);
            Assert.AreEqual(2, cfg.audibles.hotRoutes.Count);
            foreach (var r in cfg.audibles.hotRoutes)
            {
                Assert.IsNotEmpty(r.id);
                Assert.IsNotEmpty(r.name);
                Assert.Greater(r.pts.Count, 0);
                Assert.IsNull(Huddle.Find(WorldTests.Full(cfg), r.id), "a hot route id is never a play id");
            }
        }

        [Test]
        public void OnlyTheCallerAudiblesOncePerDown()
        {
            var w = AtTheLine();
            Assert.IsFalse(Audibles.Call(w, Rb, "verts"), "a teammate can't audible");
            Assert.IsFalse(Audibles.Call(w, Qb, "cover2"), "not from the other side's playbook");
            Assert.IsFalse(Audibles.Call(w, Qb, "nope"));
            Assert.IsFalse(Audibles.Call(w, Qb, "slants"), "the play already called is no change");
            Assert.AreEqual(1, Audibles.Left(w, w.Offense), "a refused audible doesn't use it up");
            Assert.IsTrue(Audibles.Call(w, Qb, "verts"));
            Assert.AreEqual("verts", w.Calls[w.Offense]);
            Assert.AreEqual(0, Audibles.Left(w, w.Offense));
            Assert.IsFalse(Audibles.Call(w, Qb, "dive"), "once a down");
            Assert.AreEqual("verts", w.Calls[w.Offense]);
            Assert.AreEqual(Phase.Set, w.Phase, "still at the line");
            Assert.AreEqual("route", LivePlay.Called(w, w.Offense).jobs[2].kind, "the bots now run the new play");

            Assert.IsTrue(Audibles.Call(w, Mlb, "mblitz"), "the defense has its own audible");
            Assert.AreEqual("mblitz", w.Calls[w.Defense]);
            Assert.IsFalse(Audibles.Call(w, Cb, "cover2"), "only the defense's caller");
        }

        [Test]
        public void NoAudibleInTheHuddleOrAfterTheSnap()
        {
            var w = AtTheLine();
            w.Step(Hike);
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.IsFalse(Audibles.Call(w, Qb, "verts"), "the ball is live");
            Assert.IsFalse(Audibles.PickHot(w, Rb, "hot_go"));
            Huddle.Begin(w);
            Assert.IsFalse(Audibles.Call(w, Qb, "verts"), "in the huddle you just call a play");
        }

        [Test]
        public void TheNextDownGetsAFreshAudible()
        {
            var w = AtTheLine();
            Assert.IsTrue(Audibles.Call(w, Qb, "verts"));
            Assert.IsTrue(Audibles.PickHot(w, Rb, "hot_go"));
            Huddle.Begin(w);
            Assert.AreEqual("", Audibles.RouteOf(w, w.Find(Rb)), "a hot route lasts one down");
            Assert.IsTrue(Huddle.Lock(w, Qb, "slants"));
            Assert.IsTrue(Huddle.Lock(w, Mlb, "cover3"));
            Assert.AreEqual(1, Audibles.Left(w, w.Offense));
            Assert.IsTrue(Audibles.Call(w, Qb, "dive"));
        }

        [Test]
        public void AReceiverSwapsToAHotRoute()
        {
            var w = AtTheLine();
            var rb = w.Find(Rb);
            Assert.AreEqual("block", Huddle.JobOf(w, rb).kind, "in Slants the RB stays in to block");
            Assert.IsFalse(Audibles.PickHot(w, Qb, "hot_go"), "the QB is not a receiver");
            Assert.IsFalse(Audibles.PickHot(w, Cb, "hot_go"), "the defense has no hot routes");
            Assert.IsFalse(Audibles.PickHot(w, Rb, "nope"));
            Assert.IsFalse(Audibles.PickHot(w, Rb, ""), "nothing to clear yet");
            Assert.IsTrue(Audibles.PickHot(w, Rb, "hot_go"));
            Assert.AreEqual("hot_go", Audibles.RouteOf(w, rb));
            var job = Audibles.HotJob(w, rb);
            Assert.AreEqual("route", job.kind);
            // the route starts from his own spot: the RB stands 7.5 back in the middle, the go is straight ahead
            var home = w.Cfg.formation.offense[rb.Spot];
            var go = w.Cfg.audibles.hotRoutes[1];
            Assert.AreEqual(-home.back + go.pts[0][0], job.pts[0][0], 1e-4f);
            Assert.AreEqual(home.z, job.pts[0][1], 1e-4f);
            Assert.IsTrue(Audibles.PickHot(w, Rb, "hot_slant"), "he can change his mind before the snap");
            Assert.IsTrue(Audibles.PickHot(w, Rb, ""), "and go back to the called route");
            Assert.IsNull(Audibles.HotJob(w, rb));
        }

        [Test]
        public void ASlantBreaksTowardTheMiddle()
        {
            var w = AtTheLine();
            var slant = w.Cfg.audibles.hotRoutes[0];
            foreach (int spot in new[] { 2, 3 })   // the wide receivers on each side
            {
                var wr = LivePlay.SpotOf(w, w.Offense, spot);
                var job = Audibles.JobFor(w, wr, slant);
                float z = w.Cfg.formation.offense[spot].z, end = job.pts[job.pts.Count - 1][1];
                Assert.Less(System.Math.Abs(end), System.Math.Abs(z), "spot " + spot + " slants inside");
            }
        }

        [Test]
        public void AnAudibleClearsThatTeamsHotRoutes()
        {
            var w = AtTheLine();
            Assert.IsTrue(Audibles.PickHot(w, Rb, "hot_go"));
            int v = Audibles.VersionOf(w, w.Defense);
            Assert.IsTrue(Audibles.Call(w, Qb, "verts"));
            Assert.AreEqual("", Audibles.RouteOf(w, w.Find(Rb)), "the route belonged to the old play");
            Assert.AreEqual(v, Audibles.VersionOf(w, w.Defense), "the other team's version never moves on our audible");
        }

        // A bot playing for an idle person runs his hot route: the RB heads up the field on the hot go instead of staying
        // in to block. Checked on the bot's own decision every tick before the throw, so it holds whatever the other
        // switches do to the rest of the play (defender moves, bots v2, the bot coach can all end it early).
        [Test]
        public void TheBotRunsTheHotRoute()
        {
            int Run(bool hot)
            {
                var w = AtTheLine();
                if (hot) Assert.IsTrue(Audibles.PickHot(w, Rb, "hot_go"));
                var rb = w.Find(Rb);
                rb.IdleTicks = (int)(w.Cfg.bots.idleTakeoverS * w.Cfg.sim.tickHz);   // he put his phone down a while ago
                w.Step(Hike);
                w.Step(null);
                Assert.AreEqual(Phase.Live, w.Phase);
                Assert.IsTrue(rb.BotHelp, "the RB isn't touching his phone, so a bot plays him");
                var go = Audibles.HotJob(w, rb);
                Assert.AreEqual(hot, go != null);
                int checkedTicks = 0;
                int ticks = (int)(w.Cfg.bots.readTimesS[0] * w.Cfg.sim.tickHz);
                for (int i = 0; i < ticks && w.Phase == Phase.Live && !w.Thrown && w.Ball.Carrier == LivePlay.Qb(w).Slot; i++)
                {
                    var input = Bots.Think(w, rb);
                    if (hot)
                    {
                        var pt = go.pts[go.pts.Count - 1];
                        float tx = w.LineOfScrimmageX + w.Dir * pt[0] - rb.X, tz = pt[1] - rb.Z;
                        float tl = (float)System.Math.Sqrt(tx * tx + tz * tz), il = (float)System.Math.Sqrt(input.MoveX * input.MoveX + input.MoveZ * input.MoveZ);
                        Assert.IsFalse(input.Block, "on the hot go he runs, he doesn't block");
                        Assert.Greater(il, 0f, "he is moving");
                        Assert.Greater((input.MoveX * tx + input.MoveZ * tz) / (il * tl), 0.99f, "straight at the end of the hot go");
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
        public void SameSeedSameGameWithAudibles()
        {
            ulong Play()
            {
                var w = AtTheLine();
                Audibles.Call(w, Qb, "verts");
                Audibles.PickHot(w, Rb, "hot_slant");
                w.Step(Hike);
                for (int i = 0; i < w.Cfg.sim.tickHz * 3; i++) w.Step(null);
                return w.Hash();
            }
            Assert.AreEqual(Play(), Play());
        }
    }
}
