using System.IO;
using System.Linq;
using Game.Core.Data;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Bots
{
    /// <summary>F-006: the swing bench counts only hits landed during a swing and writes swing-bench.json.</summary>
    public class SwingBenchTests
    {
        [Fact]
        public void BenchWritesEveryCellAndCountsOnlyHitsDuringSwings()
        {
            var dir = Path.Combine(Path.GetTempPath(), "swing-bench-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                SwingBench.Run(GameSetup.Game, Repo.Data(), 1, 1, dir);
                var root = Json.Parse(File.ReadAllText(Path.Combine(dir, "swing-bench.json")));
                var cells = root["cells"]!.Items;
                Assert.Equal(16, cells.Count); // 2 dummies × 2 owners × 4 archetypes
                foreach (var c in cells)
                {
                    foreach (var key in new[] { "dummies", "owner", "archetype", "hits_per_min", "swings_per_min", "hit_share" })
                        Assert.NotNull(c[key]);
                    var share = c["hit_share"]!.AsDouble();
                    Assert.InRange(share, 0, 1);
                    // hits are counted only inside swings, so no swings means no hits
                    if (c["swings_per_min"]!.AsDouble() == 0) Assert.Equal(0, c["hits_per_min"]!.AsDouble());
                    if (c["archetype"]!.AsString() == "idle") Assert.Equal(0, c["hits_per_min"]!.AsDouble());
                }
                double Hits(string dummies, string archetype) => cells
                    .Where(c => c["dummies"]!.AsString() == dummies && c["archetype"]!.AsString() == archetype)
                    .Sum(c => c["hits_per_min"]!.AsDouble());
                // a camper does not aim: against running dummies it must not match smart through rams
                Assert.True(Hits("running", "camper") < Hits("running", "smart") / 4,
                    $"camper {Hits("running", "camper")} vs smart {Hits("running", "smart")}");
                Assert.NotNull(root["smart_over_random"]!["all"]);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
