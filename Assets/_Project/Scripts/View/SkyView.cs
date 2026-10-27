using PocketFootball.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PocketFootball.View
{
    // The sunset (FEATURES.sunsetSky, NO_SUNSET_SKY=1: the old flat blue sky and afternoon sun). Sets everything once
    // at start from game.json "sky": the sun's colour, strength and low angle, the ambient fill, the sky box
    // (Art/Shaders/Sky.shader through Resources/Sky.mat), the far haze (linear fog that melts the ground outside the
    // stadium into the sky's horizon) and a warmer colour grade on top of the project's look. Nothing animates.
    public static class SkyView
    {
        public static bool On(GameConfig cfg) => cfg.Feature("sunsetSky");

        // The light's rotation: sunElevDeg above the horizon, coming from sunYawDeg (0 = from straight over the far
        // side, toward the TV camera; +90 = from the right end).
        public static Quaternion SunRotation(SkyConfig s) => Quaternion.Euler(s.sunElevDeg, 180f + s.sunYawDeg, 0f);

        // Where the sun is (a unit vector toward it) at a given height above the horizon, on the light's bearing.
        public static Vector3 SunDirection(SkyConfig s, float elevDeg)
        {
            float e = elevDeg * Mathf.Deg2Rad, y = s.sunYawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Sin(y), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(y));
        }

        public static bool Apply(GameConfig cfg, Light sun)
        {
            if (!On(cfg)) return false;
            var s = cfg.sky;
            sun.color = Colors.Hex(s.sunColor);
            sun.intensity = s.sunIntensity;
            sun.transform.rotation = SunRotation(s);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Colors.Hex(s.ambientColor);

            var src = Resources.Load<Material>("Sky");
            if (src != null)
            {
                var m = new Material(src) { name = "SunsetSky" };
                m.SetColor("_Zenith", Colors.Hex(s.zenithColor));
                m.SetColor("_High", Colors.Hex(s.highColor));
                m.SetColor("_Low", Colors.Hex(s.lowColor));
                m.SetColor("_Horizon", Colors.Hex(s.horizonColor));
                m.SetColor("_SunLow", Colors.Hex(s.sunLowColor));
                m.SetColor("_SunHorizon", Colors.Hex(s.sunHorizonColor));
                m.SetColor("_Disc", Colors.Hex(s.discColor));
                m.SetColor("_Haze", Colors.Hex(s.hazeColor));
                m.SetColor("_Hill", Colors.Hex(s.hillColor));
                m.SetColor("_CloudLit", Colors.Hex(s.cloudLitColor));
                m.SetColor("_CloudShade", Colors.Hex(s.cloudShadeColor));
                m.SetColor("_CloudRim", Colors.Hex(s.cloudRimColor));
                m.SetVector("_SunDir", SunDirection(s, s.paintedElevDeg));
                m.SetFloat("_SunSizeDeg", s.paintedSizeDeg);
                m.SetFloat("_SunGlow", s.sunGlow);
                m.SetFloat("_SunSpread", s.sunSpread);
                m.SetFloat("_CloudCover", s.cloudCover);
                m.SetFloat("_CloudScale", s.cloudScale);
                m.SetFloat("_CloudStretch", s.cloudStretch);
                m.SetFloat("_CloudHeight", s.cloudHeight);
                m.SetFloat("_HillsDeg", s.hillsDeg);
                RenderSettings.skybox = m;
            }
            else Debug.LogWarning("[sky] Resources/Sky.mat missing: no sky box");

            if (s.fogEndM > s.fogStartM && s.fogStartM > 0f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Colors.Hex(s.hazeColor);
                RenderSettings.fogStartDistance = s.fogStartM;
                RenderSettings.fogEndDistance = s.fogEndM;
            }

            // the sunset grade: a higher-priority global volume over the project's look (only what it sets changes)
            var go = new GameObject("SunsetGrade");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.Override(s.gradeTemperature);
            wb.tint.Override(s.gradeTint);
            var ca = p.Add<ColorAdjustments>(true);
            ca.saturation.Override(s.gradeSaturation);
            v.sharedProfile = p;
            return true;
        }

        // The TV camera draws the sky box instead of a flat colour.
        public static void UseSkyBox(GameConfig cfg, Camera cam)
        {
            if (On(cfg) && RenderSettings.skybox != null) cam.clearFlags = CameraClearFlags.Skybox;
        }
    }
}
