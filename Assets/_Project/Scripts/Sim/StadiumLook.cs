using Newtonsoft.Json;

namespace PocketFootball.Sim
{
    // FEATURES.goldenStadium (NO_GOLDEN_STADIUM=1: the sunset bowl as before): the stadium in golden-hour light. game.json
    // "stadiumLook" holds blocks laid over the config ("sky", "stadium", "field"), the same way the weather's looks
    // are: a blue sky with big white clouds and a warm sun behind the TV camera, green seats on cream concrete, white
    // truss roofs with floodlights over both sidelines, a skyline and leafy trees outside, the end zones painted in the
    // team's colour with its name, the posts' pads in the home colour. Everything it switches on is off (zero) in the
    // plain config, so the switch off puts back the old look exactly. Applied once at boot, before the weather's look
    // (rain and snow still grey the sky over it). Only the TV reads what it changes; the sim never does.
    public static class StadiumLook
    {
        public static bool On(GameConfig cfg) => cfg.Feature("goldenStadium") && cfg.stadiumLook != null;

        public static bool Apply(GameConfig cfg)
        {
            if (!On(cfg)) return false;
            using (var r = cfg.stadiumLook.CreateReader()) JsonSerializer.CreateDefault().Populate(r, cfg);
            return true;
        }
    }
}
