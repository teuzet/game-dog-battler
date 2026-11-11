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
    /// <summary>The duel: health, damage by the blow's strength, rounds, the match to three points.</summary>
    public class DuelTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);
        private static Vec2 P(string x, string y) => new Vec2(F(x), F(y));
        private static GameData DuelData(string owner = "jock") => Repo.Data().WithArena("duel").WithHumanOwner(owner);

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

        private sealed class Scene
        {
            public Simulation Sim = null!;
            public Shove Shove = null!;
            public BodiesModule Bodies = null!;
            public DogsModule Dogs = null!;
            public OwnersModule Owners = null!;
            public HitsModule Hits = null!;
            public MatchModule Match = null!;
            public ArenaModule Arena = null!;
            public int Dog(int p) => Dogs.BodyOf(p);
            public int Owner(int p) => Owners.BodyOf(p);
            public void Put(int body, string x, string y) => Bodies.Teleport(body, P(x, y));
            public List<GameEvent> Events(string kind) => Sim.Events.All.Where(e => e.Kind == kind).ToList();

            public Fixed Hp(int p)
            {
                var hud = new Dictionary<string, Fixed>();
                Sim.Get<RulesModule>("Rules").Active.Sense(Sim, hud);
                return hud["hp_" + p];
            }

            public int Round()
            {
                var hud = new Dictionary<string, Fixed>();
                Sim.Get<RulesModule>("Rules").Active.Sense(Sim, hud);
                return hud["round"].FloorToInt();
            }

            public void Tick(int n = 1)
            {
                for (int i = 0; i < n; i++) Sim.Step(TickInputs.Empty(Sim.Players));
            }

            /// <summary>Player 0's owner runs along +X at <paramref name="speed"/> into the dog of player 1 (his own dog beside him),
            /// then coasts. Returns the blow, or null if none landed in a second.</summary>
            public Hit? Strike(string speed)
            {
                Put(Owner(0), "-4", "0"); Put(Dog(0), "-4", "-1.5");
                Put(Dog(1), "0", "0"); Put(Owner(1), "1", "1.8");
                Shove.Velocities.Clear();
                var v = new Vec2(F(speed), Fixed.Zero);
                Shove.Velocities[Owner(0)] = v;
                Shove.Velocities[Dog(0)] = v;
                for (int i = 0; i < 30; i++)
                {
                    Tick();
                    if (Hits.ThisTick.Count > 0)
                    {
                        Shove.Velocities.Clear();
                        return Hits.ThisTick[0];
                    }
                }
                Shove.Velocities.Clear();
                return null;
            }

            /// <summary>Waits until the next round has begun (or the match is over).</summary>
            public void WaitForNextRound()
            {
                int round = Round();
                while (!Sim.IsOver && Round() == round) Tick();
            }
        }

        private static Scene Duel(string owner = "jock")
        {
            var data = DuelData(owner);
            int players = data.Int("game.players");
            var shove = new Shove();
            var modules = new List<IModule>
            {
                new ArenaModule(data), new ControlModule(data, players), new OwnersModule(data, players), new DogsModule(data, players),
                new PropsModule(), new LeashesModule(data, players), shove, new BodiesModule(data), new HitsModule(data, players),
                new RulesModule(data, players), new MatchModule(data, players),
            };
            var sim = new Simulation(1, players, data, modules);
            return new Scene
            {
                Sim = sim, Shove = shove,
                Bodies = sim.Get<BodiesModule>("Bodies"), Dogs = sim.Get<DogsModule>("Dogs"), Owners = sim.Get<OwnersModule>("Owners"),
                Hits = sim.Get<HitsModule>("Hits"), Match = sim.Get<MatchModule>("Match"), Arena = sim.Get<ArenaModule>("Arena"),
            };
        }

        private static Fixed Damage(GameData d, Hit hit)
        {
            var dmg = Fixed.Max(d.Num("rules.duel.damage_min"), d.Num("rules.duel.damage_per_strength") * hit.Strength);
            return hit.Strong ? dmg * d.Num("rules.duel.strong_multiplier") : dmg;
        }

        [Fact]
        public void TheArenaFitsTheDuelAndItsCeiling()
        {
            var d = DuelData();
            Assert.Equal(2, d.Int("game.players"));
            Assert.Equal(new[] { "smart" }, Archetype.Opponents(d));
            int rounds = d.Int("rules.duel.rounds_to_win") * 2 - 1;
            double fullMatch = rounds * d.Num("rules.duel.round_s").ToDouble() + (rounds - 1) * d.Num("rules.duel.between_rounds_s").ToDouble();
            Assert.True(fullMatch <= d.Int("match.seconds"));
            Assert.True(d.Int("match.seconds") <= d.Int("game.session.max_seconds"));
            var arena = GameSetup.Game.Create(1, d).Get<ArenaModule>("Arena");
            Assert.InRange(arena.CircleObstacles.Count + arena.RectObstacles.Count, 3, 5);
            Assert.Empty(arena.Props);
        }

        [Fact]
        public void TheStraightLineBetweenTheDogSpawnsIsFree()
        {
            var arena = GameSetup.Game.Create(1, DuelData()).Get<ArenaModule>("Arena");
            var a = arena.Spawns[0].Dog;
            var b = arena.Spawns[1].Dog;
            for (int i = 0; i <= 40; i++)
            {
                var p = a + (b - a) * Fixed.Ratio(i, 40);
                foreach (var c in arena.CircleObstacles) Assert.True(p.DistanceTo(c.Pos) > c.Radius + F("0.5"));
                foreach (var r in arena.RectObstacles)
                    Assert.False((p.X - r.Center.X).Abs() < r.HalfWidth + F("0.5") && (p.Y - r.Center.Y).Abs() < r.HalfHeight + F("0.5"));
            }
        }

        [Fact]
        public void MatchGoalIsInTheStateHash()
        {
            var duel = GameSetup.Game.Create(1, DuelData());
            var park = GameSetup.Game.Create(1, Repo.Data());
            Assert.NotEqual(duel.StateHash(), park.StateHash());
            Assert.NotNull(park.Get<MatchModule>("Match")); // the park sets no target: it plays by the timer as before
        }

        [Theory]
        [InlineData("teen", "7")]
        [InlineData("jock", "7")]
        [InlineData("jock", "10")]
        public void ABlowTakesHealthByTheFormula(string owner, string speed)
        {
            var s = Duel(owner);
            s.Tick();
            Assert.Equal(Fixed.One, s.Hp(1));
            var hit = s.Strike(speed);
            Assert.NotNull(hit);
            var data = DuelData(owner);
            var expected = Fixed.Max(Fixed.Zero, data.Num("rules.duel.hp") - Damage(data, hit!.Value)); // health stops at zero
            Assert.Equal(expected / data.Num("rules.duel.hp"), s.Hp(1));
            Assert.Equal(Fixed.One, s.Hp(0)); // the attacker's own health is untouched
        }

        [Fact]
        public void AWeakBlowTakesAtLeastTheMinimum()
        {
            var data = DuelData("teen");
            var s = Duel("teen");
            var hit = s.Strike("5.6");
            Assert.NotNull(hit);
            Assert.True(data.Num("rules.duel.damage_per_strength") * hit!.Value.Strength < data.Num("rules.duel.damage_min"));
            double taken = (data.Num("rules.duel.hp") - s.Hp(1) * data.Num("rules.duel.hp")).ToDouble();
            Assert.Equal(data.Num("rules.duel.damage_min").ToDouble(), taken, 2);
        }

        [Fact]
        public void BlowsDownToZeroKnockOutAwardTheRoundAndAfterThePauseBothPairsStandOnTheirSpawns()
        {
            var data = DuelData();
            var s = Duel();
            int hits = 0;
            while (s.Events("knocked_out").Count == 0)
            {
                Assert.NotNull(s.Strike("10"));
                hits++;
                Assert.True(hits < 20);
            }
            Assert.Equal(Fixed.Zero, s.Hp(1));
            var ko = s.Events("knocked_out");
            Assert.Equal(2, ko.Count);
            Assert.Equal(0, ko[0].Actor);
            Assert.Equal(Fixed.One, ko[0].Reward);
            Assert.Equal("first_blood", ko[0].Novel);
            Assert.Equal(1, ko[1].Actor);
            Assert.Equal(Fixed.One, ko[1].Threat);
            Assert.Equal(new[] { 1, 0 }, s.Match.Scores.ToArray());
            Assert.Equal(1, s.Round());
            // the pause: nothing a player does moves the dogs, they wait on the spawns
            int pause = (data.Num("rules.duel.between_rounds_s") * Simulation.TicksPerSecond).FloorToInt();
            for (int i = 0; i < pause - 1; i++)
            {
                s.Sim.Step(Pointing(s.Sim, 900, 0));
                Assert.Equal(s.Arena.Spawns[0].Dog, s.Bodies.Position(s.Dog(0)));
                Assert.Equal(1, s.Round());
            }
            s.Sim.Step(Pointing(s.Sim, 900, 0));
            Assert.Equal(2, s.Round());
            for (int p = 0; p < 2; p++)
            {
                Assert.Equal(s.Arena.Spawns[p].Dog, s.Bodies.Position(s.Dog(p)));
                Assert.Equal(s.Arena.Spawns[p].Owner, s.Bodies.Position(s.Owner(p)));
                Assert.Equal(Fixed.One, s.Hp(p));
            }
            Assert.Equal(2, s.Events("arena_event").Count(e => e.Novel == "round_start"));
        }

        private static TickInputs Pointing(Simulation sim, int x, int y)
        {
            var t = TickInputs.Empty(sim.Players);
            t[0] = new PlayerInput(x, y);
            t[1] = new PlayerInput(-x, y);
            return t;
        }

        [Fact]
        public void WhenTheRoundTimeRunsOutTheHealthierDogGetsThePoint()
        {
            var s = Duel("teen");
            Assert.NotNull(s.Strike("7"));
            Assert.True(s.Hp(1) < Fixed.One);
            s.WaitForNextRound();
            Assert.Equal(new[] { 1, 0 }, s.Match.Scores.ToArray());
            Assert.Empty(s.Events("knocked_out"));
            Assert.Equal(2, s.Round());
        }

        [Fact]
        public void WhenTheRoundTimeRunsOutWithEqualHealthNobodyGetsAPoint()
        {
            var s = Duel();
            int roundTicks = (DuelData().Num("rules.duel.round_s") * Simulation.TicksPerSecond).FloorToInt();
            s.Tick(roundTicks - 1);
            Assert.Equal(1, s.Round());
            s.WaitForNextRound();
            Assert.Equal(new[] { 0, 0 }, s.Match.Scores.ToArray());
            Assert.Equal(2, s.Round());
            Assert.False(s.Sim.IsOver);
        }

        [Fact]
        public void ThreePointsEndTheMatchAtOnceBeforeTheTimer()
        {
            var s = Duel();
            for (int round = 0; round < 3; round++)
            {
                Assert.False(s.Sim.IsOver);
                int kos = s.Events("knocked_out").Count;
                while (s.Events("knocked_out").Count == kos) Assert.NotNull(s.Strike("10"));
                s.WaitForNextRound();
            }
            Assert.True(s.Sim.IsOver);
            Assert.Equal(new[] { 3, 0 }, s.Match.Scores.ToArray());
            Assert.Equal(Outcome.Win, s.Sim.Result!.Outcomes[0]);
            Assert.Equal(Outcome.Loss, s.Sim.Result!.Outcomes[1]);
            Assert.True(s.Sim.Seconds < DuelData().Int("match.seconds"));
            Assert.Contains("по очкам", s.Sim.Result!.Why);
        }

        [Fact]
        public void SensorsShowHealthRoundAndClock()
        {
            var sim = GameSetup.Game.Create(1, DuelData());
            sim.Step(TickInputs.Empty(2));
            var v = GameSetup.Game.Sense(sim, 0);
            Assert.Equal(Fixed.One, v.Hud["hp_0"]);
            Assert.Equal(Fixed.One, v.Hud["hp_1"]);
            Assert.Equal(Fixed.One, v.Hud["round"]);
            Assert.True(v.Hud["round_time_left"] > Fixed.Zero && v.Hud["round_time_left"] <= DuelData().Num("rules.duel.round_s"));
            Assert.Equal(Fixed.Zero, v.Hud["score_0"]);
            Assert.Equal(Fixed.Zero, v.Hud["score_1"]);
        }

        [Fact]
        public void MetricsCountBlowsDamageAndPasses()
        {
            var s = Duel();
            var hit = s.Strike("10");
            var data = DuelData();
            var game = GameSetup.Game;
            var m0 = ((DogBattlerGame)game).Metrics(s.Sim, 0);
            var m1 = ((DogBattlerGame)game).Metrics(s.Sim, 1);
            Assert.Equal(1, m0["hits"]);
            Assert.Equal(1, m1["hits_taken"]);
            Assert.Equal(Fixed.Min(Damage(data, hit!.Value), data.Num("rules.duel.hp")).ToDouble(), m0["damage_dealt"]); // no more than the health there was
            Assert.Equal(m0["damage_dealt"], m1["damage_taken"]);
            Assert.Equal(1, m0["rounds_won"]); // a strong blow of a jock takes all the health: the round is his
            Assert.Equal(0, m1["hits"]);
            Assert.Equal(0, m0["near_misses_dodged"]);
            foreach (var key in new[] { "rounds_won", "damage_dealt", "damage_taken", "hits", "hits_taken", "near_misses_dodged" })
                Assert.True(m0.ContainsKey(key), key);
        }

        private static Simulation PlayBots(GameData data, int seed, string first, string second)
        {
            var sim = GameSetup.Game.Create(seed, data);
            var bots = new List<BotDriver>();
            foreach (var name in new[] { first, second })
            {
                var a = Archetype.Find(data, name);
                bots.Add(new BotDriver(bots.Count, a, GameSetup.Game.Strategy(a, data), (ulong)seed));
            }
            while (!sim.IsOver) sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
            return sim;
        }

        [Fact]
        public void SmartBeatsIdleOnTheDuelOnEverySeed()
        {
            foreach (var owner in new[] { "teen", "jock" })
                for (int seed = 1; seed <= 4; seed++)
                {
                    var sim = PlayBots(DuelData(owner), seed, "smart", "idle");
                    Assert.True(sim.Result!.Outcomes[0] == Outcome.Win, $"{owner}, seed {seed}: {sim.Result!.Why}");
                    Assert.True(sim.Events.All.Any(e => e.Kind == "knocked_out"), $"{owner}, seed {seed}: no knockout, scores {string.Join(":", sim.Get<MatchModule>("Match").Scores)}, hits {GameSetup.Game.Metrics(sim, 0)["hits"]}, dealt {GameSetup.Game.Metrics(sim, 0)["damage_dealt"]}, {sim.Seconds} s");
                }
        }

        [Fact]
        public void TheDuelIsDeterministicForksAndEndsByItsTimersWithEmptyInput()
        {
            var data = DuelData();
            var a = PlayBots(data, 5, "smart", "smart");
            var b = PlayBots(data, 5, "smart", "smart");
            Assert.Equal(a.StateHash(), b.StateHash());

            var sim = GameSetup.Game.Create(7, data);
            var bots = Repo.Bots(GameSetup.Game, data, sim.Players, 7);
            Simulation fork = null!;
            var twin = GameSetup.Game.Create(7, data);
            var twinBots = Repo.Bots(GameSetup.Game, data, twin.Players, 7);
            for (int i = 0; i < 600; i++)
            {
                if (i == 300) fork = sim.Fork();
                sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
                twin.Step(Forks.Inputs(GameSetup.Game, twin, twinBots, -1, PlayerInput.None));
            }
            Assert.Equal(sim.StateHash(), twin.StateHash());
            var forkBots = Repo.Bots(GameSetup.Game, data, fork.Players, 7);
            // the fork is independent: stepping it does not change the original
            var before = sim.StateHash();
            fork.Step(TickInputs.Empty(2));
            Assert.Equal(before, sim.StateHash());

            var empty = GameSetup.Game.Create(1, data);
            while (!empty.IsOver) empty.Step(TickInputs.Empty(empty.Players));
            Assert.Equal(data.Int("match.seconds") * Simulation.TicksPerSecond, empty.Tick);
            Assert.True(empty.Tick <= data.Int("game.session.max_seconds") * Simulation.TicksPerSecond);
            Assert.NotNull(forkBots);
        }

        [Fact]
        public void TheInputLogNamesTheArenaAndReplaysTheDuelExactly()
        {
            var data = DuelData();
            var sim = GameSetup.Game.Create(9, data);
            var bots = Repo.Bots(GameSetup.Game, data, sim.Players, 9);
            var log = new InputLog(9, sim.Players, data.Variant, "jock", "duel");
            while (!sim.IsOver)
            {
                var inputs = Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None);
                log.Record(inputs);
                sim.Step(inputs);
            }
            var replayed = InputLog.Parse(log.Serialize());
            Assert.Equal("duel", replayed.Arena);
            var again = GameSetup.Game.Create(replayed.Seed, Repo.Data().WithArena(replayed.Arena).WithHumanOwner(replayed.Owner));
            foreach (var t in replayed.Ticks) again.Step(t);
            Assert.Equal(sim.StateHash(), again.StateHash());
            Assert.Equal("", InputLog.Parse("seed 1\nplayers 2\nvariant \nticks 0\n").Arena); // old logs: the data's own arena
        }

        [Fact]
        public void TheDuelCarriesPaceTags()
        {
            var sim = PlayBots(DuelData(), 3, "smart", "smart");
            Assert.Contains(sim.Events.All, e => e.Threat != null);
            Assert.Contains(sim.Events.All, e => e.Reward != null);
            Assert.Contains(sim.Events.All, e => e.Novel == "round_start");
        }

        [Fact]
        public void WithArenaLaysTheArenasDataOverTheGameAndKeepsTheVariant()
        {
            var park = Repo.Data();
            var duel = park.WithArena("duel");
            Assert.Equal("duel", duel.Str("game.arena"));
            Assert.Equal(2, duel.Int("game.players"));
            Assert.Equal(4, park.Int("game.players"));
            Assert.Equal(4, park.WithArena("sandbox").Int("game.players")); // an arena without a data block changes only game.arena
            Assert.Equal("sandbox", park.WithArena("sandbox").Str("game.arena"));
        }
    }
}
