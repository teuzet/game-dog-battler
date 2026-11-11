using System;
using Game.Core.Bots.Skills;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots
{
    /// <summary>Leads one skill of the <see cref="SkillSet"/> and nothing else (archetype <c>params.skill</c>: move, spin,
    /// strike, dodge, brake), against the nearest rival dog. The playground of evolve: what a skill scores alone,
    /// without the decisions of a whole bot. A solo-dodge never swings; it only stays out of the way.</summary>
    public sealed class SoloStrategy : IBotStrategy
    {
        private readonly ISkill _skill;

        public SoloStrategy(SkillSet skills, string skill) : this(Pick(skills, skill)) { }

        private SoloStrategy(ISkill skill) { _skill = skill; }

        private static ISkill Pick(SkillSet s, string skill)
        {
            switch (skill)
            {
                case "move": return s.Move;
                case "spin": return s.Spin;
                case "strike": return s.Strike;
                case "dodge": return s.Dodge;
                case "brake": return s.Brake;
                default: throw new ArgumentException($"no skill '{skill}' (move, spin, strike, dodge, brake)");
            }
        }

        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            if (!Cursor.NearestRival(view, new Vec2(view.X, view.Y), -Fixed.One, out var rival))
                throw new InvalidOperationException("solo: no rival dog on the screen");
            return _skill.Act(view, rival, ref rng);
        }

        public IBotStrategy Clone() => new SoloStrategy(_skill.Clone());
    }
}
