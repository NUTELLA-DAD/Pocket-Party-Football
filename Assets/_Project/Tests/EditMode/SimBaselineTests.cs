using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // The safety net (M0-d): seeded bot games must replay exactly. Each seed is run twice and must give identical
    // hashes, and must match the pinned file in Tests/Fixtures/baseline. Re-pin ONLY on purpose:
    // tools\snapshot-baseline.ps1 (and say why in the commit).
    public sealed class SimBaselineTests
    {
        public const string Dir = "Assets/_Project/Tests/Fixtures/baseline";

        public static List<string> Run(GameConfig cfg, int seed) => Headless.RunBots(cfg, seed, cfg.baseline.seconds, cfg.baseline.players);

        static IEnumerable<int> Seeds() => ConfigTests.Load().baseline.seeds;

        [TestCaseSource(nameof(Seeds))]
        public void SameSeedSameGame(int seed)
        {
            var cfg = ConfigTests.Load();
            var a = Run(cfg, seed);
            var b = Run(cfg, seed);
            Assert.AreEqual(cfg.baseline.seconds * cfg.sim.tickHz / cfg.sim.hashEveryTicks, a.Count);
            CollectionAssert.AreEqual(a, b);
        }

        [TestCaseSource(nameof(Seeds))]
        public void MatchesPinnedBaseline(int seed)
        {
            string path = Path.Combine(Dir, "seed" + seed + ".txt");
            Assert.IsTrue(File.Exists(path), "No pinned baseline at " + path + ": run tools\\snapshot-baseline.ps1 once.");
            var pinned = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var now = Run(ConfigTests.Load(), seed);
            CollectionAssert.AreEqual(pinned, now, "The bot game for seed " + seed + " changed. If that was meant, re-pin with tools\\snapshot-baseline.ps1.");
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            var cfg = ConfigTests.Load();
            CollectionAssert.AreNotEqual(Run(cfg, 1), Run(cfg, 2));
        }
    }
}
