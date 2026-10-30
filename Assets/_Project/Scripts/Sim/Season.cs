using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PocketFootball.Sim
{
    // FEATURES.season (NO_SEASON=1 hides it): run one club through a season. Every spot on your eleven is a recruit
    // with a name, a look (skin and hair that stay the same game after game), his own SPEED / SIZE / HANDS and a
    // record. Each game earns coins (a win, a tie, a loss; a touchdown, a pick, a sack); coins sign free agents, boost
    // a recruit's stats, or pay the coaches: a bigger playbook, faster recovery, longer sprints. Cut a recruit and a
    // walk-on (a plain bot with the position's ratings, no name) fills the spot. The other teams wear names too, but
    // play on the positions' own ratings. Pure: Boot/SeasonHost.cs keeps it as season.json between games (Save / Load).
    public sealed class SeasonConfig
    {
        public int weeks;                              // games a season (0: every other team once)
        public int startCoins, coinsWin, coinsTie, coinsLoss, coinsPerTd, coinsPerPick, coinsPerSack;
        public int offers;                             // free agents on the table at once
        public int scoutCost;                          // NEW FACES: a fresh set of free agents
        public int recruitSpread;                      // a recruit's stats: the spot's position rating, give or take this
        public int signFree;                           // a recruit whose three stats add up to this (or less) signs for nothing...
        public int signCostPerPoint;                   // ...and costs this for every point above it
        public int boostCostBase, boostCostStep;       // +1 to a stat: base, plus step for every boost he already had
        public int boostMax;                           // a stat's ceiling (0: roster.ratingMax)
        public SortedDictionary<string, CoachLineConfig> coaching = new SortedDictionary<string, CoachLineConfig>(StringComparer.Ordinal);
        public List<string> firstNames = new List<string>(), lastNames = new List<string>();
        public string walkOn;                          // the lineup's word for an empty spot
        public JerseyNameConfig jerseyName = new JerseyNameConfig();
    }

    // One coaching line: its costs level by level (costs.Count levels), and what each level does. playbook: the plays
    // the club can call (plays[level] is added at that level; nothing listed = every play). recovery / sprint: perLevel
    // speeds stamina's return (x 1 + perLevel a level) / slows a sprint's drain (x 1 - perLevel a level).
    public sealed class CoachLineConfig
    {
        public string name, blurb;
        public List<int> costs = new List<int>();
        public float perLevel;
        public List<List<string>> plays = new List<List<string>>();
    }

    // The surname on a jersey's back (View/JerseyNames.cs, FEATURES.jerseyNames): letter height, pen width and gap
    // (shares of the height), the widest a name may be, and where it sits on the back (rig cm, from the torso bone).
    public sealed class JerseyNameConfig
    {
        public float letterCm, penFrac, gapFrac, maxWidthCm, aboveTorsoCm, backCm;
    }

    public sealed class Recruit
    {
        public int id;
        public string name = "";
        public int skin, hair;                   // marks.skinTones / marks.hairColors indexes: his look, for good
        public int speed, size, hands;           // 1..max; 0 = unrated (a visitor: plays his position's numbers)
        public int boosts;                       // stat points bought so far (each one costs more)
        public int games, yards, tds, tackles, picks, sacks;   // his record with the club
        PosRating rating;                        // (cached; Boost drops it)

        [JsonIgnore] public bool Rated => speed > 0 && size > 0 && hands > 0;
        [JsonIgnore] public int Total => speed + size + hands;

        // The name on his back.
        [JsonIgnore] public string Surname
        {
            get { int sp = name == null ? -1 : name.LastIndexOf(' '); return sp < 0 ? name ?? "" : name.Substring(sp + 1); }
        }

        // His own ratings for the sim (null while unrated).
        public PosRating Rating()
        {
            if (!Rated) return null;
            if (rating == null || rating.speed != speed || rating.size != size || rating.hands != hands)
                rating = new PosRating { speed = speed, size = size, hands = hands };
            return rating;
        }

        public int Stat(string stat) => stat == Season.Speed ? speed : stat == Season.Size ? size : stat == Season.Hands ? hands : 0;

        public void SetStat(string stat, int value)
        {
            if (stat == Season.Speed) speed = value;
            else if (stat == Season.Size) size = value;
            else if (stat == Season.Hands) hands = value;
            rating = null;
        }
    }

    public sealed class Season
    {
        public const string Speed = "speed", Size = "size", Hands = "hands";
        public static readonly string[] StatNames = { Speed, Size, Hands };
        public const string PlaybookLine = "playbook", RecoveryLine = "recovery", SprintLine = "sprint";

        public int schema = 1;
        public string club = "";                                  // the team you run (its short code)
        public int seed;
        public int week;                                          // games played this season
        public List<string> opponents = new List<string>();       // the schedule, by short code
        public List<GameScore> scores = new List<GameScore>();    // each game played: the club's points and theirs
        public int coins;
        public List<Recruit> lineup = new List<Recruit>();        // by spot; null = a walk-on
        public List<Recruit> offers = new List<Recruit>();        // the free agents on the table
        public SortedDictionary<string, int> coaching = new SortedDictionary<string, int>(StringComparer.Ordinal);   // line -> level
        public int nextId;
        public int roll;                                          // random draws so far (each one is new Rng(seed + roll))
        public int seasons;                                       // seasons finished
        public List<string> history = new List<string>();         // one line a finished season

        public static bool On(GameConfig cfg) => cfg.Feature("season") && cfg.Feature("teamPick") && TeamPick.League(cfg).Count > 1;

        // ---- starting out ----

        // A new season for a club (its short code): a drawn schedule against the rest of the league, a drafted eleven,
        // the opening coins, free agents on the table. Null for a club not in the league.
        public static Season Start(GameConfig cfg, string club, int seed)
        {
            int at = TeamPick.Find(cfg, club);
            if (at < 0) return null;
            var s = new Season { club = TeamPick.League(cfg)[at].@short, seed = seed, coins = cfg.season.startCoins };
            foreach (var kv in cfg.season.coaching) s.coaching[kv.Key] = 0;
            s.Schedule(cfg);
            var rng = s.NextRng();
            for (int spot = 0; spot < cfg.players.perTeam; spot++) s.lineup.Add(s.Draft(cfg, rng, spot));
            s.RefreshOffers(cfg, rng);
            return s;
        }

        Rng NextRng() => new Rng(seed + roll++);

        // The schedule: every other team in the league, shuffled; season.weeks games (0: each of them once), going
        // round again when weeks asks for more games than there are teams.
        void Schedule(GameConfig cfg)
        {
            opponents.Clear();
            var others = new List<string>();
            foreach (var t in TeamPick.League(cfg)) if (t.@short != club && !others.Contains(t.@short)) others.Add(t.@short);
            if (others.Count == 0) return;
            var rng = NextRng();
            for (int i = others.Count - 1; i > 0; i--)
            {
                int j = rng.Below(i + 1);
                (others[i], others[j]) = (others[j], others[i]);
            }
            int games = cfg.season.weeks > 0 ? cfg.season.weeks : others.Count;
            for (int g = 0; g < games; g++) opponents.Add(others[g % others.Count]);
        }

        // A recruit for a spot: a name from the pools, a look, and the spot's position ratings (the average of what he
        // plays on offense and on defense) give or take recruitSpread, kept to 1..max.
        public Recruit Draft(GameConfig cfg, Rng rng, int spot)
        {
            var r = new Recruit { id = ++nextId, name = PickName(cfg, rng), skin = rng.Below(Count(cfg.marks.skinTones)), hair = rng.Below(Count(cfg.marks.hairColors)) };
            int spread = Math.Max(0, cfg.season.recruitSpread), max = StatMax(cfg);
            PosRating off = PosOf(cfg, cfg.formation.offense, spot), def = PosOf(cfg, cfg.formation.defense, spot);
            int Roll(int a, int b)
            {
                int mid = (a + b + 1) >> 1;
                int v = mid + rng.Below(spread + spread + 1) - spread;
                return Math.Max(1, Math.Min(max, v));
            }
            int neutral = (int)cfg.ratings.neutral;
            r.speed = Roll(off?.speed ?? neutral, def?.speed ?? neutral);
            r.size = Roll(off?.size ?? neutral, def?.size ?? neutral);
            r.hands = Roll(off?.hands ?? neutral, def?.hands ?? neutral);
            return r;
        }

        static int Count<T>(List<T> list) => list == null ? 0 : list.Count;

        static PosRating PosOf(GameConfig cfg, List<SpotConfig> spots, int spot)
        {
            if (spots == null || spot < 0 || spot >= spots.Count || spots[spot].pos == null) return null;
            return cfg.ratings.positions.TryGetValue(spots[spot].pos, out var r) ? r : null;
        }

        // "FIRST LAST" from the pools; a surname nobody on the club (or on the table) has yet, when there are enough.
        string PickName(GameConfig cfg, Rng rng)
        {
            var first = cfg.season.firstNames; var last = cfg.season.lastNames;
            string f = first != null && first.Count > 0 ? first[rng.Below(first.Count)] : "";
            string l = "";
            if (last != null && last.Count > 0)
            {
                l = last[rng.Below(last.Count)];
                for (int tries = 0; tries < last.Count && SurnameTaken(l); tries++) l = last[rng.Below(last.Count)];
            }
            return (f + " " + l).Trim();
        }

        bool SurnameTaken(string surname)
        {
            foreach (var r in lineup) if (r != null && r.Surname == surname) return true;
            foreach (var r in offers) if (r != null && r.Surname == surname) return true;
            return false;
        }

        public static int StatMax(GameConfig cfg) => cfg.season.boostMax > 0 ? cfg.season.boostMax : Math.Max(1, cfg.roster.ratingMax);

        // The spot's positions, "QB·LB" (offense first).
        public static string SpotName(GameConfig cfg, int spot)
        {
            string a = spot >= 0 && spot < cfg.formation.offense.Count ? cfg.formation.offense[spot].pos : null;
            string b = spot >= 0 && spot < cfg.formation.defense.Count ? cfg.formation.defense[spot].pos : null;
            if (a == null) return b ?? "";
            return b == null || b == a ? a : a + "·" + b;
        }

        // The number on the spot's jersey (the offense's).
        public static int SpotNumber(GameConfig cfg, int spot) =>
            spot >= 0 && spot < cfg.formation.offense.Count ? cfg.formation.offense[spot].number : 0;

        // ---- the schedule ----

        [JsonIgnore] public bool Over => week >= opponents.Count;
        public bool ClubHome(int game) => (game & 1) == 0;   // (home first, then away, turn about)

        // The next game, or null when the season is over.
        public SeasonMatch Next()
        {
            if (Over) return null;
            bool home = ClubHome(week);
            string opp = opponents[week];
            return new SeasonMatch { Week = week, Opponent = opp, ClubHome = home, Away = home ? opp : club, Home = home ? club : opp };
        }

        [JsonIgnore] public int Wins { get { int n = 0; foreach (var sc in scores) if (sc.club > sc.them) n++; return n; } }
        [JsonIgnore] public int Losses { get { int n = 0; foreach (var sc in scores) if (sc.club < sc.them) n++; return n; } }
        [JsonIgnore] public int Ties { get { int n = 0; foreach (var sc in scores) if (sc.club == sc.them) n++; return n; } }

        // "3-1", or "3-1-1" with a tie.
        [JsonIgnore] public string RecordText => Wins + "-" + Losses + (Ties > 0 ? "-" + Ties : "");

        // The next game's final score goes into the record; the club's players' lines (Stats, by their Recruit) go
        // onto its recruits; the coins it earned (returned) are added. Nothing happens (0) when the season is over.
        public int Record(GameConfig cfg, int clubScore, int themScore, IEnumerable<PlayerLine> clubLines)
        {
            if (Over) return 0;
            var c = cfg.season;
            scores.Add(new GameScore { club = clubScore, them = themScore });
            int earned = clubScore > themScore ? c.coinsWin : clubScore < themScore ? c.coinsLoss : c.coinsTie;
            foreach (var r in lineup) if (r != null) r.games++;
            if (clubLines != null)
                foreach (var l in clubLines)
                {
                    if (l == null) continue;
                    earned += c.coinsPerTd * l.Tds + c.coinsPerPick * l.Picks + c.coinsPerSack * l.Sacks;
                    var r = l.Recruit >= 0 && l.Recruit < lineup.Count ? lineup[l.Recruit] : null;
                    if (r == null) continue;
                    r.yards += l.Yards; r.tds += l.Tds; r.tackles += l.Tackles; r.picks += l.Picks; r.sacks += l.Sacks;
                }
            coins += earned;
            week++;
            return earned;
        }

        // A finished season goes into the history; the same club, eleven, coaches and coins start the next one.
        public bool NextSeason(GameConfig cfg)
        {
            if (!Over) return false;
            seasons++;
            history.Add("SEASON " + seasons + ": " + RecordText);
            scores.Clear();
            week = 0;
            Schedule(cfg);
            return true;
        }

        // ---- the front office ----

        public int SignCost(GameConfig cfg, Recruit r) => r == null ? 0 : Math.Max(0, r.Total - cfg.season.signFree) * cfg.season.signCostPerPoint;

        // A free agent joins at his spot (the one there is cut); a new face takes his place on the table. False
        // when the coins don't stretch to him.
        public bool Sign(GameConfig cfg, int offer, int spot)
        {
            if (offer < 0 || offer >= offers.Count || offers[offer] == null || spot < 0 || spot >= lineup.Count) return false;
            var r = offers[offer];
            int cost = SignCost(cfg, r);
            if (cost > coins) return false;
            coins -= cost;
            lineup[spot] = r;
            var rng = NextRng();
            offers[offer] = Draft(cfg, rng, rng.Below(Math.Max(1, lineup.Count)));
            return true;
        }

        // A recruit leaves; a walk-on plays his spot.
        public bool Cut(int spot)
        {
            if (spot < 0 || spot >= lineup.Count || lineup[spot] == null) return false;
            lineup[spot] = null;
            return true;
        }

        // What +1 on this stat costs him (-1: at the ceiling, or no recruit there).
        public int BoostCost(GameConfig cfg, Recruit r, string stat)
        {
            if (r == null || !r.Rated || r.Stat(stat) >= StatMax(cfg)) return -1;
            return cfg.season.boostCostBase + cfg.season.boostCostStep * r.boosts;
        }

        public bool Boost(GameConfig cfg, int spot, string stat)
        {
            var r = spot >= 0 && spot < lineup.Count ? lineup[spot] : null;
            int cost = BoostCost(cfg, r, stat);
            if (cost < 0 || cost > coins) return false;
            coins -= cost;
            r.SetStat(stat, r.Stat(stat) + 1);
            r.boosts++;
            return true;
        }

        public int Level(string line) => coaching.TryGetValue(line, out int l) ? l : 0;

        public static int MaxLevel(GameConfig cfg, string line) => cfg.season.coaching.TryGetValue(line, out var c) ? c.costs.Count : 0;

        // What the next level of a coaching line costs (-1: maxed, or no such line).
        public int UpgradeCost(GameConfig cfg, string line)
        {
            if (!cfg.season.coaching.TryGetValue(line, out var c)) return -1;
            int l = Level(line);
            return l < c.costs.Count ? c.costs[l] : -1;
        }

        public bool Upgrade(GameConfig cfg, string line)
        {
            int cost = UpgradeCost(cfg, line);
            if (cost < 0 || cost > coins) return false;
            coins -= cost;
            coaching[line] = Level(line) + 1;
            return true;
        }

        // NEW FACES: a fresh table of free agents, for scoutCost.
        public bool Scout(GameConfig cfg)
        {
            if (cfg.season.scoutCost > coins) return false;
            coins -= cfg.season.scoutCost;
            RefreshOffers(cfg, NextRng());
            return true;
        }

        void RefreshOffers(GameConfig cfg, Rng rng)
        {
            offers.Clear();
            int n = Math.Max(0, cfg.season.offers), spots = Math.Max(1, cfg.players.perTeam);
            for (int i = 0; i < n; i++) offers.Add(Draft(cfg, rng, rng.Below(spots)));
        }

        // The spot a free agent fits best: the one whose positions' ratings he is nearest (ties: the lowest spot).
        public int BestSpot(GameConfig cfg, Recruit r)
        {
            int best = 0, bestGap = int.MaxValue, neutral = (int)cfg.ratings.neutral;
            for (int spot = 0; spot < lineup.Count; spot++)
            {
                PosRating off = PosOf(cfg, cfg.formation.offense, spot), def = PosOf(cfg, cfg.formation.defense, spot);
                int Mid(int a, int b) => (a + b + 1) >> 1;
                int gap = Math.Abs(r.speed - Mid(off?.speed ?? neutral, def?.speed ?? neutral))
                    + Math.Abs(r.size - Mid(off?.size ?? neutral, def?.size ?? neutral))
                    + Math.Abs(r.hands - Mid(off?.hands ?? neutral, def?.hands ?? neutral));
                if (gap < bestGap) { bestGap = gap; best = spot; }
            }
            return best;
        }

        // The club's playbook at its PLAYBOOK level: every play listed up to that level (null: no list, every play).
        public HashSet<string> Book(GameConfig cfg)
        {
            if (!cfg.season.coaching.TryGetValue(PlaybookLine, out var c) || c.plays == null || c.plays.Count == 0) return null;
            var book = new HashSet<string>(StringComparer.Ordinal);
            int l = Level(PlaybookLine);
            for (int i = 0; i < c.plays.Count && i <= l; i++) if (c.plays[i] != null) foreach (var id in c.plays[i]) book.Add(id);
            return book;
        }

        // ---- the game ----

        // What the sim needs for one game: each team's recruits by spot (the club's eleven; the visitors' names and
        // looks, unrated), the club's coaching levels and playbook. Club is the club's index in cfg.teams (-1: the
        // club isn't playing, so only the names show).
        public SeasonLive Live(GameConfig cfg)
        {
            var live = new SeasonLive { Roster = new Recruit[cfg.teams.Count][], Coaching = new SortedDictionary<string, int>(coaching, StringComparer.Ordinal), Book = Book(cfg) };
            for (int t = 0; t < cfg.teams.Count; t++)
            {
                if (string.Equals(cfg.teams[t].@short, club, StringComparison.OrdinalIgnoreCase))
                {
                    live.Club = t;
                    live.Roster[t] = lineup.ToArray();
                }
                else live.Roster[t] = Visitors(cfg, week, t).ToArray();
            }
            return live;
        }

        // The other team's eleven for a week: names and looks only (drawn from the season's seed, so the same faces
        // show in a replayed week), no ratings of their own.
        public List<Recruit> Visitors(GameConfig cfg, int game, int team)
        {
            var rng = new Rng(seed - game - 1 - team);
            var list = new List<Recruit>();
            var first = cfg.season.firstNames; var last = cfg.season.lastNames;
            for (int spot = 0; spot < cfg.players.perTeam; spot++)
            {
                string f = first != null && first.Count > 0 ? first[rng.Below(first.Count)] : "";
                string l = last != null && last.Count > 0 ? last[rng.Below(last.Count)] : "";
                list.Add(new Recruit { id = -(spot + 1), name = (f + " " + l).Trim(), skin = rng.Below(Count(cfg.marks.skinTones)), hair = rng.Below(Count(cfg.marks.hairColors)) });
            }
            return list;
        }

        // ---- keeping it ----

        public string Save() => JsonConvert.SerializeObject(this, Formatting.Indented);

        // Save's text back into a season, or null when it isn't one (garbage, a club no longer in the league, an
        // eleven of the wrong size). Coaching lines added to game.json since start at level 0.
        public static Season Load(string text, GameConfig cfg)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Season s;
            try { s = JsonConvert.DeserializeObject<Season>(text); }
            catch (Exception) { return null; }
            if (s == null || TeamPick.Find(cfg, s.club) < 0) return null;
            if (s.lineup == null || s.lineup.Count != cfg.players.perTeam) return null;
            if (s.opponents == null) return null;
            foreach (var o in s.opponents) if (TeamPick.Find(cfg, o) < 0) return null;
            if (s.scores == null) s.scores = new List<GameScore>();
            foreach (var sc in s.scores) if (sc == null) return null;
            if (s.week < 0 || s.week > s.opponents.Count || s.week != s.scores.Count) return null;
            if (s.offers == null) s.offers = new List<Recruit>();
            if (s.coaching == null) s.coaching = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in cfg.season.coaching) if (!s.coaching.ContainsKey(kv.Key)) s.coaching[kv.Key] = 0;
            if (s.history == null) s.history = new List<string>();
            return s;
        }
    }

    // One game's final score, the club's points first.
    public sealed class GameScore
    {
        public int club, them;
    }

    // One game of the season: which week, who the club plays, and the two teams the TV wears (away first).
    public sealed class SeasonMatch
    {
        public int Week;
        public string Opponent;
        public bool ClubHome;
        public string Away, Home;
    }

    // The season as the sim sees it for one game (World.Season; nothing here changes during the game).
    public sealed class SeasonLive
    {
        public int Club = -1;                                 // the club's index in cfg.teams (-1: not playing)
        public Recruit[][] Roster;                            // [team][recruit]: who wears each spot (null: a walk-on)
        public SortedDictionary<string, int> Coaching;        // the club's coaching levels
        public HashSet<string> Book;                          // the club's playbook (null: every play)
    }

    // How the season reaches into a game: a recruit's own ratings (Ratings.Of), the club's stamina coaching (Mover)
    // and its playbook (Huddle.Playbook). All of it is a no-op when World.Season is null.
    public static class SeasonRules
    {
        // The recruit a player wears (null: none, a walk-on, or no season).
        public static Recruit RecruitOf(World w, Player p)
        {
            var s = w.Season;
            if (s == null || p == null || p.Recruit < 0 || p.Team < 0 || s.Roster == null || p.Team >= s.Roster.Length) return null;
            var team = s.Roster[p.Team];
            return team != null && p.Recruit < team.Length ? team[p.Recruit] : null;
        }

        public static string NameOf(World w, Player p) => RecruitOf(w, p)?.name;

        // A rated recruit's own numbers, else null (the position's).
        public static PosRating Rating(World w, Player p) => RecruitOf(w, p)?.Rating();

        static int Level(World w, Player p, string line)
        {
            var s = w.Season;
            if (s == null || p == null || s.Club < 0 || p.Team != s.Club || s.Coaching == null) return 0;
            return s.Coaching.TryGetValue(line, out int l) ? l : 0;
        }

        static float PerLevel(World w, string line) => w.Cfg.season.coaching.TryGetValue(line, out var c) ? c.perLevel : 0f;

        // Mover: the share of the plain sprint drain this man pays (SPRINT coaching: less).
        public static float SprintDrain(World w, Player p)
        {
            int l = Level(w, p, Season.SprintLine);
            return l <= 0 ? 1f : Math.Max(0f, 1f - PerLevel(w, Season.SprintLine) * l);
        }

        // Mover: how much faster his stamina comes back (RECOVERY coaching).
        public static float Recovery(World w, Player p)
        {
            int l = Level(w, p, Season.RecoveryLine);
            return l <= 0 ? 1f : 1f + PerLevel(w, Season.RecoveryLine) * l;
        }

        // Huddle: whether a team may call a play. The club calls its book (kicks always); everyone else, everything.
        public static bool InBook(World w, int team, PlayConfig play)
        {
            var s = w.Season;
            if (s == null || team != s.Club || s.Book == null || play == null) return true;
            return Kicking.IsKickPlay(w, play) || s.Book.Contains(play.id);
        }
    }
}
