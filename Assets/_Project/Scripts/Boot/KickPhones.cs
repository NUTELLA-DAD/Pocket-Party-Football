using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketFootball.View;
using PocketParty.Net;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The phones' side of kicking (WS-01). Messages on top of Messages.cs:
    //   game -> phone: kickmeter {on, kind, yards}   <- ONLY the kicker's phone (on: true while he lines up the kick)
    //     a field goal with FEATURES.kickSweep adds sweep {stage: "aim"|"power", aimS, powerS, need, shank} (the two
    //     arrows' periods, the power that just clears the bar, where the hook starts) and, with FEATURES.wind,
    //     wind {mph, right, along} (right: -1..1 across his kick to his right, along: -1..1 at his back)
    //   phone -> game: kick {power 0..1, aim -1..1, aid}   (the kick pad: phone/kick.js, or layouts.js's own pad)
    //   phone -> game: kickaim {aim -1..1, aid}   (FEATURES.kickSweep's first tap: the aim, and the snap)
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

        // The first tap of a field goal (FEATURES.kickSweep): the aim the phone's arrow showed. Same guards as a kick.
        public static void OnAim(PartyHost h, PhoneSource src, JObject m)
        {
            if (PhoneProtocol.Str(m, "aid") != src.Aid) return;
            var p = h.World.Find(src.Seat.Slot);
            if (p == null) return;
            Kicking.SetAim(h.World, p.Slot, PhoneProtocol.Float(m, "aim", -1f, 1f));
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
            var stage = on ? Kicking.Stage(w) : Kicking.SweepStage.None;
            if (stage != Kicking.SweepStage.None)
            {
                var c = w.Cfg.kicking;
                o["sweep"] = new JObject
                {
                    ["stage"] = stage == Kicking.SweepStage.Aim ? "aim" : "power",
                    ["aimS"] = c.aimSweepS, ["powerS"] = c.powerSweepS,
                    ["need"] = System.Math.Round(Kicking.NeedPower(w), 2), ["shank"] = c.shankFromPower,
                };
                if (stage == Kicking.SweepStage.Power) o["sweep"]["aim"] = System.Math.Round(w.Kick.Aim, 2);   // (the first tap's aim)
                if (Kicking.WindOn(w))
                {
                    Kicking.Wind(w, out float x, out float z);
                    float speed = (float)System.Math.Sqrt(x * x + z * z);
                    o["wind"] = new JObject
                    {
                        ["mph"] = (int)System.Math.Round(speed * c.windMphPerYdS),
                        ["right"] = speed > 0f ? System.Math.Round(-z * w.Dir / speed, 2) : 0.0,
                        ["along"] = speed > 0f ? System.Math.Round(x * w.Dir / speed, 2) : 0.0,
                    };
                }
            }
            return PhoneProtocol.Text(o);
        }
    }
}
