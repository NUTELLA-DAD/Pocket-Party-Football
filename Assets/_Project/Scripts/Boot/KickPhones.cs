using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The phones' side of kicking (WS-01). Two messages on top of Messages.cs:
    //   game -> phone: kickmeter {on, kind, yards}   <- ONLY the kicker's phone (on: true while he lines up the kick)
    //   phone -> game: kick {power 0..1, aim -1..1, aid}   (the kick pad: phone/kick.js, or layouts.js's own pad)
    // The kicker's phone also gets the "kick" layout (Input/PhoneLayouts.cs), which hides HIKE and the throws.
    // It also puts the TV's kick meter (View/KickMeter.cs) on the host.
    public sealed class KickPhones : MonoBehaviour
    {
        PartyHost host;
        readonly Dictionary<int, string> last = new Dictionary<int, string>();   // by slot: the last kickmeter sent

        public static KickPhones Attach(PartyHost h)
        {
            var k = h.gameObject.AddComponent<KickPhones>();
            k.host = h;
            KickMeter.Create(h.gameObject, h.World, h.Players);
            return k;
        }

        // A swipe from a phone. The press must carry the aid of the screen on show, and only the kicker can kick.
        public static void OnKick(PartyHost h, PhoneSource src, JObject m)
        {
            if (PhoneProtocol.Str(m, "aid") != src.Aid) return;
            var p = h.World.Find(src.Seat.Slot);
            if (p == null) return;
            Kicking.Kick(h.World, p.Slot, PhoneProtocol.Float(m, "power", 0f, 1f), PhoneProtocol.Float(m, "aim", -1f, 1f));
        }

        // Every frame: each phone gets kickmeter when its "am I kicking?" changes (so only the kicker ever sees on: true).
        void Update()
        {
            if (host == null || host.Server == null || host.World == null || !Kicking.On(host.World)) return;
            var w = host.World;
            foreach (var s in host.Roster.Seats)
            {
                if (s.Kind != SeatKind.Phone || !s.Connected || s.Conn < 0) continue;
                var p = w.Find(s.Slot);
                string msg = Meter(w, p);
                string key = s.Conn + " " + msg;   // (a phone that reconnected gets it again)
                if (last.TryGetValue(s.Slot, out var was) && was == key) continue;
                last[s.Slot] = key;
                host.Server.Send(s.Conn, msg);
            }
        }

        public static string Meter(World w, Player p)
        {
            bool on = p != null && Kicking.WaitingToKick(w, p);
            var o = new JObject { ["t"] = "kickmeter", ["on"] = on };
            if (on) { o["kind"] = Kicking.Name(w); o["yards"] = Kicking.KickYards(w); }
            return PhoneProtocol.Text(o);
        }
    }
}
