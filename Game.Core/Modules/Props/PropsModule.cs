using System.Collections.Generic;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Sim;

namespace Game.Core.Modules.Props
{
    /// <summary>Objects of the arena. Stub of F-001: the arena's list is read, no kinds exist yet
    /// (they come as classes in Props/Kinds with the arenas that use them), so the park has none.</summary>
    public sealed class PropsModule : IModule
    {
        public const string ModuleName = "Props";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { ArenaModule.ModuleName, BodiesModule.ModuleName };

        public void Init(Simulation sim)
        {
            var arena = sim.Need<ArenaModule>(this, ArenaModule.ModuleName);
            if (arena.Props.Count > 0)
                throw new System.NotSupportedException($"arena '{arena.ArenaName}' lists props, but Props has no kinds yet");
        }

        public void Step(Simulation sim, TickInputs inputs) { }
        public void Hash(ref StateHasher h) { }
        public IModule Clone() => this; // no state
    }
}
