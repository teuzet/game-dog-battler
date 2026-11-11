using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>Strike: the shared aimed swing (<see cref="SwingSkill"/>) as a skill. Its numbers stay in
    /// <c>bots.swing.*</c>; the archetype's aim error spreads the point. Stateless.</summary>
    public sealed class StrikeSkill : ISkill
    {
        private readonly SwingSkill _swing;
        private readonly Fixed _aimError;

        public StrikeSkill(SwingSkill swing, Fixed aimError)
        {
            _swing = swing;
            _aimError = aimError;
        }

        public PlayerInput Act(BotView v, SeenObject target, ref DetRandom rng) =>
            Cursor.At(_swing.Aim(v, target, _aimError, ref rng));

        public ISkill Clone() => this;
    }
}
