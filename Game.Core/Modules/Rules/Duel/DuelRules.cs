using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Match;
using Game.Core.Sim;

namespace Game.Core.Modules.Rules.Duel
{
    /// <summary>The duel (design/duel.md): a dog has health for one round, a blow takes
    /// <c>max(damage_min, damage_per_strength × strength)</c> (× <c>strong_multiplier</c> for a strong one).
    /// Health is at zero → a knockout and the round's point; <c>round_s</c> runs out → the point goes to the
    /// one with more health, none if equal. The match is played to <c>rounds_to_win</c> points
    /// (Match ends it). Between rounds both pairs stand on their spawns for <c>between_rounds_s</c>:
    /// they are put back at once and held there, so nobody's input leads a dog during the pause.
    /// Health lives only here: Dogs and Hits know nothing of it, and an owner has none.</summary>
    public sealed class DuelRules : IRuleSet
    {
        public string Name => "Duel";
        public ZoneInfo? Zone => null;
        public ZoneInfo? NextZone => null;
        public bool ZoneJustAnnounced(Simulation sim) => false;

        private readonly Fixed _maxHp, _damagePerStrength, _damageMin, _strongMultiplier;
        private readonly int _roundsToWin, _roundTicks, _pauseTicks;

        private int _round;          // 1-based; 0 until the first tick
        private int _roundTicksLeft; // 0 during a pause
        private int _pauseTicksLeft; // 0 while a round is on
        private bool _firstBlood;
        private readonly Fixed[] _hp, _dealt, _taken;
        private readonly int[] _roundsWon, _hits, _hitsTaken, _dodged;

        public DuelRules(GameData data, int players)
        {
            var d = data.Get("rules.duel");
            _maxHp = d["hp"]!.AsFixed();
            _damagePerStrength = d["damage_per_strength"]!.AsFixed();
            _damageMin = d["damage_min"]!.AsFixed();
            _strongMultiplier = d["strong_multiplier"]!.AsFixed();
            _roundsToWin = d["rounds_to_win"]!.AsInt();
            _roundTicks = (d["round_s"]!.AsFixed() * Simulation.TicksPerSecond).FloorToInt();
            _pauseTicks = (d["between_rounds_s"]!.AsFixed() * Simulation.TicksPerSecond).FloorToInt();
            _hp = new Fixed[players];
            _dealt = new Fixed[players];
            _taken = new Fixed[players];
            _roundsWon = new int[players];
            _hits = new int[players];
            _hitsTaken = new int[players];
            _dodged = new int[players];
        }

        private DuelRules(DuelRules src)
        {
            _maxHp = src._maxHp;
            _damagePerStrength = src._damagePerStrength;
            _damageMin = src._damageMin;
            _strongMultiplier = src._strongMultiplier;
            _roundsToWin = src._roundsToWin;
            _roundTicks = src._roundTicks;
            _pauseTicks = src._pauseTicks;
            _round = src._round;
            _roundTicksLeft = src._roundTicksLeft;
            _pauseTicksLeft = src._pauseTicksLeft;
            _firstBlood = src._firstBlood;
            _hp = (Fixed[])src._hp.Clone();
            _dealt = (Fixed[])src._dealt.Clone();
            _taken = (Fixed[])src._taken.Clone();
            _roundsWon = (int[])src._roundsWon.Clone();
            _hits = (int[])src._hits.Clone();
            _hitsTaken = (int[])src._hitsTaken.Clone();
            _dodged = (int[])src._dodged.Clone();
        }

        public void Init(Simulation sim, RulesModule host) =>
            sim.Need<MatchModule>(host, MatchModule.ModuleName).SetTarget(_roundsToWin);

        /// <summary>Health of everybody as a share of the full (the bars on the screen), the round and its clock.</summary>
        public void Sense(Simulation sim, IDictionary<string, Fixed> hud)
        {
            for (int q = 0; q < _hp.Length; q++) hud["hp_" + q] = _hp[q] / _maxHp;
            hud["round"] = Fixed.FromInt(_round);
            hud["round_time_left"] = Fixed.Ratio(_roundTicksLeft, Simulation.TicksPerSecond);
        }

