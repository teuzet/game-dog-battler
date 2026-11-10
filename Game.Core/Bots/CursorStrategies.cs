using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots
{
    /// <summary>Bots play in cursor mode: the input is a point on the arena in centimetres.</summary>
    internal static class Cursor
    {
        public static PlayerInput At(Fixed x, Fixed y) => new PlayerInput((x * 100).FloorToInt(), (y * 100).FloorToInt());
    }

    /// <summary>Runs to the scoring circle and stays in it.</summary>
    public sealed class ZoneRunnerStrategy : IBotStrategy
    {
        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            foreach (var o in view.Objects)
                if (o.Kind == "zone") return Cursor.At(o.X, o.Y);
            return Cursor.At(view.X, view.Y);
        }

        public IBotStrategy Clone() => this;
    }

    /// <summary>Runs to random points of the arena.</summary>
    public sealed class WanderStrategy : IBotStrategy
    {
        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            var halfW = view.Hud["arena_half_width"];
            var halfH = view.Hud["arena_half_height"];
            return Cursor.At((rng.NextFixed01() * 2 - Fixed.One) * halfW, (rng.NextFixed01() * 2 - Fixed.One) * halfH);
        }

        public IBotStrategy Clone() => this;
    }
}
