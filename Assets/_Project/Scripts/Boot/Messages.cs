using Newtonsoft.Json.Linq;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Boot
{
    // Pocket Football's own phone messages (on top of PhoneProtocol's join / err / pong / stick / btn).
    //   game -> phone: joined {token, slot, name, code, team, teams}, lobby {teams}, picked {team, number, pos, color},
    //                  ui {layout: pick | huddle | walk, aid},
    //                  me {st, tired, oob} (stamina 0-100),
    //                  status {phase, secs, called[]}                       <- PUBLIC: never a play id or name
    //                  huddle {side, plays, spots, opp, me, caller, ...}    <- that team's phones ONLY (secret)
    //                  thumbs {counts, mine}, job {play, jobs, spots, me}   <- that team's phones ONLY (secret)
    //   phone -> game: pick {team}, suggest {id, aid}, lock {id, aid}
    public static class Messages
    {
        public static string Joined(Seat s, string code, GameConfig cfg, Roster r) => PhoneProtocol.Text(new JObject
        {
            ["t"] = "joined", ["token"] = s.Token, ["slot"] = s.Slot, ["name"] = s.Name, ["code"] = code, ["team"] = s.Team,
            ["teams"] = Teams(cfg, r),
        });

        public static string Lobby(GameConfig cfg, Roster r) => PhoneProtocol.Text(new JObject { ["t"] = "lobby", ["teams"] = Teams(cfg, r) });

        public static string Picked(Seat s, Player p, GameConfig cfg)
        {
            var t = cfg.teams[s.Team];
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "picked", ["team"] = s.Team, ["number"] = p.Number, ["pos"] = p.Pos, ["name"] = s.Name,
                ["teamName"] = t.name, ["color"] = t.color, ["color2"] = t.color2,
            });
        }

        // The phone's own numbers: stamina 0-100, tired (sprint locked until it refills), out of bounds.
        public static string Me(Player p) => PhoneProtocol.Text(new JObject
        {
            ["t"] = "me", ["st"] = (int)System.Math.Round(p.Stamina * 100f), ["tired"] = p.Tired, ["oob"] = p.OutOfBounds,
        });

        public static string Ui(string layout, string aid) => PhoneProtocol.Text(new JObject { ["t"] = "ui", ["layout"] = layout, ["aid"] = aid });

        // ---- the huddle ----

        // Public: every phone and the TV may see this. Only whether each team has called, never what.
        public static string Status(World w)
        {
            var called = new JArray();
            for (int t = 0; t < w.Calls.Length; t++) called.Add(w.Phase == Phase.Huddle && Huddle.IsLocked(w, t));
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "status", ["phase"] = w.Phase == Phase.Huddle ? "huddle" : "set",
                ["secs"] = Secs(w), ["called"] = called,
            });
        }

        public static int Secs(World w) => (w.PhaseTicks + w.Cfg.sim.tickHz - 1) / w.Cfg.sim.tickHz;

        // SECRET: only for phones on `team`. The team's playbook with every job, the formation, who calls.
        public static string HuddleFor(World w, int team, Player me)
        {
            var plays = new JArray();
            foreach (var p in Huddle.Playbook(w, team)) plays.Add(Play(p));
            var caller = Huddle.Caller(w, team);
            w.Suggestions.TryGetValue(me.Slot, out var mine);
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "huddle", ["side"] = Huddle.SideOf(w, team), ["secs"] = Secs(w),
                ["caller"] = caller == me, ["callerNumber"] = caller != null ? caller.Number : 0,
                ["locked"] = Huddle.IsLocked(w, team), ["mine"] = mine ?? "",
                ["plays"] = plays, ["spots"] = Spots(w, team), ["opp"] = Spots(w, Other(w, team)), ["me"] = me.Spot,
                ["thumbs"] = Counts(w, team),
            });
        }

        // SECRET: only for phones on `team`.
        public static string ThumbsFor(World w, int team, Player me)
        {
            w.Suggestions.TryGetValue(me.Slot, out var mine);
            return PhoneProtocol.Text(new JObject { ["t"] = "thumbs", ["counts"] = Counts(w, team), ["mine"] = mine ?? "", ["locked"] = Huddle.IsLocked(w, team) });
        }

        // SECRET: only for phones on `team`. The called play and this player's spot, for the job diagram.
        public static string JobFor(World w, int team, Player me)
        {
            var play = Huddle.Find(w, w.Calls[team]);
            if (play == null) return null;
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "job", ["side"] = Huddle.SideOf(w, team), ["play"] = Play(play),
                ["spots"] = Spots(w, team), ["opp"] = Spots(w, Other(w, team)), ["me"] = me.Spot,
            });
        }

        static int Other(World w, int team) => team == w.Offense ? w.Defense : w.Offense;

        static JObject Play(PlayConfig p) => JObject.FromObject(p);

        static JObject Counts(World w, int team)
        {
            var o = new JObject();
            foreach (var kv in Huddle.Thumbs(w, team)) o[kv.Key] = kv.Value;
            return o;
        }

        // The team's formation in the play frame: [ahead, z] from the ball, ahead = the offense's direction.
        static JArray Spots(World w, int team)
        {
            var a = new JArray();
            bool offense = team == w.Offense;
            foreach (var s in w.Spots(team))
                a.Add(new JObject { ["pos"] = s.pos, ["number"] = s.number, ["ahead"] = offense ? -s.back : s.back, ["z"] = s.z });
            return a;
        }

        static JArray Teams(GameConfig cfg, Roster r)
        {
            var a = new JArray();
            for (int i = 0; i < cfg.teams.Count; i++)
            {
                var t = cfg.teams[i];
                a.Add(new JObject
                {
                    ["name"] = t.name, ["short"] = t.@short, ["color"] = t.color, ["color2"] = t.color2,
                    ["people"] = r.TeamCount(i, true), ["players"] = cfg.players.perTeam,
                });
            }
            return a;
        }
    }
}
