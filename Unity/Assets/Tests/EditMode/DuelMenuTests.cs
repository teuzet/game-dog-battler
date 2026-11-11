using System.Collections.Generic;
using System.IO;
using DogBattler.View;
using Factory.Shell;
using Game.Core;
using Game.Core.Data;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DogBattler.Tests
{
    public class DuelMenuTests
    {
        private static GameData Base() =>
            GameData.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game.Core", "Data")));

        private static MatchSetup DuelBotVsBot(GameData data)
        {
            var s = MatchSetup.Defaults(data, "duel");
            s.BotVsBot = true;
            s.Bot0 = "smart";
            s.Opponent = "smart";
            return s;
        }

        [Test]
        public void The_menu_lists_the_arenas_by_title_and_the_bots_without_breakers()
        {
            var data = Base();
            var arenas = MatchSetup.Arenas(data);
            Assert.Contains(new KeyValuePair<string, string>("duel", "Дуэль"), arenas);
            Assert.IsTrue(arenas.Exists(a => a.Key == "park"));
            var bots = MatchSetup.Archetypes(data);
            CollectionAssert.AreEqual(new[] { "idle", "random", "smart", "camper" }, bots);
        }

        [Test]
        public void Defaults_take_the_first_opponent_of_the_arena_rules()
        {
            var data = Base();
            Assert.AreEqual("smart", MatchSetup.Defaults(data, "duel").Opponent);
            Assert.AreEqual("smart", MatchSetup.Defaults(data, "park").Opponent);
            Assert.AreEqual("idle", MatchSetup.Defaults(data, "sandbox").Opponent);
        }

        [Test]
        public void Bot_vs_bot_on_the_duel_gives_two_participants_and_two_drivers()
        {
            var data = Base();
            var setup = DuelBotVsBot(data);
            var built = setup.Data(data, d => d);
            var sim = GameSetup.Game.Create(1, built);
            Assert.AreEqual(2, sim.Players);
            Assert.AreEqual("Duel", sim.Get<RulesModule>(RulesModule.ModuleName).Active.Name);
            var bots = setup.Drivers(GameSetup.Game, built, sim.Players, 1);
            Assert.AreEqual(2, bots.Count);
            Assert.AreEqual(0, bots[0].Player);
            Assert.AreEqual("smart", bots[0].Archetype.Name);
            Assert.AreEqual(1, bots[1].Player);
            Assert.AreEqual("smart", bots[1].Archetype.Name);
        }

        [Test]
        public void A_human_game_has_no_driver_for_participant_0_and_the_picked_opponent_replaces_the_first()
        {
            var data = Base();
            var setup = MatchSetup.Defaults(data, "park");
            setup.Opponent = "camper";
            var built = setup.Data(data, d => d);
            var sim = GameSetup.Game.Create(1, built);
            var bots = setup.Drivers(GameSetup.Game, built, sim.Players, 1);
            Assert.AreEqual(sim.Players - 1, bots.Count);
            Assert.AreEqual(1, bots[0].Player);
            Assert.AreEqual("camper", bots[0].Archetype.Name);
            Assert.AreEqual("random", bots[1].Archetype.Name, "the rest keep the arena's list");
        }

        [Test]
        public void A_scripted_bot_duel_runs_to_the_first_knockout_and_the_hud_shows_the_victim_empty_and_1_0()
        {
            var data = Base();
            var setup = DuelBotVsBot(data);
            for (int seed = 1; seed <= 6; seed++)
            {
                var built = setup.Data(data, d => d);
                var sim = GameSetup.Game.Create(seed, built);
                var bots = setup.Drivers(GameSetup.Game, built, sim.Players, seed);
                var hud = new DuelHudModel();
                hud.Take(sim);
                Assert.AreEqual(2, hud.Hp.Length);
                bool knocked = false;
                while (!sim.IsOver && !knocked)
                {
                    sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
                    hud.Take(sim);
                    knocked = hud.KnockoutTick >= 0;
                }
                if (!knocked) continue;
                int winner = hud.KnockedOutBy, victim = 1 - winner;
                Assert.AreEqual(0f, hud.Hp[victim], 1e-4f, "the victim's bar is empty");
                Assert.AreEqual(1, hud.Wins[winner]);
                Assert.AreEqual(0, hud.Wins[victim]);
                Assert.AreEqual(winner == 0 ? "1 : 0" : "0 : 1", hud.Score());
                Assert.IsTrue(hud.Pause);
                Assert.AreEqual("Нокаут!", hud.Banner());
                return;
            }
            Assert.Fail("no knockout in six bot-vs-bot duels");
        }

        [Test]
        public void The_banner_names_the_next_round_on_a_pause_and_the_result_reads_from_the_viewed_side()
        {
            var data = Base();
            var setup = DuelBotVsBot(data);
            var built = setup.Data(data, d => d);
            var sim = GameSetup.Game.Create(3, built);
            var bots = setup.Drivers(GameSetup.Game, built, sim.Players, 3);
            var hud = new DuelHudModel();
            hud.Take(sim);
            int roundS = data.Int("rules.duel.round_s");
            bool paused = false;
            for (int t = 0; t < (roundS + 2) * Simulation.TicksPerSecond && !sim.IsOver; t++)
            {
                sim.Step(Forks.Inputs(GameSetup.Game, sim, bots, -1, PlayerInput.None));
                hud.Take(sim);
                if (hud.Pause && hud.Banner() != "Нокаут!") { paused = true; break; }
            }
            Assert.IsTrue(paused, "round 1 ended within round_s");
            Assert.AreEqual(1, hud.Round);
            Assert.AreEqual("Раунд 2", hud.Banner());
            hud.Wins = new[] { 3, 1 };
            var win = new SessionResult(new[] { Outcome.Win, Outcome.Loss }, Game.Core.Math.Fixed.Zero, "");
            hud.Over = true;
            Assert.AreEqual("Победа 3:1", hud.Result(win, 0));
            Assert.AreEqual("Поражение 1:3", hud.Result(win, 1));
            Assert.AreEqual("Красный победил", hud.Result(win, -1));
            Assert.AreEqual("", hud.Banner());
        }

        [Test]
        public void Duel_rules_pick_the_duel_panel_and_the_park_does_not()
        {
            var data = Base();
            Assert.IsTrue(DuelHudModel.Applies(GameSetup.Game.Create(1, data.WithArena("duel"))));
            Assert.IsFalse(DuelHudModel.Applies(GameSetup.Game.Create(1, data.WithArena("park"))));
        }
    }
}
