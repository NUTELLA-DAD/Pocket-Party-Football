using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PocketFootball.Tests.PlayMode
{
    // A phone made of a .NET WebSocket, talking to the game's own server on 127.0.0.1.
    // Every text it receives is also kept raw in All, so tests can check what it was (never) told.
    public sealed class FakePhone : IDisposable
    {
        readonly ClientWebSocket ws = new ClientWebSocket();
        readonly ConcurrentQueue<JObject> inbox = new ConcurrentQueue<JObject>();
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        public readonly ConcurrentQueue<string> All = new ConcurrentQueue<string>();
        public string Aid = "";      // the aid of the last "ui" message, for presses

        public Task Connect(int port) => ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/ws"), CancellationToken.None)
            .ContinueWith(t => { if (!t.IsFaulted) ReceiveLoop(); return t; }).Unwrap();

        async void ReceiveLoop()
        {
            var buf = new byte[65536];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), stop.Token);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    string text = Encoding.UTF8.GetString(buf, 0, r.Count);
                    All.Enqueue(text);
                    inbox.Enqueue(JObject.Parse(text));
                }
            }
            catch (Exception) { }
        }

        public void Send(JObject o)
        {
            var b = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None));
            ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(2000);
        }

        // Waits (in frames) for the next message of this type; others are skipped (but kept in All).
        public IEnumerator Expect(string type, Action<JObject> got, Func<JObject, bool> where = null, int frames = 300)
        {
            for (int i = 0; i < frames; i++)
            {
                while (inbox.TryDequeue(out var m))
                {
                    if ((string)m["t"] == "ui") Aid = (string)m["aid"];
                    if ((string)m["t"] == type && (where == null || where(m))) { got(m); yield break; }
                }
                yield return null;
            }
            Assert.Fail("phone never got '" + type + "'");
        }

        // Waits some frames, then takes everything queued: Latest keeps the newest message of each type.
        public readonly System.Collections.Generic.Dictionary<string, JObject> Latest = new System.Collections.Generic.Dictionary<string, JObject>();
        public IEnumerator Settle(int frames = 15)
        {
            for (int i = 0; i < frames; i++) yield return null;
            while (inbox.TryDequeue(out var m))
            {
                string t = (string)m["t"];
                if (t == "ui") Aid = (string)m["aid"];
                Latest[t] = m;
            }
        }

        // Join and pick a team; ends when the phone has its picked message.
        public IEnumerator JoinAndPick(int port, string code, string name, int team)
        {
            yield return Wait(Connect(port));
            Send(new JObject { ["t"] = "join", ["code"] = code, ["name"] = name, ["token"] = "", ["pid"] = name });
            yield return Expect("joined", _ => { });
            Send(new JObject { ["t"] = "pick", ["team"] = team });
            yield return Expect("picked", _ => { });
        }

        public void Drop() { try { ws.Abort(); } catch (Exception) { } }

        public void Dispose() { stop.Cancel(); Drop(); ws.Dispose(); }

        public static IEnumerator Wait(Task t)
        {
            for (int i = 0; i < 300 && !t.IsCompleted; i++) yield return null;
            Assert.IsTrue(t.IsCompleted && !t.IsFaulted, "task failed: " + t.Exception);
        }
    }
}
