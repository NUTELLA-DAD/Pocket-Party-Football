using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // The scoring balance pass (balance/scoring): whole seeded 22-bot games, four quarters to the final whistle, every
    // switch on. A party game wants points: about 3 to 6 touchdowns a game between the two teams, 1 to 2 interceptions,
    // 30 to 40 percent of passes incomplete, 4.5 to 6.5 yards a play, 1 to 3 sacks, a field goal try in most games, and
    // never a 0-0. docs/progress/balance-scoring.md has the before and after.
    public sealed class BalanceScoringTests
    {
        public const int Games = 12, Minutes = 45;   // (a game ends on its clock, ~25 minutes of play; 45 is a ceiling)

        static void EverythingOn(GameConfig cfg)
        {
            var keys = new System.Collections.Generic.List<string>(cfg.features.Keys);
            foreach (var k in keys) cfg.features[k] = true;
        }

        public static BotGameTests.Box Run(out string table)
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
                      " | " + (all.Count("SACKED") / n).ToString("0.0") + "/game (" + (all.SackRate * 100f).ToString("0") + "% of dropbacks) | " +
                      all.GamesWithAFieldGoalTry + " of " + Games + " games | " + (all.Plays / n).ToString("0") + "/game |\n");
            sb.Append("picks a pass: " + (all.InterceptRate * 100f).ToString("0.0") + "%\n");
            sb.Append("0-0 games: " + all.ScorelessGames + "\n");
            table = sb.ToString();
            return all;
        }

        [Test]
        public void FullBotGamesHitTheScoringTargets()
        {
            var all = Run(out string table);
            TestContext.WriteLine(table);
            TestContext.WriteLine("all: " + all);
            float n = all.Games;
            // per game (with botQuickCall on, 2026-10-26: a full bot game is ~90 snaps)
            Assert.That(all.Touchdowns / n, Is.InRange(2.5f, 6f), "touchdowns a game (both teams):\n" + table);
            Assert.That(all.Intercepted / n, Is.InRange(1f, 2.5f), "interceptions a game:\n" + table);
            Assert.AreEqual(0, all.ScorelessGames, "no 0-0 games:\n" + table);
            Assert.Greater(all.GamesWithAFieldGoalTry, Games / 2, "a field goal try in most games:\n" + table);
            // rates, as the 24-game gate (BotGameTests) has them
            Assert.That(all.IncompleteRate, Is.InRange(BotGameTests.TargetIncompleteMin, BotGameTests.TargetIncompleteMax), "incompletions:\n" + table);
            Assert.That(all.PerPlay, Is.InRange(BotGameTests.TargetYardsMin, BotGameTests.TargetYardsMax), "yards a play:\n" + table);
            Assert.That(all.SackRate, Is.InRange(BotGameTests.TargetSackRateMin, BotGameTests.TargetSackRateMax), "sacks a dropback:\n" + table);
        }
    }
}
