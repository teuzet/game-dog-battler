// Contract tests: the invariants every game of the factory keeps (see
// ARCHITECTURE.md → «Инварианты»). They run against GameSetup.Game, so they
// stay as they are when the example is replaced by the real game.
using System.Text.RegularExpressions;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;
using Xunit;

namespace Game.Core.Tests.Contract
{
    public static class Repo
    {
        public static readonly string DataDir = GameData.FindDataDir(AppContext.BaseDirectory);
        public static readonly string Root = Path.GetDirectoryName(Path.GetDirectoryName(DataDir)!)!;
        public static GameData Data() => GameData.Load(DataDir);

        /// <summary>Every participant played by the given archetype (default: smart).</summary>
        public static List<BotDriver> Bots(IGame game, GameData data, int players, int seed, string archetype = "smart")
        {
            var a = Archetype.Find(data, archetype);
            var list = new List<BotDriver>();
            for (int p = 0; p < players; p++) list.Add(new BotDriver(p, a, game.Strategy(a, data), (ulong)seed));
            return list;
        }

        public static Simulation PlayOut(IGame game, GameData data, int seed, int maxTicks = int.MaxValue, string archetype = "smart")
        {
            var sim = game.Create(seed, data);
            var bots = Bots(game, data, sim.Players, seed, archetype);
            while (!sim.IsOver && sim.Tick < maxTicks) sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
            return sim;
        }
    }

    public class ContractTests
    {
        private static readonly IGame Game = GameSetup.Game;

        [Fact]
        public void SameSeedAndInputsGiveSameHash()
        {
            var data = Repo.Data();
            foreach (int seed in new[] { 1, 2, 77 })
            {
                var a = Repo.PlayOut(Game, data, seed);
                var b = Repo.PlayOut(Game, data, seed);
                Assert.Equal(a.StateHash(), b.StateHash());
                Assert.Equal(a.Tick, b.Tick);
            }
        }

        [Fact]
        public void DifferentSeedsGiveDifferentSessions()
        {
            var data = Repo.Data();
            var hashes = new HashSet<ulong>();
            for (int seed = 1; seed <= 5; seed++) hashes.Add(Repo.PlayOut(Game, data, seed, 30 * Simulation.TicksPerSecond).StateHash());
            Assert.True(hashes.Count > 1, "the seed changes nothing: is the world random at all?");
        }

        [Fact]
        public void ForkIsAnIndependentCopy()
        {
            var data = Repo.Data();
            var sim = Repo.PlayOut(Game, data, 3, 5 * Simulation.TicksPerSecond);
            var fork = sim.Fork();
            Assert.Equal(sim.StateHash(), fork.StateHash());
            var before = sim.StateHash();
            var bots = Repo.Bots(Game, data, fork.Players, 3);
            for (int i = 0; i < 60 && !fork.IsOver; i++) fork.Step(Forks.Inputs(Game, fork, bots, -1, PlayerInput.None));
            Assert.Equal(before, sim.StateHash());
            Assert.NotEqual(before, fork.StateHash());
        }

        [Fact]
        public void EmptyInputNeverBreaksAndTheSessionAlwaysEnds()
        {
            var data = Repo.Data();
            var sim = Game.Create(5, data);
            var empty = TickInputs.Empty(sim.Players);
            while (!sim.IsOver) sim.Step(empty);
            Assert.NotNull(sim.Result);
            Assert.True(sim.Tick <= sim.MaxTicks);
            Assert.Equal(sim.Players, sim.Result!.Outcomes.Length);
        }

        [Fact]
        public void BotSessionsEnd()
        {
            var data = Repo.Data();
            foreach (var a in Archetype.LoadAll(data))
            {
                var sim = Repo.PlayOut(Game, data, 11, archetype: a.Name);
                Assert.True(sim.IsOver, $"archetype {a.Name}: the session did not end");
            }
        }

        [Fact]
        public void RequiredArchetypesExist()
        {
            var names = Archetype.LoadAll(Repo.Data()).Select(a => a.Name).ToList();
            foreach (var required in new[] { "idle", "random", "smart" }) Assert.Contains(required, names);
            foreach (var a in Archetype.LoadAll(Repo.Data()))
            {
                Assert.False(string.IsNullOrWhiteSpace(a.About), $"archetype {a.Name}: write its strategy in a couple of sentences (about)");
                Assert.True(a.Name == "idle" || a.HoldMs >= 100, $"archetype {a.Name}: hold_ms < 100 is a new input every few ticks, no person plays like that");
            }
        }

        [Fact]
        public void InputLogReplaysTheSameSession()
        {
            var data = Repo.Data();
            var sim = Game.Create(9, data);
            var bots = Repo.Bots(Game, data, sim.Players, 9);
            var log = new InputLog(9, sim.Players);
            while (!sim.IsOver)
            {
                var inputs = Forks.Inputs(Game, sim, bots, -1, PlayerInput.None);
                log.Record(inputs);
                sim.Step(inputs);
            }
            var replayed = InputLog.Parse(log.Serialize());
            var again = Game.Create(replayed.Seed, data);
            foreach (var t in replayed.Ticks) again.Step(t);
            Assert.Equal(sim.StateHash(), again.StateHash());
        }

