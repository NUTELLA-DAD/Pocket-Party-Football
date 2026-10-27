using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.hitAngle: a straight-on hit is a big hit (driven back, harder to hold on to the ball), a glancing blow
    // trips him up or leaves him staggering, and he may lose his balance and go down on his own.
    public sealed class HitAngleTests
    {
        // Two men on a live play: the carrier (offense, slot 0) at the middle of the field, a tackler (slot 1).
        static (World w, Player carrier, Player tackler) Pair(System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            tune?.Invoke(cfg);
            var w = new World(cfg, 7);
            var c = w.AddPlayer(0, w.Offense, false);
            var t = w.AddPlayer(1, w.Defense, false);
            w.Ball.GiveTo(c, cfg.ball);
            w.Phase = Phase.Live;
            c.X = 0f; c.Z = 0f;
            return (w, c, t);
        }

        [Test]
        public void TheSwitchIsInGameJsonAndOffByEnv()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("hitAngle"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_HIT_ANGLE", "1" } }).Feature("hitAngle"));
        }

        [Test]
        public void StraightOnAndFastIsABigHitThatDrivesHimBack()
        {
            var (w, c, t) = Pair();
            float dir = w.Dir;
            // he runs downfield at 7, the tackler meets him head on at 8
            c.VX = dir * 7f; c.VZ = 0f;
            t.X = c.X + dir * 1.5f; t.Z = 0f; t.VX = -dir * 8f; t.VZ = 0f;
            Assert.AreEqual(HitKind.BigHit, HitAngle.Classify(w, t, c));
            Assert.IsTrue(HitAngle.Land(w, t, c, out float fumbleMul, out string how, out float spotX));
            Assert.AreEqual("BIG HIT", how);
            Assert.AreEqual(0f, spotX, 1e-5f, "the ball is spotted where he was hit (forward progress)");
            Assert.AreEqual(w.Cfg.hit.bigHitFumbleMul, fumbleMul, 1e-5f);
            float back = System.Math.Min(w.Cfg.hit.knockbackMaxYd, 15f * w.Cfg.hit.knockbackPerYdS);
            Assert.AreEqual(-dir * back, c.X, 1e-3f, "driven back the way the hit went");
            Assert.AreEqual(HitKind.BigHit, w.Hit.Last);
            Assert.AreEqual(0f, w.Hit.FromX, 1e-4f);
            Assert.AreEqual(c.X, w.Hit.ToX, 1e-4f);
        }

        [Test]
        public void AChaseFromBehindIsAPlainTackle()
        {
            var (w, c, t) = Pair();
            float dir = w.Dir;
            c.VX = dir * 7f;
            t.X = c.X - dir * 1.5f; t.VX = dir * 8.5f;   // square, but only closing at 1.5
            Assert.AreEqual(HitKind.Square, HitAngle.Classify(w, t, c));
            Assert.IsTrue(HitAngle.Land(w, t, c, out float fumbleMul, out string how, out _));
            Assert.AreEqual("", how);
            Assert.AreEqual(1f, fumbleMul);
            Assert.AreEqual(0f, c.X, 1e-5f, "not moved");
        }

        // He runs downfield; the tackler comes across, alongside him, and only brushes him.
        static void Glancing(World w, Player c, Player t)
        {
            float dir = w.Dir;
            c.VX = dir * 7f; c.VZ = 0f;
            t.X = c.X - dir * 0.3f; t.Z = c.Z - 1.4f; t.VX = dir * 2f; t.VZ = 1f;
        }

        [Test]
        public void AGlancingBlowCanTripHimUp()
        {
            var (w, c, t) = Pair(cfg => cfg.hit.tripChance = 1f);
            Glancing(w, c, t);
            Assert.AreEqual(HitKind.Glance, HitAngle.Classify(w, t, c));
            Assert.IsTrue(HitAngle.Land(w, t, c, out float fumbleMul, out string how, out float spotX));
            Assert.AreEqual("TRIPPED", how);
            Assert.AreEqual(0f, spotX, 1e-5f, "spotted where he was tripped");
            Assert.AreEqual(0f, fumbleMul, "a trip doesn't knock it loose");
            Assert.AreEqual(w.Dir * w.Cfg.hit.tripFallYd, c.X, 1e-3f, "falls forward, the way he ran");
        }

        [Test]
        public void AGlancingBlowCanLeaveHimUpButOffBalance()
        {
            var (w, c, t) = Pair(cfg => cfg.hit.tripChance = 0f);
            Glancing(w, c, t);
            float speedBefore = System.Math.Abs(c.VX);
            Assert.IsFalse(HitAngle.Land(w, t, c, out _, out _, out _), "no tackle: he's still up");
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.Greater(c.OffBalanceTicks, 0);
            Assert.AreEqual(t.Slot, c.OffBalanceBy);
            Assert.Less(System.Math.Abs(c.VX), speedBefore, "he lost his stride");
            Assert.Greater(c.VZ, 0f, "shoved the way the hit went");
            Assert.Greater(t.StumbleTicks, 0, "the tackler bounced off");
            bool stumble = false;
            foreach (var e in w.Events.ThisTick) if (e.Kind == EventKind.Stumble && e.Slot == c.Slot && e.Other == t.Slot) stumble = true;
            Assert.IsTrue(stumble, "a Stumble event for the TV");
            Assert.Greater(HitAngle.TackleOdds(w, c, 0.5f), 0.5f, "easier to bring down while staggering");
        }

        [Test]
        public void OffBalanceHeMayGoDownOnHisOwn()
        {
            var (w, c, t) = Pair(cfg => { cfg.hit.tripChance = 0f; cfg.hit.fallLaterChance = 1f; });
            Glancing(w, c, t);
            HitAngle.Land(w, t, c, out _, out _, out _);
            c.OffBalanceTicks = 0;   // the stagger ran out
            w.Events.BeginTick();
            Assert.IsTrue(HitAngle.Step(w, c));
            Assert.AreEqual(Phase.Whistle, w.Phase);
            Assert.AreEqual("TACKLED", w.Result);
            Assert.AreEqual(HitKind.LostBalance, w.Hit.Last);
            bool credited = false;
            foreach (var e in w.Events.ThisTick) if (e.Kind == EventKind.Tackle && e.Slot == t.Slot && e.Text == "LOST BALANCE") credited = true;
            Assert.IsTrue(credited, "the man who knocked him off balance gets the tackle");
        }

        [Test]
        public void OffBalanceHeMayStayUp()
        {
            var (w, c, t) = Pair(cfg => { cfg.hit.tripChance = 0f; cfg.hit.fallLaterChance = 0f; });
            Glancing(w, c, t);
            HitAngle.Land(w, t, c, out _, out _, out _);
            c.OffBalanceTicks = 0;
            Assert.IsFalse(HitAngle.Step(w, c));
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.AreEqual(-1, c.OffBalanceBy, "rolled once");
        }

        [Test]
        public void SwitchedOffEveryWonTackleIsATackleWhereHeStands()
        {
            var (w, c, t) = Pair(cfg => cfg.features["hitAngle"] = false);
            c.VX = w.Dir * 7f; t.X = c.X + w.Dir * 1.5f; t.VX = -w.Dir * 8f;
            Assert.IsTrue(HitAngle.Land(w, t, c, out float fumbleMul, out string how, out _));
            Assert.AreEqual(1f, fumbleMul); Assert.AreEqual("", how);
            Assert.AreEqual(0f, c.X, 1e-5f);
        }

        // Bot games show all of it, and none of it with the switch off.
        [Test]
        public void BotGamesHaveBigHitsTripsAndStumbles()
        {
            int Count(bool on, out int big, out int trips, out int stumbles)
            {
                var cfg = BotCoachTests.On();
                cfg.features["hitAngle"] = on;
                big = trips = stumbles = 0;
                int tackles = 0;
                for (int seed = 1; seed <= 3; seed++)
                {
                    var w = WorldTests.Full(cfg, seed);
                    for (int i = 0; i < 10 * 60 * cfg.sim.tickHz; i++)
                    {
                        w.Step(null);
                        foreach (var e in w.Events.ThisTick)
                        {
                            if (e.Kind == EventKind.Tackle || e.Kind == EventKind.Sack) { tackles++; if (e.Text == "BIG HIT") big++; if (e.Text == "TRIPPED") trips++; }
                            if (e.Kind == EventKind.Stumble) stumbles++;
                        }
                    }
                }
                return tackles;
            }
            int all = Count(true, out int b, out int tr, out int st);
            TestContext.WriteLine(all + " tackles: " + b + " big hits, " + tr + " trips, " + st + " left off balance");
            Assert.Greater(b, 0); Assert.Greater(tr, 0); Assert.Greater(st, 0);
            Assert.Less(b + tr, all / 2, "most tackles are still plain tackles");
            Count(false, out b, out tr, out st);
            Assert.AreEqual(0, b + tr + st);
        }
    }
}
