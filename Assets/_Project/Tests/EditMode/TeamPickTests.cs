using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.teamPick (Sim/TeamPick.cs): four teams in the league, any two play. The game only ever sees the two
    // picked (cfg.teams: away, home); NO_TEAM_PICK=1 keeps game.json's own two.
    public sealed class TeamPickTests
    {
        [Test]
        public void TheSwitchAndItsEnvOverride()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("teamPick"), "FEATURES.teamPick is on in game.json");
            Assert.AreEqual("NO_TEAM_PICK", ConfigLoader.EnvName("teamPick"));
            var off = ConfigTests.Load(new Dictionary<string, string> { { "NO_TEAM_PICK", "1" } });
            var two = new List<TeamConfig>(off.teams);
            Assert.IsFalse(TeamPick.Apply(off, "CAC", "WHL"));
            CollectionAssert.AreEqual(two, off.teams, "the switch off: game.json's two still play");
        }

        [Test]
        public void TheLeagueIsFourTeamsWithTheirOwnCodes()
        {
            var cfg = ConfigTests.Load();
            var league = TeamPick.League(cfg);
            Assert.AreEqual(4, league.Count);
            Assert.AreEqual(2, cfg.teams.Count, "only two ever play");
            Assert.AreSame(cfg.teams[0], league[0]);
            Assert.AreSame(cfg.teams[1], league[1]);
            var codes = new HashSet<string>();
            foreach (var t in league)
            {
                Assert.IsTrue(codes.Add(t.@short), "short codes are unique: " + t.@short);
                StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", t.color);
                StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", t.color2);
                StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", t.numberEdge);
            }
            Assert.AreEqual(2, TeamPick.Find(cfg, "CAC"));
            Assert.AreEqual(3, TeamPick.Find(cfg, "whl"), "codes match whatever the case");
            Assert.AreEqual(-1, TeamPick.Find(cfg, "XYZ"));
        }

        [Test]
        public void PickingReplacesTheTwoThatPlay()
        {
            var cfg = ConfigTests.Load();
            var league = TeamPick.League(cfg);
            Assert.IsTrue(TeamPick.Apply(cfg, "WHL", "CAC"));
            Assert.AreEqual(2, cfg.teams.Count);
            Assert.AreEqual("WHALES", cfg.teams[0].name, "away");
            Assert.AreEqual("CACTI", cfg.teams[1].name, "home");
            Assert.AreEqual(4, TeamPick.League(cfg).Count, "the league stays whole after a pick");
            Assert.IsTrue(TeamPick.Apply(cfg, league[1].@short, league[2].@short), "a new team can play an old one");
            Assert.AreSame(league[1], cfg.teams[0]);

            var w = new World(cfg, 1);
            Assert.AreEqual(2, w.Score.Length, "the game keeps two scores");
        }

        [Test]
        public void ABadPickKeepsTheDefaults()
        {
            var cfg = ConfigTests.Load();
            var two = new List<TeamConfig>(cfg.teams);
            Assert.IsFalse(TeamPick.Apply(cfg, "CAC", "CAC"), "a team can't play itself");
            CollectionAssert.AreEqual(two, cfg.teams);
            Assert.IsFalse(TeamPick.Apply(cfg, "", ""), "nothing saved yet");
            CollectionAssert.AreEqual(two, cfg.teams);
            Assert.IsTrue(TeamPick.Apply(cfg, "nope", "WHL"), "an unknown code falls back to that side's default");
            Assert.AreSame(two[0], cfg.teams[0]);
            Assert.AreEqual("WHL", cfg.teams[1].@short);
        }

        [Test]
        public void StepSkipsTheOtherSidesTeam()
        {
            var cfg = ConfigTests.Load();
            Assert.AreEqual(2, TeamPick.Step(cfg, 0, 1, +1), "away from 0 skips home's 1");
            Assert.AreEqual(3, TeamPick.Step(cfg, 0, 1, -1), "and wraps round backwards");
            Assert.AreEqual(0, TeamPick.Step(cfg, 3, 1, +1), "and forwards");
            Assert.IsTrue(TeamPick.ParsePair("CAC, WHL", out var a, out var h));
            Assert.AreEqual("CAC", a);
            Assert.AreEqual("WHL", h);
            Assert.IsFalse(TeamPick.ParsePair("CAC", out _, out _));
        }

        [Test]
        public void EveryNewTeamHasItsKitAndLogo()
        {
            var cfg = ConfigTests.Load();
            foreach (var t in cfg.moreTeams)
            {
                var maps = new List<string> { t.modelTexture, t.cheerTexture };
                foreach (var pm in t.posModels) maps.Add(pm.modelTexture);
                foreach (var m in maps)
                    Assert.IsNotNull(Resources.Load<Texture2D>(m), t.name + ": Resources/" + m);
                Assert.IsNotNull(Resources.Load<GameObject>(t.model), t.name + ": Resources/" + t.model);
                Assert.IsNotNull(Resources.Load<Texture2D>("Logos/" + t.@short), t.name + ": Resources/Logos/" + t.@short);
            }
        }
    }
}
