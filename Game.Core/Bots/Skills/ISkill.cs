using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>The lower layer of a duel bot: one skill turns what the bot sees into the point of the cursor
    /// (the same output as a strategy). It reads only the <see cref="BotView"/> and the target the decision layer
    /// gives it (a point or a seen dog); reaction and hold stay in <see cref="BotDriver"/>. A skill's state, if it
    /// has any, is cloned with the bot. Numbers: Game.Core/Data/bots/skills.json.</summary>
    public interface ISkill
    {
        /// <summary><paramref name="target"/> is a seen object: a bare point (only X, Y) for <c>Move</c>, a seen dog for
        /// <c>Strike</c> and <c>Dodge</c> (the rival it works against); <c>Spin</c> and <c>Brake</c> ignore it.</summary>
        PlayerInput Act(BotView view, SeenObject target, ref DetRandom rng);
        ISkill Clone();
    }

    /// <summary>The five skills of one bot built from the data.</summary>
    public sealed class SkillSet
    {
        public readonly MoveSkill Move;
        public readonly SpinSkill Spin;
        public readonly StrikeSkill Strike;
        public readonly DodgeSkill Dodge;
        public readonly BrakeSkill Brake;

        public SkillSet(GameData data, Fixed aimError)
        {
            Move = new MoveSkill(data);
            Spin = new SpinSkill(data);
            Strike = new StrikeSkill(new SwingSkill(data), aimError);
            Dodge = new DodgeSkill(data);
            Brake = new BrakeSkill(data);
        }

        private SkillSet(SkillSet src)
        {
            Move = (MoveSkill)src.Move.Clone();
            Spin = (SpinSkill)src.Spin.Clone();
            Strike = (StrikeSkill)src.Strike.Clone();
            Dodge = (DodgeSkill)src.Dodge.Clone();
            Brake = (BrakeSkill)src.Brake.Clone();
        }

        public SkillSet Clone() => new SkillSet(this);
    }

    /// <summary>Reading the arena obstacles out of a view (Sense puts them in <c>Objects</c>).</summary>
    internal static class Seen
    {
        public const string Circle = "obstacle_circle", Rect = "obstacle_rect";

        public static bool IsObstacle(in SeenObject o) => o.Kind == Circle || o.Kind == Rect;

        /// <summary>The radius of the circle that holds the obstacle.</summary>
        public static Fixed Reach(in SeenObject o) =>
            o.Kind == Circle ? o.Value : Fixed.Sqrt(o.Value * o.Value + o.Value2 * o.Value2);

        public static Vec2 Me(BotView v) => new Vec2(v.X, v.Y);

        public static SeenObject OwnerOf(BotView v, int player)
        {
            foreach (var o in v.Objects)
                if (o.Kind == "owner" && o.Id == player) return o;
            throw new System.InvalidOperationException($"participant {v.Self} does not see the owner of {player}");
        }
    }

    /// <summary>Sine and cosine of a small angle in degrees (the core has no trigonometry): a Taylor series, good to 90°.</summary>
    internal static class Angle
    {
        private static readonly Fixed DegToRad = Fixed.Ratio(31416, 10000 * 180);

        public static void SinCos(Fixed degrees, out Fixed sin, out Fixed cos)
        {
            var x = degrees * DegToRad;
            var x2 = x * x;
            sin = x * (Fixed.One - x2 / 6 * (Fixed.One - x2 / 20 * (Fixed.One - x2 / 42)));
            cos = Fixed.One - x2 / 2 * (Fixed.One - x2 / 12 * (Fixed.One - x2 / 30 * (Fixed.One - x2 / 56)));
        }
    }
}
