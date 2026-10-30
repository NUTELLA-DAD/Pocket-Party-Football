using System;
using System.Collections.Generic;
using System.Globalization;
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

    // The tracks, made from their notes (MusicConfig) at a sample rate: sine-wave instruments and seeded noise drums,
    // the same samples every time. A track is mono, between -0.9 and 0.9; a looping one wraps its tails round to the
    // start so the loop has no click.
    public static class MusicSynth
    {
        const float Tau = (float)(Math.PI * 2.0);

        public struct Note { public float Beat, Beats, Freq, Vel; }

        // "C5:1 -:0.5 F#4:2" -> the notes, repeated until `beats` (a rest has no note). Unknown tokens are skipped.
        public static List<Note> Notes(string line, float beats)
        {
            var list = new List<Note>();
            var toks = (line ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var once = new List<(float freq, float len)>();
            float total = 0f;
            foreach (var t in toks)
            {
                int c = t.IndexOf(':');
                if (c <= 0 || !float.TryParse(t.Substring(c + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float len) || len <= 0f) continue;
                string pitch = t.Substring(0, c);
                once.Add((pitch == "-" ? 0f : Freq(pitch), len));
                total += len;
            }
            if (total <= 0f) return list;
            for (float at = 0f; at < beats - 1e-4f; )
                foreach (var (freq, len) in once)
                {
                    if (at >= beats - 1e-4f) break;
                    if (freq > 0f) list.Add(new Note { Beat = at, Beats = Math.Min(len, beats - at), Freq = freq, Vel = 1f });
                    at += len;
                }
            return list;
        }

        // "x...X..." -> one hit a 16th per x (X loud), repeated until `beats`.
        public static List<Note> Hits(string line, float beats)
        {
            var list = new List<Note>();
            string s = line ?? "";
            if (s.Length == 0) return list;
            int steps = (int)Math.Round(beats * 4f);
            for (int i = 0; i < steps; i++)
            {
                char ch = s[i % s.Length];
                if (ch == 'x' || ch == 'X') list.Add(new Note { Beat = i * 0.25f, Beats = 0.25f, Freq = 0f, Vel = ch == 'X' ? 1f : 0.7f });
            }
            return list;
        }

        // "A4" = 440 Hz; "C4" middle C; sharps "#", flats "b".
        public static float Freq(string pitch)
        {
            if (string.IsNullOrEmpty(pitch)) return 0f;
            int pc = "C D EF G A B".IndexOf(char.ToUpperInvariant(pitch[0]));
            if (pc < 0) return 0f;
            int i = 1;
            if (i < pitch.Length && pitch[i] == '#') { pc++; i++; }
            else if (i < pitch.Length && pitch[i] == 'b') { pc--; i++; }
            if (!int.TryParse(pitch.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int oct)) return 0f;
            int midi = (oct + 1) * 12 + pc;
            return 440f * (float)Math.Pow(2.0, (midi - 69) / 12.0);
        }

        static bool IsDrum(string inst) => inst == "kick" || inst == "snare" || inst == "hat" || inst == "crash";

        public static float Seconds(MusicTrack t) => t.bpm > 0f ? t.beats * 60f / t.bpm : 0f;

        public static float[] Render(MusicTrack t, int rate)
        {
            float spb = t.bpm > 0f ? 60f / t.bpm : 0.5f;
            int n = Math.Max(1, (int)(Seconds(t) * rate));
            var d = new float[n];
            uint seed = 29u;
            foreach (var v in t.voices)
            {
                bool drum = IsDrum(v.instrument);
                var notes = drum ? Hits(v.notes, t.beats) : Notes(v.notes, t.beats);
                var noise = new Noise(seed++);
                foreach (var note in notes)
                {
                    int start = (int)(note.Beat * spb * rate);
                    float len = note.Beats * spb;
                    float tail = Tail(v.instrument);
                    int count = (int)((len + tail) * rate);
                    for (int i = 0; i < count; i++)
                    {
                        int at = start + i;
                        if (at >= n) { if (!t.loop) break; at %= n; }
                        float s = Voice(v.instrument, note.Freq, i / (float)rate, len, noise);
                        d[at] += s * v.level * note.Vel;
                    }
                }
            }
            Normalize(d, 0.9f);
            return d;
        }

        // How long an instrument rings past the end of its note, seconds.
        static float Tail(string inst)
        {
            switch (inst)
            {
                case "kick": return 0.3f;
                case "snare": return 0.25f;
                case "hat": return 0.08f;
                case "crash": return 2.2f;
                case "pluck": return 0.3f;
                case "pad": return 0.4f;
                default: return 0.08f;
            }
        }

        // One sample of one instrument, `t` seconds into a note that lasts `len`.
        static float Voice(string inst, float f, float t, float len, Noise noise)
        {
            switch (inst)
            {
                case "brass":
                {
                    float env = Math.Min(1f, t / 0.025f) * Release(t, len, 0.08f) * (0.8f + 0.2f * Exp(t, 6f));
                    float v = 0f;
                    for (int h = 1; h <= 6; h++) v += (float)Math.Sin(Tau * f * h * t) / h;
                    return v * env;
                }
                case "pluck":
                {
                    float env = Math.Min(1f, t / 0.004f) * Exp(t, 7f);
                    return ((float)Math.Sin(Tau * f * t) + 0.35f * (float)Math.Sin(Tau * f * 2f * t) + 0.15f * (float)Math.Sin(Tau * f * 3f * t)) * env;
                }
                case "pad":
                {
                    float env = Math.Min(1f, t / 0.25f) * Release(t, len, 0.4f);
                    return ((float)Math.Sin(Tau * f * t) + 0.5f * (float)Math.Sin(Tau * f * 1.003f * t) + 0.25f * (float)Math.Sin(Tau * f * 2f * t)) * env;
                }
                case "bass":
                {
                    float env = Math.Min(1f, t / 0.008f) * Release(t, len, 0.06f) * (0.6f + 0.4f * Exp(t, 5f));
                    return ((float)Math.Sin(Tau * f * t) + 0.45f * (float)Math.Sin(Tau * f * 2f * t)) * env;
                }
                case "kick":
                {
                    // a thump that drops from 130 to 45 Hz (its phase, the integral of that sweep)
                    float ph = Tau * (45f * t + (130f - 45f) * (1f - Exp(t, 22f)) / 22f);
                    return (float)Math.Sin(ph) * Exp(t, 9f);
                }
                case "snare":
                    return (0.8f * noise.Next() + 0.5f * (float)Math.Sin(Tau * 190f * t)) * Exp(t, 20f);
                case "hat":
                    return noise.High() * Exp(t, 70f) * 0.6f;
                case "crash":
                    return noise.High() * Exp(t, 1.8f) * 0.7f;
                default:
                    return 0f;
            }
        }

        static float Release(float t, float len, float r) => t <= len ? 1f : Math.Max(0f, 1f - (t - len) / r);

        static float Exp(float t, float rate) => (float)Math.Exp(-t * rate);

        sealed class Noise
        {
            uint s;
            float prev;
            public Noise(uint seed) { s = seed == 0 ? 1u : seed; }
            public float Next()
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                return s / (float)uint.MaxValue * 2f - 1f;
            }
            // the noise with its lows taken out (hats and cymbals)
            public float High() { float x = Next(); float h = x - prev; prev = x; return h; }
        }

        static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            foreach (float v in d) max = Math.Max(max, Math.Abs(v));
            if (max <= 0f) return;
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }

    // Music on the TV (FEATURES.music, NO_MUSIC=1 turns it off; SETTINGS > MUSIC switches it for every run). The tracks
    // are made once a run (kept between scene loads), then each frame MusicDirector names the one the moment wants and
    // two sources cross-fade to it over music.fadeS. The fanfare plays once and is not started again while it is
    // still wanted. The pause menu stops the game's sound, not the music. PartyHost drives it with one line (Drive).
    public sealed class Music : MonoBehaviour
    {
        const string Key = "pp.music";   // 1 on (the default), 0 off
        static readonly Dictionary<string, float[]> made = new Dictionary<string, float[]>();
        static int madeRate;

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
            int rate = Mathf.Max(8000, cfg.music.sampleRate);
            if (madeRate != rate) { made.Clear(); madeRate = rate; }
            foreach (var kv in cfg.music.tracks)
            {
                if (!made.TryGetValue(kv.Key, out var data)) { data = MusicSynth.Render(kv.Value, rate); made[kv.Key] = data; }
                var clip = AudioClip.Create("music-" + kv.Key, data.Length, 1, rate, false);
                clip.SetData(data, 0);
                clips[kv.Key] = clip;
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

        void OnDestroy()
        {
            foreach (var c in clips.Values) if (c != null) Destroy(c);
        }
    }
}
