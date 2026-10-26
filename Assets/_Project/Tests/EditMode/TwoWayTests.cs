using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-07 two-way players (Sim/Formation.cs, FEATURES.lobby): each person holds one offense spot and one defense
    // spot and takes the right one when the team lines up; bots fill the rest; a bot game does not change.
    public sealed class TwoWayTests
    {
        static GameConfig On()
        {
            var cfg = ConfigTests.Load();
            cfg.features["lobby"] = true;
            return cfg;
        }

        static void AssertEverySpotOnce(World w, int team)
        {
            foreach (bool offense in new[] { true, false })
            {
                var seen = new HashSet<int>();
                foreach (var p in w.Players)
                    if (p.Team == team) Assert.IsTrue(seen.Add(Formation.SpotOn(w, p, offense)), "two players on one " + (offense ? "offense" : "defense") + " spot");
                Assert.AreEqual(Formation.SpotCount(w, offense), seen.Count);
                Assert.IsFalse(seen.Contains(-1));
            }
        }

        [Test]
        public void ShipsOffAndHasAnEnvSwitch()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("lobby"), "FEATURES.lobby is on in game.json (the merge captain turned it on)");
            Assert.AreEqual("NO_LOBBY", ConfigLoader.EnvName("lobby"));
            var cfg = ConfigTests.Load();
            Assert.Greater(cfg.lobby.swapAskS, 0f);
        }

        [Test]
        public void ABotGameIsTheSameWithItOn()
        {
            var off = ConfigTests.Load();
            var on = On();
            foreach (int seed in off.baseline.seeds)
                CollectionAssert.AreEqual(SimBaselineTests.Run(off, seed), SimBaselineTests.Run(on, seed), "seed " + seed);
        }

        [Test]
        public void APersonTakesTheirOffenseSpotThenTheirDefenseSpot()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg);
            int team = w.Offense;
            var me = w.ReplaceBot(team, 900);
            me.OffenseSpot = 3;     // WR #17
            me.DefenseSpot = 1;     // CB #24
            w.LineUp();
            Assert.AreEqual(3, me.Spot);
            Assert.AreEqual(cfg.formation.offense[3].number, me.Number);
            AssertEverySpotOnce(w, team);

            w.Offense = w.Defense;  // the other team has the ball now
            w.LineUp();
            Assert.AreEqual(1, me.Spot);
            Assert.AreEqual("CB", me.Pos);
            Assert.AreEqual(cfg.formation.defense[1].number, me.Number);
            AssertEverySpotOnce(w, team);
        }

        [Test]
        public void TheLowerSlotKeepsASpotTwoPeoplePicked()
        {
            var w = WorldTests.Full(On());
            int team = w.Offense;
            var a = w.ReplaceBot(team, 900);
            var b = w.ReplaceBot(team, 901);
            a.OffenseSpot = 2; b.OffenseSpot = 2;
            w.LineUp();
            Assert.AreEqual(2, a.Spot);
            Assert.AreNotEqual(2, b.Spot);
            AssertEverySpotOnce(w, team);
        }

        [Test]
        public void ElevenPeopleFillATeamBothWays()
        {
            var cfg = On();
            var w = new World(cfg, 1);
            int n = cfg.players.perTeam;
            for (int i = 0; i < n; i++)
            {
                var p = w.AddPlayer(i, 0, true);
                p.OffenseSpot = i;
                p.DefenseSpot = n - 1 - i;     // the defense line-up in the other order
            }
            for (int i = 0; i < n; i++) w.AddPlayer(100 + i, 1, false);
            w.LineUp();
            AssertEverySpotOnce(w, 0);
            AssertEverySpotOnce(w, 1);
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(i, Formation.SpotOn(w, w.Find(i), true));
                Assert.AreEqual(n - 1 - i, Formation.SpotOn(w, w.Find(i), false));
            }
            Assert.AreSame(w.Find(0), Formation.Holder(w, 0, true, 0));
        }

        // The laptop's first run with the switch on: a person (a high slot) who took the QB bot's place was moved to the
        // last free spot, so nobody hiked. Without picks nobody moves.
        [Test]
        public void APersonWhoTakesABotsPlaceKeepsItsSpot()
        {
            var w = WorldTests.Full(On());
            w.Step(null);                           // the first huddle
            var me = w.ReplaceBot(w.Offense, 500);
            Assert.AreEqual(0, me.Spot, "the person is the QB");
            w.LineUp();
            Assert.AreEqual(0, me.Spot);
            Assert.AreSame(me, LivePlay.Qb(w));
            w.Offense = w.Defense;
            w.LineUp();
            Assert.AreEqual(0, me.Spot, "and the middle LB on defense, like with the switch off");
            AssertEverySpotOnce(w, me.Team);
        }

        [Test]
        public void OffItIsOneSpotForBothSides()
        {
            var off = ConfigTests.Load();
            off.features["lobby"] = false;
            var w = WorldTests.Full(off);
            var me = w.ReplaceBot(w.Offense, 900);
            int spot = me.Spot;
            me.OffenseSpot = 3; me.DefenseSpot = 1;
            w.LineUp();
            Assert.AreEqual(spot, me.Spot, "with FEATURES.lobby off the picks are ignored");
        }
    }
}
