using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.catchPlay (Sim/CatchPlay.cs, View/Moves.cs): diving catches, one-handed catches, and jump balls where a man
    // of each team goes up for it and they come down tangled.
    public sealed class CatchPlayTests
    {
        // A pass coming down low at (0, 0) and two men: a receiver and a defender, both far off for now.
        static (World w, Player r, Player d) Low(GameConfig cfg)
        {
            var w = new World(cfg, 5);
            var r = w.AddPlayer(0, w.Offense, false);
            var d = w.AddPlayer(1, w.Defense, false);
            r.X = -30f; d.X = 30f;
            w.Phase = Phase.Live;
            var b = w.Ball;
            b.State = BallState.Flying; b.Carrier = -1;
            b.X = 0f; b.Z = 0f; b.Y = cfg.catchPlay.diveMaxHeightYd * 0.5f; b.VX = 6f; b.VY = -3f; b.VZ = 0f;
            b.TargetX = 0f; b.TargetZ = 0f;
            return (w, r, d);
        }

        [Test]
        public void TheSwitchIsInGameJsonAndOffByEnv()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("catchPlay"), "catchPlay ships on");
            Assert.AreEqual("NO_CATCH_PLAY", ConfigLoader.EnvName("catchPlay"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_CATCH_PLAY", "1" } }).Feature("catchPlay"));
            var k = ConfigTests.Load().catchPlay;
            Assert.Greater(k.diveReachYd, 0f);
            Assert.That(k.diveChanceMul, Is.InRange(0.1f, 1f));
            Assert.That(k.contestChanceMul, Is.InRange(0.1f, 1f));
            Assert.Greater(k.diveDownS, 0f);
            Assert.Greater(k.tangleS, 0f);
            var a = ConfigTests.Load().anim;
            Assert.Greater(a.leapS, 0f); Assert.Greater(a.leapYd, 0f); Assert.Greater(a.oneHandYd, 0f);
        }

        [Test]
        public void AManRunningAtALowBallJustOutOfReachLaysOutForIt()
        {
            var cfg = ConfigTests.Load();
            var (w, r, _) = Low(cfg);
            float r0 = cfg.play.catchRadiusYd, reach = cfg.catchPlay.diveReachYd;
            r.X = -(r0 + reach * 0.5f); r.Z = 0f; r.VX = cfg.catchPlay.diveMinSpeedYdS + 1f; r.VZ = 0f;
            Assert.AreSame(r, CatchPlay.Diver(w, false, null), "running at it, just out of the catch circle");
            r.VX = -r.VX;
            Assert.IsNull(CatchPlay.Diver(w, false, null), "running away from it");
            r.VX = 1f;
            Assert.IsNull(CatchPlay.Diver(w, false, null), "jogging: too slow to lay out");
            r.VX = cfg.catchPlay.diveMinSpeedYdS + 1f;
            r.X = -(r0 + reach + 0.5f);
            Assert.IsNull(CatchPlay.Diver(w, false, null), "too far even laid out");
            r.X = -(r0 + reach * 0.5f);
            w.Ball.Y = cfg.catchPlay.diveMaxHeightYd + 0.5f;
            Assert.IsNull(CatchPlay.Diver(w, false, null), "a high ball: no diving for that");
            w.Ball.Y = cfg.catchPlay.diveMaxHeightYd * 0.5f;
            r.StumbleTicks = 3;
            Assert.IsNull(CatchPlay.Diver(w, false, null), "already on the grass");
            r.StumbleTicks = 0;
            w.CatchTried.Add(r.Slot);
            Assert.IsNull(CatchPlay.Diver(w, false, null), "he had his go");
        }

        [Test]
        public void AManOfTheOtherTeamRightOnTheCatcherMakesItAJumpBall()
        {
            var cfg = ConfigTests.Load();
            var (w, r, d) = Low(cfg);
            r.X = 0f; r.Z = 0f;
            d.X = cfg.catchPlay.contestYd * 0.5f; d.Z = 0f;
            Assert.AreSame(d, CatchPlay.Rival(w, r));
            Assert.AreSame(r, CatchPlay.Rival(w, d), "the other way round (a defender going for the pick)");
            d.X = cfg.catchPlay.contestYd + 0.5f;
            Assert.IsNull(CatchPlay.Rival(w, r), "too far off to get a hand in");
            var mate = w.AddPlayer(2, w.Offense, false);
            mate.X = 0.3f; mate.Z = 0f;
            Assert.IsNull(CatchPlay.Rival(w, r), "a team-mate is no rival");
        }

        [Test]
        public void DivesAndJumpBallsAreHarderCatches()
        {
            var cfg = ConfigTests.Load();
            var w = new World(cfg, 5);
            var k = cfg.catchPlay;
            Assert.AreEqual(0.9f, CatchPlay.Chance(w, 0.9f, false, false), 1e-6f);
            Assert.AreEqual(0.9f * k.diveChanceMul, CatchPlay.Chance(w, 0.9f, true, false), 1e-6f);
            Assert.AreEqual(0.9f * k.contestChanceMul, CatchPlay.Chance(w, 0.9f, false, true), 1e-6f);
            Assert.Less(CatchPlay.Chance(w, 0.9f, true, true), CatchPlay.Chance(w, 0.9f, true, false));
        }

        [Test]
        public void TheDiverAndBothTangledMenGoDownAndTheTvIsTold()
        {
            var cfg = ConfigTests.Load();
            var (w, r, d) = Low(cfg);
            CatchPlay.After(w, r, null, true, true);
            Assert.AreEqual(Huddle.Ticks(w, cfg.catchPlay.diveDownS), r.StumbleTicks, "on his belly for diveDownS");
            Assert.AreEqual(CatchStyle.Dive, w.CatchNote.Style);
            Assert.AreEqual(r.Slot, w.CatchNote.Slot);
            Assert.AreEqual(-1, w.CatchNote.Rival);
            Assert.IsTrue(w.CatchNote.Caught);
            r.StumbleTicks = 0;
            CatchPlay.After(w, r, d, false, false);
            Assert.AreEqual(Huddle.Ticks(w, cfg.catchPlay.tangleS), r.StumbleTicks);
            Assert.AreEqual(Huddle.Ticks(w, cfg.catchPlay.tangleS), d.StumbleTicks, "both of them");
            Assert.AreEqual(CatchStyle.Contested, w.CatchNote.Style);
            Assert.AreEqual(d.Slot, w.CatchNote.Rival);
            Assert.IsFalse(w.CatchNote.Caught);
        }

        [Test]
        public void OnTheTvTheDiverLaysOutAndTheTangledPairComeDown()
        {
            var cfg = ConfigTests.Load();
            var (w, r, d) = Low(cfg);
            var m = new Moves();
            m.Observe(w);
            CatchPlay.After(w, r, null, true, true);   // (as in World.Step: the try, then Tick moves on)
            w.Tick++;
            m.Observe(w);
            Assert.AreEqual(Move.DiveCatch, m.Of(w, r).Kind, "laid out for it");
            w.Tick += r.StumbleTicks + 2; r.StumbleTicks = 0;
            m.Observe(w);
            CatchPlay.After(w, r, d, false, true);
            w.Tick++;
            m.Observe(w);
            var rm = m.Of(w, r); var dm = m.Of(w, d);
            Assert.AreEqual(Move.Tangle, rm.Kind); Assert.AreEqual(Move.Tangle, dm.Kind);
            Assert.IsTrue(rm.Won, "the catcher came down with it");
            Assert.IsFalse(dm.Won);
            Assert.IsTrue(dm.OnBack, "the other man goes over backwards");
            Assert.AreEqual(d.Slot, rm.Partner); Assert.AreEqual(r.Slot, dm.Partner);
        }

        [Test]
        public void WithTheSwitchOffTheTvShowsNoCatchMoves()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_CATCH_PLAY", "1" } });
            var (w, r, _) = Low(cfg);
            var m = new Moves();
            m.Observe(w);
            w.CatchNote.Tick = w.Tick; w.CatchNote.Slot = r.Slot; w.CatchNote.Style = CatchStyle.Dive;
            w.Tick++;
            m.Observe(w);
            Assert.AreNotEqual(Move.DiveCatch, m.Of(w, r).Kind);
            Assert.AreEqual(0f, m.LiftYd(w, r.Slot));
        }

        // Over seeded bot games: men lay out for passes and go up together for them, now and then, and the hard catches
        // are made less often than the rest.
        [Test]
        public void BotGamesHaveDivesAndJumpBalls()
        {
            var tried = new Dictionary<CatchStyle, int>();
            var made = new Dictionary<CatchStyle, int>();
            foreach (CatchStyle s in System.Enum.GetValues(typeof(CatchStyle))) { tried[s] = 0; made[s] = 0; }
            for (int seed = 1; seed <= 6; seed++)
            {
                var w = WorldTests.Full(BotCoachTests.On(), seed);
                int seen = w.CatchNote.Tick;
                for (int i = 0; i < 10 * 60 * w.Cfg.sim.tickHz; i++)
                {
                    w.Step(null);
                    if (w.CatchNote.Tick == seen) continue;   // (a try is stamped with its own tick, before World.Step moves Tick on)
                    seen = w.CatchNote.Tick;
                    tried[w.CatchNote.Style]++;
                    if (w.CatchNote.Caught) made[w.CatchNote.Style]++;
                }
            }
            foreach (var s in tried.Keys) TestContext.WriteLine(s + ": " + made[s] + " caught of " + tried[s]);
            Assert.Greater(tried[CatchStyle.Dive], 0, "somebody lays out for a pass");
            Assert.Greater(tried[CatchStyle.Contested], 0, "jump balls happen");
            Assert.Greater(tried[CatchStyle.Plain], tried[CatchStyle.Contested], "most catches are still nobody-near ones");
        }
    }
}
