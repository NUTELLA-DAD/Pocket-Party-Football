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

        // What a box score would count, over one or more bot games. Scrimmage plays only for yards a play (a kickoff,
        // a punt or a field goal is not a play from scrimmage); a pass is a play where the ball was thrown.
        public sealed class Box
        {
            public int Games, Plays, Yards, Passes, Incomplete, Intercepted, Scrambles, Punts, FieldGoalTries, GamesWithAKick;
            public readonly SortedDictionary<string, int> Results = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> Calls = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            public float PerPlay => Yards / (float)System.Math.Max(1, Plays);
            public float IncompleteRate => Incomplete / (float)System.Math.Max(1, Passes);
            public float InterceptRate => Intercepted / (float)System.Math.Max(1, Passes);

            public void Add(Box o)
            {
                Games += o.Games; Plays += o.Plays; Yards += o.Yards; Passes += o.Passes; Incomplete += o.Incomplete;
                Intercepted += o.Intercepted; Scrambles += o.Scrambles; Punts += o.Punts; FieldGoalTries += o.FieldGoalTries;
                GamesWithAKick += o.GamesWithAKick;
                foreach (var kv in o.Results) Results[kv.Key] = (Results.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
                foreach (var kv in o.Calls) Calls[kv.Key] = (Calls.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Plays).Append(" plays from scrimmage, ").Append(Yards).Append(" yards (").Append(PerPlay.ToString("0.0")).Append(" a play), ")
                  .Append(Passes).Append(" passes, ").Append(Incomplete).Append(" incomplete (").Append((IncompleteRate * 100f).ToString("0")).Append("%), ")
                  .Append(Intercepted).Append(" picked off, ").Append(Scrambles).Append(" scrambles, ")
                  .Append(Punts).Append(" punts, ").Append(FieldGoalTries).Append(" field goal tries; results:");
                foreach (var kv in Results) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
                sb.Append(" calls:");
                foreach (var kv in Calls) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(',');
                return sb.ToString();
            }
        }

        static bool IsKick(string call) => call == "punt" || call == "fg" || call == KickoffCall;
        const string KickoffCall = "(kickoff)";

        // One bot game with the coach and bots v2 on (plus whatever `tweak` switches on).
        public static Box PlayV2(int seed, int minutes, System.Action<GameConfig> tweak = null)
        {
            var cfg = BotCoachTests.On();
            tweak?.Invoke(cfg);
            var w = WorldTests.Full(cfg, seed);
            var box = new Box { Games = 1 };
            bool wasLive = false, scrambled = false;
            string call = null;
            int yardsAtSnap = 0;
            for (int i = 0; i < minutes * 60 * cfg.sim.tickHz; i++)
            {
                w.Step(null);
                bool live = w.Phase == Phase.Live;
                if (live && !wasLive)
                {
                    scrambled = false;
                    yardsAtSnap = w.YardsGained;
                    call = w.Calls[w.Offense] ?? KickoffCall;
                    box.Calls[call] = (box.Calls.TryGetValue(call, out int n) ? n : 0) + 1;
                    if (call == "punt") box.Punts++;
                    if (call == "fg" && w.Down >= cfg.downs.downsToGain) box.FieldGoalTries++;   // (not the extra point after a touchdown)
                }
                if (live && !scrambled)
                {
                    var qb = LivePlay.Qb(w);
                    if (qb != null && LivePlay.Carrier(w) == qb && !w.Thrown && Bots.Scrambling(w, qb)) { scrambled = true; box.Scrambles++; }
                }
                if (!live && wasLive && !IsKick(call))
                {
                    box.Plays++;
                    box.Yards += w.YardsGained - yardsAtSnap;
                    box.Results[w.Result] = (box.Results.TryGetValue(w.Result, out int n) ? n : 0) + 1;
                    if (w.Thrown) box.Passes++;
                    if (w.Result == "INCOMPLETE") box.Incomplete++;
                    if (w.Result == "INTERCEPTED" || w.Result == "PICK SIX") box.Intercepted++;
                }
                wasLive = live;
            }
            if (box.Punts + box.FieldGoalTries > 0) box.GamesWithAKick = 1;
            return box;
        }

        // The seeded 10-minute games the WS-06 gates look at, added up (and each one printed). Twelve, not three: one
        // game is only ~20 plays from scrimmage, so a single long run moves its average by a yard or two, and the laptop's
        // Unity runtime rounds floats a little differently from a plain .NET run, so its seeded games are different
        // games. Over twelve the numbers settle to within about half a yard.
        public const int GateGames = 12;

        static Box GateRun(System.Action<GameConfig> tweak)
        {
            var all = new Box();
            for (int seed = 1; seed <= GateGames; seed++)
            {
                var g = PlayV2(seed, 10, tweak);
                TestContext.WriteLine("seed " + seed + ": " + g);
                all.Add(g);
            }
            TestContext.WriteLine("all " + GateGames + ": " + all);
            return all;
        }

        // Every rule switch in game.json on: the game as the laptop will play it once the merge batch flips them.
        static void EverythingOn(GameConfig cfg)
        {
            var keys = new List<string>(cfg.features.Keys);
            foreach (var k in keys) cfg.features[k] = true;
        }

        // The WS-06 gate, with every switch on (it needs kicking, defender moves and carrier moves, WS-01/03/04, merged):
        // 10-minute bot games give 4 to 6 yards a play and 25 to 40 percent incomplete passes (over the twelve games
        // together); punts and field goal tries happen (a 10-minute game is only ~6 drives, so not every game has both,
        // but nearly every one has a kick on 4th down).
        [Test]
        public void CoachedBotGamesLookLikeFootball()
        {
            var cfg = ConfigTests.Load();
            foreach (var k in new[] { "kicking", "defmoves", "carriermoves" })
                if (!cfg.features.ContainsKey(k)) Assert.Inconclusive("needs FEATURES." + k + " (WS-01, WS-03 and WS-04) merged first");
            var box = GateRun(EverythingOn);
            Assert.That(box.PerPlay, Is.InRange(4f, 6f), "yards a play: " + box);
            Assert.That(box.IncompleteRate, Is.InRange(0.25f, 0.4f), "incompletions: " + box);
            Assert.Less(box.InterceptRate, 0.08f, "interceptions are rare: " + box);
            Assert.GreaterOrEqual(box.Punts, box.Games / 2, "punts: " + box);
            Assert.GreaterOrEqual(box.FieldGoalTries, box.Games / 4, "field goal tries: " + box);
            Assert.GreaterOrEqual(box.GamesWithAKick, box.Games * 3 / 4, "games with a punt or a field goal try: " + box);
            Assert.Greater(box.Scrambles, 0, "the QB scrambles sometimes: " + box);
            int Count(string k) => box.Results.TryGetValue(k, out int n) ? n : 0;
            // the pass rush wins sometimes (with the swim and bull rush), but not most of the time
            Assert.That(Count("SACKED") / (float)box.Plays, Is.InRange(0.02f, 0.12f), "sacks: " + box);
            Assert.GreaterOrEqual(Count("OUT OF BOUNDS"), 1, "a carrier goes out of bounds now and then: " + box);
            Assert.Less(Count("PLAY OVER"), box.Plays * 0.05f, "plays end on a whistle, not the clock: " + box);
        }

        // The coach and bots v2 on their own (what main has today): passes are thrown, mostly caught, rarely picked off.
        // Wider ranges than the gate above, because without the defender moves the offense gains more.
        [Test]
        public void CoachAndBotsV2OnTheirOwn()
        {
            var box = GateRun(null);
            Assert.GreaterOrEqual(box.Plays, GateGames * 20, "plays keep happening: " + box);
            Assert.That(box.PerPlay, Is.InRange(4f, 10f), "yards a play: " + box);
            Assert.GreaterOrEqual(box.Passes, GateGames * 10, "the coach calls passes: " + box);
            Assert.That(box.IncompleteRate, Is.InRange(0.25f, 0.5f), "incompletions: " + box);
            Assert.Less(box.InterceptRate, 0.08f, "interceptions are rare: " + box);
            Assert.Greater(box.Scrambles, 0, "the QB scrambles sometimes: " + box);
            int Count(string k) => box.Results.TryGetValue(k, out int n) ? n : 0;
            Assert.Greater(Count("SACKED"), 0, "the pass rush gets home sometimes: " + box);
            Assert.Less(Count("SACKED"), box.Plays * 0.15f, "sacks are rare: " + box);
            Assert.GreaterOrEqual(Count("OUT OF BOUNDS"), 1, "a carrier near the sideline steps out now and then: " + box);
            Assert.Less(Count("PLAY OVER"), box.Plays * 0.05f, "plays end on a whistle, not the clock: " + box);
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
