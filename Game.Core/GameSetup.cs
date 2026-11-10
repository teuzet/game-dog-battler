using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Bots;
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

namespace Game.Core
{
    /// <summary>The assembly point: which game this repository is and its
    /// modules in tick order. The order here is the one place every feature
    /// may touch; everything else lives in its module.</summary>
    public static class GameSetup
    {
        public static readonly IGame Game = new DogBattlerGame();
    }

    public sealed class DogBattlerGame : IGame
    {
        public string Name => "dog-battler";
        public int Players => 4;

        /// <summary>The session setup lives in data: the human's owner pick is "game.human_owner" (the shell
        /// or a variant sets it; the variant name is in the input log), bots' picks are "bots.session.owner_picks".</summary>
        public Simulation Create(int seed, GameData data)
        {
            int players = data.Has("game.players") ? data.Int("game.players") : Players;
            var modules = new List<IModule>
            {
                new ArenaModule(data),
                new ControlModule(data, players),
                new OwnersModule(data, players),
                new DogsModule(data, players),
                new PropsModule(),
                new LeashesModule(data, players),
                new BodiesModule(data),
                new HitsModule(data),
                new RulesModule(data),
                new MatchModule(data, players),
            };
            return new Simulation(seed, players, data, modules);
        }

        public BotView Sense(Simulation sim, int player)
        {
            var arena = sim.Get<ArenaModule>(ArenaModule.ModuleName);
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
            var match = sim.Get<MatchModule>(MatchModule.ModuleName);
            var rules = sim.Get<RulesModule>(RulesModule.ModuleName);
            var me = dogs.Position(sim, player);
            var v = new BotView { Tick = sim.Tick, Self = player, X = me.X, Y = me.Y };
            v.Hud["score"] = Fixed.FromInt(match.Scores[player]);
            v.Hud["time_left"] = match.TimeLeft;
            v.Hud["arena_half_width"] = arena.Width / 2;
            v.Hud["arena_half_height"] = arena.Height / 2;
            // the camera shows the whole arena
            for (int q = 0; q < sim.Players; q++)
            {
                var dog = dogs.Position(sim, q);
                var owner = owners.Position(sim, q);
                if (q != player) v.Objects.Add(new SeenObject { Kind = "dog", Id = q, X = dog.X, Y = dog.Y });
                v.Objects.Add(new SeenObject { Kind = "owner", Id = q, X = owner.X, Y = owner.Y });
            }
            if (rules.Active.Zone is ZoneInfo z)
                v.Objects.Add(new SeenObject { Kind = "zone", X = z.Center.X, Y = z.Center.Y, Value = z.Radius });
            return v;
        }

        public IBotStrategy Strategy(Archetype a, GameData data)
        {
            switch (a.Strategy)
            {
                case "idle": return new IdleStrategy();
                case "random": return new WanderStrategy();
                case "smart": return new ZoneRunnerStrategy();
                default: throw new KeyNotFoundException($"no strategy '{a.Strategy}' (archetype {a.Name})");
            }
        }

        /// <summary>No decision moments yet: they come with the rules and the swing (F-005, F-006).</summary>
        public IReadOnlyList<PlayerInput> DecisionOptions(Simulation sim, int player) => new PlayerInput[0];

        public double Score(Simulation sim, int player)
        {
            var scores = sim.Get<MatchModule>(MatchModule.ModuleName).Scores;
            int total = 0;
            foreach (var s in scores) total += s;
            return total == 0 ? 0.5 : scores[player] / (double)total;
        }

        public string Describe(Simulation sim)
        {
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var scores = sim.Get<MatchModule>(MatchModule.ModuleName).Scores;
            var sb = new StringBuilder();
            for (int p = 0; p < sim.Players; p++)
            {
                if (p > 0) sb.Append(" · ");
                var pos = dogs.Position(sim, p);
                sb.Append(p).Append(": (").Append(pos.X.ToString()).Append(", ").Append(pos.Y.ToString()).Append("), очки ")
                  .Append(scores[p].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player) =>
            new Dictionary<string, double> { { "score", sim.Get<MatchModule>(MatchModule.ModuleName).Scores[player] } };
    }
}
