using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots.Skills
{
    /// <summary>Brake: put the dog's speed out. The cursor goes to the stopping point: the dog's place plus its
    /// velocity times <c>stop_gain_s</c>, a short step ahead, because the dog follows the cursor at a speed
    /// proportional to the distance and so slows as it nears it. Stateless.</summary>
    public sealed class BrakeSkill : ISkill
    {
        private readonly Fixed _stopGain;

        public BrakeSkill(GameData data) { _stopGain = data.Num("bots.skills.brake.stop_gain_s"); }

        public PlayerInput Act(BotView v, SeenObject target, ref DetRandom rng) =>
            Cursor.At(Seen.Me(v) + new Vec2(v.VX, v.VY) * _stopGain);

        public ISkill Clone() => this;
    }
}
