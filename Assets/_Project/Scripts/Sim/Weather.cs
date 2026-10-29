using System;

namespace PocketFootball.Sim
{
    public enum Sky { Clear, Rain, Snow }

    // FEATURES.weather (NO_WEATHER=1): rain or snow on the field, picked on the title's SETTINGS page (CLEAR, RAIN,
    // SNOW or RANDOM). Boot turns RANDOM into one of the three before the game is built and writes it to weather.now,
    // so the sim only ever reads a fixed config value (no draw from the sim's random numbers to pick it).
    // FEATURES.sandField (NO_SAND_FIELD=1): a home team with "field": "sand" in game.json (the Cacti) plays on sand.
    // Each of rain, snow and sand is a game.json block of multipliers (1 = no change), and a sand field in the snow
    // takes both:
    //   topSpeed, accel, decel: footing (a low decel slides on past a cut);
    //   slipChance: each tick a player sprints into a hard cut (more than slipCutDot off his way, at slipMinSpeedYdS or
    //     more) he may slip and stumble for slipStumbleS (the same stumble a missed tackle gives);
    //   stamina: how fast a sprint drains the tank;
    //   catchChance: a wet or cold ball, hard to see coming down, is caught less often (both teams);
    //   fumble: how often a big hit knocks it loose;
    //   kickSpeed: how far a kick of the same power flies;
    //   wind: the field goal wind (FEATURES.wind) blows that much harder.
    // Clear weather on grass changes nothing and draws nothing, so the seeded bot games are as before.
    public static class Weather
    {
        public static readonly string[] Picks = { "clear", "rain", "snow", "random" };

        public static bool On(GameConfig cfg) => cfg.Feature("weather");

        public static Sky Now(GameConfig cfg)
        {
            if (!On(cfg)) return Sky.Clear;
            return Parse(cfg.weather.now);
        }

        public static Sky Now(World w) => Now(w.Cfg);

        public static Sky Parse(string name)
        {
            if (string.Equals(name, "rain", StringComparison.OrdinalIgnoreCase)) return Sky.Rain;
            if (string.Equals(name, "snow", StringComparison.OrdinalIgnoreCase)) return Sky.Snow;
            return Sky.Clear;
        }

        public static string Name(Sky s) => s == Sky.Rain ? "rain" : s == Sky.Snow ? "snow" : "clear";

        // A pick from the SETTINGS page as the weather to play in: "random" uses roll (0..1) against
        // weather.randomOdds (clear, rain, snow); an unknown pick is clear.
        public static Sky Resolve(GameConfig cfg, string pick, float roll)
        {
            if (!string.Equals(pick, "random", StringComparison.OrdinalIgnoreCase)) return Parse(pick);
            var odds = cfg.weather.randomOdds;
            if (odds == null || odds.Length == 0) return Sky.Clear;
            float total = 0f;
            foreach (float o in odds) total += Math.Max(0f, o);
            if (total <= 0f) return Sky.Clear;
            float at = roll * total;
            for (int i = 0; i < odds.Length && i <= (int)Sky.Snow; i++)
            {
                at -= Math.Max(0f, odds[i]);
                if (at < 0f) return (Sky)i;
            }
            return Sky.Clear;
        }

        // The next or last pick on the SETTINGS page.
        public static string Step(string pick, int dir)
        {
            int i = Array.IndexOf(Picks, (pick ?? "").ToLowerInvariant());
            if (i < 0) i = 0;
            int n = Picks.Length;
            return Picks[((i + dir) % n + n) % n];
        }

        // Before the stadium is built (Boot, once a boot): the game.json numbers a sand field and this weather change
        // for the TV (weatherLook: the sky, the light, the haze, the grass colours). The weather's first, so a sand
        // field stays sand in the rain.
        public static void ApplyLook(GameConfig cfg)
        {
            var look = cfg.weatherLook;
            if (look == null) return;
            var s = Now(cfg);
            var sky = s == Sky.Rain ? look.rain : s == Sky.Snow ? look.snow : null;
            if (sky != null && sky.overrides != null) Populate(sky.overrides, cfg);
            if (Sand(cfg) && look.sand != null) Populate(look.sand, cfg);
        }

        static void Populate(Newtonsoft.Json.Linq.JObject o, GameConfig cfg)
        {
            using (var r = o.CreateReader()) Newtonsoft.Json.JsonSerializer.CreateDefault().Populate(r, cfg);
        }

        // The look block for the weather now (null: clear).
        public static WeatherSkyLook Look(GameConfig cfg)
        {
            var s = Now(cfg);
            return s == Sky.Rain ? cfg.weatherLook.rain : s == Sky.Snow ? cfg.weatherLook.snow : null;
        }

        // ---- the field ----

        public static bool SandOn(GameConfig cfg) => cfg.Feature("sandField");

        // The home team (teams[1]) plays on sand.
        public static bool Sand(GameConfig cfg) =>
            SandOn(cfg) && cfg.teams.Count > 1 && cfg.teams[1] != null && string.Equals(cfg.teams[1].field, "sand", StringComparison.OrdinalIgnoreCase);

        public static bool Any(GameConfig cfg) => Now(cfg) != Sky.Clear || Sand(cfg);

        // The sky's block and the field's block in play now (null: none).
        static WeatherEffect SkyBlock(GameConfig cfg)
        {
            var s = Now(cfg);
            return s == Sky.Rain ? cfg.weather.rain : s == Sky.Snow ? cfg.weather.snow : null;
        }

