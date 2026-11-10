using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Control;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Owners;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Modules
{
    public class ControlDogsOwnersLeashesTests
    {
        private static readonly DogBattlerGame Game = new DogBattlerGame();

        private static Simulation Session() => Game.Create(1, Repo.Data());

        /// <summary>The game data with the human's owner pick set, the way the shell or a variant sets it.</summary>
        private static GameData WithHumanOwner(string owner)
        {
            var data = Repo.Data();
            return new GameData(data.Root.MergedWith(Json.Parse("{\"game\":{\"human_owner\":\"" + owner + "\"}}")), data.Variant);
        }

        private static TickInputs Inputs(Simulation sim, PlayerInput p0)
        {
            var t = TickInputs.Empty(sim.Players);
            t[0] = p0;
            return t;
        }

        private static Vec2 Move(Simulation sim, PlayerInput input, int ticks, Fixed? gain = null)
        {
            var dogs = sim.Get<DogsModule>("Dogs");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var start = bodies.Position(dogs.BodyOf(0));
            for (int i = 0; i < ticks; i++) sim.Step(Inputs(sim, input));
            return bodies.Position(dogs.BodyOf(0)) - start;
        }

        [Fact]
        public void BothModesGiveTheSameIntentForASimplePath()
        {
            // Dog 0 stands still at the spawn; a 5 cm shift per tick in push mode is 1.5 m/s along +X;
            // the cursor 1.5/gain metres ahead asks for the same velocity.
            var data = Repo.Data();
            var pushSim = Session();
            var cursorSim = Session();
            var start = pushSim.Get<BodiesModule>("Bodies").Position(pushSim.Get<DogsModule>("Dogs").BodyOf(0));
            pushSim.Step(Inputs(pushSim, new PlayerInput(5, 0, PlayerInput.PushBit)));
            var push = pushSim.Get<ControlModule>("Control").Of(0).Velocity;
            var gain = data.Num("control.cursor_gain_per_s");
            int cursorX = (int)(start.X.ToDouble() * 100 + 1.5 / gain.ToDouble() * 100);
            cursorSim.Step(Inputs(cursorSim, new PlayerInput(cursorX, (int)(start.Y.ToDouble() * 100))));
            var cursor = cursorSim.Get<ControlModule>("Control").Of(0).Velocity;
            Assert.True(push.X > Fixed.Zero && cursor.X > Fixed.Zero);
            Assert.True((push - cursor).Length < Fixed.Ratio(1, 10), $"push {push.X},{push.Y} cursor {cursor.X},{cursor.Y}");
            Assert.True(push.Y.Abs() < Fixed.Ratio(1, 100) && cursor.Y.Abs() < Fixed.Ratio(1, 100));
        }

        [Fact]
        public void BothModesRunTheDogTheSameWayAndAreCappedBySpeed()
        {
            var pushSim = Session();
            var cursorSim = Session();
            var dogs = cursorSim.Get<DogsModule>("Dogs");
            var start = cursorSim.Get<BodiesModule>("Bodies").Position(dogs.BodyOf(0));
            // a 1-second walk to the right
            var pushed = Move(pushSim, new PlayerInput(10, 0, PlayerInput.PushBit), 30);
            var cursor = Move(cursorSim, new PlayerInput((int)(start.X.ToDouble() * 100) + 300, (int)(start.Y.ToDouble() * 100)), 30);
            Assert.True(pushed.X > Fixed.FromInt(1) && cursor.X > Fixed.FromInt(1));
            Assert.True(pushed.Y.Abs() < Fixed.Ratio(1, 2) && cursor.Y.Abs() < Fixed.Ratio(1, 2));
            // a huge shift never exceeds the breed speed
            var fast = Session();
            fast.Step(Inputs(fast, new PlayerInput(30000, 0, PlayerInput.PushBit)));
            Assert.True(fast.Get<ControlModule>("Control").Of(0).Velocity.Length <= Repo.Data().Num("control.max_speed_mps") + Fixed.Ratio(1, 100));
            for (int i = 0; i < 90; i++) fast.Step(Inputs(fast, new PlayerInput(30000, 0, PlayerInput.PushBit)));
            var bodies = fast.Get<BodiesModule>("Bodies");
            var speed = bodies.Velocity(fast.Get<DogsModule>("Dogs").BodyOf(0)).Length;
            Assert.True(speed <= Repo.Data().Num("dogs.collie.speed") + Fixed.Ratio(1, 2), $"speed {speed}");
        }

        [Fact]
        public void ModeBitSwitchesModeMidSession()
        {
            var sim = Session();
            var control = sim.Get<ControlModule>("Control");
            var dogs = sim.Get<DogsModule>("Dogs");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var start = bodies.Position(dogs.BodyOf(0));
            int sx = (int)(start.X.ToDouble() * 100), sy = (int)(start.Y.ToDouble() * 100);
            // the cursor far to the left of the dog: the dog heads left
            sim.Step(Inputs(sim, new PlayerInput(sx - 400, sy)));
            Assert.True(control.Of(0).Velocity.X < Fixed.Zero);
            // the mode bit: (0, 300) is now a shift of 300 cm along +Y, not a cursor point
            sim.Step(Inputs(sim, new PlayerInput(0, 300, PlayerInput.PushBit)));
            var v = control.Of(0).Velocity;
            Assert.True(v.X.Abs() < Fixed.Ratio(1, 100) && v.Y > Fixed.Zero, "push mode reads Aim as a shift");
            // and back to the cursor
            sim.Step(Inputs(sim, new PlayerInput(sx + 400, sy)));
            Assert.True(control.Of(0).Velocity.X > Fixed.Zero);
        }

        [Fact]
        public void ColliePassiveTurnsTheVelocityAtOnceAndKeepsSpeed()
        {
            var sim = Session();
            var dogs = sim.Get<DogsModule>("Dogs");
            var bodies = sim.Get<BodiesModule>("Bodies");
            for (int i = 0; i < 20; i++) sim.Step(Inputs(sim, new PlayerInput(10, 0, PlayerInput.PushBit))); // running +X
            var before = bodies.Velocity(dogs.BodyOf(0));
            Assert.True(before.X > Fixed.One);
            sim.Step(Inputs(sim, new PlayerInput(0, 10, PlayerInput.PushBit))); // now +Y
            var after = bodies.Velocity(dogs.BodyOf(0));
            Assert.True(after.Y > after.X.Abs(), $"turned: {after.X},{after.Y}");
            Assert.True(after.Length >= before.Length - Fixed.Ratio(1, 2), "speed is not lost in the turn");
        }

        [Fact]
        public void ColliePassiveTurnsAtOnceInCursorModeToo()
        {
            var sim = Session();
            var dogs = sim.Get<DogsModule>("Dogs");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var start = bodies.Position(dogs.BodyOf(0));
            int sy = (int)(start.Y.ToDouble() * 100);
            for (int i = 0; i < 20; i++) // cursor far ahead on +X: running
                sim.Step(Inputs(sim, new PlayerInput((int)(bodies.Position(dogs.BodyOf(0)).X.ToDouble() * 100) + 600, sy)));
            var before = bodies.Velocity(dogs.BodyOf(0));
            Assert.True(before.X > Fixed.One);
            // the cursor jumps behind the dog: the velocity turns at once, no skid
            sim.Step(Inputs(sim, new PlayerInput((int)(bodies.Position(dogs.BodyOf(0)).X.ToDouble() * 100) - 600, sy)));
            var after = bodies.Velocity(dogs.BodyOf(0));
            Assert.True(after.X < Fixed.Zero, $"turned: {after.X},{after.Y}");
        }

        [Fact]
        public void ColliePassiveDoesNotSteerSpeedAboveTheBreedSpeed()
        {
            var sim = Session();
            var dogs = sim.Get<DogsModule>("Dogs");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var breed = dogs.BreedOf(0);
            bodies.SetVelocity(dogs.BodyOf(0), new Vec2(Fixed.FromInt(10), Fixed.Zero)); // knocked at 10 m/s
            sim.Step(Inputs(sim, new PlayerInput(0, 10, PlayerInput.PushBit)));           // and aims elsewhere
            var v = bodies.Velocity(dogs.BodyOf(0));
            Assert.True(v.Length <= breed.Speed + Fixed.Ratio(1, 10), $"speed {v.Length}");
        }

        [Fact]
        public void ColliePassiveMatchesTheRoster()
        {
            var sim = Session();
            var b = sim.Get<DogsModule>("Dogs").BreedOf(0);
            Assert.Equal("collie", b.Name);
            Assert.Equal(Fixed.FromInt(7), b.Speed);
            Assert.Equal(Fixed.Ratio(1, 5), b.AccelTime);
            Assert.Equal(Fixed.FromInt(2), b.Mass);
            Assert.Equal(Fixed.FromInt(3), b.LeashLength);
        }

        [Fact]
        public void TeenAndJockMatchTheRosterAndPickIsHonoured()
        {
            var sim = Game.Create(1, WithHumanOwner("jock"));
            var owners = sim.Get<OwnersModule>("Owners");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var teenIdx = -1;
            for (int p = 1; p < sim.Players; p++) if (owners.TypeOf(p).Name == "teen") teenIdx = p;
            Assert.Equal("jock", owners.TypeOf(0).Name);
            Assert.True(teenIdx > 0);
            Assert.Equal(Fixed.FromInt(110), bodies.Mass(owners.BodyOf(0)));
            Assert.Equal(Fixed.FromInt(60), bodies.Mass(owners.BodyOf(teenIdx)));
            Assert.True(owners.TypeOf(0).Friction < owners.TypeOf(teenIdx).Friction, "jock: low friction");
            Assert.True(owners.TypeOf(0).Mass > owners.TypeOf(teenIdx).Mass);
        }

        [Fact]
        public void LoggedSessionWithAJockPickReplaysToTheSameHash()
        {
            var data = WithHumanOwner("jock");
            var game = new DogBattlerGame();
            var sim = game.Create(7, data);
            var log = new InputLog(7, sim.Players, data.Variant);
            for (int i = 0; i < 120; i++)
            {
                var t = Inputs(sim, new PlayerInput(i % 40 < 20 ? 8 : -8, 3, PlayerInput.PushBit));
                log.Record(t);
                sim.Step(t);
            }
            Assert.Equal("jock", sim.Get<OwnersModule>("Owners").TypeOf(0).Name);
            var replayed = InputLog.Parse(log.Serialize());
            var again = game.Create(replayed.Seed, data);
            foreach (var t in replayed.Ticks) again.Step(t);
            Assert.Equal(sim.StateHash(), again.StateHash());
            // and the default pick (teen) is another session
            var teen = game.Create(7, Repo.Data());
            foreach (var t in replayed.Ticks) teen.Step(t);
            Assert.NotEqual(sim.StateHash(), teen.StateHash());
        }

        [Fact]
        public void DefaultSetupGivesHumanTheDataPickAndBotsTheirPicksFromData()
        {
            var data = Repo.Data();
            var sim = Game.Create(1, data);
            var owners = sim.Get<OwnersModule>("Owners");
            Assert.Equal(data.Str("game.human_owner"), owners.TypeOf(0).Name);
            var picks = data.Get("bots.session.owner_picks").Items;
            for (int p = 1; p < sim.Players; p++)
                Assert.Equal(picks[(p - 1) % picks.Count].AsString(), owners.TypeOf(p).Name);
        }

        [Fact]
        public void UnknownOwnerPickIsAnError()
        {
            Assert.Throws<KeyNotFoundException>(() => Game.Create(1, WithHumanOwner("granny")));
        }

        [Fact]
        public void OwnersMoveOnlyByTheLeash()
        {
            var sim = Session();
            var owners = sim.Get<OwnersModule>("Owners");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var start = bodies.Position(owners.BodyOf(1));
            for (int i = 0; i < 60; i++) sim.Step(TickInputs.Empty(sim.Players)); // everybody idle
            Assert.Equal(start, bodies.Position(owners.BodyOf(1)));
        }

        [Fact]
        public void OneStraightRopePerDogRegisteredInBodiesAndTensionIsZeroToOne()
        {
            var sim = Session();
            var leashes = sim.Get<LeashesModule>("Leashes");
            var bodies = sim.Get<BodiesModule>("Bodies");
            var dogs = sim.Get<DogsModule>("Dogs");
            var owners = sim.Get<OwnersModule>("Owners");
            Assert.Equal(sim.Players, bodies.Ropes.Count);
            for (int p = 0; p < sim.Players; p++)
            {
                Assert.Equal(dogs.BodyOf(p), leashes.DogBody(p));
                Assert.Equal(owners.BodyOf(p), leashes.OwnerBody(p));
                Assert.Equal(dogs.BreedOf(p).LeashLength, leashes.LengthOf(p));
                Assert.Contains(bodies.Ropes, r => r.A == dogs.BodyOf(p) && r.B == owners.BodyOf(p));
            }
            var slack = leashes.Tension(sim, 0);
            for (int i = 0; i < 90; i++)
            {
                sim.Step(Inputs(sim, new PlayerInput(0, 10, PlayerInput.PushBit)));
                for (int p = 0; p < sim.Players; p++)
                {
                    var t = leashes.Tension(sim, p);
                    Assert.True(t >= Fixed.Zero && t <= Fixed.One);
                }
            }
            Assert.True(leashes.Tension(sim, 0) > slack, "running away from the owner tightens the leash");
        }

        [Fact]
        public void OwnerHasNoHealthAnywhere()
        {
            foreach (var t in typeof(Game.Core.Modules.Owners.Types.OwnerType).Assembly.GetTypes())
                if (t.Namespace != null && t.Namespace.StartsWith("Game.Core.Modules.Owners"))
                    foreach (var m in t.GetMembers())
                        Assert.DoesNotContain("health", m.Name.ToLowerInvariant());
        }
    }
}
