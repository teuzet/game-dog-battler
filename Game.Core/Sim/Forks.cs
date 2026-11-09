using System.Collections.Generic;
using Game.Core.Bots;

namespace Game.Core.Sim
{
    /// <summary>Plays options and futures out in forks. Gives the pace map its
    /// two numbers the game cannot know from one session: how much a decision
    /// mattered (spread) and the chance to win over time.</summary>
    public static class Forks
    {
        /// <summary>Fork per option; the player holds the option for
        /// <paramref name="holdTicks"/>, then everyone plays on as their bots for
        /// <paramref name="horizonTicks"/>. Spread = best score − worst.</summary>
        public static double DecisionSpread(IGame game, Simulation sim, IReadOnlyList<BotDriver> bots, int player,
                                            IReadOnlyList<PlayerInput> options, int holdTicks, int horizonTicks)
        {
            if (options.Count < 2) return 0;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var option in options)
            {
                var fork = sim.Fork();
                var fb = CloneAll(bots);
                for (int t = 0; t < horizonTicks && !fork.IsOver; t++)
                    fork.Step(Inputs(game, fork, fb, t < holdTicks ? player : -1, option));
                double s = Score(game, fork, player);
                if (s < lo) lo = s;
                if (s > hi) hi = s;
            }
            return hi - lo;
        }

        /// <summary>Share of <paramref name="rollouts"/> possible futures in which the player wins.</summary>
        public static double WinProbability(IGame game, Simulation sim, IReadOnlyList<BotDriver> bots, int player, int rollouts)
        {
            int wins = 0;
            for (int r = 0; r < rollouts; r++)
            {
                var fork = sim.ForkFuture((ulong)(r + 1));
                var fb = CloneAll(bots);
                while (!fork.IsOver) fork.Step(Inputs(game, fork, fb, -1, PlayerInput.None));
                if (fork.Result!.Outcomes[player] == Outcome.Win) wins++;
                else if (fork.Result.Outcomes[player] == Outcome.Draw) wins += 0; // a draw is not a win
            }
            return rollouts == 0 ? 0 : wins / (double)rollouts;
        }

        public static TickInputs Inputs(IGame game, Simulation sim, IReadOnlyList<BotDriver> bots, int forced, PlayerInput forcedInput)
        {
            var inputs = new TickInputs(sim.Players);
            foreach (var b in bots)
            {
                var view = game.Sense(sim, b.Player);
                var input = b.Next(view);
                inputs[b.Player] = b.Player == forced ? forcedInput : input;
            }
            return inputs;
        }

        private static double Score(IGame game, Simulation sim, int player)
        {
            if (sim.Result == null) return game.Score(sim, player);
            switch (sim.Result.Outcomes[player])
            {
                case Outcome.Win: return 1;
                case Outcome.Loss: return 0;
                default: return 0.5;
            }
        }

        private static List<BotDriver> CloneAll(IReadOnlyList<BotDriver> bots)
        {
            var list = new List<BotDriver>(bots.Count);
            foreach (var b in bots) list.Add(b.Clone());
            return list;
        }
    }
}
