using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // The golden-hour stadium (FEATURES.goldenStadium): game.json "stadiumLook" laid over the config turns on the roofs,
    // the skyline, the team end zones and the golden sky; switched off, the config is exactly the old one.
    public sealed class StadiumLookTests
    {
        static GameConfig Golden()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(StadiumLook.Apply(cfg), "the look is on by default");
            return cfg;
        }

        [Test]
        public void TheSwitchHasItsOverride()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("goldenStadium"));
            Assert.AreEqual("NO_GOLDEN_STADIUM", ConfigLoader.EnvName("goldenStadium"));
            var off = ConfigTests.Load(new Dictionary<string, string> { ["NO_GOLDEN_STADIUM"] = "1" });
            string sky = off.sky.zenithColor;
            Assert.IsFalse(StadiumLook.Apply(off));
            Assert.AreEqual(sky, off.sky.zenithColor, "switched off, the sunset sky stays");
            Assert.AreEqual(0f, off.stadium.bowl.roofDepthM, "no roofs");
            Assert.AreEqual(0, off.stadium.bowl.skylineBuildings, "no skyline");
            Assert.IsNull(FieldView.EndZoneTeam(off, 1), "plain end zones");
            Assert.Greater(off.stadium.bowl.towerHeightM, 0f, "the light towers stay");
        }

        [Test]
        public void TheLookTurnsOnRoofsSkylineAndTeamEndZones()
        {
            var cfg = Golden();
            var bc = cfg.stadium.bowl;
            Assert.Greater(bc.roofDepthM, 0f);
            Assert.Greater(bc.skylineBuildings, 0);
            Assert.AreEqual("round", bc.treeStyle);
            Assert.IsTrue(bc.seatTreads);
            Assert.IsTrue(cfg.stadium.postPadHomeColor);
            Assert.AreSame(cfg.teams[1], FieldView.EndZoneTeam(cfg, -1), "the home team's name in both end zones");
            Assert.AreSame(cfg.teams[1], FieldView.EndZoneTeam(cfg, 1));
            cfg.field.paint.endZoneTeams = "split";
            Assert.AreSame(cfg.teams[0], FieldView.EndZoneTeam(cfg, -1), "split: the away team on the left");
            Assert.AreSame(cfg.teams[1], FieldView.EndZoneTeam(cfg, 1));
        }

        [Test]
        public void TheGoldenSunShinesFromBehindTheCameraOntoTheFarStands()
        {
            var s = Golden().sky;
            var light = SkyView.SunRotation(s) * Vector3.forward;   // the way the light travels
            Assert.Less(light.y, 0f, "the light comes from above");
            Assert.Greater(light.z, 0f, "it travels toward the far side, lighting the stands the TV camera looks at");
            Assert.Greater(s.sunElevDeg, s.paintedElevDeg);
            Assert.Greater(s.fogStartM, 200f, "the haze never reaches the field or the stands");
        }

        [Test]
        public void TheRoofsHangOverTheStandsNotTheField()
        {
            var cfg = Golden();
            var go = new GameObject("StadiumLookTest");
            try
            {
                var bowl = new Bowl(cfg, go.transform, null, null);
                var bc = cfg.stadium.bowl;
                Assert.Greater(bowl.OuterD - bc.roofDepthM, 0f, "the front edge stays behind the wall line");
                Assert.Greater(bc.roofLiftM, 0f, "the roof sits over the rim");
                Assert.Less(bc.skylineRadiusM, cfg.stadium.bowl.groundRadiusM, "the skyline stands on the ground");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void EveryTeamNameCanBePaintedInItsEndZone()
        {
            var cfg = Golden();
            foreach (var t in TeamPick.League(cfg))
                foreach (char c in t.name.ToUpperInvariant())
                    Assert.IsTrue(c == ' ' || Glyphs.Has(c), t.name + " has a letter the field paint cannot draw: " + c);
        }
    }
}
