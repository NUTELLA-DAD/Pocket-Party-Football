using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // M1-f's gate: in a bot game, every phone layout appears, and each only at its moment.
    public sealed class PhoneLayoutTests
    {
        [Test]
        public void EveryLayoutAppearsAtTheRightMoment()
        {
            var cfg = ConfigTests.Load();
            var w = WorldTests.Full(cfg, 5);
            var seen = new SortedSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < cfg.sim.tickHz * 60 * 6; i++)
            {
                w.Step(null);
                var carrier = LivePlay.Carrier(w);
                var qb = LivePlay.Qb(w);
                foreach (var p in w.Players)
                {
                    string l = PhoneLayouts.For(w, p);
                    seen.Add(l);
                    switch (l)
                    {
                        case "huddle": Assert.AreEqual(Phase.Huddle, w.Phase); break;
                        case "wait": Assert.AreEqual(Phase.Whistle, w.Phase); break;
                        case "qbpre": Assert.AreEqual(Phase.Set, w.Phase); Assert.AreSame(qb, p, "only the QB gets HIKE"); break;
                        case "qb": Assert.AreEqual(Phase.Live, w.Phase); Assert.AreSame(carrier, p); Assert.AreSame(qb, p); Assert.IsFalse(w.Thrown); break;
                        case "carrier": Assert.AreEqual(Phase.Live, w.Phase); Assert.AreSame(carrier, p, "ball-carrier buttons only with the ball"); break;
                        default:
                            Assert.AreNotSame(carrier, p, l + " is not for the ball carrier in a live play");
                            Assert.AreEqual(PhoneLayouts.Role(p), l);
                            break;
                    }
                }
            }
            foreach (var l in PhoneLayouts.All) Assert.IsTrue(seen.Contains(l), "the " + l + " layout never appeared; seen: " + string.Join(", ", seen));
        }

        [Test]
        public void EveryPositionHasItsRole()
        {
            Assert.AreEqual("block", PhoneLayouts.Role(new Player { Pos = "OL" }));
            Assert.AreEqual("block", PhoneLayouts.Role(new Player { Pos = "TE" }));
            Assert.AreEqual("route", PhoneLayouts.Role(new Player { Pos = "WR" }));
            Assert.AreEqual("route", PhoneLayouts.Role(new Player { Pos = "RB" }));
            Assert.AreEqual("rush", PhoneLayouts.Role(new Player { Pos = "DL" }));
            Assert.AreEqual("lb", PhoneLayouts.Role(new Player { Pos = "LB" }));
            Assert.AreEqual("cover", PhoneLayouts.Role(new Player { Pos = "CB" }));
            Assert.AreEqual("cover", PhoneLayouts.Role(new Player { Pos = "S" }));
        }
    }
}
