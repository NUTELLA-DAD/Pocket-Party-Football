using System;
using System.Collections.Generic;
using PocketFootball.Sim;
using UnityEngine;

namespace PocketFootball.View
{
    // The sounds the TV can make (WS-09). Each one is generated in code (AudioSynth); nothing is downloaded.
    public enum Sound { Whistle, Hit, BigHit, Crowd, Groan, Tick, Sting, Snap, Whoosh, Catch, Chime }

    // Which sound goes with which event from the shared event list (Sim/Events.cs). Every kind has at least one.
    // The huddle clock tick is not an event: AudioDirector reads it off the huddle's countdown, and it turns a big
    // tackle's Hit into a BigHit.
    public static class AudioCues
    {
        public static readonly Sound[] All = (Sound[])Enum.GetValues(typeof(Sound));
        static readonly Sound[] None = new Sound[0];

        public static Sound[] For(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Hike: return new[] { Sound.Snap };
                case EventKind.Handoff: return new[] { Sound.Catch };
                case EventKind.Throw: return new[] { Sound.Whoosh };
                case EventKind.Catch: return new[] { Sound.Catch };
                case EventKind.Incomplete: return new[] { Sound.Groan };
                case EventKind.Interception: return new[] { Sound.Catch, Sound.Crowd };
                case EventKind.Juke: return new[] { Sound.Whoosh };
                case EventKind.MissedTackle: return new[] { Sound.Whoosh };
                case EventKind.Tackle: return new[] { Sound.Hit };
                case EventKind.Stumble: return new[] { Sound.Hit };   // FEATURES.hitAngle: a glancing blow
                case EventKind.Slip: return new[] { Sound.Whoosh };   // FEATURES.weather: a slip on a slick field
                case EventKind.Sack: return new[] { Sound.BigHit, Sound.Crowd };
                case EventKind.Touchdown: return new[] { Sound.Sting, Sound.Crowd };
                case EventKind.OutOfBounds: return new[] { Sound.Hit };
                case EventKind.Whistle: return new[] { Sound.Whistle };
                case EventKind.FirstDown: return new[] { Sound.Chime };
                case EventKind.Turnover: return new[] { Sound.Groan };
                default: return None;
            }
        }

