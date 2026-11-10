using System;
using System.Globalization;

namespace Game.Core.Math
{
    /// <summary>Fixed-point number, 16 fractional bits in a long. Every game
    /// number that takes part in the simulation is a Fixed: floats differ
    /// between machines, Fixed does not.</summary>
    public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
    {
        public const int Shift = 16;
        public const long OneRaw = 1L << Shift;
        public readonly long Raw;

        private Fixed(long raw) { Raw = raw; }

        public static readonly Fixed Zero = new Fixed(0);
        public static readonly Fixed One = new Fixed(OneRaw);
        public static readonly Fixed Half = new Fixed(OneRaw / 2);

        public static Fixed FromRaw(long raw) => new Fixed(raw);
        public static Fixed FromInt(int v) => new Fixed((long)v << Shift);
        public static Fixed Ratio(long num, long den) => new Fixed((num << Shift) / den);

        public static Fixed operator +(Fixed a, Fixed b) => new Fixed(a.Raw + b.Raw);
        public static Fixed operator -(Fixed a, Fixed b) => new Fixed(a.Raw - b.Raw);
        public static Fixed operator -(Fixed a) => new Fixed(-a.Raw);
        public static Fixed operator *(Fixed a, Fixed b) => new Fixed((a.Raw * b.Raw) >> Shift);
        public static Fixed operator /(Fixed a, Fixed b) => new Fixed((a.Raw << Shift) / b.Raw);
        public static Fixed operator *(Fixed a, int b) => new Fixed(a.Raw * b);
        public static Fixed operator /(Fixed a, int b) => new Fixed(a.Raw / b);
        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;
        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;

        public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
        public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;
        public static Fixed Clamp(Fixed v, Fixed lo, Fixed hi) => Max(lo, Min(hi, v));
        public static Fixed Clamp01(Fixed v) => Clamp(v, Zero, One);
        public Fixed Abs() => Raw < 0 ? new Fixed(-Raw) : this;

        /// <summary>Square root, rounded down, by integer Newton iteration: the same on every machine.</summary>
        public static Fixed Sqrt(Fixed v)
        {
            if (v.Raw < 0) throw new ArgumentOutOfRangeException(nameof(v), "square root of a negative number");
            if (v.Raw == 0) return Zero;
            // sqrt(raw / 2^16) * 2^16 = sqrt(raw * 2^16)
            ulong n = (ulong)v.Raw << Shift;
            ulong x = n;
            ulong y = (x + 1) >> 1;
            while (y < x) { x = y; y = (x + n / x) >> 1; }
            return new Fixed((long)x);
        }

        /// <summary>Whole part, rounded down.</summary>
        public int FloorToInt() => (int)(Raw >> Shift);

        /// <summary>For display and reports only; never feed it back into the simulation.</summary>
        public double ToDouble() => Raw / (double)OneRaw;

        /// <summary>Parses "12", "-0.25", "3.125" exactly, without floating point.</summary>
        public static Fixed Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) throw new FormatException("empty number");
            s = s.Trim();
            bool neg = s[0] == '-';
            if (neg || s[0] == '+') s = s.Substring(1);
            int dot = s.IndexOf('.');
            string whole = dot < 0 ? s : s.Substring(0, dot);
            string frac = dot < 0 ? "" : s.Substring(dot + 1);
            if (whole.Length == 0) whole = "0";
            if (s.IndexOfAny(new[] { 'e', 'E' }) >= 0) throw new FormatException($"exponents are not supported: {s}");
            long w = long.Parse(whole, NumberStyles.None, CultureInfo.InvariantCulture);
            long raw = w << Shift;
            if (frac.Length > 0)
            {
                if (frac.Length > 9) frac = frac.Substring(0, 9);
                long f = long.Parse(frac, NumberStyles.None, CultureInfo.InvariantCulture);
                long scale = 1;
                for (int i = 0; i < frac.Length; i++) scale *= 10;
                raw += (f * OneRaw + scale / 2) / scale;
            }
            return new Fixed(neg ? -raw : raw);
        }

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is Fixed f && f.Raw == Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
        public override string ToString() => ToDouble().ToString("0.###", CultureInfo.InvariantCulture);
    }
}
