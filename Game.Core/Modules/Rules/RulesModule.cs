using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Match;
using Game.Core.Modules.Rules.KingOfLawn;
using Game.Core.Modules.Rules.Sandbox;
using Game.Core.Sim;

namespace Game.Core.Modules.Rules
{
    /// <summary>The circle that earns points, for the sensors of bots and for Unity.</summary>
    public readonly struct ZoneInfo
    {
        public readonly Vec2 Center;
        public readonly Fixed Radius;
        public ZoneInfo(Vec2 center, Fixed radius) { Center = center; Radius = radius; }
    }

    /// <summary>A set of rules of one arena: a class in Modules/Rules/&lt;Name&gt;/. It does not
    /// know breeds ("is the player in the zone" is asked of Dogs) and awards points through Match.</summary>
    public interface IRuleSet
    {
        string Name { get; }
        /// <summary>The scoring circle, if the rules have one.</summary>
        ZoneInfo? Zone { get; }
        /// <summary>The announced next place of the circle, if the rules have one and it is announced.</summary>
        ZoneInfo? NextZone { get; }
        /// <summary>The next place was announced a moment ago: the player may choose to run or fight.</summary>
        bool ZoneJustAnnounced(Simulation sim);
        /// <summary>The rules' own numbers of a player for metrics.json.</summary>
        IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player);
        void Step(Simulation sim, RulesModule host, TickInputs inputs);
        void Hash(ref StateHasher h);
        IRuleSet Clone();
    }

    /// <summary>Holds the rule set chosen by "rules" in the arena json.</summary>
    public sealed class RulesModule : IModule
    {
        public const string ModuleName = "Rules";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[]
        {
            ArenaModule.ModuleName, BodiesModule.ModuleName, DogsModule.ModuleName, HitsModule.ModuleName, MatchModule.ModuleName,
        };

        public IRuleSet Active { get; }

        public RulesModule(GameData data, int players)
        {
            Active = RuleSetRegistry.Create(data.Get("arenas." + data.Str("game.arena"))["rules"]!.AsString(), data, players);
        }

        private RulesModule(RulesModule src) { Active = src.Active.Clone(); }

        public void Init(Simulation sim)
        {
            var arena = sim.Need<ArenaModule>(this, ArenaModule.ModuleName);
            if (arena.RulesName != Active.Name) throw new System.InvalidOperationException($"rules '{Active.Name}' do not match arena '{arena.ArenaName}'");
        }

        public void Step(Simulation sim, TickInputs inputs) => Active.Step(sim, this, inputs);

        public void Hash(ref StateHasher h)
        {
            h.Add(Active.Name);
            Active.Hash(ref h);
        }

        public IModule Clone() => new RulesModule(this);
    }

    /// <summary>One line per rule set: name in the arena json → class.</summary>
    public static class RuleSetRegistry
    {
        public static IRuleSet Create(string name, GameData data, int players)
        {
            switch (name)
            {
                case "KingOfLawn": return new KingOfLawnRules(data, players);
                case "Sandbox": return new SandboxRules();
                default: throw new KeyNotFoundException($"no rule set '{name}' in the registry (Modules/Rules)");
            }
        }
    }
}
