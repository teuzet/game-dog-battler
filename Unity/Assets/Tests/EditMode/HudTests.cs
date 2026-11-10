using System.Collections.Generic;
using System.IO;
using DogBattler.View;
using Factory.Shell;
using Game.Core;
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
using NUnit.Framework;
using UnityEngine;

namespace DogBattler.Tests
{
    public class HudTests
    {
        private static string DataDir() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game.Core", "Data"));

        private static Simulation NewSim(GameData data = null) => GameSetup.Game.Create(1, data ?? GameData.Load(DataDir()));

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private HudView NewHud(Simulation sim)
        {
            _go = new GameObject("hud");
            var hud = _go.AddComponent<HudView>();
            hud.Rebuild(sim);
            return hud;
        }

        [Test]
        public void The_final_seconds_are_highlighted_only_at_the_end()
        {
            var sim = NewSim();
            float final = sim.Data.Int("match.final_seconds");
            Assert.AreEqual(10f, final);
            Assert.IsFalse(HudMath.IsFinal(120f, final));
            Assert.IsFalse(HudMath.IsFinal(10.5f, final));
            Assert.IsTrue(HudMath.IsFinal(10f, final));
            Assert.IsTrue(HudMath.IsFinal(0.5f, final));
            Assert.IsFalse(HudMath.IsFinal(0f, final), "the match is over");
            Assert.AreEqual("2:00", HudMath.Clock(120f));
            Assert.AreEqual("0:07", HudMath.Clock(6.2f));
        }

