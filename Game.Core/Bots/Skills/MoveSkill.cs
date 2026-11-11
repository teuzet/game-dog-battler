using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>Move: go to the point (the target's X, Y) round the obstacles on the screen. If the straight path
    /// passes an obstacle nearer than <c>clearance_m</c> (a circle grown by it, a rectangle grown by it on each side), the
    /// cursor goes to the tangent point of the nearer edge on the side the goal lies, a little wider than the clearance
    /// (a quarter more), so that once there the path is free. The side chosen for an obstacle is kept while it
    /// blocks the way (the state): the dog does not dither between the two ways round.</summary>
    public sealed class MoveSkill : ISkill
    {
        private readonly Fixed _clearance;
        private int _obstacle = -1; // Id of the obstacle being gone round
        private int _side; // +1: pass it on the left of the heading, -1: on the right

        public MoveSkill(GameData data) { _clearance = data.Num("bots.skills.move.clearance_m"); }

        private MoveSkill(MoveSkill src) { _clearance = src._clearance; _obstacle = src._obstacle; _side = src._side; }

        public PlayerInput Act(BotView v, SeenObject target, ref DetRandom rng)
        {
            var me = Seen.Me(v);
            var goal = target.Pos;
            var d = goal - me;
            bool found = false;
            SeenObject block = default;
            var blockD = Fixed.Zero;
            foreach (var o in v.Objects)
            {
                if (!Seen.IsObstacle(o) || !Blocks(o, me, goal, _clearance)) continue;
                var dist = o.Pos.DistanceTo(me);
                if (found && dist >= blockD) continue;
                block = o; blockD = dist; found = true;
            }
            if (!found)
            {
                _obstacle = -1;
                return Cursor.At(goal);
            }
            var dir = d.Normalized();
            if (_obstacle != block.Id)
            {
                _obstacle = block.Id;
                _side = dir.Cross(block.Pos - me) > Fixed.Zero ? -1 : 1; // the obstacle is on the left: go round on the right
            }
            return Cursor.At(Tangent(block, me, dir, _side, _clearance * Fixed.Ratio(5, 4)));
        }

        /// <summary>Does the segment me → goal pass the obstacle grown by <paramref name="clearance"/>? A goal or a start inside the grown
        /// obstacle is no one's business: the obstacle cannot be gone round.</summary>
        private static bool Blocks(in SeenObject o, Vec2 me, Vec2 goal, Fixed clearance)
        {
            var d = goal - me;
            if (o.Kind == Seen.Circle)
            {
                var r = o.Value + clearance;
                if (o.Pos.DistanceTo(goal) < r || o.Pos.DistanceTo(me) < r) return false;
                var t = Fixed.Clamp01(d.LengthSq == Fixed.Zero ? Fixed.Zero : (o.Pos - me).Dot(d) / d.LengthSq);
                return o.Pos.DistanceTo(me + d * t) < r;
            }
            var hw = o.Value + clearance;
            var hh = o.Value2 + clearance;
            if (Inside(o, goal, hw, hh) || Inside(o, me, hw, hh)) return false;
            // slab clipping of the segment against the box
            var lo = Fixed.Zero;
            var hi = Fixed.One;
            return Clip(me.X - o.X, d.X, hw, ref lo, ref hi) && Clip(me.Y - o.Y, d.Y, hh, ref lo, ref hi);
        }

        private static bool Inside(in SeenObject o, Vec2 p, Fixed hw, Fixed hh) =>
            (p.X - o.X).Abs() < hw && (p.Y - o.Y).Abs() < hh;

        private static bool Clip(Fixed start, Fixed delta, Fixed half, ref Fixed lo, ref Fixed hi)
        {
            if (delta == Fixed.Zero) return start.Abs() < half;
            var a = (-half - start) / delta;
            var b = (half - start) / delta;
            lo = Fixed.Max(lo, Fixed.Min(a, b));
            hi = Fixed.Min(hi, Fixed.Max(a, b));
            return lo < hi;
        }

        /// <summary>The point at which the heading me → goal clears the obstacle on the given side.</summary>
        private static Vec2 Tangent(in SeenObject o, Vec2 me, Vec2 dir, int side, Fixed clearance)
        {
            var left = new Vec2(-dir.Y, dir.X);
            if (o.Kind == Seen.Circle) return o.Pos + left * ((o.Value + clearance) * side);
            var hw = o.Value + clearance;
            var hh = o.Value2 + clearance;
            Vec2 best = default;
            var bestK = Fixed.Zero;
            bool any = false;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vec2(o.X + ((i & 1) == 0 ? -hw : hw), o.Y + ((i & 2) == 0 ? -hh : hh));
                var k = left.Dot(corner - me) * side; // how far past the obstacle's edge on the chosen side
                if (!any || k > bestK) { best = corner; bestK = k; any = true; }
            }
            return best;
        }

        public ISkill Clone() => new MoveSkill(this);
    }
}
