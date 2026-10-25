using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PocketParty.Net
{
    // The shared parts of the phone <-> game messages: JSON text frames, one object each, field "t" = type.
    // Every game uses join / joined / err / ping / pong / stick / btn; game-specific messages are built by the game.
    public static class PhoneProtocol
    {
        public static JObject Parse(string text)
        {
            try { return JObject.Parse(text); }
            catch (JsonException) { return null; }
        }

        public static string Type(JObject m) => m?["t"]?.Type == JTokenType.String ? (string)m["t"] : "";

        public static float Float(JObject m, string key, float min, float max)
        {
            var tok = m[key];
            if (tok == null || (tok.Type != JTokenType.Float && tok.Type != JTokenType.Integer)) return 0f;
            float v = tok.Value<float>();
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Math.Max(min, Math.Min(max, v));
        }

        public static string Str(JObject m, string key) => m[key]?.Type == JTokenType.String ? (string)m[key] : "";

        public static int Int(JObject m, string key, int fallback = 0) => m[key]?.Type == JTokenType.Integer ? (int)m[key] : fallback;

        public static string Err(string message) => new JObject { ["t"] = "err", ["m"] = message }.ToString(Formatting.None);

        public static string Pong(int c) => new JObject { ["t"] = "pong", ["c"] = c }.ToString(Formatting.None);

        public static string Text(JObject o) => o.ToString(Formatting.None);
    }
}
