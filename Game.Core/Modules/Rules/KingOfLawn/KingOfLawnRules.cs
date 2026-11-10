using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Match;
using Game.Core.Sim;

namespace Game.Core.Modules.Rules.KingOfLawn
{
    /// <summary>King of the Lawn. Stub of F-001: a circle that moves to a random place of the
    /// arena every period; once a second a dog alone in it earns points. Knock-outs, the
    /// 5 s warning and the 8–14 m moves come with F-005.</summary>
    public sealed class KingOfLawnRules : IRuleSet
    {
        public string Name => "KingOfLawn";
        public ZoneInfo? Zone => _placed ? new ZoneInfo(_center, _radius) : (ZoneInfo?)null;

        private readonly Fixed _radius;
        private readonly int _periodTicks, _pointsPerSecond;
        private bool _placed;
        private Vec2 _center;
        private int _holder = -1; // who earned in the last second: a new holder is an event

        public KingOfLawnRules(GameData data)
        {
            _radius = data.Num("rules.king_of_lawn.zone_radius_m");
            _periodTicks = data.Int("rules.king_of_lawn.period_s") * Simulation.TicksPerSecond;
            _pointsPerSecond = data.Int("rules.king_of_lawn.points_per_s");
        }

        private KingOfLawnRules(KingOfLawnRules src)
        {
            _radius = src._radius;
            _periodTicks = src._periodTicks;
            _pointsPerSecond = src._pointsPerSecond;
            _placed = src._placed;
            _center = src._center;
            _holder = src._holder;
        }

        public void Step(Simulation sim, RulesModule host, TickInputs inputs)
        {
            var dogs = sim.Need<DogsModule>(host, DogsModule.ModuleName);
            var match = sim.Need<MatchModule>(host, MatchModule.ModuleName);
            if (!_placed || (sim.Tick - 1) % _periodTicks == 0) Place(sim, host);
            if (sim.Tick % Simulation.TicksPerSecond != 0) return;
            int alone = -1, inside = 0;
            for (int p = 0; p < sim.Players; p++)
                if (dogs.InZone(sim, p, _center, _radius)) { inside++; alone = p; }
            if (inside != 1) { _holder = -1; return; }
            match.Award(alone, _pointsPerSecond, "zone");
            if (_holder == alone) return;
            _holder = alone;
            var e = sim.Events.Add("zone_taken", alone, $"участник {alone} занял круг");
            e.Reward = Fixed.One;
        }

        private void Place(Simulation sim, RulesModule host)
        {
            var arena = sim.Need<ArenaModule>(host, ArenaModule.ModuleName);
            ref var rng = ref sim.Rng(host);
            var halfW = arena.Width / 2 - _radius;
            var halfH = arena.Height / 2 - _radius;
            _center = new Vec2((rng.NextFixed01() * 2 - Fixed.One) * halfW, (rng.NextFixed01() * 2 - Fixed.One) * halfH);
            _placed = true;
            _holder = -1;
            var e = sim.Events.Add("zone_announced", -1, $"новый круг: ({_center.X}, {_center.Y})");
            e.Novel = "zone_announced";
            e.DecisionOptions = 2; // run to it or fight over the old one
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_placed);
            h.Add(_center);
            h.Add(_holder);
        }

        public IRuleSet Clone() => new KingOfLawnRules(this);
    }
}
