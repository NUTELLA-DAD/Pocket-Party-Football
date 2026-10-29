using System.Collections.Generic;
using System.Text;

namespace PocketFootball.Sim
{
    // FEATURES.cup (NO_CUP=1 hides it): the Pocket Cup, a knockout bracket for the whole league. The teams are drawn
    // into seeds (1 is the best), the bracket is the smallest power of two that holds them, and the seeds fill it the
    // usual way (with 8 slots: 1 v 8, 4 v 5, 2 v 7, 3 v 6), so when the league doesn't fill the bracket the top seeds
    // get the empty slots: a bye straight into the next round. Six teams: seeds 1 and 2 sit out the first round,
    // 3 v 6 and 4 v 5 play the quarterfinals, then two semifinals and the final (five games). Four teams: two
    // semifinals and a final. The better seed is the home team; a tie sends the better seed through.
    // Teams are named by their short code. Pure: Boot/CupHost.cs keeps it between games (Save / Load).
    public sealed class Cup
    {
        public readonly List<string> Seeds;   // short codes, seed 1 first
        public readonly int Size;             // slots in the first round (a power of two)
        public readonly int Rounds;           // the final is round Rounds - 1
        readonly int[] order;                 // the seed number (1 = best) in each first-round slot
        readonly List<string[]> won;          // won[round][match]: the winner's code, null while unplayed

        public Cup(List<string> seeds)
        {
            Seeds = new List<string>(seeds);
            int size = 1, rounds = 0;
            while (size < Seeds.Count) { size += size; rounds++; }
            Size = size;
            Rounds = rounds;
            order = SlotOrder(size);
            won = new List<string[]>();
            for (int r = 0, n = size; r < rounds; r++) { n >>= 1; won.Add(new string[n]); }
        }

