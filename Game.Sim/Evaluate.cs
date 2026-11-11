using System.Globalization;
using System.Text;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Sim;

/// <summary>The scorer of the factory's tool `evolve` (playtest/evolve.json): `Game.Sim evaluate --batch b.json --scores s.json`.
/// The batch is {space, generation, seeds[], playgrounds[{name, weight}], base{path: number}, league[{id, params}],
/// candidates[{id, params}]}; the scores file is {"scores": [{id, score, details{}}]}. A candidate's data is the game's data
/// ← base ← params; its score is Σ weight × the score of each playground (Data/playgrounds/&lt;name&gt;.json), a playground's
/// score is the mean over the seeds and the sessions inside. Anything unknown (playground, metric, archetype) throws.</summary>
static class Evaluate
{
    public static int Run(string[] args)
    {
        string Get(string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"evaluate: no {key}");
        }
        try
        {
            var dataDir = GameData.FindDataDir(AppContext.BaseDirectory);
            if (!Directory.Exists(dataDir)) dataDir = GameData.FindDataDir(Directory.GetCurrentDirectory());
            var batch = Json.Parse(File.ReadAllText(Get("--batch")));
            var scores = Score(GameSetup.Game, GameData.Load(dataDir), batch);
            File.WriteAllText(Get("--scores"), scores.ToString() + "\n", new UTF8Encoding(false));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("evaluate failed: " + e.Message);
            return 1;
        }
    }

    public static JsonValue Score(IGame game, GameData data, JsonValue batch)
    {
        var seeds = batch["seeds"]!.Items.Select(s => s.AsInt()).ToList();
        if (seeds.Count == 0) throw new ArgumentException("evaluate: the batch has no seeds");
        var weights = batch["playgrounds"]!.Items.Select(p => (Playground.Load(data, p["name"]!.AsString()), p["weight"]!.AsDouble())).ToList();
        if (weights.Count == 0) throw new ArgumentException("evaluate: the batch has no playgrounds");
        var baseNumbers = Numbers(batch["base"]);
        var league = batch["league"]!.Items.Select(m => Numbers(m["params"])).ToList();
        var result = JsonValue.Array();
        foreach (var c in batch["candidates"]!.Items)
        {
            var candidate = Numbers(c["params"]);
            double total = 0;
            var details = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var (pg, weight) in weights)
            {
                var score = pg.Score(game, data, seeds, baseNumbers, candidate, league, details);
                total += weight * score;
                Put(details, pg.Name, score);
            }
            var row = JsonValue.Object();
            row.Set("id", JsonValue.Str(c["id"]!.AsString()));
            row.Set("score", Num(total));
            var d = JsonValue.Object();
            foreach (var kv in details) d.Set(kv.Key, Num(kv.Value));
            row.Set("details", d);
            result.Add(row);
        }
        if (result.Items.Count != batch["candidates"]!.Items.Count) throw new InvalidOperationException("evaluate: a candidate has no score");
        var root = JsonValue.Object();
        root.Set("scores", result);
        return root;
    }

    internal static void Put(SortedDictionary<string, double> details, string key, double value)
    {
        if (!details.TryAdd(key, value)) throw new InvalidOperationException($"evaluate: two playgrounds write details '{key}'");
    }

    internal static SortedDictionary<string, double> Numbers(JsonValue? flat)
    {
        var res = new SortedDictionary<string, double>(StringComparer.Ordinal);
        if (flat != null) foreach (var kv in flat.Fields) res[kv.Key] = kv.Value.AsDouble();
        return res;
    }

    private static JsonValue Num(double v) => Json.Num(v == 0 ? 0 : v); // no "-0"
}

/// <summary>A playground (Game.Core/Data/playgrounds/&lt;name&gt;.json): an arena, who plays on it and how a session is scored.</summary>
sealed class Playground
{
    public readonly string Name, Arena, Player;
    public readonly bool League;
    public readonly IReadOnlyList<string> Owners, Opponents;
    public readonly double? Seconds;
    public readonly IReadOnlyList<Term> Terms;
    /// <summary>The verbs a league duel must use (block "verbs": skill → the least share of decisions, and "penalty"): each skill of
    /// participant 0 chosen in a smaller share of its decisions takes <see cref="VerbPenalty"/> off the duel's score. Empty: no check.</summary>
    public readonly IReadOnlyList<(string Skill, double MinShare)> Verbs;
    public readonly double VerbPenalty;

