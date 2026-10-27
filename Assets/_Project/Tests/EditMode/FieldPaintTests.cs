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
                         pp.detailM, pp.detailFadePx, pp.gradeSaturation, pp.scrimmageWidthYd,
                         pp.stripeSoftYd, pp.washYd, pp.washStretch, pp.washStrength, pp.washEdge, pp.lightYd, pp.lightStrength, pp.nearShade,
                         pp.bladeHeightM, pp.bladeSpacingM, pp.bladeWidth, pp.bladeFlat, pp.bladeLeanM, pp.bladeSteps, pp.bladeRootDark, pp.bladeTipLight })
                Assert.Greater(v, 0f);
            Assert.Greater(pp.bladeShells, 0);
            Assert.AreEqual(2, pp.detailScale.Length);
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

        // FEATURES.grassBlades: the blade shells stand on the field (one quad per shell, up to bladeHeightM) with the
        // shell shader, drawn after the field; NO_GRASS_BLADES=1 leaves the flat field.
        [Test]
        public void GrassBladesStandOnTheFieldAndCanBeSwitchedOff()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/GrassShell.shader");
            Assert.IsNotNull(shader, "GrassShell.shader did not import");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "GrassShell.shader has a compile error (see the Console)");
            Assert.AreEqual(shader, Resources.Load<Material>("GrassShell").shader);

            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("grassBlades"));
            var root = FieldView.Build(cfg, null);
            try
            {
                var paint = root.transform.Find("Paint");
                var blades = paint.Find("Blades");
                Assert.IsNotNull(blades, "no blades on the field");
                var mesh = blades.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(4 * cfg.field.paint.bladeShells, mesh.vertexCount);
                Assert.AreEqual(cfg.field.paint.bladeHeightM, mesh.bounds.max.y, 1e-4f);
                var mat = blades.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreEqual("PocketParty/GrassShell", mat.shader.name);
                Assert.Greater(mat.renderQueue, paint.GetComponent<MeshRenderer>().sharedMaterial.renderQueue, "the blades must draw after the field");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var off = FieldView.Build(ConfigTests.Load(new Dictionary<string, string> { { "NO_GRASS_BLADES", "1" } }), null);
            try { Assert.IsNull(off.transform.Find("Paint/Blades")); }
            finally { Object.DestroyImmediate(off); }
        }

        // Turf v2 (FEATURES.turfV2): the near side of the field is painted darker than the far side, and NO_TURF_V2=1
        // brings back the even field.
        [Test]
        public void TurfV2ShadesTheNearSideAndCanBeSwitchedOff()
        {
            var on = ConfigTests.Load();
            Assert.IsTrue(on.Feature("turfV2"));
            var off = ConfigTests.Load(new Dictionary<string, string> { { "NO_TURF_V2", "1" } });
            Assert.IsFalse(off.Feature("turfV2"));
            float onGap = NearFarGap(on), offGap = NearFarGap(off);
            Assert.Greater(onGap, on.field.paint.nearShade * 0.25f, "turf v2: the near side should be darker than the far side");
            Assert.Less(Mathf.Abs(offGap), onGap, "NO_TURF_V2: the near-side shade should be gone");
        }

        // The far rows' average green minus the near rows' (between the sidelines, a strip 3 yards in from each).
        static float NearFarGap(GameConfig cfg)
        {
            var root = FieldView.Build(cfg, null);
            try
            {
                var tex = (Texture2D)root.transform.Find("Paint").GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_MainTex");
                int border = Mathf.RoundToInt(cfg.field.sidelineBorderYd * 32), strip = 3 * 32;
                float Rows(int y0)
                {
                    float sum = 0; int n = 0;
                    for (int y = y0; y < y0 + strip; y += 4)
                        for (int x = tex.width / 4; x < tex.width * 3 / 4; x += 4) { sum += tex.GetPixel(x, y).g; n++; }
                    return sum / n;
                }
                return Rows(tex.height - border - strip) - Rows(border);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.2f;
    }
}
