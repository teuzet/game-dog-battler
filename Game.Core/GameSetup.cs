using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Example;
using Game.Core.Sim;

namespace Game.Core
{
    /// <summary>The assembly point: which game this repository is and its
    /// modules in tick order. The order here is the one place every feature
    /// may touch; everything else lives in its module.</summary>
    public static class GameSetup
    {
        public static readonly IGame Game = new ExampleRaceGame();
    }

    /// <summary>The template's example game (see Modules/Example/README.md).</summary>
    public sealed class ExampleRaceGame : IGame
    {
        public string Name => "example-race";
        public int Players => 4;

        public Simulation Create(int seed, GameData data)
        {
            int players = data.Has("game.race.players") ? data.Int("game.race.players") : Players;
            var modules = new List<IModule>
            {
                new Course(data.Num("game.race.length"), data.Num("game.race.fork_every")),
                new Runners(players, data),
                new Referee(data),
            };
            return new Simulation(seed, players, data, modules);
        }

        public BotView Sense(Simulation sim, int player)
        {
            var course = sim.Get<Course>(Course.ModuleName);
            var all = sim.Get<Runners>(Runners.ModuleName).All;
            var me = all[player];
            var screen = new ScreenRect(sim.Data.Num("game.race.screen_half_width"), Fixed.FromInt(5));
            var v = new BotView { Tick = sim.Tick, Self = player, X = me.Pos };
            v.Hud["stamina"] = me.Stamina;
            v.Hud["coins"] = Fixed.FromInt(me.Coins);
            v.Hud["place"] = Fixed.FromInt(me.Place);
            for (int q = 0; q < all.Length; q++)
                if (q != player && screen.Sees(me.Pos, Fixed.Zero, all[q].Pos, Fixed.Zero))
                    v.Objects.Add(new SeenObject { Kind = "runner", Id = q, X = all[q].Pos });
            for (int f = me.NextFork; f < course.Forks.Count; f++)
                if (screen.Sees(me.Pos, Fixed.Zero, course.Forks[f], Fixed.Zero))
                    v.Objects.Add(new SeenObject { Kind = "fork", Id = f, X = course.Forks[f] });
            if (screen.Sees(me.Pos, Fixed.Zero, course.Length, Fixed.Zero))
                v.Objects.Add(new SeenObject { Kind = "finish", X = course.Length });
            return v;
        }

        public IBotStrategy Strategy(Archetype a, GameData data)
        {
            switch (a.Strategy)
            {
                case "idle": return new IdleStrategy();
                case "random": return new RandomStrategy(0);
                case "runner": return new RunnerStrategy(a.Params["risk"]?.AsFixed() ?? Fixed.Half);
                default: throw new KeyNotFoundException($"no strategy '{a.Strategy}' (archetype {a.Name})");
            }
        }

        public IReadOnlyList<PlayerInput> DecisionOptions(Simulation sim, int player)
        {
            var course = sim.Get<Course>(Course.ModuleName);
            var r = sim.Get<Runners>(Runners.ModuleName).All[player];
            if (r.NextFork >= course.Forks.Count || r.StunTicks > 0) return new PlayerInput[0];
            var gap = course.Forks[r.NextFork] - r.Pos;
            // one moment per fork: just before it
            if (gap <= Fixed.Zero || gap > sim.Data.Num("game.race.jog_speed") / 3) return new PlayerInput[0];
            return new[] { new PlayerInput(1, 1), new PlayerInput(1, -1) };
        }

        public double Score(Simulation sim, int player)
        {
            var all = sim.Get<Runners>(Runners.ModuleName).All;
            double lead = 0;
            foreach (var r in all) if (r.Pos.ToDouble() > lead) lead = r.Pos.ToDouble();
            double me = all[player].Pos.ToDouble() + all[player].Stamina.ToDouble() * 0.05;
            return lead <= 0 ? 0.5 : System.Math.Max(0, System.Math.Min(1, 0.5 + (me - lead) / 20 + 0.25));
        }

        public string Describe(Simulation sim)
        {
            var all = sim.Get<Runners>(Runners.ModuleName).All;
            var sb = new StringBuilder();
            for (int p = 0; p < all.Length; p++)
            {
                if (p > 0) sb.Append(" · ");
                sb.Append(p).Append(": ").Append(all[p].Pos.FloorToInt().ToString(CultureInfo.InvariantCulture))
                  .Append("м, силы ").Append(all[p].Stamina.FloorToInt().ToString(CultureInfo.InvariantCulture));
                if (all[p].StunTicks > 0) sb.Append(", лежит");
                if (all[p].Exhausted) sb.Append(", выдохся");
            }
            return sb.ToString();
        }

        public IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player)
        {
            var r = sim.Get<Runners>(Runners.ModuleName).All[player];
            return new Dictionary<string, double> { { "coins", r.Coins }, { "place", r.Place } };
        }
    }
}