    public sealed record Term(string Metric, double Weight, bool PerMinute);

    private Playground(string name, JsonValue j)
    {
        Name = name;
        var kind = j["kind"]?.AsString() ?? "sessions";
        if (kind != "sessions" && kind != "league") throw new ArgumentException($"playground {name}: no kind '{kind}' (sessions, league)");
        League = kind == "league";
        Arena = j["arena"]!.AsString();
        Player = j["player"]!.AsString();
        Opponents = j["opponents"]!.Items.Select(o => o.AsString()).ToList();
        Owners = j["owners"]?.Items.Select(o => o.AsString()).ToList() ?? new List<string>();
        Seconds = j["seconds"]?.AsDouble();
        Terms = j["score"]!.Items.Select(t => new Term(t["metric"]!.AsString(), t["weight"]!.AsDouble(), t["per_minute"]?.AsBool() ?? false)).ToList();
        var verbs = j["verbs"];
        Verbs = verbs?.Fields.Where(f => f.Key != "penalty").Select(f => (f.Key, f.Value.AsDouble())).ToList() ?? new List<(string, double)>();
        VerbPenalty = verbs == null ? 0 : verbs["penalty"]?.AsDouble() ?? throw new ArgumentException($"playground {name}: verbs without penalty");
        if (verbs != null && !League) throw new ArgumentException($"playground {name}: verbs are checked only in a league");
        if (!League && Owners.Count == 0) throw new ArgumentException($"playground {name}: no owners");
    }

    public static Playground Load(GameData data, string name) => new Playground(name, data.Get("playgrounds." + name));

    /// <summary>The data a session on this playground runs on: the arena, then the numbers, then what the playground sets itself
    /// (who the opponents are, how long it lasts, who owns participant 0).</summary>
    public GameData SessionData(GameData data, IReadOnlyDictionary<string, double> numbers, string owner)
    {
        var d = data.WithArena(Arena).WithNumbers(numbers);
        var over = JsonValue.Object();
        var opponents = JsonValue.Array();
        foreach (var o in Opponents) opponents.Add(JsonValue.Str(o));
        var rules = JsonValue.Object();
        var rule = JsonValue.Object();
        rule.Set("opponents", opponents);
        rules.Set(Archetype.RulesKey(d).Substring("rules.".Length), rule);
        over.Set("rules", rules);
        if (Seconds is double s)
        {
            var match = JsonValue.Object();
            match.Set("seconds", Json.Num(s));
            over.Set("match", match);
        }
        d = new GameData(d.Root.MergedWith(over), d.Variant);
        return owner.Length > 0 ? d.WithHumanOwner(owner) : d;
    }

    private static IReadOnlyDictionary<string, double> Merge(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        var m = new Dictionary<string, double>(a);
        foreach (var kv in b) m[kv.Key] = kv.Value;
        return m;
    }

