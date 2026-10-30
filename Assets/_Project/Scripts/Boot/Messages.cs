using System.Collections.Generic;
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
    //                  huddle {side, plays, spots, opp, me, caller, draw, ...}   <- that team's phones ONLY (secret)
    //                  thumbs {counts, mine}, job {play, jobs, spots, me}   <- that team's phones ONLY (secret)
    //   phone -> game: pick {team}, suggest {id, aid}, lock {id, aid}
    //                  custom {base, routes: [{spot, pts}], aid}   (FEATURES.customPlays: the caller's drawn play)
    public static class Messages
    {
        public static string Joined(Seat s, string code, GameConfig cfg, Roster r) => PhoneProtocol.Text(new JObject
        {
            ["t"] = "joined", ["token"] = s.Token, ["slot"] = s.Slot, ["name"] = s.Name, ["code"] = code, ["team"] = s.Team,
            ["teams"] = Teams(cfg, r),
        });

        public static string Lobby(GameConfig cfg, Roster r) => PhoneProtocol.Text(new JObject { ["t"] = "lobby", ["teams"] = Teams(cfg, r) });

        // w (FEATURES.season): the recruit this person plays as goes in `as` (his name), when he wears one.
        public static string Picked(Seat s, Player p, GameConfig cfg, World w = null)
        {
            var t = cfg.teams[s.Team];
            var o = new JObject
            {
                ["t"] = "picked", ["team"] = s.Team, ["number"] = p.Number, ["pos"] = p.Pos, ["name"] = s.Name,
                ["teamName"] = t.name, ["color"] = t.color, ["color2"] = t.color2,
            };
            string recruit = w != null ? SeasonRules.NameOf(w, p) : null;
            if (!string.IsNullOrEmpty(recruit)) o["as"] = recruit;
            return PhoneProtocol.Text(o);
        }

        // The phone's own numbers: stamina 0-100, tired (sprint locked until it refills), out of bounds.
        public static string Me(Player p) => PhoneProtocol.Text(new JObject
        {
            ["t"] = "me", ["st"] = (int)System.Math.Round(p.Stamina * 100f), ["tired"] = p.Tired, ["oob"] = p.OutOfBounds,
        });

        public static string Ui(string layout, string aid, JArray receivers = null, string line = null, bool throwStick = false, bool driveStick = false)
        {
            var o = new JObject { ["t"] = "ui", ["layout"] = layout, ["aid"] = aid };
            if (receivers != null) o["receivers"] = receivers;
            if (line != null) o["line"] = line;
            if (throwStick) o["throwStick"] = true;   // the QB's aim-and-throw stick (FEATURES.throwStick)
            if (driveStick) o["driveStick"] = true;   // the lineman's BLOCK (FEATURES.driveBlock) or BULL RUSH (FEATURES.driveRush) button is a drive stick
            return PhoneProtocol.Text(o);
        }

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
                ["score"] = new JArray(w.Score), ["down"] = Downs.On(w) ? Downs.DownText(w) : "", ["tv"] = PartyHost.TvLine(w, w.Cfg),
            });
        }

        public static int Secs(World w) => (w.PhaseTicks + w.Cfg.sim.tickHz - 1) / w.Cfg.sim.tickHz;

        // SECRET: only for phones on `team`. The team's playbook with every job, the formation, who calls.
        public static string HuddleFor(World w, int team, Player me)
        {
            var plays = new JArray();
            foreach (var p in Huddle.Playbook(w, team)) plays.Add(Play(w, team, p));
            var caller = Huddle.Caller(w, team);
            w.Suggestions.TryGetValue(me.Slot, out var mine);
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "huddle", ["side"] = Huddle.SideOf(w, team), ["secs"] = Secs(w),
                ["caller"] = caller == me, ["callerNumber"] = caller != null ? caller.Number : 0,
                ["locked"] = Huddle.IsLocked(w, team), ["mine"] = mine ?? "",
                ["plays"] = plays, ["spots"] = Spots(w, team), ["opp"] = Spots(w, Other(w, team)), ["me"] = me.Spot,
                ["thumbs"] = Counts(w, team), ["draw"] = Draw(w, me),
            });
        }

        // FEATURES.customPlays: what the caller may draw on (null for everyone else): the pass plays that can be the
        // base, the receiver spots, how many points a path keeps, and how far it may go (ahead, back, across). SECRET like the rest of HuddleFor.
        static JObject Draw(World w, Player me)
        {
            if (!CustomPlays.CanDraw(w, me)) return null;
            var c = w.Cfg.customPlays;
            var bases = new JArray();
            foreach (var p in Huddle.Playbook(w, me.Team)) if (CustomPlays.IsBase(w, me.Team, p)) bases.Add(p.id);
            if (bases.Count == 0) return null;
            return new JObject
            {
                ["bases"] = bases, ["receivers"] = new JArray(w.Cfg.play.receivers), ["maxPoints"] = c.maxPoints,
                ["maxAheadYd"] = c.maxAheadYd, ["maxBackYd"] = c.maxBackYd, ["maxZYd"] = w.Cfg.field.HalfWidth - c.sidelineInYd,
                ["name"] = c.name ?? "",
            };
        }

        // FEATURES.customPlays: the caller's "custom" press, {base, routes: [{spot, pts: [[ahead, z], ...]}], aid}, as
        // the sim's CustomRoutes (null when it is not that shape; CustomPlays.Lock checks the rest).
        public static List<CustomRoute> CustomRoutes(JObject m)
        {
            if (!(m["routes"] is JArray a)) return null;
            var list = new List<CustomRoute>();
            try
            {
                foreach (var r in a)
                {
                    if (!(r is JObject o) || !(o["spot"] is JValue sv) || sv.Type != JTokenType.Integer || !(o["pts"] is JArray pa)) return null;
                    var cr = new CustomRoute { spot = (int)sv };
                    foreach (var q in pa)
                    {
                        if (!(q is JArray xy) || xy.Count != 2 || !Num(xy[0]) || !Num(xy[1])) return null;
                        cr.pts.Add(new[] { (float)xy[0], (float)xy[1] });
                    }
                    list.Add(cr);
                }
            }
            catch (System.OverflowException) { return null; }   // a spot number too big for an int
            return list;
        }

        static bool Num(JToken t) => t.Type == JTokenType.Float || t.Type == JTokenType.Integer;

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
            var shown = Play(w, team, play);
            if (CustomPlays.IsCustom(w, team))   // FEATURES.customPlays: the drawn routes in place of the base play's
            {
                var jobs = (JArray)shown["jobs"];
                foreach (var kv in CustomPlays.Jobs(w, team)) if (kv.Key < jobs.Count) jobs[kv.Key] = JObject.FromObject(kv.Value);
                shown["name"] = w.Cfg.customPlays.name ?? "";
                shown["custom"] = true;
            }
            return PhoneProtocol.Text(new JObject
            {
                ["t"] = "job", ["side"] = Huddle.SideOf(w, team), ["play"] = shown,
                ["spots"] = Spots(w, team), ["opp"] = Spots(w, Other(w, team)), ["me"] = me.Spot,
            });
        }

        static int Other(World w, int team) => team == w.Offense ? w.Defense : w.Offense;

        // FEATURES.formations: each card carries where the team lines up for it ("spots", the same frame as Spots below)
        // and the formation's name, so the playbook picture is the line-up the TV will show.
        static JObject Play(World w, int team, PlayConfig p)
        {
            var o = JObject.FromObject(p);
            bool offense = team == w.Offense;
            var set = Formation.SetsOn(w) ? Formation.For(w.Cfg, p, offense) : null;
            if (set == null) { o.Remove("formation"); return o; }
            o["spots"] = SpotsJson(set, offense);
            o["formationName"] = w.Cfg.formation.sets[p.formation].name ?? "";
            return o;
        }

        static JObject Counts(World w, int team)
        {
            var o = new JObject();
            foreach (var kv in Huddle.Thumbs(w, team)) o[kv.Key] = kv.Value;
            return o;
        }

        // The team's formation in the play frame: [ahead, z] from the ball, ahead = the offense's direction.
        static JArray Spots(World w, int team) => SpotsJson(w.Spots(team), team == w.Offense);

        static JArray SpotsJson(System.Collections.Generic.List<SpotConfig> spots, bool offense)
        {
            var a = new JArray();
            foreach (var s in spots)
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

        // WS-05 SECRET: only for the offense QB's phone. call {shape, k, number, pos, ms}: this receiver is calling for it.
        public static string Call(World w, Player r) { int k = Receiving.ShapeOf(w, r); return PhoneProtocol.Text(new JObject { ["t"] = "call", ["k"] = k, ["shape"] = k >= 1 && k <= w.Cfg.play.shapes.Count ? w.Cfg.play.shapes[k - 1] : "", ["number"] = r.Number, ["pos"] = r.Pos, ["ms"] = (int)(w.Cfg.receiving.callPulseS * 1000f) }); }
    }
}
