using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // How long a game runs (FEATURES.gameLength, NO_GAME_LENGTH=1: always game.json's clock). The title's SETTINGS
    // page picks one of gameLength.lengths (a quick 2 x 1 minute game up to the full 4 x 4), saved for every later
    // run; Apply writes its quarters, quarter length and halftime into the clock block before the world is built.
    // An unknown or missing pick is gameLength.defaultId.
    public sealed class GameLengthOption
    {
        public string id, label;
        public int quarters, halftimeAfterQuarter;
        public float quarterS;
    }

    public sealed class GameLengthConfig
    {
        public string defaultId;
        public List<GameLengthOption> lengths = new List<GameLengthOption>();
    }

    public static class GameLength
    {
        public static bool On(GameConfig cfg) => cfg.Feature("gameLength") && cfg.gameLength.lengths.Count > 0;

        // The option's index (the default's for an unknown id, -1 with no options at all).
        public static int Find(GameConfig cfg, string id)
        {
            var list = cfg.gameLength.lengths;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].id, id, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].id, cfg.gameLength.defaultId, StringComparison.OrdinalIgnoreCase)) return i;
            return list.Count > 0 ? 0 : -1;
        }

        public static GameLengthOption Get(GameConfig cfg, string id)
        {
            int i = Find(cfg, id);
            return i >= 0 ? cfg.gameLength.lengths[i] : null;
        }

        // The next or last option on the SETTINGS page (wraps round).
        public static string Step(GameConfig cfg, string id, int dir)
        {
            int n = cfg.gameLength.lengths.Count;
            if (n == 0) return id;
            int i = Find(cfg, id);
            return cfg.gameLength.lengths[((i + dir) % n + n) % n].id;
        }

        // The clock plays the picked length. False (nothing changed) with the switch off or no options.
        public static bool Apply(GameConfig cfg, string id)
        {
            if (!On(cfg)) return false;
            var o = Get(cfg, id);
            if (o == null || o.quarters < 1 || o.quarterS <= 0f) return false;
            cfg.clock.quarters = o.quarters;
            cfg.clock.quarterS = o.quarterS;
            cfg.clock.halftimeAfterQuarter = Math.Max(1, Math.Min(o.halftimeAfterQuarter, o.quarters));
            return true;
        }
    }
}
