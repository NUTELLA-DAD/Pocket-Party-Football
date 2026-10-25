using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Runs the sim with no Unity at all: the baseline test, bot games and replays use this.
    public static class Headless
    {
        // A bots-only game: `players` bots, slots alternating teams. Returns one line per hashEveryTicks:
        // "<tick> <hash>".
        public static List<string> RunBots(GameConfig cfg, int seed, int seconds, int players)
        {
            var w = new World(cfg, seed);
            for (int i = 0; i < players; i++) w.AddPlayer(i, i % cfg.teams.Count, false);
            var lines = new List<string>();
            int ticks = seconds * cfg.sim.tickHz;
            var none = new Dictionary<int, PlayerInput>();
            for (int t = 1; t <= ticks; t++)
            {
                w.Step(none);
                if (t % cfg.sim.hashEveryTicks == 0) lines.Add(w.Tick + " " + w.Hash().ToString("x16"));
            }
            return lines;
        }
    }
}
