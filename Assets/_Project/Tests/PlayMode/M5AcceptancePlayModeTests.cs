using System.Collections;
using System.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // M5 "Party polish" (docs/PLAN.md), the end of a game in the real Boot scene: when the last quarter runs out the
    // TV shows the post-game screen and every phone, on both teams, gets the final card with the score, the winner
    // and its own line. The other M5 items have their own tests; docs/progress/m5-acceptance.md lists them all.
    public sealed class M5AcceptancePlayModeTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; Time.timeScale = 1f; }

        [UnityTest]
        public IEnumerator TheGameEndsWithThePostGameOnTheTvAndEveryPhone()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            Assert.IsTrue(Clock.On(w) && Stats.On(w.Cfg), "the clock and the stats are on in game.json");
            int port = host.Server.Port;

            var kay = new FakePhone();
            var ray = new FakePhone();
            try
            {
                yield return kay.JoinAndPick(port, host.RoomCode, "Kay", 0);
                yield return ray.JoinAndPick(port, host.RoomCode, "Ray", 1);

                // the last seconds of the 4th quarter
                var c = w.GameClock;
                Assert.IsTrue(c.Started, "the clock has started");
                c.Quarter = w.Cfg.clock.quarters; c.SecondHalf = true; c.BreakTicks = 0;
                c.TicksLeft = 3 * w.Cfg.sim.tickHz;
                var early = host.GetComponent<PostGame>();
                Assert.IsFalse(early != null && early.Showing, "no post-game before the end");

                Time.timeScale = 8f;
                float giveUp = Time.realtimeSinceStartup + 120f;
                while (!Clock.IsOver(w) && Time.realtimeSinceStartup < giveUp) yield return null;
                Time.timeScale = 1f;
                Assert.IsTrue(Clock.IsOver(w), "the game ended");

                // the TV: the post-game screen comes up stats.showAfterS after the final whistle (TV seconds)
                var pg = host.GetComponent<PostGame>();
                Assert.IsNotNull(pg, "the post-game screen is on the TV");
                float until = Time.realtimeSinceStartup + w.Cfg.stats.showAfterS + 5f;
                while (!pg.Showing && Time.realtimeSinceStartup < until) yield return null;
                Assert.IsTrue(pg.Showing, "...and showing within showAfterS (" + w.Cfg.stats.showAfterS + " s) of the final whistle");

                // every phone, both teams
                foreach (var (phone, team) in new[] { (kay, 0), (ray, 1) })
                {
                    JObject card = null;
                    yield return phone.Expect("postgame", m => card = m);
                    Assert.IsNotNull(card, "team " + team + "'s phone got the final card");
                    var teams = (JArray)card["teams"];
                    Assert.AreEqual(2, teams.Count);
                    Assert.AreEqual(w.Score[0], (int)teams[0]["score"]);
                    Assert.AreEqual(w.Score[1], (int)teams[1]["score"]);
                    Assert.AreEqual(Clock.Winner(w), (int)card["winner"], "the winner (-1: a tie)");
                    Assert.IsNotNull(card["mine"], "its own line");
                    Assert.AreEqual(team, (int)card["mine"]["team"]);
                    StringAssert.DoesNotContain("\"play\"", card.ToString(), "nothing secret on the card");
                }
            }
            finally
            {
                kay.Dispose();
                ray.Dispose();
                Time.timeScale = 1f;
            }
        }
    }
}
