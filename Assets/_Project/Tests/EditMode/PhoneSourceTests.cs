using NUnit.Framework;
using PocketFootball.Input;
using PocketParty.Net;

namespace PocketFootball.Tests.EditMode
{
    public sealed class PhoneSourceTests
    {
        [Test]
        public void StaleAidIsIgnored()
        {
            var src = new PhoneSource(new Seat { Slot = 0 }) { Aid = "walk2" };
            Assert.IsFalse(src.OnButton("find", true, "pick1"));
            Assert.IsFalse(src.TakeFind());
            Assert.IsTrue(src.OnButton("find", true, "walk2"));
            Assert.IsTrue(src.TakeFind());
            Assert.IsFalse(src.TakeFind(), "a press is taken once");
        }

        [Test]
        public void StickMapsScreenToField()
        {
            var src = new PhoneSource(new Seat { Slot = 0 });
            src.OnStick(1f, -1f, 10f);                  // right and up on the phone
            var i = src.Poll();
            Assert.AreEqual(1f, i.MoveX);                // along the field, to the right on the TV
            Assert.AreEqual(1f, i.MoveZ);                // away from the camera
            src.ExpireStick(10.1f, 0.5f);
            Assert.AreEqual(1f, src.Poll().MoveX, "fresh stick keeps going");
            src.ExpireStick(11f, 0.5f);
            Assert.AreEqual(0f, src.Poll().MoveX, "a quiet phone stops");
        }
    }
}