        // A new cup for these teams: drawn into seeds by the seeded Rng when draw is on, else in the order given.
        public static Cup Draw(List<string> teams, int seed, bool draw)
        {
            var list = new List<string>(teams);
            if (draw)
            {
                var rng = new Rng(seed);
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = rng.Below(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
            return new Cup(list);
        }

        // 1 v size, then each half folded the same way: [1], [1,2], [1,4,2,3], [1,8,4,5,2,7,3,6].
        static int[] SlotOrder(int size)
        {
            var o = new List<int> { 1 };
            while (o.Count < size)
            {
                int sum = o.Count + o.Count + 1;
                var next = new List<int>();
                foreach (int s in o) { next.Add(s); next.Add(sum - s); }
                o = next;
            }
            return o.ToArray();
        }

        // A team that joined the league after the draw takes the next empty slot as the lowest seed, so the top seed
        // it was a bye for now has a game. Only while that bye's winner hasn't played the next round; returns false
        // (and changes nothing) when the team is already in, the bracket is full, or that slot's game is past.
        public bool AddTeam(string code)
        {
            if (string.IsNullOrEmpty(code) || SeedOf(code) > 0 || Seeds.Count >= Size) return false;
            int seed = Seeds.Count + 1, slot = System.Array.IndexOf(order, seed), match = slot >> 1;
            if (won[0][match] != null) return false;
            if (Rounds > 1 && won[1][match >> 1] != null) return false;
            Seeds.Add(code);
            return true;
        }

        public int Matches(int round) => round >= 0 && round < Rounds ? won[round].Length : 0;

        // 1 for the best seed, 0 for a code not in the cup.
        public int SeedOf(string code)
        {
            for (int i = 0; i < Seeds.Count; i++) if (Seeds[i] == code) return i + 1;
            return 0;
        }

        // One side of a match (0 the top, 1 the bottom): a team's code, or null for a bye or a game not played yet.
        public string Team(int round, int match, int side)
        {
            if (round <= 0)
            {
                int seed = order[match + match + side];
                return seed <= Seeds.Count ? Seeds[seed - 1] : null;
            }
            return Winner(round - 1, match + match + side);
        }

        // A first-round slot with no team in it (the other side goes straight through).
        public bool IsBye(int round, int match, int side) => round == 0 && order[match + match + side] > Seeds.Count;

        // Who came out of a match: its result, the team facing a bye, or null when it is still to play.
        public string Winner(int round, int match)
        {
            if (round < 0 || round >= Rounds) return null;
            if (won[round][match] != null) return won[round][match];
            if (round == 0)
            {
                bool byeA = IsBye(0, match, 0), byeB = IsBye(0, match, 1);
                if (byeA && !byeB) return Team(0, match, 1);
                if (byeB && !byeA) return Team(0, match, 0);
            }
            return null;
        }

        public string Champion => Rounds == 0 ? (Seeds.Count == 1 ? Seeds[0] : null) : Winner(Rounds - 1, 0);
        public bool Over => Champion != null;

        // The next game to play (round by round, top to bottom), or null when the cup is over.
        public CupMatch Next()
        {
            for (int r = 0; r < Rounds; r++)
                for (int m = 0; m < won[r].Length; m++)
                {
                    if (Winner(r, m) != null) continue;
                    string a = Team(r, m, 0), b = Team(r, m, 1);
                    if (a == null || b == null) continue;
                    bool aHome = SeedOf(a) < SeedOf(b);
                    return new CupMatch { Round = r, Index = m, Away = aHome ? b : a, Home = aHome ? a : b };
                }
            return null;
        }

        // The next game's result: the winner from the final score (a tie: the home team, the better seed). Returns
        // the winner's code, or null when there is no game to play.
        public string Record(int awayScore, int homeScore)
        {
            var m = Next();
            if (m == null) return null;
            string w = awayScore > homeScore ? m.Away : m.Home;
            won[m.Round][m.Index] = w;
            return w;
        }

        // "FINAL", "SEMIFINALS", "QUARTERFINALS", else "ROUND n".
        public string RoundName(int round)
        {
            int fromEnd = Rounds - 1 - round;
            return fromEnd >= 0 && fromEnd < Names.Length ? Names[fromEnd] : "ROUND " + (round + 1);
        }

        static readonly string[] Names = { "FINAL", "SEMIFINALS", "QUARTERFINALS" };

        // "ARR,GRN,CAC,WHL;,GRN/,;" : the seeds, then each round's winners (empty while unplayed), rounds split by '/'.
        public string Save()
        {
            var sb = new StringBuilder(string.Join(",", Seeds));
            sb.Append(';');
            for (int r = 0; r < Rounds; r++)
            {
                if (r > 0) sb.Append('/');
                for (int m = 0; m < won[r].Length; m++)
                {
                    if (m > 0) sb.Append(',');
                    sb.Append(won[r][m] ?? "");
                }
            }
            return sb.ToString();
        }

        // Save's text back into a cup, or null when it isn't one (a winner who wasn't in that game, say).
        public static Cup Load(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int semi = text.IndexOf(';');
            if (semi <= 0) return null;
            var seeds = new List<string>(text.Substring(0, semi).Split(','));
            foreach (var s in seeds) if (s.Length == 0) return null;
            if (new HashSet<string>(seeds).Count != seeds.Count) return null;
            var cup = new Cup(seeds);
            string rest = text.Substring(semi + 1);
            if (cup.Rounds == 0) return rest.Length == 0 ? cup : null;
            var rounds = rest.Split('/');
            if (rounds.Length != cup.Rounds) return null;
            for (int r = 0; r < cup.Rounds; r++)
            {
                var cells = rounds[r].Split(',');
                if (cells.Length != cup.won[r].Length) return null;
                for (int m = 0; m < cells.Length; m++)
                {
                    if (cells[m].Length == 0) continue;
                    if (cells[m] != cup.Team(r, m, 0) && cells[m] != cup.Team(r, m, 1)) return null;
                    cup.won[r][m] = cells[m];
                }
            }
            return cup;
        }
    }

    // One game of the cup: where it sits in the bracket and who plays (the better seed at home).
    public sealed class CupMatch
    {
        public int Round, Index;
        public string Away, Home;
    }

    // game.json "cup": the most teams in a cup (the league's first ones), and whether the seeds are drawn at random.
    public sealed class CupConfig
    {
        public int maxTeams;
        public bool randomDraw;
    }
}
