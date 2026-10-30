using System.Collections;
using System.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PocketFootball.Boot;
using PocketFootball.Sim;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketFootball.Tests.PlayMode
{
    // M1-b's gate: a team's plays and calls go ONLY to that team's phones. An Arrows phone (offense, the QB) and a
    // Grains phone (defense, the middle LB) both call a play; neither phone, and not the TV line, ever carries
    // the other team's play ids or names.
    public sealed class PlaySecrecyTests
    {
        [SetUp] public void Loopback() { PartyHost.BindOverride = IPAddress.Loopback; PartyHost.PortOverride = 0; }
        [TearDown] public void Reset() { PartyHost.BindOverride = null; PartyHost.PortOverride = -1; }

        [UnityTest]
        public IEnumerator TheOtherTeamNeverGetsThePlay()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            int port = host.Server.Port;
            int off = w.Offense, def = w.Defense;

            var arrows = new FakePhone();
            var grains = new FakePhone();
            yield return arrows.JoinAndPick(port, host.RoomCode, "Kay", off);
            yield return grains.JoinAndPick(port, host.RoomCode, "Ray", def);

            // a fresh huddle with both people in it
            Huddle.Begin(w);
            yield return arrows.Settle();
            yield return grains.Settle();
            var huddleC = arrows.Latest["huddle"];
            var huddleR = grains.Latest["huddle"];
            Assert.AreEqual("huddle", (string)arrows.Latest["ui"]["layout"]);
            Assert.AreEqual("offense", (string)huddleC["side"]);
            Assert.AreEqual("defense", (string)huddleR["side"]);
            Assert.IsTrue((bool)huddleC["caller"], "the Arrows person is the QB, so the caller");
            Assert.IsTrue((bool)huddleR["caller"], "the Grains person is the middle LB, so the caller");

            // each side calls; the call carries the huddle screen's aid
            arrows.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = arrows.Aid });
            JObject status = null;
            yield return grains.Expect("status", m => status = m, m => (bool)m["called"][off]);
            Assert.AreEqual("huddle", (string)status["phase"]);
            Assert.IsTrue((bool)status["called"][off], "the defense sees that the offense called...");
            StringAssert.Contains("CALLED", PartyHost.TvLine(w, GameBootstrap.Instance.Config));
            grains.Send(new JObject { ["t"] = "lock", ["id"] = "mblitz", ["aid"] = grains.Aid });

            JObject jobC = null, jobR = null;
            yield return arrows.Expect("job", m => jobC = m);
            yield return grains.Expect("job", m => jobR = m);
            Assert.AreEqual("slants", (string)jobC["play"]["id"]);
            Assert.AreEqual("mblitz", (string)jobR["play"]["id"]);
            Assert.AreEqual(Phase.Set, w.Phase);
            for (int i = 0; i < 10; i++) yield return null;

            // ...but never what: no offense play id or name ever reached the defense, and the other way round
            var cfg = GameBootstrap.Instance.Config;
            foreach (var p in cfg.plays)
            {
                var leaksTo = p.side == "offense" ? grains : arrows;
                foreach (var text in leaksTo.All)
                {
                    StringAssert.DoesNotContain("\"" + p.id + "\"", text, "the " + p.side + " play id '" + p.id + "' reached the other team");
                    StringAssert.DoesNotContain(p.name, text, "the " + p.side + " play name '" + p.name + "' reached the other team");
                }
            }
            Assert.Greater(grains.All.Count, 5, "the defense phone did get its own messages");

            // and the TV never shows a play
            Huddle.Begin(w);
            Assert.IsTrue(Huddle.Lock(w, Huddle.Caller(w, off).Slot, "slants"));
            string tv = PartyHost.TvLine(w, cfg);
            foreach (var p in cfg.plays) { StringAssert.DoesNotContain(p.name, tv); StringAssert.DoesNotContain(p.id, tv); }

            arrows.Dispose();
            grains.Dispose();
        }

        // WS-13: an audible and a hot route stay on their own team too. The Arrows QB audibles to another play, the
        // Arrows RB takes a hot route, the Grains' middle LB audibles back; no phone of the other team, and not
        // the TV line, ever carries one of those play ids or names, or a hot route id or name.
        [UnityTest]
        public IEnumerator TheOtherTeamNeverGetsAnAudibleOrAHotRoute()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            var cfg = GameBootstrap.Instance.Config;
            bool was = cfg.Feature("audibles");
            cfg.features["audibles"] = true;
            int port = host.Server.Port;
            int off = w.Offense, def = w.Defense;

            var qb = new FakePhone();
            var rb = new FakePhone();
            var grains = new FakePhone();
            yield return qb.JoinAndPick(port, host.RoomCode, "Kay", off);
            yield return rb.JoinAndPick(port, host.RoomCode, "Bo", off);
            yield return grains.JoinAndPick(port, host.RoomCode, "Ray", def);

            // both sides call in the huddle; then everyone is at the line with a human QB, so nobody hikes for a while
            Huddle.Begin(w);
            yield return qb.Settle();
            qb.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = qb.Aid });
            yield return grains.Settle();
            grains.Send(new JObject { ["t"] = "lock", ["id"] = "cover3", ["aid"] = grains.Aid });
            JObject offer = null, hotOffer = null, defOffer = null;
            yield return qb.Expect("audible", m => offer = m, m => (bool)m["open"]);
            yield return rb.Expect("audible", m => hotOffer = m, m => (bool)m["open"]);
            yield return grains.Expect("audible", m => defOffer = m, m => (bool)m["open"]);
            Assert.AreEqual(Phase.Set, w.Phase);
            Assert.IsTrue((bool)offer["caller"], "the QB may audible");
            Assert.AreEqual(1, (int)offer["left"]);
            Assert.AreEqual(2, ((JArray)hotOffer["hot"]).Count, "the RB gets the two hot routes");
            Assert.IsTrue((bool)defOffer["caller"], "the middle LB may audible");

            // the QB audibles (with the aid of the screen on show), the RB takes a hot route, the defense audibles
            qb.Send(new JObject { ["t"] = "audible", ["id"] = "verts", ["aid"] = qb.Aid });
            JObject job = null;
            yield return rb.Expect("job", m => job = m, m => (string)m["play"]["id"] == "verts");
            Assert.AreEqual("verts", w.Calls[off]);
            rb.Send(new JObject { ["t"] = "hot", ["id"] = "hot_go", ["aid"] = rb.Aid });
            JObject told = null;
            yield return qb.Expect("audible", m => told = m, m => ((JArray)m["routes"]).Count > 0);
            Assert.AreEqual("hot_go", (string)told["routes"][0]["id"], "the QB is told about his RB's hot route");
            var rbPlayer = LivePlay.SpotOf(w, off, 1);
            Assert.IsTrue(rbPlayer.Human, "the second Arrows phone plays the RB");
            Assert.AreEqual("hot_go", Audibles.RouteOf(w, rbPlayer));
            grains.Send(new JObject { ["t"] = "audible", ["id"] = "mblitz", ["aid"] = grains.Aid });
            yield return grains.Expect("job", m => { }, m => (string)m["play"]["id"] == "mblitz");
            Assert.AreEqual("mblitz", w.Calls[def]);
            Assert.AreEqual(Phase.Set, w.Phase, "all of it before the snap");
            for (int i = 0; i < 10; i++) yield return null;

            // nothing crossed over
            foreach (var p in cfg.plays)
                foreach (var phone in p.side == "offense" ? new[] { grains } : new[] { qb, rb })
                    foreach (var text in phone.All)
                    {
                        StringAssert.DoesNotContain("\"" + p.id + "\"", text, "the " + p.side + " play id '" + p.id + "' reached the other team");
                        StringAssert.DoesNotContain(p.name, text, "the " + p.side + " play name '" + p.name + "' reached the other team");
                    }
            foreach (var r in cfg.audibles.hotRoutes)
                foreach (var text in grains.All)
                {
                    StringAssert.DoesNotContain(r.id, text, "the hot route id '" + r.id + "' reached the defense");
                    StringAssert.DoesNotContain(r.name, text, "the hot route name '" + r.name + "' reached the defense");
                }
            foreach (var text in grains.All) StringAssert.DoesNotContain("\"routes\":[{", text, "the defense never hears whose route changed");
            string tv = PartyHost.TvLine(w, cfg);
            foreach (var p in cfg.plays) { StringAssert.DoesNotContain(p.name, tv); StringAssert.DoesNotContain(p.id, tv); }
            foreach (var r in cfg.audibles.hotRoutes) { StringAssert.DoesNotContain(r.name, tv); StringAssert.DoesNotContain(r.id, tv); }

            cfg.features["audibles"] = was;
            qb.Dispose();
            rb.Dispose();
            grains.Dispose();
        }

        // FEATURES.customPlays: a drawn play stays on its own team. The Arrows QB draws a route for the RB on top of
        // Four Verticals and locks it in; the Arrows RB's job card carries the drawn route, the Grains phone and the TV
        // only ever hear "called" (never the base play, the drawing, its points, or that it was drawn at all).
        [UnityTest]
        public IEnumerator TheOtherTeamNeverGetsADrawnPlay()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            var cfg = GameBootstrap.Instance.Config;
            bool was = cfg.Feature("customPlays");
            cfg.features["customPlays"] = true;
            int port = host.Server.Port;
            int off = w.Offense, def = w.Defense;

            var qb = new FakePhone();
            var rb = new FakePhone();
            var grains = new FakePhone();
            yield return qb.JoinAndPick(port, host.RoomCode, "Kay", off);
            yield return rb.JoinAndPick(port, host.RoomCode, "Bo", off);
            yield return grains.JoinAndPick(port, host.RoomCode, "Ray", def);

            Huddle.Begin(w);
            yield return qb.Settle();
            yield return rb.Settle();
            yield return grains.Settle();
            Assert.AreEqual(JTokenType.Object, qb.Latest["huddle"]["draw"]?.Type, "the QB is the caller: he may draw");
            Assert.AreNotEqual(JTokenType.Object, rb.Latest["huddle"]["draw"]?.Type, "the RB may not");
            Assert.AreNotEqual(JTokenType.Object, grains.Latest["huddle"]["draw"]?.Type, "nor the defense");

            // a stale screen's press does nothing; the right aid locks it in
            var route = new JArray { new JObject { ["spot"] = 1, ["pts"] = new JArray { new JArray(3.25f, 7.75f), new JArray(17.25f, 7.75f) } } };
            qb.Send(new JObject { ["t"] = "custom", ["base"] = "verts", ["routes"] = route, ["aid"] = "old1" });
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(Huddle.IsLocked(w, off), "a drawn play from an old screen does nothing");
            qb.Send(new JObject { ["t"] = "custom", ["base"] = "verts", ["routes"] = route, ["aid"] = qb.Aid });
            yield return grains.Expect("status", m => { }, m => (bool)m["called"][off]);
            Assert.AreEqual("verts", w.Calls[off]);
            Assert.IsTrue(CustomPlays.IsCustom(w, off));
            grains.Send(new JObject { ["t"] = "lock", ["id"] = "cover3", ["aid"] = grains.Aid });

            JObject job = null;
            yield return rb.Expect("job", m => job = m);
            Assert.IsTrue((bool)job["play"]["custom"]);
            Assert.AreEqual(cfg.customPlays.name, (string)job["play"]["name"]);
            Assert.AreEqual(17.25f, (float)job["play"]["jobs"][1]["pts"][1][0], 1e-3f, "the RB's job card is the drawn route");
            yield return grains.Expect("job", m => { });
            for (int i = 0; i < 10; i++) yield return null;

            foreach (var text in grains.All)
            {
                StringAssert.DoesNotContain("\"verts\"", text, "the base play reached the defense");
                StringAssert.DoesNotContain(cfg.customPlays.name, text, "the defense heard it was a drawn play");
                StringAssert.DoesNotContain(cfg.customPlays.text, text);
                StringAssert.DoesNotContain("17.25", text, "the drawn points reached the defense");
                StringAssert.DoesNotContain("\"bases\"", text);
            }
            string tv = PartyHost.TvLine(w, cfg);
            StringAssert.DoesNotContain(cfg.customPlays.name, tv);
            StringAssert.DoesNotContain("verts", tv);

            cfg.features["customPlays"] = was;
            qb.Dispose();
            rb.Dispose();
            grains.Dispose();
        }

        [UnityTest]
        public IEnumerator AStalePressIsIgnored()
        {
            SceneManager.LoadScene("Boot");
            for (int i = 0; i < 10; i++) yield return null;
            var host = PartyHost.Instance;
            var w = host.World;
            var arrows = new FakePhone();
            yield return arrows.JoinAndPick(host.Server.Port, host.RoomCode, "Kay", w.Offense);
            Huddle.Begin(w);
            yield return arrows.Settle();
            arrows.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = "walk1" });
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(Huddle.IsLocked(w, w.Offense), "a lock from an old screen does nothing");
            arrows.Send(new JObject { ["t"] = "lock", ["id"] = "slants", ["aid"] = arrows.Aid });
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(Huddle.IsLocked(w, w.Offense));
            arrows.Dispose();
        }
    }
}
