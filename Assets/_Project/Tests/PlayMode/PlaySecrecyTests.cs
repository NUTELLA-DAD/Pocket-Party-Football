using System.Collections;
using System.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // M1-b's gate: a team's plays and calls go ONLY to that team's phones. A Chiefs phone (offense, the QB) and a
    // Roughriders phone (defense, the middle LB) both call a play; neither phone, and not the TV line, ever carries
    // the other team's play ids or names.
    public sealed class PlaySecrecyTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; }

        [UnityTest]
        public IEnumerator TheOtherTeamNeverGetsThePlay()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            int port = host.Server.Port;
            int off = w.Offense, def = w.Defense;

            var chiefs = new FakePhone();
            var riders = new FakePhone();
            yield return chiefs.JoinAndPick(port, host.RoomCode, "Kay", off);
            yield return riders.JoinAndPick(port, host.RoomCode, "Ray", def);

            // a fresh huddle with both people in it
            Huddle.Begin(w);
            yield return chiefs.Settle();
            yield return riders.Settle();
            var huddleC = chiefs.Latest["huddle"];
            var huddleR = riders.Latest["huddle"];
            Assert.AreEqual("huddle", (string)chiefs.Latest["ui"]["layout"]);
            Assert.AreEqual("offense", (string)huddleC["side"]);
            Assert.AreEqual("defense", (string)huddleR["side"]);
            Assert.IsTrue((bool)huddleC["caller"], "the Chiefs person is the QB, so the caller");
            Assert.IsTrue((bool)huddleR["caller"], "the Roughriders person is the middle LB, so the caller");

            // each side calls; the call carries the huddle screen's aid
            chiefs.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = chiefs.Aid });
            JObject status = null;
            yield return riders.Expect("status", m => status = m, m => (bool)m["called"][off]);
            Assert.AreEqual("huddle", (string)status["phase"]);
            Assert.IsTrue((bool)status["called"][off], "the defense sees that the offense called...");
            StringAssert.Contains("CALLED", PartyHost.TvLine(w, GameBootstrap.Instance.Config));
            riders.Send(new JObject { ["t"] = "lock", ["id"] = "mblitz", ["aid"] = riders.Aid });

            JObject jobC = null, jobR = null;
            yield return chiefs.Expect("job", m => jobC = m);
            yield return riders.Expect("job", m => jobR = m);
            Assert.AreEqual("slants", (string)jobC["play"]["id"]);
            Assert.AreEqual("mblitz", (string)jobR["play"]["id"]);
            Assert.AreEqual(Phase.Set, w.Phase);
            for (int i = 0; i < 10; i++) yield return null;

            // ...but never what: no offense play id or name ever reached the defense, and the other way round
            var cfg = GameBootstrap.Instance.Config;
            foreach (var p in cfg.plays)
            {
                var leaksTo = p.side == "offense" ? riders : chiefs;
                foreach (var text in leaksTo.All)
                {
                    StringAssert.DoesNotContain("\"" + p.id + "\"", text, "the " + p.side + " play id '" + p.id + "' reached the other team");
                    StringAssert.DoesNotContain(p.name, text, "the " + p.side + " play name '" + p.name + "' reached the other team");
                }
            }
            Assert.Greater(riders.All.Count, 5, "the defense phone did get its own messages");

            // and the TV never shows a play
            Huddle.Begin(w);
            Assert.IsTrue(Huddle.Lock(w, Huddle.Caller(w, off).Slot, "slants"));
            string tv = PartyHost.TvLine(w, cfg);
            foreach (var p in cfg.plays) { StringAssert.DoesNotContain(p.name, tv); StringAssert.DoesNotContain(p.id, tv); }

            chiefs.Dispose();
            riders.Dispose();
        }

        [UnityTest]
        public IEnumerator AStalePressIsIgnored()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            var chiefs = new FakePhone();
            yield return chiefs.JoinAndPick(host.Server.Port, host.RoomCode, "Kay", w.Offense);
            Huddle.Begin(w);
            yield return chiefs.Settle();
            chiefs.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = "walk1" });
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(Huddle.IsLocked(w, w.Offense), "a lock from an old screen does nothing");
            chiefs.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = chiefs.Aid });
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(Huddle.IsLocked(w, w.Offense));
            chiefs.Dispose();
        }
    }
}
