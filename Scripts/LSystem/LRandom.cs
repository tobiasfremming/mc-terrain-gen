namespace LSystems
{
    // Deterministic PRNG for the rewriter. Deliberately NOT UnityEngine.Random:
    // that is a global, shared, frame-order-dependent stream, and an L-system
    // whose output depends on what else drew a random number this frame cannot
    // be regenerated. Everything in this project is seed-deterministic
    // (WorldConfig.worldSeed); grammars have to be too, or a plant that is
    // streamed out and back in changes shape.
    //
    // PCG32 (O'Neill 2014): tiny, fast, good statistical quality, and -- the
    // reason it is here rather than a plain xorshift -- it takes a *sequence*
    // selector alongside the seed, so `new LRandom(worldSeed, plantId)` gives
    // every plant its own independent stream from one world seed.
    public struct LRandom
    {
        ulong _state;
        readonly ulong _inc;

        public LRandom(uint seed, uint sequence = 0u)
        {
            unchecked
            {
                _inc = (((ulong)sequence) << 1) | 1ul;
                _state = 0ul;
                // Two steps with the seed mixed in, per the reference impl.
                _state = _state * 6364136223846793005ul + _inc;
                _state += seed;
                _state = _state * 6364136223846793005ul + _inc;
            }
        }

        public uint NextUInt()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * 6364136223846793005ul + _inc;
                uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        // Uniform in [0, 1).
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();
    }
}
