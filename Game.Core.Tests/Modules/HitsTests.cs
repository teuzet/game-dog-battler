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
    /// <summary>Blows (design/roster.md → «Удар») and the swing sandbox.</summary>
    public class HitsTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);
        private static Vec2 P(string x, string y) => new Vec2(F(x), F(y));

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
            public RulesModule Rules = null!;
            public int Dog(int p) => Dogs.BodyOf(p);
            public int Owner(int p) => Owners.BodyOf(p);
            public void Put(int body, string x, string y) => Bodies.Teleport(body, P(x, y));
            public List<GameEvent> Events(string kind) => Sim.Events.All.Where(e => e.Kind == kind).ToList();

            public void Tick(int n = 1, Dictionary<int, PlayerInput>? input = null)
            {
                for (int i = 0; i < n; i++)
                {
                    var t = TickInputs.Empty(Sim.Players);
                    if (input != null) foreach (var kv in input) t[kv.Key] = kv.Value;
                    Sim.Step(t);
                }
            }
        }

        /// <summary>The game's own modules (same order as GameSetup, plus Shove before Bodies) on the sandbox arena.
        /// Player 0's owner is of the given type. Everybody is parked in the corners; the test places who it needs.</summary>
        private static Scene Sandbox(string owner = "teen")
        {
            var data = Repo.Data();
            data = new GameData(data.Root.MergedWith(Json.Parse("{\"game\":{\"arena\":\"sandbox\",\"human_owner\":\"" + owner + "\"}}")), data.Variant);
            int players = data.Int("game.players");
            var shove = new Shove();
            var modules = new List<IModule>
            {
                new ArenaModule(data), new ControlModule(data, players), new OwnersModule(data, players), new DogsModule(data, players),
                new PropsModule(), new LeashesModule(data, players), shove, new BodiesModule(data), new HitsModule(data, players),
                new RulesModule(data, players), new MatchModule(data, players),
            };
            var sim = new Simulation(1, players, data, modules);
            var s = new Scene
            {
                Sim = sim, Shove = shove,
                Bodies = sim.Get<BodiesModule>("Bodies"), Dogs = sim.Get<DogsModule>("Dogs"), Owners = sim.Get<OwnersModule>("Owners"),
                Hits = sim.Get<HitsModule>("Hits"), Rules = sim.Get<RulesModule>("Rules"),
            };
            s.Put(s.Dog(2), "10", "6"); s.Put(s.Owner(2), "11", "7");
            s.Put(s.Dog(3), "10", "-6"); s.Put(s.Owner(3), "11", "-7");
            return s;
        }

        /// <summary>Player 0's owner runs along +X at <paramref name="speed"/> toward the dog of player 1 at (0, <paramref name="offset"/>),
        /// his own dog running beside him (the leash stays slack). Runs until the owner is past the rival.</summary>
        private static Scene Swing(string owner, string speed, string offset = "0")
        {
            var s = Sandbox(owner);
            s.Put(s.Owner(0), "-4", "0"); s.Put(s.Dog(0), "-4", "-2.5");
            s.Put(s.Dog(1), "0", offset); s.Put(s.Owner(1), "0", "3");
            var v = new Vec2(F(speed), Fixed.Zero);
            s.Shove.Velocities[s.Owner(0)] = v;
            s.Shove.Velocities[s.Dog(0)] = v;
            for (int i = 0; i < 30; i++)
            {
                s.Tick();
                if (s.Hits.ThisTick.Count > 0) s.Shove.Velocities.Clear(); // the script ends with the blow: the owner coasts on
            }
            return s;
        }

        [Fact]
        public void HitNumbersAreInData()
        {
            var data = Repo.Data();
            Assert.Equal(Fixed.FromInt(5), data.Num("hits.threshold_mps"));
            Assert.Equal(Fixed.FromInt(9), data.Num("hits.strong_threshold_mps"));
            Assert.Equal(Fixed.FromInt(2), data.Num("hits.knockback_min_m"));
            Assert.Equal(Fixed.FromInt(7), data.Num("hits.knockback_max_m"));
            Assert.Equal(F("0.4"), data.Num("hits.stun_base_s"));
            Assert.Equal(F("0.05"), data.Num("hits.stun_per_mps_s"));
            Assert.Equal(F("1.2"), data.Num("hits.stun_max_s"));
        }

        [Fact]
        public void OwnerBelowThresholdSpeedDoesNotHit()
        {
            var s = Swing("teen", "4");
            Assert.Empty(s.Events("hit"));
            Assert.Empty(s.Events("strong_hit"));
            Assert.False(s.Hits.IsStunned(1));
        }

        [Fact]
        public void OwnerTouchingAForeignDogAtThresholdSpeedHitsHerAndNotStrongly()
        {
            var s = Swing("teen", "6");
            var hit = s.Events("hit");
            Assert.Equal(2, hit.Count);
            Assert.Empty(s.Events("strong_hit"));
            var win = hit.Single(e => e.Actor == 0);
            var lose = hit.Single(e => e.Actor == 1);
            Assert.True(win.Reward > Fixed.Zero && win.Threat == null, "the attacker gets a Reward");
            Assert.True(lose.Threat > Fixed.Zero && lose.Reward == null, "the victim gets a Threat");
            Assert.True(win.Reward <= Fixed.One);
        }

        [Fact]
        public void StrengthIsOwnerMassTimesSpeedSquaredAndStrongFromNineMetresPerSecond()
        {
            // hits of the tick are observed through a recording module placed after Hits
            foreach (var (owner, speed, mass, strong) in new[] { ("teen", "6", 60, false), ("teen", "10", 60, true), ("jock", "6", 110, false) })
            {
                var s = Sandbox(owner);
                s.Put(s.Owner(0), "-4", "0"); s.Put(s.Dog(0), "-4", "-2.5");
                s.Put(s.Dog(1), "0", "0"); s.Put(s.Owner(1), "0", "3");
                var v = new Vec2(F(speed), Fixed.Zero);
                s.Shove.Velocities[s.Owner(0)] = v;
                s.Shove.Velocities[s.Dog(0)] = v;
                Hit? seen = null;
                for (int i = 0; i < 30 && seen == null; i++)
                {
                    s.Tick();
                    if (s.Hits.ThisTick.Count > 0) seen = s.Hits.ThisTick[0];
                }
                Assert.NotNull(seen);
                var rel = (double)seen!.Value.Strength.ToDouble() / mass;
                Assert.Equal(strong, seen.Value.Strong);
                Assert.Equal(0, seen.Value.Attacker);
                Assert.Equal(1, seen.Value.Victim);
                // the speed of the blow is the owner's speed (the rival stands); strength = mass × speed²
                Assert.InRange(System.Math.Sqrt(rel), double.Parse(speed) - 0.3, double.Parse(speed) + 0.01);
            }
        }

        [Fact]
        public void StrongHitIsItsOwnEventWithDoubledRewardAndANovelty()
        {
            var weak = Swing("teen", "6");
            var strong = Swing("teen", "10");
            Assert.Empty(strong.Events("hit"));
            var win = strong.Events("strong_hit").Single(e => e.Actor == 0);
            var lose = strong.Events("strong_hit").Single(e => e.Actor == 1);
            Assert.Equal("strong_hit", win.Novel);
            Assert.True(win.Reward > weak.Events("hit").Single(e => e.Actor == 0).Reward! * 2, "doubled and stronger");
            Assert.True(lose.Threat > weak.Events("hit").Single(e => e.Actor == 1).Threat);
        }

        [Fact]
        public void KnockbackIsTwoToSevenMetresAndGrowsWithStrengthAwayFromTheOwner()
        {
            var results = new List<Fixed>();
            foreach (var (owner, speed) in new[] { ("teen", "5.5"), ("teen", "7"), ("teen", "10"), ("jock", "10") })
            {
                var s = Sandbox(owner);
                s.Put(s.Owner(0), "-4", "0"); s.Put(s.Dog(0), "-4", "-2.5");
                s.Put(s.Dog(1), "0", "0"); s.Put(s.Owner(1), "0", "3");
                var v = new Vec2(F(speed), Fixed.Zero);
                s.Shove.Velocities[s.Owner(0)] = v;
                s.Shove.Velocities[s.Dog(0)] = v;
                while (s.Hits.ThisTick.Count == 0 && s.Sim.Tick < 40) s.Tick();
                Assert.Single(s.Hits.ThisTick);
                Assert.True(s.Bodies.Velocity(s.Dog(1)).X > Fixed.Zero, "away from the owner");
                // the dog really travels this far: its own owner keeps pace so that the leash stays slack,
                // and her player pushes the mouse back at the blow (a thrown dog must not run off)
                var from = s.Bodies.Position(s.Dog(1));
                var back = new Dictionary<int, PlayerInput> { { 1, new PlayerInput(-40, 0, PlayerInput.PushBit) } };
                s.Shove.Velocities.Clear();
                int guard = 0;
                while (s.Hits.IsHelpless(1) && guard++ < 150)
                {
                    s.Tick(1, back);
                    var at = s.Bodies.Position(s.Dog(1));
                    s.Bodies.Teleport(s.Owner(1), new Vec2(at.X, at.Y + Fixed.FromInt(2)));
                }
                Assert.True(guard < 150, "the slide ends");
                var d = s.Bodies.Position(s.Dog(1)).DistanceTo(from);
                Assert.True(d.ToDouble() >= 2.0 - 0.15 && d.ToDouble() <= 7.0 + 0.15, $"{owner} at {speed} m/s threw the dog {d} m");
                results.Add(d);
            }
            Assert.True(results[0] < results[1] && results[1] < results[2] && results[2] < results[3], string.Join(", ", results));
            Assert.True(results[3] > Fixed.FromInt(3) + Fixed.Half, "a strong hit takes a dog out of a 3.5 m circle");
        }

        [Fact]
        public void StunIsFourTenthsPlusFiveHundredthsPerMetreAboveThresholdAtMostOneAndAHalf()
        {
            // 6 m/s: 0.4 + 0.05 × 1 = 0.45 s; the speed of the blow is a little under the owner's, so allow a tick
            var s = Swing("teen", "6");
            var tickAfterHit = s.Events("hit")[0].Tick;
            Assert.InRange(tickAfterHit, 1, 29);
            var again = Sandbox("teen");
            again.Put(again.Owner(0), "-4", "0"); again.Put(again.Dog(0), "-4", "-2.5");
            again.Put(again.Dog(1), "0", "0"); again.Put(again.Owner(1), "0", "3");
            again.Shove.Velocities[again.Owner(0)] = new Vec2(F("6"), Fixed.Zero);
            again.Shove.Velocities[again.Dog(0)] = new Vec2(F("6"), Fixed.Zero);
            while (again.Hits.ThisTick.Count == 0 && again.Sim.Tick < 90) again.Tick();
            Assert.Single(again.Hits.ThisTick);
            int stunned = 0;
            while (again.Hits.IsStunned(1)) { again.Tick(); stunned++; }
            Assert.InRange(stunned, 12, 14); // 0.45 s of 30 Hz ticks = 13
            // a blow of 25 m/s (beyond what an owner reaches, set by the script) is capped at 1.2 s
            var hard = Sandbox("jock");
            hard.Put(hard.Owner(0), "-8", "0"); hard.Put(hard.Dog(0), "-8", "-2.5");
            hard.Put(hard.Dog(1), "0", "0"); hard.Put(hard.Owner(1), "0", "3");
            hard.Shove.Velocities[hard.Owner(0)] = new Vec2(F("25"), Fixed.Zero);
            hard.Shove.Velocities[hard.Dog(0)] = new Vec2(F("25"), Fixed.Zero);
            while (hard.Hits.ThisTick.Count == 0 && hard.Sim.Tick < 90) hard.Tick();
            Assert.Single(hard.Hits.ThisTick);
            int left = 0;
            while (hard.Hits.IsStunned(1)) { hard.Tick(); left++; }
            Assert.InRange(left, 34, 36); // 1.2 s = 36 ticks
        }

        [Fact]
        public void StunStopsTheDogsDriveUntilItEnds()
        {
            // after the blow the victim's player pushes the mouse up (+Y); her dog is thrown along +X meanwhile
            var push = new Dictionary<int, PlayerInput> { { 1, new PlayerInput(0, 40, PlayerInput.PushBit) } };
            var s2 = Sandbox("teen");
            s2.Put(s2.Owner(0), "-4", "0"); s2.Put(s2.Dog(0), "-4", "2.5");
            s2.Put(s2.Dog(1), "0", "0"); s2.Put(s2.Owner(1), "0", "-2");
            var v = new Vec2(F("6"), Fixed.Zero);
            s2.Shove.Velocities[s2.Owner(0)] = v;
            s2.Shove.Velocities[s2.Dog(0)] = v;
            while (s2.Hits.ThisTick.Count == 0 && s2.Sim.Tick < 90) s2.Tick();
            s2.Shove.Velocities.Clear();
            Assert.True(s2.Hits.IsStunned(1));
            var vy = Fixed.Zero;
            while (s2.Hits.IsStunned(1))
            {
                s2.Tick(1, push);
                if (s2.Hits.IsStunned(1)) vy = Fixed.Max(vy, s2.Bodies.Velocity(s2.Dog(1)).Y);
            }
            Assert.True(vy < Fixed.One, $"a stunned dog is not driven: vy {vy}");
            for (int i = 0; i < 90 && s2.Hits.IsHelpless(1); i++) s2.Tick(1, push);
            s2.Tick(10, push);
            Assert.True(s2.Bodies.Velocity(s2.Dog(1)).Y > Fixed.One, "driven again once the stun is over and the slide has ended");
        }

        [Fact]
        public void OwnerNeverHitsHisOwnDog()
        {
            var s = Sandbox("teen");
            s.Put(s.Owner(0), "-2.5", "0"); s.Put(s.Dog(0), "0", "0"); s.Put(s.Dog(1), "5", "5"); s.Put(s.Owner(1), "5", "7");
            s.Shove.Velocities[s.Owner(0)] = new Vec2(F("8"), Fixed.Zero);
            bool touched = false;
            for (int i = 0; i < 8; i++) // the owner reaches his dog at 8 m/s
            {
                s.Tick();
                touched |= s.Bodies.Contacts.Any(c => !c.IsStatic && c.A == s.Dog(0) && c.B == s.Owner(0) || c.A == s.Owner(0) && c.B == s.Dog(0));
            }
            Assert.True(touched);
            Assert.Empty(s.Events("hit"));
            Assert.Empty(s.Events("strong_hit"));
            Assert.False(s.Hits.IsStunned(0));
        }

        [Fact]
        public void OwnerAgainstOwnerIsAShoveWithoutBlows()
        {
            var s = Sandbox("teen");
            s.Put(s.Owner(0), "-4", "0"); s.Put(s.Dog(0), "-4", "2.5");
            s.Put(s.Owner(1), "0", "0"); s.Put(s.Dog(1), "0", "2.5");
            var v = new Vec2(F("8"), Fixed.Zero);
            s.Shove.Velocities[s.Owner(0)] = v;
            s.Shove.Velocities[s.Dog(0)] = v;
            bool touched = false;
            for (int i = 0; i < 20; i++)
            {
                s.Tick();
                touched |= s.Bodies.Contacts.Any(c => !c.IsStatic && c.A == s.Owner(0) && c.B == s.Owner(1) || c.A == s.Owner(1) && c.B == s.Owner(0));
            }
            Assert.True(touched, "the owners met");
            Assert.True(s.Bodies.Position(s.Owner(1)).DistanceTo(P("0", "0")) >= F("0.5"), "the shoved owner really moved");
            Assert.Empty(s.Events("hit"));
            Assert.Empty(s.Events("strong_hit"));
            Assert.Empty(s.Events("near_miss"));
            Assert.False(s.Hits.IsStunned(0) || s.Hits.IsStunned(1));
        }

        [Fact]
        public void ADogRammingAStandingForeignOwnerIsHitByHim()
        {
            // the rule is read literally: a touch at ≥ 5 m/s is the owner's blow, whichever of the two moved
            var s = Sandbox("teen");
            s.Put(s.Owner(1), "0", "0"); s.Put(s.Dog(1), "0", "2");
            s.Put(s.Owner(0), "-4", "-2"); s.Put(s.Dog(0), "-3", "0");
            var v = new Vec2(F("7"), Fixed.Zero);
            s.Shove.Velocities[s.Dog(0)] = v; // his owner keeps pace beside her, so the leash does not hold her back
            s.Shove.Velocities[s.Owner(0)] = v;
            s.Tick(25);
            var hit = s.Events("hit").Single(e => e.Actor == 1);
            Assert.True(hit.Reward > Fixed.Zero);
            Assert.True(s.Events("hit").Single(e => e.Actor == 0).Threat > Fixed.Zero, "her dog is the victim");
        }

        [Fact]
        public void OwnerFlyingPastAForeignDogWithoutTouchingIsANearMiss()
        {
            var s = Swing("teen", "7", "1.3"); // passes 1.3 m from the dog's centre: a 0.45 m gap
            Assert.Empty(s.Events("hit"));
            var miss = s.Events("near_miss");
            Assert.Single(miss);
            Assert.Equal(1, miss[0].Actor);
            Assert.True(miss[0].Threat > Fixed.Zero && miss[0].Reward == null);
            Assert.True(miss[0].Threat <= F("0.5"));
        }

        [Fact]
        public void SlowOrFarOwnersAreNotNearMisses()
        {
            Assert.Empty(Swing("teen", "3", "1.3").Events("near_miss"));
            Assert.Empty(Swing("teen", "7", "2.2").Events("near_miss"));
        }

        [Fact]
        public void SameSeedAndScriptGiveTheSameHashAndForkIsIndependent()
        {
            var a = Swing("teen", "7");
            var b = Swing("teen", "7");
            Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
            var fork = a.Sim.Fork();
            Assert.Equal(a.Sim.StateHash(), fork.StateHash());
            var before = a.Sim.StateHash();
            fork.Step(TickInputs.Empty(fork.Players));
            Assert.NotEqual(before, fork.StateHash());
            Assert.Equal(before, a.Sim.StateHash());
        }

        [Fact]
        public void ASecondPlayersOwnerCanHitADogStillSlidingFromAnotherBlow()
        {
            var s = Swing("teen", "6");
            Assert.True(s.Hits.IsHelpless(1), "the dog slides from the first blow");
            var dogAt = s.Bodies.Position(s.Dog(1));
            var from = dogAt + P("0", "-1.2");
            s.Put(s.Owner(2), from.X.ToString(), from.Y.ToString());
            s.Put(s.Dog(2), from.X.ToString(), (from.Y - F("2.5")).ToString());
            var v = new Vec2(Fixed.Zero, F("8"));
            s.Shove.Velocities[s.Owner(2)] = v;
            s.Shove.Velocities[s.Dog(2)] = v;
            s.Tick(8);
            Assert.Contains(s.Events("hit"), e => e.Actor == 2);
            Assert.Contains(s.Events("hit"), e => e.Actor == 1 && e.Threat > Fixed.Zero);
        }

        [Fact]
        public void AHitAtTheThresholdStillCarriesAPositiveReward()
        {
            var s = Swing("teen", "5.5");
            Assert.True(s.Events("hit").Single(e => e.Actor == 0).Reward >= F("0.2"));
        }
    }
}