        public void Step(Simulation sim, RulesModule host, TickInputs inputs)
        {
            var dogs = sim.Need<DogsModule>(host, DogsModule.ModuleName);
            var match = sim.Need<MatchModule>(host, MatchModule.ModuleName);
            var hits = sim.Need<HitsModule>(host, HitsModule.ModuleName);
            if (_round == 0) StartRound(sim);
            if (_pauseTicksLeft > 0)
            {
                HoldOnSpawns(sim, dogs);
                if (--_pauseTicksLeft == 0) StartRound(sim);
                return;
            }
            foreach (var near in hits.NearMissesThisTick) _dodged[near.Dog]++;
            foreach (var hit in hits.ThisTick)
            {
                if (hit.Attacker < 0) continue; // an object of the arena (the duel has none)
                var damage = Fixed.Max(_damageMin, _damagePerStrength * hit.Strength);
                if (hit.Strong) damage = damage * _strongMultiplier;
                damage = Fixed.Min(damage, _hp[hit.Victim]);
                _hp[hit.Victim] -= damage;
                _dealt[hit.Attacker] += damage;
                _taken[hit.Victim] += damage;
                _hits[hit.Attacker]++;
                _hitsTaken[hit.Victim]++;
                if (_hp[hit.Victim] > Fixed.Zero) continue;
                KnockOut(sim, match, dogs, hit.Attacker, hit.Victim);
                return;
            }
            if (--_roundTicksLeft == 0) TimeUp(sim, match, dogs);
        }

        private void StartRound(Simulation sim)
        {
            _round++;
            _roundTicksLeft = _roundTicks;
            for (int p = 0; p < _hp.Length; p++) _hp[p] = _maxHp;
            var e = sim.Events.Add("arena_event", -1, $"раунд {_round}: бой");
            e.Novel = "round_start";
        }

        private void KnockOut(Simulation sim, MatchModule match, DogsModule dogs, int attacker, int victim)
        {
            var win = sim.Events.Add("knocked_out", attacker, $"участник {attacker} нокаутировал собаку участника {victim}: раунд {_round}");
            win.Reward = Fixed.One;
            if (!_firstBlood) { win.Novel = "first_blood"; _firstBlood = true; }
            var lose = sim.Events.Add("knocked_out", victim, $"собака участника {victim} нокаутирована участником {attacker}: раунд {_round}");
            lose.Threat = Fixed.One;
            EndRound(sim, match, dogs, attacker);
        }

        /// <summary>The round's clock ran out: the point goes to the one with more health, to nobody if they are equal.</summary>
        private void TimeUp(Simulation sim, MatchModule match, DogsModule dogs)
        {
            int best = -1;
            bool tie = false;
            for (int p = 0; p < _hp.Length; p++)
            {
                if (best < 0 || _hp[p] > _hp[best]) { best = p; tie = false; }
                else if (_hp[p] == _hp[best]) tie = true;
            }
            if (tie)
            {
                sim.Events.Add("arena_event", -1, $"раунд {_round}: время вышло, здоровье поровну — очка нет");
                EndRound(sim, match, dogs, -1);
                return;
            }
            sim.Events.Add("arena_event", -1, $"раунд {_round}: время вышло, очко участнику {best} по здоровью");
            EndRound(sim, match, dogs, best);
        }

        private void EndRound(Simulation sim, MatchModule match, DogsModule dogs, int winner)
        {
            if (winner >= 0)
            {
                match.Award(winner, 1, "round");
                _roundsWon[winner]++;
            }
            _roundTicksLeft = 0;
            _pauseTicksLeft = _pauseTicks;
            HoldOnSpawns(sim, dogs);
        }

        private void HoldOnSpawns(Simulation sim, DogsModule dogs)
        {
            for (int p = 0; p < _hp.Length; p++) dogs.ReturnToSpawn(sim, p);
        }

        public IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player) => new Dictionary<string, double>
        {
            { "rounds_won", _roundsWon[player] },
            { "damage_dealt", _dealt[player].ToDouble() },
            { "damage_taken", _taken[player].ToDouble() },
            { "hits", _hits[player] },
            { "hits_taken", _hitsTaken[player] },
            { "near_misses_dodged", _dodged[player] },
        };

        public void Hash(ref StateHasher h)
        {
            h.Add(_round);
            h.Add(_roundTicksLeft);
            h.Add(_pauseTicksLeft);
            h.Add(_firstBlood);
            for (int p = 0; p < _hp.Length; p++)
            {
                h.Add(_hp[p]);
                h.Add(_dealt[p]);
                h.Add(_taken[p]);
                h.Add(_roundsWon[p]);
                h.Add(_hits[p]);
                h.Add(_hitsTaken[p]);
                h.Add(_dodged[p]);
            }
        }

        public IRuleSet Clone() => new DuelRules(this);
    }
}
