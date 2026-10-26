using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // The stadium round the field (WS-10, View/Stadium.cs). Only the TV reads these; the sim never does. Distances in
    // metres, "Scale" values are shares of view.playerHeightM, chances are 0..1.
    public sealed class StadiumConfig
    {
        // The stands: a wall wallBackM past the far sideline's white border, then rows of seats rising behind it, from
        // -halfLengthM to +halfLengthM along the field, with a stair aisle every aisleEveryM.
        public float wallBackM, wallHeightM, halfLengthM, rowBackM, rowDepthM, rowRiseM, firstRowM, aisleEveryM, aisleWidthM;
        public int rows, banners;
        public string wallColor, seatColor, concreteColor, aisleColor, roofColor;

        // The card crowd: one flat fan per seat (seatM apart plus up to seatJitterM), fanM tall. Shirts: homeShare in
        // the home colours, awayShare in the away colours, the rest from neutralColors. The crowd is cut into slices
        // sliceM wide; each slice has four looks (two idle looks, half up, standing with arms up) and swaps between them
        // only on fps key frames (stepped, no wobble). Idle slices change look every idleKeys keys; a wave of standing
        // fans runs along the stands every waveEveryS seconds at waveSpeedMS, waveWidthM wide (waveEveryS 0: no wave).
        public float seatM, seatJitterM, emptyChance, fanM, standM, cardTiltUp;
        public float homeShare, awayShare, capChance, signChance, idleArmsChance;
        public float sliceM, fps, waveEveryS, waveSpeedMS, waveWidthM;
        public int idleKeys;
        public List<string> neutralColors = new List<string>();

        // The far sideline: each team's players (farScale) and staff stand farRowM to farRowM + farDepthM past the
        // white border, players from farFromCentreM out across farSpreadM, staff across staffSpreadM. Tents (tentScale)
        // stand tentFromWallM in front of the wall, from tentFirstXM out every tentGapM. A bench at benchXM, coolers at
        // coolerXM, refs at farRefsXM.
        public int farPlayersPerTeam, farStaffPerTeam, tentsPerTeam;
        public float farScale, farRowM, farDepthM, farFromCentreM, farSpreadM, staffSpreadM;
        public float tentScale, tentFirstXM, tentGapM, tentFromWallM, benchXM, benchHalfM;
        public List<float> coolerXM = new List<float>(), farRefsXM = new List<float>();

        // The near sideline (the home bench, seen from behind at the bottom of the picture): nearCount places across
        // +-nearHalfLengthM, nearRowM to nearRowM + nearDepthM past the border; a share are staff, a share coolers, the
        // rest players. Refs at nearRefsXM, tents (nearTentScale) behind the row at nearTentsXM.
        public int nearCount;
        public float nearScale, nearRowM, nearDepthM, nearHalfLengthM, nearStaffShare, nearPropShare, nearTentScale;
        public List<float> nearRefsXM = new List<float>(), nearTentsXM = new List<float>();

        // The goal posts on each end line: a padded base post postBackM behind it, a gooseneck, the crossbar
        // postCrossbarM up, uprights postWidthM apart and postUprightM tall with a ribbon on top, all postThickM thick.
        public float postCrossbarM, postWidthM, postUprightM, postThickM, postBackM, postPadM, postRibbonM;
        public string postColor, postPadColor, postRibbonColor;
    }
}
