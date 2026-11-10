using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots
{
    /// <summary>The aimed swing: the one skill every bot shares (ARCHITECTURE.md → «Боты»). The owner trails the
    /// dog on the leash, so the owner's arc is the dog's path pulled back by one leash. From seen positions and
    /// velocities the skill predicts where the target will be when the owner gets there and sends the dog
    /// through a point one leash beyond it, so the owner's arc passes the target. When the owner stands too
    /// close to build speed, it first backs the dog away for a run-up. Strategies only choose whom to hit;
    /// the archetype's aim error spreads the point. Numbers: Game.Core/Data/bots/swing.json. Stateless.</summary>
    public sealed class SwingSkill
    {
        private readonly Fixed _closingSpeed, _lead, _drift, _margin, _runUp, _minTime, _maxTime, _movingSpeed;

        public SwingSkill(GameData data)
        {
            _closingSpeed = data.Num("bots.swing.closing_speed_mps");
            _lead = data.Num("bots.swing.lead_m");
            _drift = data.Num("bots.swing.drift_gain_s");
            _margin = data.Num("bots.swing.wall_margin_m");
            _runUp = data.Num("bots.swing.run_up_m");
            _minTime = data.Num("bots.swing.min_lead_s");
            _maxTime = data.Num("bots.swing.max_lead_s");
            _movingSpeed = data.Num("bots.swing.moving_speed_mps");
        }

        /// <summary>The cursor point (metres) that swings the owner of <paramref name="v"/>'s participant at <paramref name="target"/>
        /// (a seen dog).</summary>
        public Vec2 Aim(BotView v, SeenObject target, Fixed aimError, ref DetRandom rng)
        {
            var owner = Owner(v, v.Self);
            var leash = v.Hud["leash_m"];
            var o = owner.Pos;
            var dist = o.DistanceTo(target.Pos);
            var lead = Fixed.Clamp(dist / _closingSpeed, _minTime, _maxTime);
            var predicted = target.Pos + target.Vel * lead; // the target keeps its seen velocity
            var u = (predicted - o).Normalized();
            // The owner is pulled toward the dog, so the dog must stay on the line owner → target, ahead of the
            // owner by a leash and more: the cursor sits beyond the dog on that line.
            Vec2 point;
            if (dist < _runUp && owner.Vel.Dot(u) < _movingSpeed)
            {
                // the owner needs room to gather speed: back off, or go round, whichever has the most floor
                var side = new Vec2(-u.Y, u.X);
                point = RunUp(v, o, -u, leash);
                var roomBack = point.DistanceTo(o);
                foreach (var c in new[] { side, -side })
                {
                    var p = RunUp(v, o, c, leash);
                    if (p.DistanceTo(o) > roomBack) { point = p; roomBack = p.DistanceTo(o); }
                }
            }
            else
            {
                // the owner's sideways drift off the line is pulled back by tilting the heading against it
                var ov = owner.Vel;
                var drift = ov - u * ov.Dot(u);
                var heading = (u - (drift * _drift).ClampLength(Fixed.One)).Normalized(); // at most 45° off the line
                point = Clamp(v, o + heading * Fixed.Max(leash + _lead, dist + leash)); // beyond the target, so the dog never slows for the cursor
            }
            return Jitter(point, aimError, ref rng);
        }

        private Vec2 RunUp(BotView v, Vec2 o, Vec2 heading, Fixed leash) => Clamp(v, o + heading * (leash + _lead));

        private Vec2 Clamp(BotView v, Vec2 p) =>
            new Vec2(Fixed.Clamp(p.X, -v.Hud["arena_half_width"] + _margin, v.Hud["arena_half_width"] - _margin),
                     Fixed.Clamp(p.Y, -v.Hud["arena_half_height"] + _margin, v.Hud["arena_half_height"] - _margin));

        public static SeenObject Owner(BotView v, int player)
        {
            foreach (var o in v.Objects)
                if (o.Kind == "owner" && o.Id == player) return o;
            throw new System.InvalidOperationException($"participant {player} does not see its owner");
        }

        /// <summary>The cursor is never exact: a point within <paramref name="error"/> metres on each axis.</summary>
        public static Vec2 Jitter(Vec2 p, Fixed error, ref DetRandom rng) =>
            new Vec2(p.X + (rng.NextFixed01() * 2 - Fixed.One) * error, p.Y + (rng.NextFixed01() * 2 - Fixed.One) * error);
    }
}
