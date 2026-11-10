using System.IO;
using DogBattler.View;
using Factory.Shell;
using Game.Core;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Match;
using Game.Core.Modules.Owners;
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
            var events = new System.Collections.Generic.List<GameEvent>
            {
                new GameEvent { Kind = "knocked_out", Actor = 1, Reward = Fixed.Ratio(5, 8) },
                new GameEvent { Kind = "knocked_out", Actor = 2, Reward = Fixed.Ratio(8, 8) },
                new GameEvent { Kind = "knocked_out", Actor = 3, Threat = Fixed.One }, // a victim's twin: no popup
            };
            // player 0 gets a circle second, 1 a knockout (+5) with a circle second, 2 the leader bonus (+8)
            var popups = HudMath.Popups(new[] { 10, 4, 3, 0 }, new[] { 11, 10, 11, 0 }, events, 5, 8);
            Assert.AreEqual(4, popups.Count);
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 1 && p.Kind == PopupKind.Knockout && p.Points == 5));
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 2 && p.Kind == PopupKind.LeaderKnockout && p.Points == 8));
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 0 && p.Kind == PopupKind.Zone && p.Points == 1));
            Assert.That(popups, Has.Some.Matches<Popup>(p => p.Player == 1 && p.Kind == PopupKind.Zone && p.Points == 1));
            Assert.AreEqual("+1 в круге", HudMath.PopupText(new Popup(0, 1, PopupKind.Zone)));
            Assert.AreEqual("ВЫБИЛ! +5", HudMath.PopupText(new Popup(1, 5, PopupKind.Knockout)));
            Assert.AreEqual("ВЫБИЛ ЛИДЕРА! +8", HudMath.PopupText(new Popup(2, 8, PopupKind.LeaderKnockout)));
        }

        [Test]
        public void A_knockout_in_the_simulation_pops_up_at_the_attacker()
        {
            var sim = NewSim();
            var hud = NewHud(sim);
            sim.Step(new TickInputs(sim.Players));
            var match = sim.Get<MatchModule>(MatchModule.ModuleName);
            match.Award(2, 5, "knockout"); // what KingOfLawn does for a knockout
            var e = sim.Events.Add("knocked_out", 2, "test");
            e.Reward = Fixed.Ratio(5, 8);
            hud.Take(sim);
            var popups = hud.ActivePopups;
            Assert.AreEqual(1, popups.Count);
            Assert.AreEqual(2, popups[0].Player);
            Assert.AreEqual(PopupKind.Knockout, popups[0].Kind);
            Assert.AreEqual(5, popups[0].Points);
            sim.Step(new TickInputs(sim.Players)); // the next tick: same scores, the old event is not read again
            hud.Take(sim);
            Assert.AreEqual(1, hud.ActivePopups.Count);
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
            var jock = NewSim(FactoryGame.WithOwner(data, "jock"));
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
