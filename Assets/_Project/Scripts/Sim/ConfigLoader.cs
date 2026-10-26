using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace PocketFootball.Sim
{
    // Parses game.json. The environment is passed in (the sim never reads it itself), so tests can fake it.
    public static class ConfigLoader
    {
        public static GameConfig Parse(string json, Func<string, string> env = null)
        {
            var cfg = JsonConvert.DeserializeObject<GameConfig>(json) ?? new GameConfig();
            if (env != null) ApplyEnvOverrides(cfg, env);
            return cfg;
        }

        // The plays, one JSON text per play file. Sorted by id so the order never depends on the file system.
        public static void AddPlays(GameConfig cfg, IEnumerable<string> jsons)
        {
            foreach (var j in jsons)
            {
                var p = JsonConvert.DeserializeObject<PlayConfig>(j);
                if (p != null && !string.IsNullOrEmpty(p.id)) cfg.plays.Add(p);
            }
            cfg.plays.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        }

        // FEATURES.fieldStripes is switched off by NO_FIELD_STRIPES=1.
        public static void ApplyEnvOverrides(GameConfig cfg, Func<string, string> env)
        {
            var off = new List<string>();
            foreach (var kv in cfg.features)
                if (env(EnvName(kv.Key)) == "1") off.Add(kv.Key);
            foreach (var k in off) cfg.features[k] = false;
        }

        public static string EnvName(string feature)
        {
            var sb = new StringBuilder("NO_");
            for (int i = 0; i < feature.Length; i++)
            {
                char c = feature[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }
}
