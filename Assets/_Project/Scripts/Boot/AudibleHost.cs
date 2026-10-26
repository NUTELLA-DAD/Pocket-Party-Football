using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Boot
{
    // The phone side of audibles and hot routes (WS-13, Sim/Audibles.cs, phone/audible.js). PartyHost calls Drive
    // once a frame and OnMessage for the "audible" and "hot" presses. Everything here goes ONLY to the phones of the
    // team it is about (TeamSeats): never a public message, never the TV.
    //   game -> phone: audible {open, side, caller, left, plays, hot, mine, routes}   <- that team's phones ONLY (secret)
    //                  job (Messages.JobFor) again after an audible                   <- that team's phones ONLY (secret)
    //   phone -> game: audible {id, aid}  (the caller: change the play at the line)
    //                  hot {id, aid}      (a receiver: one of audibles.hotRoutes, "" = back to the called route)
    public static class AudibleHost
    {
        static PartyHost owner;
        static readonly Dictionary<int, string> sent = new Dictionary<int, string>();   // slot -> what its phone was last told

        // Each phone gets its team's audible message whenever something it shows changed: the phase, the down, its
        // spot, its team's audibles (a version per team, so one team's audible never makes the other team's phones
        // hear anything), or a new socket after a rejoin.
        public static void Drive(PartyHost h)
        {
            var w = h.World;
            if (w == null || h.Server == null || !Audibles.On(w)) return;
            if (owner != h) { owner = h; sent.Clear(); }
            foreach (var (seat, p) in TeamSeats(h, -1))
            {
                string key = seat.Conn + "|" + w.HuddleNumber + "|" + w.Phase + "|" + p.Team + "|" + p.Spot + "|" + w.Offense
                    + "|" + Audibles.VersionOf(w, p.Team) + "|" + (Huddle.Caller(w, p.Team) == p);
                if (sent.TryGetValue(seat.Slot, out var had) && had == key) continue;
                sent[seat.Slot] = key;
                h.Server.Send(seat.Conn, For(w, p));
            }
        }

        // "audible" or "hot" from a phone. The press must carry the aid of the screen on show (the one before the hike).
        public static void OnMessage(PartyHost h, PhoneSource src, JObject m)
        {
            var w = h.World;
            if (w == null || !Audibles.On(w) || PhoneProtocol.Str(m, "aid") != src.Aid) return;
            var p = w.Find(src.Seat.Slot);
            if (p == null) return;
            string t = PhoneProtocol.Type(m), id = PhoneProtocol.Str(m, "id");
            if (t == "audible")
            {
                if (!Audibles.Call(w, p.Slot, id)) return;
                foreach (var (seat, tp) in TeamSeats(h, p.Team))
                {
                    string job = Messages.JobFor(w, p.Team, tp);
                    if (job != null) h.Server.Send(seat.Conn, job);
                }
            }
            else if (t == "hot") { if (!Audibles.PickHot(w, p.Slot, id)) return; }
            else return;
            Drive(h);
        }

        // SECRET: only for phones on `me.Team`. What this person may change at the line, and the team's hot routes.
        public static string For(World w, Player me)
        {
            int team = me.Team;
            bool open = w.Phase == Phase.Set && Huddle.On(w);
            bool caller = Audibles.CanAudible(w, me);
            var plays = new JArray();
            if (caller)
                foreach (var pl in Huddle.Playbook(w, team))
                    if (pl.id != w.Calls[team]) plays.Add(new JObject { ["id"] = pl.id, ["name"] = pl.name, ["desc"] = pl.desc ?? "" });
            var hot = new JArray();
            if (Audibles.CanHot(w, me))
                foreach (var r in w.Cfg.audibles.hotRoutes) hot.Add(new JObject { ["id"] = r.id, ["name"] = r.name, ["text"] = r.text ?? "" });
            var routes = new JArray();
            foreach (var q in w.Players)
            {
                if (q.Team != team) continue;
                string rid = Audibles.RouteOf(w, q);
                var r = rid.Length > 0 ? Audibles.FindRoute(w, rid) : null;
                if (r != null) routes.Add(new JObject { ["spot"] = q.Spot, ["number"] = q.Number, ["pos"] = q.Pos, ["id"] = r.id, ["name"] = r.name });
            }
            var mine = Audibles.FindRoute(w, Audibles.RouteOf(w, me));
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "audible", ["open"] = open, ["side"] = Huddle.SideOf(w, team),
                ["caller"] = caller, ["left"] = caller ? Audibles.Left(w, team) : 0, ["plays"] = plays,
                ["hot"] = hot, ["mine"] = mine != null ? mine.id : "", ["mineText"] = mine != null ? mine.text ?? "" : "",
                ["routes"] = routes,
            });
        }

        // The connected phones on `team` (-1: every team), with their players.
        static IEnumerable<(Seat seat, Player p)> TeamSeats(PartyHost h, int team)
        {
            foreach (var s in h.Roster.Seats)
            {
                if (s.Kind != SeatKind.Phone || !s.Connected || s.Conn < 0 || s.Team < 0 || (team >= 0 && s.Team != team)) continue;
                var p = h.World.Find(s.Slot);
                if (p != null && p.Team == s.Team) yield return (s, p);
            }
        }
    }
}