        // The sound's name in the audio.levels block of game.json (bigHit, crowd, ...).
        public static string Key(Sound s)
        {
            string n = s.ToString();
            return char.ToLowerInvariant(n[0]) + n.Substring(1);
        }
    }

    // What to play this frame, from the event list and the huddle clock. Pure C#: the EditMode tests drive it with a
    // World and no frames. Only reads the World. With FEATURES.audio off it says nothing, and when it comes back on it
    // starts from that moment (no backlog of old events).
    public sealed class AudioDirector
    {
        readonly GameConfig cfg;
        readonly List<GameEvent> fresh = new List<GameEvent>();
        readonly List<Sound> cues = new List<Sound>();
        readonly int[] played = new int[AudioCues.All.Length];
        readonly Dictionary<int, float> lastSpeed = new Dictionary<int, float>();   // each player's speed last time
        int nextSeq = -1;
        int tickHuddle = -1, lastTickS = int.MaxValue;

        public AudioDirector(GameConfig cfg) { this.cfg = cfg; }

        public static bool On(GameConfig cfg) => cfg.Feature("audio");

        // How many times a sound has been asked for (tests).
        public int Played(Sound s) => played[(int)s];

        // How loud a sound plays: the master volume times its own level.
        public float Gain(Sound s) => Math.Max(0f, Math.Min(1f, cfg.audio.master)) * Math.Max(0f, Math.Min(1f, cfg.audio.Level(AudioCues.Key(s))));

        // Call once a frame after the World stepped. The sounds to start now, each at most once.
        public List<Sound> Observe(World w)
        {
            cues.Clear();
            if (!On(cfg)) { nextSeq = -1; return cues; }
            if (nextSeq < 0) nextSeq = w.Events.Next;
            fresh.Clear();
            w.Events.Since(nextSeq, fresh);
            nextSeq = w.Events.Next;
            foreach (var e in fresh)
                foreach (var s in AudioCues.For(e.Kind))
                    Add(s == Sound.Hit && e.Kind == EventKind.Tackle && BigTackle(w, e) ? Sound.BigHit : s);
            foreach (var p in w.Players) lastSpeed[p.Slot] = Speed(p);

            // the huddle clock: one tick a second for its last huddleTickFromS seconds
            if (w.Phase == Phase.Huddle && Huddle.On(w))
            {
                if (w.HuddleNumber != tickHuddle) { tickHuddle = w.HuddleNumber; lastTickS = int.MaxValue; }
                int hz = Math.Max(1, cfg.sim.tickHz);
                int left = (w.PhaseTicks + hz - 1) / hz;
                if (left < lastTickS)
                {
                    if (left >= 1 && left <= cfg.audio.huddleTickFromS) Add(Sound.Tick);
                    lastTickS = left;
                }
            }
            return cues;
        }

        static float Speed(Player p) => (float)Math.Sqrt(p.VX * p.VX + p.VZ * p.VZ);

        // A tackle that lands hard: the tackler going at least carrier.bigHitSpeedYdS (the same big hit that can
        // knock the ball loose, Sim/Fumbles.cs), or the carrier stopped at or behind the line of scrimmage. The speed
        // is the faster of now and last frame, because the TV can step a few ticks in a frame and he slows down after.
        // (The line is still the one the play started from: the next huddle moves it.) FEATURES.hitAngle: the sim names
        // how a tackle landed; a BIG HIT is big, a trip or a lost balance is not, however fast the tackler was.
        public bool BigTackle(World w, GameEvent e)
        {
            var tackler = w.Find(e.Slot);
            if (e.Text == "BIG HIT") return true;
            if (tackler != null && string.IsNullOrEmpty(e.Text))
            {
                float speed = Math.Max(Speed(tackler), lastSpeed.TryGetValue(tackler.Slot, out float was) ? was : 0f);
                float big = cfg.carrier.bigHitSpeedYdS;
                if (big > 0f && speed >= big) return true;
            }
            var carrier = w.Find(e.Other);
            float x = carrier != null ? carrier.X : e.X;
            return (x - w.LineOfScrimmageX) * w.Dir <= 0f;
        }

        void Add(Sound s)
        {
            if (cues.Contains(s)) return;
            cues.Add(s);
            played[(int)s]++;
        }
    }

    // The sounds, made from sine waves and noise at a sample rate. Same samples every time (its own seeded noise).
    // Each clip is mono, between -0.9 and 0.9.
    public static class AudioSynth
    {
        const float Tau = (float)(Math.PI * 2.0);

        sealed class Noise
        {
            uint s;
            public Noise(uint seed) { s = seed == 0 ? 1u : seed; }
            public float Next()
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                return s / (float)uint.MaxValue * 2f - 1f;
            }
        }

        public static float[] Make(Sound s, int rate)
        {
            float[] d;
            switch (s)
            {
                case Sound.Whistle: d = Whistle(rate); break;
                case Sound.Hit: d = Hit(rate, 0.3f, 110f, 45f, 18f, 40f, 0.6f, 0f); break;
                case Sound.BigHit: d = Hit(rate, 0.5f, 90f, 32f, 10f, 20f, 0.9f, 0.35f); break;
                case Sound.Crowd: d = Crowd(rate, 2.4f, 7u, false); break;
                case Sound.Groan: d = Crowd(rate, 1.6f, 11u, true); break;
                case Sound.Tick: d = Tick(rate); break;
                case Sound.Sting: d = Sting(rate); break;
                case Sound.Snap: d = Snap(rate); break;
                case Sound.Whoosh: d = Whoosh(rate); break;
                case Sound.Catch: d = Catch(rate); break;
                case Sound.Chime: d = Chime(rate); break;
                default: d = new float[1]; break;
            }
            Normalize(d, 0.9f);
            return d;
        }

        static float[] Buffer(int rate, float seconds) => new float[Math.Max(1, (int)(rate * seconds))];

        // Up from 0 over `a` seconds, down to 0 over the last `r`.
        static float Gate(float t, float len, float a, float r)
        {
            float g = 1f;
            if (a > 0f && t < a) g = t / a;
            if (r > 0f && t > len - r) g = Math.Min(g, Math.Max(0f, (len - t) / r));
            return g;
        }

        static float Exp(float t, float rate) => (float)Math.Exp(-t * rate);

        // A referee's pea whistle: a high note with the pea's rattle in it.
        static float[] Whistle(int rate)
        {
            float len = 0.55f;
            var d = Buffer(rate, len);
            var n = new Noise(3u);
            float ph = 0f, trill = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                trill += Tau * 28f / rate;
                float f = 2900f + 160f * (float)Math.Sin(trill);
                ph += Tau * f / rate;
                float am = 0.7f + 0.3f * (float)Math.Sin(trill);
                float v = (float)Math.Sin(ph) + 0.2f * (float)Math.Sin(2f * ph);
                d[i] = (v * am + 0.08f * n.Next()) * Gate(t, len, 0.015f, 0.08f);
            }
            return d;
        }

        // Pads meeting: a low thump that drops in pitch, a crunch of noise, and (big hits) a clack of helmets.
        static float[] Hit(int rate, float len, float f0, float f1, float bodyDecay, float crunchDecay, float crunch, float clack)
        {
            var d = Buffer(rate, len);
            var n = new Noise(5u);
            float ph = 0f, lp = 0f, prev = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float f = f1 + (f0 - f1) * Exp(t, 14f);
                ph += Tau * f / rate;
                float x = n.Next();
                lp += 0.35f * (x - lp);
                float hp = x - prev; prev = x;
                d[i] = (float)Math.Sin(ph) * Exp(t, bodyDecay) + crunch * lp * Exp(t, crunchDecay) + clack * hp * Exp(t, 60f);
            }
            return d;
        }

        // A stadium crowd: a roar of band-limited noise that rises and falls, with a few voices in it. The groan is
        // lower and its voices slide down ("awww").
        static float[] Crowd(int rate, float len, uint seed, bool groan)
        {
            var d = Buffer(rate, len);
            var n = new Noise(seed);
            float hiA = groan ? 0.12f : 0.3f, loA = groan ? 0.01f : 0.03f;
            float hi = 0f, lo = 0f, swayLp = 0f;
            const int Voices = 6;
            var vf = new float[Voices];
            var vs = new float[Voices];
            var vp = new float[Voices];
            for (int k = 0; k < Voices; k++)
            {
                vf[k] = (groan ? 230f : 300f) + 120f * (n.Next() * 0.5f + 0.5f);
                vs[k] = 0.3f * (n.Next() * 0.5f + 0.5f);
            }
            float rise = groan ? 0.15f : 0.5f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float x = n.Next();
                hi += hiA * (x - hi);
                lo += loA * (x - lo);
                swayLp += 0.0015f * (n.Next() - swayLp);
                float roar = (hi - lo) * (0.75f + 4f * swayLp);
                float voices = 0f;
                for (int k = 0; k < Voices; k++)
                {
                    float vt = t - vs[k];
                    if (vt < 0f) continue;
                    float glide = groan ? 1f - 0.3f * Math.Min(1f, vt / (len * 0.8f)) : 1f + 0.06f * Math.Min(1f, vt / 0.6f);
                    vp[k] += Tau * vf[k] * glide / rate;
                    float s = (float)Math.Sin(vp[k]);
                    float v = s + 0.45f * (float)Math.Sin(2f * vp[k]) + 0.25f * (float)Math.Sin(3f * vp[k]);
                    voices += v * Exp(vt, groan ? 1.4f : 1.1f) * Math.Min(1f, vt / 0.08f);
                }
                float env = t < rise ? t / rise * (t / rise) : Exp(t - rise, groan ? 1.6f : 1.0f);
                d[i] = (roar * 3f + 0.06f * voices) * env * Gate(t, len, 0f, 0.15f);
            }
            return d;
        }

        // The huddle clock: a short wooden click.
        static float[] Tick(int rate)
        {
            float len = 0.06f;
            var d = Buffer(rate, len);
            var n = new Noise(13u);
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                ph += Tau * 1800f / rate;
                d[i] = (float)Math.Sin(ph) * Exp(t, 120f) + 0.5f * n.Next() * Exp(t, 400f);
            }
            return d;
        }

        // The score sting: a bright brassy run up C, E, G and a held high C over a low C.
        static float[] Sting(int rate)
        {
            float len = 1.7f;
            var d = Buffer(rate, len);
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float[] starts = { 0f, 0.11f, 0.22f, 0.33f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float nt = t - starts[k];
                    if (nt < 0f) continue;
                    bool last = k == notes.Length - 1;
                    float env = Math.Min(1f, nt / 0.02f) * (last ? Exp(nt, 1.6f) : Exp(nt, 9f));
                    v += Brass(notes[k], nt) * env;
                    if (last) v += 0.6f * Brass(notes[0] * 0.5f, nt) * env;
                }
                d[i] = v * Gate(t, len, 0f, 0.2f);
            }
            return d;
        }

        static float Brass(float f, float t)
        {
            float v = 0f;
            for (int h = 1; h <= 6; h++) v += (float)Math.Sin(Tau * f * h * t) / h;
            return v;
        }

        // The hike: a short "hut" of noise on a low knock.
        static float[] Snap(int rate)
        {
            float len = 0.15f;
            var d = Buffer(rate, len);
            var n = new Noise(17u);
            float ph = 0f, hi = 0f, lo = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float x = n.Next();
                hi += 0.4f * (x - hi);
                lo += 0.05f * (x - lo);
                ph += Tau * 160f / rate;
                d[i] = (hi - lo) * Exp(t, 35f) + 0.8f * (float)Math.Sin(ph) * Exp(t, 30f);
            }
            return d;
        }

        // A ball or a body going past: noise that opens up and closes again.
        static float[] Whoosh(int rate)
        {
            float len = 0.35f;
            var d = Buffer(rate, len);
            var n = new Noise(19u);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float u = t / len;
                float bell = 4f * u * (1f - u);
                lp += (0.02f + 0.25f * bell) * (n.Next() - lp);
                d[i] = lp * bell * bell;
            }
            return d;
        }

        // The ball into hands: a soft pop.
        static float[] Catch(int rate)
        {
            float len = 0.14f;
            var d = Buffer(rate, len);
            var n = new Noise(23u);
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float f = 180f + 80f * Exp(t, 40f);
                ph += Tau * f / rate;
                d[i] = (float)Math.Sin(ph) * Exp(t, 35f) + 0.4f * n.Next() * Exp(t, 300f);
            }
            return d;
        }

        // A first down: two bell notes.
        static float[] Chime(int rate)
        {
            float len = 0.9f;
            var d = Buffer(rate, len);
            float[] notes = { 880f, 1318.5f };
            float[] starts = { 0f, 0.12f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float nt = t - starts[k];
                    if (nt < 0f) continue;
                    float env = Math.Min(1f, nt / 0.005f) * Exp(nt, 5f);
                    v += ((float)Math.Sin(Tau * notes[k] * nt) + 0.3f * (float)Math.Sin(Tau * notes[k] * 2.76f * nt)) * env;
                }
                d[i] = v * Gate(t, len, 0f, 0.1f);
            }
            return d;
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

    // Sound on the TV (WS-09, FEATURES.audio; NO_AUDIO=1 turns it off). Makes every clip once at start, then each
    // frame plays what AudioDirector asks for, at audio.master times the sound's own level. All sounds are flat (not
    // placed in the stadium). PartyHost drives it with one line at the end of its Update (Drive).
    public sealed class Audio : MonoBehaviour
    {
        AudioClip[] clips;
        int[] shots;
        AudioSource source;

        public AudioDirector Director { get; private set; }

        // How many times a sound has really been started on the TV (tests).
        public int Shots(Sound s) => shots[(int)s];

        // From PartyHost.Update, after the World stepped. Off: nothing is made and nothing plays.
        public static Audio Drive(GameObject on, GameConfig cfg, World w)
        {
            var a = on.GetComponent<Audio>();
            if (!AudioDirector.On(cfg))
            {
                if (a != null) a.Director.Observe(w);   // (it forgets where it was, so turning it back on plays no backlog)
                return null;
            }
            if (a == null) { a = on.AddComponent<Audio>(); a.Setup(cfg); }
            a.Frame(w);
            return a;
        }

        void Setup(GameConfig cfg)
        {
            Director = new AudioDirector(cfg);
            int rate = Mathf.Max(8000, cfg.audio.sampleRate);
            clips = new AudioClip[AudioCues.All.Length];
            shots = new int[AudioCues.All.Length];
            foreach (var s in AudioCues.All)
            {
                var data = AudioSynth.Make(s, rate);
                var clip = AudioClip.Create("sfx-" + AudioCues.Key(s), data.Length, 1, rate, false);
                clip.SetData(data, 0);
                clips[(int)s] = clip;
            }
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }

        void Frame(World w)
        {
            foreach (var s in Director.Observe(w))
            {
                float g = Director.Gain(s);
                if (g <= 0f) continue;
                source.PlayOneShot(clips[(int)s], g);
                shots[(int)s]++;
            }
        }

        void OnDestroy()
        {
            if (clips == null) return;
            foreach (var c in clips) if (c != null) Destroy(c);
        }
    }
}
