using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.jerseyNumbers and FEATURES.eyesLook (View/JerseyMarks.cs, Alive.Eyes): every player wears his own number,
    // and his eyes follow the ball, glance aside and blink.
    public sealed class JerseyMarksTests
    {
        [Test]
        public void EveryPlayerModelHasItsMarksMap()
        {
            var cfg = ConfigTests.Load();
            var models = new HashSet<string>();
            foreach (var team in cfg.teams)
            {
                models.Add(team.model);
                foreach (var pm in team.posModels) models.Add(pm.model);
                Assert.IsFalse(string.IsNullOrEmpty(team.numberFill), team.@short + " numberFill");
                Assert.IsFalse(string.IsNullOrEmpty(team.numberEdge), team.@short + " numberEdge");
            }
            foreach (var model in models)
            {
                var mm = cfg.marks.For(model);
                Assert.IsNotNull(mm, model + " is in game.json \"marks\"");
                var map = Resources.Load<Texture2D>(mm.map);
                Assert.IsNotNull(map, "Resources/" + mm.map);
                Assert.IsFalse(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(map.graphicsFormat),
                    mm.map + " is read as plain numbers (linear), not a colour");
            }
        }

        [Test]
        public void ANumberIsItsDigits()
        {
            Assert.AreEqual(new Vector4(5, 8, 2, 0), JerseyMarks.Digits(58));
            Assert.AreEqual(new Vector4(7, 0, 1, 0), JerseyMarks.Digits(7));
            Assert.AreEqual(new Vector4(9, 9, 2, 0), JerseyMarks.Digits(150));
            var d = JerseyMarks.Digits();
            Assert.AreEqual(10 * 72, d.width, "ten digits");
            // the middle of the 1's upright is on the line; a corner of its cell is far from any line
            Color32[] px = d.GetPixels32();
            int at = 45 * d.width + 1 * 72 + Mathf.RoundToInt((0.55f + 0.4f) * 40);
            Assert.Less(px[at].r, 20);
            Assert.AreEqual(255, px[2 * 72 + 1].r);
        }

        // Each player's renderer carries his own number; the team's material carries the marks map and its colours.
        // Switched off, the material wears the number first painted on the model and no player has his own.
        [Test]
        public void EachPlayerWearsHisOwnNumber()
        {
            foreach (bool on in new[] { true, false })
            {
                var cfg = ConfigTests.Load();
                cfg.features["jerseyNumbers"] = on;
                var figs = new List<FootballFigure>();
                try
                {
                    foreach (var (team, pos, number) in new[] { (0, "QB", 9), (0, "OL", 64), (1, "WR", 17), (1, (string)null, 24) })
                    {
                        var fig = FootballFigure.Create(null, "Number", cfg.view.playerHeightM);
                        figs.Add(fig);
                        Assert.IsTrue(ModelSkin.Wear(fig, cfg.teams[team], pos, cfg, Looks.Toon(null)));
                        JerseyMarks.SetNumber(fig.gameObject, cfg, number);
                        var smr = fig.GetComponentInChildren<SkinnedMeshRenderer>();
                        var m = smr.sharedMaterial;
                        Assert.AreEqual(1f, m.GetFloat("_Marks"), "the marks map is on");
                        Assert.IsNotNull(m.GetTexture("_MarksTex"));
                        var block = new MaterialPropertyBlock();
                        smr.GetPropertyBlock(block);
                        cfg.teams[team].ModelFor(pos, true, out string model, out _);
                        if (on) Assert.AreEqual(JerseyMarks.Digits(number), block.GetVector("_NumberDigits"), "his own number");
                        else
                        {
                            Assert.AreEqual(Vector4.zero, block.GetVector("_NumberDigits"), "no number of his own");
                            Assert.AreEqual(JerseyMarks.Digits(cfg.marks.For(model).number), m.GetVector("_NumberDigits"), "the model's number");
                        }
                    }
                }
                finally { foreach (var f in figs) Object.DestroyImmediate(f.gameObject); }
            }
            Assert.AreEqual("NO_JERSEY_NUMBERS", ConfigLoader.EnvName("jerseyNumbers"));
        }

        [Test]
        public void TheEyesFollowTheBallGlanceAndBlink()
        {
            var cfg = ConfigTests.Load();
            var m = cfg.marks;
            // over a long stretch: mostly on the ball, sometimes aside; blinks now and then, never for long
            int toward = 0, aside = 0, blinks = 0, keys = 50 * m.blinkEveryKeys;
            for (int k = 0; k < keys; k++)
            {
                Alive.Eyes(cfg, k, 3, 20f, 0f, out float x, out _, out float blink);
                if (x > 0f) toward++; else aside++;
                if (blink > 0f) blinks++;
                Assert.LessOrEqual(Mathf.Abs(x), m.eyeMaxX + 1e-4f);
            }
            Assert.Greater(toward, aside, "mostly at the ball, to his left");
            Assert.Greater(aside, 0, "a glance away now and then");
            Assert.AreEqual(50 * m.blinkKeys, blinks, "one blink every blinkEveryKeys");
            // far to his right and above: all the way over, and up
            Alive.Eyes(cfg, 0, 0, -170f, 80f, out float fx, out float fy, out _);
            Assert.That(Mathf.Abs(fx), Is.EqualTo(m.eyeMaxX).Within(m.eyeStep));
            Assert.Greater(fy, 0f);
            // the same moment looks the same every time (no randomness)
            Alive.Eyes(cfg, 123, 5, 10f, 0f, out float a1, out float b1, out float c1);
            Alive.Eyes(cfg, 123, 5, 10f, 0f, out float a2, out float b2, out float c2);
            Assert.AreEqual((a1, b1, c1), (a2, b2, c2));
            // switched off: straight ahead, eyes open
            cfg.features["eyesLook"] = false;
            Alive.Eyes(cfg, 7, 2, 30f, 10f, out float ox, out float oy, out float ob);
            Assert.AreEqual((0f, 0f, 0f), (ox, oy, ob));
            Assert.AreEqual("NO_EYES_LOOK", ConfigLoader.EnvName("eyesLook"));
        }
    }
}
