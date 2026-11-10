using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Match
{
    /// <summary>Points one player got from one award.</summary>
    public readonly struct PointsAward
    {
        public readonly int Player, Points;
        public readonly string Reason;
        public PointsAward(int player, int points, string reason) { Player = player; Points = points; Reason = reason; }
    }

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
        private int _leader = -1; // the last sole top scorer, -1 = nobody yet
        private List<PointsAward> _awarding = new List<PointsAward>(), _lastAwards = new List<PointsAward>();

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
            _leader = src._leader;
            _scores = (int[])src._scores.Clone();
            _awarding = new List<PointsAward>(src._awarding);
            _lastAwards = new List<PointsAward>(src._lastAwards);
        }

        public IReadOnlyList<int> Scores => _scores;
        public Fixed TimeLeft => Fixed.Ratio(_ticksLeft, Simulation.TicksPerSecond);

        public void Award(int player, int points, string reason)
        {
            _scores[player] += points;
            _awarding.Add(new PointsAward(player, points, reason));
        }

        /// <summary>The points given during the last finished tick, in the order the rules gave them
        /// (reasons: "zone", "knockout", "leader_knockout"). For views; not part of the state hash.</summary>
        public IReadOnlyList<PointsAward> LastTickAwards => _lastAwards;

        public void Init(Simulation sim) { }

        public void Step(Simulation sim, TickInputs inputs)
        {
            // the rules ran earlier in this tick: what they awarded becomes "the last tick's"
            (_lastAwards, _awarding) = (_awarding, _lastAwards);
            _awarding.Clear();
            _ticksLeft--;
            CheckLead(sim);
            if (_ticksLeft == _finalTicks) AnnounceFinal(sim);
            if (_ticksLeft <= 0) sim.End(Result());
        }

        /// <summary>A new sole top scorer is a lead change: Reward to him, Threat to the one who lost the lead.</summary>
        private void CheckLead(Simulation sim)
        {
            int top = Top(), who = -1, second = 0;
            for (int p = 0; p < _scores.Length; p++)
                if (_scores[p] == top && top > 0) who = who == -1 ? p : -2;
            if (who < 0 || who == _leader) return;
            for (int p = 0; p < _scores.Length; p++)
                if (p != who && _scores[p] > second) second = _scores[p];
            int old = _leader;
            _leader = who;
            var e = sim.Events.Add("lead_change", who, $"участник {who} вышел вперёд: {top} против {second}");
            e.Reward = Fixed.Ratio(top - second, top);
            if (old < 0) return;
            var lost = sim.Events.Add("lead_change", old, $"участник {old} потерял лидерство: {_scores[old]} против {top}");
            lost.Threat = Fixed.One - Fixed.Ratio(top - _scores[old], top);
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
            return new SessionResult(outcomes, margin, tied > 1
                ? $"матч закончен вничью: {top} очков"
                : $"матч закончен по таймеру: победил участник {System.Array.IndexOf(_scores, top)}, {top} против {second}");
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_ticksLeft);
            h.Add(_leader);
            foreach (var s in _scores) h.Add(s);
        }

        public IModule Clone() => new MatchModule(this);
    }
}
