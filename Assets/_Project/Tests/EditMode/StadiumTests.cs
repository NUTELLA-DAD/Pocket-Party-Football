using NUnit.Framework;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // WS-10: the card crowd moves only on stepped key frames (no wobble), and its wave crosses the whole stand.
    public sealed class StadiumTests
    {
        [Test]
        public void CrowdKeysAreStepped()
        {
            var sc = ConfigTests.Load().stadium;
            Assert.AreEqual(8f, sc.fps);
            Assert.AreEqual(0, Stadium.CrowdKey(0f, sc.fps));
            Assert.AreEqual(0, Stadium.CrowdKey(0.12f, sc.fps));
            Assert.AreEqual(1, Stadium.CrowdKey(0.13f, sc.fps));
            Assert.AreEqual(80, Stadium.CrowdKey(10.01f, sc.fps));
        }

        [Test]
        public void TheWaveCrossesEverySliceOncePerPeriod()
        {
            var sc = ConfigTests.Load().stadium;
            int slices = (int)System.Math.Ceiling(sc.halfLengthM * 2f / sc.sliceM);
            int period = (int)System.Math.Round(sc.waveEveryS * sc.fps);
            for (int s = 0; s < slices; s++)
            {
                int up = 0;
                for (int k = 0; k < period; k++)
                {
                    int p = Stadium.SlicePose(sc, k, s);
                    Assert.That(p, Is.InRange(0, Stadium.Poses - 1));
                    if (p == Stadium.Up) up++;
                }
                Assert.Greater(up, 0, "slice " + s + " never stands up");
                // the wave comes round again every period
                Assert.AreEqual(Stadium.SlicePose(sc, 3, s) == Stadium.Up, Stadium.SlicePose(sc, 3 + period, s) == Stadium.Up);
            }
        }

        [Test]
        public void IdleSlicesDoNotAllFlipTogether()
        {
            var sc = ConfigTests.Load().stadium;
            int key = (int)System.Math.Round(sc.waveEveryS * sc.fps) - 1;   // just before the next wave: everyone idle
            int a = 0, b = 0;
            for (int s = 0; s < 40; s++)
            {
                int p = Stadium.SlicePose(sc, key, s);
                if (p == Stadium.IdleA) a++;
                else if (p == Stadium.IdleB) b++;
            }
            Assert.Greater(a, 0);
            Assert.Greater(b, 0);
        }
    }
}
