using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Control;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Match;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Skeleton
{
    public class DataSkeletonTests
    {
        [Theory]
        [InlineData("park", "KingOfLawn")]
        [InlineData("sandbox", "Sandbox")]
        public void ArenasAre24By16AndNameTheirRules(string name, string rules)
        {
            var data = Repo.Data();
            var j = data.Get("arenas." + name);
            Assert.Equal(24, j["width"]!.AsInt());
            Assert.Equal(16, j["height"]!.AsInt());
            Assert.Equal(rules, j["rules"]!.AsString());
        }

        [Fact]
        public void CommonNumbersMatchTheDesign()
        {
            var d = Repo.Data();
            Assert.Equal(Fixed.FromInt(7), d.Num("dogs.collie.speed"));
            Assert.Equal(Fixed.FromInt(3), d.Num("dogs.collie.leash_m"));
            Assert.Equal(Fixed.FromInt(60), d.Num("owners.teen.mass"));
            Assert.Equal(Fixed.FromInt(110), d.Num("owners.jock.mass"));
            Assert.Equal(Fixed.FromInt(5), d.Num("hits.threshold_mps"));
            Assert.Equal(Fixed.FromInt(9), d.Num("hits.strong_threshold_mps"));
            Assert.Equal(Fixed.FromInt(120), d.Num("match.seconds"));
            Assert.Equal(Fixed.FromInt(10), d.Num("control.max_speed_mps"));
        }

        [Theory]
        [InlineData("park")]
        [InlineData("sandbox")]
        [InlineData("duel")]
        public void EveryArenaBuildsASession(string arena)
        {
            var data = Repo.Data();
            data = data.WithArena(arena);
            var sim = GameSetup.Game.Create(1, data);
            Assert.Equal(arena, sim.Get<ArenaModule>("Arena").ArenaName);
            Assert.Equal(arena == "park" ? "KingOfLawn" : arena == "duel" ? "Duel" : "Sandbox", sim.Get<RulesModule>("Rules").Active.Name);
            if (arena == "duel") Assert.Equal(2, sim.Players);
            for (int i = 0; i < 60; i++) sim.Step(TickInputs.Empty(sim.Players));
        }

        [Fact]
        public void ExampleModuleIsGone()
        {
            Assert.False(Directory.Exists(Path.Combine(Repo.Root, "Game.Core", "Modules", "Example")));
            Assert.False(Repo.Data().Has("game.race"));
        }
    }

    public class BodiesStubTests
    {
        private static BodiesModule Bodies(out Simulation sim)
        {
            sim = GameSetup.Game.Create(1, Repo.Data());
            return sim.Get<BodiesModule>("Bodies");
        }

        private static Vec2 P(int x, int y) => new Vec2(Fixed.FromInt(x), Fixed.FromInt(y));

        [Fact]
        public void KinematicMovesFixedStaysAndUndrivenCircleSlowsDown()
        {
            var bodies = Bodies(out var sim);
            int car = bodies.AddKinematic(P(0, 0), Fixed.One, P(3, 0));
            int post = bodies.AddFixed(P(5, 5), Fixed.Half);
            int ball = bodies.AddCircle(P(0, 0), Fixed.One, Fixed.Half, Fixed.FromInt(2));
            bodies.Drive(ball, P(4, 0), Fixed.FromInt(1000));
            sim.Step(TickInputs.Empty(sim.Players)); // driven this tick: reaches 4 m/s at once
            Assert.Equal(P(4, 0), bodies.Velocity(ball));
            for (int i = 0; i < 30; i++) sim.Step(TickInputs.Empty(sim.Players)); // 1 s of friction 2 m/s²
            Assert.Equal(2.0, bodies.Velocity(ball).X.ToDouble(), 2);
            Assert.Equal(3.0 * 31 / 30, bodies.Position(car).X.ToDouble(), 2);
            Assert.Equal(P(5, 5), bodies.Position(post));
            Assert.Empty(bodies.Contacts);
        }

        [Fact]
        public void DriveHoldsForOneTickOnly()
        {
            var bodies = Bodies(out var sim);
            int ball = bodies.AddCircle(P(0, 0), Fixed.One, Fixed.Half, Fixed.FromInt(30));
            bodies.Drive(ball, P(3, 0), Fixed.FromInt(1000));
            sim.Step(TickInputs.Empty(sim.Players));
            sim.Step(TickInputs.Empty(sim.Players)); // nobody drove it: friction 30 m/s² stops it in 0.1 s
            sim.Step(TickInputs.Empty(sim.Players));
            sim.Step(TickInputs.Empty(sim.Players));
            Assert.True(bodies.Velocity(ball).Length < Fixed.Parse("0.01"), bodies.Velocity(ball).ToString());
        }

        [Fact]
        public void RopeIsRecordedBetweenTwoBodies()
        {
            var bodies = Bodies(out _);
            int a = bodies.AddCircle(P(0, 0), Fixed.One, Fixed.Half, Fixed.One);
            int b = bodies.AddFixed(P(2, 0), Fixed.Half);
            int rope = bodies.AddRope(a, b, Fixed.FromInt(3), Fixed.FromInt(10));
            Assert.Equal(Fixed.FromInt(3), bodies.Ropes[rope].Length);
            Assert.Throws<KeyNotFoundException>(() => bodies.AddRope(a, 999, Fixed.One, Fixed.One));
        }

        [Fact]
        public void EveryPlayerHasADogAndOwnerBodyTiedByALeash()
        {
            var bodies = Bodies(out var sim);
            Assert.Equal(sim.Players * 2, bodies.Count);
            Assert.Equal(sim.Players, bodies.Ropes.Count);
            foreach (var r in bodies.Ropes) Assert.Equal(Fixed.FromInt(3), r.Length);
        }
    }

    public class ControlAndInputTests
    {
        [Fact]
        public void CursorModeRunsTheDogToThePointAndStopsThere()
        {
            var sim = GameSetup.Game.Create(1, Repo.Data());
            var dogs = sim.Get<DogsModule>("Dogs");
            var inputs = TickInputs.Empty(sim.Players);
            inputs[0] = new PlayerInput(0, 200); // (0 m, 2 m)
            for (int i = 0; i < 90; i++) sim.Step(inputs);
            var pos = dogs.Position(sim, 0);
            Assert.True(pos.DistanceTo(new Vec2(Fixed.Zero, Fixed.FromInt(2))) < Fixed.Parse("0.3"), pos.ToString());
            Assert.Equal(Fixed.Zero, dogs.Position(sim, 1).DistanceTo(sim.Get<ArenaModule>("Arena").Spawns[1].Dog));
        }

        [Fact]
        public void PushModeTurnsMouseShiftIntoVelocityWithACeiling()
        {
            var sim = GameSetup.Game.Create(1, Repo.Data());
            var dogs = sim.Get<DogsModule>("Dogs");
            var start = dogs.Position(sim, 0);
            var inputs = TickInputs.Empty(sim.Players);
            inputs[0] = new PlayerInput(1000, 0, PlayerInput.PushBit); // way over the ceiling
            for (int i = 0; i < 30; i++) sim.Step(inputs);
            var moved = (dogs.Position(sim, 0) - start).X.ToDouble();
            Assert.InRange(moved, 5.5, 7.1); // collie: 7 m/s, 0.2 s to get there
            Assert.InRange((dogs.Position(sim, 0) - start).Y.ToDouble(), -0.3, 0.3); // the leash may bend the run a little
        }

        [Fact]
        public void BothModesAndButtonsSurviveTheInputLog()
        {
            var log = new InputLog(5, 2);
            var t0 = new TickInputs(2);
            t0[0] = new PlayerInput(-1234, 567, PlayerInput.DogAbilityBit);
            t0[1] = new PlayerInput(32000, -32000, PlayerInput.PushBit | PlayerInput.OwnerAbilityBit);
            var t1 = new TickInputs(2);
            t1[0] = new PlayerInput(-1234, 567, PlayerInput.DogAbilityBit);
            t1[1] = new PlayerInput(-5, 0, PlayerInput.PushBit);
            log.Record(t0);
            log.Record(t1);
            var back = InputLog.Parse(log.Serialize());
            Assert.Equal(2, back.Ticks.Count);
            for (int t = 0; t < 2; t++)
                for (int p = 0; p < 2; p++)
                    Assert.Equal(log.Ticks[t][p], back.Ticks[t][p]);
            Assert.True(back.Ticks[0][0].DogAbility);
            Assert.False(back.Ticks[0][0].Push);
            Assert.True(back.Ticks[0][1].Push);
            Assert.True(back.Ticks[0][1].OwnerAbility);
            Assert.Equal(-1234, back.Ticks[1][0].AimX);
        }

        [Fact]
        public void LoggedMixedModeSessionReplaysToTheSameHash()
        {
            var data = Repo.Data();
            var sim = GameSetup.Game.Create(21, data);
            var log = new InputLog(21, sim.Players);
            var rng = new DetRandom(99);
            while (!sim.IsOver)
            {
                var inputs = new TickInputs(sim.Players);
                for (int p = 0; p < sim.Players; p++)
                {
                    bool push = (sim.Tick / 90 + p) % 2 == 0;
                    uint buttons = (push ? PlayerInput.PushBit : 0u) | (uint)rng.NextInt(4) << 1;
                    inputs[p] = push ? new PlayerInput(rng.Range(-40, 41), rng.Range(-40, 41), buttons)
                                     : new PlayerInput(rng.Range(-1100, 1101), rng.Range(-700, 701), buttons);
                }
                log.Record(inputs);
                sim.Step(inputs);
            }
            var replay = GameSetup.Game.Create(21, data);
            foreach (var t in InputLog.Parse(log.Serialize()).Ticks) replay.Step(t);
            Assert.Equal(sim.StateHash(), replay.StateHash());
            Assert.Equal(sim.Tick, replay.Tick);
        }
    }

    public class MatchAndHitsStubTests
    {
        [Fact]
        public void MatchAwardsScoresAndCountsDown()
        {
            var sim = GameSetup.Game.Create(1, Repo.Data());
            var match = sim.Get<MatchModule>("Match");
            Assert.Equal(Fixed.FromInt(120), match.TimeLeft);
            match.Award(2, 3, "test");
            match.Award(2, 2, "test");
            Assert.Equal(new[] { 0, 0, 5, 0 }, match.Scores);
            sim.Step(TickInputs.Empty(sim.Players));
            Assert.True(match.TimeLeft < Fixed.FromInt(120));
        }

        /// <summary>Every dog keeps to its place (cursor on itself), so nobody earns points in the circle.</summary>
        private static TickInputs Stay(Simulation sim)
        {
            var dogs = sim.Get<DogsModule>("Dogs");
            var t = TickInputs.Empty(sim.Players);
            for (int p = 0; p < sim.Players; p++)
            {
                var at = dogs.Position(sim, p);
                t[p] = new PlayerInput((at.X * 100).FloorToInt(), (at.Y * 100).FloorToInt());
            }
            return t;
        }

        [Fact]
        public void MatchEndsWithTheLeaderWinningAndTiesDrawn()
        {
            var data = Repo.Data();
            var sim = GameSetup.Game.Create(1, data);
            sim.Get<MatchModule>("Match").Award(1, 400, "test"); // far above what a circle that lands on a spawn can give
            while (!sim.IsOver) sim.Step(Stay(sim));
            Assert.Equal(new[] { Outcome.Loss, Outcome.Win, Outcome.Loss, Outcome.Loss }, sim.Result!.Outcomes);
            Assert.Equal(120 * Simulation.TicksPerSecond, sim.Tick);

            // the tie on the sandbox: no circle there, so none can land on one tied player's spawn and break it
            var sandbox = data.WithArena("sandbox");
            var tie = GameSetup.Game.Create(1, sandbox);
            tie.Get<MatchModule>("Match").Award(0, 400, "test");
            tie.Get<MatchModule>("Match").Award(3, 400, "test");
            while (!tie.IsOver) tie.Step(Stay(tie));
            Assert.Equal(new[] { Outcome.Draw, Outcome.Loss, Outcome.Loss, Outcome.Draw }, tie.Result!.Outcomes);
        }

        [Fact]
        public void HitsListIsEmptyEveryTickInTheStub()
        {
            var sim = GameSetup.Game.Create(1, Repo.Data());
            var hits = sim.Get<HitsModule>("Hits");
            sim.Step(TickInputs.Empty(sim.Players));
            Assert.Empty(hits.ThisTick);
            Assert.Equal(Fixed.FromInt(5), hits.Threshold);
            Assert.Equal(Fixed.FromInt(9), hits.StrongThreshold);
        }

        [Fact]
        public void EmptyInputSessionEndsWithinTheSessionCap()
        {
            var sim = GameSetup.Game.Create(5, Repo.Data());
            while (!sim.IsOver) sim.Step(TickInputs.Empty(sim.Players));
            Assert.True(sim.Tick <= sim.MaxTicks);
            Assert.Equal(Repo.Data().Int("game.session.max_seconds") * Simulation.TicksPerSecond, sim.MaxTicks);
        }

        [Fact]
        public void SmartBotHoldsTheCircleAndBeatsIdle()
        {
            var data = Repo.Data();
            var sim = GameSetup.Game.Create(8, data);
            var smart = Archetype.Find(data, "smart");
            var idle = Archetype.Find(data, "idle");
            var bots = new List<BotDriver>();
            for (int p = 0; p < sim.Players; p++)
            {
                var a = p == 0 ? smart : idle;
                bots.Add(new BotDriver(p, a, GameSetup.Game.Strategy(a, data), 8));
            }
            while (!sim.IsOver) sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
            Assert.True(sim.Get<MatchModule>("Match").Scores[0] > 0);
            Assert.Equal(Outcome.Win, sim.Result!.Outcomes[0]);
        }
    }
}
