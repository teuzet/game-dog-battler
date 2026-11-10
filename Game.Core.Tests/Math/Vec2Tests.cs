using Game.Core.Math;
using Xunit;

namespace Game.Core.Tests.Math
{
    public class Vec2Tests
    {
        private static Vec2 V(string x, string y) => new Vec2(Fixed.Parse(x), Fixed.Parse(y));

        [Fact]
        public void SqrtIsExactOnSquares()
        {
            foreach (int n in new[] { 0, 1, 2, 3, 7, 100, 1234 })
                Assert.Equal(Fixed.FromInt(n), Fixed.Sqrt(Fixed.FromInt(n * n)));
            Assert.Equal(Fixed.Half, Fixed.Sqrt(Fixed.Parse("0.25")));
        }

        [Fact]
        public void SqrtRoundsDownAndNeverOvershoots()
        {
            for (long raw = 1; raw < 5_000_000; raw += 7919)
            {
                var v = Fixed.FromRaw(raw);
                var s = Fixed.Sqrt(v);
                Assert.True(s * s <= v, $"sqrt({v}) = {s} is too big");
                var next = Fixed.FromRaw(s.Raw + 1);
                Assert.True(next.Raw * next.Raw > raw << Fixed.Shift, $"sqrt({v}) = {s} is too small");
            }
        }

        [Fact]
        public void SqrtOfTwoIsPinned() => Assert.Equal(92681, Fixed.Sqrt(Fixed.FromInt(2)).Raw);

        [Fact]
        public void SqrtOfNegativeThrows() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.Sqrt(Fixed.FromInt(-1)));

        [Fact]
        public void LengthAndNormalize()
        {
            var v = V("3", "4");
            Assert.Equal(Fixed.FromInt(5), v.Length);
            Assert.Equal(Fixed.FromInt(25), v.LengthSq);
            Assert.Equal(V("0.6", "0.8").X.ToDouble(), v.Normalized().X.ToDouble(), 3);
            Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
            Assert.Equal(Fixed.FromInt(5), Vec2.Zero.DistanceTo(v));
        }

        [Fact]
        public void ArithmeticDotCross()
        {
            var a = V("1", "2");
            var b = V("3", "-1");
            Assert.Equal(V("4", "1"), a + b);
            Assert.Equal(V("-2", "3"), a - b);
            Assert.Equal(V("2", "4"), a * Fixed.FromInt(2));
            Assert.Equal(Fixed.One, a.Dot(b)); // 3 - 2
            Assert.Equal(Fixed.FromInt(-7), a.Cross(b)); // -1 - 6
        }

        [Fact]
        public void ClampLengthKeepsDirection()
        {
            var v = V("6", "8").ClampLength(Fixed.FromInt(5));
            Assert.Equal(3.0, v.X.ToDouble(), 2);
            Assert.Equal(4.0, v.Y.ToDouble(), 2);
            Assert.Equal(V("1", "1"), V("1", "1").ClampLength(Fixed.FromInt(5)));
        }
    }
}
