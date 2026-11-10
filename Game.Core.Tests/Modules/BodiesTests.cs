using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Modules
{
    public class BodiesTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);
        private static Vec2 P(string x, string y) => new Vec2(F(x), F(y));

        /// <summary>A session on a test arena made from the sandbox plus the given obstacles; the game's
        /// own dogs and owners stand at the spawns (|Y| ≥ 4), tests work around the origin.</summary>
        private static Simulation Session(out BodiesModule bodies, string obstaclesJson = "[]", int seed = 1)
        {
            var data = Repo.Data();
            var arena = Json.Parse("{\"title\":\"t\",\"rules\":\"Sandbox\",\"width\":24,\"height\":16,\"spawns\":" +
                                   data.Get("arenas.sandbox")["spawns"]!.ToString() + ",\"obstacles\":" + obstaclesJson + ",\"props\":[]}");
            data.Root["arenas"]!.Set("test", arena);
            data.Root["game"]!.Set("arena", JsonValue.Str("test"));
            var sim = GameSetup.Game.Create(seed, data);
            bodies = sim.Get<BodiesModule>("Bodies");
            return sim;
        }

        private static void Tick(Simulation sim, int n = 1)
        {
            for (int i = 0; i < n; i++) sim.Step(TickInputs.Empty(sim.Players));
        }

        [Fact]
        public void DogDragsOwnerWithinLeashAndOwnerSlidesToAStopByFriction()
        {
            var sim = Session(out var bodies);
            var data = Repo.Data(); // the roster's numbers: a 2-mass collie against a 60-mass teen
            var length = data.Num("dogs.collie.leash_m");
            int dog = bodies.AddCircle(P("0", "0"), data.Num("dogs.collie.mass"), data.Num("dogs.collie.radius"), data.Num("dogs.collie.friction"));
            int owner = bodies.AddCircle(P("-2", "0"), data.Num("owners.teen.mass"), data.Num("owners.teen.radius"), data.Num("owners.teen.friction"));
            bodies.AddRope(dog, owner, length, data.Num("leash.stiffness"));
            Fixed maxStretch = Fixed.Zero;
            for (int t = 0; t < 60; t++) // 2 s of running at 7 m/s
            {
                bodies.Drive(dog, P("7", "0"), Fixed.FromInt(35));
                Tick(sim);
                var d = bodies.Position(dog).DistanceTo(bodies.Position(owner));
                if (d > maxStretch) maxStretch = d;
            }
            Assert.True(bodies.Velocity(owner).X > Fixed.FromInt(2), "the owner is dragged along");
            Assert.True(bodies.Position(owner).X > Fixed.FromInt(3), "the owner moved with the dog");
            Assert.True(maxStretch <= length + F("0.1"), $"stretch {maxStretch}");
            // the dog stops (friction), the owner slides on, then stops too
            Tick(sim, 150);
            Assert.Equal(Vec2.Zero, bodies.Velocity(owner));
            Assert.Equal(Vec2.Zero, bodies.Velocity(dog));
            Assert.True(bodies.Position(dog).DistanceTo(bodies.Position(owner)) <= length + F("0.1"));
        }

        [Fact]
        public void RopeAllowsSlack()
        {
            var sim = Session(out var bodies);
            int a = bodies.AddCircle(P("0", "0"), Fixed.FromInt(10), F("0.3"), Fixed.FromInt(2));
            int b = bodies.AddCircle(P("2", "0"), Fixed.FromInt(10), F("0.3"), Fixed.FromInt(2));
            bodies.AddRope(a, b, Fixed.FromInt(3), Fixed.FromInt(50));
            bodies.Drive(a, P("0", "1"), Fixed.FromInt(1000));
            Tick(sim);
            Assert.Equal(Fixed.Zero, bodies.Velocity(b).X);
            Assert.Equal(Fixed.Zero, bodies.Velocity(b).Y);
        }

        [Fact]
        public void FixedBodyHoldsTheRopeAsAnAxis()
        {
            var sim = Session(out var bodies);
            int post = bodies.AddFixed(P("0", "0"), F("0.3"));
            int dog = bodies.AddCircle(P("1", "0"), Fixed.FromInt(10), F("0.3"), Fixed.FromInt(2));
            bodies.AddRope(post, dog, Fixed.FromInt(2), Fixed.FromInt(50));
            for (int t = 0; t < 60; t++)
            {
                bodies.Drive(dog, P("5", "0"), Fixed.FromInt(40));
                Tick(sim);
            }
            Assert.Equal(P("0", "0"), bodies.Position(post));
            Assert.True(bodies.Position(dog).DistanceTo(P("0", "0")) <= Fixed.FromInt(2) + F("0.1"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FastBodyDoesNotTunnelThroughAnother(bool targetFixed)
        {
            var sim = Session(out var bodies);
            int bullet = bodies.AddCircle(P("-4", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            int target = targetFixed
                ? bodies.AddFixed(P("0", "0"), F("0.4"))
                : bodies.AddCircle(P("0", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            bodies.Drive(bullet, P("14", "0"), Fixed.FromInt(100000));
            bool touched = false;
            for (int t = 0; t < 30; t++)
            {
                Tick(sim);
                Assert.True(bodies.Position(bullet).X < bodies.Position(target).X, $"tick {t}: passed through");
                foreach (var c in bodies.Contacts)
                    if (c.A == bullet && c.B == target)
                    {
                        touched = true;
                        Assert.True(c.RelSpeed >= Fixed.FromInt(13), $"relative speed {c.RelSpeed}");
                    }
            }
            Assert.True(touched);
        }

        [Fact]
        public void ContactCarriesBothBodiesAndRelativeSpeed()
        {
            var sim = Session(out var bodies);
            int a = bodies.AddCircle(P("-1", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            int b = bodies.AddCircle(P("1", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            bodies.Drive(a, P("6", "0"), Fixed.FromInt(100000));
            bodies.Drive(b, P("-6", "0"), Fixed.FromInt(100000));
            bool found = false;
            for (int t = 0; t < 10; t++)
            {
                bodies.Drive(a, P("6", "0"), Fixed.FromInt(100000));
                bodies.Drive(b, P("-6", "0"), Fixed.FromInt(100000));
                Tick(sim);
                foreach (var c in bodies.Contacts)
                    if (c.A == a && c.B == b) { found = true; Assert.True(c.RelSpeed >= Fixed.FromInt(11)); Assert.True(c.Normal.X > Fixed.Zero); }
            }
            Assert.True(found);
        }

        [Fact]
        public void BodyBouncesOffCircleObstacle()
        {
            const string tree = "[{\"shape\":\"circle\",\"x\":0,\"y\":0,\"radius\":1}]";
            var sim = Session(out var bodies, tree);
            int ball = bodies.AddCircle(P("-3", "0"), Fixed.FromInt(5), F("0.5"), Fixed.Zero);
            bool hit = false;
            bodies.Drive(ball, P("10", "0"), Fixed.FromInt(100000));
            for (int t = 0; t < 30; t++)
            {
                Tick(sim);
                Assert.True(bodies.Position(ball).DistanceTo(P("0", "0")) >= F("1.49"), $"inside the tree at tick {t}");
                foreach (var c in bodies.Contacts) if (c.A == ball && c.IsStatic) hit = true;
            }
            Assert.True(hit);
            Assert.True(bodies.Velocity(ball).X < Fixed.Zero, "bounced back");
        }

        [Fact]
        public void StaticContactsNameTheObstacleAndTheBound()
        {
            // tree 0 (circle), bench 1 is index 1 (rect after circles); bounds follow: left, right, bottom, top
            const string both = "[{\"shape\":\"circle\",\"x\":-4,\"y\":0,\"radius\":1},{\"shape\":\"rect\",\"x\":4,\"y\":0,\"width\":2,\"height\":2}]";
            var sim = Session(out var bodies, both);
            int a = bodies.AddCircle(P("-2", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero); // → tree
            int b = bodies.AddCircle(P("2", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);  // → bench
            int c = bodies.AddCircle(P("0", "5"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);  // → top bound (hh = 8)
            var seen = new System.Collections.Generic.Dictionary<int, int>();
            for (int t = 0; t < 40; t++)
            {
                bodies.Drive(a, P("-8", "0"), Fixed.FromInt(100000));
                bodies.Drive(b, P("8", "0"), Fixed.FromInt(100000));
                bodies.Drive(c, P("0", "8"), Fixed.FromInt(100000));
                Tick(sim);
                foreach (var k in bodies.Contacts) if (k.IsStatic && !seen.ContainsKey(k.A)) seen[k.A] = k.StaticIndex;
            }
            Assert.Equal(0, seen[a]);
            Assert.Equal(1, seen[b]);
            Assert.Equal(2 + 3, seen[c]);
        }

        [Fact]
        public void BodyStopsAtRectangleObstacleFromAnySide()
        {
            const string bench = "[{\"shape\":\"rect\",\"x\":0,\"y\":0,\"width\":4,\"height\":1}]";
            var sim = Session(out var bodies, bench);
            int fromLeft = bodies.AddCircle(P("-5", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            int fromAbove = bodies.AddCircle(P("0", "5"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            int inside = bodies.AddCircle(P("1.5", "0.1"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            for (int t = 0; t < 30; t++)
            {
                bodies.Drive(fromLeft, P("9", "0"), Fixed.FromInt(100000));
                bodies.Drive(fromAbove, P("0", "-9"), Fixed.FromInt(100000));
                Tick(sim);
            }
            Assert.True(bodies.Position(fromLeft).X <= F("-2.39"));
            Assert.True(bodies.Position(fromAbove).Y >= F("0.89"));
            var p = bodies.Position(inside);
            Assert.True(p.X.Abs() >= F("2.39") || p.Y.Abs() >= F("0.89"), $"still inside the bench at {p}");
        }

        [Fact]
        public void BoundsHoldBodiesInsideTheArena()
        {
            var sim = Session(out var bodies);
            int a = bodies.AddCircle(P("11", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            int b = bodies.AddCircle(P("0", "7"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            for (int t = 0; t < 60; t++)
            {
                bodies.Drive(a, P("14", "0"), Fixed.FromInt(100000));
                bodies.Drive(b, P("0", "14"), Fixed.FromInt(100000));
                Tick(sim);
                Assert.True(bodies.Position(a).X <= F("11.61"));
                Assert.True(bodies.Position(b).Y <= F("7.61"));
            }
        }

        [Fact]
        public void KinematicPushesDynamicAndIsNotPushed()
        {
            var sim = Session(out var bodies);
            int car = bodies.AddKinematic(P("-3", "0"), F("0.8"), P("5", "0"));
            int dog = bodies.AddCircle(P("0", "0"), Fixed.FromInt(10), F("0.3"), Fixed.Zero);
            Tick(sim, 30);
            Assert.Equal(Fixed.FromInt(5), bodies.Velocity(car).X);
            Assert.True(bodies.Position(dog).X > bodies.Position(car).X);
        }

        [Fact]
        public void SameSeedAndInputsGiveSameHashWithObstaclesRopesAndCollisions()
        {
            const string obs = "[{\"shape\":\"circle\",\"x\":1,\"y\":1,\"radius\":1},{\"shape\":\"rect\",\"x\":-3,\"y\":0,\"width\":2,\"height\":1}]";
            ulong Run()
            {
                var sim = Session(out var bodies, obs, 5);
                int a = bodies.AddCircle(P("-6", "0"), Fixed.FromInt(20), F("0.35"), Fixed.FromInt(6));
                int b = bodies.AddCircle(P("-8", "0"), Fixed.FromInt(60), F("0.4"), Fixed.FromInt(3));
                bodies.AddRope(a, b, Fixed.FromInt(3), Fixed.FromInt(50));
                for (int t = 0; t < 120; t++)
                {
                    bodies.Drive(a, P("7", t % 40 < 20 ? "1" : "-1"), Fixed.FromInt(35));
                    Tick(sim);
                }
                return sim.StateHash();
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void ArenaLoadsBoundsObstaclesSpawnsAndRules()
        {
            const string obs = "[{\"shape\":\"circle\",\"x\":2,\"y\":3,\"radius\":0.5},{\"shape\":\"rect\",\"x\":-4,\"y\":1,\"width\":3,\"height\":0.8}]";
            var sim = Session(out _, obs);
            var arena = sim.Get<ArenaModule>("Arena");
            Assert.Equal(Fixed.FromInt(24), arena.Width);
            Assert.Equal(Fixed.FromInt(16), arena.Height);
            Assert.Equal("Sandbox", arena.RulesName);
            Assert.Equal(4, arena.Spawns.Count);
            Assert.Single(arena.CircleObstacles);
            Assert.Equal(F("0.5"), arena.CircleObstacles[0].Radius);
            Assert.Single(arena.RectObstacles);
            Assert.Equal(F("1.5"), arena.RectObstacles[0].HalfWidth);
            Assert.Equal(F("0.4"), arena.RectObstacles[0].HalfHeight);
        }

        [Fact]
        public void ForkKeepsPhysicsIndependent()
        {
            var sim = Session(out var bodies);
            int a = bodies.AddCircle(P("0", "0"), Fixed.FromInt(5), F("0.4"), Fixed.Zero);
            bodies.Drive(a, P("3", "0"), Fixed.FromInt(1000));
            Tick(sim);
            var fork = sim.Fork();
            Tick(sim, 5);
            Assert.NotEqual(sim.StateHash(), fork.StateHash());
            Tick(fork, 5);
            Assert.Equal(sim.StateHash(), fork.StateHash());
        }
    }
}
