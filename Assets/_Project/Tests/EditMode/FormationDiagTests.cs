using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PocketFootball.Sim;

namespace PocketFootball.Tests.EditMode
{
    // A tuning aid for FEATURES.formations, run by name only: every offense play against every defense, many seeded
    // 22-bot snaps, with the formations off / offense only / defense only / both. Prints yards a snap and results.
    public sealed class FormationDiagTests
    {
        static readonly string[] Offense = { "dive", "toss", "sneak", "slants", "verts", "boot", "screen" };
        static readonly string[] Defense = { "cover2", "cover3", "mblitz", "zblitz", "goalline", "prevent" };
        const int Seeds = 8;

        [Test, Explicit("tuning aid")]
        public void FormationsYardsByPlay()
        {
            var sb = new StringBuilder();
            foreach (var mode in new[] { "off", "offense", "defense", "both" })
            {
                var cfg = ConfigTests.Load();
                foreach (var p in cfg.plays)
                {
                    bool keep = mode == "both" || (mode == "offense" && p.side == "offense") || (mode == "defense" && p.side == "defense");
                    if (!keep) p.formation = null;
                }
                sb.Append("== formations ").Append(mode).Append('\n');
                foreach (var o in Offense)
                {
                    float yards = 0f; int n = 0;
                    var results = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
                    var line = new StringBuilder();
                    foreach (var d in Defense)
                    {
                        float dy = 0f;
                        for (int seed = 1; seed <= Seeds; seed++)
                        {
                            float y = OneSnap(cfg, o, d, seed, out string r);
                            dy += y; yards += y; n++;
                            results[r] = (results.TryGetValue(r, out int k) ? k : 0) + 1;
                        }
                        line.Append(' ').Append(d).Append(' ').Append((dy / Seeds).ToString("0.0"));
                    }
                    sb.Append(o).Append(": ").Append((yards / n).ToString("0.0")).Append(" a snap |").Append(line).Append(" |");
                    foreach (var kv in results) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value);
                    sb.Append('\n');
                }
            }
            TestContext.WriteLine(sb.ToString());
            UnityEngine.Debug.Log("[formdiag]\n" + sb);
        }

        static float OneSnap(GameConfig cfg, string offense, string defense, int seed, out string result)
        {
            var w = WorldTests.Full(cfg, seed);
            w.Step(null);
            w.Calls[w.Offense] = offense; w.Calls[w.Defense] = defense;
            int hz = cfg.sim.tickHz;
            for (int i = 0; i < hz * 40 && w.Phase != Phase.Live; i++) w.Step(null);
            float los = w.LineOfScrimmageX, dir = w.Dir;
            int limit = (int)((cfg.play.maxPlayS + 1f) * hz);
            for (int i = 0; i < limit && w.Phase == Phase.Live; i++) w.Step(null);
            result = string.IsNullOrEmpty(w.Result) ? "?" : w.Result.Replace(' ', '_');
            if (w.Thrown) result = "pass:" + result;
            return (w.NextLos - los) * dir;
        }
    }
}
