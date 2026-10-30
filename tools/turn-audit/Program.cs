// The turn gauntlet outside Unity (TurnAudit.cs): `dotnet run -c Release -- [seconds] [seeds] [ENV=1 ...]` from this
// folder, e.g. `-- 600 3 NO_CALM_TURNS=1` for the old turning. Prints one report per seed.
using System;
using System.Collections.Generic;
using System.IO;
using PocketFootball.Sim;
using PocketFootball.Tests.EditMode;

static class Program
{
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 300, seeds = args.Length > 1 ? int.Parse(args[1]) : 3;
        var env = new Dictionary<string, string> { ["NO_KICKING"] = "1" };
        for (int i = 2; i < args.Length; i++) { var kv = args[i].Split('='); env[kv[0]] = kv.Length > 1 ? kv[1] : "1"; }
        var cfg = ConfigLoader.Parse(File.ReadAllText(Path.Combine(root, "Assets/_Project/Config/game.json")), k => env.TryGetValue(k, out var v) ? v : null);
        var plays = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Assets/_Project/Config/plays"), "*.json")) plays.Add(File.ReadAllText(f));
        ConfigLoader.AddPlays(cfg, plays);
        for (int seed = 1; seed <= seeds; seed++)
            Console.WriteLine("seed " + seed + ": " + TurnAudit.Run(cfg, seed, seconds).Report());
    }
}
