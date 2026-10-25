namespace PocketFootball.Sim
{
    // The sim's only randomness: xorshift32, seeded. Same seed, same numbers, on every run.
    public sealed class Rng
    {
        uint state;

        public Rng(int seed)
        {
            state = (uint)seed ^ 0x9E3779B9u;   // hygiene:allow (golden-ratio mix, so seed 0 still works)
            if (state == 0) state = 1;
            Next();
        }

        public uint Next()
        {
            uint x = state;
            x ^= x << 13;   // hygiene:allow (xorshift32 shifts)
            x ^= x >> 17;   // hygiene:allow
            x ^= x << 5;    // hygiene:allow
            state = x;
            return x;
        }

        // [0, 1)
        public float Value() => (Next() >> 8) * (1f / 16777216f);   // hygiene:allow (24 bits of mantissa)

        // [min, max)
        public float Range(float min, float max) => min + (max - min) * Value();

        // [0, n)
        public int Below(int n) => n <= 0 ? 0 : (int)(Next() % (uint)n);

        public uint State => state;
    }
}
