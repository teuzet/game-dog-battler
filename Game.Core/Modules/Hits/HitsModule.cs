using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Owners;
using Game.Core.Modules.Props;
using Game.Core.Sim;

namespace Game.Core.Modules.Hits
{
    /// <summary>A blow of this tick. Attacker and victim are players; a blow by an
    /// object of the arena has Attacker = -1.</summary>
    public readonly struct Hit
    {
        public readonly int Attacker, Victim;
        public readonly Fixed Strength;
        public readonly bool Strong;

        public Hit(int attacker, int victim, Fixed strength, bool strong)
        {
            Attacker = attacker;
            Victim = victim;
            Strength = strength;
            Strong = strong;
        }
    }

    /// <summary>Blows on dogs. Stub of F-001: it exposes the list of the tick (empty until
    /// F-004 detects blows from the contacts) and the thresholds from Data/hits.json.</summary>
    public sealed class HitsModule : IModule
    {
        public const string ModuleName = "Hits";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { BodiesModule.ModuleName, OwnersModule.ModuleName, PropsModule.ModuleName };

        /// <summary>Relative speed (m/s) from which a touch is a blow, and a strong one.</summary>
        public Fixed Threshold { get; }
        public Fixed StrongThreshold { get; }

        private readonly List<Hit> _tick = new List<Hit>();

        public HitsModule(GameData data)
        {
            Threshold = data.Num("hits.threshold_mps");
            StrongThreshold = data.Num("hits.strong_threshold_mps");
        }

        private HitsModule(HitsModule src)
        {
            Threshold = src.Threshold;
            StrongThreshold = src.StrongThreshold;
            _tick.AddRange(src._tick);
        }

        /// <summary>Blows of the last tick.</summary>
        public IReadOnlyList<Hit> ThisTick => _tick;

        /// <summary>A blow detected in this tick; the one place a blow enters the list.</summary>
        public void Record(Hit hit) => _tick.Add(hit);

        public void Init(Simulation sim) { }

        public void Step(Simulation sim, TickInputs inputs) => _tick.Clear();

        public void Hash(ref StateHasher h)
        {
            h.Add(_tick.Count);
            foreach (var x in _tick) { h.Add(x.Attacker); h.Add(x.Victim); h.Add(x.Strength); h.Add(x.Strong); }
        }

        public IModule Clone() => new HitsModule(this);
    }
}
