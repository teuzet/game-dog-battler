using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>Dodge: get out of the way of the rival's owner. The owner of a swinging rival trails his dog on the leash,
    /// so he moves on an arc round the rival's dog: from the seen places and velocities the skill turns him round that dog
    /// at the rate he turns now (speed across the leash over its length) and follows him over the next <c>horizon_s</c> in
    /// steps of <c>sample_s</c>. An owner farther from his dog than the leash and <c>arc_slack_m</c> is not on the leash and flies
    /// straight. If the path comes nearer than <c>danger_m</c> to my dog, the dog steps <c>step_m</c> perpendicular to the path
    /// at the point of closest approach, away from it, or, when an obstacle is within <c>cover_m</c> and lies on my side of
    /// the path, goes behind it (the far side from the owner, <c>cover_gap_m</c> beyond its edge). An owner slower than
    /// <c>min_speed_mps</c> is no threat. With no threat the cursor stays on the dog. Stateless.</summary>
    public sealed class DodgeSkill : ISkill
    {
        private static readonly Fixed RadToDeg = Fixed.Ratio(1800000, 31416);
        private const int MaxStepDeg = 90; // the sine series of Angle is good to here

        private readonly Fixed _horizon, _danger, _step, _cover, _coverGap, _minSpeed, _arcSlack, _sample;

        public DodgeSkill(GameData data)
        {
            _horizon = data.Num("bots.skills.dodge.horizon_s");
            _danger = data.Num("bots.skills.dodge.danger_m");
            _step = data.Num("bots.skills.dodge.step_m");
            _cover = data.Num("bots.skills.dodge.cover_m");
            _coverGap = data.Num("bots.skills.dodge.cover_gap_m");
            _minSpeed = data.Num("bots.skills.dodge.min_speed_mps");
            _arcSlack = data.Num("bots.skills.dodge.arc_slack_m");
            _sample = data.Num("bots.skills.dodge.sample_s");
        }

        public Fixed Danger => _danger;

        /// <summary>How near the owner of <paramref name="rival"/> (a seen dog) passes my dog within the horizon (metres); -1 when he is no threat.</summary>
        public Fixed Pass(BotView v, SeenObject rival) => Predict(v, rival, out _, out _);

        /// <summary>The same, and where the path is nearest to my dog and the direction of travel there.</summary>
        private Fixed Predict(BotView v, SeenObject rival, out Vec2 at, out Vec2 dir)
        {
            at = default; dir = default;
            var owner = SwingSkill.Owner(v, rival.Id);
            if (owner.Vel.Length < _minSpeed) return -Fixed.One;
            var me = Seen.Me(v);
            var r = owner.Pos - rival.Pos; // from the rival's dog to his owner
            var vrel = owner.Vel - rival.Vel;
            bool onLeash = r.Length <= v.Hud["leash_m"] + _arcSlack && r.LengthSq > Fixed.Zero;
            int n = System.Math.Max(1, (_horizon / _sample + Fixed.Half).FloorToInt());
            var dt = _horizon / n;
            Fixed sin = Fixed.Zero, cos = Fixed.One;
            if (onLeash)
            {
                var omegaDeg = r.Cross(vrel) / r.LengthSq * dt * RadToDeg;
                Angle.SinCos(Fixed.Clamp(omegaDeg, -Fixed.FromInt(MaxStepDeg), Fixed.FromInt(MaxStepDeg)), out sin, out cos);
            }
            var best = -Fixed.One;
            var p0 = owner.Pos;
            for (int i = 1; i <= n; i++)
            {
                Vec2 p1;
                if (onLeash)
                {
                    r = new Vec2(r.X * cos - r.Y * sin, r.X * sin + r.Y * cos);
                    p1 = rival.Pos + rival.Vel * (dt * i) + r;
                }
                else p1 = p0 + owner.Vel * dt;
                var seg = p1 - p0;
                var t = seg.LengthSq == Fixed.Zero ? Fixed.Zero : Fixed.Clamp01((me - p0).Dot(seg) / seg.LengthSq);
                var near = p0 + seg * t;
                var d = near.DistanceTo(me);
                if (best < Fixed.Zero || d < best) { best = d; at = near; dir = seg.Normalized(); }
                p0 = p1;
            }
            return best;
        }

        public PlayerInput Act(BotView v, SeenObject target, ref DetRandom rng)
        {
            var me = Seen.Me(v);
            var pass = Predict(v, target, out var at, out var dir);
            if (pass < Fixed.Zero || pass >= _danger) return Cursor.At(me);
            var left = new Vec2(-dir.Y, dir.X);
            var side = left.Dot(me - at) < Fixed.Zero ? -Fixed.One : Fixed.One; // away from the path
            if (NearestCover(v, me, out var cover))
            {
                var owner = SwingSkill.Owner(v, target.Id);
                var away = (cover.Pos - owner.Pos).Normalized();
                var behind = cover.Pos + away * (Seen.Reach(cover) + _coverGap);
                if (left.Dot(behind - at) * side >= Fixed.Zero) return Cursor.At(behind); // not across the path
            }
            return Cursor.At(me + left * (side * _step));
        }

        /// <summary>Is an obstacle within <c>cover_m</c> of my dog (somewhere to hide)?</summary>
        public bool HasCover(BotView v, Vec2 me) => NearestCover(v, me, out _);

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
