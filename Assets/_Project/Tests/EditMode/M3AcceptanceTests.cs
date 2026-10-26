using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M3 "A full game" (docs/PLAN.md): kicks, the clock, halftime, the end and the winner, played as whole seeded
    // 22-bot games with every switch as game.json ships it (kicking on). The single rules (a safety, the replay, the
    // result card, rejoin, bot cover) have their own tests; docs/progress/m3-acceptance.md lists them all.
    public sealed class M3AcceptanceTests
    {
        static GameConfig Cfg()
        {
            var cfg = ConfigTests.Load();
            cfg.features["kicking"] = true;   // (ConfigTests.Load turns it off for other features' tests)
            return cfg;
        }

        // One live play, as it began and ended.
        struct Play
        {
            public int Quarter;
            public bool Kickoff, Try, SecondHalf;
            public KickKind Kind;
            public string Result;
            public int StartTick, EndTick;
            public bool ClockRanAfter;   // the clock running in the whistle phase after it
        }

        sealed class Game
        {
            public World W;
            public readonly List<Play> Plays = new List<Play>();
            public int LongestPhaseTicks; public Phase LongestPhase;
            public int ScrimmagePlays => Plays.Count(p => !p.Kickoff && !p.Try);
        }

        static Game PlayAGame(GameConfig cfg, int seed)
        {
            var g = new Game { W = WorldTests.Full(cfg, seed) };
            var w = g.W;
            int hz = cfg.sim.tickHz, max = hz * 60 * 60;
            Phase last = w.Phase; int since = 0;
            Play cur = default; bool live = false;
            for (int i = 0; i < max && !Clock.IsOver(w); i++)
            {
                w.Step(null);
                if (w.Phase == last) since++;
                else
                {
                    if (since > g.LongestPhaseTicks && !Clock.IsHalftime(w)) { g.LongestPhaseTicks = since; g.LongestPhase = last; }
                    since = 0;
                }
                if (w.Phase == Phase.Live && last != Phase.Live)
                {
                    cur = new Play { Quarter = w.GameClock.Quarter, Kickoff = w.Kick.Kickoff, Try = w.Kick.Try, Kind = Kicking.Mode(w), SecondHalf = w.GameClock.SecondHalf, StartTick = w.Tick };
                    live = true;
                }
                if (live && w.Phase == Phase.Whistle && last == Phase.Live)
                {
                    cur.Result = w.Result; cur.EndTick = w.Tick; cur.ClockRanAfter = w.GameClock.Running;
                    g.Plays.Add(cur); live = false;
                }
                last = w.Phase;
            }
            return g;
        }

        static string Summary(Game g, int seed)
        {
            var w = g.W;
            var counts = g.Plays.GroupBy(p => p.Result).OrderBy(k => k.Key).Select(k => k.Key + " " + k.Count());
            return "seed " + seed + ": " + g.Plays.Count + " live plays (" + g.ScrimmagePlays + " from scrimmage, " +
                   g.Plays.Count(p => p.Kickoff) + " kickoffs, " + g.Plays.Count(p => p.Try) + " tries, " +
                   g.Plays.Count(p => p.Kind == KickKind.Punt) + " punts, " + g.Plays.Count(p => p.Kind == KickKind.FieldGoal && !p.Try) + " field goal tries), " +
                   "score " + w.Score[0] + "-" + w.Score[1] + ", winner " + Clock.Winner(w) + ", longest " + g.LongestPhase + " " +
                   (g.LongestPhaseTicks / (float)w.Cfg.sim.tickHz).ToString("0.0") + " s; results: " + string.Join(", ", counts);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ABotGameIsPlayedToTheFinalWhistle(int seed)
        {
            var cfg = Cfg();
            var g = PlayAGame(cfg, seed);
            var w = g.W;
            string s = Summary(g, seed);
            TestContext.WriteLine(s);

            // the end: four quarters, a second half, the final whistle and a winner (or a tie)
            Assert.IsTrue(Clock.IsOver(w), "the game ended inside an hour of play: " + s);
            Assert.AreEqual(cfg.clock.quarters, w.GameClock.Quarter, s);
            Assert.IsTrue(w.GameClock.SecondHalf, "there was a second half: " + s);
            Assert.AreEqual(w.Score[0] > w.Score[1] ? 0 : w.Score[1] > w.Score[0] ? 1 : -1, Clock.Winner(w), "the winner has more points");

            // about 50 plays, no stalls, a sane score
            Assert.That(g.ScrimmagePlays, Is.InRange(35, 75), "plays from scrimmage (PLAN: ~50): " + s);
            Assert.Less(g.LongestPhaseTicks, (cfg.huddle.seconds + cfg.huddle.setSeconds + cfg.play.maxPlayS) * cfg.sim.tickHz, "no phase stalls: " + s);
            foreach (int pts in w.Score) Assert.That(pts, Is.InRange(0, 70), "a sane score: " + s);
            foreach (var p in g.Plays)
                Assert.LessOrEqual(p.EndTick - p.StartTick, (cfg.play.maxPlayS + 1f) * cfg.sim.tickHz, "every play ends inside maxPlayS: " + s);

            // kicks: the opening kickoff, the second half's, a try after every touchdown, a kickoff after every score
            Assert.IsTrue(g.Plays[0].Kickoff, "the game opens with a kickoff: " + s);
            int firstSecond = g.Plays.FindIndex(p => p.SecondHalf);
            Assert.GreaterOrEqual(firstSecond, 0, s);
            Assert.IsTrue(g.Plays[firstSecond].Kickoff, "the second half opens with a kickoff: " + s);
            for (int i = 0; i + 1 < g.Plays.Count; i++)
            {
                var p = g.Plays[i]; var next = g.Plays[i + 1];
                bool halftimeBetween = !p.SecondHalf && next.SecondHalf;
                if (p.Result == "TOUCHDOWN" || p.Result == "PICK SIX")
                    Assert.IsTrue(next.Try || (halftimeBetween && next.Kickoff), "play " + (i + 1) + " " + p.Result + ": a try next, got " + Describe(next) + "; " + s);
                if (p.Try || p.Result == "FIELD GOAL" || p.Result == "SAFETY")
                    Assert.IsTrue(next.Kickoff, "play " + (i + 1) + " " + (p.Try ? "a try (" + p.Result + ")" : p.Result) + ": a kickoff next, got " + Describe(next) + "; " + s);
            }

            // the clock: stopped by the stop results, running on after the rest (a quarter's last play aside)
            foreach (var p in g.Plays.Where(p => !string.IsNullOrEmpty(p.Result)))
            {
                bool stops = cfg.clock.stopOn.Contains(p.Result);
                if (stops) Assert.IsFalse(p.ClockRanAfter, p.Result + " stops the clock: " + s);
            }
            Assert.IsTrue(g.Plays.Any(p => !cfg.clock.stopOn.Contains(p.Result) && p.ClockRanAfter), "some plays let the clock run on: " + s);
        }

        static string Describe(Play p) => p.Kickoff ? "a kickoff" : p.Try ? "a try" : p.Kind == KickKind.Punt ? "a punt" : p.Kind == KickKind.FieldGoal ? "a field goal" : "a scrimmage play";

        // Over a few games every kind of kick shows up.
        [Test]
        public void BotGamesKickEveryKindOfKick()
        {
            var cfg = Cfg();
            var all = new List<Play>();
            var sb = new StringBuilder();
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var g = PlayAGame(cfg, seed);
                all.AddRange(g.Plays);
                sb.Append(Summary(g, seed)).Append('\n');
            }
            string s = sb.ToString();
            TestContext.WriteLine(s);
            Assert.Greater(all.Count(p => p.Kickoff), 3, "kickoffs: " + s);
            Assert.Greater(all.Count(p => p.Kind == KickKind.Punt), 0, "punts: " + s);
            Assert.Greater(all.Count(p => p.Kind == KickKind.FieldGoal && !p.Try), 0, "field goal tries: " + s);
            Assert.Greater(all.Count(p => p.Try), 0, "tries after touchdowns: " + s);
            Assert.Greater(all.Count(p => p.Result == "EXTRA POINT" || p.Result == "TWO POINTS"), 0, "a try that scores: " + s);
        }
    }
}
