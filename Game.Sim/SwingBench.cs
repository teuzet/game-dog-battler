using System.Globalization;
using System.Text;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Owners;
using Game.Core.Sim;

/// <summary>The swing bench (design/overview.md → «Риск №1», hypothesis H1): every tracked archetype plays in the
/// swing sandbox against standing mannequins (idle) and running ones (random), once per owner type. A swing is
/// one stretch of the owner moving at blow speed (hits.threshold_mps) until he slows to half of it; it hits if
/// the participant's hit count grew while it lasted (hits outside a swing, rams by a rival, are not counted). Writes swing-bench.json and prints the smart/random ratio.</summary>
static class SwingBench
{
    private static readonly (string Name, string Archetype)[] Dummies = { ("standing", "idle"), ("running", "random") };

    public static void Run(IGame game, GameData data, int seeds, int firstSeed, string outDir)
    {
        var tracked = data.Get("bots.session.tracked").Items.Select(i => i.AsString()).ToList();
        var owners = new List<string>();
        foreach (var o in data.Get("rules.sandbox.owner_types").Items) owners.Add(o.AsString());
        var cells = new List<Cell>();
        foreach (var (dummy, dummyArchetype) in Dummies)
        foreach (var owner in owners)
        foreach (var name in tracked)
        {
            var d = data.WithArena("sandbox").WithHumanOwner(owner);
            d = new GameData(d.Root.MergedWith(Json.Parse("{\"rules\":{\"sandbox\":{\"opponents\":[\"" + dummyArchetype + "\"]}}}")), d.Variant);
            var cell = new Cell(dummy, owner, name);
            for (int seed = firstSeed; seed < firstSeed + seeds; seed++) Play(game, d, seed, name, cell);
            cells.Add(cell);
        }

        var json = JsonValue.Array();
        var sb = new StringBuilder();
        sb.Append("dummies  owner  archetype   hits/min  swings/min  hit share\n");
        foreach (var c in cells)
        {
            var row = JsonValue.Object();
            row.Set("dummies", JsonValue.Str(c.Dummy));
            row.Set("owner", JsonValue.Str(c.Owner));
            row.Set("archetype", JsonValue.Str(c.Archetype));
            row.Set("hits_per_min", Json.Num(c.HitsPerMin));
            row.Set("swings_per_min", Json.Num(c.SwingsPerMin));
            row.Set("hit_share", Json.Num(c.HitShare));
            json.Add(row);
            sb.Append($"{c.Dummy,-8} {c.Owner,-5}  {c.Archetype,-9} {Fmt(c.HitsPerMin),9} {Fmt(c.SwingsPerMin),11} {Fmt(c.HitShare),10}\n");
        }

        var ratios = JsonValue.Object();
        sb.Append("\nsmart/random hits per minute (H1 ≥ 2.5×, judged by the playtest):\n");
        foreach (var (dummy, _) in Dummies)
        {
            foreach (var owner in owners.Append(""))
            {
                var sel = cells.Where(c => c.Dummy == dummy && (owner.Length == 0 || c.Owner == owner)).ToList();
                var key = dummy + (owner.Length == 0 ? "" : "/" + owner);
                AddRatio(sb, ratios, key, sel);
            }
        }
        AddRatio(sb, ratios, "all", cells);

        var result = JsonValue.Object();
        result.Set("seeds", Json.Num(seeds));
        result.Set("cells", json);
        result.Set("smart_over_random", ratios);
        File.WriteAllText(Path.Combine(outDir, "swing-bench.json"), result.ToString() + "\n", new UTF8Encoding(false));
        Console.Write(sb.ToString());
    }

    private static void AddRatio(StringBuilder sb, JsonValue ratios, string key, List<Cell> sel)
    {
        double smart = sel.Where(c => c.Archetype == "smart").Sum(c => c.HitsPerMin);
        double random = sel.Where(c => c.Archetype == "random").Sum(c => c.HitsPerMin);
        sb.Append($"  {key,-14} smart {Fmt(smart)} / random {Fmt(random)} = {(random == 0 ? "n/a (random never hit)" : Fmt(smart / random) + "×")}\n");
        ratios.Set(key, random == 0 ? JsonValue.Null() : Json.Num(smart / random));
    }

    private static void Play(IGame game, GameData data, int seed, string archetype, Cell cell)
    {
        var sim = game.Create(seed, data);
        var opponents = Archetype.Opponents(data);
        var bots = new List<BotDriver>();
        for (int p = 0; p < sim.Players; p++)
        {
            var a = Archetype.Find(data, p == 0 ? archetype : opponents[(p - 1) % opponents.Count]);
            bots.Add(new BotDriver(p, a, game.Strategy(a, data), (ulong)seed));
        }
        var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
        var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
        var blow = data.Num("hits.threshold_mps");
        var slow = blow * data.Num("bots.swing.swing_end_share");
        bool swinging = false, swingHit = false;
        int lastHits = 0;
        while (!sim.IsOver)
        {
            sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
            int hits = (int)game.Metrics(sim, 0)["hits"];
            var speed = bodies.Velocity(owners.BodyOf(0)).Length;
            if (!swinging && speed >= blow) { swinging = true; swingHit = false; }
            // only a hit landed while the owner is swinging counts: a rival dog running into a standing owner is a ram, not a swing
            if (swinging && hits > lastHits) { cell.Hits += hits - lastHits; swingHit = true; }
            lastHits = hits;
            if (swinging && speed < slow)
            {
                swinging = false;
                cell.Swings++;
                if (swingHit) cell.HitSwings++;
            }
        }
        if (swinging) { cell.Swings++; if (swingHit) cell.HitSwings++; }
        cell.Minutes += sim.Seconds / 60.0;
    }

    private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed class Cell
    {
        public readonly string Dummy, Owner, Archetype;
        public int Hits, Swings, HitSwings;
        public double Minutes;
        public Cell(string dummy, string owner, string archetype) { Dummy = dummy; Owner = owner; Archetype = archetype; }
        public double HitsPerMin => Minutes == 0 ? 0 : Hits / Minutes;
        public double SwingsPerMin => Minutes == 0 ? 0 : Swings / Minutes;
        public double HitShare => Swings == 0 ? 0 : HitSwings / (double)Swings;
    }
}
