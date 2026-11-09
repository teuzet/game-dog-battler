using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Example
{
    public struct Runner
    {
        public Fixed Pos, Stamina;
        public int Coins, NextFork, BoostTicks, StunTicks, Place;
        public bool Exhausted;
    }

    /// <summary>Owns the runners: movement, stamina, lanes at the forks.
    /// Input: X = 1 sprint, 0 jog, −1 rest; Y &gt; 0 at a fork = the upper lane
    /// (a coin, and a chance to trip).</summary>
    public sealed class Runners : IModule
    {
        public const string ModuleName = "Runners";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { Course.ModuleName };

        public Runner[] All;
        private readonly Numbers _n;
        private bool _finalStretch;

        private sealed class Numbers
        {
            public Fixed Rest, Jog, Sprint, RestGain, JogGain, SprintCost, Max, ExhaustedUntil, Boost, HazardChance, ThreatGap, FinalStretch;
            public int BoostTicks, StunTicks;
        }

        public Runners(int players, GameData data)
        {
            Fixed perTick(string key) => data.Num("game.race." + key) / Simulation.TicksPerSecond;
            _n = new Numbers
            {
                Rest = perTick("rest_speed"), Jog = perTick("jog_speed"), Sprint = perTick("sprint_speed"),
                RestGain = perTick("rest_gain"), JogGain = perTick("jog_gain"), SprintCost = perTick("sprint_cost"),
                Max = data.Num("game.race.stamina_max"), ExhaustedUntil = data.Num("game.race.exhausted_until"),
                Boost = perTick("coin_boost_speed"), HazardChance = data.Num("game.race.hazard_chance"),
                ThreatGap = data.Num("game.race.threat_gap"), FinalStretch = data.Num("game.race.final_stretch"),
                BoostTicks = (data.Num("game.race.coin_boost_s") * Simulation.TicksPerSecond).FloorToInt(),
                StunTicks = (data.Num("game.race.stun_s") * Simulation.TicksPerSecond).FloorToInt(),
            };
            All = new Runner[players];
            for (int i = 0; i < players; i++) All[i] = new Runner { Stamina = _n.Max, Place = 1 };
        }

        private Runners(Runners src)
        {
            _n = src._n;
            _finalStretch = src._finalStretch;
            All = (Runner[])src.All.Clone();
        }

        public void Step(Simulation sim, TickInputs inputs)
        {
            var course = sim.Need<Course>(this, Course.ModuleName);
            for (int p = 0; p < All.Length; p++) Move(sim, course, p, inputs[p]);
            Places(sim);
            if (sim.Tick % Simulation.TicksPerSecond == 0) Threats(sim);
            Fixed lead = Fixed.Zero;
            foreach (var r in All) lead = Fixed.Max(lead, r.Pos);
            if (!_finalStretch && lead >= course.Length * _n.FinalStretch)
            {
                _finalStretch = true;
                sim.Events.Add("final-stretch", -1, "лидер на финишной прямой").Novel = "final-stretch";
            }
        }

        private void Move(Simulation sim, Course course, int p, PlayerInput input)
        {
            ref var r = ref All[p];
            if (r.StunTicks > 0) { r.StunTicks--; return; }
            int mode = r.Exhausted ? -1 : input.X;
            Fixed speed = mode > 0 ? _n.Sprint : mode < 0 ? _n.Rest : _n.Jog;
            r.Stamina += mode > 0 ? -_n.SprintCost : mode < 0 ? _n.RestGain : _n.JogGain;
            r.Stamina = Fixed.Clamp(r.Stamina, Fixed.Zero, _n.Max);
            if (!r.Exhausted && r.Stamina <= Fixed.Zero)
            {
                r.Exhausted = true;
                var e = sim.Events.Add("exhausted", p, "выдохся");
                e.Novel = "exhausted";
                e.Threat = Fixed.Ratio(6, 10);
            }
            else if (r.Exhausted && r.Stamina >= _n.ExhaustedUntil) r.Exhausted = false;
            if (r.BoostTicks > 0) { r.BoostTicks--; speed += _n.Boost; }
            r.Pos += speed;
            if (r.NextFork < course.Forks.Count && r.Pos >= course.Forks[r.NextFork]) Fork(sim, p, ref r, input.Y > 0);
        }

        private void Fork(Simulation sim, int p, ref Runner r, bool upper)
        {
            int fork = ++r.NextFork;
            var d = sim.Events.Add("fork", p, $"развилка {fork}: {(upper ? "верх" : "низ")}");
            d.DecisionOptions = 2;
            if (!upper) return;
            r.Coins++;
            r.BoostTicks = _n.BoostTicks;
            var c = sim.Events.Add("coin", p, $"монета ({r.Coins})");
            c.Reward = Fixed.One;
            c.Novel = r.Coins == 1 ? "first-coin" : "coin";
            if (sim.Rng(this).Chance(_n.HazardChance))
            {
                r.StunTicks = _n.StunTicks;
                r.BoostTicks = 0;
                var h = sim.Events.Add("trip", p, "споткнулся");
                h.Novel = "trip";
                h.Threat = Fixed.Ratio(8, 10);
            }
        }

        private void Places(Simulation sim)
        {
            for (int p = 0; p < All.Length; p++)
            {
                int place = 1;
                for (int q = 0; q < All.Length; q++)
                    if (q != p && (All[q].Pos > All[p].Pos || (All[q].Pos == All[p].Pos && q < p))) place++;
                if (place < All[p].Place && sim.Tick > Simulation.TicksPerSecond)
                {
                    var e = sim.Events.Add("overtake", p, $"обогнал, теперь {place}-й");
                    e.Reward = Fixed.Half;
                    e.Novel = place == 1 ? "take-lead" : "overtake";
                }
                All[p].Place = place;
            }
        }

        private void Threats(Simulation sim)
        {
            Fixed lead = Fixed.Zero;
            foreach (var r in All) lead = Fixed.Max(lead, r.Pos);
            for (int p = 0; p < All.Length; p++)
            {
                var r = All[p];
                Fixed behind = Fixed.Clamp01((lead - r.Pos) / _n.ThreatGap);
                Fixed tired = r.Exhausted ? Fixed.Ratio(3, 10) : Fixed.Zero;
                sim.Events.Add("pulse", p, "").Threat = Fixed.Clamp01(behind + tired);
            }
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_finalStretch);
            foreach (var r in All)
            {
                h.Add(r.Pos); h.Add(r.Stamina); h.Add(r.Coins); h.Add(r.NextFork);
                h.Add(r.BoostTicks); h.Add(r.StunTicks); h.Add(r.Place); h.Add(r.Exhausted);
            }
        }

        public IModule Clone() => new Runners(this);
    }
}
