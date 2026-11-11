using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Bots;
using Game.Core.Bots.Skills;
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

namespace Game.Core.Tests.Bots
{
    /// <summary>F-032: the five skills of the duel bot on the duel arena, led by a test strategy through the real BotDriver
    /// (reaction and hold of the smart archetype). The rival is idle or scripted.</summary>
    public class SkillsTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);
        private static Vec2 P(string x, string y) => new Vec2(F(x), F(y));

        /// <summary>Sets velocities of bodies every tick between Dogs and Bodies: a scripted rival.</summary>
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

        /// <summary>A strategy for the test: asks a function of the view and the tick for the cursor (the skill chosen inside).</summary>
        private sealed class Led : IBotStrategy
        {
            public delegate PlayerInput Pick(BotView v, ref DetRandom rng);
            private readonly Pick _pick;
            public Led(Pick pick) { _pick = pick; }
            public PlayerInput Decide(BotView view, ref DetRandom rng) => _pick(view, ref rng);
            public IBotStrategy Clone() => this;
        }

        private sealed class Scene
        {
            public GameData Data = null!;
            public Simulation Sim = null!;
            public Shove Shove = null!;
            public BodiesModule Bodies = null!;
            public DogsModule Dogs = null!;
            public OwnersModule Owners = null!;
            public HitsModule Hits = null!;
            public SkillSet Skills = null!;
            public BotDriver? Driver;
            public readonly List<Hit> Blows = new List<Hit>();
            public int Dog(int p) => Dogs.BodyOf(p);
            public int Owner(int p) => Owners.BodyOf(p);
            public void Put(int body, string x, string y) => Bodies.Teleport(body, P(x, y));
            public Vec2 DogPos(int p) => Bodies.Position(Dog(p));
            public Vec2 OwnerPos(int p) => Bodies.Position(Owner(p));

            public void Lead(Led.Pick pick) =>
                Driver = new BotDriver(0, Archetype.Find(Data, "smart"), new Led(pick), 1UL);

            /// <summary>One tick; the led participant 0 sees the screen through the driver, the rival gives no input.</summary>
            public void Tick()
            {
                var t = TickInputs.Empty(Sim.Players);
                if (Driver != null) t[0] = Driver.Next(GameSetup.Game.Sense(Sim, 0));
                Sim.Step(t);
                Blows.AddRange(Hits.ThisTick);
            }
        }

        private static Scene Duel(string owner = "teen")
        {
            var data = Repo.Data().WithArena("duel").WithHumanOwner(owner);
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
                Data = data, Sim = sim, Shove = shove,
                Bodies = sim.Get<BodiesModule>("Bodies"), Dogs = sim.Get<DogsModule>("Dogs"), Owners = sim.Get<OwnersModule>("Owners"),
                Hits = sim.Get<HitsModule>("Hits"), Skills = new SkillSet(data, Fixed.Zero),
            };
        }

        private static SeenObject Point(Vec2 p) => new SeenObject { Kind = "point", X = p.X, Y = p.Y };

        [Fact]
        public void SkillNumbersAreInDataWithTheTuningListAndLimits()
        {
            var d = Repo.Data();
            foreach (var (skill, keys) in new[]
            {
                ("move", new[] { "clearance_m" }),
                ("spin", new[] { "ring_m", "lead_deg", "target_speed_mps" }),
                ("dodge", new[] { "horizon_s", "danger_m", "step_m", "cover_m" }),
                ("brake", new[] { "stop_gain_s" }),
            })
            {
                var node = d.Get("bots.skills." + skill);
                Assert.NotNull(node["_"]);
                var tuned = node["тюнить"]!.Items.Select(i => i.AsString()).ToList();
                foreach (var k in keys) { Assert.Contains(k, tuned); Assert.True(d.Has("bots.skills." + skill + "." + k)); }
            }
        }

        [Fact]
        public void SenseShowsTheObstaclesOfTheArena()
        {
            var s = Duel();
            var v = GameSetup.Game.Sense(s.Sim, 0);
            var circles = v.Objects.Where(o => o.Kind == "obstacle_circle").ToList();
            var rects = v.Objects.Where(o => o.Kind == "obstacle_rect").ToList();
            Assert.Equal(2, circles.Count);
            Assert.Equal(2, rects.Count);
            Assert.All(circles, c => Assert.Equal(F("0.5"), c.Value));
            Assert.All(rects, r => { Assert.InRange(r.Value.ToDouble(), 0.799, 0.801); Assert.InRange(r.Value2.ToDouble(), 0.599, 0.601); });
            Assert.Equal(4, v.Objects.Where(o => o.Kind.StartsWith("obstacle")).Select(o => o.Id).Distinct().Count());
        }

        [Fact]
        public void SkillStateIsClonedWithTheSkill()
        {
            var s = Duel();
            var spin = s.Skills.Spin;
            var rng = new DetRandom(1);
            var v = GameSetup.Game.Sense(s.Sim, 0);
            spin.Act(v, default, ref rng); // chooses the sense of the turn
            var copy = (SpinSkill)spin.Clone();
            var a = rng; var b = rng;
            Assert.Equal(spin.Act(v, default, ref a), copy.Act(v, default, ref b));
            Assert.NotSame(spin, copy);
        }

        /// <summary>The goal is behind the cube at (0, 3.5), level with its centre. From the spawn a straight cursor
        /// slides round the cube along its face (the physics pushes a dog out along the face), so the dog starts level
        /// with the goal: the straight line then meets the left face squarely and the cursor holds the dog against it.</summary>
        private static Fixed RunToPoint(Scene s, bool withMove, out Fixed closest)
        {
            var goal = P("3", "3.5");
            s.Put(s.Dog(0), "-4", "3.5"); s.Put(s.Owner(0), "-6.5", "3.5");
            s.Lead((BotView v, ref DetRandom rng) =>
                withMove ? s.Skills.Move.Act(v, Point(goal), ref rng) : Cursor_At(goal));
            closest = Fixed.FromInt(100);
            for (int i = 0; i < 6 * Simulation.TicksPerSecond; i++)
            {
                s.Tick();
                closest = Fixed.Min(closest, s.DogPos(0).DistanceTo(goal));
            }
            return s.DogPos(0).DistanceTo(goal);
        }

        private static PlayerInput Cursor_At(Vec2 p) => new PlayerInput((p.X * 100).FloorToInt(), (p.Y * 100).FloorToInt());

        [Fact]
        public void MoveGoesRoundTheCubeAndReachesThePointBehindIt()
        {
            var s = Duel();
            RunToPoint(s, true, out var closest);
            Assert.True(closest < F("0.5"), $"Move came to {closest} m of the point");
        }

        [Fact]
        public void AStraightCursorToTheSamePointDoesNotGetThere()
        {
            var s = Duel();
            RunToPoint(s, false, out var closest);
            Assert.True(closest >= F("0.5"), $"the straight cursor came to {closest} m of the point");
        }

        [Fact]
        public void SpinBringsATeenOwnerToBlowSpeedInThreeSeconds()
        {
            var s = Duel("teen");
            s.Lead((BotView v, ref DetRandom rng) => s.Skills.Spin.Act(v, default, ref rng));
            var best = Fixed.Zero;
            for (int i = 0; i < 3 * Simulation.TicksPerSecond; i++)
            {
                s.Tick();
                best = Fixed.Max(best, s.Bodies.Velocity(s.Owner(0)).Length);
            }
            Assert.True(best >= s.Data.Num("hits.threshold_mps"), $"the owner reached {best} m/s");
        }

        /// <summary>The rival's owner flies at my dog along -X at 9 m/s from (7, 0.3); my dog stands at (0, 0) among no obstacle
        /// within cover reach; my owner is behind it on the dodge side. Returns the blows on me and how near the owner came.</summary>
        private static Fixed RivalFlies(Scene s, bool dodge, out List<Hit> blows)
        {
            s.Put(s.Dog(0), "0", "0"); s.Put(s.Owner(0), "-1", "-1.5");
            s.Put(s.Owner(1), "7", "0.3"); s.Put(s.Dog(1), "7.8", "0.3");
            s.Shove.Velocities[s.Owner(1)] = P("-9", "0");
            s.Shove.Velocities[s.Dog(1)] = P("-9", "0");
            if (dodge)
            {
                var rival = new SeenObject { Kind = "dog", Id = 1 };
                s.Lead((BotView v, ref DetRandom rng) => s.Skills.Dodge.Act(v, rival, ref rng));
            }
            var nearest = Fixed.FromInt(100);
            for (int i = 0; i < 2 * Simulation.TicksPerSecond; i++)
            {
                s.Tick();
                nearest = Fixed.Min(nearest, s.OwnerPos(1).DistanceTo(s.DogPos(0)));
            }
            blows = s.Blows.Where(b => b.Victim == 0).ToList();
            return nearest;
        }

        [Fact]
        public void DodgeLetsTheFlyingOwnerPassWithoutAHit()
        {
            var s = Duel();
            var nearest = RivalFlies(s, true, out var blows);
            Assert.Empty(blows);
            Assert.True(nearest >= s.Data.Num("bots.skills.dodge.danger_m"), $"the owner passed at {nearest} m");
        }

        [Fact]
        public void AStandingDogTakesTheSameBlow()
        {
            var s = Duel();
            RivalFlies(s, false, out var blows);
            Assert.NotEmpty(blows);
        }

        [Fact]
        public void DodgeGoesBehindTheNearestObstacleWhenItIsWithinCover()
        {
            var s = Duel();
            // the dog stands 1 m from the circle at (-2.5, 2) on the side of the rival
            s.Put(s.Dog(0), "-1.5", "2.2"); s.Put(s.Owner(1), "6", "2.4"); s.Put(s.Dog(1), "7", "2.4");
            s.Shove.Velocities[s.Owner(1)] = P("-9", "0");
            var v = GameSetup.Game.Sense(s.Sim, 0);
            s.Shove.Velocities[s.Owner(1)] = P("-9", "0");
            s.Tick(); // the velocity shows up in the view
            v = GameSetup.Game.Sense(s.Sim, 0);
            var rng = new DetRandom(1);
            var input = s.Skills.Dodge.Act(v, new SeenObject { Kind = "dog", Id = 1 }, ref rng);
            Assert.True(input.AimX < -250 - 50, $"the cursor is at x={input.AimX} cm: not behind the circle");
        }

        [Fact]
        public void BrakeStopsADogRunningAtSevenMetresPerSecond()
        {
            var s = Duel();
            var goal = P("7.5", "-1.5");
            s.Put(s.Owner(0), "-1", "-1.5"); s.Put(s.Dog(0), "-0.5", "-1.5");
            int brakeFrom = 45; // the driver sees the screen 0.25 s late: the dog runs on a bit more before the first Brake
            s.Lead((BotView v, ref DetRandom rng) =>
                v.Tick < brakeFrom ? s.Skills.Move.Act(v, Point(goal), ref rng) : s.Skills.Brake.Act(v, default, ref rng));
            for (int i = 0; i < brakeFrom + 8; i++) s.Tick();
            var speed = s.Bodies.Velocity(s.Dog(0)).Length;
            Assert.True(speed >= F("6.5"), $"the dog ran at {speed} m/s");
            int ticks = 0;
            while (s.Bodies.Velocity(s.Dog(0)).Length >= Fixed.One && ticks < 3 * Simulation.TicksPerSecond) { s.Tick(); ticks++; }
            Assert.True(ticks <= Simulation.TicksPerSecond, $"it took {ticks} ticks to slow below 1 m/s");
        }

        [Fact]
        public void StrikeAimsLikeTheSwing()
        {
            var s = Duel();
            var v = GameSetup.Game.Sense(s.Sim, 0);
            var rival = v.Objects.First(o => o.Kind == "dog");
            var rngA = new DetRandom(5); var rngB = new DetRandom(5);
            var expected = new SwingSkill(s.Data).Aim(v, rival, Fixed.Zero, ref rngA);
            var input = s.Skills.Strike.Act(v, rival, ref rngB);
            Assert.Equal(new PlayerInput((expected.X * 100).FloorToInt(), (expected.Y * 100).FloorToInt()), input);
        }
    }
}
