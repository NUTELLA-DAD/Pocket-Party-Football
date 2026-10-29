using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.rosterPage (Sim/RosterCards.cs): the title's ROSTER page, a card for every player model a team wears.
    public sealed class RosterTests
    {
        [Test]
        public void TheSwitchItsEnvOverrideAndTheButton()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("rosterPage"));
            Assert.AreEqual("NO_ROSTER_PAGE", ConfigLoader.EnvName("rosterPage"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_ROSTER_PAGE", "1" } }).Feature("rosterPage"));
            var tex = Resources.Load<Texture2D>("Title/ButtonRoster");
            Assert.IsNotNull(tex, "Resources/Title/ButtonRoster.png");
            Assert.Greater(tex.width, tex.height);
        }

        // Every position on the field is on exactly one card per model it wears, every card's model loads, and every
        // position but the kicker shows its ratings.
        [Test]
        public void EveryTeamsCardsCoverEveryPositionAndTheirModelsLoad()
        {
            var cfg = ConfigTests.Load();
            foreach (var team in TeamPick.League(cfg))
            {
                var cards = RosterCards.Of(cfg, team);
                Assert.GreaterOrEqual(cards.Count, 2, team.@short);
                var seen = new HashSet<string>();
                var models = new HashSet<string>();
                foreach (var c in cards)
                {
                    Assert.IsTrue(models.Add(c.Model), team.@short + ": one card per model, " + c.Model);
                    Assert.IsNotNull(Resources.Load<GameObject>(c.Model), c.Model);
                    Assert.IsNotEmpty(c.Spots, c.Model);
                    Assert.IsNotNull(cfg.roster.For(c.Model), c.Model + " has a name in roster.models");
                    foreach (var sp in c.Spots)
                    {
                        seen.Add(sp.Pos);
                        Assert.AreNotEqual(sp.Pos, sp.PosName, sp.Pos + " has a full name");
                        if (sp.Pos != "K") Assert.IsNotNull(sp.Rating, sp.Pos + " has ratings");
                        else Assert.IsNotEmpty(sp.Note, "the kicker has a line instead");
                    }
                }
                foreach (var pos in cfg.roster.positions) Assert.IsTrue(seen.Contains(pos), team.@short + " shows " + pos);
            }
        }

        // The card matches what a player on the field wears: the outside linebackers (#50, #58) on the lean model,
        // the middle one with the linemen.
        [Test]
        public void TheLinebackersSplitByNumber()
        {
            var cfg = ConfigTests.Load();
            var cards = RosterCards.Of(cfg, cfg.teams[0]);
            RosterSpot lean = null, middle = null;
            foreach (var c in cards)
                foreach (var sp in c.Spots)
                    if (sp.Pos == "LB")
                    {
                        if (sp.Numbers.Count > 0) lean = sp; else middle = sp;
                        cfg.teams[0].ModelFor("LB", true, out string m, out _, out _, sp.Numbers.Count > 0 ? sp.Numbers[0] : 54);
                        Assert.AreEqual(m, c.Model);
                    }
            Assert.IsNotNull(lean);
            Assert.IsNotNull(middle);
            CollectionAssert.AreEquivalent(new[] { 50, 58 }, lean.Numbers);
        }

        [Test]
        public void WithoutPositionModelsThereIsOneCard()
        {
            var cfg = ConfigTests.Load(new Dictionary<string, string> { { "NO_POS_MODELS", "1" } });
            var cards = RosterCards.Of(cfg, cfg.teams[0]);
            Assert.AreEqual(1, cards.Count);
            Assert.AreEqual(cfg.roster.positions.Count, cards[0].Spots.Count);
        }
    }
}
