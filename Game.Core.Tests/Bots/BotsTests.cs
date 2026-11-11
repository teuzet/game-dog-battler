using System.Linq;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Bots
{
    /// <summary>F-006: honest sensors, the shared aimed swing, the archetype table.</summary>
    public class BotsTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);

        private static GameData SandboxData(string owner = "teen")
        {
            var data = Repo.Data();
            return data.WithArena("sandbox").WithHumanOwner(owner);
        }

        private static BotView View(Vec2 owner, Vec2 ownerVel)
        {
            var v = new BotView { Self = 0, X = owner.X + F("2"), Y = owner.Y };
            v.Hud["leash_m"] = F("3");
            v.Hud["arena_half_width"] = F("12");
            v.Hud["arena_half_height"] = F("8");
            v.Objects.Add(new SeenObject { Kind = "owner", Id = 0, X = owner.X, Y = owner.Y, VX = ownerVel.X, VY = ownerVel.Y });
            return v;
        }

        private static SeenObject Dog(string x, string y, string vx = "0", string vy = "0") =>
            new SeenObject { Kind = "dog", Id = 1, X = F(x), Y = F(y), VX = F(vx), VY = F(vy) };

        [Fact]
        public void ArchetypesCarryTheTableOfTheDesign()
        {
            var data = Repo.Data();
            (string Name, int Reaction, int Hold, string Aim)[] table =
            {
                ("random", 400, 1000, "0"), ("smart", 250, 600, "0.6"), ("camper", 300, 3000, "1"),
            };
            foreach (var row in table)
            {
                var a = Archetype.Find(data, row.Name);
                Assert.Equal(row.Reaction, a.ReactionMs);
                Assert.Equal(row.Hold, a.HoldMs);
                Assert.Equal(F(row.Aim), a.AimError);
            }
            Assert.Equal(0, Archetype.Find(data, "idle").ReactionMs);
        }

        [Fact]
        public void SenseShowsVelocitiesAndTheLeashButNoIntentsOrFuture()
        {
            var data = SandboxData();
            var sim = GameSetup.Game.Create(4, data);
            var bots = Repo.Bots(GameSetup.Game, data, sim.Players, 4);
            for (int i = 0; i < 40; i++) sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
            var view = GameSetup.Game.Sense(sim, 0);
            Assert.True(view.Hud["leash_m"] > Fixed.Zero);
            Assert.True(new Vec2(view.VX, view.VY).Length > Fixed.Zero, "the bot's own dog is running");
            Assert.True(SwingSkill.Owner(view, 0).Vel.Length > Fixed.Zero, "its owner is being dragged");
            Assert.Equal(3, view.Objects.Count(o => o.Kind == "dog")); // the rivals' dogs, not its own
            Assert.Equal(4, view.Objects.Count(o => o.Kind == "owner"));
            Assert.All(view.Objects, o => Assert.Contains(o.Kind, new[] { "dog", "owner", "zone", "next_zone" }));
        }

        [Fact]
        public void SwingGoesThroughAStandingTargetAndBeyondIt()
        {
            var skill = new SwingSkill(Repo.Data());
            var rng = new DetRandom(1);
            // owner at (-8, 0) already running at the target at (0, 0)
            var aim = skill.Aim(View(new Vec2(F("-8"), F("0")), new Vec2(F("5"), F("0"))), Dog("0", "0"), Fixed.Zero, ref rng);
            Assert.True(aim.X > F("0"), "the cursor is beyond the target");
            Assert.Equal(Fixed.Zero, aim.Y);
        }

        [Fact]
        public void SwingLeadsAMovingTarget()
        {
            var skill = new SwingSkill(Repo.Data());
            var rng = new DetRandom(1);
            var owner = new Vec2(F("-8"), F("0"));
            var still = skill.Aim(View(owner, new Vec2(F("5"), F("0"))), Dog("0", "0"), Fixed.Zero, ref rng);
            var running = skill.Aim(View(owner, new Vec2(F("5"), F("0"))), Dog("0", "0", "0", "4"), Fixed.Zero, ref rng);
            Assert.True(running.Y > still.Y, "the cursor goes where the target will be (running up)");
        }

        [Fact]
        public void SwingBacksOffForARunUpWhenTheOwnerIsCloseAndAtRest()
        {
            var data = Repo.Data();
            var skill = new SwingSkill(data);
            var rng = new DetRandom(1);
            var owner = new Vec2(-data.Num("bots.swing.run_up_m") / 2, F("0")); // closer than the run-up distance
            var aim = skill.Aim(View(owner, Vec2.Zero), Dog("0", "0"), Fixed.Zero, ref rng);
            Assert.True(aim.X <= owner.X + F("0.01") || aim.Y != Fixed.Zero,
                "the dog does not charge at once: it runs away or round to gather speed");
        }

        [Fact]
        public void AimErrorSpreadsTheSwingPointWithinTheArchetypeError()
        {
            var skill = new SwingSkill(Repo.Data());
            var rng = new DetRandom(7);
            var view = View(new Vec2(F("-8"), F("0")), new Vec2(F("5"), F("0")));
            var exact = skill.Aim(view, Dog("0", "0"), Fixed.Zero, ref rng);
            bool differs = false;
            for (int i = 0; i < 20; i++)
            {
                var p = skill.Aim(view, Dog("0", "0"), F("0.6"), ref rng);
                Assert.True((p.X - exact.X).Abs() <= F("0.6") && (p.Y - exact.Y).Abs() <= F("0.6"));
                differs |= p != exact;
            }
            Assert.True(differs);
        }

        [Fact]
        public void SmartHitsMannequinsMoreOftenThanRandomAndCamperSitsStill()
        {
            int Hits(string archetype)
            {
                int total = 0;
                for (int seed = 1; seed <= 3; seed++)
                {
                    var data = SandboxData("jock");
                    var sim = GameSetup.Game.Create(seed, data);
                    var bots = Repo.Bots(GameSetup.Game, data, sim.Players, seed, "idle");
                    var a = Archetype.Find(data, archetype);
                    bots[0] = new BotDriver(0, a, GameSetup.Game.Strategy(a, data), (ulong)seed);
                    while (!sim.IsOver) sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
                    total += (int)GameSetup.Game.Metrics(sim, 0)["hits"];
                }
                return total;
            }
            int smart = Hits("smart"), random = Hits("random");
            Assert.True(smart >= 10, $"smart hit {smart} times in 6 minutes of sandbox");
            Assert.True(smart > random * 2, $"smart {smart} vs random {random}");
            Assert.Equal(0, Hits("camper"));
        }

        [Fact]
        public void WanderChangesItsPointEveryOneToTwoSeconds()
        {
            var s = new WanderStrategy(F("1"), F("2"));
            var rng = new DetRandom(3);
            var view = new BotView();
            view.Hud["arena_half_width"] = F("12");
            view.Hud["arena_half_height"] = F("8");
            var points = new System.Collections.Generic.List<(int Tick, PlayerInput P)>();
            for (int t = 0; t < 300; t++)
            {
                view.Tick = t;
                var p = s.Decide(view, ref rng);
                if (points.Count == 0 || !points[^1].P.Equals(p)) points.Add((t, p));
            }
            for (int i = 1; i < points.Count - 1; i++)
            {
                int gap = points[i + 1].Tick - points[i].Tick;
                Assert.InRange(gap, Simulation.TicksPerSecond, 2 * Simulation.TicksPerSecond + 1);
            }
        }
    }
}
