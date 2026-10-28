using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // The title screen (FEATURES.titleScreen): its pictures, its switch and the attract mode's camera angles.
    public sealed class TitleScreenTests
    {
        [Test]
        public void TheSwitchIsOnAndNoTitleScreenTurnsItOff()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("titleScreen"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_TITLE_SCREEN", "1" } }).Feature("titleScreen"));
        }

        [Test]
        public void TheLogoAndTheThreeButtonsLoad()
        {
            foreach (var name in new[] { "Title/TitleArt", "Title/ButtonPlay", "Title/ButtonHowToPlay", "Title/ButtonSettings" })
            {
                var tex = Resources.Load<Texture2D>(name);
                Assert.IsNotNull(tex, name);
                Assert.Greater(tex.width, tex.height, name + " is wider than tall");
            }
        }

        [Test]
        public void ThereAreSeveralAnglesAndTheBroadcastOne()
        {
            var shots = ConfigTests.Load().title.shots;
            int own = 0, broadcast = 0;
            foreach (var s in shots) if (s.broadcast) broadcast++; else own++;
            Assert.AreEqual(1, broadcast);
            Assert.GreaterOrEqual(own, 4);
        }

        // Wherever the ball is and whichever way the offense goes, every angle stands above the grass inside the
        // stadium walls and has the ball in front of it, inside its lens.
        [Test]
        public void EveryAngleSeesTheBall()
        {
            var cfg = ConfigTests.Load();
            float m = cfg.field.yardM;
            foreach (var s in cfg.title.shots)
            {
                if (s.broadcast) continue;
                foreach (float dir in new[] { 1f, -1f })
                    foreach (float x in new[] { -45f, -10f, 0f, 25f, 48f })
                        foreach (float z in new[] { -20f, 0f, 20f })
                        {
                            var p = TitleShots.Pose(cfg, s, x, z, dir);
                            string at = s.name + " ball " + x + "," + z + " dir " + dir;
                            Assert.Greater(p.position.y, 0.4f, at);
                            Assert.LessOrEqual(Mathf.Abs(p.position.z), cfg.title.maxSideYd * m + 1e-3f, at);
                            Assert.LessOrEqual(Mathf.Abs(p.position.x), cfg.title.maxEndYd * m + 1e-3f, at);
                            var fwd = (p.lookAt - p.position).normalized;
                            var toBall = (new Vector3(x, 0.5f, z) * m - p.position).normalized;
                            Assert.Less(Vector3.Angle(fwd, toBall), p.fovDeg * 0.5f, at);
                        }
            }
        }

        [Test]
        public void EveryCutChangesTheAngle()
        {
            for (int n = 2; n <= 6; n++)
                for (int cur = 0; cur < n; cur++)
                    foreach (float pick in new[] { 0f, 0.2f, 0.5f, 0.8f, 0.9999f, 1f })
                    {
                        int next = TitleShots.Next(n, cur, pick);
                        Assert.AreNotEqual(cur, next);
                        Assert.That(next, Is.InRange(0, n - 1));
                    }
            Assert.AreEqual(0, TitleShots.Next(1, 0, 0.5f));
            Assert.That(TitleShots.Next(6, -1, 1f), Is.InRange(0, 5));
        }
    }
}
