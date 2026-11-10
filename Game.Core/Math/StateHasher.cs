namespace Game.Core.Math
{
    /// <summary>FNV-1a over the state's numbers. Two states with the same hash
    /// are the same state; lockstep and replays rest on that.</summary>
    public struct StateHasher
    {
        public ulong Value;

        public static StateHasher Create() => new StateHasher { Value = 14695981039346656037UL };

        public void Add(ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    Value ^= (byte)(v >> (i * 8));
                    Value *= 1099511628211UL;
                }
            }
        }

        public void Add(long v) => Add(unchecked((ulong)v));
        public void Add(int v) => Add((long)v);
        public void Add(bool v) => Add(v ? 1L : 0L);
        public void Add(Fixed v) => Add(v.Raw);
        public void Add(Vec2 v) { Add(v.X); Add(v.Y); }

        public void Add(string s)
        {
            Add(s.Length);
            foreach (char c in s) Add((int)c);
        }
    }
}
