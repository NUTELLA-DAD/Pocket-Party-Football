using System.Collections;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using PocketParty.Net;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // WS-07's gate (FEATURES.lobby): two phones pick an offense and a defense spot, swap one, drop and rejoin into the
    // same spots while a bot covers; 22 phones fill both teams both ways with no bots left.
    public sealed class LobbyTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; Lobby.HoldOverride = true; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; Lobby.HoldOverride = null; }

        static IEnumerator Boot()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            GameBootstrap.Instance.Config.features["lobby"] = true;   // ships off; on for these tests
            yield return null;
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static bool IsLobby(JObject m) => (string)m["layout"] == "lobby";

        static Seat SeatNamed(string name)
        {
            foreach (var s in PartyHost.Instance.Roster.Seats) if (s.Name == name) return s;
            return null;
        }

        static void AssertEverySpotOnce(World w, int team)
        {
            foreach (bool offense in new[] { true, false })
            {
                var seen = new HashSet<int>();
                foreach (var p in w.Players)
                    if (p.Team == team) Assert.IsTrue(seen.Add(Formation.SpotOn(w, p, offense)), "two players on one spot");
                Assert.AreEqual(Formation.SpotCount(w, offense), seen.Count);
            }
        }

        [UnityTest]
        public IEnumerator TwoPhonesPickSwapDropAndRejoinIntoTheSameSpots()
        {
            yield return Boot();
            var host = PartyHost.Instance;
            var w = host.World;
            var lobby = Lobby.Instance;
            Assert.IsNotNull(lobby, "no Lobby on the host");
            Assert.IsTrue(lobby.Holding, "the game waits on the lobby until kickoff");
            int port = host.Server.Port, team = w.Offense;
            var off = GameBootstrap.Instance.Config.formation.offense;

            // Kay joins and picks the Chiefs: the lobby screen comes up
            var kay = new FakePhone();
            yield return FakePhone.Wait(kay.Connect(port));
            kay.Send(new JObject { ["t"] = "join", ["code"] = host.RoomCode, ["name"] = "Kay", ["token"] = "", ["pid"] = "kay" });
            JObject joined = null;
            yield return kay.Expect("joined", m => joined = m);
            string token = (string)joined["token"];
            int kaySlot = (int)joined["slot"];
            kay.Send(new JObject { ["t"] = "pick", ["team"] = team });
            yield return kay.Expect("ui", _ => { }, IsLobby);
            JObject lineup = null;
            yield return kay.Expect("lineup", m => lineup = m);
            Assert.IsTrue((bool)lineup["held"]);
            Assert.AreEqual(11, ((JArray)lineup["offense"]).Count);
            Assert.AreEqual(11, ((JArray)lineup["defense"]).Count);

            // Kay takes WR (offense spot 3) and CB (defense spot 1) from the bots
            kay.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = 3, ["aid"] = kay.Aid });
            kay.Send(new JObject { ["t"] = "spot", ["side"] = "d", ["spot"] = 1, ["aid"] = kay.Aid });
            yield return kay.Settle();
            var k = w.Find(kaySlot);
            Assert.AreEqual(3, Formation.SpotOn(w, k, true));
            Assert.AreEqual(1, Formation.SpotOn(w, k, false));
            Assert.AreEqual(3, k.Spot, "lined up on the offense spot (the team has the ball)");
            Assert.AreEqual(off[3].number, k.Number);
            Assert.AreEqual(3, (int)kay.Latest["lineup"]["you"]["o"]);
            AssertEverySpotOnce(w, team);

            // a press from an old screen does nothing
            kay.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = 4, ["aid"] = "lobby0" });
            yield return Frames(10);
            Assert.AreEqual(3, Formation.SpotOn(w, k, true));

            // Ray joins the same team; Kay's screen changed with the pick, so it asks for the lobby back
            var ray = new FakePhone();
            yield return ray.JoinAndPick(port, host.RoomCode, "Ray", team);
            yield return ray.Expect("ui", _ => { }, IsLobby);
            yield return kay.Settle();
            kay.Send(new JObject { ["t"] = "lobbyme" });
            yield return kay.Expect("ui", _ => { }, IsLobby);
            var r = w.Find(SeatNamed("RAY").Slot);
            int rayOld = Formation.SpotOn(w, r, true);
            Assert.AreNotEqual(3, rayOld);

            // Ray wants Kay's WR spot: Kay is asked, says yes, and they swap
            yield return ray.Settle();
            ray.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = 3, ["aid"] = ray.Aid });
            JObject asked = null;
            yield return kay.Expect("lineup", m => asked = m, m => m["ask"] != null);
            Assert.AreEqual("RAY", (string)asked["ask"]["from"]);
            Assert.AreEqual(off[3].pos + " #" + off[3].number, (string)asked["ask"]["give"]);
            Assert.AreEqual(3, Formation.SpotOn(w, k, true), "nothing moves until Kay answers");
            kay.Send(new JObject { ["t"] = "swap", ["yes"] = true, ["aid"] = kay.Aid });
            yield return kay.Settle();
            Assert.AreEqual(rayOld, Formation.SpotOn(w, k, true));
            Assert.AreEqual(3, Formation.SpotOn(w, r, true));
            Assert.AreEqual(1, Formation.SpotOn(w, k, false), "the defense spot stays put");
            AssertEverySpotOnce(w, team);

            // Kay's screen locks: a bot covers the same player, on the same spots
            kay.Dispose();
            yield return Frames(30);
            Assert.IsFalse(host.Roster[kaySlot].Connected);
            Assert.AreSame(k, w.Find(kaySlot), "the player stays in the game");
            Assert.IsFalse(k.Human, "a bot plays for the dropped phone");
            Assert.AreEqual(rayOld, Formation.SpotOn(w, k, true));
            Assert.AreEqual(1, Formation.SpotOn(w, k, false));
            yield return ray.Settle();
            Assert.IsTrue((bool)ray.Latest["lineup"]["offense"][rayOld]["away"], "Ray's lobby shows Kay away");

            // a new phone joining now (Kay's player looks like a bot) does not take Kay's place or spots
            var zed = new FakePhone();
            yield return zed.JoinAndPick(port, host.RoomCode, "Zed", team);
            yield return Frames(10);
            k = w.Find(kaySlot);
            Assert.IsNotNull(k, "Kay still has a player");
            Assert.AreEqual(team, k.Team);
            Assert.IsFalse(k.Human);
            Assert.AreEqual(rayOld, Formation.SpotOn(w, k, true));
            Assert.AreEqual(1, Formation.SpotOn(w, k, false));
            Assert.AreEqual(11, w.TeamCount(team));
            AssertEverySpotOnce(w, team);

            // Kay comes back with the saved token: same seat, same player, same two spots, a person again
            var kay2 = new FakePhone();
            yield return FakePhone.Wait(kay2.Connect(port));
            kay2.Send(new JObject { ["t"] = "join", ["code"] = host.RoomCode, ["name"] = "Kay", ["token"] = token, ["pid"] = "kay" });
            JObject again = null;
            yield return kay2.Expect("joined", m => again = m);
            Assert.AreEqual(kaySlot, (int)again["slot"]);
            yield return kay2.Expect("ui", _ => { }, IsLobby);
            JObject back = null;
            yield return kay2.Expect("lineup", m => back = m);
            Assert.AreEqual(rayOld, (int)back["you"]["o"]);
            Assert.AreEqual(1, (int)back["you"]["d"]);
            yield return Frames(5);
            Assert.AreSame(k, w.Find(kaySlot));
            Assert.IsTrue(k.Human);

            // the lineup is team-only: the other team never hears Kay's name
            var other = new FakePhone();
            yield return other.JoinAndPick(port, host.RoomCode, "Oz", w.Defense);
            yield return other.Settle();
            foreach (var text in other.All) if (text.Contains("\"lineup\"")) StringAssert.DoesNotContain("KAY", text);

            // kickoff: the game runs again and the phones go to the huddle
            int tick = w.Tick;
            yield return kay2.Settle();
            kay2.Send(new JObject { ["t"] = "kickoff", ["aid"] = kay2.Aid });
            yield return kay2.Expect("ui", _ => { }, m => (string)m["layout"] == "huddle");
            Assert.IsFalse(lobby.Holding);
            yield return Frames(10);
            Assert.Greater(w.Tick, tick, "the sim steps after kickoff");
            Assert.AreEqual(rayOld, k.Spot, "Kay lines up on the offense spot after the swap");

            foreach (var p in new[] { ray, zed, kay2, other }) p.Dispose();
        }

        // FEATURES.huddleSwitch: after kickoff a person picks a new position from the huddle screen. A bot's spot is
        // theirs at once and every huddle screen of the team comes again with the new spots; a person's spot asks them.
        [UnityTest]
        public IEnumerator SwitchPositionFromTheHuddle()
        {
            yield return Boot();
            var host = PartyHost.Instance;
            var w = host.World;
            var cfg = GameBootstrap.Instance.Config;
            cfg.features["huddleSwitch"] = true;
            int port = host.Server.Port, team = w.Offense;

            var kay = new FakePhone();
            yield return kay.JoinAndPick(port, host.RoomCode, "Kay", team);
            var ray = new FakePhone();
            yield return ray.JoinAndPick(port, host.RoomCode, "Ray", team);
            var oz = new FakePhone();
            yield return oz.JoinAndPick(port, host.RoomCode, "Oz", w.Defense);
            yield return kay.Settle();
            kay.Send(new JObject { ["t"] = "lobbyme" });
            yield return kay.Expect("ui", _ => { }, IsLobby);
            string lobbyAid = kay.Aid;
            kay.Send(new JObject { ["t"] = "kickoff", ["aid"] = lobbyAid });
            yield return kay.Expect("ui", _ => { }, m => (string)m["layout"] == "huddle");
            yield return Frames(5);
            Assert.AreEqual(Phase.Huddle, w.Phase);
            yield return kay.Settle();
            yield return ray.Settle();
            Assert.IsTrue((bool)kay.Latest["lineup"]["switch"], "the huddle screen gets the spots to switch to");

            var k = w.Find(SeatNamed("KAY").Slot);
            var r = w.Find(SeatNamed("RAY").Slot);
            int to = -1;
            for (int i = 0; i < Formation.SpotCount(w, true) && to < 0; i++)
            {
                var h = Formation.Holder(w, team, true, i);
                if (h != k && h != r) to = i;
            }

            // a press from the old lobby screen does nothing; the huddle screen's press moves Kay at once
            kay.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = to, ["aid"] = lobbyAid });
            yield return Frames(10);
            Assert.AreNotEqual(to, Formation.SpotOn(w, k, true));
            kay.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = to, ["aid"] = kay.Aid });
            yield return kay.Settle();
            Assert.AreEqual(to, Formation.SpotOn(w, k, true));
            Assert.AreEqual(to, k.Spot, "relabelled in the huddle");
            Assert.AreEqual(cfg.formation.offense[to].number, k.Number);
            Assert.AreEqual(to, (int)kay.Latest["huddle"]["me"], "the huddle screen comes again with the new spot");
            Assert.AreEqual(to, (int)kay.Latest["lineup"]["you"]["o"]);
            AssertEverySpotOnce(w, team);

            // Ray asks for Kay's new spot from the huddle: Kay is asked, says yes, they swap
            yield return ray.Settle();
            int rayOld = Formation.SpotOn(w, r, true);
            ray.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = to, ["aid"] = ray.Aid });
            yield return kay.Expect("lineup", _ => { }, m => m["ask"] != null);
            kay.Send(new JObject { ["t"] = "swap", ["yes"] = true, ["aid"] = kay.Aid });
            yield return kay.Settle();
            Assert.AreEqual(rayOld, Formation.SpotOn(w, k, true));
            Assert.AreEqual(to, Formation.SpotOn(w, r, true));
            AssertEverySpotOnce(w, team);

            // team-only: the other team never hears Kay's name
            yield return oz.Settle();
            foreach (var text in oz.All) if (text.Contains("\"lineup\"")) StringAssert.DoesNotContain("KAY", text);

            // NO_HUDDLE_SWITCH: the press does nothing
            cfg.features["huddleSwitch"] = false;
            yield return kay.Settle();
            int before = Formation.SpotOn(w, k, true);
            kay.Send(new JObject { ["t"] = "spot", ["side"] = "o", ["spot"] = to == 0 ? 1 : 0, ["aid"] = kay.Aid });
            yield return Frames(10);
            Assert.AreEqual(before, Formation.SpotOn(w, k, true));
            Assert.AreEqual("NO_HUDDLE_SWITCH", ConfigLoader.EnvName("huddleSwitch"));

            foreach (var p in new[] { kay, ray, oz }) p.Dispose();
        }

        [UnityTest]
        public IEnumerator TwentyTwoPhonesFillBothSides()
        {
            yield return Boot();
            var host = PartyHost.Instance;
            var w = host.World;
            int port = host.Server.Port;
            var phones = new List<FakePhone>();
            for (int i = 0; i < 22; i++)
            {
                var ph = new FakePhone();
                yield return ph.JoinAndPick(port, host.RoomCode, "P" + i, i % 2);
                phones.Add(ph);
            }
            yield return Frames(10);
            Assert.AreEqual(22, w.Players.Count);
            foreach (var p in w.Players) Assert.IsTrue(p.Human, "no bots left: " + p.Slot);
            for (int team = 0; team < 2; team++)
            {
                Assert.AreEqual(11, host.Roster.TeamCount(team, true));
                AssertEverySpotOnce(w, team);
            }

            // every spot on the lobby screen is a person
            yield return phones[0].Settle();
            var lineup = phones[0].Latest["lineup"];
            foreach (var side in new[] { "offense", "defense" })
                foreach (var s in (JArray)lineup[side]) Assert.IsTrue((bool)s["person"], side + " " + s["pos"] + " is a bot");

            // two people on a full team swap defense spots (their screens changed as teammates picked: lobby back)
            foreach (var ph in new[] { phones[0], phones[2] })
            {
                yield return ph.Settle();
                ph.Send(new JObject { ["t"] = "lobbyme" });
                yield return ph.Expect("ui", _ => { }, IsLobby);
            }
            var a = w.Find(SeatNamed("P0").Slot);
            var b = w.Find(SeatNamed("P2").Slot);
            int aD = Formation.SpotOn(w, a, false), bD = Formation.SpotOn(w, b, false);
            phones[0].Send(new JObject { ["t"] = "spot", ["side"] = "d", ["spot"] = bD, ["aid"] = phones[0].Aid });
            yield return phones[2].Expect("lineup", _ => { }, m => m["ask"] != null);
            phones[2].Send(new JObject { ["t"] = "swap", ["yes"] = true, ["aid"] = phones[2].Aid });
            yield return Frames(15);
            Assert.AreEqual(bD, Formation.SpotOn(w, a, false));
            Assert.AreEqual(aD, Formation.SpotOn(w, b, false));
            AssertEverySpotOnce(w, 0);

            // a 23rd phone is told the game is full
            var late = new FakePhone();
            yield return FakePhone.Wait(late.Connect(port));
            late.Send(new JObject { ["t"] = "join", ["code"] = host.RoomCode, ["name"] = "Late", ["token"] = "", ["pid"] = "late" });
            yield return late.Expect("err", _ => { });
            late.Dispose();
            foreach (var p in phones) p.Dispose();
        }
    }
}
