namespace PocketFootball.Sim
{
    // The sunset sky and its light (FEATURES.sunsetSky, NO_SUNSET_SKY=1: the old flat blue sky and afternoon sun).
    // Only the TV reads these (View/SkyView.cs, Art/Shaders/Sky.shader). Colours are hex.
    //   The sun (the real light): sunElevDeg up, coming from sunYawDeg (0 = from the far side, +90 = from the right
    //   end), sunColor at sunIntensity; ambientColor fills the rest.
    //   The painted sun: paintedElevDeg up on the same bearing (low on the horizon), paintedSizeDeg wide, its glow.
    //   The gradient: zenith -> high -> low -> horizon; toward the sun the low and horizon colours turn to sunLow and
    //   sunHorizon (sunSpread: how wide that warm side is).
    //   Clouds: long painted streaks (cloudCover 0..1, cloudScale, cloudStretch), a lit edge on the sun side.
    //   Hills: a dusky silhouette hillsDeg high along the horizon. Fog: far things (fogStartM..fogEndM) melt into
    //   hazeColor, which is also the sky below the horizon.
    //   Grade: a runtime colour-grade override on top of the project's look (warmer, a touch of pink).
    public sealed class SkyConfig
    {
        public float sunElevDeg, sunYawDeg, sunIntensity;
        public string sunColor, ambientColor;
        public float paintedElevDeg, paintedSizeDeg, sunGlow;
        public string zenithColor, highColor, lowColor, horizonColor, sunLowColor, sunHorizonColor, discColor;
        public float sunSpread;
        public float cloudCover, cloudScale, cloudStretch, cloudHeight;
        public string cloudLitColor, cloudShadeColor, cloudRimColor;
        public float hillsDeg;
        public string hillColor, hazeColor;
        public float fogStartM, fogEndM;
        public float gradeTemperature, gradeTint, gradeSaturation;
    }
}
