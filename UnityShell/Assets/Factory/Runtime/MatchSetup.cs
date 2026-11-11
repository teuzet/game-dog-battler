using System.Collections.Generic;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Sim;

namespace Factory.Shell
{
    /// <summary>What the pre-match menu picked, without any Unity calls so it can be tested: the arena,
    /// the bot opponent, and whether the human plays or watches a bot (participant 0). It only
    /// picks data and bots; the rules and the numbers stay in Game.Core.</summary>
    public sealed class MatchSetup
    {
        public string Arena = "";
        public string Opponent = "";
        public bool BotVsBot;
        public string Bot0 = "";
        public string Owner = "";

        public MatchSetup Copy() => (MatchSetup)MemberwiseClone();

        /// <summary>Arenas of Data/arenas: name and the title shown in the menu.</summary>
        public static List<KeyValuePair<string, string>> Arenas(GameData data)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var f in data.Get("arenas").Fields)
                if (f.Value["rules"] != null) list.Add(new KeyValuePair<string, string>(f.Key, f.Value["title"]?.AsString() ?? f.Key));
            return list;
        }

        /// <summary>Archetypes a bot may be picked from: <c>bots.session.tracked</c> without the breakers.</summary>
        public static List<string> Archetypes(GameData data)
        {
            var list = new List<string>();
            foreach (var n in data.Get("bots.session.tracked").Items)
                if (!n.AsString().StartsWith("breaker-")) list.Add(n.AsString());
            return list;
        }

        /// <summary>The menu's start: the data's own arena, its first opponent, smart for the watched bot.</summary>
        public static MatchSetup Defaults(GameData data, string arena)
        {
            var onArena = data.WithArena(arena);
            var s = new MatchSetup { Arena = arena, Opponent = Archetype.Opponents(onArena)[0], Owner = data.Str("game.human_owner") };
            var all = Archetypes(data);
            s.Bot0 = all.Contains("smart") ? "smart" : all[0];
            return s;
        }

        /// <summary>The data of the match: the arena first (its players and length), then the variant on top so its numbers win.</summary>
        public GameData Data(GameData baseData, System.Func<GameData, GameData> variant) =>
            variant(baseData.WithArena(Arena)).WithHumanOwner(Owner);

        /// <summary>Opponents of the arena with the first one replaced by the picked bot.</summary>
        public List<string> OpponentNames(GameData data)
        {
            var names = new List<string>(Archetype.Opponents(data));
            names[0] = Opponent;
            return names;
        }

        /// <summary>The bots of the session: everybody but the human, and participant 0 too when a bot plays instead.</summary>
        public List<BotDriver> Drivers(IGame game, GameData data, int players, int seed)
        {
            var names = OpponentNames(data);
            var bots = new List<BotDriver>();
            for (int p = 0; p < players; p++)
            {
                string name = p == 0 ? Bot0 : names[(p - 1) % names.Count];
                if (p == 0 && !BotVsBot) continue;
                var a = Archetype.Find(data, name);
                bots.Add(new BotDriver(p, a, game.Strategy(a, data), (ulong)seed));
            }
            return bots;
        }
    }
}
