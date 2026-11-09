using System.Collections.Generic;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Sim;

namespace Game.Core
{
    /// <summary>What the factory's runner, the contract tests and the Unity
    /// shell need to know about the game. One implementation per game, in
    /// GameSetup.</summary>
    public interface IGame
    {
        string Name { get; }
        int Players { get; }

        /// <summary>A new session: the modules in tick order.</summary>
        Simulation Create(int seed, GameData data);

        /// <summary>Honest sensors: what the player sees on screen.</summary>
        BotView Sense(Simulation sim, int player);

        /// <summary>A strategy by its name in archetypes.json; idle and random are built in.</summary>
        IBotStrategy Strategy(Archetype archetype, GameData data);

        /// <summary>Inputs the player can choose between right now, if this is
        /// a moment of decision (empty otherwise). The runner plays each out in
        /// a fork to measure how much the choice matters.</summary>
        IReadOnlyList<PlayerInput> DecisionOptions(Simulation sim, int player);

        /// <summary>0..1: how well the player stands; compares forks.</summary>
        double Score(Simulation sim, int player);

        /// <summary>One line of state for the chronicle, without judgement.</summary>
        string Describe(Simulation sim);

        /// <summary>Game metrics of a finished session for metrics.json (the hypotheses' numbers).</summary>
        IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player);
    }
}
