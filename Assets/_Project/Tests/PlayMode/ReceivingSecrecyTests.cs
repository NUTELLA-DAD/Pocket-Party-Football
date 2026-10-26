using System.Collections;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // WS-05's gate: CALL FOR IT is team-private. A Chiefs receiver taps it; the Chiefs QB's phone gets the "call"
    // message, the Roughriders phone never does (not as a message, not inside the public status), and neither does
    // the receiver's own phone (only the QB needs it).
    public sealed class ReceivingSecrecyTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; }

        [UnityTest]
        public IEnumerator OnlyTheQbPhoneHearsTheCall()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            w.Cfg.features["receiving"] = true;            // (ships off; on for this test)
            int port = host.Server.Port, off = w.Offense, def = w.Defense;

            var qbPhone = new FakePhone();
            var rbPhone = new FakePhone();
            var riders = new FakePhone();
            yield return qbPhone.JoinAndPick(port, host.RoomCode, "Kay", off);   // the first person on offense is the QB
            yield return rbPhone.JoinAndPick(port, host.RoomCode, "Bo", off);    // the next one takes spot 1, the RB
            yield return riders.JoinAndPick(port, host.RoomCode, "Ray", def);

            var qb = LivePlay.Qb(w);
            var rb = LivePlay.SpotOf(w, off, 1);
            Assert.IsTrue(qb.Human && rb.Human, "both Chiefs people are on the field");
            Assert.Greater(Receiving.ShapeOf(w, rb), 0, "the RB is a receiver");

            // (drop what came before: the next "ui" the RB phone sees must be the one for the live play)
            yield return qbPhone.Settle();
            yield return rbPhone.Settle();

            // both sides call, then the snap
            Huddle.Begin(w);
            Assert.IsTrue(Huddle.Lock(w, Huddle.Caller(w, off).Slot, "slants"));
            Assert.IsTrue(Huddle.Lock(w, Huddle.Caller(w, def).Slot, "cover3"));
            Assert.AreEqual(Phase.Set, w.Phase);
            LivePlay.Hike(w);
            yield return rbPhone.Expect("ui", _ => { }, m => (string)m["layout"] == "route");

            // the RB taps CALL FOR IT on his route buttons
            rbPhone.Send(new JObject { ["t"] = "btn", ["b"] = "call", ["d"] = 1, ["aid"] = rbPhone.Aid });
            rbPhone.Send(new JObject { ["t"] = "btn", ["b"] = "call", ["d"] = 0, ["aid"] = rbPhone.Aid });
            JObject call = null;
            yield return qbPhone.Expect("call", m => call = m);
            Assert.AreEqual(Receiving.ShapeOf(w, rb), (int)call["k"]);
            Assert.AreEqual(w.Cfg.play.shapes[(int)call["k"] - 1], (string)call["shape"]);
            Assert.AreEqual(rb.Number, (int)call["number"]);
            Assert.AreEqual(1, w.Recv.Calls);

            yield return riders.Settle(30);
            yield return rbPhone.Settle(30);
            Assert.Greater(riders.All.Count, 3, "the defense phone did get its own messages");
            foreach (var text in riders.All)
            {
                var m = JObject.Parse(text);
                Assert.AreNotEqual("call", (string)m["t"], "the call reached the other team");
                StringAssert.DoesNotContain("calling", text);
            }
            Assert.IsFalse(rbPhone.All.Any(t => (string)JObject.Parse(t)["t"] == "call"), "only the QB's phone is told");

            w.Cfg.features["receiving"] = false;
            qbPhone.Dispose();
            rbPhone.Dispose();
            riders.Dispose();
        }
    }
}
