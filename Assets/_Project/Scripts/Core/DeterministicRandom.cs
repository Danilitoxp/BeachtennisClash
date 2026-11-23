namespace BeachTennis.Core
{
    /// <summary>
    /// Small, fast, seedable PRNG (xorshift32). Same seed → same sequence on every platform,
    /// so simulation randomness (shot error, AI decisions) is reproducible in tests, replays
    /// and networking. Never use UnityEngine.Random or System.Random inside the simulation.
    /// </summary>
    public struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [min, max).</summary>
        public int Range(int min, int max) => min + (int)(NextUInt() % (uint)(max - min));
    }
}
