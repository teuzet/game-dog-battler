using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>Dodge: get out of the way of the rival's owner. From his seen place and velocity the skill predicts his
    /// straight pass over the next <c>horizon_s</c>; if it comes nearer than <c>danger_m</c> to my dog, the dog steps
    /// <c>step_m</c> sideways, perpendicular to the arc, away from it, or, when an obstacle is within
    /// <c>cover_m</c>, goes behind it (the far side from the owner, <c>cover_gap_m</c> beyond its edge). An owner slower than
    /// <c>min_speed_mps</c> is no threat. With no threat the cursor stays on the dog. Stateless.</summary>
    public sealed class DodgeSkill : ISkill
    {
        private readonly Fixed _horizon, _danger, _step, _cover, _coverGap, _minSpeed;

        public DodgeSkill(GameData data)
        {
            _horizon = data.Num("bots.skills.dodge.horizon_s");
            _danger = data.Num("bots.skills.dodge.danger_m");
            _step = data.Num("bots.skills.dodge.step_m");
            _cover = data.Num("bots.skills.dodge.cover_m");
            _coverGap = data.Num("bots.skills.dodge.cover_gap_m");
            _minSpeed = data.Num("bots.skills.dodge.min_speed_mps");
        }

        public Fixed Danger => _danger;

        /// <summary>How near the owner of <paramref name="rival"/> passes my dog within the horizon (metres); -1 when he is no threat.</summary>
        public Fixed Pass(BotView v, int rival)
        {
            var owner = Seen.OwnerOf(v, rival);
            var speed = owner.Vel.Length;
            if (speed < _minSpeed) return -Fixed.One;
            var rel = Seen.Me(v) - owner.Pos;
            var t = Fixed.Clamp(rel.Dot(owner.Vel) / (speed * speed), Fixed.Zero, _horizon);
            return (rel - owner.Vel * t).Length;
        }

        public PlayerInput Act(BotView v, SeenObject target, ref DetRandom rng)
        {
            var me = Seen.Me(v);
            var pass = Pass(v, target.Id);
            if (pass < Fixed.Zero || pass >= _danger) return Cursor.At(me);
            var owner = Seen.OwnerOf(v, target.Id);
            var dir = owner.Vel.Normalized();
            if (NearestCover(v, me, out var cover))
            {
                var away = (cover.Pos - owner.Pos).Normalized();
                return Cursor.At(cover.Pos + away * (Seen.Reach(cover) + _coverGap));
            }
            var left = new Vec2(-dir.Y, dir.X);
            var side = left.Dot(me - owner.Pos) < Fixed.Zero ? -Fixed.One : Fixed.One; // away from the arc
            return Cursor.At(me + left * (side * _step));
        }

        private bool NearestCover(BotView v, Vec2 me, out SeenObject best)
        {
            best = default;
            bool any = false;
            var bestD = Fixed.Zero;
            foreach (var o in v.Objects)
            {
                if (!Seen.IsObstacle(o)) continue;
                var d = o.Pos.DistanceTo(me) - Seen.Reach(o);
                if (d > _cover || (any && d >= bestD)) continue;
                best = o; bestD = d; any = true;
            }
            return any;
        }

        public ISkill Clone() => this;
    }
}
