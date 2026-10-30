using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // Which track the moment wants (FEATURES.music). Pure C#: the EditMode tests ask it with a World and no frames.
    //   the title screen, or the lobby waiting for KICK OFF  -> title (a marching-band loop)
    //   the game is over                                    -> postgame (a fanfare, once)
    //   a huddle                                            -> huddle (a quiet loop under the play calling)
    //   anything else (a live play, the whistle)           -> nothing: the crowd and the hits have the stage
    public static class MusicDirector
    {
        public static bool On(GameConfig cfg) => cfg.Feature("music");

        public static string Pick(GameConfig cfg, World w, bool title, bool lobby, bool enabled)
        {
            if (!On(cfg) || !enabled) return null;
            string want;
            if (title || lobby) want = "title";
            else if (w != null && Clock.IsOver(w)) want = "postgame";
            else if (w != null && w.Phase == Phase.Huddle && Huddle.On(w)) want = "huddle";
            else return null;
            return cfg.music.tracks.ContainsKey(want) ? want : null;
        }
    }

    // Music on the TV (FEATURES.music, NO_MUSIC=1 turns it off; SETTINGS > MUSIC switches it for every run). The tracks
    // are audio files under Resources (music.tracks.<id>.file); each frame MusicDirector names the one the moment wants
    // and two sources cross-fade to it over music.fadeS. A missing file is silence. The fanfare plays once and is not started again while it is
    // still wanted. The pause menu stops the game's sound, not the music. PartyHost drives it with one line (Drive).
    public sealed class Music : MonoBehaviour
    {
        const string Key = "pp.music";   // 1 on (the default), 0 off

        public static bool Enabled => PlayerPrefs.GetInt(Key, 1) != 0;

        // The SETTINGS row (the title's and the pause menu's).
        public static void Toggle()
        {
            PlayerPrefs.SetInt(Key, Enabled ? 0 : 1);
            PlayerPrefs.Save();
        }

        GameConfig cfg;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource now, old;
        string playing;      // the track now's source plays (null: silence)
        string finished;     // a one-shot that has played to its end and is still wanted

        public string Playing => playing;

        // From PartyHost.Update, after the World stepped. title: the title screen is up; lobby: the lobby waits.
        public static Music Drive(GameObject on, GameConfig cfg, World w, bool title, bool lobby)
        {
            var m = on.GetComponent<Music>();
            if (!MusicDirector.On(cfg) && m == null) return null;
            if (m == null) { m = on.AddComponent<Music>(); m.Setup(cfg); }
            m.Frame(MusicDirector.Pick(cfg, w, title, lobby, Enabled));
            return m;
        }

        void Setup(GameConfig c)
        {
            cfg = c;
            foreach (var kv in cfg.music.tracks)
            {
                var clip = string.IsNullOrEmpty(kv.Value.file) ? null : Resources.Load<AudioClip>(kv.Value.file);
                if (clip != null) clips[kv.Key] = clip;
                else Debug.Log("[music] no file for " + kv.Key + " (Resources/" + kv.Value.file + "): silence");
            }
            now = Source();
            old = Source();
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }

        AudioSource Source()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;   // (the pause menu pauses the game's sound; the music carries on under it)
            s.volume = 0f;
            return s;
        }

        float Level(string id) => id != null && cfg.music.tracks.TryGetValue(id, out var t)
            ? Mathf.Clamp01(cfg.music.master) * Mathf.Clamp01(t.level) : 0f;

        void Frame(string want)
        {
            if (want != finished) finished = null;
            if (want != null && want == finished) want = null;   // the fanfare played; stay quiet until the moment changes
            if (want != playing)
            {
                // the old track fades out on the spare source, the new one starts from the top
                (now, old) = (old, now);
                playing = want;
                if (want != null && clips.TryGetValue(want, out var clip))
                {
                    now.clip = clip;
                    now.loop = cfg.music.tracks[want].loop;
                    now.volume = 0f;
                    now.Play();
                }
                else now.Stop();
            }
            float step = cfg.music.fadeS > 0f ? Time.unscaledDeltaTime / cfg.music.fadeS : 1f;
            float target = Level(playing);
            now.volume = Mathf.MoveTowards(now.volume, target, step * Mathf.Max(target, 0.01f));
            old.volume = Mathf.MoveTowards(old.volume, 0f, step);
            if (old.volume <= 0f && old.isPlaying) old.Stop();
            // a one-shot that reached its end: remembered, so it is not started again
            if (playing != null && !now.loop && !now.isPlaying && now.clip != null && now.time == 0f) { finished = playing; playing = null; }
        }

    }
}
