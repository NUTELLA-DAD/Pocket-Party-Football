using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-d's gate: 22-bot games play like football. Bots are for "did we break anything?", never "is it fun?", so the
    // ranges are wide; the numbers are printed so a person can look.
    public sealed class BotGameTests
    {
        static World Play(int seed, int minutes)
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg, seed);
            for (int i = 0; i < minutes * 60 * cfg.sim.tickHz; i++) w.Step(null);
            return w;
        }

        static string Report(World w)
        {
            var sb = new StringBuilder();
            sb.Append(w.PlaysRun).Append(" plays, ").Append(w.YardsGained).Append(" yards (")
              .Append((w.YardsGained / (float)System.Math.Max(1, w.PlaysRun)).ToString("0.0")).Append(" a play):");
            foreach (var kv in w.Results) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
            return sb.ToString();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BotGamesLookLikeFootball(int seed)
        {
            var w = Play(seed, 10);
            string r = Report(w);
            TestContext.WriteLine("seed " + seed + ": " + r);
            Assert.GreaterOrEqual(w.PlaysRun, 20, "plays keep happening: " + r);
            float ypp = w.YardsGained / (float)w.PlaysRun;
            Assert.That(ypp, Is.InRange(1.5f, 15f), "yards per play: " + r);
            int Count(string k) => w.Results.TryGetValue(k, out int n) ? n : 0;
            Assert.Less(Count("SACKED"), w.PlaysRun * 0.4f, "the line holds up most of the time: " + r);
            Assert.Less(Count("INCOMPLETE"), w.PlaysRun * 0.6f, "most passes get somewhere: " + r);
            Assert.Less(Count("TOUCHDOWN"), w.PlaysRun * 0.3f, "touchdowns are special: " + r);
            Assert.Less(Count("PLAY OVER"), w.PlaysRun * 0.1f, "plays end on a whistle, not the clock: " + r);
        }

        // ---- WS-06: the bot coach and bots v2 ----

        // One 10-minute game with the coach and bots v2 on, counting what a box score would.
        public sealed class Box
        {
            public int Plays, Yards, Passes, Incomplete, Scrambles, Punts, FieldGoalTries;
            public SortedDictionary<string, int> Results, Calls = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            public float PerPlay => Yards / (float)System.Math.Max(1, Plays);
            public float IncompleteRate => Incomplete / (float)System.Math.Max(1, Passes);
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Plays).Append(" plays, ").Append(Yards).Append(" yards (").Append(PerPlay.ToString("0.0")).Append(" a play), ")
                  .Append(Passes).Append(" passes, ").Append(Incomplete).Append(" incomplete (").Append((IncompleteRate * 100f).ToString("0")).Append("%), ")
                  .Append(Scrambles).Append(" scrambles, ").Append(Punts).Append(" punts, ").Append(FieldGoalTries).Append(" field goal tries; results:");
                foreach (var kv in Results) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
                sb.Append(" calls:");
                foreach (var kv in Calls) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
                return sb.ToString();
            }
        }

        public static Box PlayV2(int seed, int minutes, System.Action<GameConfig> tweak = null)
        {
            var cfg = BotCoachTests.On();
            tweak?.Invoke(cfg);
            var w = WorldTests.Full(cfg, seed);
            var box = new Box();
            bool wasLive = false, scrambled = false;
            for (int i = 0; i < minutes * 60 * cfg.sim.tickHz; i++)
            {
                w.Step(null);
                bool live = w.Phase == Phase.Live;
                if (live && !wasLive)
                {
                    scrambled = false;
                    string call = w.Calls[w.Offense] ?? "(kickoff)";
                    box.Calls[call] = (box.Calls.TryGetValue(call, out int n) ? n : 0) + 1;
                    if (call == "punt") box.Punts++;
                    if (call == "fg" && w.Down >= cfg.downs.downsToGain) box.FieldGoalTries++;   // (not the extra point after a touchdown)
                }
                if (live && !scrambled)
                {
                    var qb = LivePlay.Qb(w);
                    if (qb != null && LivePlay.Carrier(w) == qb && !w.Thrown && Bots.Scrambling(w, qb)) { scrambled = true; box.Scrambles++; }
                }
                // a play ends: was it a pass?
                if (!live && wasLive && w.Thrown && w.Calls[w.Offense] != "punt" && w.Calls[w.Offense] != "fg") box.Passes++;
                wasLive = live;
            }
            box.Plays = w.PlaysRun;
            box.Yards = w.YardsGained;
            box.Results = w.Results;
            box.Incomplete = w.Results.TryGetValue("INCOMPLETE", out int inc) ? inc : 0;
            return box;
        }

        // The WS-06 gate: with the coach and bots v2 on, bot games give 4 to 6 yards a play and 25 to 40 percent of
        // passes fall incomplete. (With FEATURES.kicking on as well, PunterAndKickerGetWork checks the kicks.)
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CoachedBotGamesLookLikeFootball(int seed)
        {
            var box = PlayV2(seed, 10);
            TestContext.WriteLine("seed " + seed + ": " + box);
            Assert.GreaterOrEqual(box.Plays, 40, "plays keep happening: " + box);
            Assert.That(box.PerPlay, Is.InRange(4f, 6f), "yards per play: " + box);
            Assert.GreaterOrEqual(box.Passes, 10, "the coach calls passes: " + box);
            Assert.That(box.IncompleteRate, Is.InRange(0.25f, 0.4f), "incompletions: " + box);
            Assert.Greater(box.Scrambles, 0, "the QB scrambles sometimes: " + box);
            int Count(string k) => box.Results.TryGetValue(k, out int n) ? n : 0;
            Assert.Less(Count("SACKED"), box.Plays * 0.15f, "sacks are rare: " + box);
            Assert.Less(Count("PLAY OVER"), box.Plays * 0.05f, "plays end on a whistle, not the clock: " + box);
        }

        // Once kicking (WS-01) has merged: every 10-minute coached game has a punt and a field goal try.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PunterAndKickerGetWork(int seed)
        {
            var cfg = ConfigTests.Load();
            if (!cfg.features.ContainsKey("kicking")) Assert.Inconclusive("kicking (WS-01) has not merged yet");
            var box = PlayV2(seed, 10, c => c.features["kicking"] = true);
            TestContext.WriteLine("seed " + seed + ": " + box);
            Assert.GreaterOrEqual(box.Punts, 1, "a punt: " + box);
            Assert.GreaterOrEqual(box.FieldGoalTries, 1, "a field goal try: " + box);
            Assert.That(box.PerPlay, Is.InRange(4f, 6f), "yards per play: " + box);
        }

        [Test]
        public void ABlockHoldsARusherUp()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.Calls[w.Offense] = "slants"; w.Calls[w.Defense] = "mblitz";
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            for (int i = 0; i < cfg.sim.tickHz; i++) w.Step(null);
            int held = 0;
            foreach (var p in w.Players) if (p.Team == w.Defense && p.BlockedTicks > 0) held++;
            Assert.Greater(held, 0, "the line engages the rush");
        }

        [Test]
        public void ABotPlaysForAnIdlePerson()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            var me = w.ReplaceBot(w.Defense, 500);
            w.Calls[w.Offense] = "slants"; w.Calls[w.Defense] = "cover3";
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            int idle = (int)(cfg.bots.idleTakeoverS * cfg.sim.tickHz);
            bool helped = false;
            for (int i = 0; i < idle + cfg.sim.tickHz && w.Phase == Phase.Live; i++) { w.Step(null); helped |= me.BotHelp; }
            Assert.IsTrue(helped || w.Phase != Phase.Live, "after " + cfg.bots.idleTakeoverS + " s idle a bot plays the spot");
            // touching the stick takes it back
            w.Step(new Dictionary<int, PlayerInput> { { 500, new PlayerInput { MoveX = 1f } } });
            Assert.IsFalse(me.BotHelp);
        }

        [Test]
        public void TheBotQbReadsBeforeHeThrows()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.Calls[w.Offense] = "verts"; w.Calls[w.Defense] = "cover2";
            for (int i = 0; i < cfg.sim.tickHz * 30 && w.Phase != Phase.Live; i++) w.Step(null);
            int first = (int)(cfg.bots.readTimesS[0] * cfg.sim.tickHz);
            for (int i = 0; i < first - 2; i++) w.Step(null);
            Assert.IsFalse(w.Thrown, "no throw before his first read");
            for (int i = 0; i < cfg.bots.readDeadlineS * cfg.sim.tickHz && !w.Thrown && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.IsTrue(w.Thrown || w.Phase != Phase.Live, "he throws by his deadline (or was sacked first)");
        }
    }
}
