using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Match
{
    /// <summary>The timer and the score table; ends the match. Rules award points through
    /// <see cref="Award"/>. A tie for the top is a draw for those who tie.</summary>
    public sealed class MatchModule : IModule
    {
        public const string ModuleName = "Match";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new string[0];

        private readonly int _finalTicks;
        private readonly int[] _scores;
        private int _ticksLeft;

        public MatchModule(GameData data, int players)
        {
            _ticksLeft = data.Int("match.seconds") * Simulation.TicksPerSecond;
            _finalTicks = data.Int("match.final_seconds") * Simulation.TicksPerSecond;
            _scores = new int[players];
        }

        private MatchModule(MatchModule src)
        {
            _finalTicks = src._finalTicks;
            _ticksLeft = src._ticksLeft;
            _scores = (int[])src._scores.Clone();
        }

        public IReadOnlyList<int> Scores => _scores;
        public Fixed TimeLeft => Fixed.Ratio(_ticksLeft, Simulation.TicksPerSecond);

        public void Award(int player, int points, string reason) => _scores[player] += points;

        public void Init(Simulation sim) { }

        public void Step(Simulation sim, TickInputs inputs)
        {
            _ticksLeft--;
            if (_ticksLeft == _finalTicks) AnnounceFinal(sim);
            if (_ticksLeft <= 0) sim.End(Result());
        }

        /// <summary>The last seconds: threat to each player by how far he trails the leader.</summary>
        private void AnnounceFinal(Simulation sim)
        {
            int lead = Top();
            for (int p = 0; p < _scores.Length; p++)
            {
                var e = sim.Events.Add("final_seconds", p, $"последние {_finalTicks / Simulation.TicksPerSecond} с: {_scores[p]} из {lead}");
                e.Threat = lead == 0 ? Fixed.Zero : Fixed.Ratio(lead - _scores[p], lead);
            }
        }

        private int Top()
        {
            int top = 0;
            foreach (var s in _scores) if (s > top) top = s;
            return top;
        }

        private SessionResult Result()
        {
            int top = Top();
            int tied = 0, second = 0;
            foreach (var s in _scores)
                if (s == top) tied++;
                else if (s > second) second = s;
            var outcomes = new Outcome[_scores.Length];
            for (int p = 0; p < outcomes.Length; p++)
                outcomes[p] = _scores[p] != top ? Outcome.Loss : tied > 1 ? Outcome.Draw : Outcome.Win;
            var margin = tied > 1 || top == 0 ? Fixed.Zero : Fixed.Ratio(top - second, top);
            return new SessionResult(outcomes, margin, tied > 1 ? "матч закончен вничью" : "матч закончен по таймеру");
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_ticksLeft);
            foreach (var s in _scores) h.Add(s);
        }

        public IModule Clone() => new MatchModule(this);
    }
}
