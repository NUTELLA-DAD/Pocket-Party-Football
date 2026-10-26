using System.Text;
using NUnit.Framework;

namespace PocketFootball.Tests.EditMode
{
    // The scoring numbers of whole seeded 22-bot games (four quarters to the final whistle, every switch on), for the
    // balance write-up (docs/progress/balance-scoring.md). Explicit: it only prints; the 24-game gate in BotGameTests
    // holds the targets. Run it with -testFilter BalanceScoringTests.
    public sealed class BalanceScoringTests
    {
        public const int Games = 12, Minutes = 45;   // (a game ends on its clock; 45 is a ceiling)

        static void EverythingOn(PocketFootball.Sim.GameConfig cfg)
        {
            var keys = new System.Collections.Generic.List<string>(cfg.features.Keys);
            foreach (var k in keys) cfg.features[k] = true;
        }

        [Test, Explicit("prints the 12-game scoring table; the targets live in BotGameTests")]
        public void FullBotGamesScoringTable()
        {
            var all = new BotGameTests.Box();
            var sb = new StringBuilder("| seed | score | TDs | picks | incomplete | yd/play | sacks | FG tries | snaps |\n|---|---|---|---|---|---|---|---|---|\n");
            for (int seed = 1; seed <= Games; seed++)
            {
                var g = BotGameTests.PlayV2(seed, Minutes, EverythingOn);
                sb.Append("| ").Append(seed).Append(" | ").Append(g.Final).Append(" | ").Append(g.Touchdowns).Append(" | ").Append(g.Intercepted)
                  .Append(" | ").Append((g.IncompleteRate * 100f).ToString("0")).Append("% | ").Append(g.PerPlay.ToString("0.0"))
                  .Append(" | ").Append(g.Count("SACKED")).Append(" | ").Append(g.FieldGoalTries).Append(" | ").Append(g.Plays).Append(" |\n");
                all.Add(g);
            }
            float n = all.Games;
            sb.Append("| **all " + Games + "** | " + (all.Points / n).ToString("0.0") + " pts/game | " + (all.Touchdowns / n).ToString("0.0") + "/game | " +
                      (all.Intercepted / n).ToString("0.0") + "/game | " + (all.IncompleteRate * 100f).ToString("0") + "% | " + all.PerPlay.ToString("0.0") +
                      " | " + (all.Count("SACKED") / n).ToString("0.0") + "/game | " + all.GamesWithAFieldGoalTry + " of " + Games + " games | " + (all.Plays / n).ToString("0") + "/game |\n");
            sb.Append("picks a pass: " + (all.InterceptRate * 100f).ToString("0.0") + "%\n");
            sb.Append("0-0 games: " + all.ScorelessGames + "\n");
            TestContext.WriteLine(sb.ToString());
        }
    }
}
