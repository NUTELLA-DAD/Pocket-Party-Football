using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // WS-00: the shared event list (Sim/Events.cs). Bot games fire every kind, the events say who and where, they
    // come out the same for the same seed, and they are not part of the game (the pinned baseline in
    // SimBaselineTests is unchanged by them).
    public sealed class EventsTests
    {
        const int Me = 500;

        static void Run(World w, int ticks, Dictionary<int, PlayerInput> input = null) { for (int i = 0; i < ticks; i++) w.Step(input); }
        static Dictionary<int, PlayerInput> Press(PlayerInput i) => new Dictionary<int, PlayerInput> { { Me, i } };

        // Lined up and Set, both plays called, a person at QB.
        static World AtTheLine(string offensePlay = "slants", string defensePlay = "cover3", System.Action<GameConfig> tune = null)
        {
            var cfg = ConfigTests.Load();
            tune?.Invoke(cfg);
            var w = WorldTests.Full(cfg);
            w.Step(null);
            w.ReplaceBot(w.Offense, Me);
            w.Calls[w.Defense] = defensePlay;
            Assert.IsTrue(Huddle.Lock(w, Me, offensePlay));
            Assert.AreEqual(Phase.Set, w.Phase);
            return w;
        }

        static void ClearDefense(World w)
        {
            foreach (var p in w.Players) if (p.Team == w.Defense) { p.X = w.LineOfScrimmageX + w.Cfg.formation.attackDir * 40f; p.Z = 25f; p.HomeX = p.X; p.HomeZ = p.Z; }
        }

        static List<GameEvent> All(World w) => w.Events.Since(0, new List<GameEvent>());

        static List<EventKind> Kinds(World w)
        {
            var k = new List<EventKind>();
            foreach (var e in All(w)) k.Add(e.Kind);
            return k;
        }

        // Kinds a bot game need not fire, each proved by a scripted test below: bots don't juke yet (WS-04 and WS-06
        // give them moves), and since the bigger players (PR #2) the line holds every rush and nobody runs out of
        // bounds, so sacks and out of bounds are left to the bot coach's tuning.
        static readonly EventKind[] Scripted = { EventKind.Juke, EventKind.Sack, EventKind.OutOfBounds };

        [Test]
        public void TenMinuteBotGamesFireEveryKind()
        {
            var cfg = ConfigTests.Load();
            var seen = new SortedDictionary<EventKind, int>();
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var w = WorldTests.Full(cfg, seed);
                Run(w, 10 * 60 * cfg.sim.tickHz);
                foreach (EventKind k in System.Enum.GetValues(typeof(EventKind)))
                    seen[k] = (seen.TryGetValue(k, out int n) ? n : 0) + w.Events.Total(k);
                // the totals agree with the play-by-play numbers
                Assert.AreEqual(w.PlaysRun, w.Events.Total(EventKind.Whistle), "one whistle a play, seed " + seed);
                int Count(string r) => w.Results.TryGetValue(r, out int c) ? c : 0;
                Assert.AreEqual(Count("SACKED"), w.Events.Total(EventKind.Sack), "sacks, seed " + seed);
                Assert.AreEqual(Count("INCOMPLETE"), w.Events.Total(EventKind.Incomplete), "incompletions, seed " + seed);
                Assert.AreEqual(Count("TOUCHDOWN") + Count("PICK SIX"), w.Events.Total(EventKind.Touchdown), "touchdowns, seed " + seed);
                Assert.AreEqual(Count("INTERCEPTED") + Count("PICK SIX"), w.Events.Total(EventKind.Interception), "interceptions, seed " + seed);
                // (an interception return that ends in a tackle or out of bounds is still called INTERCEPTED)
                Assert.GreaterOrEqual(w.Events.Total(EventKind.Tackle), Count("TACKLED"), "tackles, seed " + seed);
                Assert.GreaterOrEqual(w.Events.Total(EventKind.OutOfBounds), Count("OUT OF BOUNDS"), "out of bounds, seed " + seed);
            }
            var sb = new StringBuilder();
            foreach (var kv in seen) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
            TestContext.WriteLine("three 10-minute bot games: " + sb);
            foreach (var kv in seen)
                if (!Scripted.Contains(kv.Key)) Assert.Greater(kv.Value, 0, kv.Key + " never fired: " + sb);
        }

        [Test]
        public void SameSeedSameEvents()
        {
            var cfg = ConfigTests.Load();
            string Game(int seed)
            {
                var w = WorldTests.Full(cfg, seed);
                Run(w, 2 * 60 * cfg.sim.tickHz);
                var sb = new StringBuilder();
                foreach (var e in All(w)) sb.Append(e).Append(' ').Append(e.X.ToString("R")).Append(' ').Append(e.Z.ToString("R")).Append('\n');
                return sb.ToString();
            }
            string a = Game(1);
            Assert.IsNotEmpty(a);
            Assert.AreEqual(a, Game(1));
        }

        [Test]
        public void RecordingEventsDoesNotChangeTheGame()
        {
            // a reader that drains the log every tick sees the same game as nobody reading it
            var cfg = ConfigTests.Load();
            var a = WorldTests.Full(cfg, 2);
            var b = WorldTests.Full(cfg, 2);
            int next = 0;
            var buf = new List<GameEvent>();
            for (int i = 0; i < 60 * cfg.sim.tickHz; i++)
            {
                a.Step(null); b.Step(null);
                buf.Clear(); b.Events.Since(next, buf); next = b.Events.Next;
                Assert.AreEqual(a.Hash(), b.Hash(), "tick " + i);
            }
            Assert.Greater(next, 0);
        }

        [Test]
        public void HikeThrowCatchAndWho()
        {
            var w = AtTheLine(tune: c => c.play.catchChanceOffense = 1f);
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            Assert.AreEqual(1, w.Events.ThisTick.Count);
            var hike = w.Events.ThisTick[0];
            Assert.AreEqual(EventKind.Hike, hike.Kind);
            Assert.AreEqual(Me, hike.Slot);
            Assert.AreEqual(w.Offense, hike.Team);
            Run(w, 20);
            Assert.AreEqual(0, w.Events.ThisTick.Count, "a quiet tick has no events");
            var target = LivePlay.Receiver(w, 1);
            w.Step(Press(new PlayerInput { Throw = 1 }));
            Assert.AreEqual(EventKind.Throw, w.Events.ThisTick[0].Kind);
            Assert.AreEqual(Me, w.Events.ThisTick[0].Slot);
            Assert.AreEqual(target.Slot, w.Events.ThisTick[0].Other);
            for (int i = 0; i < w.Cfg.sim.tickHz * 4 && w.Ball.State == BallState.Flying; i++) w.Step(null);
            var caught = w.Events.ThisTick.Find(e => e.Kind == EventKind.Catch);
            Assert.AreEqual(EventKind.Catch, caught.Kind, "the catch is this tick's event");
            Assert.AreEqual(target.Slot, caught.Slot);
            Assert.AreEqual(Me, caught.Other);
            Assert.AreEqual(target.X, caught.X, 1e-4f);
        }

        [Test]
        public void IncompleteThenTheWhistle()
        {
            var w = AtTheLine(tune: c => { c.play.catchChanceOffense = 0f; c.play.catchChanceDefense = 0f; });
            w.Step(Press(new PlayerInput { Hike = true }));
            w.Step(Press(new PlayerInput { Throw = 2, Lob = true }));
            for (int i = 0; i < w.Cfg.sim.tickHz * 6 && w.Phase == Phase.Live; i++) w.Step(null);
            Assert.AreEqual("INCOMPLETE", w.Result);
            var tick = w.Events.ThisTick;
            Assert.AreEqual(EventKind.Incomplete, tick[0].Kind);
            Assert.AreEqual(EventKind.Whistle, tick[1].Kind);
            Assert.AreEqual("INCOMPLETE", tick[1].Text);
            CollectionAssert.AreEqual(new[] { EventKind.Hike, EventKind.Throw, EventKind.Incomplete, EventKind.Whistle }, Kinds(w));
        }

        [Test]
        public void AJukeMakesATackleMiss()
        {
            var w = AtTheLine();
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            var me = w.Find(Me);
            w.Step(Press(new PlayerInput { Juke = true }));
            Assert.AreEqual(EventKind.Juke, w.Events.ThisTick[0].Kind);
            Assert.AreEqual(Me, w.Events.ThisTick[0].Slot);
            // a defender right on him while the juke lasts
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense) { d = p; break; }
            d.X = me.X; d.Z = me.Z; d.HomeX = d.X; d.HomeZ = d.Z; d.GoalX = d.X; d.GoalZ = d.Z;
            w.Step(null);
            var miss = w.Events.ThisTick.Find(e => e.Kind == EventKind.MissedTackle);
            Assert.AreEqual(EventKind.MissedTackle, miss.Kind, "juked: " + string.Join(", ", Kinds(w)));
            Assert.AreEqual(Me, miss.Other);
            Assert.AreEqual(Phase.Live, w.Phase);
        }

        [Test]
        public void ATackleIsFollowedByTheWhistle()
        {
            var w = AtTheLine(tune: c => { c.play.tackleBase = 1f; });
            ClearDefense(w);
            w.Step(Press(new PlayerInput { Hike = true }));
            var me = w.Find(Me);
            Player d = null;
            foreach (var p in w.Players) if (p.Team == w.Defense) { d = p; break; }
            d.X = me.X; d.Z = me.Z; d.HomeX = d.X; d.HomeZ = d.Z; d.GoalX = d.X; d.GoalZ = d.Z;
            w.Step(null);
            Assert.AreEqual(Phase.Whistle, w.Phase);
            var tick = w.Events.ThisTick;
            Assert.AreEqual(EventKind.Sack, tick[0].Kind, "the QB behind the line: a sack, not a tackle");
            Assert.AreEqual(d.Slot, tick[0].Slot);
            Assert.AreEqual(Me, tick[0].Other);
            Assert.AreEqual(EventKind.Whistle, tick[1].Kind);
            Assert.AreEqual("SACKED", tick[1].Text);
            Assert.AreEqual(Me, tick[1].Slot);
        }

        [Test]
        public void RunningOffTheFieldIsOutOfBounds()
        {
            var w = AtTheLine();
            ClearDefense(w);                                   // (to the +Z side; he runs for the -Z sideline)
            w.Step(Press(new PlayerInput { Hike = true }));
            var run = Press(new PlayerInput { MoveZ = -1f, Sprint = true });
            for (int i = 0; i < w.Cfg.sim.tickHz * 10 && w.Phase == Phase.Live; i++) w.Step(run);
            Assert.AreEqual("OUT OF BOUNDS", w.Result);
            var tick = w.Events.ThisTick;
            Assert.AreEqual(EventKind.OutOfBounds, tick[0].Kind);
            Assert.AreEqual(Me, tick[0].Slot);
            Assert.Less(tick[0].Z, -w.Cfg.field.HalfWidth, "past the sideline");
            Assert.AreEqual(EventKind.Whistle, tick[1].Kind);
            Assert.AreEqual("OUT OF BOUNDS", tick[1].Text);
        }

        [Test]
        public void ThisTickEmptiesOnTheNextStep()
        {
            var w = AtTheLine();
            w.Step(Press(new PlayerInput { Hike = true }));
            Assert.AreEqual(1, w.Events.ThisTick.Count);
            w.Step(null);
            Assert.AreEqual(0, w.Events.ThisTick.Count);
            Assert.AreEqual(1, w.Events.Total(EventKind.Hike), "the total remembers");
        }

        [Test]
        public void AReaderThatFallsBehindGetsWhatIsLeft()
        {
            var cfg = ConfigTests.Load();
            cfg.events.logKeep = 3;
            var w = new World(cfg, 1);
            for (int i = 0; i < 5; i++) w.Events.Push(w, EventKind.Hike, i, 0, 0f, 0f);
            var got = w.Events.Since(0, new List<GameEvent>());
            Assert.AreEqual(3, got.Count, "only logKeep are kept");
            Assert.AreEqual(2, got[0].Seq);
            Assert.AreEqual(5, w.Events.Next);
            Assert.AreEqual(1, w.Events.Since(4, new List<GameEvent>()).Count);
            Assert.AreEqual(0, w.Events.Since(5, new List<GameEvent>()).Count);
            Assert.AreEqual(5, w.Events.Total(EventKind.Hike));
            Assert.Greater(ConfigTests.Load().events.logKeep, 0, "game.json keeps a log");
        }
    }
}
