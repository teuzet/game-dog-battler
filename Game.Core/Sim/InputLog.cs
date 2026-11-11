using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Game.Core.Sim
{
    /// <summary>Seed, variant and every tick's inputs: enough to replay a
    /// session exactly. The demo build writes Nick's sessions this way, the
    /// playtest replays them like a bot's. Text, one line per change.</summary>
    public sealed class InputLog
    {
        public int Seed;
        public string Variant = "";
        public int Players;
        public readonly List<TickInputs> Ticks = new List<TickInputs>();

        /// <summary>The owner type the human picked before the match (game.human_owner), "" = the data's own.</summary>
        public string Owner = "";

        /// <summary>The arena played (game.arena); "" in logs written before arenas were recorded: they read as the data's own.</summary>
        public string Arena = "";

        public InputLog(int seed, int players, string variant = "", string owner = "", string arena = "")
        {
            Arena = arena;
            Seed = seed;
            Players = players;
            Variant = variant;
            Owner = owner;
        }

        public void Record(TickInputs inputs) => Ticks.Add(inputs.Copy());

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("# factory input log v1\n");
            sb.Append("seed ").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("players ").Append(Players.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("variant ").Append(Variant).Append('\n');
            if (Owner.Length > 0) sb.Append("owner ").Append(Owner).Append('\n');
            if (Arena.Length > 0) sb.Append("arena ").Append(Arena).Append('\n');
            sb.Append("ticks ").Append(Ticks.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            var last = new PlayerInput[Players];
            for (int t = 0; t < Ticks.Count; t++)
                for (int p = 0; p < Players; p++)
                {
                    var v = Ticks[t][p];
                    if (v.Equals(last[p])) continue;
                    last[p] = v;
                    sb.Append(t.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(p.ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append(v.AimX.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(v.AimY.ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append(v.Buttons.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
            return sb.ToString();
        }

        public static InputLog Parse(string text)
        {
            int seed = 0, players = 0, ticks = 0;
            string variant = "", owner = "", arena = "";
            var changes = new List<int[]>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split(' ');
                switch (parts[0])
                {
                    case "seed": seed = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    case "players": players = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    case "variant": variant = parts.Length > 1 ? parts[1] : ""; break;
                    case "owner": owner = parts[1]; break;
                    case "arena": arena = parts[1]; break;
                    case "ticks": ticks = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    default:
                        if (parts.Length != 5) throw new FormatException($"bad input log line: {line}");
                        var c = new int[5];
                        for (int i = 0; i < 5; i++) c[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
                        changes.Add(c);
                        break;
                }
            }
            var log = new InputLog(seed, players, variant, owner, arena);
            var cur = new PlayerInput[players];
            int k = 0;
            for (int t = 0; t < ticks; t++)
            {
                while (k < changes.Count && changes[k][0] == t)
                {
                    var c = changes[k++];
                    cur[c[1]] = new PlayerInput(c[2], c[3], (uint)c[4]);
                }
                var ti = new TickInputs(players);
                for (int p = 0; p < players; p++) ti[p] = cur[p];
                log.Ticks.Add(ti);
            }
            return log;
        }

        public static InputLog Load(string path) => Parse(File.ReadAllText(path));
    }
}
