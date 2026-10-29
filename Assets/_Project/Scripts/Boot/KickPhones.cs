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
    //   FEATURES.kickSync: sweep also carries t (game seconds into the arrow now sweeping), maxDeg (the aim's widest
    //     angle, kicking.maxAimDeg), postsYd (half the posts' width) and distYd (the hold to the posts), sent again
    //     every kicking.phoneSyncS; the phone runs its arrow from t, and its kickaim and kick add st (the seconds its
    //     arrow showed) for Kicking.TapAt.
    // The kicker's phone also gets the "kick" layout (Input/PhoneLayouts.cs), which hides HIKE and the throws.
    // It also puts the TV's kick meter (View/KickMeter.cs) on the host.
    public sealed class KickPhones : MonoBehaviour
    {
        PartyHost host;
        readonly Dictionary<int, string> last = new Dictionary<int, string>();   // by slot: the last kickmeter sent
        readonly Dictionary<int, float> sentAt = new Dictionary<int, float>();   // by slot: when (FEATURES.kickSync)

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
            if (Synced(h.World, m)) { Kicking.TapAt(h.World, p.Slot, PhoneProtocol.Float(m, "st", 0f, 600f)); return; }
            Kicking.Kick(h.World, p.Slot, PhoneProtocol.Float(m, "power", 0f, 1f), PhoneProtocol.Float(m, "aim", -1f, 1f));
        }

        // FEATURES.kickSync: a tap from a phone running the game's sweep clock (it says which second it showed)
        static bool Synced(World w, JObject m) =>
            w.Cfg.Feature("kickSync") && m["st"] != null && Kicking.Stage(w) != Kicking.SweepStage.None;

        // The first tap of a field goal (FEATURES.kickSweep): the aim the phone's arrow showed. Same guards as a kick.
        public static void OnAim(PartyHost h, PhoneSource src, JObject m)
        {
            if (PhoneProtocol.Str(m, "aid") != src.Aid) return;
            var p = h.World.Find(src.Seat.Slot);
            if (p == null) return;
            if (Synced(h.World, m)) { Kicking.TapAt(h.World, p.Slot, PhoneProtocol.Float(m, "st", 0f, 600f)); return; }
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
                string key = s.Conn + " " + Meter(w, p, false);   // (a phone that reconnected gets it again)
                bool same = last.TryGetValue(s.Slot, out var was) && was == key;
                // FEATURES.kickSync: while an arrow sweeps, the game's sweep clock again every phoneSyncS
                bool sync = Kicking.Stage(w) != Kicking.SweepStage.None && p != null && Kicking.WaitingToKick(w, p) && w.Cfg.Feature("kickSync")
                    && (!sentAt.TryGetValue(s.Slot, out float at) || Time.unscaledTime - at >= w.Cfg.kicking.phoneSyncS);
                if (same && !sync) continue;
                last[s.Slot] = key;
                sentAt[s.Slot] = Time.unscaledTime;
                host.Server.Send(s.Conn, Meter(w, p));
            }
        }

        // clock: with FEATURES.kickSync, the sweep's t (left out to tell whether anything else changed)
        public static string Meter(World w, Player p, bool clock = true)
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
                if (w.Cfg.Feature("kickSync"))
                {
                    o["sweep"]["maxDeg"] = c.maxAimDeg;
                    o["sweep"]["postsYd"] = c.postsHalfWidthYd;
                    o["sweep"]["distYd"] = System.Math.Round(System.Math.Abs(w.Dir * w.Cfg.field.HalfLength - w.Kick.HoldX), 1);   // (the hold to the posts)
                    if (clock) o["sweep"]["t"] = System.Math.Round(Kicking.SweepSeconds(w), 3);
                }
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
