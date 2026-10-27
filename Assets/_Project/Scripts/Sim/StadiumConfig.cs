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

        // The cheerleaders (FEATURES.cheerleaders): cheerPerTeam of each team's squad (the team's cheerModel) stand in a
        // row cheerRowM past the white border, in front of their own team's bench (spread out so the following camera
        // always has a few in the picture), from cheerFromCentreM out every cheerGapM, cheerScale of a player's height. They all do the same routine together: cheerRoutine lists the
        // cheer poses (FootballFigure.PoseKind.Cheer frames), one per key at cheerFps (stepped, no wobble); the second
        // team's squad runs cheerTeamOffset keys behind the first.
        public int cheerPerTeam, cheerTeamOffset;
        public float cheerScale, cheerRowM, cheerFromCentreM, cheerGapM, cheerFps;
        public List<int> cheerRoutine = new List<int>();

        // The chain crew (gauntlet 3): one person in stripes holds each down marker, crewBackM behind it (off the
        // field), crewScale of a player's height, an orange cap.
        public float crewScale, crewBackM;
        public string crewCapColor;

        // The goal posts on each end line: a padded base post postBackM behind it, a gooseneck, the crossbar
        // postCrossbarM up, uprights postWidthM apart and postUprightM tall with a ribbon on top, all postThickM thick.
        public float postCrossbarM, postWidthM, postUprightM, postThickM, postBackM, postPadM, postRibbonM;
        public string postColor, postPadColor, postRibbonColor;

        // The full bowl round the field (FEATURES.stadiumBowl, NO_STADIUM_BOWL=1: only the old straight stand on the
        // far side). View/Bowl.cs.
        public BowlConfig bowl = new BowlConfig();
    }

    // The outdoor stadium bowl (View/Bowl.cs): the wall line is a rounded rectangle round the field, the far side at the
    // old wall (wallBackM past the border), the ends endBackM past the end lines, the near side nearBackM past its
    // border, corners cornerM round. The lower tier keeps the old rows (rows, rowDepthM, rowRiseM) all the way round;
    // behind it a concourse (concourseM deep), a fascia (fasciaM tall, a ribbon board with banners every bannerEveryM),
    // then the upper tier (upperRows, steeper), a rim wall (rimM) and the outer wall with pilasters. Light towers at the
    // corners (towerHeightM, a lamp head of towerLampsX by towerLampsY), flags along the rim, a big scoreboard over the
    // left end, a ground plane with a car park and a ring of trees outside. segM / cornerDeg: how finely the curve is cut.
    public sealed class BowlConfig
    {
        public float endBackM, nearBackM, cornerM, segM, cornerDeg;
        public float concourseM, fasciaM, upperBackM, upperRowDepthM, upperRowRiseM, rimM, rimThickM;
        public int upperRows;
        public float upperSeatM, upperEmptyChance;
        public float portalEveryM, portalWidthM;
        public int portalRow, portalRows;
        public float bannerEveryM, pilasterEveryM;
        public string fasciaColor, ribbonColor, upperSeatColor, upperConcreteColor, rimColor, outerColor, pilasterColor, portalColor;
        public float towerHeightM, towerOutM, towerTiltDeg, towerLampM;
        public int towerLampsX, towerLampsY;
        public string towerColor, lampColor;
        public float lampGlow;
        public float flagEveryM, flagPoleM, flagWM, flagHM;
        public float boardWidthM, boardHeightM, boardLiftM;
        public string boardColor;
        public float groundRadiusM, parkingM, treeInM, treeOutM;
        public int trees;
        public string groundColor, parkingColor, treeColor, treeDarkColor, trunkColor;
    }
}
