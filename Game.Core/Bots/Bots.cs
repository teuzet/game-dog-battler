using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots
{
    /// <summary>A strategy that fits in a couple of sentences (written in the
    /// archetype's "about"). Gets only the view; keeps no hidden access.</summary>
    public interface IBotStrategy
    {
        PlayerInput Decide(BotView view, ref DetRandom rng);
        IBotStrategy Clone();
    }

    /// <summary>A bot archetype from Game.Core/Data/bots/archetypes.json.</summary>
    public sealed class Archetype
    {
        public string Name = "";
        public string Strategy = "";
        public string About = "";
        public int ReactionMs = 250; // it sees the screen this late
        public int HoldMs = 300; // it keeps a decision at least this long (inertia)
        public Fixed AimError = Fixed.Zero; // metres: the spread of the cursor point of a swing
        public JsonValue Params = JsonValue.Object();

        public static IReadOnlyList<Archetype> LoadAll(GameData data)
        {
            var list = new List<Archetype>();
            foreach (var a in data.Get("bots.archetypes").Items)
            {
                list.Add(new Archetype
                {
                    Name = a["name"]!.AsString(),
                    Strategy = a["strategy"]?.AsString() ?? a["name"]!.AsString(),
                    About = a["about"]?.AsString() ?? "",
                    ReactionMs = a["reaction_ms"]?.AsInt() ?? 250,
                    HoldMs = a["hold_ms"]?.AsInt() ?? 300,
                    AimError = a["aim_error_m"]?.AsFixed() ?? Fixed.Zero,
                    Params = a["params"] ?? JsonValue.Object(),
                });
            }
            return list;
        }

        /// <summary>Who drives players 1..: the active rule set names them in its own file when it needs to
        /// (Data/rules/sandbox.json → "opponents": mannequins), otherwise Data/bots/session.json → "opponents".</summary>
        public static IReadOnlyList<string> Opponents(GameData data)
        {
            var rules = data.Get("arenas." + data.Str("game.arena"))["rules"]!.AsString();
            var key = new System.Text.StringBuilder();
            foreach (var c in rules)
            {
                if (char.IsUpper(c) && key.Length > 0) key.Append('_');
                key.Append(char.ToLowerInvariant(c));
            }
            var path = "rules." + key + ".opponents";
            var names = new List<string>();
            foreach (var n in data.Get(data.Has(path) ? path : "bots.session.opponents").Items) names.Add(n.AsString());
            return names;
        }

        public static Archetype Find(GameData data, string name)
        {
            foreach (var a in LoadAll(data)) if (a.Name == name) return a;
            throw new KeyNotFoundException($"no bot archetype '{name}' in Game.Core/Data/bots/archetypes.json");
        }
    }

    /// <summary>Does nothing. The gate checks that doing nothing loses.</summary>
    public sealed class IdleStrategy : IBotStrategy
    {
        public PlayerInput Decide(BotView view, ref DetRandom rng) => PlayerInput.None;
        public IBotStrategy Clone() => this;
    }

    /// <summary>Random stick and buttons. The gate checks that thinking beats it.</summary>
    public sealed class RandomStrategy : IBotStrategy
    {
        private readonly int _buttons;

        public RandomStrategy(int buttons) { _buttons = buttons; }

        public PlayerInput Decide(BotView view, ref DetRandom rng) =>
            new PlayerInput(rng.Range(-1, 2), rng.Range(-1, 2), _buttons == 0 ? 0u : (uint)rng.NextInt(1 << _buttons));

        public IBotStrategy Clone() => this;
    }

    /// <summary>Runs a strategy like a person would: it sees the screen with a
    /// delay (reaction) and keeps a decision for a while (inertia), instead
    /// of producing a fresh input every tick.</summary>
    public sealed class BotDriver
    {
        public readonly int Player;
        public readonly Archetype Archetype;
        private readonly IBotStrategy _strategy;
        private readonly int _reactionTicks, _holdTicks;
        private readonly Queue<BotView> _seen = new Queue<BotView>();
        private DetRandom _rng;
        private PlayerInput _current;
        private int _heldFor;

        public BotDriver(int player, Archetype archetype, IBotStrategy strategy, ulong seed)
        {
            Player = player;
            Archetype = archetype;
            _strategy = strategy;
            _reactionTicks = System.Math.Max(0, archetype.ReactionMs * Simulation.TicksPerSecond / 1000);
            _holdTicks = System.Math.Max(1, archetype.HoldMs * Simulation.TicksPerSecond / 1000);
            _rng = new DetRandom(seed * 31UL + (ulong)player + 7UL);
            _heldFor = int.MaxValue / 2;
        }

        private BotDriver(BotDriver src)
        {
            Player = src.Player;
            Archetype = src.Archetype;
            _strategy = src._strategy.Clone();
            _reactionTicks = src._reactionTicks;
            _holdTicks = src._holdTicks;
            _seen = new Queue<BotView>(src._seen);
            _rng = src._rng;
            _current = src._current;
            _heldFor = src._heldFor;
        }

        public BotDriver Clone() => new BotDriver(this);

        /// <summary>Feeds this tick's view; returns the input for this tick.</summary>
        public PlayerInput Next(BotView now)
        {
            _seen.Enqueue(now);
            while (_seen.Count > _reactionTicks + 1) _seen.Dequeue();
            _heldFor++;
            if (_seen.Count <= _reactionTicks) return _current; // has not seen anything yet
            if (_heldFor < _holdTicks) return _current;
            var next = _strategy.Decide(_seen.Peek(), ref _rng);
            if (!next.Equals(_current)) _heldFor = 0;
            _current = next;
            return _current;
        }
    }
}
