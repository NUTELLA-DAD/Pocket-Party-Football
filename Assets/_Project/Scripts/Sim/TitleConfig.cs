using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // The title screen (FEATURES.titleScreen, NO_TITLE_SCREEN=1 goes straight to the lobby as before).
    // Only the TV reads these (Boot/TitleScreen.cs, View/TitleShots.cs).
    //   Behind the logo and the buttons the bots play a game of their own, filmed from a new angle every play (shots).
    //   A shot stands back from the ball (backYd, against the way the offense goes; less than 0 is in front of it),
    //   out to the side (sideYd, more than 0 toward the TV camera's sideline), heightYd up, and looks aheadYd past the
    //   ball, lookUpYd above the grass, through a fovDeg lens. broadcast: the game's own sideline camera instead.
    //   The camera follows the ball (followRate) and holds each shot at least minShotS. It never stands further from
    //   the middle than maxSideYd across or maxEndYd along the field (inside the stadium walls).
    //   Layout, as parts of the screen height: the logo (logoTopFrac, logoHeightFrac), then the buttons (playHeightFrac,
    //   otherHeightFrac, gapFrac apart). The picked button grows by hoverScale; the others dim to dimOthers.
    //   tint darkens the game behind it. PLAY fades to black in fadeS (8 steps a second), then the lobby opens.
    //   Settings: the sound goes up and down in volumeStep.
    public sealed class TitleConfig
    {
        public float minShotS, followRate, lookUpYd, maxSideYd, maxEndYd;
        public float logoTopFrac, logoHeightFrac, playHeightFrac, otherHeightFrac, gapFrac;
        public float hoverScale, dimOthers, tint, fadeS, volumeStep;
        public List<TitleShot> shots = new List<TitleShot>();
    }

    public sealed class TitleShot
    {
        public string name;
        public bool broadcast;
        public float backYd, sideYd, heightYd, aheadYd, fovDeg;
    }
}
