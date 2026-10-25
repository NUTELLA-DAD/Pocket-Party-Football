using Newtonsoft.Json.Linq;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Boot
{
    // Pocket Football's own phone messages (on top of PhoneProtocol's join / err / pong / stick / btn).
    //   game -> phone: joined {token, slot, name, code, team, teams}, lobby {teams}, picked {team, number, pos, color},
    //                  ui {layout: pick | walk, aid},
    //                  me {st, tired, oob} (stamina 0-100)
    //   phone -> game: pick {team}
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
