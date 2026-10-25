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

        sealed class FakePhone : IDisposable
        {
            readonly ClientWebSocket ws = new ClientWebSocket();
            readonly ConcurrentQueue<JObject> inbox = new ConcurrentQueue<JObject>();
            readonly CancellationTokenSource stop = new CancellationTokenSource();

            public Task Connect(int port) => ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/ws"), CancellationToken.None)
                .ContinueWith(t => { if (!t.IsFaulted) ReceiveLoop(); return t; }).Unwrap();

            async void ReceiveLoop()
            {
                var buf = new byte[16384];
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), stop.Token);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        inbox.Enqueue(JObject.Parse(Encoding.UTF8.GetString(buf, 0, r.Count)));
                    }
                }
                catch (Exception) { }
            }

            public void Send(JObject o)
            {
                var b = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None));
                ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(2000);
            }

            // Waits (in frames) for the next message of this type; others are skipped.
            public IEnumerator Expect(string type, Action<JObject> got, int frames = 300)
            {
                for (int i = 0; i < frames; i++)
                {
                    while (inbox.TryDequeue(out var m))
                        if ((string)m["t"] == type) { got(m); yield break; }
                    yield return null;
                }
                Assert.Fail("phone never got '" + type + "'");
            }

            public void Drop() { try { ws.Abort(); } catch (Exception) { } }

            public void Dispose() { stop.Cancel(); Drop(); ws.Dispose(); }
        }

        static IEnumerator Wait(Task t)
        {
            for (int i = 0; i < 300 && !t.IsCompleted; i++) yield return null;
            Assert.IsTrue(t.IsCompleted && !t.IsFaulted, "task failed: " + t.Exception);
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
            yield return Wait(phone.Connect(port));
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
            Assert.AreEqual("walk", (string)ui["layout"]);
            var player = host.World.Find(slot);
            Assert.IsNotNull(player, "no capsule for the phone");
            Assert.IsTrue(player.Human);
            Assert.AreEqual(1, player.Team);
            Assert.AreEqual(1, host.Players.Count);

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
            yield return Wait(again.Connect(port));
            again.Send(new JObject { ["t"] = "join", ["code"] = host.RoomCode, ["name"] = "Tess", ["token"] = token, ["pid"] = "p1" });
            JObject rejoined = null, repicked = null;
            yield return again.Expect("joined", m => rejoined = m);
            yield return again.Expect("picked", m => repicked = m);
            yield return again.Expect("ui", m => ui = m);
            Assert.AreEqual(slot, (int)rejoined["slot"]);
            Assert.AreEqual(1, (int)rejoined["team"]);
            Assert.AreEqual((int)picked["number"], (int)repicked["number"]);
            Assert.AreEqual("walk", (string)ui["layout"]);
            Assert.IsTrue(host.Roster[slot].Connected);
            Assert.AreEqual(1, host.Players.Count, "still one capsule, not two");
            again.Dispose();
        }

        [UnityTest]
        public IEnumerator WrongCodeIsRefused()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var phone = new FakePhone();
            yield return Wait(phone.Connect(host.Server.Port));
            phone.Send(new JObject { ["t"] = "join", ["code"] = "ZZZZ" == host.RoomCode ? "YYYY" : "ZZZZ", ["name"] = "X", ["token"] = "", ["pid"] = "p" });
            JObject err = null;
            yield return phone.Expect("err", m => err = m);
            StringAssert.Contains("room code", (string)err["m"]);
            Assert.AreEqual(0, host.Roster.Count);
            phone.Dispose();
        }
    }
}