    /// <summary>The mean score of the candidate over the seeds and the sessions; its inner numbers go to <paramref name="details"/>.</summary>
    public double Score(IGame game, GameData data, IReadOnlyList<int> seeds, IReadOnlyDictionary<string, double> baseNumbers,
        IReadOnlyDictionary<string, double> candidate, IReadOnlyList<IReadOnlyDictionary<string, double>> league,
        SortedDictionary<string, double> details)
    {
        var mine = Merge(baseNumbers, candidate);
        var sessionScores = new List<double>();
        var termSums = new double[Terms.Count];
        if (!League)
        {
            foreach (var owner in Owners)
            {
                var simData = SessionData(data, mine, owner);
                var rivals = SessionData(data, baseNumbers, owner);
                foreach (var seed in seeds)
                {
                    var s = Play(game, simData, seed, Player, simData, Opponents, rivals);
                    sessionScores.Add(s.Sum(termSums, Terms));
                }
            }
        }
        else
        {
            // rivals: every league member (the same archetype on its own numbers), then every archetype of "opponents" on the base data
            var outcomes = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);
            var shares = new SortedDictionary<string, List<double>>(StringComparer.Ordinal); // skill → its share of the decisions, per duel
            var simData = SessionData(data, mine, "");
            var rivals = new List<(string Archetype, GameData Data, string Bucket)>();
            foreach (var member in league) rivals.Add((Player, SessionData(data, Merge(baseNumbers, member), ""), "winrate_league"));
            var baseData = SessionData(data, baseNumbers, "");
            foreach (var o in Opponents) rivals.Add((o, baseData, "winrate_" + o));
            foreach (var seed in seeds)
            foreach (var (archetype, rivalData, bucket) in rivals)
            {
                var s = Play(game, simData, seed, Player, simData, new[] { archetype }, rivalData);
                double outcome = s.Outcome == Outcome.Win ? 1 : s.Outcome == Outcome.Draw ? 0.5 : 0;
                if (!outcomes.TryGetValue(bucket, out var list)) outcomes[bucket] = list = new List<double>();
                list.Add(outcome);
                sessionScores.Add(outcome + s.Sum(termSums, Terms) - VerbFine(s, shares));
            }
            foreach (var kv in outcomes) Evaluate.Put(details, kv.Key, kv.Value.Average());
            foreach (var kv in shares) Evaluate.Put(details, "pick_" + kv.Key, kv.Value.Average());
        }
        for (int i = 0; i < Terms.Count; i++) Evaluate.Put(details, Name + "." + Terms[i].Metric, termSums[i] / sessionScores.Count);
        return sessionScores.Average();
    }

    /// <summary>Notes the shares of the skills participant 0 chose (when its strategy tells them) and returns the fine for the verbs
    /// it left out: <see cref="VerbPenalty"/> per named skill below its share.</summary>
    private double VerbFine(Played s, SortedDictionary<string, List<double>> shares)
    {
        if (s.Picks == null)
        {
            if (Verbs.Count > 0) throw new InvalidOperationException($"playground {Name}: verbs are checked, but {Player} does not tell its picks (IPicks)");
            return 0;
        }
        double total = s.Picks.Values.Sum();
        foreach (var kv in s.Picks)
        {
            if (!shares.TryGetValue(kv.Key, out var list)) shares[kv.Key] = list = new List<double>();
            list.Add(total == 0 ? 0 : kv.Value / total);
        }
        double fine = 0;
        foreach (var (skill, min) in Verbs)
        {
            if (!s.Picks.TryGetValue(skill, out var n)) throw new KeyNotFoundException($"playground {Name}: no skill '{skill}' (the bot has: {string.Join(", ", s.Picks.Keys)})");
            if (total == 0 || n / total < min) fine += VerbPenalty;
        }
        return fine;
    }

    /// <summary>One session: participant 0 plays <paramref name="player"/> on <paramref name="playerData"/>, participants 1.. play
    /// the opponents in turn on <paramref name="rivalData"/> (their numbers are not the candidate's).</summary>
    private static Played Play(IGame game, GameData simData, int seed, string player, GameData playerData, IReadOnlyList<string> opponents, GameData rivalData)
    {
        var sim = game.Create(seed, simData);
        var bots = new List<BotDriver>();
        for (int p = 0; p < sim.Players; p++)
        {
            var a = Archetype.Find(p == 0 ? playerData : rivalData, p == 0 ? player : opponents[(p - 1) % opponents.Count]);
            bots.Add(new BotDriver(p, a, game.Strategy(a, p == 0 ? playerData : rivalData), (ulong)seed));
        }
        while (!sim.IsOver) sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
        return new Played(sim.Result!.Outcomes[0], game.Metrics(sim, 0), sim.Seconds, (bots[0].Strategy as IPicks)?.Picks);
    }

    /// <summary><paramref name="Picks"/>: how often participant 0 chose each skill, null when its strategy does not tell (no <see cref="IPicks"/>).</summary>
    private sealed record Played(Outcome Outcome, IReadOnlyDictionary<string, double> Metrics, double Seconds, IReadOnlyDictionary<string, int>? Picks)
    {
        /// <summary>Σ weight × metric of the score terms; the unweighted term values are added to <paramref name="termSums"/>.</summary>
        public double Sum(double[] termSums, IReadOnlyList<Term> terms)
        {
            double total = 0;
            for (int i = 0; i < terms.Count; i++)
            {
                var t = terms[i];
                if (!Metrics.TryGetValue(t.Metric, out var v))
                    throw new KeyNotFoundException($"no metric '{t.Metric}' (the rules of this arena give: {string.Join(", ", Metrics.Keys)})");
                if (t.PerMinute) v /= Seconds / 60.0;
                termSums[i] += v;
                total += t.Weight * v;
            }
            return total;
        }
    }
}
