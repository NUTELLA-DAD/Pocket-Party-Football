using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.cup (Sim/Cup.cs): the Pocket Cup, a knockout bracket. Six teams: seeds 1 and 2 get byes, five games.
    public sealed class CupTests
    {
        static readonly List<string> Six = new List<string> { "S1", "S2", "S3", "S4", "S5", "S6" };

        [Test]
        public void TheSwitchItsEnvOverrideAndTheButton()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("cup"), "FEATURES.cup is on in game.json");
            Assert.AreEqual("NO_CUP", ConfigLoader.EnvName("cup"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_CUP", "1" } }).Feature("cup"));
            Assert.GreaterOrEqual(cfg.cup.maxTeams, TeamPick.League(cfg).Count, "the whole league fits in a cup");
            Assert.Greater(cfg.title.menuBottomFrac, 0.5f);
            var tex = Resources.Load<Texture2D>("Title/ButtonCup");
            Assert.IsNotNull(tex, "Resources/Title/ButtonCup.png");
            Assert.Greater(tex.width, tex.height);
        }

        [Test]
        public void SixTeamsTheTopTwoSitOutTheFirstRound()
        {
            var cup = new Cup(Six);
            Assert.AreEqual(8, cup.Size);
            Assert.AreEqual(3, cup.Rounds);
            Assert.AreEqual("QUARTERFINALS", cup.RoundName(0));
            Assert.AreEqual("SEMIFINALS", cup.RoundName(1));
            Assert.AreEqual("FINAL", cup.RoundName(2));
            // first round, top to bottom: 1 v bye, 4 v 5, 2 v bye, 3 v 6
            Assert.AreEqual("S1", cup.Team(0, 0, 0)); Assert.IsTrue(cup.IsBye(0, 0, 1));
            Assert.AreEqual("S4", cup.Team(0, 1, 0)); Assert.AreEqual("S5", cup.Team(0, 1, 1));
            Assert.AreEqual("S2", cup.Team(0, 2, 0)); Assert.IsTrue(cup.IsBye(0, 2, 1));
            Assert.AreEqual("S3", cup.Team(0, 3, 0)); Assert.AreEqual("S6", cup.Team(0, 3, 1));
            Assert.AreEqual("S1", cup.Winner(0, 0), "a bye goes straight through");
            Assert.AreEqual("S1", cup.Team(1, 0, 0), "and waits in the semifinal");

            int games = 0;
            while (cup.Next() != null) { cup.Record(0, 7); games++; }   // the home team (the better seed) always wins
            Assert.AreEqual(5, games, "six teams: two quarterfinals, two semifinals, the final");
            Assert.IsTrue(cup.Over);
            Assert.AreEqual("S1", cup.Champion);
        }

        [Test]
        public void TheBetterSeedIsHomeAndATieSendsThemThrough()
        {
            var cup = new Cup(Six);
            var m = cup.Next();
            Assert.AreEqual(0, m.Round);
            Assert.AreEqual(1, m.Index, "4 v 5 first (the 1 v bye needs no game)");
            Assert.AreEqual("S5", m.Away);
            Assert.AreEqual("S4", m.Home);
            Assert.AreEqual("S5", cup.Record(21, 14), "the away team won");
            Assert.AreEqual("S5", cup.Team(1, 0, 1), "and meets seed 1 in the semifinal");
            m = cup.Next();
            Assert.AreEqual("S6", m.Away);
            Assert.AreEqual("S3", cup.Record(10, 10), "a tie: the higher seed goes through");
            m = cup.Next();
            Assert.AreEqual(1, m.Round);
            Assert.AreEqual("S5", m.Away);
            Assert.AreEqual("S1", m.Home);
        }

        [Test]
        public void FourTeamsAreTwoSemisAndAFinal()
        {
            var cup = new Cup(new List<string> { "A", "B", "C", "D" });
            Assert.AreEqual(2, cup.Rounds);
            Assert.AreEqual("SEMIFINALS", cup.RoundName(0));
            for (int m = 0; m < cup.Matches(0); m++) { Assert.IsFalse(cup.IsBye(0, m, 0)); Assert.IsFalse(cup.IsBye(0, m, 1)); }
            Assert.AreEqual("A", cup.Team(0, 0, 0)); Assert.AreEqual("D", cup.Team(0, 0, 1));
            Assert.AreEqual("B", cup.Team(0, 1, 0)); Assert.AreEqual("C", cup.Team(0, 1, 1));
            int games = 0;
            while (cup.Next() != null) { cup.Record(3, 0); games++; }   // the away team (the worse seed) always wins
            Assert.AreEqual(3, games);
            Assert.AreEqual("D", cup.Champion, "D won at A, C won at B, then D won at C (the better seed hosts the final)");
        }

        [Test]
        public void TheWholeLeagueIsEightTeamsWithNoByes()
        {
            var cfg = ConfigTests.Load();
            var teams = new List<string>();
            foreach (var t in TeamPick.League(cfg)) teams.Add(t.@short);
            Assert.AreEqual(8, teams.Count, "the league's eight teams");
            Assert.GreaterOrEqual(cfg.cup.maxTeams, teams.Count, "all play in the cup");
            CollectionAssert.Contains(teams, "CMT");
            CollectionAssert.Contains(teams, "COB");
            var cup = new Cup(teams);
            Assert.AreEqual(8, cup.Size);
            Assert.AreEqual(3, cup.Rounds);
            for (int m = 0; m < cup.Matches(0); m++) { Assert.IsFalse(cup.IsBye(0, m, 0)); Assert.IsFalse(cup.IsBye(0, m, 1)); }
            int games = 0;
            while (cup.Next() != null) { cup.Record(0, 7); games++; }
            Assert.AreEqual(7, games, "four quarterfinals, two semifinals, the final");
        }

        [Test]
        public void ItSavesAndLoadsMidway()
        {
            var cup = new Cup(Six);
            cup.Record(21, 14);
            cup.Record(7, 10);
            cup.Record(0, 3);
            string text = cup.Save();
            var back = Cup.Load(text);
            Assert.IsNotNull(back, text);
            Assert.AreEqual(text, back.Save());
            Assert.AreEqual(cup.Next().Away, back.Next().Away);
            Assert.AreEqual(cup.Next().Home, back.Next().Home);
            Assert.IsNull(Cup.Load(""));
            Assert.IsNull(Cup.Load("garbage"));
            Assert.IsNull(Cup.Load("A,A;"), "a team twice");
            Assert.IsNull(Cup.Load("A,B,C,D;X,/"), "a winner who wasn't in that game");
        }

        [Test]
        public void TheDrawIsSeededAndKeepsEveryTeam()
        {
            var a = Cup.Draw(Six, 42, true);
            var b = Cup.Draw(Six, 42, true);
            CollectionAssert.AreEqual(a.Seeds, b.Seeds, "same seed, same draw");
            CollectionAssert.AreEquivalent(Six, a.Seeds);
            CollectionAssert.AreEqual(Six, Cup.Draw(Six, 42, false).Seeds, "no draw: the order given");
        }
    }
}
