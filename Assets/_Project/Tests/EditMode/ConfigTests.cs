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

        public static GameConfig Load(Dictionary<string, string> env = null)
        {
            string json = File.ReadAllText("Assets/_Project/Config/game.json");
            var cfg = ConfigLoader.Parse(json, k => env != null && env.TryGetValue(k, out var v) ? v : null);
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
    }
}