        [Test]
        public void Popups_tell_circle_seconds_knockouts_and_the_leader_bonus_apart()
        {
            // player 0 gets a circle second, 1 a knockout with a circle second, 2 the leader bonus
            var awards = new[]
            {
                new PointsAward(0, 1, "zone"), new PointsAward(1, 5, "knockout"),
                new PointsAward(1, 1, "zone"), new PointsAward(2, 8, "leader_knockout"),
            };
            var popups = HudMath.Popups(awards);
            Assert.AreEqual(4, popups.Count);
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 1 && p.Kind == PopupKind.Knockout && p.Points == 5));
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 2 && p.Kind == PopupKind.LeaderKnockout && p.Points == 8));
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 0 && p.Kind == PopupKind.Zone && p.Points == 1));
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 1 && p.Kind == PopupKind.Zone && p.Points == 1));
            Assert.AreEqual("+1 в круге", HudMath.PopupText(new Popup(0, 1, PopupKind.Zone)));
            Assert.AreEqual("ВЫБИЛ! +5", HudMath.PopupText(new Popup(1, 5, PopupKind.Knockout)));
            Assert.AreEqual("ВЫБИЛ ЛИДЕРА! +8", HudMath.PopupText(new Popup(2, 8, PopupKind.LeaderKnockout)));
        }

        /// <summary>Puts blows into the list of Hits between Hits and Rules, like the core's own KingOfLawn tests.</summary>
        private sealed class ScriptedHits : IModule
        {
            public string Name => "ScriptedHits";
            public IReadOnlyList<string> DependsOn { get; } = new[] { HitsModule.ModuleName };
            public readonly Dictionary<int, Hit> Blows = new Dictionary<int, Hit>();
            public void Init(Simulation sim) { }
            public void Step(Simulation sim, TickInputs inputs)
            {
                if (Blows.TryGetValue(sim.Tick, out var h)) sim.Need<HitsModule>(this, HitsModule.ModuleName).Record(h);
            }
            public void Hash(ref StateHasher h) { }
            public IModule Clone() => this;
        }

        private static Simulation ParkWithBlows(out ScriptedHits hits)
        {
            var data = GameData.Load(DataDir());
            int players = data.Int("game.players");
            hits = new ScriptedHits();
            var modules = new List<IModule>
            {
                new ArenaModule(data), new ControlModule(data, players), new OwnersModule(data, players), new DogsModule(data, players),
                new PropsModule(), new LeashesModule(data, players), new BodiesModule(data), new HitsModule(data, players),
                hits, new RulesModule(data, players), new MatchModule(data, players),
            };
            return new Simulation(1, players, data, modules);
        }

        private static void Place(Simulation sim, int player, int xDm, int yDm)
        {
            var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
            var pos = new Vec2(Fixed.Ratio(xDm, 10), Fixed.Ratio(yDm, 10));
            bodies.Teleport(sim.Get<DogsModule>(DogsModule.ModuleName).BodyOf(player), pos);
            bodies.Teleport(sim.Get<OwnersModule>(OwnersModule.ModuleName).BodyOf(player), pos + new Vec2(Fixed.FromInt(-2), Fixed.Zero));
        }

        /// <summary>One tick with every cursor on its own dog: nobody moves of his own will.</summary>
        private static void Tick(Simulation sim)
        {
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var inputs = new TickInputs(sim.Players);
            for (int p = 0; p < sim.Players; p++)
            {
                var at = dogs.Position(sim, p);
                inputs[p] = new PlayerInput((at.X * 100).FloorToInt(), (at.Y * 100).FloorToInt());
            }
            sim.Step(inputs);
        }

        private static void RunTo(Simulation sim, int tick) { while (sim.Tick < tick) Tick(sim); }

        /// <summary>A real knockout through the rules: a blow on a dog in the circle, then it is out within the window.
        /// With leaderFirst the victim leads the score alone, so the bonus is the leader's.</summary>
        private HudView KnockOut(bool leaderFirst, out Simulation sim)
        {
            sim = ParkWithBlows(out var hits);
            var hud = NewHud(sim);
            Place(sim, 0, -90, -50); Place(sim, 1, 90, -50); Place(sim, 2, 90, 50); Place(sim, 3, -90, 50);
            Place(sim, 0, -50, 0);  // the attacker stands outside the circle
            Place(sim, 1, 0, 0);    // the victim is inside it
            var match = sim.Get<MatchModule>(MatchModule.ModuleName);
            if (leaderFirst) { match.Award(1, 3, "test"); match.Award(2, 2, "test"); }
            RunTo(sim, 10);
            hits.Blows[11] = new Hit(0, 1, Fixed.FromInt(100), false);
            RunTo(sim, 15);
            Place(sim, 1, 50, 0);   // thrown out of the circle
            Tick(sim);
            hud.Take(sim);
            return hud;
        }

        [Test]
        public void A_real_knockout_pops_up_five_points_at_the_attacker()
        {
            var hud = KnockOut(false, out var sim);
            var pops = hud.ActivePopups;
            Assert.AreEqual(1, pops.Count);
            Assert.AreEqual(0, pops[0].Player);
            Assert.AreEqual(PopupKind.Knockout, pops[0].Kind);
            Assert.AreEqual(5, pops[0].Points);
            Assert.AreEqual(5, sim.Get<MatchModule>(MatchModule.ModuleName).Scores[0]);
            Tick(sim); // the next tick: the old award is not read again
            hud.Take(sim);
            Assert.AreEqual(1, hud.ActivePopups.Count);
        }

        [Test]
        public void A_real_knockout_of_the_leader_pops_up_the_bonus()
        {
            var hud = KnockOut(true, out var sim);
            var pops = hud.ActivePopups;
            Assert.AreEqual(1, pops.Count);
            Assert.AreEqual(0, pops[0].Player);
            Assert.AreEqual(PopupKind.LeaderKnockout, pops[0].Kind);
            Assert.AreEqual(8, pops[0].Points);
            Assert.AreEqual(8, sim.Get<MatchModule>(MatchModule.ModuleName).Scores[0]);
        }

        [Test]
        public void The_winner_screen_names_the_winner_the_human_or_a_draw()
        {
            var win = new SessionResult(new[] { Outcome.Loss, Outcome.Win, Outcome.Loss, Outcome.Loss }, Fixed.One, "");
            Assert.AreEqual("Победил синий", HudMath.WinnerTitle(win, 0));
            Assert.AreEqual("Ты победил!", HudMath.WinnerTitle(win, 1));
            Assert.AreEqual("Ничья", HudMath.WinnerTitle(SessionResult.Draw(4, ""), 0));
            Assert.AreEqual(1, HudMath.Leader(new[] { 3, 9, 4, 0 }));
            Assert.AreEqual(-1, HudMath.Leader(new[] { 9, 9, 4, 0 }));
            Assert.AreEqual(-1, HudMath.Leader(new[] { 0, 0, 0, 0 }));
        }

        [Test]
        public void The_match_ends_and_the_session_result_has_a_winner_to_show()
        {
            var sim = NewSim();
            var inputs = new TickInputs(sim.Players);
            sim.Get<MatchModule>(MatchModule.ModuleName).Award(3, 1000, "zone");
            while (!sim.IsOver) sim.Step(inputs);
            Assert.AreEqual("Победил фиолетовый", HudMath.WinnerTitle(sim.Result, 0));
        }

        [Test]
        public void The_next_circle_is_known_five_seconds_before_the_move()
        {
            var sim = NewSim();
            var rules = sim.Get<RulesModule>(RulesModule.ModuleName).Active;
            var inputs = new TickInputs(sim.Players);
            int period = sim.Data.Int("rules.king_of_lawn.period_s"), announce = sim.Data.Int("rules.king_of_lawn.announce_s");
            Assert.AreEqual(5, announce);
            Assert.IsNotNull(rules.Zone, "the circle is drawn from the start");
            while (sim.Tick < (period - announce) * Simulation.TicksPerSecond - 1) sim.Step(inputs);
            Assert.IsNull(rules.NextZone);
            sim.Step(inputs);
            Assert.IsNotNull(rules.NextZone);
        }

        [Test]
        public void The_pre_match_pick_sets_the_owner_and_the_mouse_mode()
        {
            var data = GameData.Load(DataDir());
            var choices = data.Get("game.human_choices").Items;
            Assert.AreEqual(2, choices.Count);
            Assert.AreEqual("teen", choices[0].AsString());
            Assert.AreEqual("jock", choices[1].AsString());
            var jock = NewSim(data.WithHumanOwner("jock"));
            Assert.AreEqual("jock", jock.Get<OwnersModule>(OwnersModule.ModuleName).TypeOf(0).Name);
            // the bots' owners come from Data/bots/session.json, not from the pick
            var picks = data.Get("bots.session.owner_picks").Items;
            for (int p = 1; p < 4; p++)
                Assert.AreEqual(picks[(p - 1) % picks.Count].AsString(), jock.Get<OwnersModule>(OwnersModule.ModuleName).TypeOf(p).Name);
            var mouse = new MouseInput();
            mouse.SetPush(false);
            Assert.IsFalse(mouse.Push);
            mouse.SetPush(true);
            Assert.IsTrue(mouse.Push);
            Assert.AreEqual(3, Archetype_Opponents(data));
        }

        private static int Archetype_Opponents(GameData data) => Game.Core.Bots.Archetype.Opponents(data).Count;
    }
}
