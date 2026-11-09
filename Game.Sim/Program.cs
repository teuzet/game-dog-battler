// Headless session runner for the factory's tool `sim` (playtest/sim.json).
// For every tracked archetype plays N seeds against the opponents from
// Game.Core/Data/bots/session.json and writes into --out:
//   metrics.json     win rates, durations and the game's own metrics
//   chronicle-*.md   a few sessions second by second, events and state only
//   sessions.jsonl   one line per session with pace tags (see tools/sim/SKILL.md)
// Options: --seeds N --first-seed S --out DIR [--variant set/name] [--replay inputs.txt]
using System.Globalization;
using System.Text;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Sim;

var opts = Args.Parse(args);
var dataDir = GameData.FindDataDir(AppContext.BaseDirectory);
if (!Directory.Exists(dataDir)) dataDir = GameData.FindDataDir(Directory.GetCurrentDirectory());
var data = GameData.Load(dataDir);
if (opts.Variant.Length > 0)
{
    var parts = opts.Variant.Split('/');
    data = data.WithVariant(dataDir, parts[0], parts[1]);
}
var game = GameSetup.Game;
Directory.CreateDirectory(opts.Out);

if (opts.Replay.Length > 0)
{
    var replay = Runner.Replay(game, data, InputLog.Load(opts.Replay));
    File.WriteAllText(Path.Combine(opts.Out, "replay.md"), replay, new UTF8Encoding(false));
    Console.WriteLine($"replayed {opts.Replay}");
    return 0;
}

var setup = data.Get("bots.session");
var tracked = setup["tracked"]!.Items.Select(i => i.AsString()).ToList();
var opponents = setup["opponents"]!.Items.Select(i => i.AsString()).ToList();
var sessions = new StringBuilder();
var metrics = JsonValue.Object();
var perArchetype = JsonValue.Object();
int chronicles = 0;

