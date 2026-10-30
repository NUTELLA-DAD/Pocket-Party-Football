using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Music on the TV (FEATURES.music, NO_MUSIC=1 turns it off; View/Music.cs). Only the TV reads these; the sim never
    // does. master: the music's own volume under the SETTINGS sound level (0..1); fadeS: how long a track takes to
    // fade in or out when the moment changes. A track: its audio file under Resources (no extension: Music/title is
    // Resources/Music/title.ogg, .wav or .mp3), its level, and whether it loops. A track whose file is missing is
    // silence, so the game runs before the music is in.
    public sealed class MusicConfig
    {
        public float master, fadeS;
        public SortedDictionary<string, MusicTrack> tracks = new SortedDictionary<string, MusicTrack>();
    }

    public sealed class MusicTrack
    {
        public string file = "";
        public float level;
        public bool loop;
    }
}
