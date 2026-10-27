namespace PocketFootball.Sim
{
    // How the field is painted (WS-11, View/FieldView.cs and Art/Shaders/Grass*): the `paint` part of the field block.
    // Only the TV reads these; the sim never does. Sizes in yards. Lines and numbers: their opacity (the grass shows
    // through a little) and a soft edge. Logo: the home team's mark in the middle, painted in two tones from its ONE
    // texture: brighter than logoSplit (luma: the white trim) goes the light tone (logoLightMix of the way from the grass to the line
    // colour), the rest the dark tone (logoDarkMix of the way from the grass to the team colour, times logoDarkValue).
    // Brush: seeded dabs along the mowing direction baked into the paint; patches: broad lighter and darker mottling. Detail and grade: the grass shader (fine
    // brush strokes up close, detailScale [x, z] their noise frequency per axis (bigger z than x: long along z, which the TV
    // camera squashes, so they look round on screen), that fade out before they would shimmer, then a warm grade: saturation, gain and lift,
    // linear RGB). Turf v2 (FEATURES.turfV2): stripes blended over stripeSoftYd either side of their edges; a watercolour
    // wash (washYd across, washStretch times longer along z, washEdge sharpens it, washStrength its depth); broad sunlight
    // and cloud shade (lightYd, lightStrength); the near side up to nearShade darker. Scrimmage and first-down lines: their width and colours (View/Markings.cs).
    public sealed class FieldPaintConfig
    {
        public float lineAlpha, lineSoftYd;
        public float numberHeightYd, numberStrokeYd, numberGapYd, numberAlpha, arrowYd;
        public float logoWidthYd, logoHeightYd, logoAlpha, logoSplit, logoLightMix, logoDarkMix, logoDarkValue;
        public float brushDabsPerSqYd, brushLenYd, brushWidthYd, brushTiltDeg, brushStrength, grainStrength, patchYd, patchStrength;
        public float detailM, detailAmount, detailFadePx;
        public float[] detailScale;
        public float stripeSoftYd, washYd, washStretch, washStrength, washEdge, lightYd, lightStrength, nearShade;
        public float gradeSaturation;
        public float[] gradeGain, gradeLift;
        public float scrimmageWidthYd;
        public string scrimmageColor, firstDownColor;
    }
}
