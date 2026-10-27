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
            var kc = Load().teams[0];
            kc.ModelFor("LB", true, out string m, out string tex);
            Assert.AreEqual("Models/Linebacker/Linebacker_Rigged", m);
            Assert.AreEqual("Models/Linebacker/LinebackerKC_BaseColor", tex, "the Chiefs' linebackers wear their own colours");
            var ssk = Load().teams[1];
            ssk.ModelFor("LB", true, out m, out tex);
            Assert.AreEqual("Models/Linebacker/Linebacker_Rigged", m);
            Assert.AreEqual("Models/Linebacker/Linebacker_BaseColor", tex);
            ssk.ModelFor("CB", true, out m, out _);
            Assert.AreEqual(ssk.model, m, "a position with no model of its own wears the team's");
            ssk.ModelFor("LB", false, out m, out _);
            Assert.AreEqual(ssk.model, m, "NO_POS_MODELS=1: the team's model for every position");
            Assert.IsFalse(Load(new Dictionary<string, string> { { "NO_POS_MODELS", "1" } }).Feature("posModels"));
        }
    }
}
