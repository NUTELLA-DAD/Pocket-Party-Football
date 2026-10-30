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
    // and cloud shade (lightYd, lightStrength); the near side up to nearShade darker. Blades (FEATURES.grassBlades, Art/Shaders/GrassShell.shader): bladeShells layers
    // up to bladeHeightM, one blade per bladeSpacingM (m), bladeWidth its half-width (share of that), bladeFlat how flat, bladeLeanM
    // its tip's curl, bladeSteps toon light steps from the root (bladeRootDark darker) to the tip (bladeTipLight lighter).
    // Scrimmage and first-down lines: their width and colours (View/Markings.cs).
    public sealed class FieldPaintConfig
    {
        public float lineAlpha, lineSoftYd;
        public float numberHeightYd, numberStrokeYd, numberGapYd, numberAlpha, arrowYd;
        public float logoWidthYd, logoHeightYd, logoAlpha, logoSplit, logoLightMix, logoDarkMix, logoDarkValue;
        public float brushDabsPerSqYd, brushLenYd, brushWidthYd, brushTiltDeg, brushStrength, grainStrength, patchYd, patchStrength;
        public float detailM, detailAmount, detailFadePx;
        public float[] detailScale;
        public float stripeSoftYd, washYd, washStretch, washStrength, washEdge, lightYd, lightStrength, nearShade;
        public float bladeHeightM, bladeSpacingM, bladeWidth, bladeFlat, bladeLeanM, bladeSteps, bladeRootDark, bladeTipLight;
        public int bladeShells;
        public float gradeSaturation;
        public float[] gradeGain, gradeLift;
        // FEATURES.goldenStadium (StadiumLook.cs turns these on): the end zones in a team's colour (endZoneMix of the way from
        // the end zone grass to the team colour, times endZoneValue) with its name across them, sideline to sideline, the
        // letters' tops toward the end line. endZoneTeams "home": the home team in both; "split": the away team on the
        // left, the home team on the right; empty: plain grass as before. The name: endZoneNameHeightYd tall at most
        // (smaller when a long name would not fit endZoneNameMarginYd in from the sidelines), endZoneNameStrokeYd pen,
        // endZoneNameGap between letters (a share of a letter's height), in endZoneNameColor with an
        // endZoneNameEdgeYd edge in the team colour darkened to endZoneNameEdgeValue.
        public string endZoneTeams, endZoneNameColor;
        public float endZoneMix, endZoneValue, endZoneNameHeightYd, endZoneNameStrokeYd, endZoneNameGap, endZoneNameMarginYd;
        public float endZoneNameEdgeYd, endZoneNameEdgeValue, endZoneNameAlpha;
        public float scrimmageWidthYd;
        public string scrimmageColor, firstDownColor;
    }
}
