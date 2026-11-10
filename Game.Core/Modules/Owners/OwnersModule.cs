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
        private readonly Fixed _dogSpeedFactor, _tensionBonus, _speedMax, _brake;

        /// <summary>The session picks live in data, so a logged session replays: the human (player 0)
        /// has "game.human_owner", bots take "bots.session.owner_picks" in turn.</summary>
        public OwnersModule(GameData data, int players)
        {
            _types = new OwnerType[players];
            _body = new int[players];
            _dogSpeedFactor = data.Num("owners.speed_rule.dog_speed_factor");
            _tensionBonus = data.Num("owners.speed_rule.tension_bonus_mps");
            _speedMax = data.Num("owners.speed_rule.max_mps");
            _brake = data.Num("owners.speed_rule.brake_mps2");
            var botPicks = data.Get("bots.session.owner_picks").Items;
            for (int p = 0; p < players; p++)
            {
                string pick = p == 0 ? data.Str("game.human_owner") : botPicks[(p - 1) % botPicks.Count].AsString();
                _types[p] = OwnerTypeRegistry.Create(pick, data);
            }
        }

        private OwnersModule(OwnersModule src)
        {
            _types = src._types; // immutable
            _dogSpeedFactor = src._dogSpeedFactor;
            _tensionBonus = src._tensionBonus;
            _speedMax = src._speedMax;
            _brake = src._brake;
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

        /// <summary>The speed ceiling of design/roster.md → «Удар»: 1.6 × the dog's speed + a bonus that grows with
        /// the leash tension, never above <c>owners.speed_rule.max_mps</c>. Speed above it is not cut at once
        /// but braked by <c>owners.speed_rule.brake_mps2</c>: the owner is only as fast as the dog that swings him,
        /// yet a shove from another owner still moves him before the brake eats it.</summary>
        public void Step(Simulation sim, TickInputs inputs)
        {
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            var leashes = sim.Need<LeashesModule>(this, LeashesModule.ModuleName);
            var brakePerTick = _brake / Simulation.TicksPerSecond;
            for (int p = 0; p < _body.Length; p++)
            {
                var ceiling = Fixed.Min(_speedMax, _dogSpeedFactor * bodies.Velocity(leashes.DogBody(p)).Length + _tensionBonus * leashes.Tension(sim, p));
                var vel = bodies.Velocity(_body[p]);
                if (vel.Length > ceiling) bodies.SetVelocity(_body[p], vel.ClampLength(Fixed.Max(ceiling, vel.Length - brakePerTick)));
            }
        }

        public void Hash(ref StateHasher h)
        {
            foreach (var b in _body) h.Add(b);
        }

        public IModule Clone() => new OwnersModule(this);
    }
}
