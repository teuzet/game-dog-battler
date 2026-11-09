using System.Collections.Generic;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Example
{
    /// <summary>Owns the track: its length and where the forks are. Built once from data.</summary>
    public sealed class Course : IModule
    {
        public const string ModuleName = "Course";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new string[0];

        public readonly Fixed Length;
        public readonly List<Fixed> Forks = new List<Fixed>();

        public Course(Fixed length, Fixed forkEvery)
        {
            Length = length;
            for (var x = forkEvery; x < length - forkEvery / 2; x += forkEvery) Forks.Add(x);
        }

        public void Step(Simulation sim, TickInputs inputs) { }

        public void Hash(ref StateHasher h)
        {
            h.Add(Length);
            foreach (var f in Forks) h.Add(f);
        }

        public IModule Clone() => this; // never changes after creation
    }
}