        [Fact]
        public void PaceTagsAreThere()
        {
            var data = Repo.Data();
            var sim = Repo.PlayOut(Game, data, 4);
            var all = sim.Events.All;
            Assert.Contains(all, e => e.DecisionOptions > 1);
            Assert.Contains(all, e => e.Threat != null);
            Assert.Contains(all, e => e.Reward != null);
            Assert.Contains(all, e => e.Novel != null);
            Assert.All(all.Where(e => e.Threat != null), e => Assert.InRange(e.Threat!.Value.ToDouble(), 0, 1));
        }

        [Fact]
        public void ModuleGraphMatchesModulesMd()
        {
            var map = ParseModulesMd(File.ReadAllText(Path.Combine(Repo.Root, "MODULES.md")));
            var sim = Game.Create(1, Repo.Data());
            var names = sim.Modules.Select(m => m.Name).ToList();
            Assert.Equal(map.Keys.OrderBy(k => k, StringComparer.Ordinal), names.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var m in sim.Modules)
                Assert.True(map[m.Name].SetEquals(m.DependsOn),
                    $"{m.Name}: code depends on [{string.Join(", ", m.DependsOn)}], MODULES.md says [{string.Join(", ", map[m.Name])}]");
        }

        [Fact]
        public void TickOrderFollowsModulesMd()
        {
            var md = File.ReadAllText(Path.Combine(Repo.Root, "MODULES.md"));
            var order = ParseModulesMd(md).Keys.ToList();
            var sim = Game.Create(1, Repo.Data());
            Assert.Equal(order, sim.Modules.Select(m => m.Name).ToList());
        }

        [Fact]
        public void DataLoadsAndVariantsApply()
        {
            var data = Repo.Data();
            foreach (var set in GameData.VariantSets(Repo.DataDir))
                foreach (var name in GameData.VariantNames(Repo.DataDir, set))
                {
                    var v = data.WithVariant(Repo.DataDir, set, name);
                    var sim = Game.Create(1, v);
                    Assert.False(sim.IsOver);
                }
        }

        /// <summary>Rows of the table «| Модуль | Владеет | Зависит от |», in order.</summary>
        public static Dictionary<string, HashSet<string>> ParseModulesMd(string md)
        {
            var map = new Dictionary<string, HashSet<string>>();
            var order = new List<string>();
            foreach (var line in md.Split('\n'))
            {
                var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (!line.TrimStart().StartsWith("|") || cells.Length < 3) continue;
                if (cells[0] == "Модуль" || Regex.IsMatch(cells[0], "^-+$")) continue;
                var deps = cells[2] is "—" or "-" or "" ? new HashSet<string>()
                    : cells[2].Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToHashSet();
                map[cells[0]] = deps;
                order.Add(cells[0]);
            }
            return order.ToDictionary(k => k, k => map[k]);
        }
    }

    public class MathTests
    {
        [Fact]
        public void FixedParsesExactly()
        {
            Assert.Equal(Fixed.FromInt(3), Fixed.Parse("3"));
            Assert.Equal(Fixed.Half, Fixed.Parse("0.5"));
            Assert.Equal(-Fixed.Half, Fixed.Parse("-0.5"));
            Assert.Equal(Fixed.FromInt(7) / 4, Fixed.Parse("1.75"));
        }

        [Fact]
        public void RandomIsPinned()
        {
            var r = new DetRandom(42);
            var first = Enumerable.Range(0, 4).Select(_ => r.NextInt(1000)).ToArray();
            var again = new DetRandom(42);
            Assert.Equal(first, Enumerable.Range(0, 4).Select(_ => again.NextInt(1000)).ToArray());
        }

        [Fact]
        public void BotsReactLateAndHoldTheirDecision()
        {
            var a = new Archetype { Name = "t", About = "x", ReactionMs = 100, HoldMs = 200 };
            var driver = new BotDriver(0, a, new FlipStrategy(), 1);
            var outputs = Enumerable.Range(0, 30).Select(t => driver.Next(new BotView { Tick = t }).X).ToList();
            Assert.Equal(0, outputs[0]); // 100 ms = 3 ticks: nothing seen yet
            int changes = outputs.Zip(outputs.Skip(1), (x, y) => x != y ? 1 : 0).Sum();
            Assert.InRange(changes, 1, 30 / 6 + 1); // 200 ms = 6 ticks of inertia
        }

        private sealed class FlipStrategy : IBotStrategy
        {
            private int _n;
            public PlayerInput Decide(BotView view, ref DetRandom rng) => new PlayerInput(_n++ % 2 == 0 ? 1 : -1, 0);
            public IBotStrategy Clone() => new FlipStrategy { _n = _n };
        }
    }
}
