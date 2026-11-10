using System;

namespace Game.Core.Math
{
    /// <summary>A point or a vector in the arena plane (metres). All components
    /// are Fixed, so every operation is the same on every machine.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly Fixed X, Y;

        public Vec2(Fixed x, Fixed y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(Fixed.Zero, Fixed.Zero);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, Fixed k) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator /(Vec2 a, Fixed k) => new Vec2(a.X / k, a.Y / k);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public Fixed Dot(Vec2 o) => X * o.X + Y * o.Y;
        public Fixed Cross(Vec2 o) => X * o.Y - Y * o.X;
        public Fixed LengthSq => X * X + Y * Y;
        public Fixed Length => Fixed.Sqrt(LengthSq);
        public Fixed DistanceTo(Vec2 o) => (o - this).Length;

        /// <summary>Unit vector; the zero vector stays zero.</summary>
        public Vec2 Normalized()
        {
            var len = Length;
            return len == Fixed.Zero ? Zero : this / len;
        }

        /// <summary>The same direction with length at most <paramref name="max"/>.</summary>
        public Vec2 ClampLength(Fixed max) => LengthSq <= max * max ? this : Normalized() * max;

        public bool Equals(Vec2 o) => this == o;
        public override bool Equals(object? obj) => obj is Vec2 v && this == v;
        public override int GetHashCode() => (X.Raw, Y.Raw).GetHashCode();
        public override string ToString() => $"({X}, {Y})";
    }
}
