using System;
using System.Collections.Generic;
using System.IO;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketFootball.Boot
{
    // The season between games (FEATURES.season, NO_SEASON=1 hides it; the season itself is Sim/Season.cs). The
    // title's SEASON page is the front office: the eleven, the free agents, the coaches, PLAY NEXT GAME. The season is
    // kept as season.json in Unity's persistent data folder (it survives the scene reloads between games and quitting);
    // pp.season.live (PlayerPrefs) is 1 while the game the TV is about to play (or playing) is the season's next one.
    // That game wears the club and its opponent (Apply, at boot), its eleven wears the recruits (Dress, once the bots
    // are in), and when the clock ends it the result and the coins go into the season (Drive, from PostGameHost) and
    // the post-game screen gets a gold strip: the result, and Enter / (A) / a click back to the front office.
    public static class SeasonHost
    {
        const string LiveKey = "pp.season.live", FileName = "season.json";

        public static SeasonMatch Live { get; private set; }   // the season game being played this boot, else null
        static bool recorded;
        static string result;   // the gold strip's text, once the game is over

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Live = null; recorded = false; result = null; }

        public static bool On(GameConfig cfg) => Season.On(cfg);

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        // The saved season, or null when there is none (or it no longer fits the league: the file stays for a look).
        public static Season Load(GameConfig cfg)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = Season.Load(File.ReadAllText(FilePath), cfg);
                if (s == null) Debug.LogWarning("[season] " + FilePath + " isn't a season for this league; the front office starts fresh");
                return s;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[season] couldn't read " + FilePath + ": " + e.Message);
                return null;
            }
        }

        public static void Save(Season s)
        {
            if (s == null) return;
            try { File.WriteAllText(FilePath, s.Save()); }
            catch (Exception e) { Debug.LogError("[season] couldn't save " + FilePath + ": " + e.Message); }
        }

        // START OVER: the season file goes.
        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogError("[season] couldn't delete " + FilePath + ": " + e.Message); }
            SetLive(false);
            Debug.Log("[season] season wiped");
        }

        // A fresh season for a club (its short code), saved over any season there was.
        public static Season StartNew(GameConfig cfg, string club)
        {
            var s = Season.Start(cfg, club, Environment.TickCount & 0x7fffffff);
            if (s == null) { Debug.LogWarning("[season] no such club: " + club); return null; }
            Save(s);
            Debug.Log("[season] new season: " + s.club + " play " + string.Join(", ", s.opponents));
            return s;
        }

        // The next boot plays the season's next game (true), or a plain game (false: the title's PLAY).
        public static void SetLive(bool on)
        {
            PlayerPrefs.SetInt(LiveKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        // At boot, before anything is built: a season game wears the club and its opponent. The title (or a tool run)
        // never is one.
        public static void Apply(GameConfig cfg, bool title)
        {
            Live = null; recorded = false; result = null;
            if (title || Application.isBatchMode || !On(cfg) || PlayerPrefs.GetInt(LiveKey, 0) != 1) return;
            var s = Load(cfg);
            var m = s?.Next();
            if (m == null) { SetLive(false); return; }
            TeamPick.Apply(cfg, m.Away, m.Home);
            if (cfg.teams.Count < 2 || cfg.teams[0].@short != m.Away || cfg.teams[1].@short != m.Home) return;
            Live = m;
            cfg.overtime.mustWin = true;   // FEATURES.overtime: no ties in a season game, overtime plays on until somebody scores
            Debug.Log("[season] week " + (m.Week + 1) + ": " + m.Away + " at " + m.Home);
        }

        // From PartyHost.Create, once the bots are in: the club's recruits onto its eleven (names, looks, ratings) and
        // the visitors' names onto theirs; the club's coaching and playbook into the world.
        public static void Dress(World w)
        {
            if (Live == null || w == null) return;
            var s = Load(w.Cfg);
            if (s == null) return;
            w.Season = s.Live(w.Cfg);
            foreach (var p in w.Players) w.SetRecruit(p, p.Spot);
            Debug.Log("[season] the eleven are dressed: " + (w.Season.Club >= 0 ? w.Cfg.teams[w.Season.Club].name : "(no club)") + ", playbook " + (w.Season.Book == null ? "all" : w.Season.Book.Count + " plays"));
        }

        // From PostGameHost, every frame: once the clock ends a season game, its score and the club's lines go into
        // the season (once), and the coins into the till.
        public static void Drive(PartyHost h, PostGame g)
        {
            if (Live == null || h.World == null) return;
            var w = h.World;
            if (!recorded && Clock.IsOver(w))
            {
                recorded = true;
                var s = Load(w.Cfg);
                var m = s?.Next();
                if (m != null && m.Week == Live.Week && m.Opponent == Live.Opponent)
                {
                    int club = w.Season != null && w.Season.Club >= 0 ? w.Season.Club : (m.ClubHome ? 1 : 0);
                    int them = club == 0 ? 1 : 0;
                    var lines = new List<PlayerLine>();
                    if (g != null && g.Stats != null) foreach (var l in g.Stats.Lines) if (l.Team == club) lines.Add(l);
                    int earned = s.Record(w.Cfg, w.Score[club], w.Score[them], lines);
                    Save(s);
                    SetLive(false);
                    string name = w.Cfg.teams[club].name.ToUpperInvariant();
                    string verdict = w.Score[club] > w.Score[them] ? name + " WIN" : w.Score[club] < w.Score[them] ? name + " LOSE" : "A TIE";
                    result = "WEEK " + (m.Week + 1) + "  ·  " + verdict + " " + w.Score[club] + "-" + w.Score[them] + "  ·  +" + earned + " COINS";
                    if (s.Over) result += "  ·  SEASON OVER: " + s.RecordText;
                    Debug.Log("[season] " + result);
                }
            }
            if (result == null || g == null || !g.Showing) return;
            var strip = h.GetComponent<CupStrip>();
            if (strip == null) strip = h.gameObject.AddComponent<CupStrip>();
            strip.Heading = "SEASON";
            strip.Hint = "Enter, (A) or click: the front office";
            strip.Press = ToFrontOffice;
            strip.Text = result;
        }

        // Back to the title's SEASON page (a fresh scene: the post-game screen goes).
        public static void ToFrontOffice()
        {
            TitleScreen.ReturnTo("season");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