        static WeatherEffect FieldBlock(GameConfig cfg) => Sand(cfg) ? cfg.weather.sand : null;

        enum Knob { TopSpeed, Accel, Decel, Stamina, Catch, Fumble, KickSpeed, Wind }

        static float Get(WeatherEffect e, Knob k)
        {
            if (e == null) return 1f;
            switch (k)
            {
                case Knob.TopSpeed: return e.topSpeed;
                case Knob.Accel: return e.accel;
                case Knob.Decel: return e.decel;
                case Knob.Stamina: return e.stamina;
                case Knob.Catch: return e.catchChance;
                case Knob.Fumble: return e.fumble;
                case Knob.KickSpeed: return e.kickSpeed;
                default: return e.wind;
            }
        }

        static float Mul(GameConfig cfg, Knob k) => Get(SkyBlock(cfg), k) * Get(FieldBlock(cfg), k);

        public static float TopSpeed(World w) => Mul(w.Cfg, Knob.TopSpeed);
        public static float Accel(World w) => Mul(w.Cfg, Knob.Accel);
        public static float Decel(World w) => Mul(w.Cfg, Knob.Decel);
        public static float Stamina(World w) => Mul(w.Cfg, Knob.Stamina);
        public static float Catch(World w, float chance) => chance * Mul(w.Cfg, Knob.Catch);
        public static float Fumble(World w) => Mul(w.Cfg, Knob.Fumble);
        public static float KickSpeed(World w) => Mul(w.Cfg, Knob.KickSpeed);
        public static float Wind(World w) => Mul(w.Cfg, Knob.Wind);

        // The chance a hard cut slips him up this tick (the two blocks' chances together).
        public static float SlipChance(World w)
        {
            float Stay(WeatherEffect e) => e == null ? 1f : 1f - Math.Max(0f, Math.Min(1f, e.slipChance));
            return 1f - Stay(SkyBlock(w.Cfg)) * Stay(FieldBlock(w.Cfg));
        }

        // Mover asks this after the footing step: a person or bot sprinting hard into a cut on a slick field may go
        // down for a moment. Only in a live play, never while already stumbling. True: he slipped.
        public static bool Slip(World w, Player p, float wantX, float wantZ)
        {
            if (w.Phase != Phase.Live || p.StumbleTicks > 0 || !p.Sprinting) return false;
            float chance = SlipChance(w);
            if (chance <= 0f) return false;
            var c = w.Cfg.weather;
            float v2 = p.VX * p.VX + p.VZ * p.VZ, want2 = wantX * wantX + wantZ * wantZ;
            if (v2 < c.slipMinSpeedYdS * c.slipMinSpeedYdS || want2 <= 0f) return false;
            float dot = (p.VX * wantX + p.VZ * wantZ) / (float)Math.Sqrt(v2 * want2);
            if (dot > c.slipCutDot) return false;
            if (w.Rng.Value() >= chance) return false;
            p.StumbleTicks = Huddle.Ticks(w, c.slipStumbleS);
            w.Events.Push(w, EventKind.Slip, p.Slot, p.Team, p.X, p.Z);
            return true;
        }
    }

    // One weather or field's multipliers (1 = as on a dry grass field). See Weather.
    public sealed class WeatherEffect
    {
        public float topSpeed = 1f, accel = 1f, decel = 1f, stamina = 1f, catchChance = 1f, fumble = 1f, kickSpeed = 1f, wind = 1f;
        public float slipChance;
    }

    // game.json "weather" (Weather.cs; the look is in "weatherLook", View/WeatherView.cs).
    public sealed class WeatherConfig
    {
        public string now = "clear";        // the weather this game is played in (Boot writes the SETTINGS pick here)
        public float[] randomOdds;          // RANDOM: the odds of clear, rain, snow
        public WeatherEffect rain = new WeatherEffect(), snow = new WeatherEffect(), sand = new WeatherEffect();
        public float slipMinSpeedYdS, slipCutDot, slipStumbleS;
    }
}

namespace PocketFootball.Sim
{
    // game.json "weatherLook": how rain, snow and sand look on the TV (View/WeatherView.cs, View/FieldCover.cs). The sim
    // never reads it.
    public sealed class WeatherLookConfig
    {
        public float keyFps;                  // the falling rain and snow move in stepped keys this many a second
        public float[] boxM;                  // the box of air the drops fill (x, y, z metres), in front of the TV camera
        public float aheadM;                  // ...its middle this far ahead of the camera
        public WeatherSkyLook rain = new WeatherSkyLook(), snow = new WeatherSkyLook();
        // a sand field: game.json numbers it changes before the stadium is built (field colours, the stains, ...)
        public Newtonsoft.Json.Linq.JObject sand;
    }

    public sealed class WeatherSkyLook
    {
        public int count;                     // drops or flakes in the box
        public float widthM, lengthM;         // a rain streak's width and length; a flake's size (widthM)
        public float fallMS, driftMS, windLean;   // fall speed, a flake's own sideways drift, and how much the wind tilts them
        public string color;
        public float alpha;
        public int splashCount;               // rain: splashes on the grass each key
        public float splashM, splashAlpha;
        public float cover;                   // snow: how much of the grass the snow covers (0..1), in patches patchYd across
        public float coverPatchYd, coverEdge;
        public string coverColor;
        // game.json numbers this weather changes before the stadium is built (the sky, the light, the haze, the grass)
        public Newtonsoft.Json.Linq.JObject overrides;
    }
}
