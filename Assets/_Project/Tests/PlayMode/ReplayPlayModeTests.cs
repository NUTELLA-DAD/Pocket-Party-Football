using System.Collections;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // WS-08 in the real game: a bot game reaches a whistle, the TV swaps to the replay's own figures for as long as
    // the config says while the game itself keeps going, then swaps back. No errors on the way.
    public sealed class ReplayPlayModeTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = System.Net.IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; Time.timeScale = 1f; }

        [UnityTest]
        public IEnumerator ReplayPlaysAtTheWhistleAndHandsBack()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var cfg = GameBootstrap.Instance.Config;
            Assert.IsTrue(cfg.Feature("replay"), "FEATURES.replay is on in game.json");
            var replay = host.GetComponent<Replay>();
            Assert.IsNotNull(replay, "PartyHost drives the replay");

            // the bots play: huddle, hike, a play, the whistle (sped up 4x until the replay starts)
            Time.timeScale = 4f;
            float giveUp = Time.realtimeSinceStartup + 120f;
            while (!replay.Showing && Time.realtimeSinceStartup < giveUp) yield return null;
            Time.timeScale = 1f;
            Assert.IsTrue(replay.Showing, "a replay within two minutes of bot football");

            float began = Time.realtimeSinceStartup;
            int tickAtStart = host.World.Tick;
            float length = replay.Tape.LengthS;
            yield return null;
            int anyLive = host.World.Players[0].Slot;
            Assert.IsFalse(host.Players.Figure(anyLive).gameObject.activeInHierarchy, "the live figures hide during the replay");
            int shownRoots = 0;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == "Players" && go.activeInHierarchy) shownRoots++;
            Assert.AreEqual(1, shownRoots, "one set of figures on the TV: the replay's");
            Assert.IsTrue(replay.Tape.ShowCard, "the result card is up");
            Assert.IsTrue(Replay.OnScreen, "the on-field marks (rings, triangles, badges) know a replay is on");
            var marks = GameObject.Find("Markings");
            if (marks != null)
                foreach (Transform c in marks.transform)
                    if (c.name == "Ring" || c.name == "FindMe") Assert.IsFalse(c.gameObject.activeSelf, "no player rings during the replay");

            while (replay.Showing && Time.realtimeSinceStartup < began + length + 5f) yield return null;
            float took = Time.realtimeSinceStartup - began;
            Assert.IsFalse(replay.Showing, "the replay ended");
            Assert.IsFalse(Replay.OnScreen, "the marks come back after the replay");
            Assert.AreEqual(length, took, 0.5f, "the replay lasts (seconds + afterS) / speed");
            Assert.AreEqual((cfg.replay.seconds + cfg.replay.afterS) / cfg.replay.speed, length, 0.01f);
            Assert.Greater(host.World.Tick, tickAtStart + (int)(length * cfg.sim.tickHz * 0.5f), "the game kept going under the replay");
            yield return null;
            Assert.IsTrue(host.Players.Figure(anyLive).gameObject.activeInHierarchy, "the live figures are back");
        }
    }
}
