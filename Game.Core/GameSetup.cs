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
                new RulesModule(data, players),
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
            if (rules.Active.NextZone is ZoneInfo n)
                v.Objects.Add(new SeenObject { Kind = "next_zone", X = n.Center.X, Y = n.Center.Y, Value = n.Radius });
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

        private static PlayerInput CursorAt(Vec2 p) => new PlayerInput((p.X * 100).FloorToInt(), (p.Y * 100).FloorToInt());

        /// <summary>A moment of decision: the next circle was just announced (run to it / fight over this one),
        /// or a rival is within the leash range (swing at him / retreat from him).</summary>
        public IReadOnlyList<PlayerInput> DecisionOptions(Simulation sim, int player)
        {
            var rules = sim.Get<RulesModule>(RulesModule.ModuleName).Active;
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var me = dogs.Position(sim, player);
            int rival = -1;
            var reach = sim.Get<LeashesModule>(LeashesModule.ModuleName).LengthOf(player);
            var best = reach;
            for (int q = 0; q < sim.Players; q++)
            {
                if (q == player) continue;
                var d = me.DistanceTo(dogs.Position(sim, q));
                if (d <= best) { best = d; rival = q; }
            }
            if (rules.ZoneJustAnnounced(sim) && rules.NextZone is ZoneInfo next)
            {
                if (rival < 0) return new[] { CursorAt(next.Center), CursorAt(me) };
                return new[] { CursorAt(next.Center), CursorAt(dogs.Position(sim, rival)) };
            }
            if (rival < 0) return new PlayerInput[0];
            var away = me - dogs.Position(sim, rival);
            var retreat = away.LengthSq == Fixed.Zero ? me : me + away.Normalized() * reach; // back off by one leash
            return new[] { CursorAt(dogs.Position(sim, rival)), CursorAt(retreat) };
        }

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

        /// <summary>Per player: points and the rules' numbers (knockout points, fight share, hits a minute, the
        /// longest equal in-zone stretch: H4).</summary>
        public IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player)
        {
            var m = new Dictionary<string, double> { { "points", sim.Get<MatchModule>(MatchModule.ModuleName).Scores[player] } };
            foreach (var kv in sim.Get<RulesModule>(RulesModule.ModuleName).Active.Metrics(sim, player)) m[kv.Key] = kv.Value;
            return m;
        }
    }
}
