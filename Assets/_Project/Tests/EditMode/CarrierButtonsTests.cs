using NUnit.Framework;
using PocketFootball.Input;
using PocketFootball.Sim;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    // WS-04: the carrier screen's SPIN, STIFF ARM and DIVE buttons reach the sim as PlayerInput.Move.
    public sealed class CarrierButtonsTests
    {
        static PhoneSource Phone() => new PhoneSource(new Seat { Slot = 0 }) { Aid = "carrier1" };

        [Test]
        public void EachButtonIsItsMove()
        {
            var src = Phone();
            Assert.IsTrue(src.OnButton("spin", true, "carrier1"));
            Assert.AreEqual(CarrierMove.Spin, src.Poll().Move);
            Assert.IsTrue(src.OnButton("stiff", true, "carrier1"));
            Assert.AreEqual(CarrierMove.StiffArm, src.Poll().Move);
            Assert.IsTrue(src.OnButton("divein", true, "carrier1"));
            Assert.AreEqual(CarrierMove.Dive, src.Poll().Move);
        }

        [Test]
        public void APressIsTakenOnceAndTheReleaseDoesNothing()
        {
            var src = Phone();
            src.OnButton("spin", true, "carrier1");
            src.OnButton("spin", false, "carrier1");
            Assert.AreEqual(CarrierMove.Spin, src.Poll().Move, "a quick tap between two ticks still counts");
            Assert.AreEqual(CarrierMove.None, src.Poll().Move, "taken once");
        }

        [Test]
        public void SpinIsNoLongerAJukeAndDiveTackleIsStillATackle()
        {
            var src = Phone();
            src.OnButton("spin", true, "carrier1");
            Assert.IsFalse(src.Poll().Juke);
            src.OnButton("dive", true, "carrier1");
            var i = src.Poll();
            Assert.IsTrue(i.Tackle, "the defense's DIVE TACKLE");
            Assert.AreEqual(CarrierMove.None, i.Move);
        }

        [Test]
        public void AStaleScreenIsIgnored()
        {
            var src = Phone();
            Assert.IsFalse(src.OnButton("stiff", true, "route3"));
            Assert.AreEqual(CarrierMove.None, src.Poll().Move);
        }
    }
}
