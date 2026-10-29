using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.toonHud: the game's own font (Pocket Block) and the navy and gold scoreboard.
    public sealed class ToonHudTests
    {
        [Test]
        public void TheSwitchIsOnAndNoToonHudTurnsItOff()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("toonHud"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_TOON_HUD", "1" } }).Feature("toonHud"));
        }

        // Every letter the TV writes in the HUD is drawn in the font itself (nothing falls back to another font).
        [Test]
        public void TheFontLoadsAndHasEveryHudCharacter()
        {
            var font = Resources.Load<Font>(UiFont.Path);
            Assert.IsNotNull(font, UiFont.Path);
            const string hud = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?'\"-+=/|()[]#%&*_·◄►▲▼●✓—°";
            foreach (char c in hud) Assert.IsTrue(font.HasCharacter(c), "Pocket Block has '" + c + "' (U+" + ((int)c).ToString("X4") + ")");
        }

        // On a wide TV and on a 4:3 one, at 720p and at 4K: the bar fits on screen with its badges, the pieces sit in
        // order without touching, and the banner starts below the down-and-distance tab.
        [Test]
        public void TheScoreboardFitsAndItsPiecesDoNotOverlap()
        {
            foreach (var (w, h) in new[] { (1280f, 720f), (1920f, 1080f), (3840f, 2160f), (1440f, 1080f) })
            {
                var l = ToonHud.Measure(w, h / 1080f, true);
                string at = w + "x" + h;
                Assert.Greater(l.Badge[0].x, 0f, at + ": the left badge is on screen");
                Assert.Less(l.Badge[1].xMax, w, at + ": the right badge is on screen");
                Assert.AreEqual(w * 0.5f, l.Bar.center.x, 0.5f, at + ": centred");
                Assert.LessOrEqual(l.Plate[0].xMax, l.Center.x, at);
                Assert.LessOrEqual(l.Center.xMax, l.Plate[1].x, at);
                for (int t = 0; t < 2; t++)
                {
                    Assert.IsTrue(Inside(l.Bar, l.Plate[t]), at + ": plate " + t + " in the bar");
                    Assert.IsTrue(Inside(l.Plate[t], l.Score[t]), at + ": score box " + t + " in its plate");
                    Assert.IsFalse(l.Name[t].Overlaps(l.Score[t]), at + ": name " + t + " clear of the score");
                    Assert.IsFalse(l.Name[t].Overlaps(l.Badge[t]), at + ": name " + t + " clear of the badge");
                    Assert.Greater(l.Name[t].width, 60f * l.K, at + ": room for a three-letter name");
                }
                Assert.Greater(l.Tab.yMax, l.Bar.yMax, at + ": the tab hangs under the bar");
                Assert.Greater(l.BannerTop, l.Tab.yMax, at + ": the banner is under the tab");
                Assert.Less(l.BannerTop, h * 0.2f, at + ": and still near the top");
            }
            var noTab = ToonHud.Measure(1920f, 1f, false);
            Assert.AreEqual(Rect.zero, noTab.Tab);
            Assert.Greater(noTab.BannerTop, noTab.Bar.yMax);
        }

        static bool Inside(Rect outer, Rect r) => r.xMin >= outer.xMin - 0.01f && r.xMax <= outer.xMax + 0.01f && r.yMin >= outer.yMin - 0.01f && r.yMax <= outer.yMax + 0.01f;
    }
}
