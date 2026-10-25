using System.IO;
using PocketFootball.Sim;
using UnityEditor;
using UnityEngine;

namespace PocketFootball.Editor
{
    // Re-pins the bot-game baseline. Only on purpose, when a rule change is MEANT to change bot games:
    // Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod PocketFootball.Editor.Fixtures.SnapshotBaseline
    public static class Fixtures
    {
        const string ConfigPath = "Assets/_Project/Config/game.json";
        const string Dir = "Assets/_Project/Tests/Fixtures/baseline";

        [MenuItem("Pocket Football/Snapshot Baseline")]
        public static void SnapshotBaseline()
        {
            var cfg = ConfigLoader.Parse(File.ReadAllText(ConfigPath));
            Directory.CreateDirectory(Dir);
            foreach (int seed in cfg.baseline.seeds)
            {
                var lines = Headless.RunBots(cfg, seed, cfg.baseline.seconds, cfg.baseline.players);
                File.WriteAllText(Path.Combine(Dir, "seed" + seed + ".txt"), string.Join("\n", lines) + "\n");
                Debug.Log("[fixtures] seed " + seed + ": " + lines.Count + " hashes, last " + lines[lines.Count - 1]);
            }
            AssetDatabase.Refresh();
        }
    }
}
