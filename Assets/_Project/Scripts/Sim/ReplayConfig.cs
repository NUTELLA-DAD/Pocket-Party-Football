namespace PocketFootball.Sim
{
    // The instant replay and the result card (WS-08, View/Replay.cs and View/ResultCard.cs, FEATURES.replay). Only
    // the TV reads these; the sim never does. seconds: how much of the play before the whistle is replayed; afterS:
    // how long after the whistle the replay starts (and how much after the whistle it shows); speed: 0.5 = half
    // speed; cardS: how long the result card stays up after the replay; the rest: sizes on a 1080p TV.
    public sealed class ReplayConfig
    {
        public float seconds, afterS, speed, cardS;
        public float tagFontPx, cardTitlePx, cardLinePx, cardWidthPx;
    }
}
