using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Sim
{
    /// <summary>F-041: the scorer of evolve (Game.Sim evaluate), the playgrounds as data and the search space files.</summary>
    public class EvaluateTests
    {
        private static IEnumerable<string> PlaygroundNames() =>
            Repo.Data().Get("playgrounds").Fields.Select(f => f.Key);

        [Fact]
        public void EveryPlaygroundLoadsWithItsArenaArchetypesAndMetrics()
        {
            var data = Repo.Data();
            Assert.Contains("bag", PlaygroundNames());
            foreach (var name in PlaygroundNames())
            {
                var pg = Playground.Load(data, name);
                Assert.True(data.Has("arenas." + pg.Arena), $"{name}: no arena {pg.Arena}");
                Archetype.Find(data, pg.Player);
                foreach (var o in pg.Opponents) Archetype.Find(data, o);
                foreach (var owner in pg.Owners.Count == 0 ? new[] { "" } : pg.Owners.ToArray())
                {
                    var sim = GameSetup.Game.Create(1, pg.SessionData(data, new Dictionary<string, double>(), owner));
                    var metrics = GameSetup.Game.Metrics(sim, 0);
                    foreach (var t in pg.Terms) Assert.True(metrics.ContainsKey(t.Metric), $"{name}: no metric {t.Metric} on {pg.Arena}");
                }
            }
        }

        [Fact]
        public void SoloArchetypesAreNotTracked()
        {
            var tracked = Repo.Data().Get("bots.session.tracked").Items.Select(i => i.AsString()).ToList();
            Assert.DoesNotContain("solo-strike", tracked);
            Assert.DoesNotContain("solo-dodge", tracked);
        }

        [Fact]
        public void EverySearchSpaceFitsTheData()
        {
            var data = Repo.Data();
            var files = Directory.GetFiles(Path.Combine(Repo.Root, "playtest", "evolve"), "*.json");
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                var space = Json.Parse(File.ReadAllText(file));
                var name = Path.GetFileName(file);
                foreach (var p in space["params"]!.Fields)
                {
                    Assert.True(data.Has(p.Key), $"{name}: no {p.Key} in the data");
                    var min = p.Value["min"]!.AsDouble();
                    var max = p.Value["max"]!.AsDouble();
                    var start = p.Value["start"]!.AsDouble();
                    Assert.True(min < max, $"{name}: {p.Key} min {min} ≥ max {max}");
                    Assert.InRange(start, min, max);
                    Assert.Equal(data.Num(p.Key).ToDouble(), start, 3); // start = the game's number now
                }
                foreach (var pg in space["playgrounds"]!.Items) Assert.Contains(pg["name"]!.AsString(), PlaygroundNames());
            }
        }

        private static JsonValue Batch(string playgrounds, string league = "[]", string seeds = "[1, 2]") => Json.Parse(
            "{\"space\":\"t\",\"generation\":0,\"seeds\":" + seeds + ",\"playgrounds\":" + playgrounds + ",\"base\":{},\"league\":" + league + "," +
            "\"candidates\":[{\"id\":\"c0\",\"params\":{}},{\"id\":\"c1\",\"params\":{\"bots.swing.lead_m\":0}}]}");

        [Fact]
        public void BatchScoresEveryCandidateAndRepeatsByteForByte()
        {
            var data = Repo.Data();
            var batch = Batch("[{\"name\":\"bag\",\"weight\":1},{\"name\":\"dodge\",\"weight\":2}]");
            var first = Evaluate.Score(GameSetup.Game, data, batch).ToString();
            Assert.Equal(first, Evaluate.Score(GameSetup.Game, data, batch).ToString());
            var scores = Json.Parse(first)["scores"]!.Items;
            Assert.Equal(new[] { "c0", "c1" }, scores.Select(s => s["id"]!.AsString()));
            foreach (var s in scores)
            {
                var d = s["details"]!;
                double bag = d["bag"]!.AsDouble(), dodge = d["dodge"]!.AsDouble();
                Assert.NotNull(d["bag.hits"]);
                Assert.NotNull(d["dodge.hits_taken"]);
                Assert.Equal(bag + 2 * dodge, s["score"]!.AsDouble(), 3);
            }
            // lead_m at the minimum changes the swing, so the bag numbers differ
            Assert.NotEqual(scores[0]["details"]!["bag"]!.AsDouble(), scores[1]["details"]!["bag"]!.AsDouble());
        }

        [Fact]
        public void LeagueBatchGivesWinRates()
        {
            var batch = Batch("[{\"name\":\"duel_league\",\"weight\":1}]", "[{\"id\":\"g0-00\",\"params\":{\"bots.swing.lead_m\":1}}]");
            var scores = Json.Parse(Evaluate.Score(GameSetup.Game, Repo.Data(), batch).ToString())["scores"]!.Items;
            bool anyDealt = false, anyTaken = false;
            foreach (var s in scores)
            {
                var d = s["details"]!;
                Assert.InRange(d["winrate_league"]!.AsDouble(), 0, 1);
                Assert.InRange(d["winrate_smart"]!.AsDouble(), 0, 1);
                // F-050: where participant 0 took and dealt its blows, by the skill it led then
                foreach (var prefix in new[] { "taken_in_", "dealt_in_" })
                {
                    double sum = 0;
                    foreach (var skill in DuelistStrategy.Skills)
                    {
                        var share = d[prefix + skill]!.AsDouble();
                        Assert.InRange(share, 0, 1);
                        sum += share;
                    }
                    Assert.True(System.Math.Abs(sum) < 1e-9 || System.Math.Abs(sum - 1) < 1e-6,$"{prefix}*: sum {sum}");
                    if (sum > 0.5) { if (prefix == "taken_in_") anyTaken = true; else anyDealt = true; }
                }
            }
            Assert.True(anyTaken && anyDealt, "eight duels without a blow either way");
        }

        /// <summary>F-050: every rival of a league gets its own duel seed, so smart twice in "opponents" is two duels, not one twice.
        /// Were the two duels of a seed the same, every averaged detail of [smart, smart] would equal that of [smart].</summary>
        [Fact]
        public void LeagueRepeatedOpponentPlaysIndependentDuelsAndRepeatsByteForByte()
        {
            var data = Repo.Data();
            JsonValue Run(string opponents)
            {
                var d = new GameData(data.Root.MergedWith(Json.Parse("{\"playgrounds\":{\"duel_league\":{\"opponents\":" + opponents + "}}}")));
                var batch = Json.Parse("{\"space\":\"t\",\"generation\":0,\"seeds\":[1,2,3,4],\"playgrounds\":[{\"name\":\"duel_league\",\"weight\":1}]," +
                                       "\"base\":{},\"league\":[],\"candidates\":[{\"id\":\"c0\",\"params\":{}}]}");
                return Evaluate.Score(GameSetup.Game, d, batch);
            }
            var twice = Run("[\"smart\",\"smart\"]").ToString();
            Assert.Equal(twice, Run("[\"smart\",\"smart\"]").ToString());
            var once = Run("[\"smart\"]");
            Assert.NotEqual(once.ToString(), twice);
            Assert.NotEqual(Playground.DuelSeed(1, 0), Playground.DuelSeed(1, 1));
        }

        [Fact]
        public void UnknownPlaygroundMetricOrNumberThrows()
        {
            var data = Repo.Data();
            Assert.Throws<KeyNotFoundException>(() => Evaluate.Score(GameSetup.Game, data, Batch("[{\"name\":\"nope\",\"weight\":1}]")));
            Assert.Throws<KeyNotFoundException>(() => data.WithNumbers(new Dictionary<string, double> { { "bots.swing.no_such", 1 } }));
            var over = Json.Parse("{\"playgrounds\":{\"bad\":{\"arena\":\"sandbox\",\"owners\":[\"teen\"],\"player\":\"solo-strike\",\"opponents\":[\"idle\"],\"seconds\":5," +
                                  "\"score\":[{\"metric\":\"rounds_won\",\"weight\":1}]}}}");
            var withBad = new GameData(data.Root.MergedWith(over));
            Assert.Throws<KeyNotFoundException>(() => Evaluate.Score(GameSetup.Game, withBad, Batch("[{\"name\":\"bad\",\"weight\":1}]")));
        }

        [Fact]
        public void WithNumbersLaysNumbersOverTheData()
        {
            var data = Repo.Data();
            var lead = data.Num("bots.swing.lead_m");
            var over = data.WithNumbers(new Dictionary<string, double> { { "bots.swing.lead_m", 1.5 }, { "bots.skills.dodge.danger_m", 2 } });
            Assert.Equal(1.5, over.Num("bots.swing.lead_m").ToDouble(), 6);
            Assert.Equal(2, over.Num("bots.skills.dodge.danger_m").ToDouble(), 6);
            Assert.Equal(lead, data.Num("bots.swing.lead_m")); // the source is untouched
        }
    }
}
