using System.Threading.Tasks;
using PocketFootball.Sim;
using PocketParty.Net;
using UnityEngine;

namespace PocketFootball.Boot
{
    // FEATURES.hotspotQr (NO_HOTSPOT_QR=1: off): a game with no Wi-Fi router. The host turns on Windows Mobile Hotspot
    // and types its name and password once under SETTINGS > HOTSPOT WI-FI. The join screen and the pause menu then show
    // a second card, NO WI-FI?, beside the usual code: 1. a Wi-Fi code (the phone camera offers "Join network?"),
    // 2. the game's code on the hotspot's own address (192.168.137.1 here, when the PC is on its home Wi-Fi too).
    // Every few seconds (net.addressCheckS, off the main thread) it looks again, so turning the hotspot on after the
    // game started works. PP_HOTSPOT_NAME / PP_HOTSPOT_PASSWORD beat the saved pair (screenshots, tests).
    public sealed class HotspotJoin : MonoBehaviour
    {
        const string NameKey = "pp.hotspot.name", PassKey = "pp.hotspot.pass";

        public static HotspotJoin Instance { get; private set; }

        PartyHost host;
        GameConfig cfg;
        Texture2D wifiQr, gameQr;
        string wifiName, wifiPass, gameQrFor;
        float nextCheck;
        Task<string[]> check;

        public string HotspotUrl { get; private set; }   // the game on this PC's own hotspot (null: no hotspot up)
        public bool Shown => wifiQr != null && host != null && host.ServerError == null;
        public bool HotspotUp => HotspotUrl != null;

        public static bool On(GameConfig cfg) => cfg.Feature("hotspotQr");

        public static string SavedName => Env("PP_HOTSPOT_NAME") ?? PlayerPrefs.GetString(NameKey, "");
        public static string SavedPassword => Env("PP_HOTSPOT_PASSWORD") ?? PlayerPrefs.GetString(PassKey, "");

        static string Env(string name)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        // (the title's SETTINGS > HOTSPOT WI-FI page; an empty name forgets the hotspot)
        public static void Save(string name, string password)
        {
            PlayerPrefs.SetString(NameKey, name ?? "");
            PlayerPrefs.SetString(PassKey, string.IsNullOrEmpty(name) ? "" : password ?? "");
            PlayerPrefs.Save();
            Instance?.Reload();
        }

