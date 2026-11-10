using System.Collections.Generic;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Match;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;
using Xunit.Abstractions;

namespace Game.Core.Tests.Bots
{
    /// <summary>F-009: the breakers play the park by the same rules as every bot (seen screen, reaction, hold)
    /// and their numbers are reported, not asserted to lose: whether they dominate is the finding.</summary>
    public class BreakerTests
    {
        private readonly ITestOutputHelper _out;

        public BreakerTests(ITestOutputHelper output) { _out = output; }

        /// <summary>The session runner's line-up: the tracked archetype as player 0 against session.json opponents.</summary>
        private static (Simulation Sim, int Points) Play(string archetype, int seed)
        {
            var data = Repo.Data();
            var game = GameSetup.Game;
            var sim = game.Create(seed, data);
            var opponents = Archetype.Opponents(data);
            var bots = new List<BotDriver>();
            for (int p = 0; p < sim.Players; p++)
            {
                var a = Archetype.Find(data, p == 0 ? archetype : opponents[(p - 1) % opponents.Count]);
                bots.Add(new BotDriver(p, a, game.Strategy(a, data), (ulong)seed));
            }
            while (!sim.IsOver) sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
            return (sim, (int)game.Metrics(sim, 0)["points"]);
        }

        [Theory]
        [InlineData("breaker-squatter")]
        [InlineData("breaker-orbit")]
        public void BreakersAreArchetypesWithAnAboutAndHonestTiming(string name)
        {
            var a = Archetype.Find(Repo.Data(), name);
            Assert.False(string.IsNullOrWhiteSpace(a.About));
            Assert.True(a.ReactionMs >= 250, "no faster than smart");
            Assert.True(a.HoldMs >= 600, "keeps a decision at least as long as smart");
            Assert.Contains(name, System.Linq.Enumerable.Select(Repo.Data().Get("bots.session.tracked").Items, i => i.AsString()));
        }

        [Theory]
        [InlineData("breaker-squatter")]
        [InlineData("breaker-orbit")]
        public void BreakersFinishTheMatchAndScoreMoreThanIdle(string name)
        {
            for (int seed = 1; seed <= 2; seed++)
            {
                var (sim, points) = Play(name, seed);
                var (_, idle) = Play("idle", seed);
                Assert.True(sim.IsOver);
                Assert.True(points > idle, $"{name} {points} vs idle {idle} on seed {seed}");
            }
        }

        [Fact]
        public void SquatterIsInTheNextCircleBeforeItMoves()
        {
            var data = Repo.Data();
            var game = GameSetup.Game;
            var sim = game.Create(3, data);
            var a = Archetype.Find(data, "breaker-squatter");
            var bots = Repo.Bots(game, data, sim.Players, 3, "idle");
            bots[0] = new BotDriver(0, a, game.Strategy(a, data), 3UL);
            var rules = sim.Get<RulesModule>(RulesModule.ModuleName).Active;
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var period = data.Int("rules.king_of_lawn.period_s") * Simulation.TicksPerSecond;
            while (sim.Tick < period - 1) sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
            var next = rules.NextZone!.Value;
            Assert.True(dogs.InZone(sim, 0, next.Center, next.Radius), "it ran to the announced circle and waits there");
        }

        /// <summary>Not a gate: prints the breakers' match results next to smart's on the same seeds
        /// (the sim tool's numbers are the report; this is the quick look without forks).</summary>
        [Fact(Skip = "measurement, not a gate: remove Skip and run with --filter BreakerTests.Measure")]
        public void Measure()
        {
            foreach (var name in new[] { "smart", "camper", "breaker-squatter", "breaker-orbit" })
            {
                int wins = 0, points = 0, n = 20, knockedOut = 0, hitOn = 0;
                var rivals = new int[4];
                for (int seed = 1; seed <= n; seed++)
                {
                    var (sim, pts) = Play(name, seed);
                    wins += sim.Result!.Outcomes[0] == Outcome.Win ? 1 : 0;
                    points += pts;
                    for (int p = 1; p < 4; p++) rivals[p] += sim.Get<MatchModule>(MatchModule.ModuleName).Scores[p];
                    foreach (var e in sim.Events.All)
                        if (e.Actor == 0 && e.Kind == "knocked_out" && e.Threat != null) knockedOut++;
                        else if (e.Actor == 0 && (e.Kind == "hit" || e.Kind == "strong_hit") && e.Threat != null) hitOn++;
                }
                _out.WriteLine($"{name}: wins {wins}/{n}, points {points / (double)n:0.0}, rivals smart/random/idle " +
                               $"{rivals[1] / (double)n:0.0}/{rivals[2] / (double)n:0.0}/{rivals[3] / (double)n:0.0}, " +
                               $"knocked out {knockedOut / (double)n:0.0}, hit {hitOn / (double)n:0.0} a match");
            }
        }
    }
}
