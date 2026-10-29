using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // PocketParty.Net with no Unity: the server, the roster, the QR encoder.
    public sealed class NetTests
    {
        const string PhoneDir = "Assets/StreamingAssets/phone";

        static string Http(int port, string path)
        {
            using (var c = new TcpClient("127.0.0.1", port))
            {
                var s = c.GetStream();
                s.ReadTimeout = 5000;
                var req = Encoding.ASCII.GetBytes("GET " + path + " HTTP/1.1\r\nHost: x\r\n\r\n");
                s.Write(req, 0, req.Length);
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        [Test]
        public void ServesThePhonePageAndNothingElse()
        {
            using (var srv = new PhoneServer(PhoneDir))
            {
                Assert.IsTrue(srv.Start(IPAddress.Loopback, 0, 1));
                StringAssert.StartsWith("HTTP/1.1 200", Http(srv.Port, "/"));
                StringAssert.Contains("Pocket Party Football", Http(srv.Port, "/join/ABCD"));
                StringAssert.StartsWith("HTTP/1.1 200", Http(srv.Port, "/app.js"));
                StringAssert.StartsWith("HTTP/1.1 200", Http(srv.Port, "/style.css"));
                StringAssert.StartsWith("HTTP/1.1 404", Http(srv.Port, "/../ProjectSettings/ProjectVersion.txt"));
                StringAssert.StartsWith("HTTP/1.1 404", Http(srv.Port, "/app.js.meta"));
                StringAssert.StartsWith("HTTP/1.1 404", Http(srv.Port, "/nope.js"));
            }
        }

        [Test]
        public void WebSocketRoundTrip()
        {
            using (var srv = new PhoneServer(PhoneDir))
            using (var ws = new ClientWebSocket())
            {
                Assert.IsTrue(srv.Start(IPAddress.Loopback, 0, 1));
                Assert.IsTrue(ws.ConnectAsync(new Uri("ws://127.0.0.1:" + srv.Port + "/ws"), CancellationToken.None).Wait(5000));
                var open = Next(srv);
                Assert.AreEqual(NetEventKind.Open, open.Kind);
                var hello = Encoding.UTF8.GetBytes("{\"t\":\"ping\",\"c\":7}");
                Assert.IsTrue(ws.SendAsync(new ArraySegment<byte>(hello), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000));
                var msg = Next(srv);
                Assert.AreEqual(NetEventKind.Message, msg.Kind);
                Assert.AreEqual(7, PhoneProtocol.Int(PhoneProtocol.Parse(msg.Text), "c"));
                srv.Send(open.Conn, PhoneProtocol.Pong(7));
                var buf = new byte[256];
                var rt = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                Assert.IsTrue(rt.Wait(5000));
                StringAssert.Contains("pong", Encoding.UTF8.GetString(buf, 0, rt.Result.Count));
            }
        }

        static NetEvent Next(PhoneServer srv)
        {
            for (int i = 0; i < 500; i++)
            {
                if (srv.TryDequeue(out var e)) return e;
                Thread.Sleep(10);
            }
            Assert.Fail("no server event");
            return default;
        }

        [Test]
        public void TokenRejoinKeepsTheSlotAndTeam()
        {
            var r = new Roster(22);
            var a = r.JoinPhone(1, "ann", "", "pidA", out bool re1);
            a.Team = 1;
            var b = r.JoinPhone(2, "bob", "", "pidB", out _);
            r.Disconnect(a);
            var again = r.JoinPhone(3, "", a.Token, "pidA", out bool re2);
            Assert.IsFalse(re1);
            Assert.IsTrue(re2);
            Assert.AreSame(a, again);
            Assert.AreEqual(1, again.Team);
            Assert.AreEqual(3, again.Conn);
            Assert.AreEqual("ANN", again.Name);
            Assert.AreNotEqual(a.Slot, b.Slot);
            Assert.AreEqual(2, r.Count);
        }

        [Test]
        public void RosterFillsUp()
        {
            var r = new Roster(3);
            Assert.IsNotNull(r.JoinPhone(1, "a", "", "", out _));
            Assert.IsNotNull(r.AddBot(0));
            Assert.IsNotNull(r.JoinPhone(2, "b", "", "", out _));
            Assert.IsNull(r.JoinPhone(3, "c", "", "", out _));
            Assert.AreEqual(1, r.TeamCount(0, false));
            Assert.AreEqual(0, r.TeamCount(0, true));
        }

        [Test]
        public void NamesAreCleaned()
        {
            Assert.AreEqual("KENNY", Roster.CleanName("  kenny<script> ", 0).Substring(0, 5));
            Assert.AreEqual("PLAYER 4", Roster.CleanName("   ", 3));
            Assert.LessOrEqual(Roster.CleanName("abcdefghijklmnopqrstuvwxyz", 0).Length, 12);
        }

        [Test]
        public void RoomCodeUsesOnlyClearLetters()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            string code = Roster.NewRoomCode(letters, 4, new Random(1));
            Assert.AreEqual(4, code.Length);
            foreach (char c in code) StringAssert.Contains(c.ToString(), letters);
        }

        [Test]
        public void QrEncodesAJoinUrl()
        {
            var qr = QrCode.EncodeText("http://192.168.1.23:8080/join/KEEL", QrEcc.Medium);
            Assert.GreaterOrEqual(qr.Size, 21);
            Assert.IsTrue(qr.GetModule(0, 0));        // finder pattern corner is dark
            Assert.IsFalse(qr.GetModule(7, 7));       // separator is light
        }

        [Test]
        public void LanAddressRanksPrivateRanges()
        {
            Assert.AreEqual(1, LanAddress.Rank(IPAddress.Parse("192.168.1.5")));
            Assert.AreEqual(2, LanAddress.Rank(IPAddress.Parse("10.0.0.2")));
            Assert.AreEqual(3, LanAddress.Rank(IPAddress.Parse("172.20.1.1")));
            Assert.AreEqual(0, LanAddress.Rank(IPAddress.Parse("8.8.8.8")));
            Assert.IsNotEmpty(LanAddress.Best());
        }
    

        // FEATURES.hotspotQr: the Wi-Fi join code phones' cameras read as "Join network?"
        [Test]
        public void WifiCodeHoldsTheNameAndPassword()
        {
            Assert.AreEqual("WIFI:T:WPA;S:Party TV;P:touchdown1;;", WifiQr.Payload("Party TV", "touchdown1"));
            Assert.AreEqual("WIFI:T:nopass;S:Open;;", WifiQr.Payload("Open", ""));
            Assert.AreEqual("WIFI:T:WPA;S:a\\;b\\:c;P:p\\,q\\\"r\\\\s12;;", WifiQr.Payload("a;b:c", "p,q\"r\\s12"));
            Assert.IsNull(WifiQr.Payload("", "whatever1"));
            Assert.IsNull(WifiQr.Problem("Party TV", "touchdown1"));
            Assert.IsNull(WifiQr.Problem("Party TV", ""));
            Assert.IsNotNull(WifiQr.Problem("", "touchdown1"));
            Assert.IsNotNull(WifiQr.Problem("Party TV", "short"));
            Assert.IsNotNull(WifiQr.Problem(new string('x', 33), "touchdown1"));
            var qr = QrCode.EncodeText(WifiQr.Payload("Party TV", "touchdown1"), QrEcc.Medium);
            Assert.GreaterOrEqual(qr.Size, 21);
        }

        [Test]
        public void HotspotAddressIsKeptApartFromTheLan()
        {
            Assert.IsTrue(LanAddress.IsHotspot("192.168.137.1", LanAddress.WindowsHotspotPrefix));
            Assert.IsFalse(LanAddress.IsHotspot("192.168.1.37", LanAddress.WindowsHotspotPrefix));
            Assert.IsFalse(LanAddress.IsHotspot("192.168.13.7", LanAddress.WindowsHotspotPrefix));
            Assert.IsFalse(LanAddress.IsHotspot("192.168.137.1", ""));
            Assert.IsFalse(LanAddress.IsHotspot(null, LanAddress.WindowsHotspotPrefix));
            Assert.IsFalse(LanAddress.IsHotspot(LanAddress.Best(), LanAddress.WindowsHotspotPrefix));
            string hot = LanAddress.Hotspot();
            if (hot != null) StringAssert.StartsWith(LanAddress.WindowsHotspotPrefix, hot);
        }
    }
}
