using System;
using Game.Core.Math;

namespace Game.Core.Sim
{
    /// <summary>One player's input for one tick: the mouse and the ability buttons.
    /// Unity maps the mouse to it, bots produce it, the input log stores it.
    /// Cursor mode (<see cref="Push"/> clear): Aim is the point on the arena the
    /// dog runs to, in centimetres from the arena centre. Push mode: Aim is the
    /// mouse shift accumulated over this tick, in centimetres.</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        /// <summary>Mode bit: set = push, clear = cursor.</summary>
        public const uint PushBit = 1u << 0;
        /// <summary>LMB / Q: the dog's ability.</summary>
        public const uint DogAbilityBit = 1u << 1;
        /// <summary>RMB / E: the owner's ability.</summary>
        public const uint OwnerAbilityBit = 1u << 2;

        public readonly short AimX; // centimetres
        public readonly short AimY; // centimetres
        public readonly uint Buttons; // PushBit | DogAbilityBit | OwnerAbilityBit

        public PlayerInput(int aimX, int aimY, uint buttons = 0)
        {
            AimX = (short)System.Math.Max(short.MinValue, System.Math.Min(short.MaxValue, aimX));
            AimY = (short)System.Math.Max(short.MinValue, System.Math.Min(short.MaxValue, aimY));
            Buttons = buttons;
        }

        public static readonly PlayerInput None = default;

        /// <summary>Same as AimX / AimY: the generic names the factory's bots and contract tests use.</summary>
        public short X => AimX;
        public short Y => AimY;

        public bool Push => (Buttons & PushBit) != 0;
        public bool DogAbility => (Buttons & DogAbilityBit) != 0;
        public bool OwnerAbility => (Buttons & OwnerAbilityBit) != 0;

        public bool Held(int button) => (Buttons & (1u << button)) != 0;
        public bool Equals(PlayerInput o) => AimX == o.AimX && AimY == o.AimY && Buttons == o.Buttons;
        public override bool Equals(object? obj) => obj is PlayerInput p && Equals(p);
        public override int GetHashCode() => (AimX & 0xFFFF) | ((AimY & 0xFFFF) << 16) ^ (int)(Buttons << 5);
        public override string ToString() => $"{AimX},{AimY},{Buttons}";
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
            foreach (var p in _players) { h.Add((int)p.AimX); h.Add((int)p.AimY); h.Add((long)p.Buttons); }
        }
    }
}
