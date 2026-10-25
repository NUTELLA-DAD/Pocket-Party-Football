using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Runs the sim with no Unity at all: the baseline test, bot games and replays use this.
    public static class Headless
    {
        // A bots-only game: `players` bots filling the formations, slots alternating teams, wandering (so the
        // mover, bumping, bounds and stamina all get exercised). Partway through, the QB throws a lob, so the ball's
        // flight is in the hashes too. Returns one line per hashEveryTicks: "<tick> <hash>".
        public static List<string> RunBots(GameConfig cfg, int seed, int seconds, int players)
        {
            var w = new World(cfg, seed) { BotsWander = true };
            for (int i = 0; i < players; i++) w.AddPlayer(i, i % cfg.teams.Count, false);
            var lines = new List<string>();
            int ticks = seconds * cfg.sim.tickHz;
            int throwAt = cfg.sim.tickHz;
            var none = new Dictionary<int, PlayerInput>();
            for (int t = 1; t <= ticks; t++)
            {
                if (t == throwAt) w.Throw(w.Ball.X + cfg.formation.attackDir * cfg.bots.wanderYd, w.Ball.Z, true);
                w.Step(none);
                if (t % cfg.sim.hashEveryTicks == 0) lines.Add(w.Tick + " " + w.Hash().ToString("x16"));
            }
            return lines;
        }
    }
}