foreach (var name in tracked)
{
    int wins = 0, n = 0;
    double seconds = 0;
    var sums = new SortedDictionary<string, double>(StringComparer.Ordinal);
    for (int seed = opts.FirstSeed; seed < opts.FirstSeed + opts.Seeds; seed++)
    {
        bool chronicle = name == "smart" && chronicles < 3;
        var s = Runner.Play(game, data, seed, name, opponents, chronicle);
        sessions.Append(s.Line).Append('\n');
        n++;
        wins += s.Outcome == Outcome.Win ? 1 : 0;
        seconds += s.Seconds;
        foreach (var kv in s.Metrics) sums[kv.Key] = (sums.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
        if (chronicle)
        {
            chronicles++;
            File.WriteAllText(Path.Combine(opts.Out, $"chronicle-{seed}.md"), s.Chronicle, new UTF8Encoding(false));
        }
    }
    var a = JsonValue.Object();
    a.Set("sessions", Json.Num(n));
    a.Set("winrate", Json.Num(n == 0 ? 0 : wins / (double)n));
    a.Set("duration_s", Json.Num(n == 0 ? 0 : seconds / n));
    foreach (var kv in sums) a.Set(kv.Key, Json.Num(kv.Value / Math.Max(1, n)));
    perArchetype.Set(name, a);
}
metrics.Set("game", JsonValue.Str(game.Name));
metrics.Set("variant", JsonValue.Str(data.Variant));
metrics.Set("seeds", Json.Num(opts.Seeds));
var opp = JsonValue.Array();
foreach (var o in opponents) opp.Add(JsonValue.Str(o));
metrics.Set("opponents", opp);
metrics.Set("archetypes", perArchetype);
File.WriteAllText(Path.Combine(opts.Out, "metrics.json"), metrics.ToString() + "\n", new UTF8Encoding(false));
File.WriteAllText(Path.Combine(opts.Out, "sessions.jsonl"), sessions.ToString(), new UTF8Encoding(false));
Console.WriteLine($"{tracked.Count} archetypes × {opts.Seeds} seeds → {opts.Out}");
return 0;

sealed record Args(int Seeds, int FirstSeed, string Out, string Variant, string Replay)
{
    public static Args Parse(string[] a)
    {
        string Get(string key, string fallback)
        {
            int i = Array.IndexOf(a, key);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }
        return new Args(int.Parse(Get("--seeds", "40"), CultureInfo.InvariantCulture),
                        int.Parse(Get("--first-seed", "1"), CultureInfo.InvariantCulture),
                        Get("--out", "playtest/run"), Get("--variant", ""), Get("--replay", ""));
    }
}

sealed record SessionOut(Outcome Outcome, double Seconds, string Line, string Chronicle, IReadOnlyDictionary<string, double> Metrics);

static class Runner
{
    const int Tracked = 0; // the participant whose session is measured
    const int WinProbEverySeconds = 5;
    const int WinProbRollouts = 8;
    const int DecisionHorizonSeconds = 6;

    public static SessionOut Play(IGame game, GameData data, int seed, string archetype, IReadOnlyList<string> opponents, bool chronicle)
    {
        var sim = game.Create(seed, data);
        var bots = new List<BotDriver>();
        for (int p = 0; p < sim.Players; p++)
        {
            var a = Archetype.Find(data, p == Tracked ? archetype : opponents[(p - 1) % opponents.Count]);
            bots.Add(new BotDriver(p, a, game.Strategy(a, data), (ulong)seed));
        }
        var events = new StringBuilder();
        var winProb = new StringBuilder();
        var chron = new StringBuilder();
        chron.Append($"# Сессия {seed}: участник 0 — {archetype}, соперники — {string.Join(", ", opponents)}\n\n");
        bool firstEvent = true, firstWp = true;
        int lastDecisionTick = -1000;
        int seenEvents = 0;
        while (!sim.IsOver)
        {
            var options = game.DecisionOptions(sim, Tracked);
            double? spread = null;
            if (options.Count >= 2 && sim.Tick - lastDecisionTick > Simulation.TicksPerSecond)
            {
                spread = Forks.DecisionSpread(game, sim, bots, Tracked, options, Simulation.TicksPerSecond / 2,
                                              DecisionHorizonSeconds * Simulation.TicksPerSecond);
                lastDecisionTick = sim.Tick;
            }
            if (sim.Tick % (WinProbEverySeconds * Simulation.TicksPerSecond) == 0)
            {
                double p = Forks.WinProbability(game, sim, bots, Tracked, WinProbRollouts);
                winProb.Append(firstWp ? "" : ",").Append($"{{\"t\":{F(sim.Seconds)},\"p\":{F(p)}}}");
                firstWp = false;
            }
            sim.Step(Forks.Inputs(game, sim, bots, -1, PlayerInput.None));
            if (spread != null)
            {
                events.Append(firstEvent ? "" : ",").Append($"{{\"t\":{F(sim.Seconds)},\"decision\":{{\"options\":{options.Count},\"spread\":{F(spread.Value)}}}}}");
                firstEvent = false;
            }
            for (; seenEvents < sim.Events.All.Count; seenEvents++)
            {
                var e = sim.Events.All[seenEvents];
                if (chronicle && e.Text.Length > 0) chron.Append($"- {F(e.Tick / (double)Simulation.TicksPerSecond)} с · {(e.Actor < 0 ? "мир" : "участник " + e.Actor)} · {e.Text}\n");
                if (e.Actor != Tracked) continue;
                var tags = new StringBuilder();
                if (e.Threat != null) tags.Append($",\"threat\":{F(e.Threat.Value.ToDouble())}");
                if (e.Reward != null) tags.Append($",\"reward\":{F(e.Reward.Value.ToDouble())}");
                if (e.Novel != null) tags.Append($",\"novel\":{Str(e.Novel)}");
                if (tags.Length == 0) continue;
                events.Append(firstEvent ? "" : ",").Append($"{{\"t\":{F(e.Tick / (double)Simulation.TicksPerSecond)}{tags}}}");
                firstEvent = false;
            }
            // events of the world (Actor = -1) with a novelty tag are new for everyone
            if (chronicle && sim.Tick % Simulation.TicksPerSecond == 0) chron.Append($"  {F(sim.Seconds)} с: {game.Describe(sim)}\n");
        }
        foreach (var e in sim.Events.All)
            if (e.Actor < 0 && e.Novel != null)
            {
                events.Append(firstEvent ? "" : ",").Append($"{{\"t\":{F(e.Tick / (double)Simulation.TicksPerSecond)},\"novel\":{Str(e.Novel)}}}");
                firstEvent = false;
            }
        var res = sim.Result!;
        var outcome = res.Outcomes[Tracked];
        string o = outcome == Outcome.Win ? "win" : outcome == Outcome.Loss ? "loss" : "draw";
        chron.Append($"\nИтог: {res.Why}; участник 0 — {o}.\n");
        string line = $"{{\"seed\":{seed},\"archetype\":{Str(archetype)},\"outcome\":\"{o}\",\"duration_s\":{F(sim.Seconds)}," +
                      $"\"margin\":{F(res.Margin.ToDouble())},\"events\":[{events}],\"win_prob\":[{winProb}]}}";
        return new SessionOut(outcome, sim.Seconds, line, chron.ToString(), game.Metrics(sim, Tracked));
    }

    /// <summary>Nick's session from the demo build, played again: the same chronicle a bot session gets.</summary>
    public static string Replay(IGame game, GameData data, InputLog log)
    {
        var sim = game.Create(log.Seed, data);
        var sb = new StringBuilder($"# Повтор сессии Nick (сид {log.Seed}, вариант {log.Variant})\n\n");
        int seen = 0;
        foreach (var t in log.Ticks)
        {
            if (sim.IsOver) break;
            sim.Step(t);
            for (; seen < sim.Events.All.Count; seen++)
            {
                var e = sim.Events.All[seen];
                if (e.Text.Length > 0) sb.Append($"- {F(e.Tick / (double)Simulation.TicksPerSecond)} с · {(e.Actor < 0 ? "мир" : "участник " + e.Actor)} · {e.Text}\n");
            }
            if (sim.Tick % Simulation.TicksPerSecond == 0) sb.Append($"  {F(sim.Seconds)} с: {game.Describe(sim)}\n");
        }
        sb.Append(sim.Result == null ? "\nСессия не доиграна.\n" : $"\nИтог: {sim.Result.Why}.\n");
        return sb.ToString();
    }

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static string Str(string s)
    {
        var sb = new StringBuilder();
        Json.WriteString(sb, s);
        return sb.ToString();
    }
}
