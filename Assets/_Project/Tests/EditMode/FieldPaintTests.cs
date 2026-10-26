using System.Collections.Generic;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEditor;
using UnityEngine;

namespace PocketFootball.Tests.EditMode
{
    // WS-11: the painted field, the grass shader and the centre mark.
    public sealed class FieldPaintTests
    {
        [Test]
        public void GrassShaderCompilesAndIsInResources()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/Grass.shader");
            Assert.IsNotNull(shader, "Grass.shader did not import");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "Grass.shader has a compile error (see the Console)");
            var mat = Resources.Load<Material>("Grass");
            Assert.IsNotNull(mat, "Resources/Grass.mat is missing");
            Assert.AreEqual(shader, mat.shader);
        }

        [Test]
        public void PaintNumbersAllHaveValues()
        {
            var pp = ConfigTests.Load().field.paint;
            foreach (float v in new[] { pp.lineAlpha, pp.numberHeightYd, pp.numberStrokeYd, pp.numberAlpha, pp.arrowYd, pp.logoWidthYd, pp.logoHeightYd,
                         pp.logoAlpha, pp.logoSplit, pp.logoLightMix, pp.logoDarkMix, pp.logoDarkValue, pp.brushDabsPerSqYd, pp.patchYd, pp.patchStrength, pp.brushLenYd, pp.brushWidthYd,
                         pp.detailM, pp.detailFadePx, pp.gradeSaturation, pp.scrimmageWidthYd })
                Assert.Greater(v, 0f);
            Assert.AreEqual(3, pp.gradeGain.Length);
            Assert.AreEqual(3, pp.gradeLift.Length);
            foreach (var c in new[] { pp.scrimmageColor, pp.firstDownColor }) StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", c);
        }

        [Test]
        public void NoGrassShaderPutsTheToonShaderBack()
        {
            Assert.IsTrue(ConfigTests.Load().Feature("grassShader"));
            var off = ConfigTests.Load(new Dictionary<string, string> { { "NO_GRASS_SHADER", "1" } });
            Assert.AreSame(Looks.Toon(null), FieldView.Ground(off, null));
            var on = FieldView.Ground(ConfigTests.Load(), null);
            Assert.AreEqual("PocketParty/Grass", on.shader.name);
            Object.DestroyImmediate(on);
        }

        // The middle of the field carries the home team's mark from its one texture, in exactly two tones of paint:
        // some pixels near the light tone, some near the dark tone.
        [Test]
        public void CentreMarkIsPaintedInTwoTones()
        {
            var cfg = ConfigTests.Load();
            var pp = cfg.field.paint;
            var root = FieldView.Build(cfg, null);
            try
            {
                var tex = (Texture2D)root.transform.Find("Paint").GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_MainTex");
                Assert.IsNotNull(tex);
                Color grass = Colors.Hex(cfg.field.grassColor), line = Colors.Hex(cfg.field.lineColor);
                // (each tone laid at logoAlpha over the grass)
                Color light = Color.Lerp(grass, Color.Lerp(grass, line, pp.logoLightMix), pp.logoAlpha);
                Color dark = Color.Lerp(grass, Color.Lerp(grass, Colors.Hex(cfg.teams[1].color), pp.logoDarkMix) * pp.logoDarkValue, pp.logoAlpha);
                int lightHits = 0, darkHits = 0;
                int r = Mathf.RoundToInt(pp.logoHeightYd * 16);   // (half the logo box, 32 px a yard)
                for (int y = tex.height / 2 - r; y < tex.height / 2 + r; y += 2)
                    for (int x = tex.width / 2 - r; x < tex.width / 2 + r; x += 2)
                    {
                        var c = tex.GetPixel(x, y);
                        if (Near(c, light)) lightHits++;
                        if (Near(c, dark)) darkHits++;
                    }
                Assert.Greater(lightHits, 50, "no light tone in the centre mark");
                Assert.Greater(darkHits, 50, "no dark tone in the centre mark");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.2f;
    }
}
