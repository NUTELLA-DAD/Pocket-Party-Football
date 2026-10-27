using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // The stadium bowl (FEATURES.stadiumBowl) and the sunset (FEATURES.sunsetSky): the bowl's wall line is one closed,
    // unbroken loop that keeps clear of the field, the sideline people and the goal posts; its rows run parallel to
    // it; the crowd's wave goes all the way round; the sun comes from above at the configured bearing.
    public sealed class BowlTests
    {
        static BowlPath Path(GameConfig cfg) => Bowl.MakePath(cfg);

        [Test]
        public void TheWallLineIsAClosedUnbrokenLoop()
        {
            var cfg = ConfigTests.Load();
            var p = Path(cfg);
            var bc = cfg.stadium.bowl;
            foreach (float d in new[] { 0f, 10f, 35f })
            {
                Assert.Less(Vector3.Distance(p.At(0f, d), p.At(p.Length, d)), 1e-3f, "the loop closes at d " + d);
                var ts = p.Stations(bc.segM, bc.cornerDeg);
                float chord = 0f;
                for (int i = 0; i + 1 < ts.Count; i++)
                {
                    float gap = Vector3.Distance(p.At(ts[i], d), p.At(ts[i + 1], d));
                    Assert.Less(gap, bc.segM * p.Stretch(ts[i], d) + 0.01f, "no jump between pieces at t " + ts[i]);
                    chord += gap;
                }
                // the cut pieces add up to (just under) the row's true length
                Assert.That(chord, Is.InRange(p.LengthAt(d) * 0.99f, p.LengthAt(d) + 0.01f));
            }
        }

        [Test]
        public void RowsRunStraightOutFromTheWallLine()
        {
            var p = Path(ConfigTests.Load());
            for (float t = 0f; t < p.Length; t += 3.7f)
            {
                var n = p.Out(t);
                Assert.AreEqual(1f, n.magnitude, 1e-4f);
                Assert.AreEqual(0f, n.y, 1e-6f);
                var off = p.At(t, 12f) - p.At(t, 0f);
                Assert.Less(Vector3.Distance(off, n * 12f), 1e-3f, "t " + t);
            }
        }

        [Test]
        public void TheBowlKeepsClearOfTheFieldTheSidelinesAndThePosts()
        {
            var cfg = ConfigTests.Load();
            var p = Path(cfg);
            var f = cfg.field;
            var sc = cfg.stadium;
            float m = f.yardM;
            float side = (f.HalfWidth + f.sidelineBorderYd) * m, endX = f.HalfLength * m;
            Assert.AreEqual(side + sc.wallBackM, p.ZFar, 1e-4f, "the far wall stays where the old stand was");
            Assert.Greater(p.A, endX + sc.postBackM + sc.postThickM, "the goal posts stand inside the bowl");
            Assert.Greater(-p.ZNear, side + sc.nearRowM + sc.nearDepthM + 2.2f * sc.nearTentScale, "the near bench row and its tents fit");
            for (float t = 0f; t < p.Length; t += 1f)
            {
                var w = p.At(t, 0f);
                bool inside = Mathf.Abs(w.x) < endX && w.z > -side && w.z < side;
                Assert.IsFalse(inside, "the wall line crosses the field at t " + t);
            }
        }

        [Test]
        public void TheWaveGoesAllTheWayRoundTheBowl()
        {
            var cfg = ConfigTests.Load();
            var sc = cfg.stadium;
            int slices = Mathf.CeilToInt(Path(cfg).Length / sc.sliceM);
            int period = Mathf.RoundToInt(sc.waveEveryS * sc.fps);
            for (int s = 0; s < slices; s++)
            {
                bool up = false;
                for (int k = 0; k < period && !up; k++) up = Stadium.SlicePose(sc, k, s) == Stadium.Up;
                Assert.IsTrue(up, "slice " + s + " of " + slices + " never stands up");
            }
        }

        [Test]
        public void TheSunsetSunShinesDownFromItsBearing()
        {
            var s = ConfigTests.Load().sky;
            var light = SkyView.SunRotation(s) * Vector3.forward;   // the way the light travels
            Assert.Less(light.y, 0f, "the light comes from above");
            Assert.AreEqual(s.sunElevDeg, Mathf.Asin(-light.y) * Mathf.Rad2Deg, 0.01f);
            var toSun = SkyView.SunDirection(s, s.sunElevDeg);
            Assert.Less(Vector3.Distance(toSun, -light), 1e-4f, "the painted sun sits on the light's bearing");
            Assert.Greater(s.sunElevDeg, s.paintedElevDeg, "the painted sun sits low; the light comes from higher up (readable shadows)");
            Assert.Greater(s.fogStartM, 200f, "the haze never reaches the field or the stands");
        }

        [Test]
        public void BothSwitchesHaveTheirOverrides()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("stadiumBowl"));
            Assert.IsTrue(cfg.Feature("sunsetSky"));
            Assert.AreEqual("NO_STADIUM_BOWL", ConfigLoader.EnvName("stadiumBowl"));
            Assert.AreEqual("NO_SUNSET_SKY", ConfigLoader.EnvName("sunsetSky"));
            var off = ConfigTests.Load(new Dictionary<string, string> { ["NO_STADIUM_BOWL"] = "1", ["NO_SUNSET_SKY"] = "1" });
            Assert.IsFalse(off.Feature("stadiumBowl"));
            Assert.IsFalse(off.Feature("sunsetSky"));
        }
    }
}
