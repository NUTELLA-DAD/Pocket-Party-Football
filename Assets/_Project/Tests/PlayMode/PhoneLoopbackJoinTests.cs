using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PocketFootball.Boot;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // M0-c: a fake phone joins the real game over the game's own server on 127.0.0.1, picks a team, walks its
    // capsule, "locks the screen" (drops the connection) and rejoins with its token as the same player.
    public sealed class PhoneLoopbackJoinTests
    {
        [SetUp]
        public void Loopback()
        {
            PartyHost.BindOverride = IPAddress.Loopback;
            PartyHost.PortOverride = 0;
        }

        [TearDown]
        public void Reset()
        {
            PartyHost.BindOverride = null;
            PartyHost.PortOverride = -1;
        }

        [UnityTest]
        public IEnumerator PhoneJoinsPicksWalksAndRejoins()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            Assert.IsNotNull(host, "no PartyHost");
            Assert.IsNull(host.ServerError, host.ServerError);
            int port = host.Server.Port;

            // join
            var phone = new FakePhone();
            yield return FakePhone.Wait(phone.Connect(port));
            phone.Send(new JObject { ["t"] = "join", ["code"] = host.RoomCode.ToLowerInvariant(), ["name"] = "Tess", ["token"] = "", ["pid"] = "p1" });
            JObject joined = null, ui = null, picked = null;
            yield return phone.Expect("joined", m => joined = m);
            yield return phone.Expect("ui", m => ui = m);
            Assert.AreEqual("pick", (string)ui["layout"]);
            Assert.AreEqual(-1, (int)joined["team"]);
            Assert.AreEqual(2, ((JArray)joined["teams"]).Count);
            string token = (string)joined["token"];
            int slot = (int)joined["slot"];

            // pick the Roughriders
            phone.Send(new JObject { ["t"] = "pick", ["team"] = 1 });
            yield return phone.Expect("picked", m => picked = m);
            yield return phone.Expect("ui", m => ui = m);
            Assert.AreEqual(1, (int)picked["team"]);
            Assert.That((string)ui["layout"], Is.EqualTo("huddle").Or.EqualTo("walk"), "on the field: the huddle or the line");
            var player = host.World.Find(slot);
            Assert.IsNotNull(player, "no capsule for the phone");
            Assert.IsTrue(player.Human);
            Assert.AreEqual(1, player.Team);
            Assert.AreEqual(22, host.World.Players.Count, "the person took a bot's spot: still 22");
            Assert.AreEqual(11, host.World.TeamCount(1));

            // walk right (screen right = +X along the field) for about a second
            float x0 = player.X, z0 = player.Z;
            for (int i = 0; i < 25; i++)
            {
                phone.Send(new JObject { ["t"] = "stick", ["x"] = 1, ["y"] = 0 });
                float until = Time.realtimeSinceStartup + 0.04f;
                while (Time.realtimeSinceStartup < until) yield return null;
            }
            phone.Send(new JObject { ["t"] = "stick", ["x"] = 0, ["y"] = 0 });
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Greater(player.X - x0, 2f, "the capsule should have walked toward +X");
            Assert.AreEqual(z0, player.Z, 0.01f);

            // lock the screen: the connection drops, the capsule stays
            phone.Dispose();
            for (int i = 0; i < 20; i++) yield return null;
            Assert.IsFalse(host.Roster[slot].Connected);
            Assert.IsNotNull(host.World.Find(slot), "a dropped phone's player must stay");

            // unlock: rejoin with the saved token, same slot, team and number
            var again = new FakePhone();
            yield return FakePhone.Wait(again.Connect(port));
            again.Send(new JObject { ["t"] = "join", ["code"] = host.RoomCode, ["name"] = "Tess", ["token"] = token, ["pid"] = "p1" });
            JObject rejoined = null, repicked = null;
            yield return again.Expect("joined", m => rejoined = m);
            yield return again.Expect("picked", m => repicked = m);
            yield return again.Expect("ui", m => ui = m);
            Assert.AreEqual(slot, (int)rejoined["slot"]);
            Assert.AreEqual(1, (int)rejoined["team"]);
            Assert.AreEqual((int)picked["number"], (int)repicked["number"]);
            Assert.That((string)ui["layout"], Is.EqualTo("huddle").Or.EqualTo("walk"), "on the field: the huddle or the line");
            Assert.IsTrue(host.Roster[slot].Connected);
            Assert.AreEqual(22, host.World.Players.Count, "still 22 players, not 23");
            again.Dispose();
        }

        [UnityTest]
        public IEnumerator WrongCodeIsRefused()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var phone = new FakePhone();
            yield return FakePhone.Wait(phone.Connect(host.Server.Port));
            phone.Send(new JObject { ["t"] = "join", ["code"] = "ZZZZ" == host.RoomCode ? "YYYY" : "ZZZZ", ["name"] = "X", ["token"] = "", ["pid"] = "p" });
            JObject err = null;
            yield return phone.Expect("err", m => err = m);
            StringAssert.Contains("room code", (string)err["m"]);
            Assert.AreEqual(0, host.Roster.Count);
            phone.Dispose();
        }
    }
}