        public static void Attach(PartyHost h, GameConfig cfg)
        {
            if (!On(cfg)) return;
            var j = h.gameObject.AddComponent<HotspotJoin>();
            j.host = h;
            j.cfg = cfg;
            Instance = j;
            j.Reload();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // The saved name and password again: the Wi-Fi code, and a first look for the hotspot right now.
        public void Reload()
        {
            wifiName = SavedName.Trim();
            wifiPass = SavedPassword;
            string payload = WifiQr.Problem(wifiName, wifiPass) == null ? WifiQr.Payload(wifiName, wifiPass) : null;
            wifiQr = payload != null ? PartyHost.MakeQr(payload) : null;
            if (Watching) Apply(new[] { LanAddress.Best(cfg.net.hotspotPrefix), LanAddress.Hotspot(cfg.net.hotspotPrefix) });
            else Apply(null);
            if (wifiQr != null) Debug.Log("[net] hotspot code for '" + wifiName + "'" + (HotspotUp ? ", hotspot up at " + HotspotUrl : ", hotspot not found yet"));
        }

        // Only with a hotspot saved and the server on every network (the tests listen on 127.0.0.1 alone).
        bool Watching => wifiQr != null && host.BoundToAll && host.Server != null && host.ServerError == null && !Application.isBatchMode;

        void Update()
        {
            if (!Watching) return;
            if (check != null)
            {
                if (!check.IsCompleted) return;
                if (check.Status == TaskStatus.RanToCompletion) Apply(check.Result);
                check = null;
            }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + Mathf.Max(1f, cfg.net.addressCheckS);
            string prefix = cfg.net.hotspotPrefix;
            check = Task.Run(() => new[] { LanAddress.Best(prefix), LanAddress.Hotspot(prefix) });
        }

        // found: { the best LAN address, the hotspot address or null } (null: stop showing a hotspot address).
        void Apply(string[] found)
        {
            string hot = found != null ? found[1] : null;
            if (found != null)
            {
                // nothing but the hotspot (no other network): the usual code points at the hotspot
                host.MoveTo(found[0] == "127.0.0.1" && hot != null ? hot : found[0]);
            }
            string url = hot != null && host.Server != null ? "http://" + hot + ":" + host.Server.Port : null;
            if (url != HotspotUrl) Debug.Log("[net] " + (url != null ? "hotspot up: phones on it join at " + url + "/join/" + host.RoomCode : "hotspot off"));
            HotspotUrl = url;
            // the second game code, when the hotspot's address isn't the usual one
            string want = url != null && url != host.Url ? url + "/join/" + host.RoomCode : null;
            if (want != gameQrFor)
            {
                gameQrFor = want;
                gameQr = want != null ? PartyHost.MakeQr(want) : null;
            }
        }

        // ---- the card: the caller draws its background; this draws what's on it ----
        GUIStyle head, line, note;

        // The card's contents inside c, each QR code at most qrMax across.
        public void DrawCard(Rect c, float s, float qrMax, Color gold, Color muted)
        {
            head ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            line ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            note ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true, wordWrap = true };
            head.fontSize = Mathf.RoundToInt(36 * s);
            line.fontSize = Mathf.RoundToInt(23 * s);
            note.fontSize = Mathf.RoundToInt(19 * s);
            head.normal.textColor = gold;
            line.normal.textColor = Color.white;
            note.normal.textColor = muted;

            float pad = 22 * s, headH = 52 * s, stepH = 62 * s, nameH = 52 * s, hintH = HotspotUp ? 0f : 78 * s, gap = 12 * s;
            int codes = gameQr != null ? 2 : 1;
            float fixedH = 2 * pad + headH + stepH * 2 + nameH + hintH + (gap + 16 * s) * codes;
            float q = Mathf.Max(40 * s, Mathf.Min(qrMax, c.width - 2 * pad - 16 * s, (c.height - fixedH) / codes));
            float w = c.width - 2 * pad, x = c.x + pad, y = c.y + pad;

            GUI.Label(new Rect(x, y, w, headH), "NO WI-FI?", head);
            y += headH;
            GUI.Label(new Rect(x, y, w, stepH), "<b>1.</b> Scan to join the TV's Wi-Fi", line);
            y += stepH;
            y = Code(wifiQr, c.center.x, y, q, s) + gap;
            GUI.Label(new Rect(x, y, w, nameH), "<b>" + wifiName + "</b>" + (string.IsNullOrEmpty(wifiPass) ? "" : "\npassword <b>" + wifiPass + "</b>"), note);
            y += nameH;
            GUI.Label(new Rect(x, y, w, stepH), gameQr != null ? "<b>2.</b> Then scan this one" : "<b>2.</b> Then scan the game code", line);
            y += stepH;
            if (gameQr != null) y = Code(gameQr, c.center.x, y, q, s) + gap;
            if (!HotspotUp)
                GUI.Label(new Rect(x, y, w, hintH), "Hosting the hotspot on this PC? Turn on <b>Mobile hotspot</b> in Windows Settings first.", note);
        }

        // a QR code on white, centred on cx, top at y; returns its bottom
        static float Code(Texture2D tex, float cx, float y, float q, float s)
        {
            float edge = 8 * s;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cx - q * 0.5f - edge, y, q + 2 * edge, q + 2 * edge), Texture2D.whiteTexture);
            if (tex != null) GUI.DrawTexture(new Rect(cx - q * 0.5f, y + edge, q, q), tex);
            return y + q + 2 * edge;
        }
    }
}
