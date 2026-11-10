using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Modules.Match;
using Game.Core.Sim;
using Game.Core.Tests.Contract;
using Xunit;
using Xunit.Abstractions;

namespace Game.Core.Tests.Playtest
{
    /// <summary>F-010: the numbers of the design's hypotheses that the session runner does not report on its own
    /// (design/overview.md → «Гипотезы»): H2 — when the first blow of a random player lands; H4 — the winner's share
    /// of points from knockouts; H5 — how many matches end within 10% of points; and lead changes after the 60th
    /// second. Played on the runner's line-up (the measured archetype as player 0 against session.json opponents),
    /// without forks, so it is quick. Not a gate: runs only when the request file .factory/playtest-measure.json
    /// exists in the repository root ({"out": "path of the JSON to write", "variant": "set/name" or "", "seeds": 40,
    /// "owner": "jock" or "" for the measured player's owner type}),
    /// otherwise it passes without playing.</summary>
    public class PlaytestMeasureTests
    {
        private readonly ITestOutputHelper _out;

        public PlaytestMeasureTests(ITestOutputHelper output) { _out = output; }

        private static Simulation Play(IGame game, GameData data, string archetype, int seed)
        {
            var sim = game.Create(seed, data);
            var opponents = Archetype.Opponents(data);
            var bots = new List<BotDriver>();
            for (int p = 0; p < sim.Players; p++)
            {
                var a = Archetype.Find(data, p == 0 ? archetype : opponents[(p - 1) % opponents.Count]);
                bots.Add(new BotDriver(p, a, game.Strategy(a, data), (ulong)seed));
            }
            while (!sim.IsOver) sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
            return sim;
        }

        /// <summary>Seconds until player 0 lands his first blow (hit or strong_hit as the attacker), or -1.</summary>
        private static double FirstBlow(Simulation sim)
        {
            foreach (var e in sim.Events.All)
                if (e.Actor == 0 && (e.Kind == "hit" || e.Kind == "strong_hit") && e.Reward != null)
                    return e.Tick / (double)Simulation.TicksPerSecond;
            return -1;
        }

