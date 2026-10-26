using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Sound on the TV (WS-09, View/Audio.cs, FEATURES.audio; NO_AUDIO=1 turns it off). Only the TV reads these; the
    // sim never does. master: one volume for everything (0..1); levels: each sound's own volume by name (whistle,
    // hit, bigHit, crowd, groan, tick, sting, snap, whoosh, catch, chime), times master; sampleRate: what the sounds
    // are made at, Hz (they are generated in code, nothing is downloaded); huddleTickFromS: the huddle clock ticks
    // once a second for its last this-many seconds.
    public sealed class AudioConfig
    {
        public float master;
        public int sampleRate;
        public float huddleTickFromS;
        public SortedDictionary<string, float> levels = new SortedDictionary<string, float>();

        public float Level(string sound) => levels.TryGetValue(sound, out float v) ? v : 0f;
    }
}
