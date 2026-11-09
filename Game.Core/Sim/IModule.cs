using System.Collections.Generic;
using Game.Core.Math;

namespace Game.Core.Sim
{
    /// <summary>A module owns one subsystem whole: its state, its data, its
    /// rules. Simulation steps modules in the order GameSetup lists them.
    /// A module reads another only through <see cref="Simulation.Need{T}"/>,
    /// and only one it names in <see cref="DependsOn"/>; MODULES.md is the
    /// map, a contract test checks the code against it.</summary>
    public interface IModule
    {
        /// <summary>Name as in MODULES.md.</summary>
        string Name { get; }

        /// <summary>Modules this one reads, by name.</summary>
        IReadOnlyList<string> DependsOn { get; }

        void Step(Simulation sim, TickInputs inputs);

        /// <summary>Adds every number of the module's state.</summary>
        void Hash(ref StateHasher h);

        /// <summary>A deep copy for a forked simulation.</summary>
        IModule Clone();
    }
}