        [Fact]
        public void Measure()
        {
            var requestPath = Path.Combine(Repo.Root, ".factory", "playtest-measure.json");
            if (!File.Exists(requestPath)) return;
            var request = Json.Parse(File.ReadAllText(requestPath));
            var data = Repo.Data();
            var variant = request["variant"]?.AsString() ?? "";
            if (variant.Length > 0)
            {
                var parts = variant.Split('/');
                data = data.WithVariant(Repo.DataDir, parts[0], parts[1]);
            }
            int seeds = request["seeds"]?.AsInt() ?? 40;
            // "owner": the measured player's owner type (game.human_owner); "" keeps the data's own (teen)
            var owner = request["owner"]?.AsString() ?? "";
            if (owner.Length > 0) data = data.WithHumanOwner(owner);
            var game = GameSetup.Game;
            int randomEarly = 0, smartEarly = 0, closeTen = 0, closeTwenty = 0, lateLead = 0, fightInBand = 0, smartAboveRandom = 0;
            // the random opponent's seat in smart's own session (session.json → opponents), for «smart beats random»
            int randomSeat = 1 + new List<string>(Archetype.Opponents(data)).IndexOf("random");
            double hitsSmart = 0, hitsRandom = 0, winnerFight = 0, equalStretch = 0;
            var rows = JsonValue.Array();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var r = Play(game, data, "random", seed);
                var s = Play(game, data, "smart", seed);
                double firstRandom = FirstBlow(r), firstSmart = FirstBlow(s);
                if (firstRandom >= 0 && firstRandom <= 30) randomEarly++;
                if (firstSmart >= 0 && firstSmart <= 30) smartEarly++;
                hitsRandom += game.Metrics(r, 0)["hits_per_min"];
                hitsSmart += game.Metrics(s, 0)["hits_per_min"];

                var scores = s.Get<MatchModule>(MatchModule.ModuleName).Scores;
                int winner = 0, top = -1, second = -1;
                for (int p = 0; p < scores.Count; p++)
                    if (scores[p] > top) { second = top; top = scores[p]; winner = p; }
                    else if (scores[p] > second) second = scores[p];
                double margin = top <= 0 ? 0 : (top - second) / (double)top;
                if (randomSeat > 0 && scores[0] > scores[randomSeat]) smartAboveRandom++;
                if (margin <= 0.10) closeTen++;
                if (margin <= 0.20) closeTwenty++;
                var wm = game.Metrics(s, winner);
                winnerFight += wm["fight_share"];
                if (wm["fight_share"] >= 0.35 && wm["fight_share"] <= 0.60) fightInBand++;
                equalStretch += wm["longest_equal_stretch_s"];
                int lateChanges = 0;
                foreach (var e in s.Events.All)
                    if (e.Kind == "lead_change" && e.Reward != null && e.Tick > 60 * Simulation.TicksPerSecond) lateChanges++;
                if (lateChanges > 0) lateLead++;

                var row = JsonValue.Object();
                row.Set("seed", Json.Num(seed));
                row.Set("random_first_blow_s", Json.Num(firstRandom));
                row.Set("smart_first_blow_s", Json.Num(firstSmart));
                row.Set("winner", Json.Num(winner));
                row.Set("winner_archetype", JsonValue.Str(winner == 0 ? "smart" : Archetype.Opponents(data)[(winner - 1) % Archetype.Opponents(data).Count]));
                row.Set("margin", Json.Num(margin));
                row.Set("winner_fight_share", Json.Num(wm["fight_share"]));
                row.Set("winner_points", Json.Num(top));
                row.Set("lead_changes_after_60s", Json.Num(lateChanges));
                row.Set("longest_equal_stretch_s", Json.Num(wm["longest_equal_stretch_s"]));
                rows.Add(row);
            }
            // H3 and dominance: the campers and breakers as player 0 on the same seeds, next to smart's own numbers
            var standing = JsonValue.Object();
            double smartWins = 0;
            foreach (var row in rows.Items) if (row["winner"]!.AsInt() == 0) smartWins++;
            foreach (var name in new[] { "idle", "camper", "breaker-squatter", "breaker-orbit" })
            {
                double wins = 0, points = 0, fight = 0, hits = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var sim = Play(game, data, name, seed);
                    var m = game.Metrics(sim, 0);
                    wins += sim.Result!.Outcomes[0] == Outcome.Win ? 1 : 0;
                    points += m["points"];
                    fight += m["fight_share"];
                    hits += m["hits_per_min"];
                }
                var a = JsonValue.Object();
                a.Set("winrate", Json.Num(wins / seeds));
                a.Set("points", Json.Num(points / seeds));
                a.Set("fight_share", Json.Num(fight / seeds));
                a.Set("hits_per_min", Json.Num(hits / seeds));
                standing.Set(name, a);
            }
            var summary = JsonValue.Object();
            summary.Set("variant", JsonValue.Str(variant));
            summary.Set("owner", JsonValue.Str(owner));
            summary.Set("seeds", Json.Num(seeds));
            summary.Set("smart_winrate", Json.Num(smartWins / seeds));
            summary.Set("smart_above_random_share", Json.Num(randomSeat > 0 ? smartAboveRandom / (double)seeds : -1));
            summary.Set("archetypes_as_player_0", standing);
            summary.Set("h1_hits_per_min_smart", Json.Num(hitsSmart / seeds));
            summary.Set("h1_hits_per_min_random", Json.Num(hitsRandom / seeds));
            summary.Set("h1_ratio", Json.Num(hitsRandom == 0 ? 0 : hitsSmart / hitsRandom));
            summary.Set("h2_random_first_blow_within_30s_share", Json.Num(randomEarly / (double)seeds));
            summary.Set("h2_smart_first_blow_within_30s_share", Json.Num(smartEarly / (double)seeds));
            summary.Set("h4_winner_fight_share_mean", Json.Num(winnerFight / seeds));
            summary.Set("h4_winner_fight_share_in_35_60_share", Json.Num(fightInBand / (double)seeds));
            summary.Set("h4_longest_equal_stretch_s_mean", Json.Num(equalStretch / seeds));
            summary.Set("h5_margin_le_10pct_share", Json.Num(closeTen / (double)seeds));
            summary.Set("margin_le_20pct_share", Json.Num(closeTwenty / (double)seeds));
            summary.Set("lead_change_after_60s_share", Json.Num(lateLead / (double)seeds));
            var all = JsonValue.Object();
            all.Set("summary", summary);
            all.Set("seeds_detail", rows);
            _out.WriteLine(summary.ToString());
            var path = request["out"]!.AsString();
            File.WriteAllText(Path.IsPathRooted(path) ? path : Path.Combine(Repo.Root, path), all.ToString() + "\n", new UTF8Encoding(false));
        }
    }
}
