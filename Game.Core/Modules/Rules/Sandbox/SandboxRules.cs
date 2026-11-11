using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Sim;

namespace Game.Core.Modules.Rules.Sandbox
{
    /// <summary>The swing sandbox (design/overview.md → «Риск №1»): no zone, no points. Player 0 plays,
    /// the others are mannequins (driven by the idle archetype, see Archetype.Opponents). A mannequin
    /// whose dog was hit goes back, dog and owner, to its spawn <c>rules.sandbox.respawn_s</c> after the
    /// blow. Counts player 0's blows for the swing test bench.</summary>
    public sealed class SandboxRules : IRuleSet
    {
        private const int NoReturn = -1;

        public string Name => "Sandbox";
        public ZoneInfo? Zone => null;
        public ZoneInfo? NextZone => null;
        public bool ZoneJustAnnounced(Simulation sim) => false;

        private readonly int _respawnTicks;
        private readonly int[] _returnAt; // per mannequin: the tick it goes home, or NoReturn
        private readonly int[] _hits, _strongHits, _nearMisses; // per player, as the attacker

        public SandboxRules(GameData data, int players)
        {
            _respawnTicks = (data.Num("rules.sandbox.respawn_s") * Simulation.TicksPerSecond).FloorToInt();
            _returnAt = new int[players];
            for (int p = 0; p < players; p++) _returnAt[p] = NoReturn;
            _hits = new int[players];
            _strongHits = new int[players];
            _nearMisses = new int[players];
        }

        private SandboxRules(SandboxRules src)
        {
            _respawnTicks = src._respawnTicks;
            _returnAt = (int[])src._returnAt.Clone();
            _hits = (int[])src._hits.Clone();
            _strongHits = (int[])src._strongHits.Clone();
            _nearMisses = (int[])src._nearMisses.Clone();
        }

        public void Init(Simulation sim, RulesModule host) { }

        public void Sense(Simulation sim, IDictionary<string, Fixed> hud) { }

        public void Step(Simulation sim, RulesModule host, TickInputs inputs)
        {
            var dogs = sim.Need<DogsModule>(host, DogsModule.ModuleName);
            var hits = sim.Need<HitsModule>(host, HitsModule.ModuleName);
            for (int p = 1; p < _returnAt.Length; p++)
            {
                if (_returnAt[p] == NoReturn || sim.Tick < _returnAt[p]) continue;
                dogs.ReturnToSpawn(sim, p);
                _returnAt[p] = NoReturn;
                var e = sim.Events.Add("arena_event", -1, $"манекен {p} вернулся на место");
                e.Novel = "mannequin_return";
            }
            foreach (var hit in hits.ThisTick)
            {
                if (hit.Attacker < 0) continue; // an object of the arena (none in the sandbox)
                _hits[hit.Attacker]++;
                if (hit.Strong) _strongHits[hit.Attacker]++;
                if (hit.Victim > 0) _returnAt[hit.Victim] = sim.Tick + _respawnTicks; // a new blow starts the wait again
            }
            foreach (var near in hits.NearMissesThisTick) _nearMisses[near.Owner]++;
        }

        public IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player)
        {
            double minutes = sim.Seconds / 60.0;
            return new Dictionary<string, double>
            {
                { "hits", _hits[player] },
                { "strong_hits", _strongHits[player] },
                { "near_misses", _nearMisses[player] },
                { "hits_per_min", minutes == 0 ? 0 : _hits[player] / minutes },
            };
        }

        public void Hash(ref StateHasher h)
        {
            for (int p = 0; p < _returnAt.Length; p++)
            {
                h.Add(_returnAt[p]);
                h.Add(_hits[p]);
                h.Add(_strongHits[p]);
                h.Add(_nearMisses[p]);
            }
        }

        public IRuleSet Clone() => new SandboxRules(this);
    }
}
