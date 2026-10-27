using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // WS-08: the instant replay (View/Replay.cs) and the result card (View/ResultCard.cs). The replay starts after
    // the whistle, lasts as long as the config says, shows what really happened, and never changes the game: the
    // hashes are the same with the replay on, off, or not there at all.
    public sealed class ReplayTests
    {
        const float Tv = 1f / 60f;   // a 60 fps TV

        static int Ticks(GameConfig cfg, float s) => (int)System.Math.Round(s * cfg.sim.tickHz);

        static GameConfig Off() => ConfigTests.Load(new Dictionary<string, string> { { "NO_REPLAY", "1" } });

        // The TV loop with the replay: two frames a tick, the tape watching, the replay drawn into its own World.
        sealed class Tv60
        {
            public readonly World W;
            public readonly ReplayTape Tape;
            readonly World ghost;
            public Tv60(GameConfig cfg, int seed) { W = WorldTests.Full(cfg, seed); Tape = new ReplayTape(cfg); ghost = new World(cfg, 0); }

            public void Tick()
            {
                W.Step(null);
                for (int f = 0; f < 2; f++)
                {
                    Tape.Observe(W);
                    Tape.Advance(Tv);
                    if (Tape.Playing && Tape.Segment(Tape.PlayTick, out var a, out var b, out _)) { ReplayTape.Copy(a, ghost); ReplayTape.Copy(b, ghost); }
                }
            }
        }

        [Test]
        public void ConfigBlockAndSwitch()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("replay"), "FEATURES.replay is on in game.json");
            Assert.IsFalse(Off().Feature("replay"), "NO_REPLAY=1 turns it off");
            Assert.AreEqual(2f, cfg.replay.seconds, "the last 2 seconds of the play");
            Assert.Greater(cfg.replay.speed, 0f);
            Assert.Less(cfg.replay.speed, 1f, "slow motion");
            Assert.Greater(cfg.replay.afterS, 0f);
            Assert.Greater(cfg.replay.cardS, 0f);
            Assert.Greater(cfg.replay.cardWidthPx, 0f);
        }

        // The sim is untouched: a game with the replay watching (and playing back) hashes the same, tick by tick, as
        // one with no replay at all, and as one with NO_REPLAY=1.
        [Test]
        public void HashesAreEqualWithReplayOnAndOff()
        {
            var on = ConfigTests.Load();
            foreach (int seed in new[] { 1, 2 })
            {
                var with = new Tv60(on, seed);
                var without = WorldTests.Full(on, seed);
                var off = new Tv60(Off(), seed);
                int ticks = 3 * 60 * on.sim.tickHz;
                for (int t = 0; t < ticks; t++)
                {
                    with.Tick(); without.Step(null); off.Tick();
                    ulong h = without.Hash();
                    Assert.AreEqual(h, with.W.Hash(), "replay on changed the game at tick " + with.W.Tick + ", seed " + seed);
                    Assert.AreEqual(h, off.W.Hash(), "replay off changed the game at tick " + off.W.Tick + ", seed " + seed);
                }
                Assert.Greater(with.Tape.Replays, 1, "replays really played during the game, seed " + seed);
                Assert.AreEqual(0, off.Tape.Replays, "NO_REPLAY=1: no replays, seed " + seed);
                Assert.IsNull(off.Tape.Card, "NO_REPLAY=1: no result card, seed " + seed);
                TestContext.WriteLine("seed " + seed + ": " + with.Tape.Replays + " replays in 3 minutes, " + without.PlaysRun + " plays");
            }
        }

        // The replay starts afterS after the whistle, covers the last `seconds` of the play plus the afterS, and lasts
        // that long divided by the speed on the TV.
        [Test]
        public void ReplayRunsForTheConfiguredTime()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg, 1);
            var tape = new ReplayTape(cfg);
            int whistleTick = -1, startTick = -1, frames = 0;
            int nextSeq = 0;
            var events = new List<GameEvent>();
            for (int t = 0; t < 2 * 60 * cfg.sim.tickHz && startTick < 0; t++)
            {
                w.Step(null);
                events.Clear();
                w.Events.Since(nextSeq, events);
                nextSeq = w.Events.Next;
                foreach (var e in events) if (e.Kind == EventKind.Whistle && whistleTick < 0) whistleTick = e.Tick;
                tape.Observe(w);
                if (tape.Playing) startTick = w.Tick;
            }
            Assert.GreaterOrEqual(whistleTick, 0, "a whistle in the first two minutes");
            Assert.AreEqual(whistleTick + Ticks(cfg, cfg.replay.afterS), startTick, "the replay starts afterS after the whistle");
            Assert.AreEqual(Ticks(cfg, cfg.replay.seconds + cfg.replay.afterS), tape.ToTick - tape.FromTick, "the last `seconds` of the play plus afterS");
            Assert.AreEqual(whistleTick + Ticks(cfg, cfg.replay.afterS), tape.ToTick);
            float expect = (cfg.replay.seconds + cfg.replay.afterS) / cfg.replay.speed;
            Assert.AreEqual(expect, tape.LengthS, 0.001f);
            Assert.IsTrue(tape.ShowCard, "the result card shows with the replay");
            // play it on a 60 fps TV while the game goes on
            while (tape.Advance(Tv)) { frames++; if (frames % 2 == 0) { w.Step(null); tape.Observe(w); } Assert.Less(frames, 10000); }
            Assert.AreEqual(expect, frames * Tv, Tv * 1.5f, "the replay lasts (seconds + afterS) / speed");
            Assert.IsFalse(tape.Playing);
            Assert.IsTrue(tape.ShowCard, "the card stays up after the replay");
            int cardFrames = 0;
            while (tape.ShowCard) { tape.Advance(Tv); cardFrames++; Assert.Less(cardFrames, 10000); }
            Assert.AreEqual(cfg.replay.cardS, cardFrames * Tv, Tv * 1.5f, "for cardS");
        }

        // What the replay shows is what happened: each tick it plays is the World as it was on that tick, in slow motion.
        [Test]
        public void ReplayShowsWhatHappened()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg, 2);
            var tape = new ReplayTape(cfg);
            var ball = new SortedDictionary<int, (float x, float y, float z)>();
            var people = new SortedDictionary<int, string>();
            string Where(World x) { var sb = new System.Text.StringBuilder(); foreach (var p in x.Players) sb.Append(p.Slot).Append(':').Append(p.X).Append(',').Append(p.Z).Append(' '); return sb.ToString(); }
            for (int t = 0; t < 2 * 60 * cfg.sim.tickHz && !tape.Playing; t++)
            {
                w.Step(null);
                ball[w.Tick] = (w.Ball.X, w.Ball.Y, w.Ball.Z);
                people[w.Tick] = Where(w);
                tape.Observe(w);
            }
            Assert.IsTrue(tape.Playing, "a replay in the first two minutes");
            var ghost = new World(cfg, 0);
            int shown = 0, lastTick = -1;
            float lastPlayTick = tape.PlayTick;
            while (tape.Advance(Tv))
            {
                Assert.GreaterOrEqual(tape.PlayTick, lastPlayTick, "the replay only goes forward");
                Assert.LessOrEqual(tape.PlayTick - lastPlayTick, cfg.replay.speed * cfg.sim.tickHz * Tv + 0.001f, "slow motion");
                lastPlayTick = tape.PlayTick;
                Assert.IsTrue(tape.Segment(tape.PlayTick, out var a, out var b, out float alpha));
                Assert.That(alpha, Is.InRange(0f, 1f));
                Assert.That(a.Tick, Is.InRange(tape.FromTick, tape.ToTick));
                ReplayTape.Copy(a, ghost);
                Assert.AreEqual(people[a.Tick], Where(ghost), "everyone where they were on tick " + a.Tick);
                Assert.AreEqual(ball[a.Tick], (ghost.Ball.X, ghost.Ball.Y, ghost.Ball.Z), "the ball where it was on tick " + a.Tick);
                if (a.Tick != lastTick) { shown++; lastTick = a.Tick; }
            }
            Assert.GreaterOrEqual(shown, tape.ToTick - tape.FromTick, "every tick of the replay was shown");
            // the replay ends after the whistle: the last tick shown is a whistle
            Assert.IsTrue(tape.Segment(tape.ToTick, out var end, out _, out _));
            Assert.AreEqual(Phase.Whistle, end.Phase);
        }

        // The card after a tackle: the yards the sim counted, the tackler from the event list, the next down as the
        // scoreboard says it.
        [Test]
        public void ResultCardAfterATackle()
        {
            var cfg = ConfigTests.Load();
            int checkedCards = 0;
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var w = WorldTests.Full(cfg, seed);
                var tape = new ReplayTape(cfg);
                int nextSeq = 0, yardsBefore = 0;
                var events = new List<GameEvent>();
                for (int t = 0; t < 5 * 60 * cfg.sim.tickHz; t++)
                {
                    if (w.Phase == Phase.Live) yardsBefore = w.YardsGained;
                    w.Step(null);
                    tape.Observe(w);
                    events.Clear();
                    w.Events.Since(nextSeq, events);
                    nextSeq = w.Events.Next;
                    GameEvent? tackle = null;
                    foreach (var e in events) if (e.Kind == EventKind.Tackle) tackle = e;
                    bool whistled = false;
                    foreach (var e in events) if (e.Kind == EventKind.Whistle && e.Text == "TACKLED") whistled = true;
                    if (!whistled || !tackle.HasValue) continue;
                    var c = tape.Card;
                    Assert.IsNotNull(c);
                    Assert.AreEqual("TACKLED", c.Result);
                    Assert.AreEqual("TACKLED", c.Headline);
                    Assert.AreEqual(w.YardsGained - yardsBefore, c.Yards, "the yards the sim counted, seed " + seed);
                    Assert.IsTrue(c.ShowYards);
                    Assert.AreEqual(ResultCard.TackleVerb(tackle.Value.Text), c.ByVerb);   // (FEATURES.hitAngle: "Big hit by", ...)
                    Assert.AreEqual(tackle.Value.Slot, c.BySlot, "the tackler from the event list");
                    var tackler = w.Find(c.BySlot);
                    Assert.AreEqual(tackler.Number, c.ByNumber);
                    Assert.AreEqual(tackler.Team, c.ByTeam);
                    StringAssert.EndsWith(Downs.DownText(w), c.NextText, "the next down as the scoreboard says it");
                    if (w.Note.Length > 0) StringAssert.StartsWith(w.Note, c.NextText);
                    checkedCards++;
                }
            }
            TestContext.WriteLine(checkedCards + " tackle cards checked");
            Assert.Greater(checkedCards, 0, "bot games have tackles");
        }

        [Test]
        public void CardWords()
        {
            Assert.AreEqual("+7 YARDS", ResultCard.YardsText(7));
            Assert.AreEqual("+1 YARD", ResultCard.YardsText(1));
            Assert.AreEqual("NO GAIN", ResultCard.YardsText(0));
            Assert.AreEqual("LOSS OF 3", ResultCard.YardsText(-3));
            Assert.AreEqual("SACKED!", ResultCard.Headline("SACKED"));
            Assert.AreEqual("TOUCHDOWN!", ResultCard.Headline("TOUCHDOWN"));
            Assert.AreEqual("PICK SIX!", ResultCard.Headline("PICK SIX"));
            var c = new ResultCard.Info { ByVerb = "Tackle by", BySlot = 3, ByNumber = 54, ByPos = "LB", ByTeam = 1 };
            Assert.AreEqual("Tackle by #54 LB", ResultCard.ByLine(c, null), "a bot: his number");
            Assert.AreEqual("Tackle by Kenny (#54 LB)", ResultCard.ByLine(c, s => s == 3 ? "Kenny" : null), "a person: the name");
            Assert.AreEqual("", ResultCard.ByLine(new ResultCard.Info(), null), "nobody to name");
        }
    }
}
