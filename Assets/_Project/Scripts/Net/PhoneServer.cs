using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PocketParty.Net
{
    public enum NetEventKind { Open, Message, Close, Page }   // Page: a phone loaded the page (Text = its address), for the log

    public struct NetEvent
    {
        public NetEventKind Kind;
        public int Conn;
        public string Text;
    }

    // A tiny HTTP + WebSocket server (RFC 6455 text frames only) for the phone controllers.
    // Serves: GET / and /join/<CODE> -> index.html, any flat file in the phone folder with a known type (/app.js,
    // /style.css, ...), and upgrades /ws. Nothing else. Shared by the Pocket Party games (from Airship Crew's server).
    // Network threads only enqueue events; the game drains them on the main thread with TryDequeue.
    public sealed class PhoneServer : IDisposable
    {
        const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const int MaxHeaderBytes = 8192;
        const int MaxFrameBytes = 16384;
        // Only flat file names from the phone folder: no folders, no "..".
        static readonly System.Text.RegularExpressions.Regex SafeName = new System.Text.RegularExpressions.Regex(@"^/[A-Za-z0-9_-]+\.[a-z0-9]+$");
        static readonly Dictionary<string, string> Types = new Dictionary<string, string>
        {
            { ".js", "application/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
            { ".png", "image/png" }, { ".svg", "image/svg+xml" }, { ".json", "application/json; charset=utf-8" },
        };

        readonly string webRoot;
        readonly ConcurrentQueue<NetEvent> events = new ConcurrentQueue<NetEvent>();
        readonly ConcurrentDictionary<int, Conn> conns = new ConcurrentDictionary<int, Conn>();
        TcpListener listener;
        Thread acceptThread;
        volatile bool running;
        int nextId;

        public int Port { get; private set; }
        public string Error { get; private set; }
        public int ConnectionCount => conns.Count;

        sealed class Conn
        {
            public int Id;
            public TcpClient Client;
            public NetworkStream Stream;
            public readonly object SendLock = new object();
            public volatile bool Open = true;
        }

        public PhoneServer(string webRoot) { this.webRoot = webRoot; }

        // Tries port, port+1, ... port+tries-1. Port 0 = any free port (tests).
        public bool Start(IPAddress bind, int port, int tries)
        {
            for (int i = 0; i < Math.Max(1, tries); i++)
            {
                int p = port == 0 ? 0 : port + i;
                try
                {
                    listener = new TcpListener(bind, p);
                    listener.Start();
                    Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    running = true;
                    acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "PhoneServer.Accept" };
                    acceptThread.Start();
                    return true;
                }
                catch (SocketException e) { Error = e.Message; listener = null; }
            }
            return false;
        }

        public bool TryDequeue(out NetEvent e) => events.TryDequeue(out e);

        public void Send(int conn, string text)
        {
            if (!conns.TryGetValue(conn, out var c) || !c.Open) return;
            byte[] payload = Encoding.UTF8.GetBytes(text);
            byte[] frame = BuildFrame(0x1, payload);
            try { lock (c.SendLock) c.Stream.Write(frame, 0, frame.Length); }
            catch (Exception) { CloseConn(c); }
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch (Exception) { }
            foreach (var c in conns.Values) CloseConn(c);
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { if (!running) return; continue; }
                client.NoDelay = true;
                var t = new Thread(() => Serve(client)) { IsBackground = true, Name = "PhoneServer.Conn" };
                t.Start();
            }
        }

        void Serve(TcpClient client)
        {
            Conn conn = null;
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 10000;
                var (method, path, headers) = ReadRequest(stream);
                if (method != "GET") { WriteStatus(stream, "405 Method Not Allowed"); return; }
                string pathOnly = path.Split('?')[0];
                if (pathOnly == "/ws" && headers.TryGetValue("upgrade", out var up) && up.ToLowerInvariant() == "websocket"
                    && headers.TryGetValue("sec-websocket-key", out var key))
                {
                    string accept = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + WsGuid)));
                    string resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
                    byte[] rb = Encoding.ASCII.GetBytes(resp);
                    stream.Write(rb, 0, rb.Length);
                    stream.ReadTimeout = Timeout.Infinite;
                    conn = new Conn { Id = Interlocked.Increment(ref nextId), Client = client, Stream = stream };
                    conns[conn.Id] = conn;
                    events.Enqueue(new NetEvent { Kind = NetEventKind.Open, Conn = conn.Id });
                    FrameLoop(conn);
                }
                else
                {
                    if (pathOnly == "/" || pathOnly.StartsWith("/join/", StringComparison.Ordinal))
                        events.Enqueue(new NetEvent { Kind = NetEventKind.Page, Text = (client.Client.RemoteEndPoint as IPEndPoint)?.Address + " " + pathOnly });
                    ServeFile(stream, pathOnly);
                }
            }
            catch (Exception) { }
            finally
            {
                if (conn != null) CloseConn(conn);
                else { try { client.Close(); } catch (Exception) { } }
            }
        }

        void ServeFile(NetworkStream stream, string path)
        {
            string file, type;
            if (path == "/" || path.StartsWith("/join/", StringComparison.Ordinal)) { file = "index.html"; type = "text/html; charset=utf-8"; }
            else if (!SafeName.IsMatch(path) || !Types.TryGetValue(Path.GetExtension(path), out type)) { WriteStatus(stream, "404 Not Found"); return; }
            else file = path.Substring(1);
            string full = Path.Combine(webRoot, file);
            if (!File.Exists(full)) { WriteStatus(stream, "404 Not Found"); return; }
            byte[] body = File.ReadAllBytes(full);
            string head = "HTTP/1.1 200 OK\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                          "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(head);
            stream.Write(hb, 0, hb.Length);
            stream.Write(body, 0, body.Length);
        }

        static void WriteStatus(NetworkStream s, string status)
        {
            byte[] b = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            s.Write(b, 0, b.Length);
        }

        static (string method, string path, Dictionary<string, string> headers) ReadRequest(NetworkStream s)
        {
            var buf = new List<byte>();
            int b;
            while ((b = s.ReadByte()) != -1)
            {
                buf.Add((byte)b);
                int n = buf.Count;
                if (n >= 4 && buf[n - 4] == '\r' && buf[n - 3] == '\n' && buf[n - 2] == '\r' && buf[n - 1] == '\n') break;
                if (n > MaxHeaderBytes) throw new IOException("header too large");
            }
            string[] lines = Encoding.ASCII.GetString(buf.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) throw new IOException("bad request line");
            var headers = new Dictionary<string, string>();
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) headers[lines[i].Substring(0, c).Trim().ToLowerInvariant()] = lines[i].Substring(c + 1).Trim();
            }
            return (first[0], first[1], headers);
        }

        void FrameLoop(Conn c)
        {
            var s = c.Stream;
            var message = new MemoryStream();
            while (running && c.Open)
            {
                int b0 = s.ReadByte(), b1 = s.ReadByte();
                if (b0 < 0 || b1 < 0) return;
                bool fin = (b0 & 0x80) != 0;
                int opcode = b0 & 0x0F;
                bool masked = (b1 & 0x80) != 0;
                long len = b1 & 0x7F;
                if (len == 126) { var l2 = ReadExact(s, 2); len = (l2[0] << 8) | l2[1]; }
                else if (len == 127) { var l = ReadExact(s, 8); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | l[i]; }
                if (len > MaxFrameBytes || len < 0) return;
                byte[] mask = masked ? ReadExact(s, 4) : null;
                byte[] data = ReadExact(s, (int)len);
                if (masked) for (int i = 0; i < data.Length; i++) data[i] ^= mask[i & 3];

                if (opcode == 0x8) { TrySend(c, BuildFrame(0x8, new byte[0])); return; }               // close
                if (opcode == 0x9) { TrySend(c, BuildFrame(0xA, data)); continue; }                    // ping -> pong
                if (opcode == 0xA) continue;                                                            // pong
                if (opcode == 0x1 || opcode == 0x0)
                {
                    message.Write(data, 0, data.Length);
                    if (message.Length > MaxFrameBytes) return;
                    if (fin)
                    {
                        string text = Encoding.UTF8.GetString(message.ToArray());
                        message.SetLength(0);
                        events.Enqueue(new NetEvent { Kind = NetEventKind.Message, Conn = c.Id, Text = text });
                    }
                }
            }
        }

        static byte[] ReadExact(NetworkStream s, int n)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) throw new IOException("closed");
                got += r;
            }
            return buf;
        }

        static byte[] BuildFrame(int opcode, byte[] payload)
        {
            int n = payload.Length;
            int headLen = n < 126 ? 2 : n <= 0xFFFF ? 4 : 10;
            var f = new byte[headLen + n];
            f[0] = (byte)(0x80 | opcode);
            if (n < 126) f[1] = (byte)n;
            else if (n <= 0xFFFF) { f[1] = 126; f[2] = (byte)(n >> 8); f[3] = (byte)n; }
            else { f[1] = 127; for (int i = 0; i < 8; i++) f[2 + i] = (byte)((long)n >> (8 * (7 - i))); }
            Buffer.BlockCopy(payload, 0, f, headLen, n);
            return f;
        }

        void TrySend(Conn c, byte[] frame)
        {
            try { lock (c.SendLock) c.Stream.Write(frame, 0, frame.Length); } catch (Exception) { }
        }

        void CloseConn(Conn c)
        {
            if (!c.Open) return;
            c.Open = false;
            conns.TryRemove(c.Id, out _);
            try { c.Client.Close(); } catch (Exception) { }
            events.Enqueue(new NetEvent { Kind = NetEventKind.Close, Conn = c.Id });
        }
    }
}
