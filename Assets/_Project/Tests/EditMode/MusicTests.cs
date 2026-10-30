using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.music: the three tracks in game.json (files under Resources/Music) and the track each moment picks (View/Music.cs).
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
            Assert.Greater(cfg.music.fadeS, 0f);
            foreach (var id in new[] { "title", "huddle", "postgame" })
            {
                Assert.IsTrue(cfg.music.tracks.ContainsKey(id), id);
                StringAssert.StartsWith("Music/", cfg.music.tracks[id].file, id + ": a file under Resources/Music");
                Assert.Greater(cfg.music.tracks[id].level, 0f, id);
            }
            Assert.IsTrue(cfg.music.tracks["title"].loop);
            Assert.IsTrue(cfg.music.tracks["huddle"].loop);
            Assert.IsFalse(cfg.music.tracks["postgame"].loop, "the fanfare plays once");
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
