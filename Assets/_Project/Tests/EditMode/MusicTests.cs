using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.music: the tracks are notes in game.json, made in code (View/Music.cs), and the moment picks the track.
    public sealed class MusicTests
    {
        [Test]
        public void TheSwitchAndTheThreeTracksLoad()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("music"));
            Assert.AreEqual("NO_MUSIC", ConfigLoader.EnvName("music"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_MUSIC", "1" } }).Feature("music"));
            Assert.Greater(cfg.music.master, 0f);
            Assert.LessOrEqual(cfg.music.master, 1f);
            Assert.Greater(cfg.music.sampleRate, 0);
            foreach (var id in new[] { "title", "huddle", "postgame" }) Assert.IsTrue(cfg.music.tracks.ContainsKey(id), id);
            Assert.IsTrue(cfg.music.tracks["title"].loop);
            Assert.IsTrue(cfg.music.tracks["huddle"].loop);
            Assert.IsFalse(cfg.music.tracks["postgame"].loop, "the fanfare plays once");
        }

        // Every note line fills its track exactly (no half-bar left over at the loop) and every pitch is a real note.
        [Test]
        public void EveryVoiceFillsItsTrackAndEveryPitchParses()
        {
            var cfg = ConfigTests.Load();
            foreach (var kv in cfg.music.tracks)
            {
                var t = kv.Value;
                Assert.Greater(t.bpm, 0f, kv.Key);
                Assert.Greater(t.beats, 0f, kv.Key);
                foreach (var v in t.voices)
                {
                    string where = kv.Key + " " + v.instrument;
                    Assert.Greater(v.level, 0f, where);
                    bool drum = v.instrument == "kick" || v.instrument == "snare" || v.instrument == "hat" || v.instrument == "crash";
                    if (drum)
                    {
                        Assert.AreEqual(0, v.notes.Length % 16, where + ": whole bars of 16ths");
                        continue;
                    }
                    CollectionAssert.Contains(new[] { "brass", "pluck", "pad", "bass" }, v.instrument, where);
                    float sum = 0f;
                    foreach (var tok in v.notes.Split(' '))
                    {
                        var parts = tok.Split(':');
                        Assert.AreEqual(2, parts.Length, where + ": " + tok);
                        if (parts[0] != "-") Assert.Greater(MusicSynth.Freq(parts[0]), 0f, where + ": " + tok);
                        sum += float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    }
                    Assert.AreEqual(0f, t.beats % sum, 1e-3f, where + ": " + sum + " beats repeat evenly into " + t.beats);
                }
            }
        }

        [Test]
        public void PitchesAndPatterns()
        {
            Assert.AreEqual(440f, MusicSynth.Freq("A4"), 0.01f);
            Assert.AreEqual(261.63f, MusicSynth.Freq("C4"), 0.01f);
            Assert.AreEqual(MusicSynth.Freq("C#4"), MusicSynth.Freq("Db4"), 0.01f);
            Assert.AreEqual(0f, MusicSynth.Freq("H4"));
            var n = MusicSynth.Notes("C4:1 -:1", 8f);
            Assert.AreEqual(4, n.Count, "the line repeats to fill 8 beats; rests make no note");
            Assert.AreEqual(6f, n[3].Beat);
            Assert.AreEqual(4, MusicSynth.Hits("x...", 4f).Count, "one hit a beat");
            var loud = MusicSynth.Hits("X.x.", 1f);
            Assert.AreEqual(1f, loud[0].Vel);
            Assert.Less(loud[1].Vel, 1f);
        }

        [Test]
        public void EveryTrackRendersTheSameEveryTime()
        {
            var cfg = ConfigTests.Load();
            foreach (var kv in cfg.music.tracks)
            {
                var a = MusicSynth.Render(kv.Value, cfg.music.sampleRate);
                var b = MusicSynth.Render(kv.Value, cfg.music.sampleRate);
                Assert.AreEqual((int)(MusicSynth.Seconds(kv.Value) * cfg.music.sampleRate), a.Length, kv.Key);
                CollectionAssert.AreEqual(a, b, kv.Key + ": the same samples every time");
                float peak = 0f;
                foreach (float v in a) peak = System.Math.Max(peak, System.Math.Abs(v));
                Assert.AreEqual(0.9f, peak, 1e-3f, kv.Key + ": normalized");
            }
        }

        [Test]
        public void TheMomentPicksTheTrack()
        {
            var cfg = ConfigTests.Load();
            cfg.features["clock"] = true;
            var w = WorldTests.Full(cfg, 1);
            w.Step(null);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            Assert.AreEqual("title", MusicDirector.Pick(cfg, w, true, false, true), "the title screen");
            Assert.AreEqual("title", MusicDirector.Pick(cfg, w, false, true, true), "the lobby waiting");
            Assert.AreEqual("huddle", MusicDirector.Pick(cfg, w, false, false, true), "a huddle");
            Assert.IsNull(MusicDirector.Pick(cfg, w, true, false, false), "MUSIC OFF in SETTINGS");
            for (int i = 0; i < 30 * 60 && w.Phase != Phase.Live; i++) w.Step(null);
            Assert.AreEqual(Phase.Live, w.Phase);
            Assert.IsNull(MusicDirector.Pick(cfg, w, false, false, true), "a live play: no music");
            w.GameClock.Over = true;
            Assert.AreEqual("postgame", MusicDirector.Pick(cfg, w, false, false, true), "the game is over");
            cfg.features["music"] = false;
            Assert.IsNull(MusicDirector.Pick(cfg, w, true, false, true), "NO_MUSIC=1");
        }
    }
}
