using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Example
{
    /// <summary>Owns the rule of the ending: the first across the line wins.</summary>
    public sealed class Referee : IModule
    {
        public const string ModuleName = "Referee";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { Course.ModuleName, Runners.ModuleName };
        private readonly Fixed _marginScale;

        public Referee(GameData data) { _marginScale = data.Num("game.race.margin_scale"); }

        public void Step(Simulation sim, TickInputs inputs)
        {
            var course = sim.Need<Course>(this, Course.ModuleName);
            var all = sim.Need<Runners>(this, Runners.ModuleName).All;
            int best = -1;
            for (int p = 0; p < all.Length; p++)
                if (all[p].Pos >= course.Length && (best < 0 || all[p].Pos > all[best].Pos)) best = p;
            if (best < 0) return;
            Fixed second = Fixed.Zero;
            for (int p = 0; p < all.Length; p++) if (p != best) second = Fixed.Max(second, all[p].Pos);
            var outcomes = new Outcome[all.Length];
            for (int p = 0; p < all.Length; p++) outcomes[p] = p == best ? Outcome.Win : Outcome.Loss;
            sim.End(new SessionResult(outcomes, (all[best].Pos - second) / _marginScale, $"финишировал участник {best}"));
        }

        public void Hash(ref StateHasher h) => h.Add(_marginScale);
        public IModule Clone() => this; // no state
    }
}
