using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Music on the TV (FEATURES.music, NO_MUSIC=1 turns it off; View/Music.cs). Only the TV reads these; the sim never
    // does. Every track is written here as notes and made in code when the game starts, like the sound effects, so
    // nothing is downloaded. master: the music's own volume under the SETTINGS sound level (0..1); fadeS: how long a
    // track takes to fade in or out when the moment changes; sampleRate: Hz the tracks are made at.
    // A track: bpm, its length in beats, whether it loops, its level, and its voices. A voice is one instrument
    // (brass, pluck, bass, pad: notes; kick, snare, hat, crash: drums) at its own level.
    //   notes: "C5:1 E5:0.5 -:0.5 G5:2"  pitch:beats, "-" a rest; the line repeats until the track's length.
    //   drums: "x...x...x...x..."  one character per 16th note, x a hit, X a loud one; the line repeats
    //              when it runs out (16 characters: every bar).
    public sealed class MusicConfig
    {
        public float master, fadeS;
        public int sampleRate;
        public SortedDictionary<string, MusicTrack> tracks = new SortedDictionary<string, MusicTrack>();
    }

    public sealed class MusicTrack
    {
        public float bpm, beats, level;
        public bool loop;
        public List<MusicVoice> voices = new List<MusicVoice>();
    }

    public sealed class MusicVoice
    {
        public string instrument = "", notes = "";
        public float level;
    }
}
