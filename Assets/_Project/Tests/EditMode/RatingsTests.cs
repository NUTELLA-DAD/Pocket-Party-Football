using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // Player ratings by position (FEATURES.ratings, Ratings.cs): speed, size and hands.
    public sealed class RatingsTests
    {
        static World Game(bool on)
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            cfg.features["ratings"] = on;
            var w = new World(cfg, 1);
            for (int i = 0; i < cfg.players.perTeam * 2; i++) w.AddPlayer(i, i % 2, false);
            return w;
        }

        static Player Pos(World w, int team, string pos)
        {
            foreach (var p in w.Players) if (p.Team == team && p.Pos == pos) return p;
            Assert.Fail("no " + pos + " on team " + team);
            return null;
        }

        static float Speed(Player p) => (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);

        // A lone player in that position's ratings, sprinting flat out for a second.
        static float TopSpeed(bool on, string pos)
        {
            var cfg = ConfigTests.Load(ConfigTests.NoHuddle);
            cfg.features["ratings"] = on;
            var w = new World(cfg, 1);
            var p = w.AddPlayer(0, 0, true);
            p.Pos = pos;
            p.X = 0; p.Z = 0;
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(WorldTests.Stick(0, 1f, 0f, true));
            Assert.AreEqual(pos, p.Pos);
            return Speed(p);
        }

        [Test]
        public void ShipsOnWithEveryPositionRated()
        {
            var cfg = ConfigTests.Load(ConfigTests.AsShipped);
            Assert.IsTrue(cfg.Feature("ratings"));
            foreach (var s in cfg.formation.offense) Assert.IsTrue(cfg.ratings.positions.ContainsKey(s.pos), s.pos);
            foreach (var s in cfg.formation.defense) Assert.IsTrue(cfg.ratings.positions.ContainsKey(s.pos), s.pos);
            Assert.AreEqual("NO_RATINGS", ConfigLoader.EnvName("ratings"));
        }

        [Test]
        public void RunningBacksOutrunReceiversWhoOutrunLinemen()
        {
            float rb = TopSpeed(true, "RB"), wr = TopSpeed(true, "WR"), te = TopSpeed(true, "TE"), ol = TopSpeed(true, "OL");
            Assert.Greater(rb, wr);
            Assert.Greater(wr, te);
            Assert.Greater(te, ol);
        }

        [Test]
        public void SwitchedOffEveryoneRunsTheSame()
        {
            Assert.AreEqual(TopSpeed(false, "RB"), TopSpeed(false, "OL"), 1e-4f);
            Assert.AreEqual(TopSpeed(false, "QB"), TopSpeed(true, "QB"), 1e-4f, "a neutral 5 plays like the switch off");
        }

        [Test]
        public void ReceiversHaveTheBestHands()
        {
            var w = Game(true);
            float plain = w.Cfg.play.catchChanceOffense;
            float wr = Ratings.Catch(w, Pos(w, w.Offense, "WR"), plain);
            float te = Ratings.Catch(w, Pos(w, w.Offense, "TE"), plain);
            float rb = Ratings.Catch(w, Pos(w, w.Offense, "RB"), plain);
            float ol = Ratings.Catch(w, Pos(w, w.Offense, "OL"), plain);
            Assert.Greater(wr, te);
            Assert.Greater(te, rb);
            Assert.Greater(rb, ol);
            Assert.LessOrEqual(wr, w.Cfg.receiving.catchChanceMax);
            Assert.AreEqual(plain, Ratings.Catch(Game(false), Pos(w, w.Offense, "WR"), plain), "switched off: the plain chance");
        }

        [Test]
        public void BigMenBlockLongerAndTackleHarder()
        {
            var w = Game(true);
            Player ol = Pos(w, w.Offense, "OL"), te = Pos(w, w.Offense, "TE"), qb = Pos(w, w.Offense, "QB"), rb = Pos(w, w.Offense, "RB");
            Player dl = Pos(w, w.Defense, "DL"), lb = Pos(w, w.Defense, "LB"), cb = Pos(w, w.Defense, "CB");
            Assert.Greater(Ratings.ShedFrac(w, ol, dl), 1f, "a lineman holds a DL longer than today");
            Assert.Less(Ratings.ShedFrac(w, te, lb), Ratings.ShedFrac(w, ol, lb), "a TE holds a LB less long than a lineman does");
            Assert.Greater(Ratings.TackleBonus(w, dl, qb), 0f, "a DL brings a QB down more often");
            Assert.Less(Ratings.TackleBonus(w, cb, rb), 0f, "a small CB misses a RB more often");
            var off = Game(false);
            Assert.AreEqual(1f, Ratings.ShedFrac(off, ol, dl));
            Assert.AreEqual(0f, Ratings.TackleBonus(off, dl, qb));
        }
    }
}
