using System;
using Game.Core.Math;

namespace Game.Core.Sim
{
    /// <summary>One participant's input for one tick: a stick and buttons.
    /// Unity maps keys to it, bots produce it, the input log stores it.</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public readonly sbyte X; // -1..1
        public readonly sbyte Y; // -1..1
        public readonly uint Buttons; // bit i = button i held

        public PlayerInput(int x, int y, uint buttons = 0)
        {
            X = (sbyte)System.Math.Max(-1, System.Math.Min(1, x));
            Y = (sbyte)System.Math.Max(-1, System.Math.Min(1, y));
            Buttons = buttons;
        }

        public static readonly PlayerInput None = default;
        public bool Held(int button) => (Buttons & (1u << button)) != 0;
        public bool Equals(PlayerInput o) => X == o.X && Y == o.Y && Buttons == o.Buttons;
        public override bool Equals(object? obj) => obj is PlayerInput p && Equals(p);
        public override int GetHashCode() => (X + 2) | ((Y + 2) << 4) | (int)(Buttons << 8);
        public override string ToString() => $"{X},{Y},{Buttons}";
    }

    /// <summary>Everyone's input for one tick, by player index.</summary>
    public sealed class TickInputs
    {
        private readonly PlayerInput[] _players;

        public TickInputs(int players) { _players = new PlayerInput[players]; }

        public int Count => _players.Length;

        public PlayerInput this[int player]
        {
            get => player >= 0 && player < _players.Length ? _players[player] : PlayerInput.None;
            set => _players[player] = value;
        }

        public static TickInputs Empty(int players) => new TickInputs(players);

        public TickInputs Copy()
        {
            var c = new TickInputs(_players.Length);
            Array.Copy(_players, c._players, _players.Length);
            return c;
        }

        public void Hash(ref StateHasher h)
        {
            foreach (var p in _players) { h.Add(p.X); h.Add(p.Y); h.Add((long)p.Buttons); }
        }
    }
}
