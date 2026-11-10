using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Control;
using Game.Core.Modules.Dogs.Breeds;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Props;
using Game.Core.Sim;

namespace Game.Core.Modules.Dogs
{
    /// <summary>The dogs: one per player, a breed each (pairs in "game.pairs"). The dog
    /// accelerates to the intent's velocity within the breed's speed; no abilities in
    /// sprint 1; stun and prop pulls come later.</summary>
    public sealed class DogsModule : IModule
    {
        public const string ModuleName = "Dogs";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[]
        {
            ArenaModule.ModuleName, ControlModule.ModuleName, BodiesModule.ModuleName, HitsModule.ModuleName, LeashesModule.ModuleName, PropsModule.ModuleName,
        };

        private readonly Breed[] _breeds;
        private readonly int[] _body;

        public DogsModule(GameData data, int players)
        {
            _breeds = new Breed[players];
            _body = new int[players];
            var pairs = data.Get("game.pairs").Items;
            for (int p = 0; p < players; p++) _breeds[p] = BreedRegistry.Create(pairs[p % pairs.Count]["dog"]!.AsString(), data);
        }

        private DogsModule(DogsModule src)
        {
            _breeds = src._breeds; // immutable
            _body = (int[])src._body.Clone();
        }

        public int Players => _breeds.Length;
        public Breed BreedOf(int player) => _breeds[player];
        public int BodyOf(int player) => _body[player];

        public Vec2 Position(Simulation sim, int player) =>
            sim.Need<BodiesModule>(this, BodiesModule.ModuleName).Position(_body[player]);

        /// <summary>Is the player's dog inside the circle? Rules ask this instead of knowing breeds.</summary>
        public bool InZone(Simulation sim, int player, Vec2 center, Fixed radius) =>
            Position(sim, player).DistanceTo(center) <= radius;

        public void Init(Simulation sim)
        {
            var arena = sim.Need<ArenaModule>(this, ArenaModule.ModuleName);
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            var control = sim.Need<ControlModule>(this, ControlModule.ModuleName);
            var leashes = sim.Need<LeashesModule>(this, LeashesModule.ModuleName);
            var hits = sim.Need<HitsModule>(this, HitsModule.ModuleName);
            for (int p = 0; p < _breeds.Length; p++)
            {
                var b = _breeds[p];
                _body[p] = bodies.AddCircle(arena.Spawns[p % arena.Spawns.Count].Dog, b.Mass, b.Radius, b.Friction);
                control.Bind(p, _body[p]);
                hits.BindDog(p, _body[p]);
                leashes.AttachDog(p, _body[p], b.LeashLength);
            }
        }

        public void Step(Simulation sim, TickInputs inputs)
        {
            var control = sim.Need<ControlModule>(this, ControlModule.ModuleName);
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            var hits = sim.Need<HitsModule>(this, HitsModule.ModuleName);
            for (int p = 0; p < _breeds.Length; p++)
            {
                if (hits.IsHelpless(p)) continue; // a stunned or thrown dog is not driven: friction slows it
                var b = _breeds[p];
                var want = control.Of(p).Velocity.ClampLength(b.Speed);
                var vel = bodies.Velocity(_body[p]);
                var steered = b.SteerVelocity(vel, want);
                if (steered != vel) bodies.SetVelocity(_body[p], steered);
                bodies.Drive(_body[p], want, b.Speed / b.AccelTime);
            }
        }

        public void Hash(ref StateHasher h)
        {
            foreach (var b in _body) h.Add(b);
        }

        public IModule Clone() => new DogsModule(this);
    }
}
