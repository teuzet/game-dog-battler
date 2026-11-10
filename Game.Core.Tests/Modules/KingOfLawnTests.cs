using System.Collections.Generic;
using System.Linq;
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
    public class KingOfLawnTests
    {
        private static Fixed F(string s) => Fixed.Parse(s);
        private static Vec2 P(string x, string y) => new Vec2(F(x), F(y));

        // The rules' numbers as the data has them (the playtest tunes them in Data/rules/king_of_lawn.json).
        private static int Ko => Repo.Data().Int("rules.king_of_lawn.knockout_points");
        private static int LeaderKo => Repo.Data().Int("rules.king_of_lawn.leader_knockout_points");
        private static int PeriodTicks => Repo.Data().Int("rules.king_of_lawn.period_s") * Simulation.TicksPerSecond;
        private static int AnnounceTicks => Repo.Data().Int("rules.king_of_lawn.announce_s") * Simulation.TicksPerSecond;

        /// <summary>Puts blows into the list of Hits between Hits and Rules (a blow is born in the tick it is recorded).</summary>
        private sealed class ScriptedHits : IModule
        {
            public string Name => "ScriptedHits";
            public IReadOnlyList<string> DependsOn { get; } = new[] { HitsModule.ModuleName };
            public readonly Dictionary<int, Hit> Blows = new Dictionary<int, Hit>(); // tick → blow
            public void Init(Simulation sim) { }
            public void Step(Simulation sim, TickInputs inputs)
            {
                if (Blows.TryGetValue(sim.Tick, out var h)) sim.Need<HitsModule>(this, HitsModule.ModuleName).Record(h);
            }
            public void Hash(ref StateHasher h) { }
            public IModule Clone() => this;
        }

        private sealed class Scene
        {
            public Simulation Sim = null!;
            public ScriptedHits Hits = null!;
            public BodiesModule Bodies = null!;
            public DogsModule Dogs = null!;
            public OwnersModule Owners = null!;
            public MatchModule Match = null!;
            public RulesModule Rules = null!;

            /// <summary>Dog and owner of a player at rest; the owner stands 2 m to the left, inside the leash.</summary>
            public void Place(int p, string x, string y)
            {
                var pos = P(x, y);
                Bodies.Teleport(Dogs.BodyOf(p), pos);
                Bodies.Teleport(Owners.BodyOf(p), pos + P("-2", "0"));
            }

            /// <summary>Everyone's cursor is on his own dog: nobody moves of his own will.</summary>
            public void Tick(int n = 1)
            {
                for (int i = 0; i < n; i++)
                {
                    var inputs = new TickInputs(Sim.Players);
                    for (int p = 0; p < Sim.Players; p++)
                    {
                        var at = Dogs.Position(Sim, p);
                        inputs[p] = new PlayerInput((at.X * 100).FloorToInt(), (at.Y * 100).FloorToInt());
                    }
                    Sim.Step(inputs);
                }
            }

            public void RunTo(int tick) => Tick(tick - Sim.Tick);

            public List<GameEvent> Events(string kind) => Sim.Events.All.Where(e => e.Kind == kind).ToList();
        }

        /// <summary>The game's own modules (same order as GameSetup) with the scripted blows before Rules; park arena.</summary>
        private static Scene Park(int seed = 1, string rent = "each", string pointsPerS = null!)
        {
            // the scoring tests below state the old rule (each dog inside gets the whole rent); the rent tests choose their own
            var over = "{\"rules\":{\"king_of_lawn\":{\"rent\":\"" + rent + "\"" + (pointsPerS == null ? "" : ",\"points_per_s\":" + pointsPerS) + "}}}";
            var data = new GameData(Repo.Data().Root.MergedWith(Json.Parse(over)));
            int players = data.Int("game.players");
            var hits = new ScriptedHits();
            var modules = new List<IModule>
            {
                new ArenaModule(data), new ControlModule(data, players), new OwnersModule(data, players), new DogsModule(data, players),
                new PropsModule(), new LeashesModule(data, players), new BodiesModule(data), new HitsModule(data, players),
                hits, new RulesModule(data, players), new MatchModule(data, players),
            };
            var sim = new Simulation(seed, players, data, modules);
            var s = new Scene
            {
                Sim = sim, Hits = hits,
                Bodies = sim.Get<BodiesModule>("Bodies"), Dogs = sim.Get<DogsModule>("Dogs"), Owners = sim.Get<OwnersModule>("Owners"),
                Match = sim.Get<MatchModule>("Match"), Rules = sim.Get<RulesModule>("Rules"),
            };
            // the four spawns are away from the lawn; park everyone far from the first circle (0, 0)
            s.Place(0, "-9", "-5"); s.Place(1, "9", "-5"); s.Place(2, "9", "5"); s.Place(3, "-9", "5");
            return s;
        }

        [Fact]
        public void ParkLayoutIsInData()
        {
            var arena = Park().Sim.Get<ArenaModule>("Arena");
            Assert.Equal("KingOfLawn", arena.RulesName);
            Assert.Equal(Fixed.FromInt(24), arena.Width);
            Assert.Equal(Fixed.FromInt(16), arena.Height);
            Assert.Equal(4, arena.CircleObstacles.Count); // trees
            Assert.Equal(3, arena.RectObstacles.Count);   // benches
            Assert.Equal(4, arena.Spawns.Count);
            Assert.Empty(arena.Props); // no cat in sprint 1
        }

        [Fact]
        public void DogsInTheCircleEarnAPointASecondEach()
        {
            var s = Park();
            s.Place(0, "0", "0");
            s.Place(1, "3.4", "0");   // inside the 3.5 m radius
            s.Place(2, "0", "3.7");   // outside
            s.RunTo(29);
            Assert.All(s.Match.Scores, x => Assert.Equal(0, x));
            s.RunTo(30);
            Assert.Equal(new[] { 1, 1, 0, 0 }, s.Match.Scores);
            s.RunTo(90);
            Assert.Equal(new[] { 3, 3, 0, 0 }, s.Match.Scores);
        }

        [Fact]
        public void DataDefaultRentIsSplitAndVariantsOfSprint1KeepEach()
        {
            Assert.Equal("split", Repo.Data().Str("rules.king_of_lawn.rent"));
            foreach (var name in new[] { "A", "B" })
                Assert.Equal("each", Repo.Data().WithVariant(Repo.DataDir, "sprint-1", name).Str("rules.king_of_lawn.rent"));
        }

        [Fact]
        public void UnknownRentModeThrows()
        {
            Assert.ThrowsAny<System.Exception>(() => Park(1, "bogus"));
        }

        [Fact]
        public void SplitRentIsSharedEquallyAmongDogsInside()
        {
            var s = Park(1, "split");
            s.Place(0, "0", "0");
            s.Place(1, "1", "0");
            s.RunTo(120); // 4 s, two inside: half a point a second each
            Assert.Equal(new[] { 2, 2, 0, 0 }, s.Match.Scores);
            s.Place(1, "9", "-5");
            s.RunTo(240); // 4 s alone: the whole rent
            Assert.Equal(new[] { 6, 2, 0, 0 }, s.Match.Scores);
            Assert.All(s.Match.LastTickAwards.Concat(new PointsAward[0]), a => Assert.Equal("zone", a.Reason));
        }

        [Fact]
        public void SoleRentPaysOnlyADogAloneAndTheSurvivorOfAKnockoutEarns()
        {
            var s = Park(1, "sole");
            s.Place(0, "-5", "0");
            s.Place(1, "0", "0");
            s.Place(2, "1", "1");
            s.Hits.Blows[101] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.RunTo(90); // 3 s with two inside: nothing
            Assert.All(s.Match.Scores, x => Assert.Equal(0, x));
            s.RunTo(105);
            s.Place(1, "6", "0"); // knocked out: Ko for the attacker, player 2 is left alone in the circle
            s.RunTo(190); // alone at 120, 150 and 180 s-ticks
            Assert.Equal(new[] { Ko, 0, 3, 0 }, s.Match.Scores);
        }

        [Fact]
        public void FractionalPointsAccrueUntilWhole()
        {
            var s = Park(1, "each", "0.5");
            s.Place(0, "0", "0");
            s.RunTo(30);
            Assert.Equal(0, s.Match.Scores[0]);
            Assert.Empty(s.Match.LastTickAwards);
            s.RunTo(60);
            Assert.Equal(1, s.Match.Scores[0]);
            var award = Assert.Single(s.Match.LastTickAwards);
            Assert.Equal("zone", award.Reason);
            s.RunTo(120);
            Assert.Equal(2, s.Match.Scores[0]);
        }

        [Fact]
        public void RentAccumulatorIsInTheHashAndTheFork()
        {
            Scene Run() { var s = Park(3, "split"); s.Place(0, "0", "0"); s.Place(1, "1", "0"); s.RunTo(45); return s; }
            var a = Run();
            Assert.Equal(Run().Sim.StateHash(), a.Sim.StateHash());
            var fork = a.Sim.Fork();
            Assert.Equal(a.Sim.StateHash(), fork.StateHash());
            a.Tick(15); // the original goes on: a second half point at 60 pays out a whole one
            Assert.Equal(new[] { 1, 1, 0, 0 }, a.Match.Scores);
            Assert.Equal(new[] { 0, 0, 0, 0 }, fork.Get<MatchModule>("Match").Scores);
            Assert.NotEqual(a.Sim.StateHash(), fork.StateHash());
        }

        [Fact]
        public void BlowThatPutsADogOutOfTheCircleWithinASecondIsAKnockout()
        {
            var s = Park();
            s.Place(0, "-5", "0");   // the attacker stands outside
            s.Place(1, "0", "0");    // the victim is inside
            s.RunTo(10);
            s.Hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.RunTo(20);
            Assert.Equal(0, s.Match.Scores[0]);
            s.Place(1, "5", "0");    // thrown out 0.3 s after the blow
            s.Tick();
            Assert.Equal(new[] { Ko, 0, 0, 0 }, s.Match.Scores);
            var ko = s.Events("knocked_out");
            Assert.Equal(2, ko.Count);
            Assert.Equal(0, ko[0].Actor);
            Assert.True(ko[0].Reward > Fixed.Zero);
            Assert.Equal(1, ko[1].Actor);
            Assert.True(ko[1].Threat > Fixed.Zero);
            s.Tick(60); // no second award for the same blow
            Assert.Equal(Ko, s.Match.Scores[0]);
        }

        [Fact]
        public void LeavingLaterThanASecondAfterTheBlowEarnsNothing()
        {
            var s = Park();
            s.Place(0, "-5", "0");
            s.Place(1, "0", "0");
            s.RunTo(10);
            s.Hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.RunTo(11 + 31);
            s.Place(1, "5", "0");
            s.Tick(5);
            Assert.Equal(0, s.Match.Scores[0]);
            Assert.Empty(s.Events("knocked_out"));
        }

        [Fact]
        public void BlowOnADogOutsideTheCircleIsNotAKnockout()
        {
            var s = Park();
            s.Place(0, "-5", "0");
            s.Place(1, "5", "0");
            s.RunTo(10);
            s.Hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.Tick(60);
            Assert.Equal(0, s.Match.Scores[0]);
        }

        [Fact]
        public void BlowByAnObjectOfTheArenaAwardsNothing()
        {
            var s = Park();
            s.Place(1, "0", "0");
            s.RunTo(10);
            s.Hits.Blows[11] = new Hit(-1, 1, Fixed.FromInt(100), true);
            s.RunTo(12);
            s.Place(1, "5", "0");
            s.Tick(3);
            Assert.All(s.Match.Scores, x => Assert.Equal(0, x));
        }

        [Fact]
        public void KnockingOutTheSoleLeaderIsWorthTheLeaderPrize()
        {
            var s = Park();
            s.Place(0, "-5", "0");
            s.Place(1, "0", "0");
            s.Match.Award(1, 3, "test");  // player 1 leads alone
            s.Match.Award(2, 2, "test");
            s.RunTo(10);
            s.Hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.RunTo(15);
            s.Place(1, "5", "0");
            s.Tick();
            Assert.True(LeaderKo > Ko);
            Assert.Equal(LeaderKo, s.Match.Scores[0]);
        }

        [Fact]
        public void MatchKeepsTheAwardsOfTheLastTickWithTheirReason()
        {
            foreach (bool leader in new[] { false, true })
            {
                var s = Park();
                s.Place(0, "-5", "0");
                s.Place(1, "0", "0");
                if (leader) { s.Match.Award(1, 3, "test"); s.Match.Award(2, 2, "test"); }
                s.RunTo(10);
                s.Hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
                s.RunTo(15);
                s.Place(1, "5", "0");
                s.Tick();
                var koAwards = s.Match.LastTickAwards.Where(a => a.Reason != "zone").ToList();
                var ko = Assert.Single(koAwards);
                Assert.Equal(0, ko.Player);
                Assert.Equal(leader ? LeaderKo : Ko, ko.Points);
                Assert.Equal(leader ? "leader_knockout" : "knockout", ko.Reason);
                s.Tick();
                Assert.DoesNotContain(s.Match.LastTickAwards, a => a.Reason.EndsWith("knockout"));
            }
        }

        [Fact]
        public void TiedForTheLeadIsNotALeader()
        {
            var s = Park();
            s.Place(0, "-5", "0");
            s.Place(1, "0", "0");
            s.Match.Award(1, 3, "test");
            s.Match.Award(2, 3, "test");
            s.RunTo(10);
            s.Hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.RunTo(15);
            s.Place(1, "5", "0");
            s.Tick();
            Assert.Equal(Ko, s.Match.Scores[0]);
        }

        [Fact]
        public void CircleIsAnnouncedAheadAndMovesEveryPeriod()
        {
            var s = Park();
            int period = PeriodTicks, announce = AnnounceTicks;
            var first = s.Rules.Active.Zone!.Value;
            Assert.Equal(Vec2.Zero, first.Center);
            Assert.Equal(F("3.5"), first.Radius);
            s.RunTo(period - announce - 1);
            Assert.Empty(s.Events("zone_announced"));
            Assert.Null(s.Rules.Active.NextZone);
            s.RunTo(period - announce);
            var ann = Assert.Single(s.Events("zone_announced"));
            Assert.Equal(2, ann.DecisionOptions);
            Assert.Equal("zone_announced", ann.Novel);
            var next = s.Rules.Active.NextZone!.Value;
            Assert.Equal(first.Center, s.Rules.Active.Zone!.Value.Center); // still here until the period ends
            s.RunTo(period - 1);
            Assert.Equal(first.Center, s.Rules.Active.Zone!.Value.Center);
            s.RunTo(period);
            Assert.Equal(next.Center, s.Rules.Active.Zone!.Value.Center);
            Assert.Null(s.Rules.Active.NextZone);
            s.RunTo(2 * period - announce);
            Assert.Equal(2, s.Events("zone_announced").Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void MovesAre8To14MetresInsideTheArenaAndOffObstacles(int seed)
        {
            var s = Park(seed);
            var arena = s.Sim.Get<ArenaModule>("Arena");
            var prev = s.Rules.Active.Zone!.Value;
            for (int move = 1; move <= 5; move++)
            {
                s.RunTo(move * PeriodTicks);
                var z = s.Rules.Active.Zone!.Value;
                var d = z.Center.DistanceTo(prev.Center);
                Assert.InRange(d.ToDouble(), 8, 14);
                Assert.True(z.Center.X.Abs() + z.Radius <= arena.Width / 2, $"x {z.Center}");
                Assert.True(z.Center.Y.Abs() + z.Radius <= arena.Height / 2, $"y {z.Center}");
                foreach (var o in arena.CircleObstacles) Assert.True(z.Center.DistanceTo(o.Pos) > o.Radius, $"tree {o.Pos} vs {z.Center}");
                foreach (var r in arena.RectObstacles)
                    Assert.False((z.Center.X - r.Center.X).Abs() < r.HalfWidth && (z.Center.Y - r.Center.Y).Abs() < r.HalfHeight, $"bench {r.Center} vs {z.Center}");
                prev = z;
            }
        }

        [Fact]
        public void ZoneMovesDifferWithTheSeedAndRepeatWithTheSame()
        {
            Vec2 At(int seed) { var s = Park(seed); s.RunTo(600); return s.Rules.Active.Zone!.Value.Center; }
            Assert.Equal(At(7), At(7));
            Assert.NotEqual(At(7), At(8));
        }

        [Fact]
        public void MatchEndsAtTwoMinutesWithWinnerAndMargin()
        {
            var s = Park();
            s.Match.Award(2, 1000, "test");  // far above what the circle can give in two minutes
            s.Match.Award(1, 600, "test");
            s.RunTo(3299);
            Assert.Empty(s.Events("final_seconds"));
            s.RunTo(3300);
            var fin = s.Events("final_seconds");
            Assert.Equal(4, fin.Count);
            Assert.All(fin, e => Assert.InRange(e.Threat!.Value.ToDouble(), 0, 1));
            Assert.True(fin.Single(e => e.Actor == 3).Threat! > fin.Single(e => e.Actor == 1).Threat!); // trailing more
            Assert.False(s.Sim.IsOver);
            s.RunTo(3600);
            Assert.True(s.Sim.IsOver);
            var r = s.Sim.Result!;
            Assert.Equal(Outcome.Win, r.Outcomes[2]);
            Assert.Equal(Outcome.Loss, r.Outcomes[1]);
            Assert.Equal(Outcome.Loss, r.Outcomes[0]);
            Assert.InRange(r.Margin.ToDouble(), 0.3, 0.5);
            Assert.Contains("участник 2", r.Why);
        }

        [Fact]
        public void LeadChangeGivesRewardToTheNewLeaderAndThreatToTheOldOne()
        {
            var s = Park();
            s.Place(0, "0", "0");
            s.RunTo(30);
            var first = s.Events("lead_change");
            Assert.Single(first);
            Assert.Equal(0, first[0].Actor);
            Assert.NotNull(first[0].Reward);
            s.Match.Award(1, 5, "test");
            s.Tick();
            var all = s.Events("lead_change");
            Assert.Equal(3, all.Count);
            Assert.Equal(1, all[1].Actor);
            Assert.NotNull(all[1].Reward);
            Assert.Equal(0, all[2].Actor);
            Assert.NotNull(all[2].Threat);
        }

        [Fact]
        public void MetricsReportPointsFightShareHitsAndEqualStretch()
        {
            var s = Park();
            var game = GameSetup.Game;
            s.Place(0, "-5", "0");
            s.Place(1, "0", "0");
            s.Place(2, "1", "1");
            s.Hits.Blows[301] = new Hit(0, 1, Fixed.FromInt(100), false); // the blow at 10 s
            s.RunTo(300);
            // 10 s with two dogs in: an equal stretch of 10 seconds
            Assert.Equal(10, game.Metrics(s.Sim, 1)["longest_equal_stretch_s"]);
            s.RunTo(305);
            s.Place(1, "6", "0");
            s.Tick();
            var m0 = game.Metrics(s.Sim, 0);
            Assert.Equal(Ko, m0["points"]);
            Assert.Equal(Ko, m0["knockout_points"]);
            Assert.Equal(1.0, m0["fight_share"]);
            Assert.Equal(1 / (s.Sim.Seconds / 60.0), m0["hits_per_min"], 6);
            var m2 = game.Metrics(s.Sim, 2);
            Assert.Equal(10, m2["points"]);
            Assert.Equal(0.0, m2["fight_share"]);
            Assert.Equal(10, m2["longest_equal_stretch_s"]);
        }

        [Fact]
        public void KnockoutBreaksTheEqualStretch()
        {
            var s = Park();
            s.Place(1, "0", "0");
            s.Place(2, "1", "1");
            s.Place(0, "-5", "0");
            s.Hits.Blows[121] = new Hit(0, 1, Fixed.FromInt(100), false);
            s.RunTo(125);
            s.Place(1, "6", "0");
            s.Tick(31 * 6); // 6 more seconds of player 2 alone
            // 4 s before the knockout and 6 s after it: had it not broken the stretch, it would be 10
            Assert.Equal(6, GameSetup.Game.Metrics(s.Sim, 2)["longest_equal_stretch_s"]);
        }

        [Fact]
        public void DecisionOptionsOnAnnouncementAreRunToTheNextCircleOrFight()
        {
            var s = Park();
            var game = GameSetup.Game;
            s.RunTo(PeriodTicks - AnnounceTicks - 1);
            Assert.Empty(game.DecisionOptions(s.Sim, 0)); // nothing announced, nobody near
            s.RunTo(PeriodTicks - AnnounceTicks);
            var opts = game.DecisionOptions(s.Sim, 0);
            Assert.Equal(2, opts.Count);
            var next = s.Rules.Active.NextZone!.Value.Center;
            Assert.Equal((next.X * 100).FloorToInt(), opts[0].AimX);
            Assert.Equal((next.Y * 100).FloorToInt(), opts[0].AimY);
            s.RunTo(PeriodTicks - AnnounceTicks + Simulation.TicksPerSecond + 1); // the moment (decision_window_s) has passed
            Assert.Empty(game.DecisionOptions(s.Sim, 0));
        }

        [Fact]
        public void DecisionOptionsWhenARivalIsWithinLeashAreSwingOrRetreat()
        {
            var s = Park();
            var game = GameSetup.Game;
            s.Place(0, "-5", "0");
            s.Place(1, "-3", "0");  // 2 m < the collie's 3 m leash
            s.Tick();
            var opts = game.DecisionOptions(s.Sim, 0);
            Assert.Equal(2, opts.Count);
            Assert.Equal(-300, opts[0].AimX); // swing at the rival
            Assert.True(opts[1].AimX < -500);  // retreat away from him
            Assert.Empty(game.DecisionOptions(s.Sim, 2));
        }

        [Fact]
        public void BotsSeeTheAnnouncedCircle()
        {
            var s = Park();
            s.RunTo(PeriodTicks - AnnounceTicks);
            var view = GameSetup.Game.Sense(s.Sim, 0);
            Assert.Contains(view.Objects, o => o.Kind == "zone");
            Assert.Contains(view.Objects, o => o.Kind == "next_zone");
        }

        [Fact]
        public void ForkKeepsTheRulesIndependent()
        {
            var s = Park();
            s.Place(0, "0", "0");
            s.RunTo(100);
            var fork = s.Sim.Fork();
            s.Tick(600);
            Assert.NotEqual(s.Sim.StateHash(), fork.StateHash());
            Assert.Equal(Vec2.Zero, fork.Get<RulesModule>("Rules").Active.Zone!.Value.Center);
        }
    }
}
