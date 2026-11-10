using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Control;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Owners.Types;
using Game.Core.Sim;

namespace Game.Core.Modules.Owners
{
    /// <summary>The owners: one per player, the type he picked. Sprint 1: an owner is a plain
    /// body with no abilities and no health; it moves only by the leash.</summary>
    public sealed class OwnersModule : IModule
    {
        public const string ModuleName = "Owners";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[]
        {
            ArenaModule.ModuleName, ControlModule.ModuleName, BodiesModule.ModuleName, LeashesModule.ModuleName,
        };

        private readonly OwnerType[] _types;
        private readonly int[] _body;

        /// <summary>One owner type name per player: what each player picked in the session setup.</summary>
        public OwnersModule(GameData data, IReadOnlyList<string> picks)
        {
            _types = new OwnerType[picks.Count];
            _body = new int[picks.Count];
            for (int p = 0; p < picks.Count; p++) _types[p] = OwnerTypeRegistry.Create(picks[p], data);
        }

        private OwnersModule(OwnersModule src)
        {
            _types = src._types; // immutable
            _body = (int[])src._body.Clone();
        }

        public OwnerType TypeOf(int player) => _types[player];
        public int BodyOf(int player) => _body[player];

        public Vec2 Position(Simulation sim, int player) =>
            sim.Need<BodiesModule>(this, BodiesModule.ModuleName).Position(_body[player]);

        public void Init(Simulation sim)
        {
            var arena = sim.Need<ArenaModule>(this, ArenaModule.ModuleName);
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            var leashes = sim.Need<LeashesModule>(this, LeashesModule.ModuleName);
            for (int p = 0; p < _types.Length; p++)
            {
                var t = _types[p];
                _body[p] = bodies.AddCircle(arena.Spawns[p % arena.Spawns.Count].Owner, t.Mass, t.Radius, t.Friction);
                leashes.AttachOwner(p, _body[p]);
            }
        }

        public void Step(Simulation sim, TickInputs inputs) { }

        public void Hash(ref StateHasher h)
        {
            foreach (var b in _body) h.Add(b);
        }

        public IModule Clone() => new OwnersModule(this);
    }
}
