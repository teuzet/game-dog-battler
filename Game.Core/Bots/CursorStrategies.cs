using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots
{
    /// <summary>Bots play in cursor mode: the input is a point on the arena in centimetres.</summary>
    internal static class Cursor
    {
        public static PlayerInput At(Vec2 p) => At(p.X, p.Y);
        public static PlayerInput At(Fixed x, Fixed y) => new PlayerInput((x * 100).FloorToInt(), (y * 100).FloorToInt());

        public static bool Find(BotView v, string kind, out SeenObject found)
        {
            foreach (var o in v.Objects)
                if (o.Kind == kind) { found = o; return true; }
            found = default;
            return false;
        }

        /// <summary>The rival dog nearest to <paramref name="from"/> that satisfies <paramref name="within"/> (metres from the point, or -1 for any).</summary>
        public static bool NearestRival(BotView v, Vec2 from, Fixed within, out SeenObject best)
        {
            best = default;
            bool any = false;
            var bestD = Fixed.Zero;
            foreach (var o in v.Objects)
            {
                if (o.Kind != "dog") continue;
                var d = o.Pos.DistanceTo(from);
                if (within >= Fixed.Zero && d > within) continue;
                if (!any || d < bestD) { best = o; bestD = d; any = true; }
            }
            return any;
        }
    }

    /// <summary>Runs to random points of the arena, changing the point every 1–2 seconds.</summary>
    public sealed class WanderStrategy : IBotStrategy
    {
        private int _until = -1;
        private PlayerInput _point;

        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            if (view.Tick >= _until)
            {
                var halfW = view.Hud["arena_half_width"];
                var halfH = view.Hud["arena_half_height"];
                _point = Cursor.At((rng.NextFixed01() * 2 - Fixed.One) * halfW, (rng.NextFixed01() * 2 - Fixed.One) * halfH);
                _until = view.Tick + Simulation.TicksPerSecond + rng.NextInt(Simulation.TicksPerSecond + 1);
            }
            return _point;
        }

        public IBotStrategy Clone() => (WanderStrategy)MemberwiseClone();
    }

    /// <summary>Holds the circle and swings at the nearest rival standing at it; leaves for the announced
    /// circle early when leading. Without a circle (the sandbox) it hunts the nearest rival anywhere.</summary>
    public sealed class SmartStrategy : IBotStrategy
    {
        private readonly SwingSkill _skill;
        private readonly Fixed _aimError;

        public SmartStrategy(SwingSkill skill, Fixed aimError)
        {
            _skill = skill;
            _aimError = aimError;
        }

        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            var me = new Vec2(view.X, view.Y);
            bool hasZone = Cursor.Find(view, "zone", out var zone);
            if (!hasZone)
            {
                if (Cursor.NearestRival(view, me, -Fixed.One, out var prey)) return Cursor.At(_skill.Aim(view, prey, _aimError, ref rng));
                return Cursor.At(me);
            }
            if (Leading(view) && Cursor.Find(view, "next_zone", out var next))
                return Cursor.At(SwingSkill.Jitter(next.Pos, _aimError, ref rng));
            if (Cursor.NearestRival(view, zone.Pos, zone.Value, out var rival) && rival.Pos.DistanceTo(me) <= view.Hud["leash_m"])
                return Cursor.At(_skill.Aim(view, rival, _aimError, ref rng));
            return Cursor.At(SwingSkill.Jitter(zone.Pos, _aimError, ref rng));
        }

        private static bool Leading(BotView v)
        {
            var mine = v.Hud["score_" + v.Self];
            if (mine <= Fixed.Zero) return false;
            foreach (var kv in v.Hud)
                if (kv.Key.StartsWith("score_") && kv.Key != "score_" + v.Self && kv.Value >= mine) return false;
            return true;
        }

        public IBotStrategy Clone() => this;
    }

    /// <summary>Stands in the middle of the circle and keeps the owner moving without aiming: the cursor jumps
    /// to the next of four points of a small ring (<c>ring_m</c>) every <c>spin_period_s</c>, so the dog
    /// is dragged back and forth round the centre and the owner is swung along (a full round takes four periods).</summary>
    public sealed class CamperStrategy : IBotStrategy
    {
        private readonly Fixed _aimError, _ring;
        private readonly int _periodTicks;

        public CamperStrategy(Fixed aimError, GameData data)
        {
            _aimError = aimError;
            _ring = data.Num("bots.camper.ring_m");
            _periodTicks = (data.Num("bots.camper.spin_period_s") * Simulation.TicksPerSecond).FloorToInt();
        }

        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            var center = Cursor.Find(view, "zone", out var zone) ? zone.Pos : Vec2.Zero;
            int step = view.Tick / _periodTicks % 4;
            var offset = step == 0 ? new Vec2(_ring, Fixed.Zero) : step == 1 ? new Vec2(Fixed.Zero, _ring)
                : step == 2 ? new Vec2(-_ring, Fixed.Zero) : new Vec2(Fixed.Zero, -_ring);
            return Cursor.At(SwingSkill.Jitter(center + offset, _aimError, ref rng));
        }

        public IBotStrategy Clone() => this;
    }
}
