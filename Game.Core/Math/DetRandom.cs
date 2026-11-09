namespace Game.Core.Math
{
    /// <summary>Deterministic random numbers (xorshift64*). A value type: copying
    /// it forks the sequence, which is what a forked simulation needs.</summary>
    public struct DetRandom
    {
        public ulong State;

        public DetRandom(ulong seed)
        {
            // splitmix64 of the seed, so seeds 1, 2, 3 do not start alike
            ulong z = seed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            State = (z ^ (z >> 31)) | 1UL;
        }

        /// <summary>A stream of its own for one module: seed + a stable name.
        /// A new module that draws numbers does not shift anyone else's.</summary>
        public static DetRandom Derive(int seed, string stream)
        {
            var h = StateHasher.Create();
            h.Add(seed);
            h.Add(stream);
            return new DetRandom(h.Value);
        }

        public ulong NextULong()
        {
            State ^= State >> 12;
            State ^= State << 25;
            State ^= State >> 27;
            return State * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>0 ≤ value &lt; maxExclusive.</summary>
        public int NextInt(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)((NextULong() >> 33) % (ulong)maxExclusive);

        public int Range(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>A Fixed in [0, 1).</summary>
        public Fixed NextFixed01() => Fixed.FromRaw((long)(NextULong() >> (64 - Fixed.Shift)));

        public bool Chance(Fixed p) => NextFixed01() < p;
    }
}
