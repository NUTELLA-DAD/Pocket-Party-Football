using System;
using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // The title's ROSTER page (FEATURES.rosterPage, NO_ROSTER_PAGE=1: no button): one card for every player model a
    // team puts on the field, the positions that wear it, and each position's ratings (Ratings.cs). Built from the
    // team's "model" and "posModels" the same way a player on the field picks his (TeamConfig.ModelFor), so a new
    // position model in game.json gets its own card with no code. Cards come in the order of roster.positions.
    public sealed class RosterCard
    {
        public string Model, Texture, Name;
        public int Number;   // the jersey number the card's model wears (roster.models)
        public readonly List<RosterSpot> Spots = new List<RosterSpot>();
    }

    // One position on a card. Numbers: only the players wearing these numbers at this position wear this model (the
    // outside linebackers); none: every one not picked out by another card.
    public sealed class RosterSpot
    {
        public string Pos, PosName, Note;
        public List<int> Numbers = new List<int>();
        public PosRating Rating;   // null: this position has no ratings (the kicker)
    }

    public static class RosterCards
    {
        public static bool On(GameConfig cfg) => cfg.Feature("rosterPage");

        public static List<RosterCard> Of(GameConfig cfg, TeamConfig team)
        {
            var cards = new List<RosterCard>();
            if (team == null) return cards;
            bool byPos = cfg.Feature("posModels");
            foreach (var pos in cfg.roster.positions)
            {
                // the entries picking out some numbers first (the outside linebackers), then the one for everyone else
                if (byPos && team.posModels != null)
                    foreach (var pm in team.posModels)
                        if (pm != null && pm.pos == pos && !string.IsNullOrEmpty(pm.model) && pm.numbers != null && pm.numbers.Count > 0)
                            Add(cfg, cards, pm.model, pm.modelTexture, pos, pm.numbers);
                team.ModelFor(pos, byPos, out string model, out string tex, out _, -1);
                if (!string.IsNullOrEmpty(model)) Add(cfg, cards, model, tex, pos, null);
            }
            return cards;
        }

        static void Add(GameConfig cfg, List<RosterCard> cards, string model, string tex, string pos, List<int> numbers)
        {
            RosterCard card = null;
            foreach (var c in cards) if (c.Model == model) { card = c; break; }
            if (card == null)
            {
                var look = cfg.roster.For(model);
                card = new RosterCard { Model = model, Texture = tex, Name = look?.name ?? Short(model), Number = look?.number ?? 0 };
                cards.Add(card);
            }
            foreach (var s in card.Spots) if (s.Pos == pos) return;
            cfg.roster.posNames.TryGetValue(pos, out string posName);
            cfg.roster.posNotes.TryGetValue(pos, out string note);
            PosRating rating = null;
            if (cfg.ratings.positions != null) cfg.ratings.positions.TryGetValue(pos, out rating);
            card.Spots.Add(new RosterSpot
            {
                Pos = pos, PosName = posName ?? pos, Note = note,
                Numbers = numbers != null ? new List<int>(numbers) : new List<int>(), Rating = rating,
            });
        }

        // "Models/QB/QB_Rigged": "QB" (a model with no name in roster.models)
        static string Short(string model)
        {
            int slash = model.LastIndexOf('/');
            string s = slash >= 0 ? model.Substring(slash + 1) : model;
            int cut = s.IndexOf("_Rigged", StringComparison.Ordinal);
            return (cut > 0 ? s.Substring(0, cut) : s).ToUpperInvariant();
        }
    }

    // game.json "roster": the positions in the order the page shows them, their full names, a line for a position with
    // no ratings, and each model's name on its card and the jersey number it wears there. ratingMax: the top of the
    // ratings' scale (the length of each bar).
    public sealed class RosterConfig
    {
        public List<string> positions = new List<string>();
        public SortedDictionary<string, string> posNames = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public SortedDictionary<string, string> posNotes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public List<RosterModel> models = new List<RosterModel>();
        public int ratingMax;
        // the model on the page: the lens, the camera's turn round from the sun (degrees, so the light falls on his
        // front), and how much room round him (1.2: his size and a tenth more each side)
        public float fovDeg, turnDeg, frameMargin;
        public string backdrop, floor;

        public RosterModel For(string model)
        {
            foreach (var m in models) if (m != null && m.model == model) return m;
            return null;
        }
    }

    public sealed class RosterModel
    {
        public string model, name;
        public int number;
    }
}
