using System.Collections.Generic;
using System.Linq;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Control;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Match;
using Game.Core.Modules.Owners;
using Game.Core.Modules.Props;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Modules
{
    /// <summary>The swing sandbox: mannequins that return after a blow, the metrics, the opponents helper.</summary>
    public class SandboxTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);

        private static GameData SandboxData(string owner = "teen")
        {
            var data = Repo.Data();
            return new GameData(data.Root.MergedWith(Json.Parse("{\"game\":{\"arena\":\"sandbox\",\"human_owner\":\"" + owner + "\"}}")), data.Variant);
        }

        /// <summary>Sets velocities of bodies every tick between Dogs and Bodies: a scripted swing at an exact speed.</summary>
        private sealed class Shove : IModule
        {
            public string Name => "Shove";
            public IReadOnlyList<string> DependsOn { get; } = new[] { BodiesModule.ModuleName };
            public readonly Dictionary<int, Vec2> Velocities = new Dictionary<int, Vec2>();
            public void Init(Simulation sim) { }
            public void Step(Simulation sim, TickInputs inputs)
            {
                var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
                foreach (var kv in Velocities.OrderBy(k => k.Key)) bodies.SetVelocity(kv.Key, kv.Value);
            }
            public void Hash(ref StateHasher h) { }
            public IModule Clone() => this;
        }

        [Fact]
        public void OpponentsAreNamedByTheRuleSetOtherwiseBySession()
        {
            var park = Repo.Data();
            Assert.Equal(new[] { "smart", "random", "idle" }, Archetype.Opponents(park));
            Assert.Equal(new[] { "idle" }, Archetype.Opponents(SandboxData()));
        }

        [Fact]
        public void AStruckMannequinReturnsExactlyTwoSecondsAfterTheBlowAtRest()
        {
            var data = SandboxData("jock");
            int players = data.Int("game.players");
            var shove = new Shove();
            var sim = new Simulation(1, players, data, new List<IModule>
            {
                new ArenaModule(data), new ControlModule(data, players), new OwnersModule(data, players), new DogsModule(data, players),
                new PropsModule(), new LeashesModule(data, players), shove, new BodiesModule(data), new HitsModule(data, players),
                new RulesModule(data, players), new MatchModule(data, players),
            });
            var bodies = sim.Get<BodiesModule>("Bodies");
            var dogs = sim.Get<DogsModule>("Dogs");
            var owners = sim.Get<OwnersModule>("Owners");
            var arena = sim.Get<ArenaModule>("Arena");
            var rules = sim.Get<RulesModule>("Rules");
            // player 0 swings at the dog of mannequin 1 at (0, 0)
            bodies.Teleport(owners.BodyOf(0), new Vec2(F("-4"), F("0")));
            bodies.Teleport(dogs.BodyOf(0), new Vec2(F("-4"), F("-2.5")));
            bodies.Teleport(dogs.BodyOf(1), Vec2.Zero);
            bodies.Teleport(owners.BodyOf(1), new Vec2(F("0"), F("3")));
            var v = new Vec2(F("8"), Fixed.Zero);
            shove.Velocities[owners.BodyOf(0)] = v;
            shove.Velocities[dogs.BodyOf(0)] = v;

            int blowTick = -1, returnTick = -1;
            for (int i = 0; i < 150 && returnTick < 0; i++)
            {
                sim.Step(TickInputs.Empty(players));
                if (blowTick < 0 && sim.Events.All.Any(e => e.Kind == "hit" && e.Actor == 0)) { blowTick = sim.Tick; shove.Velocities.Clear(); }
                if (returnTick < 0 && sim.Events.All.Any(e => e.Kind == "arena_event"))
                {
                    returnTick = sim.Tick;
                    Assert.Equal(arena.Spawns[1].Dog, dogs.Position(sim, 1));
                    Assert.Equal(arena.Spawns[1].Owner, owners.Position(sim, 1));
                    Assert.Equal(Vec2.Zero, bodies.Velocity(dogs.BodyOf(1)));
                    Assert.Equal(Vec2.Zero, bodies.Velocity(owners.BodyOf(1)));
                }
            }
            Assert.True(blowTick > 0, "no blow");
            Assert.Equal(blowTick + 2 * Simulation.TicksPerSecond, returnTick);
            var returned = sim.Events.All.Single(e => e.Kind == "arena_event");
            Assert.Equal(returnTick, returned.Tick);
            Assert.Equal("mannequin_return", returned.Novel);

            var m = rules.Active.Metrics(sim, 0);
            Assert.Equal(1, m["hits"]);
            Assert.True(m["hits_per_min"] > 0);
            Assert.Equal(0, m["strong_hits"]); // 8 m/s is a blow, not a strong one (9)
            Assert.Equal(0, rules.Active.Metrics(sim, 1)["hits"]);
        }

        private static TickInputs Scripted(Simulation sim)
        {
            var t = TickInputs.Empty(sim.Players);
            int phase = sim.Tick / 45 % 4;
            t[0] = new PlayerInput(phase < 2 ? -900 : 900, phase % 2 == 0 ? -300 : 300);
            return t;
        }

        [Fact]
        public void SandboxSessionWithIdleMannequinsIsDeterministicAndForks()
        {
            var data = SandboxData();
            var a = GameSetup.Game.Create(3, data);
            var b = GameSetup.Game.Create(3, data);
            Simulation fork = null!;
            for (int i = 0; i < 400; i++)
            {
                if (i == 200) fork = a.Fork();
                a.Step(Scripted(a));
                b.Step(Scripted(b));
            }
            Assert.Equal(a.StateHash(), b.StateHash());
            for (int i = 200; i < 400; i++) fork.Step(Scripted(fork));
            Assert.Equal(a.StateHash(), fork.StateHash());
        }

        [Fact]
        public void SandboxMatchEndsByTheMatchTimer()
        {
            var data = SandboxData();
            var sim = GameSetup.Game.Create(2, data);
            var bots = Repo.Bots(GameSetup.Game, data, sim.Players, 2, "idle");
            while (!sim.IsOver) sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
            Assert.Equal(data.Int("match.seconds") * Simulation.TicksPerSecond, sim.Tick);
            Assert.Contains("Sandbox", sim.Get<RulesModule>("Rules").Active.Name);
            Assert.All(sim.Get<MatchModule>("Match").Scores, s => Assert.Equal(0, s)); // no points
        }
    }
}
