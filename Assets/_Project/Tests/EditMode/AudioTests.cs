using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // WS-09: sound on the TV (View/Audio.cs). Every event kind has a sound, every sound is made in code and is
    // audible, a bot game asks for every sound, nothing plays with FEATURES.audio off, and listening never changes
    // the game.
    public sealed class AudioTests
    {
        static GameConfig On()
        {
            var cfg = ConfigTests.Load();
            cfg.features["audio"] = true;
            return cfg;
        }

        [Test]
        public void ConfigBlockAndSwitch()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.features.ContainsKey("audio"), "FEATURES.audio is in game.json");
            Assert.IsTrue(cfg.Feature("audio"), "FEATURES.audio is on in game.json (the merge captain turned it on)");
            var on = On();
            ConfigLoader.ApplyEnvOverrides(on, k => k == "NO_AUDIO" ? "1" : null);
            Assert.IsFalse(on.Feature("audio"), "NO_AUDIO=1 turns it off");
            Assert.Greater(cfg.audio.master, 0f, "one master volume");
            Assert.LessOrEqual(cfg.audio.master, 1f);
            Assert.Greater(cfg.audio.sampleRate, 0);
            Assert.Greater(cfg.audio.huddleTickFromS, 0f);
            foreach (var s in AudioCues.All)
            {
                Assert.IsTrue(cfg.audio.levels.ContainsKey(AudioCues.Key(s)), "audio.levels." + AudioCues.Key(s) + " is in game.json");
                Assert.Greater(cfg.audio.Level(AudioCues.Key(s)), 0f, AudioCues.Key(s));
            }
            Assert.AreEqual(AudioCues.All.Length, cfg.audio.levels.Count, "no level for a sound that does not exist");
        }

        [Test]
        public void EveryEventKindHasASound()
        {
            foreach (EventKind k in System.Enum.GetValues(typeof(EventKind)))
                Assert.Greater(AudioCues.For(k).Length, 0, k + " has no sound");
            Assert.Contains(Sound.Whistle, AudioCues.For(EventKind.Whistle));
            Assert.Contains(Sound.Hit, AudioCues.For(EventKind.Tackle));
            Assert.Contains(Sound.BigHit, AudioCues.For(EventKind.Sack));
            Assert.Contains(Sound.Sting, AudioCues.For(EventKind.Touchdown));
            Assert.Contains(Sound.Crowd, AudioCues.For(EventKind.Touchdown));
        }

        // Each sound is made from code, has some length, is loud enough to hear, never clips, and is the same every time.
        [Test]
        public void EverySoundIsMadeInCode()
        {
            int rate = ConfigTests.Load().audio.sampleRate;
            foreach (var s in AudioCues.All)
            {
                var a = AudioSynth.Make(s, rate);
                Assert.Greater(a.Length, rate / 50, s + " is too short");
                Assert.Less(a.Length, rate * 3, s + " is too long");
                double sum = 0; float peak = 0f;
                foreach (float v in a)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), s + " has a bad sample");
                    peak = System.Math.Max(peak, System.Math.Abs(v));
                    sum += v * v;
                }
                Assert.AreEqual(0.9f, peak, 0.001f, s + " is normalised to 0.9");
                Assert.Greater(System.Math.Sqrt(sum / a.Length), 0.02, s + " is too quiet");
                CollectionAssert.AreEqual(a, AudioSynth.Make(s, rate), s + " is the same every time");
            }
        }

        [Test]
        public void GainIsMasterTimesLevel()
        {
            var cfg = On();
            var d = new AudioDirector(cfg);
            Assert.AreEqual(cfg.audio.master * cfg.audio.levels["whistle"], d.Gain(Sound.Whistle), 1e-5f);
            cfg.audio.master = 0f;
            foreach (var s in AudioCues.All) Assert.AreEqual(0f, d.Gain(s), s + " with master 0");
            cfg.audio.master = 3f;
            foreach (var s in AudioCues.All) Assert.LessOrEqual(d.Gain(s), 1f, s + " never above full volume");
        }

        // Three 10-minute bot games (like EventsTests) ask for every sound; the huddle ticks its last seconds.
        [Test]
        public void BotGamesPlayEverySound()
        {
            var cfg = On();
            var total = new int[AudioCues.All.Length];
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var w = WorldTests.Full(cfg, seed);
                var d = new AudioDirector(cfg);
                int whistles = 0;
                for (int i = 0; i < 10 * 60 * cfg.sim.tickHz; i++)
                {
                    w.Step(null);
                    foreach (var s in d.Observe(w)) if (s == Sound.Whistle) whistles++;
                }
                Assert.AreEqual(w.Events.Total(EventKind.Whistle), whistles, "a whistle for every whistle, seed " + seed);
                // every bot huddle runs its full time, so it ticks huddleTickFromS times (the last one may be cut off)
                int full = (int)cfg.audio.huddleTickFromS;
                Assert.GreaterOrEqual(d.Played(Sound.Tick), full * (w.HuddleNumber - 1), "huddle ticks, seed " + seed);
                Assert.LessOrEqual(d.Played(Sound.Tick), full * w.HuddleNumber, "huddle ticks, seed " + seed);
                foreach (var s in AudioCues.All) total[(int)s] += d.Played(s);
            }
            var sb = new StringBuilder();
            foreach (var s in AudioCues.All) sb.Append(s).Append(' ').Append(total[(int)s]).Append(", ");
            TestContext.WriteLine("three 10-minute bot games: " + sb);
            foreach (var s in AudioCues.All) Assert.Greater(total[(int)s], 0, s + " never played: " + sb);
        }

        // A tackle is a big hit when the tackler comes in fast (carrier.bigHitSpeedYdS, as for fumbles) or the carrier
        // goes down at or behind the line; otherwise it is a plain hit.
        [Test]
        public void BigTackles()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg, 1);
            w.Step(null);
            var d = new AudioDirector(cfg);
            Player tackler = null, carrier = null;
            foreach (var p in w.Players)
            {
                if (p.Team == w.Offense && carrier == null) carrier = p;
                if (p.Team != w.Offense && tackler == null) tackler = p;
            }
            var e = new GameEvent { Kind = EventKind.Tackle, Slot = tackler.Slot, Team = tackler.Team, Other = carrier.Slot };
            tackler.VX = 0f; tackler.VZ = 0f;
            carrier.X = w.LineOfScrimmageX + w.Dir * 5f; e.X = carrier.X;
            Assert.IsFalse(d.BigTackle(w, e), "slow tackle after a gain: a plain hit");
            tackler.VX = cfg.carrier.bigHitSpeedYdS + 0.5f;
            Assert.IsTrue(d.BigTackle(w, e), "a fast tackler: a big hit");
            tackler.VX = 0f;
            carrier.X = w.LineOfScrimmageX - w.Dir * 2f; e.X = carrier.X;
            Assert.IsTrue(d.BigTackle(w, e), "stopped behind the line: a big hit");
        }

        // The tick only comes in the huddle's last seconds, once a second.
        [Test]
        public void HuddleTicksOnlyAtTheEnd()
        {
            var cfg = On();
            var w = WorldTests.Full(cfg, 1);
            var d = new AudioDirector(cfg);
            var at = new List<int>();
            for (int i = 0; i < (cfg.huddle.seconds + 1) * cfg.sim.tickHz; i++)
            {
                w.Step(null);
                if (d.Observe(w).Contains(Sound.Tick)) at.Add(w.PhaseTicks);
            }
            Assert.AreEqual((int)cfg.audio.huddleTickFromS, at.Count, "one tick a second for the last seconds: " + string.Join(",", at));
            foreach (int left in at) Assert.LessOrEqual(left, cfg.audio.huddleTickFromS * cfg.sim.tickHz);
            for (int k = 1; k < at.Count; k++) Assert.AreEqual(cfg.sim.tickHz, at[k - 1] - at[k], "a second apart");
        }

        // With the switch off (the default) a whole bot game asks for nothing; turned on mid-game, old events stay quiet.
        [Test]
        public void NothingPlaysWithTheSwitchOff()
        {
            var cfg = ConfigTests.Load();
            cfg.features["audio"] = false;
            Assert.IsFalse(AudioDirector.On(cfg));
            var w = WorldTests.Full(cfg, 2);
            var d = new AudioDirector(cfg);
            for (int i = 0; i < 3 * 60 * cfg.sim.tickHz; i++)
            {
                w.Step(null);
                Assert.AreEqual(0, d.Observe(w).Count, "tick " + i);
            }
            Assert.Greater(w.Events.Next, 0, "the game did make events");
            foreach (var s in AudioCues.All) Assert.AreEqual(0, d.Played(s), s.ToString());

            cfg.features["audio"] = true;
            Assert.AreEqual(0, d.Observe(w).Count, "switched on: no backlog of old events");
        }

        // Listening only reads the World: a game with sound hashes the same, tick by tick, as one without.
        [Test]
        public void ListeningDoesNotChangeTheGame()
        {
            var cfg = On();
            var a = WorldTests.Full(cfg, 3);
            var b = WorldTests.Full(cfg, 3);
            var d = new AudioDirector(cfg);
            for (int i = 0; i < 90 * cfg.sim.tickHz; i++)
            {
                a.Step(null); b.Step(null);
                d.Observe(b);
                Assert.AreEqual(a.Hash(), b.Hash(), "tick " + i);
            }
        }
    }
}
