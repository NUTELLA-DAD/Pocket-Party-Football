using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    public sealed class ConfigTests
    {
        // For tests of other rules: the huddle off, so it never lines everyone up mid-test.
        public static Dictionary<string, string> NoHuddle => new Dictionary<string, string> { { "NO_HUDDLE", "1" } };

        // Kicking (WS-01) is off unless a test asks for it: with it on the game opens with a kickoff, and the tests of
        // other rules expect the first huddle. A test that wants the game exactly as game.json has it passes AsShipped.
        public static Dictionary<string, string> AsShipped => new Dictionary<string, string> { { "NO_KICKING", "0" } };

        public static GameConfig Load(Dictionary<string, string> env = null)
        {
            string json = File.ReadAllText("Assets/_Project/Config/game.json");
            var e = env != null ? new Dictionary<string, string>(env) : new Dictionary<string, string>();
            if (!e.ContainsKey("NO_KICKING")) e["NO_KICKING"] = "1";
            var cfg = ConfigLoader.Parse(json, k => e.TryGetValue(k, out var v) ? v : null);
            var plays = new List<string>();
            foreach (var f in Directory.GetFiles("Assets/_Project/Config/plays", "*.json")) plays.Add(File.ReadAllText(f));
            ConfigLoader.AddPlays(cfg, plays);
            return cfg;
        }

        [Test]
        public void GameJsonLoadsTheField()
        {
            var cfg = Load();
            Assert.AreEqual(1, cfg.schema);
            Assert.AreEqual(30, cfg.sim.tickHz);
            Assert.AreEqual(120f, cfg.field.lengthYd);
            Assert.AreEqual(53.3f, cfg.field.widthYd, 1e-4f);
            Assert.AreEqual(50f, cfg.field.GoalLineX);
        }

        [Test]
        public void EveryNumberHasAValue()
        {
            var cfg = Load();
            Assert.Greater(cfg.field.yardM, 0f);
            Assert.Greater(cfg.field.lineEveryYd, 0f);
            Assert.Greater(cfg.field.stripeYd, 0f);
            Assert.Greater(cfg.camera.visibleYards, 0f);
            Assert.Greater(cfg.camera.fovDeg, 0f);
            foreach (var c in new[] { cfg.field.grassColor, cfg.field.grassAltColor, cfg.field.lineColor, cfg.field.endZoneColor, cfg.field.surroundColor, cfg.field.skyColor })
                StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", c);
        }

        [Test]
        public void FeatureEnvNameIsUpperSnake()
        {
            Assert.AreEqual("NO_FIELD_STRIPES", ConfigLoader.EnvName("fieldStripes"));
            Assert.AreEqual("NO_SECRET_CALLS", ConfigLoader.EnvName("secretCalls"));
        }

        [Test]
        public void NoEnvSwitchTurnsAFeatureOff()
        {
            Assert.IsTrue(Load().Feature("fieldStripes"));
            var off = Load(new Dictionary<string, string> { { "NO_FIELD_STRIPES", "1" } });
            Assert.IsFalse(off.Feature("fieldStripes"));
            Assert.IsFalse(off.Feature("noSuchFeature"));
        }

        [Test]
        public void LinebackersWearTheirOwnModel()
        {
            var arr = Load().teams[0];
            arr.ModelFor("LB", true, out string m, out string tex);
            Assert.AreEqual("Models/Linebacker/Linebacker_Rigged", m);
            Assert.AreEqual("Models/Linebacker/LinebackerARR_BaseColor", tex, "the Arrows' linebackers wear their own colours");
            var grn = Load().teams[1];
            grn.ModelFor("LB", true, out m, out tex);
            Assert.AreEqual("Models/Linebacker/Linebacker_Rigged", m);
            Assert.AreEqual("Models/Linebacker/Linebacker_BaseColor", tex);
            grn.ModelFor("CB", true, out m, out _);
            Assert.AreEqual(grn.model, m, "a position with no model of its own wears the team's");
            grn.ModelFor("LB", false, out m, out _);
            Assert.AreEqual(grn.model, m, "NO_POS_MODELS=1: the team's model for every position");
            Assert.IsFalse(Load(new Dictionary<string, string> { { "NO_POS_MODELS", "1" } }).Feature("posModels"));
        }

        [Test]
        public void QuarterbacksWearTheirOwnModel()
        {
            Load().teams[0].ModelFor("QB", true, out string m, out string tex);
            Assert.AreEqual("Models/QB/QB_Rigged", m);
            Assert.AreEqual("Models/QB/QB_BaseColor", tex, "the Arrows' QB wears the model's own colours");
            Load().teams[1].ModelFor("QB", true, out m, out tex);
            Assert.AreEqual("Models/QB/QB_Rigged", m);
            Assert.AreEqual("Models/QB/QBGRN_BaseColor", tex, "the Grains' QB wears the green recolour");
        }

        [Test]
        public void WideReceiversWearTheirOwnModel()
        {
            Load().teams[0].ModelFor("WR", true, out string m, out string tex);
            Assert.AreEqual("Models/WR/WR_Rigged", m);
            Assert.AreEqual("Models/WR/WR_BaseColor", tex, "the Arrows' WR wears the model's own colours");
            Load().teams[1].ModelFor("WR", true, out m, out tex);
            Assert.AreEqual("Models/WR/WR_Rigged", m);
            Assert.AreEqual("Models/WR/WRGRN_BaseColor", tex, "the Grains' WR wears the green recolour");
            Load().teams[0].ModelFor("CB", true, out m, out _);
            Assert.AreEqual(Load().teams[0].model, m, "defensive backs keep the team's model");
        }

        [Test]
        public void TightEndsWearTheirOwnModel()
        {
            Load().teams[0].ModelFor("TE", true, out string m, out string tex);
            Assert.AreEqual("Models/TE/TE_Rigged", m);
            Assert.AreEqual("Models/TE/TE_BaseColor", tex, "the Arrows' TE wears the model's own colours");
            Load().teams[1].ModelFor("TE", true, out m, out tex);
            Assert.AreEqual("Models/TE/TE_Rigged", m);
            Assert.AreEqual("Models/TE/TEGRN_BaseColor", tex, "the Grains' TE wears the green recolour");
        }

        [Test]
        public void KickersWearTheirOwnModel()
        {
            Load().teams[0].ModelFor("K", true, out string m, out string tex);
            Assert.AreEqual("Models/Kicker/Kicker_Rigged", m);
            Assert.AreEqual("Models/Kicker/Kicker_BaseColor", tex, "the Arrows' kicker wears the model's own colours");
            Load().teams[1].ModelFor("K", true, out m, out tex);
            Assert.AreEqual("Models/Kicker/Kicker_Rigged", m);
            Assert.AreEqual("Models/Kicker/KickerGRN_BaseColor", tex, "the Grains' kicker wears the green recolour");
            Assert.AreEqual("NO_KICKER_MODEL", ConfigLoader.EnvName("kickerModel"));
        }

        [Test]
        public void EveryFormationSpotWearsTheModelForItsPosition()
        {
            // the bulky #64 on the line both ways, the receivers' #81 on the WRs, the tight end's #88 on the TE, the #24 in the backfield and
            // the secondary; a two-way player changes model with his spot (PlayersView rebuilds the body on a new position)
            var cfg = Load();
            var want = new Dictionary<string, string>
            {
                { "QB", "Models/QB/QB_Rigged" }, { "K", "Models/Kicker/Kicker_Rigged" },
                { "OL", "Models/Linebacker/Linebacker_Rigged" }, { "DL", "Models/Linebacker/Linebacker_Rigged" },
                { "LB", "Models/Linebacker/Linebacker_Rigged" },
                { "WR", "Models/WR/WR_Rigged" }, { "TE", "Models/TE/TE_Rigged" },
                { "RB", "Models/Player24/Player24_Rigged" }, { "CB", "Models/Player24/Player24_Rigged" }, { "S", "Models/Player24/Player24_Rigged" },
            };
            var spots = new List<SpotConfig>(cfg.formation.offense);
            spots.AddRange(cfg.formation.defense);
            foreach (var s in spots)
            {
                Assert.IsTrue(want.ContainsKey(s.pos), "a body is chosen for " + s.pos);
                var tex = new List<string>();
                foreach (var team in cfg.teams)
                {
                    team.ModelFor(s.pos, true, out string m, out string t);
                    Assert.AreEqual(want[s.pos], m, team.@short + " " + s.pos);
                    Assert.IsFalse(string.IsNullOrEmpty(t), team.@short + " " + s.pos + " has a colour map");
                    tex.Add(t);
                }
                Assert.AreNotEqual(tex[0], tex[1], s.pos + ": each team wears its own colours");
            }
        }
    }
}
