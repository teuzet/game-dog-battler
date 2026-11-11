using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>Spin: swing the owner up. The owner trails the dog on the leash, so the dog runs round him: the
    /// cursor sits on a ring of <c>ring_m</c> round the owner, <c>lead_deg</c> ahead of the dog along the turn, and the owner
    /// is dragged round and picks up speed. Once he moves at <c>target_speed_mps</c> the skill holds the dog still
    /// (the decision layer then strikes). The sense of the turn is chosen once, along the owner's own sideways
    /// motion, and kept (the state).</summary>
    public sealed class SpinSkill : ISkill
    {
        private readonly Fixed _ring, _targetSpeed, _sin, _cos;
        private int _dir; // +1 counter-clockwise, -1 clockwise, 0 not chosen yet

        public SpinSkill(GameData data)
        {
            _ring = data.Num("bots.skills.spin.ring_m");
            _targetSpeed = data.Num("bots.skills.spin.target_speed_mps");
            Angle.SinCos(data.Num("bots.skills.spin.lead_deg"), out _sin, out _cos);
        }

        private SpinSkill(SpinSkill src)
        {
            _ring = src._ring; _targetSpeed = src._targetSpeed; _sin = src._sin; _cos = src._cos; _dir = src._dir;
        }

        public PlayerInput Act(BotView v, SeenObject target, ref DetRandom rng)
        {
            var owner = SwingSkill.Owner(v, v.Self);
            var me = Seen.Me(v);
            if (owner.Vel.Length >= _targetSpeed) return Cursor.At(me);
            var u = (me - owner.Pos).Normalized(); // the dog is never on its owner: the bodies push apart
            if (_dir == 0) _dir = u.Cross(owner.Vel) < Fixed.Zero ? -1 : 1; // the owner already drifts one way: join it
            var s = _dir > 0 ? _sin : -_sin;
            var ahead = new Vec2(u.X * _cos - u.Y * s, u.X * s + u.Y * _cos);
            return Cursor.At(owner.Pos + ahead * _ring);
        }

        public ISkill Clone() => new SpinSkill(this);
    }
}
